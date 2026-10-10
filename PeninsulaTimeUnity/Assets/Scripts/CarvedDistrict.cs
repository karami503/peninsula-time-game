using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace PeninsulaTime
{
    // A whole 구 as one lump carved to the shape of the land. Its ground follows the surveyed terrain (Geo/JinhaeTerrain,
    // 30 m grid, cut in 0.25 m steps); the sea and rivers are cut down into it (Geo/JinhaeWater, Overture water); the
    // roads (OSM, CityContext) are cut into it; the buildings (Geo/JinhaeBuildings: Overture footprints from OSM and the
    // Qianshi East Asian Buildings set; heights from mapped floors, else estimated from footprint size) stand out of it;
    // rails lie on its bed; cherry trees, pines and people are carved lumps of their own. Heights are metres above the
    // ground at the origin (the station), so the sea lies at -datum. It is made and dropped chunk by chunk around the
    // walker, like Minecraft: fine (0.5 m cells, with colliders) near, coarse (2 m) further out, and one low mesh of the
    // whole land to the horizon. Every chunk samples a cell beyond its edge from the same data, so the lump has no seams.
    public sealed class CarvedDistrict : MonoBehaviour
    {
        public const float Chunk=64f,Ground=.15f,Step=.25f,Near=160f;
        // A rail line keeps its own level: the ground averaged over GradeWindow either side (held to the ground on the
        // station pads). Where that runs more than FillLimit over the ground, or over water, it crosses on a bridge;
        // where it runs more than CutDepth under the ground, in a covered cutting (a tunnel).
        const float GradeStep=4f,GradeWindow=150f,FillLimit=2.5f,CutDepth=4f,Cutting=3f;
        const int MaxPeople=70;
        public const byte Earth=0,Road=1,House=2,Bed=3,Plaza=4,Line=5,Water=6,Grass=7;

        const byte GroundRail=0,BridgeRail=1,TunnelRail=2;
        sealed class Shape{public byte kind,structure;public Vector2[] points;public float[] along;public float size,baseY;public int seed;public bool trees;public Rect box;public float[] grade;}
        sealed class Piece{public GameObject go;public int n;public float cell;public float[] height;public byte[] kind,tone;public bool fine;}
        sealed class Person{public Transform body;public Vector3 target;public float speed,wait;}

        public Transform focus;public Material material;
        public Func<Vector2,int> site; // 1: station square (paved, no buildings), 2: rail yard (no buildings)
        // Levelled pads: where weight(p) > 0 the ground is drawn toward the height (a station's ground).
        public readonly List<KeyValuePair<Func<Vector2,float>,float>> flats=new List<KeyValuePair<Func<Vector2,float>,float>>();
        public Mesh tree,pine;public Mesh[] people;
        public float datum; // the surveyed height of the origin, metres above the sea
        double lon0,lat0,sx;
        short[] dem;int demCols,demRows;double demLon0,demLat0,demLon1,demLat1;
        byte[] wet;int wetCols,wetRows;double wetLon0,wetLat0,wetLon1,wetLat1;
        readonly Dictionary<Vector2Int,List<Shape>> buckets=new Dictionary<Vector2Int,List<Shape>>();
        readonly Dictionary<Vector2Int,Piece> pieces=new Dictionary<Vector2Int,Piece>();
        readonly List<Person> walkers=new List<Person>();
        readonly System.Random random=new System.Random(7);float nextScan;
        Vector2Int settledAt=new Vector2Int(int.MinValue,0); // Stream has nothing left to make while the walker stays in this chunk
        public int ChunkCount{get{return pieces.Count;}}
        public int FineCount{get{int n=0;foreach(var p in pieces.Values)if(p.fine)n++;return n;}}
        public int BuildingCount{get;private set;}
        public float BridgeMetres{get;private set;}
        public float TunnelMetres{get;private set;}
        public int MappedCrossingCount{get{return mappedCrossings.Count;}}
        public int PeopleCount{get{walkers.RemoveAll(p=>p.body==null);return walkers.Count;}}
        public IEnumerable<GameObject> Chunks{get{foreach(var p in pieces.Values)yield return p.go;}}
        public static float Radius{get{return Mathf.Max(PerformanceRuntime.RenderDistance+64f,Near+128f);}}

        public Vector3 Local(double lon,double lat){return new Vector3((float)((lon-lon0)*sx),0,(float)((lat-lat0)*111320));}
        // The mapped railway lines (name, points in local metres), read by Origin.
        public readonly List<KeyValuePair<string,Vector2[]>> MappedRails=new List<KeyValuePair<string,Vector2[]>>();
        readonly List<byte> mappedRailStructures=new List<byte>();
        readonly List<Vector2> mappedCrossings=new List<Vector2>();

        // Local metres are measured from (lon, lat), x east and z north; loads the terrain, the water and the rails.
        public void Origin(double lon,double lat)
        {
            lon0=lon;lat0=lat;sx=111320*Math.Cos(lat*Math.PI/180);
            var terrain=Resources.Load<TextAsset>("Geo/JinhaeTerrain");
            if(terrain==null)Debug.LogError("CarvedDistrict: Geo/JinhaeTerrain missing");
            else using(var r=new BinaryReader(new MemoryStream(terrain.bytes)))
            {
                if(new string(r.ReadChars(4))!="PTDM")throw new InvalidDataException("Geo/JinhaeTerrain: bad header");
                demCols=r.ReadInt32();demRows=r.ReadInt32();demLon0=r.ReadDouble();demLat0=r.ReadDouble();demLon1=r.ReadDouble();demLat1=r.ReadDouble();
                dem=new short[demCols*demRows];for(int i=0;i<dem.Length;i++)dem[i]=r.ReadInt16();
            }
            var water=Resources.Load<TextAsset>("Geo/JinhaeWater");
            if(water==null)Debug.LogError("CarvedDistrict: Geo/JinhaeWater missing");
            else using(var r=new BinaryReader(new MemoryStream(water.bytes)))
            {
                if(new string(r.ReadChars(4))!="PTWM")throw new InvalidDataException("Geo/JinhaeWater: bad header");
                wetCols=r.ReadInt32();wetRows=r.ReadInt32();wetLon0=r.ReadDouble();wetLat0=r.ReadDouble();wetLon1=r.ReadDouble();wetLat1=r.ReadDouble();
                wet=r.ReadBytes((wetCols*wetRows+7)/8);
            }
            datum=0;datum=Raw(Vector2.zero);
            MappedRails.Clear();
            mappedRailStructures.Clear();mappedCrossings.Clear();
            foreach(var row in Lines("Geo/JinhaeRail"))
            {
                var f=row.Split('|');if(f.Length<4)continue;
                MappedRails.Add(new KeyValuePair<string,Vector2[]>(f[2],Points(f[3])));
                string structure=f.Length>4?f[4]:"ground";
                mappedRailStructures.Add(structure=="bridge"?BridgeRail:structure=="tunnel"?TunnelRail:GroundRail);
            }
            foreach(var row in Lines("Geo/JinhaeCrossings")){var f=row.Split('|');if(f.Length>=2){var p=Local(double.Parse(f[0],CultureInfo.InvariantCulture),double.Parse(f[1],CultureInfo.InvariantCulture));mappedCrossings.Add(new Vector2(p.x,p.z));}}
        }
        static IEnumerable<string> Lines(string resource)
        {
            var text=Resources.Load<TextAsset>(resource);
            if(text==null){Debug.LogError("CarvedDistrict: "+resource+" missing");yield break;}
            foreach(var row in text.text.Split('\n')){var t=row.Trim();if(t.Length>0)yield return t;}
        }
        Vector2[] Points(string pairs)
        {
            var list=pairs.Split(';');var points=new Vector2[list.Length];
            for(int i=0;i<list.Length;i++){var xy=list[i].Split(',');var p=Local(double.Parse(xy[0],CultureInfo.InvariantCulture),double.Parse(xy[1],CultureInfo.InvariantCulture));points[i]=new Vector2(p.x,p.z);}
            return points;
        }

        // The surveyed ground (never below the sea) relative to the origin, before any pad is levelled.
        public float Raw(Vector2 p)
        {
            if(dem==null)return 0;
            double lon=lon0+p.x/sx,lat=lat0+p.y/111320.0;
            float fx=Mathf.Clamp((float)((lon-demLon0)/(demLon1-demLon0)*(demCols-1)),0,demCols-1.001f),fz=Mathf.Clamp((float)((lat-demLat0)/(demLat1-demLat0)*(demRows-1)),0,demRows-1.001f);
            int i=(int)fx,j=(int)fz;float u=fx-i,v=fz-j;
            float a=dem[j*demCols+i],b=dem[j*demCols+i+1],c=dem[(j+1)*demCols+i],d=dem[(j+1)*demCols+i+1];
            return Mathf.Max(Mathf.Lerp(Mathf.Lerp(a,b,u),Mathf.Lerp(c,d,u),v)*.1f,0)-datum;
        }
        // The ground height at a point, with the pads levelled.
        public float Elevation(Vector2 p)
        {
            float e=Raw(p);
            foreach(var flat in flats){float w=flat.Key(p);if(w>0)e=Mathf.Lerp(e,flat.Value,Mathf.Clamp01(w));}
            return e;
        }
        public float Base(Vector2 p){return Mathf.Round(Elevation(p)/Step)*Step;}
        public bool WaterAt(Vector2 p)
        {
            if(wet==null)return false;
            double lon=lon0+p.x/sx,lat=lat0+p.y/111320.0;
            int i=(int)Math.Round((lon-wetLon0)/(wetLon1-wetLon0)*(wetCols-1)),j=(int)Math.Round((lat-wetLat0)/(wetLat1-wetLat0)*(wetRows-1));
            if(i<0||j<0||i>=wetCols||j>=wetRows)return false;
            int at=j*wetCols+i;return (wet[at>>3]>>(at&7)&1)!=0;
        }
        // How far the pads level the ground at a point (0..1).
        float FlatWeight(Vector2 p){float w=0;foreach(var flat in flats)w=Mathf.Max(w,Mathf.Clamp01(flat.Key(p)));return w;}
        static Vector2 PointAt(Shape s,float distance)
        {
            int k=Array.BinarySearch(s.along,distance);if(k<0)k=~k;k=Mathf.Clamp(k,1,s.along.Length-1);
            float l=s.along[k]-s.along[k-1];return l<1e-4f?s.points[k]:Vector2.Lerp(s.points[k-1],s.points[k],Mathf.Clamp01((distance-s.along[k-1])/l));
        }
        void Grade(Shape s)
        {
            int n=Mathf.CeilToInt(s.along[s.along.Length-1]/GradeStep)+2;var ground=new float[n];var pad=new float[n];var sum=new double[n+1];
            for(int i=0;i<n;i++){var p=PointAt(s,i*GradeStep);ground[i]=Elevation(p);pad[i]=FlatWeight(p);sum[i+1]=sum[i]+ground[i];}
            int w=Mathf.RoundToInt(GradeWindow/GradeStep);s.grade=new float[n];
            for(int i=0;i<n;i++)
            {
                int a=Mathf.Max(0,i-w),b=Mathf.Min(n,i+w+1);float mean=(float)((sum[b]-sum[a])/(b-a));
                if(s.structure==GroundRail)s.grade[i]=ground[i];
                else s.grade[i]=Mathf.Max(Mathf.Lerp(mean,ground[i],pad[i]),-datum+2f);
                var q=PointAt(s,i*GradeStep);
                if(s.structure==GroundRail)foreach(var crossing in mappedCrossings)if((crossing-q).sqrMagnitude<36f){s.grade[i]=ground[i];break;}
                if(i<n-1&&s.structure==BridgeRail)BridgeMetres+=GradeStep;else if(i<n-1&&s.structure==TunnelRail)TunnelMetres+=GradeStep;
            }
        }
        static float GradeAt(Shape s,float distance)
        {
            float f=Mathf.Max(distance,0)/GradeStep;int i=Mathf.Clamp((int)f,0,s.grade.Length-2);return Mathf.Lerp(s.grade[i],s.grade[i+1],f-i);
        }
        // The ground level of the rail line nearest `p` (within 4 m: the bed, bridge deck or tunnel floor), else the ground.
        public float LineHeight(Vector2 p)
        {
            List<Shape> shapes;float best=16f,found=float.NaN;
            if(buckets.TryGetValue(Key(new Vector3(p.x,0,p.y)),out shapes))foreach(var s in shapes)
            {
                if(s.kind!=Bed)continue;
                for(int k=1;k<s.points.Length;k++)
                {
                    var a=s.points[k-1];var d=s.points[k]-a;float l2=d.sqrMagnitude;if(l2<1e-6f)continue;
                    float t=Mathf.Clamp01(Vector2.Dot(p-a,d)/l2);float gap=(a+d*t-p).sqrMagnitude;
                    if(gap<best){best=gap;found=GradeAt(s,s.along[k-1]+t*Mathf.Sqrt(l2));}
                }
            }
            return float.IsNaN(found)?Elevation(p):found;
        }

        // Gathers the roads (OSM, inside `area`, a lon/lat box), the building footprints, the mapped rails and
        // `extraRails` (local metres: station tracks the map lacks). Set site and flats first.
        public void Setup(Rect area,List<Vector2[]> extraRails)
        {
            foreach(var f in CityContext.Source)
            {
                if(f.building||!area.Contains(new Vector2(f.lon,f.lat)))continue;
                var points=new Vector2[f.points.Length];
                for(int i=0;i<points.Length;i++){var p=Local(f.points[i].x,f.points[i].y);points[i]=new Vector2(p.x,p.z);}
                Add(new Shape{kind=Road,points=points,size=f.size,trees=!string.IsNullOrEmpty(f.name)});
            }
            BuildingCount=0;
            foreach(var row in Lines("Geo/JinhaeBuildings"))
            {
                var f=row.Split('|');if(f.Length<5)continue;
                var points=Points(f[4]);if(points.Length<3)continue;
                var centre=Vector2.zero;foreach(var p in points)centre+=p;centre/=points.Length;
                if(site!=null&&site(centre)!=0)continue; // the station's own ground
                Add(new Shape{kind=House,points=points,size=float.Parse(f[2],CultureInfo.InvariantCulture),seed=f[1].GetHashCode()&0x7fffffff,baseY=Base(centre)});
                BuildingCount++;
            }
            BridgeMetres=TunnelMetres=0;
            var groups=new List<string>();for(int i=0;i<MappedRails.Count;i++){string key=MappedRails[i].Key+"|"+mappedRailStructures[i];if(!groups.Contains(key))groups.Add(key);}
            foreach(var group in groups)
            {
                var parts=group.Split('|');byte structure=byte.Parse(parts[parts.Length-1]);string name=group.Substring(0,group.LastIndexOf('|'));
                var ways=new List<Vector2[]>();for(int i=0;i<MappedRails.Count;i++)if(MappedRails[i].Key==name&&mappedRailStructures[i]==structure)ways.Add(MappedRails[i].Value);
                foreach(var line in Join(ways))AddRail(line,structure);
            }
            if(extraRails!=null)foreach(var points in extraRails)AddRail(points,GroundRail);
        }
        void AddRail(Vector2[] points,byte structure){var s=new Shape{kind=Bed,structure=structure,points=points};Add(s);if(s.along!=null)Grade(s);}
        // Ways joined into lines where their ends meet (within 2 m), so a line's level runs on across the joins.
        static List<Vector2[]> Join(List<Vector2[]> ways)
        {
            var left=new List<Vector2[]>(ways);var lines=new List<Vector2[]>();
            while(left.Count>0)
            {
                var line=new List<Vector2>(left[0]);left.RemoveAt(0);
                for(bool grew=true;grew;)
                {
                    grew=false;
                    for(int w=0;w<left.Count&&!grew;w++)
                    {
                        var way=new List<Vector2>(left[w]);Vector2 head=line[0],tail=line[line.Count-1];
                        if((way[way.Count-1]-tail).sqrMagnitude<4f||(way[0]-head).sqrMagnitude<4f)way.Reverse();
                        if((way[0]-tail).sqrMagnitude<4f){way.RemoveAt(0);line.AddRange(way);grew=true;}
                        else if((way[way.Count-1]-head).sqrMagnitude<4f){way.RemoveAt(way.Count-1);line.InsertRange(0,way);grew=true;}
                        if(grew)left.RemoveAt(w);
                    }
                }
                lines.Add(line.ToArray());
            }
            return lines;
        }
        void Add(Shape shape)
        {
            if(shape.points.Length<2)return;
            shape.along=new float[shape.points.Length];
            for(int i=1;i<shape.points.Length;i++)shape.along[i]=shape.along[i-1]+Vector2.Distance(shape.points[i-1],shape.points[i]);
            shape.box=Bounds(shape.points);
            float reach=shape.kind==House?4f:Mathf.Max(shape.size,3f)*.5f+14f; // the pavement beside a building or road
            var keys=new HashSet<Vector2Int>();
            int count=shape.kind==House?1:shape.points.Length-1;
            for(int s=0;s<count;s++)
            {
                var box=shape.kind==House?shape.box:Rect.MinMaxRect(Mathf.Min(shape.points[s].x,shape.points[s+1].x),Mathf.Min(shape.points[s].y,shape.points[s+1].y),Mathf.Max(shape.points[s].x,shape.points[s+1].x),Mathf.Max(shape.points[s].y,shape.points[s+1].y));
                for(int x=Mathf.FloorToInt((box.xMin-reach-2f)/Chunk);x<=Mathf.FloorToInt((box.xMax+reach+2f)/Chunk);x++)
                    for(int z=Mathf.FloorToInt((box.yMin-reach-2f)/Chunk);z<=Mathf.FloorToInt((box.yMax+reach+2f)/Chunk);z++)keys.Add(new Vector2Int(x,z));
            }
            foreach(var key in keys){List<Shape> list;if(!buckets.TryGetValue(key,out list))buckets[key]=list=new List<Shape>();list.Add(shape);}
        }
        static Rect Bounds(Vector2[] points)
        {
            Vector2 lo=points[0],hi=points[0];foreach(var p in points){lo=Vector2.Min(lo,p);hi=Vector2.Max(hi,p);}
            return Rect.MinMaxRect(lo.x,lo.y,hi.x,hi.y);
        }

        // Makes the fine chunks within `radius` of `at` and the coarse ones out to Radius now (the walker's start).
        public void BuildAround(Vector3 at,float radius)
        {
            FarLand();
            foreach(var key in Wanted(at,radius))if(!Has(key,true))Build(key,true);
            foreach(var key in Wanted(at,Radius))if(!pieces.ContainsKey(key))Build(key,false);
        }
        bool Has(Vector2Int key,bool fine){Piece p;return pieces.TryGetValue(key,out p)&&(p.fine||!fine);}
        List<Vector2Int> Wanted(Vector3 at,float radius)
        {
            var keys=new List<Vector2Int>();int reach=Mathf.CeilToInt(radius/Chunk);var centre=Key(at);
            for(int x=-reach;x<=reach;x++)for(int z=-reach;z<=reach;z++)
            {
                var key=new Vector2Int(centre.x+x,centre.y+z);
                if(Distance(key,at)<=radius)keys.Add(key);
            }
            keys.Sort((a,b)=>Distance(a,at).CompareTo(Distance(b,at)));
            return keys;
        }
        static Vector2Int Key(Vector3 p){return new Vector2Int(Mathf.FloorToInt(p.x/Chunk),Mathf.FloorToInt(p.z/Chunk));}
        static float Distance(Vector2Int key,Vector3 p)
        {
            float dx=Mathf.Max(key.x*Chunk-p.x,0,p.x-(key.x+1)*Chunk),dz=Mathf.Max(key.y*Chunk-p.z,0,p.z-(key.y+1)*Chunk);
            return Mathf.Sqrt(dx*dx+dz*dz);
        }

        void Update()
        {
            if(focus==null)return;
            if(Time.unscaledTime>=nextScan){nextScan=Time.unscaledTime+.25f;DropFar(focus.position);}
            Stream(focus.position);MovePeople(Time.deltaTime);
        }
        // Makes one chunk a call: the nearest that should be fine and is not, else the nearest missing coarse one.
        public void Stream(Vector3 at)
        {
            var here=Key(at);if(here==settledAt)return;
            foreach(var key in Wanted(at,Near))if(!Has(key,true)){Build(key,true);return;}
            foreach(var key in Wanted(at,Radius))if(!pieces.ContainsKey(key)){Build(key,false);return;}
            settledAt=here;
        }
        // Drops the chunks far behind and makes the fine ones left behind coarse again.
        public void DropFar(Vector3 at)
        {
            var drop=new List<Vector2Int>();var coarsen=new List<Vector2Int>();
            foreach(var pair in pieces)
            {
                float distance=Distance(pair.Key,at);
                if(distance>Radius+96f)drop.Add(pair.Key);else if(pair.Value.fine&&distance>Near+96f)coarsen.Add(pair.Key);
            }
            foreach(var key in drop)Drop(key);
            foreach(var key in coarsen)Build(key,false);
        }
        void Drop(Vector2Int key)
        {
            var piece=pieces[key];
            if(piece.go!=null){DestroyNow(piece.go.GetComponent<MeshFilter>().sharedMesh);DestroyNow(piece.go);}
            pieces.Remove(key);settledAt=new Vector2Int(int.MinValue,0);
        }
        static void DestroyNow(UnityEngine.Object o){if(Application.isPlaying)Destroy(o);else DestroyImmediate(o);}

        // Ground height and kind at a world point, if its chunk is made.
        public bool Sample(Vector3 p,out float height,out byte kind)
        {
            Piece piece;height=0;kind=Earth;
            var key=Key(p);if(!pieces.TryGetValue(key,out piece))return false;
            int n=piece.n,row=n+2;
            int i=Mathf.Clamp(Mathf.FloorToInt((p.x-key.x*Chunk)/piece.cell),0,n-1),j=Mathf.Clamp(Mathf.FloorToInt((p.z-key.y*Chunk)/piece.cell),0,n-1);
            int at=(i+1)+(j+1)*row;height=piece.height[at];kind=piece.kind[at];return true;
        }

        void Build(Vector2Int key,bool fine)
        {
            if(pieces.ContainsKey(key))Drop(key);
            int n=fine?128:32;int row=n+2;
            var piece=new Piece{n=n,cell=Chunk/n,fine=fine,height=new float[row*row],kind=new byte[row*row],tone=new byte[row*row]};
            Carve(key,piece);
            var data=new MeshData();
            Columns(key,piece,data);
            List<Shape> shapes;
            if(buckets.TryGetValue(key,out shapes))foreach(var s in shapes)if(s.kind==Bed)Structures(key,s,data);
            if(fine&&buckets.TryGetValue(key,out shapes))
            {
                foreach(var s in shapes)if(s.kind==Bed)LayRails(key,s,data);
                if(tree!=null)foreach(var s in shapes)if(s.kind==Road&&s.trees)PlantTrees(key,piece,s,data);
            }
            if(fine&&pine!=null)PlantForest(key,piece,data);
            var go=new GameObject((fine?"진해 덩어리 ":"진해 먼 덩어리 ")+key.x+","+key.y);go.transform.SetParent(transform,false);
            var mesh=data.Build(go.name);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
            if(fine)go.AddComponent<MeshCollider>().sharedMesh=mesh;
            piece.go=go;pieces[key]=piece;
            if(fine)SpawnPeople(key,piece);
        }

        // The lump's surface over the chunk and one cell around it.
        void Carve(Vector2Int key,Piece piece)
        {
            int row=piece.n+2;float cell=piece.cell,x0=key.x*Chunk-cell,z0=key.y*Chunk-cell;
            var town=new bool[row*row];
            for(int j=0;j<row;j++)for(int i=0;i<row;i++)
            {
                int at=i+j*row;var p=new Vector2(x0+(i+.5f)*cell,z0+(j+.5f)*cell);
                int where=site!=null?site(p):0;float e=Base(p);
                if(where==0&&WaterAt(p)){piece.kind[at]=Water;piece.height[at]=e-.6f;continue;}
                piece.kind[at]=where==1?Plaza:where==2?Bed:Grass;piece.height[at]=e+Ground;town[at]=where!=0;
            }
            List<Shape> shapes;
            if(buckets.TryGetValue(key,out shapes))foreach(byte pass in new[]{Bed,Road,House})foreach(var s in shapes)
            {
                if(s.kind!=pass)continue;
                if(s.kind==House){Footprint(s,piece,x0,z0,town);continue;}
                float half=s.kind==Bed?1.5f:Mathf.Clamp(s.size,3f,35f)*.5f,wide=s.kind==Bed?Cutting:half+12f;
                for(int k=1;k<s.points.Length;k++)
                {
                    Vector2 a=s.points[k-1],b=s.points[k],d=b-a;float length=d.magnitude;if(length<.01f)continue;
                    int i0=Mathf.Max(0,Mathf.FloorToInt((Mathf.Min(a.x,b.x)-wide-x0)/cell)),i1=Mathf.Min(row-1,Mathf.FloorToInt((Mathf.Max(a.x,b.x)+wide-x0)/cell));
                    int j0=Mathf.Max(0,Mathf.FloorToInt((Mathf.Min(a.y,b.y)-wide-z0)/cell)),j1=Mathf.Min(row-1,Mathf.FloorToInt((Mathf.Max(a.y,b.y)+wide-z0)/cell));
                    for(int j=j0;j<=j1;j++)for(int i=i0;i<=i1;i++)
                    {
                        var p=new Vector2(x0+(i+.5f)*cell,z0+(j+.5f)*cell);float t=Mathf.Clamp(Vector2.Dot(p-a,d)/(length*length),0,1);
                        float off=Vector2.Distance(p,a+d*t);if(off>=wide)continue;
                        int at=i+j*row;
                        if(s.kind==Bed)
                        {
                            if(piece.kind[at]==Plaza)continue;
                            float rail=Mathf.Round(GradeAt(s,s.along[k-1]+t*length)/Step)*Step,e=Base(p);
                            if(s.structure==BridgeRail)continue; // mapped bridge spans the terrain or water below it
                            if(off<half||rail<e){piece.kind[at]=Bed;piece.height[at]=rail+Ground;} // the bed, or the cutting
                            continue;
                        }
                        town[at]=true;if(off>=half||piece.kind[at]==Plaza||piece.kind[at]==Bed)continue; // the station square is paved over; level crossings keep the bed
                        bool line=s.size>=8f&&off<.26f&&Mathf.Repeat(s.along[k-1]+t*length,6f)<3.5f;
                        piece.kind[at]=line?Line:Road;piece.height[at]=Base(p);
                    }
                }
            }
            // Pavement near roads and buildings; everywhere else is grass, wood and hillside.
            for(int at=0;at<row*row;at++)if(piece.kind[at]==Grass&&town[at])piece.kind[at]=Earth;
        }
        void Footprint(Shape s,Piece piece,float x0,float z0,bool[] town)
        {
            int row=piece.n+2;float cell=piece.cell,top=s.baseY+Ground+Mathf.Clamp(s.size,3f,90f);
            int pad=Mathf.CeilToInt(3f/cell);
            int i0=Mathf.FloorToInt((s.box.xMin-x0)/cell),i1=Mathf.FloorToInt((s.box.xMax-x0)/cell);
            int j0=Mathf.FloorToInt((s.box.yMin-z0)/cell),j1=Mathf.FloorToInt((s.box.yMax-z0)/cell);
            for(int j=Mathf.Max(0,j0-pad);j<=Mathf.Min(row-1,j1+pad);j++)for(int i=Mathf.Max(0,i0-pad);i<=Mathf.Min(row-1,i1+pad);i++)town[i+j*row]=true;
            for(int j=Mathf.Max(0,j0);j<=Mathf.Min(row-1,j1);j++)for(int i=Mathf.Max(0,i0);i<=Mathf.Min(row-1,i1);i++)
            {
                var p=new Vector2(x0+(i+.5f)*cell,z0+(j+.5f)*cell);int at=i+j*row;byte under=piece.kind[at];
                if(under==Water||under==Plaza||under==Bed||under==Road||under==Line||!Inside(p,s.points))continue;
                piece.kind[at]=House;piece.height[at]=Mathf.Max(top,piece.height[at]+3f);piece.tone[at]=(byte)(s.seed%Walls.Length);
            }
        }
        static bool Inside(Vector2 p,Vector2[] polygon)
        {
            bool inside=false;
            for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
            {
                var a=polygon[i];var b=polygon[j];
                if((a.y>p.y)!=(b.y>p.y)&&p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)inside=!inside;
            }
            return inside;
        }

        static readonly Color32[] Walls={new Color32(237,227,204,255),new Color32(230,230,224,255),new Color32(199,201,204,255),new Color32(219,199,168,255),new Color32(234,204,204,255),new Color32(194,219,209,255),new Color32(168,107,87,255)};
        static readonly Color32[] Roofs={new Color32(107,148,115,255),new Color32(140,143,145,255),new Color32(122,128,135,255)};
        static readonly Color32 Window=new Color32(77,97,117,255),Asphalt=new Color32(61,64,69,255),Paint=new Color32(237,191,51,255),Gravel=new Color32(120,110,97,255),Kerb=new Color32(150,146,136,255);
        Color32 Top(byte kind,byte tone,float height,int gx,int gz)
        {
            bool odd=(((gx>>1)+(gz>>1))&1)!=0;
            switch(kind)
            {
                case Road:return Asphalt;
                case Line:return Paint;
                case Bed:return Gravel;
                case House:return Roofs[tone%Roofs.Length];
                case Water:return odd?new Color32(58,104,146,255):new Color32(64,112,154,255);
                case Plaza:return (((gx>>2)+(gz>>2))&1)==0?new Color32(176,164,146,255):new Color32(160,149,132,255);
                case Grass:return Hillside(height+datum,odd);
                default:return odd?new Color32(164,160,148,255):new Color32(172,168,156,255);
            }
        }
        // Meadow low down, dark wood up the hills (by height above the sea).
        static Color32 Hillside(float aboveSea,bool odd)
        {
            var low=odd?new Color32(112,146,86,255):new Color32(104,138,80,255);var high=odd?new Color32(66,104,60,255):new Color32(60,96,56,255);
            return Color32.Lerp(low,high,Mathf.Clamp01((aboveSea-30f)/90f));
        }
        static Color32 Side(byte kind)
        {
            switch(kind){case Grass:return new Color32(92,78,60,255);case Bed:return new Color32(100,92,82,255);case Plaza:return new Color32(150,140,125,255);default:return Kerb;}
        }

        // Tops merged into rectangles of one height and colour, and a wall wherever a cell stands above its neighbour.
        void Columns(Vector2Int key,Piece piece,MeshData data)
        {
            int n=piece.n,row=n+2;float cell=piece.cell,x0=key.x*Chunk,z0=key.y*Chunk;var m=Matrix4x4.identity;
            var colours=new Color32[n*n];var done=new bool[n*n];
            for(int j=0;j<n;j++)for(int i=0;i<n;i++){int at=(i+1)+(j+1)*row;colours[i+j*n]=Top(piece.kind[at],piece.tone[at],piece.height[at],Mathf.FloorToInt((x0+i*cell)/.5f),Mathf.FloorToInt((z0+j*cell)/.5f));}
            for(int j=0;j<n;j++)for(int i=0;i<n;)
            {
                int c=i+j*n;if(done[c]){i++;continue;}
                float h=piece.height[(i+1)+(j+1)*row];var colour=colours[c];
                int w=1;while(i+w<n&&Same(i+w,j,h,colour,colours,done,piece))w++;
                int d=1;bool grow=true;
                while(j+d<n&&grow){for(int k=0;k<w;k++)if(!Same(i+k,j+d,h,colour,colours,done,piece)){grow=false;break;}if(grow)d++;}
                for(int y=0;y<d;y++)for(int k=0;k<w;k++)done[i+k+(j+y)*n]=true;
                var a=new Vector3(x0+i*cell,h,z0+j*cell);
                data.Quad(m,a,a+Vector3.forward*d*cell,a+new Vector3(w*cell,0,d*cell),a+Vector3.right*w*cell,Vector3.up,colour);
                i+=w;
            }
            // Walls facing -x, +x, -z, +z, merged along their run.
            for(int axis=0;axis<2;axis++)foreach(int dir in new[]{-1,1})
                for(int line=0;line<n;line++)
                {
                    int run=0;float top=0,bottom=0;byte tone=0,what=0;
                    for(int k=0;k<=n;k++)
                    {
                        bool wall=false;float t=0,b=0;byte o=0,kind=0;
                        if(k<n)
                        {
                            int i=axis==0?line:k,j=axis==0?k:line;int at=(i+1)+(j+1)*row;
                            t=piece.height[at];b=piece.height[axis==0?at+dir:at+dir*row];
                            if(t>b+.001f){wall=true;kind=piece.kind[at];o=kind==House?piece.tone[at]:(byte)0;}
                        }
                        if(run>0&&(!wall||t!=top||b!=bottom||kind!=what||o!=tone))
                        {
                            int start=k-run;float across=(line+(dir>0?1:0))*cell;
                            Vector3 p0,p1;
                            if(axis==0){p0=new Vector3(x0+across,0,z0+start*cell);p1=new Vector3(x0+across,0,z0+k*cell);}
                            else{p0=new Vector3(x0+start*cell,0,z0+across);p1=new Vector3(x0+k*cell,0,z0+across);}
                            var normal=axis==0?new Vector3(dir,0,0):new Vector3(0,0,dir);
                            if(what==House)Facade(data,p0,p1,bottom,top,normal,Walls[tone%Walls.Length]);
                            else data.Quad(m,p0+Vector3.up*bottom,p1+Vector3.up*bottom,p1+Vector3.up*top,p0+Vector3.up*top,normal,Side(what));
                            run=0;
                        }
                        if(wall){if(run==0){top=t;bottom=b;tone=o;what=kind;}run++;}
                    }
                }
        }
        static bool Same(int i,int j,float h,Color32 colour,Color32[] colours,bool[] done,Piece piece)
        {
            int n=piece.n,c=i+j*n;return !done[c]&&colours[c].Equals(colour)&&piece.height[(i+1)+(j+1)*(n+2)]==h;
        }
        // A building wall cut into storeys from the ground beside it: a band of windows in each, a parapet on top.
        static void Facade(MeshData data,Vector3 p0,Vector3 p1,float bottom,float top,Vector3 normal,Color32 wall)
        {
            var m=Matrix4x4.identity;var cuts=new List<float>{bottom,top};
            for(float floor=bottom;floor<top-.6f;floor+=3f){cuts.Add(floor+.9f);cuts.Add(Mathf.Min(floor+2.3f,top-.6f));}
            cuts.RemoveAll(y=>y<bottom||y>top);cuts.Sort();
            for(int k=1;k<cuts.Count;k++)
            {
                float y0=cuts[k-1],y1=cuts[k];if(y1-y0<.01f)continue;
                float mid=(y0+y1)*.5f,inFloor=Mathf.Repeat(mid-bottom,3f);
                bool glass=mid<top-.6f&&inFloor>.9f&&inFloor<2.3f;
                data.Quad(m,p0+Vector3.up*y0,p1+Vector3.up*y0,p1+Vector3.up*y1,p0+Vector3.up*y1,normal,glass?Window:wall);
            }
        }

        // Two rails on sleepers along the part of the line whose segments start in this chunk, following its grade.
        void LayRails(Vector2Int key,Shape s,MeshData data)
        {
            var area=new Rect(key.x*Chunk,key.y*Chunk,Chunk,Chunk);
            var steel=new Color32(92,96,102,255);var timber=new Color32(97,84,71,255);
            for(int k=1;k<s.points.Length;k++)
            {
                Vector2 a=s.points[k-1],b=s.points[k];if(!area.Contains((a+b)*.5f))continue;
                var d=b-a;float length=d.magnitude;if(length<.01f)continue;d/=length;var nrm=new Vector2(-d.y,d.x);
                for(float at=0;at<length;at+=8f) // short pieces, so the rails follow the ground
                {
                    float to=Mathf.Min(at+8f,length);Vector2 p=a+d*at,q=a+d*to;float yp=GradeAt(s,s.along[k-1]+at)+Ground+.25f,yq=GradeAt(s,s.along[k-1]+to)+Ground+.25f;
                    foreach(float side in new[]{-.7175f,.7175f})data.Bar(V(p+nrm*side),V(q+nrm*side),.04f,yp-.15f,yp,yq-.15f,yq,steel);
                }
                for(float at=Mathf.Ceil(s.along[k-1]/.65f)*.65f;at<s.along[k];at+=.65f)
                {
                    var c=a+d*(at-s.along[k-1]);float y=GradeAt(s,at)+Ground+.1f;
                    data.Bar(V(c-nrm*1.2f),V(c+nrm*1.2f),.11f,y-.12f,y,timber);
                }
            }
        }
        static Vector3 V(Vector2 p){return new Vector3(p.x,0,p.y);}
        // Bridges where the line runs high over the ground or water (a deck on piers every 24 m, low parapets), and a
        // concrete roof over its deep cuttings, along the part of the line whose segments start in this chunk.
        void Structures(Vector2Int key,Shape s,MeshData data)
        {
            var area=new Rect(key.x*Chunk,key.y*Chunk,Chunk,Chunk);
            var concrete=new Color32(158,155,148,255);var deck=new Color32(120,118,112,255);
            for(int k=1;k<s.points.Length;k++)
            {
                Vector2 a=s.points[k-1],b=s.points[k];if(!area.Contains((a+b)*.5f))continue;
                var d=b-a;float length=d.magnitude;if(length<.01f)continue;d/=length;var nrm=new Vector2(-d.y,d.x);
                for(float at=0;at<length;at+=8f)
                {
                    float to=Mathf.Min(at+8f,length);Vector2 p=a+d*at,q=a+d*to,mid=(p+q)*.5f;
                    float gp=GradeAt(s,s.along[k-1]+at)+Ground,gq=GradeAt(s,s.along[k-1]+to)+Ground,e=Elevation(mid);
                    if(s.structure==BridgeRail)
                    {
                        data.Bar(V(p),V(q),2.2f,gp-.9f,gp,gq-.9f,gq,deck);
                        foreach(float side in new[]{-2.05f,2.05f})data.Bar(V(p+nrm*side),V(q+nrm*side),.15f,gp,gp+.9f,gq,gq+.9f,concrete);
                        float pier=Mathf.Ceil((s.along[k-1]+at)/24f)*24f-s.along[k-1];
                        if(pier<to){var c=a+d*pier;float top=GradeAt(s,s.along[k-1]+pier)+Ground-.9f;data.Bar(V(c-nrm*1.8f),V(c+nrm*1.8f),.6f,Mathf.Min(e,top)-1f,top,concrete);}
                    }
                    else if(s.structure==TunnelRail)
                        foreach(float side in new[]{-1f,1f})
                        {
                            data.Bar(V(p+nrm*side*(Cutting-.2f)),V(q+nrm*side*(Cutting-.2f)),.2f,gp,gp+5.6f,gq,gq+5.6f,concrete);
                            data.Bar(V(p+nrm*side*1.6f),V(q+nrm*side*1.6f),1.6f,gp+5.6f,gp+6.2f,gq+5.6f,gq+6.2f,concrete);
                        }
                }
            }
        }

        static void Append(MeshData data,Mesh mesh,Matrix4x4 place)
        {
            var vertices=mesh.vertices;var normals=mesh.normals;var colours=mesh.colors32;var triangles=mesh.triangles;int start=data.vertices.Count;
            for(int v=0;v<vertices.Length;v++){data.vertices.Add(place.MultiplyPoint3x4(vertices[v]));data.normals.Add(place.MultiplyVector(normals[v]));data.colours.Add(colours[v]);}
            foreach(int t in triangles)data.triangles.Add(start+t);
        }
        // Cherry trees every 16 m along each named road, on the pavement either side.
        void PlantTrees(Vector2Int key,Piece piece,Shape s,MeshData data)
        {
            var area=new Rect(key.x*Chunk,key.y*Chunk,Chunk,Chunk);float half=Mathf.Clamp(s.size,3f,35f)*.5f+1.4f;int n=piece.n,row=n+2;
            for(int k=1;k<s.points.Length;k++)
            {
                Vector2 a=s.points[k-1],b=s.points[k];var d=b-a;float length=d.magnitude;if(length<.01f)continue;d/=length;var nrm=new Vector2(-d.y,d.x);
                for(float at=Mathf.Ceil(s.along[k-1]/16f)*16f;at<s.along[k];at+=16f)
                    foreach(float side in new[]{-1f,1f})
                    {
                        var p=a+d*(at-s.along[k-1])+nrm*side*half;if(!area.Contains(p))continue;
                        int i=Mathf.Clamp(Mathf.FloorToInt((p.x-area.xMin)/piece.cell),0,n-1),j=Mathf.Clamp(Mathf.FloorToInt((p.y-area.yMin)/piece.cell),0,n-1);int cell=(i+1)+(j+1)*row;
                        if(piece.kind[cell]!=Earth&&piece.kind[cell]!=Plaza)continue;
                        bool clear=true;foreach(int nb in new[]{cell-1,cell+1,cell-row,cell+row})if(piece.kind[nb]==House||piece.kind[nb]==Bed||piece.kind[nb]==Water)clear=false;
                        if(!clear)continue;
                        Append(data,tree,Matrix4x4.TRS(new Vector3(p.x,piece.height[cell],p.y),Quaternion.Euler(0,(Mathf.FloorToInt(at)*37)%4*90,0),Vector3.one));
                    }
            }
        }
        // Pines on the open hillsides (35 m above the sea and up), scattered over a 10 m grid.
        void PlantForest(Vector2Int key,Piece piece,MeshData data)
        {
            int n=piece.n,row=n+2;const int Grid=6;
            for(int gx=0;gx<Grid;gx++)for(int gz=0;gz<Grid;gz++)
            {
                int h=Hash(key.x*Grid+gx,key.y*Grid+gz);if(h%100>=55)continue;
                float x=(gx+.2f+(h/100%60)/100f)*Chunk/Grid,z=(gz+.2f+(h/6000%60)/100f)*Chunk/Grid;
                int i=Mathf.Clamp((int)(x/piece.cell),2,n-3),j=Mathf.Clamp((int)(z/piece.cell),2,n-3);int at=(i+1)+(j+1)*row;
                if(piece.kind[at]!=Grass||piece.height[at]+datum<35f)continue;
                bool clear=true;foreach(int nb in new[]{at-2,at+2,at-2*row,at+2*row})if(piece.kind[nb]!=Grass)clear=false;
                if(!clear)continue;
                Append(data,pine,Matrix4x4.TRS(new Vector3(key.x*Chunk+x,piece.height[at],key.y*Chunk+z),Quaternion.Euler(0,h%4*90,0),Vector3.one*(.8f+(h/360000%40)/100f)));
            }
        }
        static int Hash(int x,int z){unchecked{uint h=(uint)(x*73856093)^(uint)(z*19349663);h^=h>>13;h*=0x5bd1e995;h^=h>>15;return (int)(h&0x7fffffff);}}

        // The whole surveyed land to the horizon on a 60 m grid, sunk a little under the made chunks, and the sea as one
        // level sheet: the coast is where the land rises through it, so it follows the terrain, not the grid.
        Transform far;
        void FarLand()
        {
            if(far!=null||dem==null)return;
            const int Stride=2;var data=new MeshData();const float Sea=-.4f;
            int cols=(demCols-1)/Stride+1,rows=(demRows-1)/Stride+1;
            for(int j=0;j<rows;j++)for(int i=0;i<cols;i++)
            {
                double lon=demLon0+(demLon1-demLon0)*(i*Stride)/(demCols-1),lat=demLat0+(demLat1-demLat0)*(j*Stride)/(demRows-1);
                var p=Local(lon,lat);var q=new Vector2(p.x,p.z);bool sea=WaterAt(q);float e=Elevation(q);
                float above=e+datum;sea=sea&&above<1.5f; // inland water keeps its height
                data.vertices.Add(new Vector3(p.x,sea?-datum-3f:e-Mathf.Clamp(above-1f,0,2.5f),p.z));data.normals.Add(Vector3.up);
                data.colours.Add(WaterAt(q)?new Color32(60,106,148,255):Hillside(above,false));
            }
            for(int j=0;j<rows-1;j++)for(int i=0;i<cols-1;i++){int a=i+j*cols;data.triangles.AddRange(new[]{a,a+cols,a+cols+1,a,a+cols+1,a+1});}
            int corner=data.vertices.Count;var c0=Local(demLon0,demLat0);var c1=Local(demLon1,demLat1);
            foreach(var c in new[]{new Vector3(c0.x,0,c0.z),new Vector3(c0.x,0,c1.z),new Vector3(c1.x,0,c1.z),new Vector3(c1.x,0,c0.z)})
            {data.vertices.Add(c+Vector3.up*(Sea-datum));data.normals.Add(Vector3.up);data.colours.Add(new Color32(60,106,148,255));}
            data.triangles.AddRange(new[]{corner,corner+1,corner+2,corner,corner+2,corner+3});
            var go=new GameObject("진해 땅 (먼 곳)");go.transform.SetParent(transform,false);
            var mesh=data.Build(go.name);mesh.RecalculateNormals();
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;far=go.transform;
        }

        // A couple of people on each new fine chunk's pavements; they stroll between pavement points.
        void SpawnPeople(Vector2Int key,Piece piece)
        {
            if(people==null||people.Length==0)return;
            walkers.RemoveAll(p=>p.body==null);int n=piece.n,row=n+2;
            for(int k=0;k<2&&walkers.Count<MaxPeople;k++)
                for(int attempt=0;attempt<40;attempt++)
                {
                    int i=random.Next(n),j=random.Next(n);int at=(i+1)+(j+1)*row;
                    if(piece.kind[at]!=Earth&&piece.kind[at]!=Plaza)continue;
                    var p=new Vector3(key.x*Chunk+(i+.5f)*piece.cell,piece.height[at],key.y*Chunk+(j+.5f)*piece.cell);
                    var body=new GameObject("진해 사람");body.transform.SetParent(piece.go.transform,false);body.transform.position=p;
                    body.AddComponent<MeshFilter>().sharedMesh=people[random.Next(people.Length)];body.AddComponent<MeshRenderer>().sharedMaterial=material;
                    walkers.Add(new Person{body=body.transform,target=p,speed=1.1f+(float)random.NextDouble()*.5f,wait=(float)random.NextDouble()*2f});
                    break;
                }
        }
        // A pavement cell a person can step onto from `from` (at most one terrain step up or down).
        bool Walkable(Vector3 p,float from,out float height)
        {
            byte kind;return Sample(p,out height,out kind)&&(kind==Earth||kind==Plaza)&&Mathf.Abs(height-from)<.3f;
        }
        public void MovePeople(float dt)
        {
            for(int n=walkers.Count-1;n>=0;n--)
            {
                var person=walkers[n];if(person.body==null){walkers.RemoveAt(n);continue;}
                if(person.wait>0){person.wait-=dt;continue;}
                var at=person.body.position;var to=person.target-at;to.y=0;
                if(to.magnitude<.3f){person.target=PickTarget(at);person.wait=(float)random.NextDouble()*3f;continue;}
                var next=at+to.normalized*Mathf.Min(to.magnitude,person.speed*dt);float h;
                if(!Walkable(next,at.y,out h)){person.target=at;continue;}
                next.y=h+Mathf.Abs(Mathf.Sin(Time.time*person.speed*6f))*.04f; // a boxy bob in step
                person.body.SetPositionAndRotation(next,Quaternion.LookRotation(to));
            }
        }
        Vector3 PickTarget(Vector3 from)
        {
            for(int attempt=0;attempt<8;attempt++)
            {
                float angle=(float)random.NextDouble()*Mathf.PI*2,reach=6f+(float)random.NextDouble()*20f;
                var to=from+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*reach;bool clear=true;float y=from.y,h;
                for(float t=1;t<=reach&&clear;t+=1f){clear=Walkable(Vector3.Lerp(from,to,t/reach),y,out h);y=h;}
                if(clear)return to;
            }
            return from;
        }
    }
}
