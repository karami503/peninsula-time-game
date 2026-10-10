using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using UnityEngine;

namespace PeninsulaTime
{
    // Whole-city Changwon data built by AssetSources/Changwon/build_world.py: terrain heights and land classes on a 16 m grid,
    // Overture building footprints in 512 m chunks, the road/rail graph with surveyed bridge and tunnel heights, places,
    // lakes and the Changwon BIS bus routes. x = east, z = north, metres from OriginLon/OriginLat.
    public static class ChangwonData
    {
        public const float OriginLon=128.62f,OriginLat=35.20f;
        public static readonly float SX=111320f*Mathf.Cos(OriginLat*Mathf.Deg2Rad),SZ=111320f;
        public const byte Sea=0,Water=1,Forest=2,Grass=3,Farm=4,Orchard=5,Residential=6,Commercial=7,Industrial=8,Sand=9,Rock=10,Urban=11,Wetland=12,Golf=13,Cemetery=14,Campus=15,Pitch=16;
        public const byte Motorway=0,Trunk=1,Primary=2,Secondary=3,Tertiary=4,Local=5,Service=6,Track=7,Footway=8,Rail=9,LightRail=10;
        public const byte Ground=0,Bridge=1,Tunnel=2;

        public class Road {public int a,b,index;public byte cls,flags,lanes;public float width,length;public string name;public Vector3[] pts;public byte[] kind;public float[] along;
            public bool OneWay{get{return (flags&1)!=0;}} public bool Link{get{return (flags&2)!=0;}} public bool Drivable{get{return cls<=Track;}} public bool IsRail{get{return cls>=Rail;}}
            public Vector3 At(float distance,out Vector3 forward){
                if(distance<=0){forward=(pts[1]-pts[0]).normalized;return pts[0];}
                for(int i=1;i<pts.Length;i++)if(along[i]>=distance||i==pts.Length-1){float t=Mathf.InverseLerp(along[i-1],along[i],distance);forward=(pts[i]-pts[i-1]).normalized;return Vector3.Lerp(pts[i-1],pts[i],t);}
                forward=Vector3.forward;return pts[pts.Length-1];
            }
        }
        public class Building {public byte kind,floors;public float height,baseY;public string name;public Vector2[] ring;public Vector2 center;}
        public class Place {public Vector2 pos;public string kind,name;}
        public class Lake {public string name,kind;public float surface;public Vector2[] ring;}
        public class BusRoute {public string id,number,title;public Color color;public Vector3[] shape;public float[] along;public int[] stopIndex;public string[] stopNames;public float length;}

        public static bool Loaded,Loading;public static string Error,Source;
        public static int NX,NZ;public static float X0,Z0,Cell;static short[] heights;static byte[] land;
        public static int CX,CZ;public static float ChunkX0,ChunkZ0,ChunkSize;static int[] buildingOffsets;static byte[] buildingBlob;
        public static Vector2[] Nodes;public static Road[] Roads;public static int[][] RoadsInChunk;public static List<int>[] NodeRoads;
        public static readonly List<Place> Places=new List<Place>();public static readonly List<Place> Infrastructure=new List<Place>();
        public static readonly List<Lake> Lakes=new List<Lake>();public static readonly List<BusRoute> Buses=new List<BusRoute>();
        static readonly Dictionary<int,List<Building>> buildingCache=new Dictionary<int,List<Building>>();

        public static Vector2 ToXZ(double lon,double lat){return new Vector2((float)((lon-OriginLon)*SX),(float)((lat-OriginLat)*SZ));}
        public static Vector2 ToLonLat(float x,float z){return new Vector2(OriginLon+x/SX,OriginLat+z/SZ);}
        public static float MinX{get{return X0;}} public static float MinZ{get{return Z0;}}
        public static float MaxX{get{return X0+(NX-1)*Cell;}} public static float MaxZ{get{return Z0+(NZ-1)*Cell;}}
        public static bool Inside(float x,float z){return Loaded&&x>X0&&z>Z0&&x<MaxX&&z<MaxZ;}

