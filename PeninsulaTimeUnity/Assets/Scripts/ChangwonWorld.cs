using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace PeninsulaTime
{
    // Streams the whole of Changwon around a focus transform: 512 m detail chunks (terrain, roads with markings and
    // sidewalks, bridges with piers, tunnels, buildings, trees, street lamps, lakes) near the focus, and 4 km far tiles
    // at 64 m resolution with the tall-building skyline everywhere else. Geometry is generated on a worker thread.
    public class ChangwonWorld : MonoBehaviour
    {
        public static ChangwonWorld Active;
        public Transform focus;
        public int detailRings=2,colliderRings=1;
        public float drawDistance=9000f;
        public int ChunksLoaded{get{return chunks.Count;}} public int FarTilesLoaded{get{return farTiles.Count;}}
        public static long BuildMillis;public static int Builds; // worker time spent on detail chunks (QA)
        public bool Ready{get{Chunk c;var k=ChangwonData.ChunkOf(FocusPos.x,FocusPos.z);if(!chunks.TryGetValue(Key(k.x,k.y),out c)||c.go==null||!c.collider)return false;foreach(var m in c.colliders)if(m!=null&&m.sharedMesh==null)return false;return true;}}
        public event Action<int,int,GameObject> ChunkShown; // cx, cz, chunk root (detail level 0)
        public event Action<int,int> ChunkHidden;
        public Vector3 FocusPos{get{return focus!=null?focus.position:Vector3.zero;}}

        const int FarTileChunks=8; // 4096 m
        const int TerrainSub=0,RoadMarked=0,RoadPlain=1,Pavement=2,Concrete=3,Steel=4,Rail=5,Lamp=6,RoadSubs=7;
        const int WallHouse=0,WallApartment=1,WallVilla=2,WallGlass=3,WallGrey=4,WallFactory=5,WallBrick=6,WallShed=7,Roof=8,RoofDark=9,BuildingSubs=10;

        class Chunk {public int cx,cz,lod=-1,wanted;public bool pending,collider;public GameObject go;public MeshCollider[] colliders=new MeshCollider[0];public long stamp;}
        class FarTile {public int tx,tz;public GameObject go;public bool pending;public string signature="";}
        class Job {public int cx,cz,lod;public bool far;public HashSet<long> exclude;public string signature;}
        class Label {public string text;public Vector3 pos,facing;public float size;public Color color;}
        class Result {public Job job;public MeshBuild terrain,roads,buildings,water,props;public List<Label> labels;}

        readonly Dictionary<long,Chunk> chunks=new Dictionary<long,Chunk>();
        readonly Dictionary<long,FarTile> farTiles=new Dictionary<long,FarTile>();
        readonly Queue<Job> jobs=new Queue<Job>();readonly Queue<Result> results=new Queue<Result>();
        readonly Queue<KeyValuePair<MeshCollider,Mesh>> baked=new Queue<KeyValuePair<MeshCollider,Mesh>>();
        readonly object gate=new object();Thread worker;volatile bool stopping;
        Transform rootT,chunkRoot,farRoot;Material terrainMat,waterMat,seaMat,signMat,glowMat;Material[] roadMats,buildingMats;
        Texture dayFacadeTex;Texture2D litFacade;bool nightLit;readonly Vector2[] dayScale=new Vector2[BuildingSubs];readonly Texture[] dayTex=new Texture[BuildingSubs];readonly Color[] dayColor=new Color[BuildingSubs];
        GameObject sea;Font signFont;Light moon;Vector2Int lastCenter=new Vector2Int(int.MinValue,0);float nextScan;

        static long Key(int a,int b){return ((long)a<<32)^(uint)b;}

        public void Init(Transform parent,Transform focusTransform){
            Active=this;focus=focusTransform;rootT=new GameObject("창원 오픈월드").transform;rootT.SetParent(parent,false);
            chunkRoot=new GameObject("상세 구역").transform;chunkRoot.SetParent(rootT,false);
            farRoot=new GameObject("원경").transform;farRoot.SetParent(rootT,false);
            CreateMaterials();
            sea=GameObject.CreatePrimitive(PrimitiveType.Plane);sea.name="바다 (마산만·진해만)";Destroy(sea.GetComponent<Collider>());
            sea.transform.SetParent(rootT,false);sea.transform.localScale=new Vector3(3000,1,3000);sea.GetComponent<Renderer>().sharedMaterial=seaMat;
            sea.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            if(PerformanceRuntime.Level==0){detailRings=1;drawDistance=6000;}
            // A soft fill light so streets stay readable at night (the sun/moon cycle alone leaves the city nearly black).
            moon=new GameObject("밤 보조광").AddComponent<Light>();moon.transform.SetParent(rootT,false);moon.type=LightType.Directional;
            moon.transform.rotation=Quaternion.Euler(55,140,0);moon.color=new Color(.55f,.65f,1f);moon.intensity=0;moon.shadows=LightShadows.None;
            stopping=false;worker=new Thread(Work){IsBackground=true,Name="Changwon chunk builder"};worker.Start();
        }
        void OnDestroy(){stopping=true;lock(gate)Monitor.PulseAll(gate);if(Active==this)Active=null;}

        Material NewMat(Color color,string texture,float tiling,float smooth=.2f,float metal=0){
            var baseMaterial=Resources.Load<Material>("RuntimeBase");
            var m=baseMaterial!=null?new Material(baseMaterial):new Material(Shader.Find("Standard"));
            m.color=color;if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",smooth);if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",smooth);if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",metal);
            if(texture!=null){var t=Resources.Load<Texture2D>("Textures/City/"+texture);if(t!=null){m.mainTexture=t;m.mainTextureScale=Vector2.one*tiling;}}
            return m;
        }
        void CreateMaterials(){
            var vc=Shader.Find("Peninsula/VertexTerrain");
            var unlit=Shader.Find("Peninsula/FloorGuide");glowMat=unlit!=null?new Material(unlit):null;if(glowMat!=null)glowMat.name="창원 불빛 (조명 무관)";
            litFacade=Resources.Load<Texture2D>("Textures/City/facade_lit");
            terrainMat=vc!=null?new Material(vc):NewMat(new Color(.4f,.5f,.35f),null,1);terrainMat.name="창원 지형 (정점 색)";
            waterMat=NewMat(new Color(.12f,.25f,.30f),null,1,.78f);waterMat.name="호수·하천";
            seaMat=NewMat(new Color(.09f,.21f,.29f),null,1,.8f);seaMat.name="바다";
            roadMats=new[]{NewMat(Color.white,"osm_road",1,.12f),NewMat(new Color(.80f,.80f,.80f),"asphalt",1,.1f),NewMat(new Color(.60f,.59f,.56f),"pavement",1),
                NewMat(new Color(.62f,.62f,.60f),"concrete",1),NewMat(new Color(.42f,.45f,.48f),null,1,.5f,.6f),NewMat(new Color(.36f,.33f,.30f),"stone",1),
                glowMat??(vc!=null?terrainMat:NewMat(Color.white,null,1))};
            buildingMats=new[]{NewMat(new Color(.93f,.91f,.87f),"facade",1),NewMat(Color.white,"facade_white",1),NewMat(Color.white,"facade_beige",1),
                NewMat(Color.white,"facade_glass",1,.6f,.2f),NewMat(Color.white,"facade_grey",1),NewMat(new Color(.82f,.86f,.92f),"factory_blue",1),
                NewMat(Color.white,"facade_brick",1),NewMat(new Color(.62f,.64f,.66f),"steel_blue",1,.4f,.3f),NewMat(new Color(.66f,.67f,.66f),"roof_flat",1),NewMat(new Color(.40f,.44f,.47f),"roof_grey",1)};
            var signShader=Shader.Find("Peninsula/SignText");
            signFont=Font.CreateDynamicFontFromOSFont(new[]{"Apple SD Gothic Neo","Malgun Gothic","Noto Sans CJK KR","Noto Sans KR","NanumGothic","Arial Unicode MS"},64);
            signMat=signShader!=null?new Material(signShader):signFont.material;if(signMat!=null&&signFont!=null)signMat.mainTexture=signFont.material.mainTexture;
        }

        void Update(){
            if(!ChangwonData.Loaded||focus==null)return;
            var p=FocusPos;
            sea.transform.position=new Vector3(Mathf.Round(p.x/100)*100,0,Mathf.Round(p.z/100)*100);
            if(Time.unscaledTime>=nextScan){nextScan=Time.unscaledTime+.25f;Scan(p);}
            if(DayCycle.Night!=nightLit)SetNight(DayCycle.Night);
            if(moon!=null)moon.intensity=Mathf.MoveTowards(moon.intensity,DayCycle.Night?.42f:0f,Time.deltaTime*.2f);
            while(true){KeyValuePair<MeshCollider,Mesh> b;lock(gate){if(baked.Count==0)break;b=baked.Dequeue();}if(b.Key!=null){b.Key.sharedMesh=b.Value;if(b.Key.enabled)Physics.SyncTransforms();}}
            float budget=Time.realtimeSinceStartup+.008f;
            while(Time.realtimeSinceStartup<budget){Result r;lock(gate){if(results.Count==0)break;r=results.Dequeue();}Apply(r);}
        }

        // Decide which chunks should exist at which level, and which far tiles need rebuilding.
        void Scan(Vector3 p){
            var c=ChangwonData.ChunkOf(p.x,p.z);
            var wanted=new Dictionary<long,int>();
            for(int dz=-detailRings-1;dz<=detailRings+1;dz++)for(int dx=-detailRings-1;dx<=detailRings+1;dx++){
                int cx=c.x+dx,cz=c.y+dz;if(cx<0||cz<0||cx>=ChangwonData.CX||cz>=ChangwonData.CZ)continue;
                var min=ChangwonData.ChunkMin(cx,cz);float size=ChangwonData.ChunkSize;
                float ddx=Mathf.Max(0,Mathf.Max(min.x-p.x,p.x-(min.x+size))),ddz=Mathf.Max(0,Mathf.Max(min.z-p.z,p.z-(min.z+size)));
                float d=Mathf.Sqrt(ddx*ddx+ddz*ddz);
                int lod=d<size*.55f?0:d<size*(detailRings+.15f)?1:-1;
                if(lod>=0)wanted[Key(cx,cz)]=lod;
            }
            // Drop chunks no longer wanted (keep a one-ring hysteresis so turning around does not rebuild).
            var drop=new List<long>();
            foreach(var pair in chunks){var ch=pair.Value;if(!wanted.ContainsKey(pair.Key)&&(Mathf.Abs(ch.cx-c.x)>detailRings+1||Mathf.Abs(ch.cz-c.y)>detailRings+1))drop.Add(pair.Key);}
            foreach(var k in drop){var ch=chunks[k];if(ch.go!=null){Destroy(ch.go);ChunkHidden?.Invoke(ch.cx,ch.cz);}chunks.Remove(k);}
            // Nearest first.
            var order=new List<KeyValuePair<long,int>>(wanted);
            order.Sort((a,b)=>Dist(a.Key,c).CompareTo(Dist(b.Key,c)));
            foreach(var pair in order){
                Chunk ch;if(!chunks.TryGetValue(pair.Key,out ch)){ch=new Chunk{cx=(int)(pair.Key>>32),cz=Unkey(pair.Key)};chunks[pair.Key]=ch;}
                ch.wanted=pair.Value;
                if(!ch.pending&&ch.lod!=ch.wanted&&!(ch.lod==0&&ch.wanted==1)){ch.pending=true;Enqueue(new Job{cx=ch.cx,cz=ch.cz,lod=ch.wanted});}
                bool wantCollider=Mathf.Abs(ch.cx-c.x)<=colliderRings&&Mathf.Abs(ch.cz-c.y)<=colliderRings;
                if(ch.go!=null&&wantCollider!=ch.collider)SetColliders(ch,wantCollider);
            }
            // Far tiles: rebuild a tile when the set of detail chunks inside it changes.
            int tilesX=(ChangwonData.CX+FarTileChunks-1)/FarTileChunks,tilesZ=(ChangwonData.CZ+FarTileChunks-1)/FarTileChunks;
            var cover=new HashSet<long>();foreach(var pair in chunks)if(pair.Value.go!=null||pair.Value.pending)cover.Add(pair.Key);
            float reach=drawDistance+FarTileChunks*ChangwonData.ChunkSize;
            for(int tz=0;tz<tilesZ;tz++)for(int tx=0;tx<tilesX;tx++){
                var min=ChangwonData.ChunkMin(tx*FarTileChunks,tz*FarTileChunks);float s=FarTileChunks*ChangwonData.ChunkSize;
                float ddx=Mathf.Max(0,Mathf.Max(min.x-p.x,p.x-(min.x+s))),ddz=Mathf.Max(0,Mathf.Max(min.z-p.z,p.z-(min.z+s)));
                long k=Key(tx,tz);FarTile tile;farTiles.TryGetValue(k,out tile);
                if(ddx*ddx+ddz*ddz>reach*reach){if(tile!=null&&tile.go!=null){Destroy(tile.go);farTiles.Remove(k);}continue;}
                var inside=new HashSet<long>();var sig=new System.Text.StringBuilder();
                for(int cz=tz*FarTileChunks;cz<(tz+1)*FarTileChunks;cz++)for(int cx=tx*FarTileChunks;cx<(tx+1)*FarTileChunks;cx++){long ck=Key(cx,cz);if(cover.Contains(ck)){inside.Add(ck);sig.Append(cx).Append(',').Append(cz).Append(';');}}
                if(tile==null){tile=new FarTile{tx=tx,tz=tz,signature=null};farTiles[k]=tile;}
                string signature=sig.ToString();
                if(!tile.pending&&tile.signature!=signature){tile.pending=true;Enqueue(new Job{cx=tx,cz=tz,far=true,exclude=inside,signature=signature});}
            }
            lastCenter=c;
        }
        static int Unkey(long k){return (int)(uint)(k&0xffffffff);}
        static float Dist(long k,Vector2Int c){int x=(int)(k>>32),z=(int)(uint)(k&0xffffffff);return (x-c.x)*(x-c.x)+(z-c.y)*(z-c.y);}
        void Enqueue(Job j){lock(gate){jobs.Enqueue(j);Monitor.Pulse(gate);}}

        void Work(){
            while(!stopping){
                Job j;lock(gate){while(jobs.Count==0&&!stopping)Monitor.Wait(gate);if(stopping)return;j=jobs.Dequeue();}
                Result r;
                try{r=j.far?BuildFar(j):BuildChunk(j);}
                catch(Exception e){Debug.LogError("Changwon chunk "+j.cx+","+j.cz+": "+e);r=new Result{job=j};}
                lock(gate)results.Enqueue(r);
            }
        }

        void Apply(Result r){
            var j=r.job;
            if(j.far){
                FarTile tile;if(!farTiles.TryGetValue(Key(j.cx,j.cz),out tile))return;
                tile.pending=false;tile.signature=j.signature;
                if(tile.go!=null)Destroy(tile.go);
                tile.go=new GameObject("원경 "+j.cx+","+j.cz);tile.go.transform.SetParent(farRoot,false);
                AddPart(tile.go,"지형",r.terrain,new[]{terrainMat},false);AddPart(tile.go,"원경 건물",r.buildings,new[]{terrainMat},false);
                AddPart(tile.go,"호수",r.water,new[]{waterMat},false);
                return;
            }
            Chunk ch;if(!chunks.TryGetValue(Key(j.cx,j.cz),out ch)){return;}
            ch.pending=false;
            if(ch.go!=null){Destroy(ch.go);ChunkHidden?.Invoke(ch.cx,ch.cz);}
            ch.lod=j.lod;ch.collider=false;
            var go=new GameObject("창원 구역 "+j.cx+","+j.cz+(j.lod==0?"":" (중간)"));go.transform.SetParent(chunkRoot,false);ch.go=go;
            var cols=new List<MeshCollider>();
            var t=AddPart(go,"지형",r.terrain,new[]{terrainMat},true);if(t!=null)cols.Add(t);
            var rd=AddPart(go,"도로",r.roads,roadMats,true,RoadMarked,RoadPlain,Pavement,Concrete,Rail);if(rd!=null)cols.Add(rd);
            var b=AddPart(go,"건물",r.buildings,buildingMats,true,0,1,2,3,4,5,6,7,8,9);if(b!=null)cols.Add(b);
            AddPart(go,"호수",r.water,new[]{waterMat},false);
            var pr=AddPart(go,"나무·가로등",r.props,new[]{terrainMat,glowMat??terrainMat},true,0);if(pr!=null)cols.Add(pr);
            ch.colliders=cols.ToArray();foreach(var c in ch.colliders)c.enabled=false;
            if(r.labels!=null&&j.lod==0)foreach(var l in r.labels)MakeLabel(go.transform,l);
            var center=ChangwonData.ChunkOf(FocusPos.x,FocusPos.z);
            SetColliders(ch,Mathf.Abs(ch.cx-center.x)<=colliderRings&&Mathf.Abs(ch.cz-center.y)<=colliderRings);
            if(ch.wanted!=ch.lod&&!(ch.lod==0&&ch.wanted==1)){ch.pending=true;Enqueue(new Job{cx=ch.cx,cz=ch.cz,lod=ch.wanted});}
            if(j.lod==0)ChunkShown?.Invoke(ch.cx,ch.cz,go);
        }
        MeshCollider AddPart(GameObject parent,string name,MeshBuild mb,Material[] mats,bool collide,params int[] colliderSubs){
            if(mb==null||mb.Empty)return null;
            var o=new GameObject(name);o.transform.SetParent(parent.transform,false);
            var mesh=mb.ToMesh(name);o.AddComponent<MeshFilter>().sharedMesh=mesh;var mr=o.AddComponent<MeshRenderer>();
            var use=new Material[mb.sub.Length];for(int i=0;i<use.Length;i++)use[i]=mats[Mathf.Min(i,mats.Length-1)];mr.sharedMaterials=use;
            if(!collide)return null;
            Mesh colliderMesh=colliderSubs.Length==0||colliderSubs.Length>=mb.sub.Length?mesh:mb.ToCollider(name+" 충돌",colliderSubs);
            if(colliderMesh==null)return null;
            // Cook the collision mesh on a worker thread (Physics.BakeMesh), then attach it on the main thread.
            var mc=o.AddComponent<MeshCollider>();mc.enabled=false;int id=colliderMesh.GetInstanceID();
            ThreadPool.QueueUserWorkItem(_=>{try{Physics.BakeMesh(id,false);}catch(Exception){}lock(gate)baked.Enqueue(new KeyValuePair<MeshCollider,Mesh>(mc,colliderMesh));});
            return mc;
        }
        // At night the residential and office walls switch to the lit-window facade and are brightened so towns glow.
        void SetNight(bool night){
            nightLit=night;
            for(int i=0;i<buildingMats.Length;i++){
                var m=buildingMats[i];if(m==null)continue;
                if(dayTex[i]==null&&dayColor[i].a==0){dayTex[i]=m.mainTexture;dayScale[i]=m.mainTextureScale;dayColor[i]=m.color;}
                bool windows=i==WallHouse||i==WallApartment||i==WallVilla||i==WallGrey||i==WallBrick;
                if(night){
                    if(windows&&litFacade!=null){m.mainTexture=litFacade;m.mainTextureScale=i==WallHouse?Vector2.one:new Vector2(4f,4f);}
                    m.color=i==Roof||i==RoofDark?dayColor[i]*.8f:windows?new Color(2.3f,2.1f,1.7f):dayColor[i]*1.6f;
                }else{m.mainTexture=dayTex[i];m.mainTextureScale=dayScale[i];m.color=dayColor[i];}
            }
        }
        void SetColliders(Chunk ch,bool on){ch.collider=on;foreach(var c in ch.colliders)if(c!=null)c.enabled=on;if(on)Physics.SyncTransforms();}
        void MakeLabel(Transform parent,Label l){
            var go=new GameObject("간판 "+l.text);go.transform.SetParent(parent,false);go.transform.position=l.pos;
            go.transform.rotation=Quaternion.LookRotation(-l.facing,Vector3.up);
            var tm=go.AddComponent<TextMesh>();tm.text=l.text;tm.font=signFont;tm.fontSize=64;tm.characterSize=l.size*10f/64f*1.4f;tm.anchor=TextAnchor.MiddleCenter;tm.alignment=TextAlignment.Center;tm.color=l.color;
            var mr=go.GetComponent<MeshRenderer>();if(signMat!=null)mr.sharedMaterial=signMat;else if(signFont!=null)mr.sharedMaterial=signFont.material;
        }

        // ------------------------------------------------------------------ colours
        static Color32 LandColor(byte cls,float slope,float h,uint hash){
            Color c;
            switch(cls){
                case ChangwonData.Sea:c=new Color(.42f,.40f,.32f);break;
                case ChangwonData.Water:c=new Color(.30f,.33f,.28f);break;
                case ChangwonData.Forest:c=new Color(.21f,.33f,.17f);break;
                case ChangwonData.Grass:c=new Color(.33f,.45f,.24f);break;
                case ChangwonData.Farm:c=new Color(.60f,.56f,.31f);break; // October: rice paddies ripening
                case ChangwonData.Orchard:c=new Color(.38f,.48f,.24f);break;
                case ChangwonData.Residential:c=new Color(.55f,.54f,.50f);break;
                case ChangwonData.Commercial:c=new Color(.47f,.47f,.46f);break;
                case ChangwonData.Industrial:c=new Color(.52f,.52f,.52f);break;
                case ChangwonData.Sand:c=new Color(.80f,.74f,.58f);break;
                case ChangwonData.Rock:c=new Color(.50f,.48f,.44f);break;
                case ChangwonData.Wetland:c=new Color(.34f,.44f,.30f);break;
                case ChangwonData.Golf:c=new Color(.32f,.52f,.27f);break;
                case ChangwonData.Cemetery:c=new Color(.45f,.55f,.33f);break;
                case ChangwonData.Campus:c=new Color(.54f,.55f,.46f);break;
                case ChangwonData.Pitch:c=new Color(.28f,.50f,.28f);break;
                default:c=new Color(.53f,.52f,.49f);break;
            }
            if(slope>.75f&&cls!=ChangwonData.Sea)c=Color.Lerp(c,new Color(.45f,.43f,.38f),Mathf.Clamp01((slope-.75f)*1.5f));
            c*=.86f; // Lambert under the bright sun washes colours out
            float jitter=((hash&255)/255f-.5f)*.08f;c.r+=jitter;c.g+=jitter*1.2f;c.b+=jitter*.6f;
            return c;
        }
        static uint Hash(int a,int b){unchecked{uint h=(uint)(a*73856093)^(uint)(b*19349663);h^=h>>13;h*=0x5bd1e995;h^=h>>15;return h;}}

        // ------------------------------------------------------------------ terrain
        // Tunnel mouths: a corridor from 12 m outside to 30 m inside each portal where the hillside is above the road.
        class Portal {public Vector3 at,dir;public float half;}
        static List<Portal> Portals(int cx,int cz){
            var list=new List<Portal>();
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++){
                int x=cx+dx,z=cz+dz;if(x<0||z<0||x>=ChangwonData.CX||z>=ChangwonData.CZ)continue;
                foreach(int id in ChangwonData.RoadsInChunk[z*ChangwonData.CX+x]){
                    var r=ChangwonData.Roads[id];
                    for(int k=1;k<r.pts.Length;k++){
                        bool into=r.kind[k-1]!=ChangwonData.Tunnel&&r.kind[k]==ChangwonData.Tunnel,outOf=r.kind[k-1]==ChangwonData.Tunnel&&r.kind[k]!=ChangwonData.Tunnel;
                        if(!into&&!outOf)continue;
                        var at=into?r.pts[k]:r.pts[k-1];var dir=(into?r.pts[Mathf.Min(k+1,r.pts.Length-1)]-r.pts[k]:r.pts[k-2>=0?k-2:0]-r.pts[k-1]);dir.y=0;
                        if(dir.sqrMagnitude<.01f){dir=r.pts[k]-r.pts[k-1];dir.y=0;if(!into)dir=-dir;}
                        list.Add(new Portal{at=at,dir=dir.normalized,half=r.width*.5f+3.5f});
                    }
                }
            }
            return list;
        }
        static bool InPortal(List<Portal> portals,float x,float z,float y){
            if(portals==null)return false;
            foreach(var p in portals){
                float ax=x-p.at.x,az=z-p.at.z;float along=ax*p.dir.x+az*p.dir.z;if(along<-12f||along>30f)continue;
                float side=Mathf.Abs(ax*p.dir.z-az*p.dir.x);if(side>p.half)continue;
                if(y>p.at.y+1.5f)return true;
            }
            return false;
        }
        static void Terrain(MeshBuild mb,int i0,int j0,int cells,int step,bool skirt,HashSet<long> exclude=null,int excludeCells=0,List<Portal> portals=null){
            int n=cells/step+1;var index=new int[n*n];
            for(int b=0;b<n;b++)for(int a=0;a<n;a++){
                int i=i0+a*step,j=j0+b*step;float y=ChangwonData.GridHeight(i,j);
                float hx=ChangwonData.GridHeight(i+step,j)-ChangwonData.GridHeight(i-step,j),hz=ChangwonData.GridHeight(i,j+step)-ChangwonData.GridHeight(i,j-step);
                float d=2*step*ChangwonData.Cell;var normal=new Vector3(-hx/d,1,-hz/d).normalized;
                index[b*n+a]=mb.Vertex(new Vector3(ChangwonData.X0+i*ChangwonData.Cell,y,ChangwonData.Z0+j*ChangwonData.Cell),normal,Vector2.zero,LandColor(ChangwonData.GridLand(i,j),1-normal.y>0?Mathf.Sqrt(hx*hx+hz*hz)/d:0,y,Hash(i,j)));
            }
            for(int b=0;b<n-1;b++)for(int a=0;a<n-1;a++){
                if(exclude!=null){int ci=(i0+a*step)/excludeCells,cj=(j0+b*step)/excludeCells;if(exclude.Contains(Key(ci,cj)))continue;}
                int p00=index[b*n+a],p10=index[b*n+a+1],p01=index[(b+1)*n+a],p11=index[(b+1)*n+a+1];
                if(portals!=null&&portals.Count>0){var c=(mb.v[p00]+mb.v[p11])*.5f;if(InPortal(portals,c.x,c.z,Mathf.Max(mb.v[p00].y,mb.v[p11].y)))continue;}
                mb.Tri(TerrainSub,p00,p01,p10);mb.Tri(TerrainSub,p10,p01,p11);
            }
            if(!skirt)return;
            // Skirts hide cracks against coarser neighbours.
            int[][] edges={new int[n],new int[n],new int[n],new int[n]};
            for(int k=0;k<n;k++){edges[0][k]=index[k];edges[1][k]=index[(n-1)*n+(n-1-k)];edges[2][k]=index[(n-1-k)*n];edges[3][k]=index[k*n+n-1];}
            foreach(var e in edges)for(int k=0;k<n-1;k++){
                int a=e[k],b=e[k+1];Vector3 pa=mb.v[a],pb=mb.v[b];
                int da=mb.Vertex(pa-Vector3.up*20,mb.n[a],Vector2.zero,mb.c[a]),db=mb.Vertex(pb-Vector3.up*20,mb.n[b],Vector2.zero,mb.c[b]);
                mb.Tri(TerrainSub,a,b,da);mb.Tri(TerrainSub,b,db,da);mb.Tri(TerrainSub,a,da,b);mb.Tri(TerrainSub,b,da,db);
            }
        }

        // ------------------------------------------------------------------ detail chunk
        Result BuildChunk(Job j){
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var r=new Result{job=j,terrain=new MeshBuild(1),roads=new MeshBuild(RoadSubs),buildings=new MeshBuild(BuildingSubs),water=new MeshBuild(1),props=new MeshBuild(2),labels=new List<Label>()};
            int cells=Mathf.RoundToInt(ChangwonData.ChunkSize/ChangwonData.Cell);
            var portals=Portals(j.cx,j.cz);
            Terrain(r.terrain,j.cx*cells,j.cz*cells,cells,j.lod==0?1:2,true,null,0,portals);
            foreach(var pt in portals)if(pt.at.x>=ChangwonData.ChunkMin(j.cx,j.cz).x&&pt.at.x<ChangwonData.ChunkMin(j.cx,j.cz).x+ChangwonData.ChunkSize&&pt.at.z>=ChangwonData.ChunkMin(j.cx,j.cz).z&&pt.at.z<ChangwonData.ChunkMin(j.cx,j.cz).z+ChangwonData.ChunkSize)PortalFrame(r.roads,pt);
            var min=ChangwonData.ChunkMin(j.cx,j.cz);float size=ChangwonData.ChunkSize;
            Func<Vector3,bool> mine=p=>p.x>=min.x&&p.x<min.x+size&&p.z>=min.z&&p.z<min.z+size;
            var rnd=new System.Random(j.cx*7919+j.cz);
            Roads(r,j,mine,rnd);
            Buildings(r,j,rnd);
            Lakes(r.water,min,size);
            if(j.lod==0)Trees(r.props,j,cells,rnd);
            BuildMillis+=watch.ElapsedMilliseconds;Builds++;
            return r;
        }

        static readonly int[] RoadRank={8,7,6,5,4,3,2,1,0,0,0};
        void Roads(Result r,Job j,Func<Vector3,bool> mine,System.Random rnd){
            var ids=ChangwonData.RoadsInChunk[j.cz*ChangwonData.CX+j.cx];var mb=r.roads;var junctions=new HashSet<int>();
            foreach(int id in ids){
                var road=ChangwonData.Roads[id];var P=road.pts;int n=P.Length;
                if(j.lod>0&&road.cls>=ChangwonData.Track&&road.cls<ChangwonData.Rail)continue;
                float half=road.width*.5f,lift=.06f+RoadRank[road.cls]*.012f;
                bool marked=road.cls<=ChangwonData.Tertiary&&road.width>=9;
                int sub=road.IsRail?Rail:road.cls==ChangwonData.Footway?Pavement:marked?RoadMarked:RoadPlain;
                // u range: the road texture carries four lanes; two-lane roads use its middle half.
                float u0=marked&&road.width<13?.25f:0,u1=marked&&road.width<13?.75f:1;
                var right=new Vector3[n];
                for(int k=0;k<n;k++){
                    Vector3 before=(P[k]-P[Mathf.Max(0,k-1)]),after=(P[Mathf.Min(n-1,k+1)]-P[k]);before.y=0;after.y=0;
                    if(k==0)before=after;if(k==n-1)after=before;before.Normalize();after.Normalize();
                    var dir=(before+after);if(dir.sqrMagnitude<1e-4f)dir=after;dir.Normalize();
                    var rt=new Vector3(dir.z,0,-dir.x);float miter=Mathf.Max(.35f,Vector3.Dot(rt,new Vector3(after.z,0,-after.x)));right[k]=rt/miter;
                }
                bool urban=false;
                for(int k=0;k<n-1;k++){
                    Vector3 a=P[k],b=P[k+1];if(!mine((a+b)*.5f))continue;
                    {var mid=(a+b)*.5f;byte lc=ChangwonData.LandAt(mid.x,mid.z);urban=lc==ChangwonData.Residential||lc==ChangwonData.Commercial||lc==ChangwonData.Urban||lc==ChangwonData.Campus||lc==ChangwonData.Industrial;}
                    var up=Vector3.up*lift;
                    Vector3 al=a-right[k]*half+up,ar=a+right[k]*half+up,bl=b-right[k+1]*half+up,br=b+right[k+1]*half+up;
                    float va=road.along[k]/12f,vb=road.along[k+1]/12f;
                    mb.Quad(sub,al,bl,br,ar,new Vector2(u0,va),new Vector2(u0,vb),new Vector2(u1,vb),new Vector2(u1,va),new Color32(255,255,255,255));
                    bool bridge=road.kind[k]==ChangwonData.Bridge||road.kind[k+1]==ChangwonData.Bridge;
                    bool tunnel=road.kind[k]==ChangwonData.Tunnel&&road.kind[k+1]==ChangwonData.Tunnel;
                    if(road.IsRail)RailTrack(mb,a,b,right[k],right[k+1],lift);
                    if(bridge)BridgeSegment(mb,road,k,al,ar,bl,br,right[k],right[k+1],rnd);
                    else if(tunnel&&j.lod==0)TunnelSegment(mb,al,ar,bl,br);
                    else if(urban&&road.Drivable&&road.cls>=ChangwonData.Primary&&road.cls<=ChangwonData.Local&&j.lod==0){
                        Sidewalk(mb,a,b,right[k],right[k+1],half,lift,1);Sidewalk(mb,a,b,right[k],right[k+1],half,lift,-1);
                        if(road.cls<=ChangwonData.Tertiary&&(int)(road.along[k]/32)!=(int)(road.along[k+1]/32))StreetLamp(r.props,b,right[k+1],half+1.6f);
                    }
                }
                if(road.Drivable){if(mine(P[0]))junctions.Add(road.a);if(mine(P[n-1]))junctions.Add(road.b);}
            }
            // Junction pads fill the gaps where ribbons meet.
            foreach(int node in junctions){
                var list=ChangwonData.NodeRoads[node];if(list==null||list.Count<3)continue;
                float w=0,y=float.MinValue;Vector3 at=Vector3.zero;bool ground=true;
                foreach(int ri in list){var rr=ChangwonData.Roads[ri];if(!rr.Drivable)continue;w=Mathf.Max(w,rr.width);var p=rr.a==node?rr.pts[0]:rr.pts[rr.pts.Length-1];at=p;y=Mathf.Max(y,p.y);if((rr.a==node?rr.kind[0]:rr.kind[rr.kind.Length-1])!=ChangwonData.Ground)ground=false;}
                if(w<=0||!ground)continue;
                float radius=w*.62f;var c=new Vector3(at.x,y+.05f,at.z);int center=mb.Vertex(c,Vector3.up,new Vector2(.5f,.5f),new Color32(255,255,255,255));
                for(int s=0;s<=16;s++){float ang=-s*Mathf.PI*2/16;mb.Vertex(c+new Vector3(Mathf.Cos(ang),0,Mathf.Sin(ang))*radius,Vector3.up,new Vector2(.5f+Mathf.Cos(ang)*.1f,.5f+Mathf.Sin(ang)*.1f),new Color32(255,255,255,255));}
                for(int s=0;s<16;s++)mb.Tri(RoadPlain,center,center+1+s,center+2+s);
            }
        }
        static void Sidewalk(MeshBuild mb,Vector3 a,Vector3 b,Vector3 ra,Vector3 rb,float half,float lift,int side){
            float inner=half,outer=half+2.6f;var up=Vector3.up*(lift+.16f);var w=new Color32(255,255,255,255);
            Vector3 ai=a+ra*inner*side+up,ao=a+ra*outer*side+up,bi=b+rb*inner*side+up,bo=b+rb*outer*side+up;
            float len=Vector3.Distance(a,b)/3f;
            if(side>0)mb.Quad(Pavement,ai,bi,bo,ao,Vector2.zero,new Vector2(0,len),new Vector2(1,len),new Vector2(1,0),w);
            else mb.Quad(Pavement,ao,bo,bi,ai,Vector2.zero,new Vector2(0,len),new Vector2(1,len),new Vector2(1,0),w);
            // Kerb face toward the carriageway.
            var down=Vector3.up*.18f;
            if(side>0)mb.Quad(Concrete,ai-down,ai,bi,bi-down,Vector2.zero,new Vector2(0,.1f),new Vector2(len,.1f),new Vector2(len,0),w);
            else mb.Quad(Concrete,bi-down,bi,ai,ai-down,Vector2.zero,new Vector2(0,.1f),new Vector2(len,.1f),new Vector2(len,0),w);
        }
        static void RailTrack(MeshBuild mb,Vector3 a,Vector3 b,Vector3 ra,Vector3 rb,float lift){
            var w=new Color32(255,255,255,255);
            foreach(float off in new[]{-.72f,.72f}){
                Vector3 a0=a+ra*(off-.05f)+Vector3.up*(lift+.18f),a1=a+ra*(off+.05f)+Vector3.up*(lift+.18f),b0=b+rb*(off-.05f)+Vector3.up*(lift+.18f),b1=b+rb*(off+.05f)+Vector3.up*(lift+.18f);
                mb.Quad(Steel,a0,b0,b1,a1,Vector2.zero,Vector2.up,Vector2.one,Vector2.right,w);
                mb.Quad(Steel,a0-Vector3.up*.16f,b0-Vector3.up*.16f,b0,a0,Vector2.zero,Vector2.up,Vector2.one,Vector2.right,w);
                mb.Quad(Steel,a1,b1,b1-Vector3.up*.16f,a1-Vector3.up*.16f,Vector2.zero,Vector2.up,Vector2.one,Vector2.right,w);
            }
        }
        static void BridgeSegment(MeshBuild mb,ChangwonData.Road road,int k,Vector3 al,Vector3 ar,Vector3 bl,Vector3 br,Vector3 ra,Vector3 rb,System.Random rnd){
            var w=new Color32(255,255,255,255);var down=Vector3.up*1.4f;float len=Vector3.Distance(al,bl)/4f;
            // Deck underside and edge beams.
            mb.Quad(Concrete,ar-down,br-down,bl-down,al-down,Vector2.zero,new Vector2(0,len),new Vector2(1,len),new Vector2(1,0),w);
            mb.Quad(Concrete,al-down,bl-down,bl,al,Vector2.zero,new Vector2(len,0),new Vector2(len,.3f),new Vector2(0,.3f),w);
            mb.Quad(Concrete,ar,br,br-down,ar-down,Vector2.zero,new Vector2(len,0),new Vector2(len,.3f),new Vector2(0,.3f),w);
            // Parapets.
            var rail=Vector3.up*1.05f;
            mb.Quad(Concrete,bl,bl+rail,al+rail,al,Vector2.zero,new Vector2(0,.25f),new Vector2(len,.25f),new Vector2(len,0),w);
            mb.Quad(Concrete,al,al+rail,bl+rail,bl,Vector2.zero,new Vector2(0,.25f),new Vector2(len,.25f),new Vector2(len,0),w);
            mb.Quad(Concrete,ar,ar+rail,br+rail,br,Vector2.zero,new Vector2(0,.25f),new Vector2(len,.25f),new Vector2(len,0),w);
            mb.Quad(Concrete,br,br+rail,ar+rail,ar,Vector2.zero,new Vector2(0,.25f),new Vector2(len,.25f),new Vector2(len,0),w);
            // Piers about every 40 m, down to the ground or sea bed.
            float along=road.along[k],next=road.along[k+1];
            for(float s=Mathf.Ceil(along/40f)*40f;s<next;s+=40f){
                Vector3 f;var p=road.At(s,out f);float ground=Mathf.Max(ChangwonData.Height(p.x,p.z),-12f);float top=p.y-1.4f;
                if(top-ground<2.5f)continue;
                float wid=Mathf.Max(3f,road.width*.45f);
                mb.Box(Concrete,new Vector3(p.x,(top+ground)*.5f,p.z),new Vector3(1.8f,top-ground,1.8f),Quaternion.LookRotation(f.sqrMagnitude>0?new Vector3(f.x,0,f.z):Vector3.forward),w);
                mb.Box(Concrete,new Vector3(p.x,top-.5f,p.z),new Vector3(wid*2,1f,2.2f),Quaternion.LookRotation(f.sqrMagnitude>0?new Vector3(f.x,0,f.z):Vector3.forward),w);
            }
        }
        static void TunnelSegment(MeshBuild mb,Vector3 al,Vector3 ar,Vector3 bl,Vector3 br){
            var w=new Color32(255,255,255,255);var up=Vector3.up*6.5f;float len=Vector3.Distance(al,bl)/4f;
            mb.Quad(Concrete,bl,bl+up,al+up,al,Vector2.zero,new Vector2(0,1.6f),new Vector2(len,1.6f),new Vector2(len,0),w);
            mb.Quad(Concrete,ar,ar+up,br+up,br,Vector2.zero,new Vector2(0,1.6f),new Vector2(len,1.6f),new Vector2(len,0),w);
            mb.Quad(Concrete,al+up,bl+up,br+up,ar+up,Vector2.zero,new Vector2(0,len),new Vector2(1,len),new Vector2(1,0),w);
            // Ceiling lamps every segment.
            Vector3 m=(al+ar+bl+br)*.25f+up-Vector3.up*.15f;
            mb.Box(Lamp,m,new Vector3(.6f,.1f,1.2f),Quaternion.LookRotation((bl-al).sqrMagnitude>0?bl-al:Vector3.forward),new Color32(255,236,190,255));
        }
        // Concrete portal face around a tunnel mouth.
        static void PortalFrame(MeshBuild mb,Portal p){
            var right=new Vector3(p.dir.z,0,-p.dir.x);var w=new Color32(255,255,255,255);float half=p.half-1.5f;var c=p.at+Vector3.up*.05f;
            var rot=Quaternion.LookRotation(p.dir);
            mb.Box(Concrete,c+right*(half+1.2f)+Vector3.up*4f,new Vector3(2.4f,8f,2f),rot,w);
            mb.Box(Concrete,c-right*(half+1.2f)+Vector3.up*4f,new Vector3(2.4f,8f,2f),rot,w);
            mb.Box(Concrete,c+Vector3.up*7.6f,new Vector3(half*2+4.8f,2.2f,2f),rot,w);
        }
        static void StreetLamp(MeshBuild props,Vector3 at,Vector3 right,float offset){
            var p=at+right.normalized*offset;var pole=new Color32(90,96,104,255);
            props.Box(0,p+Vector3.up*4.2f,new Vector3(.18f,8.4f,.18f),Quaternion.identity,pole);
            props.Box(0,p+Vector3.up*8.35f-right.normalized*1.1f,new Vector3(2.2f,.14f,.18f),Quaternion.LookRotation(right),pole);
            props.Box(1,p+Vector3.up*8.2f-right.normalized*2.0f,new Vector3(.5f,.16f,.32f),Quaternion.LookRotation(right),new Color32(255,236,190,255));
        }

        // ------------------------------------------------------------------ buildings
        static int WallMaterial(ChangwonData.Building b,uint h){
            switch(b.kind){
                case 1:return WallApartment;
                case 8:return (h&1)==0?WallVilla:WallBrick;
                case 2:case 10:return b.height>22?WallGlass:(h&1)==0?WallGrey:WallVilla;
                case 3:return WallFactory;
                case 4:return WallVilla;
                case 5:case 9:case 11:return WallGrey;
                case 7:return WallShed;
                default:return (h%3)==0?WallBrick:WallHouse;
            }
        }
        // Metres per texture repeat (u, v) for each wall material.
        static readonly Vector2[] WallRepeat={new Vector2(3f,3.1f),new Vector2(12f,12.4f),new Vector2(12f,12.4f),new Vector2(12f,12.4f),new Vector2(12f,12.8f),new Vector2(8f,8f),new Vector2(12f,12.4f),new Vector2(6f,6f)};
        void Buildings(Result r,Job j,System.Random rnd){
            var list=ChangwonData.BuildingsIn(j.cx,j.cz);var mb=r.buildings;var white=new Color32(255,255,255,255);
            foreach(var b in list){
                var ring=b.ring;int n=ring.Length;if(n<3)continue;
                if(j.lod>0&&b.height<6&&b.kind!=1)continue;
                bool ccw=Polygon.SignedArea(ring)>0;uint h=Hash((int)b.center.x,(int)b.center.y);
                int wall=WallMaterial(b,h);var rep=WallRepeat[wall];
                float y0=b.baseY-1f,y1=b.baseY+b.height;
                float perimeter=0;
                for(int i=0;i<n;i++){
                    // Walk the ring counter-clockwise so every wall faces outward.
                    var A=ccw?ring[i]:ring[n-1-i];var B=ccw?ring[(i+1)%n]:ring[(2*n-2-i)%n];
                    float len=Vector2.Distance(A,B);if(len<.05f)continue;
                    Vector3 a0=new Vector3(A.x,y0,A.y),a1=new Vector3(A.x,y1,A.y),b1=new Vector3(B.x,y1,B.y),b0=new Vector3(B.x,y0,B.y);
                    float u0=perimeter/rep.x,u1=(perimeter+len)/rep.x;perimeter+=len;
                    // Start the facade texture at the ground (floor lines align with the base).
                    float v0=-1f/rep.y,v1=b.height/rep.y;
                    mb.Quad(wall,a0,a1,b1,b0,new Vector2(u0,v0),new Vector2(u0,v1),new Vector2(u1,v1),new Vector2(u1,v0),white);
                }
                // Roof.
                var tris=Polygon.Triangulate(ring);var roofSub=b.kind==3||b.kind==7?RoofDark:Roof;
                int baseIndex=mb.v.Count;
                for(int i=0;i<n;i++)mb.Vertex(new Vector3(ring[i].x,y1,ring[i].y),Vector3.up,new Vector2(ring[i].x/8f,ring[i].y/8f),white);
                for(int t=0;t+2<tris.Count;t+=3)mb.Tri(roofSub,baseIndex+tris[t],baseIndex+tris[t+2],baseIndex+tris[t+1]);
                // Rooftop water tank / stair hut on flat residential roofs, as on Korean apartments and villas.
                if(j.lod==0&&(b.kind==1||b.kind==8)&&b.height>9){
                    var c=new Vector3(b.center.x,y1+1.4f,b.center.y);mb.Box(Roof,c,new Vector3(4f,2.8f,4f),Quaternion.Euler(0,h%90,0),white);
                }
                if(j.lod==0&&!string.IsNullOrEmpty(b.name)&&b.name.Length<=16&&b.height>=8){
                    // Name sign on the longest wall.
                    int best=0;float bestLen=0;for(int i=0;i<n;i++){float l=Vector2.Distance(ring[i],ring[(i+1)%n]);if(l>bestLen){bestLen=l;best=i;}}
                    Vector2 A=ring[best],B=ring[(best+1)%n];var mid=(A+B)*.5f;var along=(B-A).normalized;var outward=ccw?new Vector2(along.y,-along.x):new Vector2(-along.y,along.x);
                    float size=Mathf.Clamp(bestLen/Mathf.Max(4,b.name.Length)*.9f,.8f,3.2f);
                    r.labels.Add(new Label{text=b.name,pos=new Vector3(mid.x+outward.x*.25f,Mathf.Min(y1-size,b.baseY+Mathf.Max(4f,b.height*.82f)),mid.y+outward.y*.25f),facing=new Vector3(outward.x,0,outward.y),size=size,color=b.kind==1?new Color(.20f,.30f,.45f):new Color(.95f,.95f,.92f)});
                }
            }
        }

        // Each lake is drawn by the chunk that holds its first vertex (detail chunk, or the far tile while that chunk is not loaded).
        static void Lakes(MeshBuild mb,Vector3 min,float size){
            foreach(var lake in ChangwonData.Lakes){
                var first=lake.ring[0];if(first.x<min.x||first.x>=min.x+size||first.y<min.z||first.y>=min.z+size)continue;
                var tris=Polygon.Triangulate(lake.ring);int baseIndex=mb.v.Count;
                foreach(var p in lake.ring)mb.Vertex(new Vector3(p.x,lake.surface,p.y),Vector3.up,p/20f,new Color32(255,255,255,255));
                for(int t=0;t+2<tris.Count;t+=3)mb.Tri(0,baseIndex+tris[t],baseIndex+tris[t+2],baseIndex+tris[t+1]);
            }
        }

        void Trees(MeshBuild mb,Job j,int cells,System.Random rnd){
            for(int b=0;b<cells;b++)for(int a=0;a<cells;a++){
                int i=j.cx*cells+a,jj=j.cz*cells+b;byte cls=ChangwonData.GridLand(i,jj);
                int count=cls==ChangwonData.Forest?2:cls==ChangwonData.Grass||cls==ChangwonData.Cemetery||cls==ChangwonData.Golf?(rnd.NextDouble()<.18?1:0):cls==ChangwonData.Orchard?1:0;
                if(cls==ChangwonData.Residential&&rnd.NextDouble()<.05)count=1;
                for(int t=0;t<count;t++){
                    float x=ChangwonData.X0+(i+(float)rnd.NextDouble())*ChangwonData.Cell,z=ChangwonData.Z0+(jj+(float)rnd.NextDouble())*ChangwonData.Cell;
                    Vector3 f;ChangwonData.Road nr;float al;if(ChangwonData.NearestRoad(new Vector3(x,ChangwonData.Height(x,z),z),9f,out nr,out al,out f,false))continue;
                    float y=ChangwonData.Height(x,z);if(y<.9f)continue;
                    float hgt=cls==ChangwonData.Orchard?3.5f:6f+(float)rnd.NextDouble()*7f;bool pine=cls==ChangwonData.Forest&&rnd.NextDouble()<.55;
                    Tree(mb,new Vector3(x,y,z),hgt,pine,rnd);
                }
            }
        }
        static void Tree(MeshBuild mb,Vector3 p,float h,bool pine,System.Random rnd){
            var trunk=new Color32(92,70,50,255);float g=(float)rnd.NextDouble();
            var leaf=pine?new Color32((byte)(34+g*16),(byte)(68+g*22),(byte)(40+g*10),255):new Color32((byte)(58+g*30),(byte)(100+g*35),(byte)(44+g*16),255);
            mb.Box(0,p+Vector3.up*h*.2f,new Vector3(.3f,h*.4f,.3f),Quaternion.Euler(0,g*90,0),trunk);
            if(pine){
                // Two stacked pyramids.
                Cone(mb,p+Vector3.up*h*.25f,h*.32f,h*.55f,leaf);Cone(mb,p+Vector3.up*h*.55f,h*.22f,h*.45f,leaf);
            }else{
                // A rounded crown from two overlapping hexagonal blobs.
                Blob(mb,p+Vector3.up*h*.62f,h*.3f,h*.5f,leaf,g);
                var darker=new Color32((byte)(leaf.r*.85f),(byte)(leaf.g*.88f),(byte)(leaf.b*.85f),255);
                Blob(mb,p+Vector3.up*h*.5f+new Vector3(h*.12f*(g-.5f),0,h*.1f),h*.24f,h*.36f,darker,g+.5f);
            }
        }
        static void Blob(MeshBuild mb,Vector3 center,float radius,float height,Color32 c,float spin){
            var top=center+Vector3.up*height*.5f;var bottom=center-Vector3.up*height*.5f;var ring=new Vector3[7];
            for(int i=0;i<6;i++){float a=(i+spin)*Mathf.PI/3f;ring[i]=center+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius;}ring[6]=ring[0];
            for(int i=0;i<6;i++){
                // Upper and lower faces, each wound to face outward.
                var n=Vector3.Cross(ring[i+1]-ring[i],top-ring[i]).normalized;if(Vector3.Dot(n,ring[i]-center)<0)n=-n;
                int s=mb.Vertex(ring[i],n,Vector2.zero,c);mb.Vertex(top,n,Vector2.zero,c);mb.Vertex(ring[i+1],n,Vector2.zero,c);mb.Tri(0,s,s+1,s+2);
                var m=Vector3.Cross(bottom-ring[i],ring[i+1]-ring[i]).normalized;if(Vector3.Dot(m,ring[i]-center)<0)m=-m;
                var shade=new Color32((byte)(c.r*.8f),(byte)(c.g*.8f),(byte)(c.b*.8f),255);
                int t=mb.Vertex(ring[i],m,Vector2.zero,shade);mb.Vertex(ring[i+1],m,Vector2.zero,shade);mb.Vertex(bottom,m,Vector2.zero,shade);mb.Tri(0,t,t+1,t+2);
            }
        }
        static void Cone(MeshBuild mb,Vector3 b,float radius,float height,Color32 c){
            var top=b+Vector3.up*height;var p=new Vector3[5];
            for(int i=0;i<4;i++){float a=i*Mathf.PI*.5f+.4f;p[i]=b+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius;}p[4]=p[0];
            for(int i=0;i<4;i++){
                var n=Vector3.Cross(p[i+1]-p[i],top-p[i]).normalized;if(n.y<0)n=-n;
                int s=mb.Vertex(p[i],n,Vector2.zero,c);mb.Vertex(top,n,Vector2.zero,c);mb.Vertex(p[i+1],n,Vector2.zero,c);
                // Wind so the outside faces the viewer.
                if(Vector3.Dot(Vector3.Cross(top-p[i],p[i+1]-p[i]),(p[i]+p[i+1])*.5f-b)>0)mb.Tri(0,s,s+1,s+2);else mb.Tri(0,s,s+2,s+1);
            }
        }

        // ------------------------------------------------------------------ far tile
        Result BuildFar(Job j){
            var r=new Result{job=j,terrain=new MeshBuild(1),buildings=new MeshBuild(1),water=new MeshBuild(1)};
            int cells=Mathf.RoundToInt(ChangwonData.ChunkSize/ChangwonData.Cell);
            int tileCells=cells*FarTileChunks;int i0=j.cx*tileCells,j0=j.cz*tileCells;
            int iEnd=Mathf.Min(i0+tileCells,ChangwonData.NX-1),jEnd=Mathf.Min(j0+tileCells,ChangwonData.NZ-1);
            int span=Mathf.Max(iEnd-i0,jEnd-j0);span=Mathf.CeilToInt(span/4f)*4;
            Terrain(r.terrain,i0,j0,Mathf.Min(tileCells,span),4,false,j.exclude,cells);
            for(int cz=j.cz*FarTileChunks;cz<(j.cz+1)*FarTileChunks;cz++)for(int cx=j.cx*FarTileChunks;cx<(j.cx+1)*FarTileChunks;cx++){
                if(j.exclude.Contains(Key(cx,cz)))continue;
                foreach(var b in ChangwonData.BuildingsIn(cx,cz)){
                    if(b.height<13&&!(b.kind==3&&b.ring.Length>3&&Mathf.Abs(Polygon.SignedArea(b.ring))>3000))continue;
                    FarBuilding(r.buildings,b);
                }
                var min=ChangwonData.ChunkMin(cx,cz);Lakes(r.water,min,ChangwonData.ChunkSize);
            }
            return r;
        }
        static void FarBuilding(MeshBuild mb,ChangwonData.Building b){
            var ring=b.ring;int n=ring.Length;bool ccw=Polygon.SignedArea(ring)>0;
            byte shade=(byte)(b.kind==1?222:b.kind==3?168:190);var c=new Color32(shade,shade,(byte)(shade+6),255);
            float y0=b.baseY-2,y1=b.baseY+b.height;
            for(int k=0;k<n;k++){
                var A=ccw?ring[k]:ring[n-1-k];var B=ccw?ring[(k+1)%n]:ring[(2*n-2-k)%n];
                mb.Quad(0,new Vector3(A.x,y0,A.y),new Vector3(A.x,y1,A.y),new Vector3(B.x,y1,B.y),new Vector3(B.x,y0,B.y),Vector2.zero,Vector2.up,Vector2.one,Vector2.right,c);
            }
            var tris=Polygon.Triangulate(ring);int baseIndex=mb.v.Count;
            foreach(var p in ring)mb.Vertex(new Vector3(p.x,y1,p.y),Vector3.up,Vector2.zero,new Color32((byte)(shade-30),(byte)(shade-30),(byte)(shade-26),255));
            for(int t=0;t+2<tris.Count;t+=3)mb.Tri(0,baseIndex+tris[t],baseIndex+tris[t+2],baseIndex+tris[t+1]);
        }
    }
}