        // Terrain height in metres (bilinear on the 16 m grid). Roads have already been cut into this surface.
        public static float Height(float x,float z){
            if(heights==null)return 0;
            float fx=Mathf.Clamp((x-X0)/Cell,0,NX-1.001f),fz=Mathf.Clamp((z-Z0)/Cell,0,NZ-1.001f);
            int i=(int)fx,j=(int)fz;float u=fx-i,v=fz-j;int k=j*NX+i;
            return ((heights[k]*(1-u)+heights[k+1]*u)*(1-v)+(heights[k+NX]*(1-u)+heights[k+NX+1]*u)*v)*.1f;
        }
        public static float GridHeight(int i,int j){i=Mathf.Clamp(i,0,NX-1);j=Mathf.Clamp(j,0,NZ-1);return heights[j*NX+i]*.1f;}
        public static byte GridLand(int i,int j){i=Mathf.Clamp(i,0,NX-1);j=Mathf.Clamp(j,0,NZ-1);return land[j*NX+i];}
        public static byte LandAt(float x,float z){if(land==null)return Sea;return GridLand(Mathf.RoundToInt((x-X0)/Cell),Mathf.RoundToInt((z-Z0)/Cell));}
        public static Vector2Int ChunkOf(float x,float z){return new Vector2Int(Mathf.Clamp(Mathf.FloorToInt((x-ChunkX0)/ChunkSize),0,CX-1),Mathf.Clamp(Mathf.FloorToInt((z-ChunkZ0)/ChunkSize),0,CZ-1));}
        public static Vector3 ChunkMin(int cx,int cz){return new Vector3(ChunkX0+cx*ChunkSize,0,ChunkZ0+cz*ChunkSize);}

        // Footprints of one chunk, decoded on first use. Safe to call from worker threads.
        public static List<Building> BuildingsIn(int cx,int cz){
            if(buildingBlob==null||cx<0||cz<0||cx>=CX||cz>=CZ)return new List<Building>();
            int key=cz*CX+cx;
            lock(buildingCache){List<Building> hit;if(buildingCache.TryGetValue(key,out hit))return hit;}
            var list=new List<Building>();int p=buildingOffsets[key];int count=BitConverter.ToInt32(buildingBlob,p);p+=4;
            float ox=ChunkX0+(cx+.5f)*ChunkSize,oz=ChunkZ0+(cz+.5f)*ChunkSize;
            for(int b=0;b<count;b++){
                var bd=new Building{kind=buildingBlob[p],floors=buildingBlob[p+1],height=BitConverter.ToUInt16(buildingBlob,p+2)*.1f,baseY=BitConverter.ToInt16(buildingBlob,p+4)*.1f};
                int nameLength=buildingBlob[p+6],n=buildingBlob[p+7];p+=8;
                bd.name=nameLength>0?Encoding.UTF8.GetString(buildingBlob,p,nameLength):"";p+=nameLength;
                bd.ring=new Vector2[n];Vector2 sum=Vector2.zero;
                for(int i=0;i<n;i++){bd.ring[i]=new Vector2(ox+BitConverter.ToInt16(buildingBlob,p)*.05f,oz+BitConverter.ToInt16(buildingBlob,p+2)*.05f);sum+=bd.ring[i];p+=4;}
                bd.center=sum/Mathf.Max(1,n);list.Add(bd);
            }
            lock(buildingCache)buildingCache[key]=list;
            return list;
        }

        // Nearest point on a drivable road to p (searching the chunk and its neighbours).
        public static bool NearestRoad(Vector3 p,float maxDistance,out Road road,out float distanceAlong,out Vector3 point,bool drivableOnly=true){
            road=null;distanceAlong=0;point=p;float best=maxDistance*maxDistance;
            if(RoadsInChunk==null)return false;var c=ChunkOf(p.x,p.z);
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++){
                int cx=c.x+dx,cz=c.y+dz;if(cx<0||cz<0||cx>=CX||cz>=CZ)continue;
                foreach(int ri in RoadsInChunk[cz*CX+cx]){
                    var r=Roads[ri];if(drivableOnly&&!r.Drivable||!drivableOnly&&r.IsRail)continue;
                    for(int i=1;i<r.pts.Length;i++){
                        Vector3 a=r.pts[i-1],b=r.pts[i];Vector3 ab=b-a;ab.y=0;float l2=ab.sqrMagnitude;if(l2<1e-4f)continue;
                        float t=Mathf.Clamp01(((p.x-a.x)*ab.x+(p.z-a.z)*ab.z)/l2);Vector3 q=Vector3.Lerp(a,b,t);
                        float d=(q.x-p.x)*(q.x-p.x)+(q.z-p.z)*(q.z-p.z);
                        if(d<best&&Mathf.Abs(q.y-p.y)<12f){best=d;road=r;point=q;distanceAlong=Mathf.Lerp(r.along[i-1],r.along[i],t);}
                    }
                }
            }
            return road!=null;
        }

        public static string StatusText{get{return Loaded?"창원 데이터 "+Source:Loading?"창원 데이터 불러오는 중…":Error??"";}}

        // Reads the packed files (Resources in a Unity build; StreamingAssets/Changwon as a fallback for patched test builds)
        // on the main thread and decodes them on a worker thread.
        public static IEnumerator Load(){
            if(Loaded||Loading)yield break;
            Loading=true;Error=null;
            var files=new Dictionary<string,byte[]>();
            foreach(var name in new[]{"cw_terrain.bytes","cw_buildings.bytes","cw_roads.bytes","cw_places.txt","cw_lakes.txt","cw_buses.bytes","cw_areas.txt","cw_landmarks.txt"}){
                var bytes=Read(name);
                if(bytes==null){Error="창원 데이터 파일이 없습니다: "+name;Loading=false;yield break;}
                files[name]=bytes;yield return null;
            }
            Exception failure=null;bool done=false;
            var worker=new Thread(()=>{try{Decode(files);}catch(Exception e){failure=e;}done=true;});
            worker.IsBackground=true;worker.Start();
            while(!done)yield return null;
            Loading=false;
            if(failure!=null){Error="창원 데이터 오류: "+failure.Message;Debug.LogError(failure);yield break;}
            Loaded=true;
            Debug.Log("Changwon data: "+Roads.Length+" roads, "+Places.Count+" places, "+Buses.Count+" bus routes, grid "+NX+"x"+NZ+" ("+Source+")");
        }
        static byte[] Read(string file){
            string resource="Changwon/"+Path.GetFileNameWithoutExtension(file);
            var asset=Resources.Load<TextAsset>(resource);
            if(asset!=null){Source="Resources";var b=asset.bytes;Resources.UnloadAsset(asset);return b;}
            string path=Path.Combine(Path.Combine(Application.streamingAssetsPath,"Changwon"),file);
            if(!path.Contains("://")&&File.Exists(path)){Source="StreamingAssets";return File.ReadAllBytes(path);}
            return null;
        }
        static byte[] Gunzip(byte[] data){
            if(data.Length<2||data[0]!=0x1f||data[1]!=0x8b)return data;
            using(var input=new GZipStream(new MemoryStream(data),CompressionMode.Decompress))using(var output=new MemoryStream()){input.CopyTo(output);return output.ToArray();}
        }
        static void Expect(BinaryReader r,string magic){var m=Encoding.ASCII.GetString(r.ReadBytes(4));if(m!=magic)throw new InvalidDataException("bad header "+m+" (expected "+magic+")");}
        static float F(string s){return float.Parse(s,CultureInfo.InvariantCulture);}

        static void Decode(Dictionary<string,byte[]> files){
            using(var r=new BinaryReader(new MemoryStream(Gunzip(files["cw_terrain.bytes"])))){
                Expect(r,"CWT1");NX=r.ReadInt32();NZ=r.ReadInt32();X0=r.ReadSingle();Z0=r.ReadSingle();Cell=r.ReadSingle();
                heights=new short[NX*NZ];
                for(int j=0;j<NZ;j++){int acc=0;for(int i=0;i<NX;i++){acc+=r.ReadInt16();heights[j*NX+i]=(short)acc;}}
                land=r.ReadBytes(NX*NZ);
            }
            using(var r=new BinaryReader(new MemoryStream(Gunzip(files["cw_buildings.bytes"])))){
                Expect(r,"CWB1");CX=r.ReadInt32();CZ=r.ReadInt32();ChunkX0=r.ReadSingle();ChunkZ0=r.ReadSingle();ChunkSize=r.ReadSingle();
                buildingOffsets=new int[CX*CZ+1];for(int i=0;i<buildingOffsets.Length;i++)buildingOffsets[i]=r.ReadInt32();
                buildingBlob=r.ReadBytes(buildingOffsets[CX*CZ]);
            }
            using(var r=new BinaryReader(new MemoryStream(Gunzip(files["cw_roads.bytes"])))){
                Expect(r,"CWR1");int n=r.ReadInt32();Nodes=new Vector2[n];for(int i=0;i<n;i++)Nodes[i]=new Vector2(r.ReadSingle(),r.ReadSingle());
                int e=r.ReadInt32();Roads=new Road[e];NodeRoads=new List<int>[n];
                for(int i=0;i<e;i++){
                    var road=new Road{index=i,a=r.ReadInt32(),b=r.ReadInt32(),cls=r.ReadByte(),flags=r.ReadByte()};road.width=r.ReadByte()/4f;road.lanes=r.ReadByte();
                    int nameLength=r.ReadByte();road.name=nameLength>0?Encoding.UTF8.GetString(r.ReadBytes(nameLength)):"";
                    int pc=r.ReadUInt16();road.pts=new Vector3[pc];road.kind=new byte[pc];road.along=new float[pc];
                    for(int k=0;k<pc;k++){road.pts[k]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());road.kind[k]=r.ReadByte();if(k>0)road.along[k]=road.along[k-1]+Vector2.Distance(new Vector2(road.pts[k].x,road.pts[k].z),new Vector2(road.pts[k-1].x,road.pts[k-1].z));}
                    road.length=road.along[pc-1];Roads[i]=road;
                    (NodeRoads[road.a]??(NodeRoads[road.a]=new List<int>())).Add(i);(NodeRoads[road.b]??(NodeRoads[road.b]=new List<int>())).Add(i);
                }
                int cx=r.ReadInt32(),cz=r.ReadInt32();r.ReadSingle();r.ReadSingle();r.ReadSingle();
                RoadsInChunk=new int[cx*cz][];
                for(int c=0;c<cx*cz;c++){int count=r.ReadInt32();var ids=new int[count];for(int k=0;k<count;k++)ids[k]=r.ReadInt32();RoadsInChunk[c]=ids;}
            }
            Places.Clear();Infrastructure.Clear();
            foreach(var line in Encoding.UTF8.GetString(files["cw_places.txt"]).Split('\n')){
                if(line.Length==0||line[0]=='#')continue;var f=line.Split('|');if(f.Length<4)continue;
                var place=new Place{pos=new Vector2(F(f[0]),F(f[1])),kind=f[2],name=f[3].Trim()};
                if(place.kind.StartsWith("i:")){place.kind=place.kind.Substring(2);Infrastructure.Add(place);}else Places.Add(place);
            }
            Lakes.Clear();
            foreach(var line in Encoding.UTF8.GetString(files["cw_lakes.txt"]).Split('\n')){
                if(line.Length==0||line[0]=='#')continue;var f=line.Split('|');if(f.Length<4)continue;
                var pairs=f[3].Split(';');var ring=new Vector2[pairs.Length];
                for(int i=0;i<pairs.Length;i++){var xy=pairs[i].Split(',');ring[i]=new Vector2(F(xy[0]),F(xy[1]));}
                Lakes.Add(new Lake{name=f[0],kind=f[1],surface=F(f[2]),ring=ring});
            }
            Buses.Clear();
            foreach(var line in Encoding.UTF8.GetString(Gunzip(files["cw_buses.bytes"])).Split('\n')){
                if(line.Length==0||line[0]=='#')continue;var f=line.Split('|');if(f.Length<6)continue;
                var route=new BusRoute{id=f[0],number=f[1],title=f[3]};Color c;route.color=ColorUtility.TryParseHtmlString(f[2],out c)?c:new Color(.15f,.5f,.8f);
                var pts=f[4].Split(';');route.shape=new Vector3[pts.Length];route.along=new float[pts.Length];
                for(int i=0;i<pts.Length;i++){var v=pts[i].Split(',');route.shape[i]=new Vector3(F(v[0]),F(v[1]),F(v[2]));if(i>0)route.along[i]=route.along[i-1]+Vector3.Distance(route.shape[i],route.shape[i-1]);}
                route.length=route.along[pts.Length-1];
                var stops=f[5].Split(';');var idx=new List<int>();var names=new List<string>();
                foreach(var s in stops){int colon=s.IndexOf(':');if(colon<1)continue;int k;if(!int.TryParse(s.Substring(0,colon),out k))continue;idx.Add(Mathf.Clamp(k,0,pts.Length-1));names.Add(s.Substring(colon+1));}
                route.stopIndex=idx.ToArray();route.stopNames=names.ToArray();Buses.Add(route);
            }
            ChangwonAreas.Parse(Encoding.UTF8.GetString(files["cw_areas.txt"]));
            ChangwonLandmarks.Parse(Encoding.UTF8.GetString(files["cw_landmarks.txt"]));
        }
    }
}
