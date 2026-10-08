using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PeninsulaTime
{
    // F on a landmark model (ChangwonThing kind "landmark", payload the Landmark): systems on world.root may claim it
    // (e.g. stamps); otherwise its Korean description is shown.
    public class ChangwonLandmarkInfo : MonoBehaviour, IChangwonUse
    {
        public bool Handles(ChangwonThing thing){return thing!=null&&thing.kind=="landmark";}
        public void Use(ChangwonThing thing)
        {
            if(ChangwonSession.Root!=null)foreach(var h in ChangwonSession.Root.GetComponents<IChangwonUse>())if(h.Handles(thing)){h.Use(thing);return;}
            if(!string.IsNullOrEmpty(thing.detail))ChangwonSession.Toast(thing.detail);
        }
    }

    // Procedural models of the famous Changwon landmarks (창원광장, 마창대교, 솔라타워, 진해탑, the two stadiums, the 용지호수
    // fountain, the 경화역/여좌천 cherry trees, 마산어시장 stalls, summit stones; the 돝섬 pier and boat are ChangwonFerry's), built near the player under
    // ChangwonSession.Root (GameController adds this to world.root on entry) and dropped again when far. Runs before ChangwonWorld so the footprint blocks the models replace
    // are flattened before the first chunk is generated.
    [DefaultExecutionOrder(-50)]
    public class ChangwonLandmarkModels : MonoBehaviour
    {
        public static ChangwonLandmarkModels Instance;
        const int Solid=0,Plain=1,Glow=2; // submeshes: lit + collider, lit, unlit glow (lamps, cables, water jets)
        class Site
        {
            public ChangwonLandmarks.Landmark lm;public System.Action<Site,MeshBuild> build;public float range;public Vector2 at;
            public ChangwonData.Building anchor;public GameObject go;public readonly List<Mesh> meshes=new List<Mesh>();
            public Transform[] parts;public int anim; // anim 1: fountain jets
        }
        readonly List<Site> sites=new List<Site>();Transform holder;float nextScan;static Material lit,glow;
        static readonly Vector3 Up=Vector3.up;

        void Awake(){if(Instance==null)Instance=this;}
        void OnDestroy(){if(Instance!=this)return;Release();Instance=null;}

        void Update()
        {
            if(Instance!=this)return; // a second copy stays idle
            bool on=ChangwonSession.Active&&ChangwonData.Loaded&&ChangwonWorld.Active!=null&&ChangwonSession.Root!=null;
            if(!on||holder==null||holder.parent!=ChangwonSession.Root){if(holder!=null||sites.Count>0)Release();if(!on)return;Prepare();}
            float t=Time.time;
            foreach(var s in sites)if(s.anim!=0&&s.go!=null)Animate(s,t);
            if(Time.unscaledTime<nextScan)return;nextScan=Time.unscaledTime+.5f;
            var p=ChangwonWorld.Active.FocusPos;var here=new Vector2(p.x,p.z);bool built=false;
            foreach(var s in sites){
                float d=(s.at-here).magnitude;
                if(s.go==null&&d<s.range&&!built){Build(s);built=true;} // one build per scan spreads the cost
                else if(s.go!=null&&d>s.range+400f)Drop(s);
            }
        }

        void Prepare()
        {
            holder=new GameObject("명소 모델").transform;holder.SetParent(ChangwonSession.Root,false);holder.gameObject.AddComponent<ChangwonLandmarkInfo>();sites.Clear();
            if(lit==null){
                var vc=Shader.Find("Peninsula/VertexTerrain");lit=new Material(vc!=null?vc:Shader.Find("Standard")){name="창원 명소 (정점 색)"};
                var un=Shader.Find("Peninsula/FloorGuide");glow=un!=null?new Material(un){name="창원 명소 불빛"}:lit;
            }
            bool fast=PerformanceRuntime.Level==0;float near=fast?1600f:2500f,far=fast?4500f:6500f;
            foreach(var lm in ChangwonLandmarks.All){
                var s=new Site{lm=lm,at=lm.pos,range=near};string n=lm.name;
                if(n.Contains("창원광장"))s.build=Plaza;
                else if(n.Contains("마창대교")){s.build=Bridge;s.range=far;}
                else if(n.Contains("솔라타워")){s.build=SolarTower;s.range=far;s.anchor=Anchor(lm.pos,300f,"솔라",0);}
                else if(n.Contains("진해탑")){s.build=JinhaeTower;s.anchor=Anchor(lm.pos,250f,"진해탑",0)??Anchor(lm.pos,250f,"진해박물관",0);}
                else if(n.Contains("NC파크")){s.build=Stadium;s.range=far;s.anchor=Anchor(lm.pos,250f,"NC",0);}
                else if(n.Contains("종합운동장")){s.build=Stadium;s.range=far;s.anchor=Anchor(lm.pos,150f,null,15000f);}
                else if(n.Contains("용지호수"))s.build=Fountain;
                else if(n.Contains("경화역"))s.build=Gyeonghwa;
                else if(n.Contains("여좌천"))s.build=Yeojwa;
                else if(n.Contains("어시장"))s.build=Market;
                else if(lm.kind=="mountain"){s.build=Summit;s.range=fast?1000f:1500f;}
                if(s.build==null)continue;
                // ponytail: lowers the shared footprint block (the model stands in its place); a ChangwonWorld skip-list hook would be cleaner.
                if(s.anchor!=null){s.at=s.anchor.center;s.anchor.height=.2f;}
                sites.Add(s);
            }
        }

        void Build(Site s)
        {
            s.go=new GameObject(s.lm.name+" 모델");s.go.transform.SetParent(holder,false);
            var mb=new MeshBuild(3);
            try{s.build(s,mb);}catch(System.Exception e){Debug.LogWarning("Changwon landmark model "+s.lm.name+": "+e);}
            if(!mb.Empty)Part(s,s.go.transform,s.lm.name,mb,true,s.build!=Plaza);
        }
        void Drop(Site s)
        {
            if(s.go!=null)Destroy(s.go);
            foreach(var m in s.meshes)if(m!=null)Destroy(m);
            s.meshes.Clear();s.go=null;s.parts=null;s.anim=0;
        }
        void Release(){foreach(var s in sites)Drop(s);sites.Clear();if(holder!=null)Destroy(holder.gameObject);holder=null;}

        // One renderer (+ collider of the Solid submesh, + an info hint when `info`) for a MeshBuild.
        GameObject Part(Site s,Transform parent,string name,MeshBuild mb,bool collide,bool info)
        {
            var o=new GameObject(name);o.transform.SetParent(parent,false);
            var mesh=mb.ToMesh(name);s.meshes.Add(mesh);
            o.AddComponent<MeshFilter>().sharedMesh=mesh;o.AddComponent<MeshRenderer>().sharedMaterials=new[]{lit,lit,glow};
            if(!collide||mb.sub[Solid].Count==0)return o;
            var cm=mb.ToCollider(name+" 충돌",Solid);s.meshes.Add(cm);o.AddComponent<MeshCollider>().sharedMesh=cm;
            if(info){var th=o.AddComponent<ChangwonThing>();th.kind="landmark";th.title=s.lm.name;th.hint=s.lm.name+" 안내 (F)";th.detail=s.lm.name+" · "+s.lm.description;th.payload=s.lm;}
            return o;
        }
        static void Sign(string text,Transform parent,Vector3 at,Vector3 toViewer,float height,Color color)
        {
            var w=ChangwonSession.Builder;if(w!=null)w.ChangwonSign(text,parent,at,toViewer,height,color);
        }

        void Animate(Site s,float t)
        {
            if(s.parts==null)return;
            if(s.anim==1){
                // 용지호수 music fountain: jets play at night only.
                bool night=DayCycle.Night;
                if(s.parts[0].gameObject.activeSelf!=night)foreach(var p in s.parts)p.gameObject.SetActive(night);
                if(!night)return;
                s.parts[0].localScale=new Vector3(1,.3f+.7f*(.5f+.5f*Mathf.Sin(t*.8f))*(.65f+.35f*Mathf.Sin(t*.31f+1f)),1);
                s.parts[1].localScale=new Vector3(1,.25f+.75f*Mathf.Abs(Mathf.Sin(t*1.3f+1f)),1);s.parts[1].localRotation=Quaternion.Euler(0,t*12f,0);
                s.parts[2].localScale=new Vector3(1,.4f+.6f*(.5f+.5f*Mathf.Sin(t*2.1f+2f)),1);
            }
        }

        // ------------------------------------------------------------------ helpers
        static Color32 C(int r,int g,int b){return new Color32((byte)Mathf.Clamp(r,0,255),(byte)Mathf.Clamp(g,0,255),(byte)Mathf.Clamp(b,0,255),255);}
        static Color32 Shade(Color32 c,float k){return C((int)(c.r*k),(int)(c.g*k),(int)(c.b*k));}
        static Vector2 XZ(Vector3 v){return new Vector2(v.x,v.z);}
        static Vector3 G(Vector2 p,float lift=0){return new Vector3(p.x,ChangwonData.Height(p.x,p.y)+lift,p.y);}
        // Quad that faces `toward` whatever order the corners come in.
        static void Face(MeshBuild mb,int s,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 toward,Color32 col)
        {
            if(Vector3.Dot(Vector3.Cross(b-a,d-a),toward)<0){var t=b;b=d;d=t;}
            mb.Quad(s,a,b,c,d,Vector2.zero,Vector2.up,Vector2.one,Vector2.right,col);
        }
        static void Tri(MeshBuild mb,int s,Vector3 a,Vector3 b,Vector3 d,Vector3 toward,Color32 col)
        {
            var n=Vector3.Cross(b-a,d-a);if(Vector3.Dot(n,toward)<0){var t=b;b=d;d=t;n=-n;}n.Normalize();
            int i=mb.Vertex(a,n,Vector2.zero,col);mb.Vertex(b,n,Vector2.zero,col);mb.Vertex(d,n,Vector2.zero,col);mb.Tri(s,i,i+1,i+2);
        }
        // Box from a to b, w wide along `side` and d deep across.
        static void Strut(MeshBuild mb,int s,Vector3 a,Vector3 b,float w,float d,Vector3 side,Color32 col)
        {
            var dir=b-a;float len=dir.magnitude;if(len<.01f)return;dir/=len;
            var fwd=Vector3.Cross(side,dir);if(fwd.sqrMagnitude<1e-6f)fwd=Vector3.Cross(Vector3.right,dir);if(fwd.sqrMagnitude<1e-6f)fwd=Vector3.forward;
            mb.Box(s,(a+b)*.5f,new Vector3(w,len,d),Quaternion.LookRotation(fwd.normalized,dir),col);
        }
        // Hexagonal double pyramid: a low-poly tree crown.
        static void Lump(MeshBuild mb,int s,Vector3 c,float r,float h,Color32 col,float spin)
        {
            var top=c+Up*h*.5f;var bot=c-Up*h*.5f;var dark=Shade(col,.82f);
            for(int i=0;i<6;i++){
                float a0=(i+spin)*Mathf.PI/3f,a1=(i+1+spin)*Mathf.PI/3f;
                var p0=c+new Vector3(Mathf.Cos(a0),0,Mathf.Sin(a0))*r;var p1=c+new Vector3(Mathf.Cos(a1),0,Mathf.Sin(a1))*r;var o=(p0+p1)*.5f-c;
                Tri(mb,s,p0,top,p1,o+Up*r*.5f,col);Tri(mb,s,p0,p1,bot,o-Up*r*.5f,dark);
            }
        }
        static void Cherry(MeshBuild mb,Vector3 p,System.Random rnd)
        {
            float h=5.5f+(float)rnd.NextDouble()*2.5f,g=(float)rnd.NextDouble();
            mb.Box(Solid,p+Up*h*.22f,new Vector3(.35f,h*.44f,.35f),Quaternion.Euler(0,g*90,0),C(84,62,52));
            var pink=C(250-(int)(g*22),176+(int)(g*34),198+(int)(g*22));
            Lump(mb,Plain,p+Up*h*.66f,h*.42f,h*.48f,pink,g);
            Lump(mb,Plain,p+Up*h*.56f+new Vector3((g-.5f)*h*.3f,0,h*.12f),h*.33f,h*.36f,Shade(pink,.94f),g+.5f);
        }

        static readonly HashSet<int> seen=new HashSet<int>();
        static List<ChangwonData.Road> RoadsNear(Vector2 p,float r)
        {
            var list=new List<ChangwonData.Road>();seen.Clear();
            var a=ChangwonData.ChunkOf(p.x-r,p.y-r);var b=ChangwonData.ChunkOf(p.x+r,p.y+r);
            for(int cz=a.y;cz<=b.y;cz++)for(int cx=a.x;cx<=b.x;cx++)foreach(int id in ChangwonData.RoadsInChunk[cz*ChangwonData.CX+cx])if(seen.Add(id))list.Add(ChangwonData.Roads[id]);
            return list;
        }
        static List<ChangwonData.Building> BuildingsAround(Vector2 p,float r)
        {
            var list=new List<ChangwonData.Building>();
            var a=ChangwonData.ChunkOf(p.x-r,p.y-r);var b=ChangwonData.ChunkOf(p.x+r,p.y+r);
            for(int cz=a.y;cz<=b.y;cz++)for(int cx=a.x;cx<=b.x;cx++)foreach(var bd in ChangwonData.BuildingsIn(cx,cz))if((bd.center-p).sqrMagnitude<(r+100f)*(r+100f))list.Add(bd);
            return list;
        }
        // The largest footprint near p whose name contains key (or, with no key, at least minArea m²).
        static ChangwonData.Building Anchor(Vector2 p,float r,string key,float minArea)
        {
            ChangwonData.Building best=null;float bestArea=minArea;
            foreach(var b in BuildingsAround(p,r)){
                if((b.center-p).sqrMagnitude>r*r||b.ring.Length<3||key!=null&&(b.name==null||!b.name.Contains(key)))continue;
                float area=Mathf.Abs(Polygon.SignedArea(b.ring));if(area>=bestArea){bestArea=area;best=b;}
            }
            return best;
        }
        // Long axis of a footprint, its half extents along/across it, and the centre of that box.
        static Vector2 Axis(Vector2[] ring,ref Vector2 c,out float major,out float minor)
        {
            float xx=0,zz=0,xz=0;foreach(var q in ring){var d=q-c;xx+=d.x*d.x;zz+=d.y*d.y;xz+=d.x*d.y;}
            float ang=.5f*Mathf.Atan2(2*xz,xx-zz);var ax=new Vector2(Mathf.Cos(ang),Mathf.Sin(ang));var ay=new Vector2(-ax.y,ax.x);
            float u0=1e9f,u1=-1e9f,v0=1e9f,v1=-1e9f;
            foreach(var q in ring){var d=q-c;float u=Vector2.Dot(d,ax),v=Vector2.Dot(d,ay);u0=Mathf.Min(u0,u);u1=Mathf.Max(u1,u);v0=Mathf.Min(v0,v);v1=Mathf.Max(v1,v);}
            c+=ax*(u0+u1)*.5f+ay*(v0+v1)*.5f;major=(u1-u0)*.5f;minor=(v1-v0)*.5f;
            if(minor>major){float t=major;major=minor;minor=t;ax=ay;}
            return ax;
        }
        static float Closest(ChangwonData.Road r,Vector2 p,out Vector3 point,out float along)
        {
            float best=float.MaxValue;point=r.pts[0];along=0;
            for(int i=1;i<r.pts.Length;i++){
                Vector2 a=XZ(r.pts[i-1]),ab=XZ(r.pts[i])-a;float l2=ab.sqrMagnitude,t=l2>1e-6f?Mathf.Clamp01(Vector2.Dot(p-a,ab)/l2):0;
                var q=Vector3.Lerp(r.pts[i-1],r.pts[i],t);float d=(XZ(q)-p).sqrMagnitude;
                if(d<best){best=d;point=q;along=Mathf.Lerp(r.along[i-1],r.along[i],t);}
            }
            return best;
        }
        // Inside a building or on a drivable road: no tree, stall or prop there.
        static bool Blocked(List<ChangwonData.Building> near,Vector2 q){return InBuilding(near,q)||OnRoad(q);}
        static bool InBuilding(List<ChangwonData.Building> near,Vector2 q)
        {
            foreach(var b in near)if(b.height>.5f&&(b.center-q).sqrMagnitude<8100f&&Polygon.Contains(b.ring,q))return true;
            return false;
        }
        static bool OnRoad(Vector2 q)
        {
            ChangwonData.Road r;float al;Vector3 pt;
            return ChangwonData.NearestRoad(G(q),10f,out r,out al,out pt)&&(XZ(pt)-q).magnitude<r.width*.5f+.9f;
        }
        static Vector2 Centroid(Vector2[] ring)
        {
            float a=0;var c=Vector2.zero;var o=ring[0];
            for(int i=0,j=ring.Length-1;i<ring.Length;j=i++){var p=ring[j]-o;var q=ring[i]-o;float cr=p.x*q.y-q.x*p.y;a+=cr;c+=(p+q)*cr;}
            return Mathf.Abs(a)<1e-3f?o:o+c/(3f*a);
        }
        static Vector2 SummitOf(Vector2 p,float r)
        {
            var best=p;float h=float.MinValue;
            for(float dx=-r;dx<=r;dx+=8f)for(float dz=-r;dz<=r;dz+=8f){if(dx*dx+dz*dz>r*r)continue;float y=ChangwonData.Height(p.x+dx,p.y+dz);if(y>h){h=y;best=new Vector2(p.x+dx,p.y+dz);}}
            return best;
        }
        // Frame of a model: centre c, axis ax (local +x), up; local +z = across.
        static Vector3 L(Vector2 c,Vector2 ax,float u,float v,float y){return new Vector3(c.x+ax.x*u-ax.y*v,y,c.y+ax.y*u+ax.x*v);}

        // ------------------------------------------------------------------ 창원광장
        // The roundabout's central island: lawn, ring path, cross paths, hedge, flagpoles and a steel sculpture.
        void Plaza(Site s,MeshBuild mb)
        {
            Vector2 c=s.lm.pos;float ring,half,R=70f;
            if(RingFit(ref c,out ring,out half))R=Mathf.Clamp(ring-half-1.5f,25f,140f);else c=s.lm.pos;
            s.at=c;const int N=64,K=10;
            System.Func<float,float,Vector3> P=(r,a)=>G(c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,.3f);
            for(int k=1;k<=K;k++)for(int i=0;i<N;i++){
                float r0=R*(k-1)/K,r1=R*k/K,a0=i*2*Mathf.PI/N,a1=(i+1)*2*Mathf.PI/N;var grass=(k+i/8)%2==0?C(86,128,60):C(78,119,55);
                if(k==1)Tri(mb,Solid,P(0,0),P(r1,a0),P(r1,a1),Up,grass);else Face(mb,Solid,P(r0,a0),P(r1,a0),P(r1,a1),P(r0,a1),Up,grass);
            }
            var paving=C(184,176,160);float rp=R*.62f;
            for(int i=0;i<N;i++){
                float a0=i*2*Mathf.PI/N,a1=(i+1)*2*Mathf.PI/N;
                Face(mb,Plain,P(rp-2.5f,a0)+Up*.12f,P(rp+2.5f,a0)+Up*.12f,P(rp+2.5f,a1)+Up*.12f,P(rp-2.5f,a1)+Up*.12f,Up,paving);
                if(i%2==0)Tri(mb,Plain,P(0,0)+Up*.12f,P(18f,a0)+Up*.12f,P(18f,a0+4*Mathf.PI/N)+Up*.12f,Up,C(196,188,172));
            }
            for(int q=0;q<4;q++){
                float a=q*Mathf.PI*.5f;var dir=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var side=new Vector2(-dir.y,dir.x)*2f;
                for(float r=18f;r<R-2f;r+=8f){float r1=Mathf.Min(r+8f,R-2f);Face(mb,Plain,G(c+dir*r-side,.42f),G(c+dir*r1-side,.42f),G(c+dir*r1+side,.42f),G(c+dir*r+side,.42f),Up,paving);}
            }
            // Hedge round the rim (gaps where the cross paths meet it) keeps cars off the lawn.
            for(int i=0;i<N;i++){
                if(i%(N/4)==0)continue; // openings where the cross paths meet the rim
                float a0=(i-.5f)*2*Mathf.PI/N,a1=(i+.5f)*2*Mathf.PI/N;
                var p0=c+new Vector2(Mathf.Cos(a0),Mathf.Sin(a0))*(R-1.2f);var p1=c+new Vector2(Mathf.Cos(a1),Mathf.Sin(a1))*(R-1.2f);var m=G((p0+p1)*.5f,.35f);
                mb.Box(Solid,m,new Vector3(1f,1.7f,(p1-p0).magnitude+.2f),Quaternion.LookRotation(new Vector3(p1.x-p0.x,0,p1.y-p0.y)),C(48,88,46));
            }
            // Centre: granite base, three leaning steel blades, a ring of flagpoles.
            var b=G(c,.3f);mb.Box(Solid,b+Up*.4f,new Vector3(9,.8f,9),Quaternion.identity,C(150,146,140));
            for(int k=0;k<3;k++){float a=k*2*Mathf.PI/3;var foot=b+Up*.8f+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*3.2f;Strut(mb,Solid,foot,b+Up*19f+new Vector3(Mathf.Cos(a+1f),0,Mathf.Sin(a+1f))*.6f,1.3f,.45f,new Vector3(-Mathf.Sin(a),0,Mathf.Cos(a)),C(205,210,216));}
            Color32[] flags={C(245,245,245),C(30,80,170),C(245,245,245),C(0,140,120),C(245,245,245),C(220,60,50)};
            for(int k=0;k<12;k++){
                float a=k*Mathf.PI/6f;var foot=G(c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*14f,.3f);
                mb.Box(Solid,foot+Up*7f,new Vector3(.22f,14f,.22f),Quaternion.identity,C(220,222,225));
                var along=new Vector3(-Mathf.Sin(a),0,Mathf.Cos(a));mb.Box(Plain,foot+Up*13.1f+along*1.2f,new Vector3(.04f,1.5f,2.3f),Quaternion.LookRotation(along),flags[k%flags.Length]);
            }
            Sign("창원광장",s.go.transform,b+Up*.4f+Vector3.back*4.56f,Vector3.back,.45f,new Color(.95f,.95f,.92f));
        }
        // Centre and radius of the roundabout ring road near c (length-weighted centroid of its arcs, refined twice).
        static bool RingFit(ref Vector2 c,out float radius,out float half)
        {
            radius=0;half=0;
            for(int it=0;it<3;it++){
                Vector2 sum=Vector2.zero;float len=0,rad=0,wid=0;
                foreach(var r in RoadsNear(c,230f)){
                    if(r.cls>ChangwonData.Tertiary)continue;
                    float dmin=1e9f,dmax=0;foreach(var p in r.pts){float d=Vector2.Distance(XZ(p),c);dmin=Mathf.Min(dmin,d);dmax=Mathf.Max(dmax,d);}
                    if(dmin<40f||dmax>220f||dmax-dmin>12f)continue;
                    for(int k=1;k<r.pts.Length;k++){Vector2 a=XZ(r.pts[k-1]),b=XZ(r.pts[k]);float l=Vector2.Distance(a,b);sum+=(a+b)*.5f*l;len+=l;rad+=Vector2.Distance((a+b)*.5f,c)*l;wid+=r.width*l;}
                }
                if(len<150f)return false;
                c=sum/len;radius=rad/len;half=wid/len*.5f;
            }
            return true;
        }

        // ------------------------------------------------------------------ 마창대교
        // Two 164 m inverted-Y concrete pylons 400 m apart over the water, with fan cables to both deck edges.
        void Bridge(Site s,MeshBuild mb)
        {
            // The deck: drivable bridge roads near the landmark (picked by shape; OSM names vary between 마창대교 and 남해안대로).
            var deck=new List<ChangwonData.Road>();
            foreach(var r in RoadsNear(s.lm.pos,1600f))if(r.Drivable&&System.Array.IndexOf(r.kind,ChangwonData.Bridge)>=0)deck.Add(r);
            ChangwonData.Road main=null;float w0=0,w1=0,best=0;
            foreach(var r in deck){
                float a=-1,b=-1;Vector3 f;
                for(float d=0;d<=r.length;d+=10f){var p=r.At(d,out f);if(p.y>20f&&ChangwonData.Height(p.x,p.z)<0){if(a<0)a=d;b=d;}}
                if(a>=0&&b-a>best){best=b-a;main=r;w0=a;w1=b;}
            }
            if(main==null||best<150f)return;
            float mid=(w0+w1)*.5f,off=Mathf.Min(200f,best/3f);
            Pylon(mb,deck,main,mid-off,1,off);Pylon(mb,deck,main,mid+off,-1,off);
        }
        static float DeckY(List<ChangwonData.Road> deck,Vector2 q)
        {
            float best=float.MaxValue,y=0;Vector3 p;float al;
            foreach(var r in deck){float d=Closest(r,q,out p,out al);if(d<best){best=d;y=p.y;}}
            return y;
        }
        // inward: +1 when the main span lies ahead (+along) of this pylon.
        static void Pylon(MeshBuild mb,List<ChangwonData.Road> deck,ChangwonData.Road main,float sp,int inward,float off)
        {
            Vector3 f;var pa=main.At(sp,out f);f.y=0;f.Normalize();var right=new Vector3(f.z,0,-f.x);
            var pb=pa;float bd=45f*45f;Vector3 q;float al;
            foreach(var r in deck)if(r!=main){float d=Closest(r,XZ(pa),out q,out al);if(d<bd){bd=d;pb=q;}}
            var c=(pa+pb)*.5f;var shift=c-pa;shift.y=0;
            float halfDeck=Mathf.Abs(Vector3.Dot(pb-pa,right))*.5f+main.width*.5f+.5f,lo=Mathf.Min(pa.y,pb.y),hi=Mathf.Max(pa.y,pb.y);
            float ground=ChangwonData.Height(c.x,c.z),foot=Mathf.Max(ground,0)+3f,W=halfDeck+3.5f;const float Top=164f;
            var concrete=C(214,212,206);var rot=Quaternion.LookRotation(f);c.y=0;
            mb.Box(Plain,c+Up*(Mathf.Min(ground,0)-2f+foot)*.5f,new Vector3(2*W+14f,foot-Mathf.Min(ground,0)+2f,16f),rot,C(150,148,142));
            for(int sd=-1;sd<=1;sd+=2){
                Strut(mb,Plain,c+right*sd*(W+3f)+Up*foot,c+right*sd*W+Up*(lo-2f),4.5f,7f,right,concrete);
                Strut(mb,Plain,c+right*sd*W+Up*(lo-2f),c+right*sd*2.4f+Up*(hi+48f),4f,6.5f,right,concrete);
            }
            mb.Box(Plain,c+Up*(lo-3.5f),new Vector3(2*W+4.5f,3f,6.5f),rot,concrete);
            mb.Box(Plain,c+Up*(hi+44f+Top)*.5f,new Vector3(5.5f,Top-hi-44f,7.5f),rot,concrete);
            mb.Box(Plain,c+Up*(Top+.6f),new Vector3(6.5f,1.2f,8.5f),rot,C(180,178,172));
            mb.Box(Glow,c+Up*(Top+1.7f),Vector3.one,rot,C(255,40,30)); // aviation light
            // Semi-fan stays: 11 per side per plane, longest to the top.
            const int n=11;float inner=off-8f,outer=Mathf.Min(170f,off*.85f);var cable=C(236,238,240);
            for(int side=-1;side<=1;side+=2){
                float span=side==inward?inner:outer;
                for(int k=0;k<n;k++){
                    float t=k/(n-1f),y=hi+56f+(Top-4f-hi-56f)*t,d=24f+(span-24f)*t;
                    Vector3 f2;var at=main.At(sp+side*d,out f2)+shift;f2.y=0;f2.Normalize();var r2=new Vector3(f2.z,0,-f2.x);
                    for(int plane=-1;plane<=1;plane+=2){
                        var low=at+r2*plane*(halfDeck-.6f);low.y=DeckY(deck,XZ(low))+1.2f;
                        Strut(mb,Plain,c+right*plane*1.6f+Up*y,low,.4f,.4f,right,cable);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ 창원솔라타워
        // 136 m slim tower with a sail of dark blue solar panels and an observation deck near the top.
        void SolarTower(Site s,MeshBuild mb)
        {
            Vector2 c=s.at,ax=Vector2.right;float a=45f,b=20f,y0;
            if(s.anchor!=null){ax=Axis(s.anchor.ring,ref c,out a,out b);y0=s.anchor.baseY;}else y0=ChangwonData.Height(c.x,c.y);
            var rot=Quaternion.LookRotation(new Vector3(-ax.y,0,ax.x));var white=C(232,234,236);
            float coreU=-Mathf.Clamp(a*.45f,10f,30f);
            mb.Box(Solid,L(c,ax,0,0,y0+2.5f),new Vector3(Mathf.Clamp(2*a,40f,90f),6f,Mathf.Clamp(2*b,18f,32f)),rot,C(205,207,210));
            mb.Box(Solid,L(c,ax,coreU,0,y0+68f),new Vector3(9f,136f,9f),rot,white);
            mb.Box(Solid,L(c,ax,coreU,0,y0+105f),new Vector3(15f,10f,15f),rot,white);
            mb.Box(Plain,L(c,ax,coreU,0,y0+105.5f),new Vector3(15.4f,4f,15.4f),rot,C(55,78,100));
            mb.Box(Plain,L(c,ax,coreU,0,y0+140f),new Vector3(.8f,8f,.8f),rot,C(200,60,50));
            mb.Box(Glow,L(c,ax,coreU,0,y0+144.3f),new Vector3(.9f,.9f,.9f),rot,C(255,40,30));
            for(int i=0;i<16;i++){
                float ya=10f+i*7.6f,yb=ya+7f,t=(ya+yb)*.5f/136f,len=52f*Mathf.Pow(1f-t,.75f);
                mb.Box(Solid,L(c,ax,coreU+4.5f+len*.5f,0,y0+(ya+yb)*.5f),new Vector3(len,yb-ya,.7f),rot,i%2==0?C(26,46,102):C(34,58,122));
            }
            Strut(mb,Solid,L(c,ax,coreU+4.5f+52f*Mathf.Pow(1f-13.5f/136f,.75f),0,y0+10f),L(c,ax,coreU+5.5f,0,y0+132f),1.2f,1.2f,Up,white);
            mb.Box(Solid,L(c,ax,coreU+30f,0,y0+9.5f),new Vector3(52f,1f,1.2f),rot,white);
            Sign("창원솔라타워",s.go.transform,L(c,ax,0,-Mathf.Clamp(b,9f,16f)-.1f,y0+4f),new Vector3(ax.y,0,-ax.x),1.4f,new Color(.15f,.2f,.35f));
        }

        // ------------------------------------------------------------------ 진해탑
        // 28 m tower on 제황산: a base hall, seven stacked storeys like a warship mast, observation deck and mast top.
        void JinhaeTower(Site s,MeshBuild mb)
        {
            Vector2 c,ax=Vector2.right;float a,b,y0;
            if(s.anchor!=null){c=s.anchor.center;ax=Axis(s.anchor.ring,ref c,out a,out b);y0=s.anchor.baseY;}
            else{c=SummitOf(s.lm.pos,200f);y0=ChangwonData.Height(c.x,c.y);}
            s.at=c;var rot=Quaternion.LookRotation(new Vector3(-ax.y,0,ax.x));var white=C(236,236,232);var glass=C(64,84,108);
            mb.Box(Solid,L(c,ax,0,0,y0+1.75f),new Vector3(20f,5f,16f),rot,white);
            mb.Box(Plain,L(c,ax,0,0,y0+2.2f),new Vector3(20.2f,1.4f,16.2f),rot,glass);
            mb.Box(Solid,L(c,ax,0,0,y0+13f),new Vector3(6.5f,18f,6.5f),rot,white);
            for(int i=0;i<7;i++){
                float y=y0+4.25f+i*2.5f;
                mb.Box(Plain,L(c,ax,0,0,y+1f),new Vector3(9.4f,1.6f,9.4f),rot,glass);
                mb.Box(Solid,L(c,ax,0,0,y+2.1f),new Vector3(10.6f-i*.25f,.5f,10.6f-i*.25f),rot,white);
            }
            float d=y0+21.75f;
            mb.Box(Solid,L(c,ax,0,0,d),new Vector3(14f,.6f,14f),rot,white);
            for(int k=0;k<4;k++){var o=k<2?new Vector2(0,k==0?6.9f:-6.9f):new Vector2(k==2?6.9f:-6.9f,0);mb.Box(Plain,L(c,ax,o.x,o.y,d+.8f),k<2?new Vector3(14f,1f,.12f):new Vector3(.12f,1f,14f),rot,C(200,205,210));}
            mb.Box(Plain,L(c,ax,0,0,d+1.7f),new Vector3(9f,2.6f,9f),rot,glass);
            mb.Box(Solid,L(c,ax,0,0,d+3.25f),new Vector3(10f,.5f,10f),rot,white);
            mb.Box(Plain,L(c,ax,0,0,d+4.9f),new Vector3(.45f,2.8f,.45f),rot,C(210,210,210));
            mb.Box(Plain,L(c,ax,0,0,d+5.4f),new Vector3(4.5f,.2f,.2f),rot,C(210,210,210)); // yard: the mast look
            mb.Box(Glow,L(c,ax,0,0,d+6.4f),new Vector3(.5f,.5f,.5f),rot,C(255,60,40));
            var front=new Vector3(ax.y,0,-ax.x);
            Sign("진해탑",s.go.transform,L(c,ax,0,-8.15f,y0+3.2f),front,1.1f,new Color(.12f,.18f,.3f));
            Sign("진해탑",s.go.transform,L(c,ax,0,8.15f,y0+3.2f),-front,1.1f,new Color(.12f,.18f,.3f));
        }

        // ------------------------------------------------------------------ 창원NC파크 / 창원종합운동장
        void Stadium(Site s,MeshBuild mb)
        {
            bool ball=s.lm.name.Contains("NC");
            Vector2 c=s.at,ax=Vector2.right;float a=ball?120f:115f,b=ball?100f:95f,y0;
            if(s.anchor!=null){ax=Axis(s.anchor.ring,ref c,out a,out b);y0=s.anchor.baseY;}else y0=ChangwonData.Height(c.x,c.y);
            s.at=c;a=Mathf.Clamp(a-1f,70f,140f);b=Mathf.Clamp(b-1f,55f,125f);
            float top=y0+Mathf.Clamp(s.lm.height,15f,45f),depth=ball?42f:34f,ai=a-depth,bi=b-depth;
            // Field level: above the highest ground inside, so the terrain never shows through.
            float fy=y0;for(float u=-ai;u<=ai;u+=16f)for(float v=-bi;v<=bi;v+=16f)if(u*u/(ai*ai)+v*v/(bi*bi)<=1f){var p=L(c,ax,u,v,0);fy=Mathf.Max(fy,ChangwonData.Height(p.x,p.z));}
            fy+=.3f;
            System.Func<float,float,float,float,Vector3> E=(ea,eb,th,y)=>L(c,ax,ea*Mathf.Cos(th),eb*Mathf.Sin(th),y);
            var facade=ball?C(52,62,82):C(196,196,190);var facade2=ball?C(70,82,104):C(176,178,174);
            Color32[] seats=ball?new[]{C(36,58,108),C(30,48,92),C(54,82,132),C(46,70,118),C(140,144,150),C(124,128,134)}:new[]{C(176,52,48),C(156,44,42),C(214,138,44),C(196,124,40),C(44,92,160),C(38,80,142)};
            const int N=64;float trackIn=ball?0:13f;
            for(int i=0;i<N;i++){
                float t0=i*2*Mathf.PI/N,t1=(i+1)*2*Mathf.PI/N;var outward=E(a,b,(t0+t1)*.5f,0)-L(c,ax,0,0,0);
                float g0=ChangwonData.Height(E(a,b,t0,0).x,E(a,b,t0,0).z),g1=ChangwonData.Height(E(a,b,t1,0).x,E(a,b,t1,0).z),bottom=Mathf.Min(Mathf.Min(g0,g1),y0)-2.5f;
                // Outer facade in two bands, rim, parapet.
                Face(mb,Solid,E(a,b,t0,bottom),E(a,b,t0,top-8f),E(a,b,t1,top-8f),E(a,b,t1,bottom),outward,(i&1)==0?facade:Shade(facade,.94f));
                Face(mb,Solid,E(a,b,t0,top-8f),E(a,b,t0,top),E(a,b,t1,top),E(a,b,t1,top-8f),outward,facade2);
                Face(mb,Solid,E(a-1.5f,b-1.5f,t0,top),E(a,b,t0,top),E(a,b,t1,top),E(a-1.5f,b-1.5f,t1,top),Up,facade2);
                Face(mb,Solid,E(a-1.5f,b-1.5f,t0,top-1.6f),E(a-1.5f,b-1.5f,t0,top),E(a-1.5f,b-1.5f,t1,top),E(a-1.5f,b-1.5f,t1,top-1.6f),-outward,facade2);
                // Stands from the field wall up to the rim, in coloured sections.
                for(int k=0;k<6;k++){
                    float u0=k/6f,u1=(k+1)/6f;float ea0=Mathf.Lerp(ai,a-1.5f,u0),eb0=Mathf.Lerp(bi,b-1.5f,u0),ea1=Mathf.Lerp(ai,a-1.5f,u1),eb1=Mathf.Lerp(bi,b-1.5f,u1);
                    float h0=Mathf.Lerp(fy+3f,top-1.6f,u0),h1=Mathf.Lerp(fy+3f,top-1.6f,u1);
                    var col=seats[(k/2*2+((i/8)&1))%seats.Length];if((k&1)==1)col=Shade(col,.9f);
                    Face(mb,Solid,E(ea0,eb0,t0,h0),E(ea1,eb1,t0,h1),E(ea1,eb1,t1,h1),E(ea0,eb0,t1,h0),Up-outward.normalized,col);
                }
                Face(mb,Solid,E(ai,bi,t0,fy-.5f),E(ai,bi,t0,fy+3f),E(ai,bi,t1,fy+3f),E(ai,bi,t1,fy-.5f),-outward,C(90,96,104));
                // Field and (athletics) the running track.
                var centre=L(c,ax,0,0,fy);
                Tri(mb,Plain,centre,E(ai-trackIn,bi-trackIn,t0,fy),E(ai-trackIn,bi-trackIn,t1,fy),Up,(i/4&1)==0?C(72,138,62):C(66,128,57));
                if(!ball){
                    Face(mb,Plain,E(ai-trackIn,bi-trackIn,t0,fy),E(ai,bi,t0,fy),E(ai,bi,t1,fy),E(ai-trackIn,bi-trackIn,t1,fy),Up,C(176,72,56));
                    Face(mb,Plain,E(ai-trackIn+.1f,bi-trackIn+.1f,t0,fy+.03f),E(ai-trackIn+.35f,bi-trackIn+.35f,t0,fy+.03f),E(ai-trackIn+.35f,bi-trackIn+.35f,t1,fy+.03f),E(ai-trackIn+.1f,bi-trackIn+.1f,t1,fy+.03f),Up,C(240,240,240));
                }
            }
            if(ball){
                // Infield: dirt diamond with home plate toward the -axis end, grass square inside, white bases.
                float hu=-ai+22f,dg=27.43f*.7071f;var dirt=C(168,118,80);
                Face(mb,Plain,L(c,ax,hu-6f,0,fy+.15f),L(c,ax,hu+dg+3f,-dg-9f,fy+.15f),L(c,ax,hu+2*dg+12f,0,fy+.15f),L(c,ax,hu+dg+3f,dg+9f,fy+.15f),Up,dirt);
                Face(mb,Plain,L(c,ax,hu+3f,0,fy+.25f),L(c,ax,hu+dg,-dg+3f,fy+.25f),L(c,ax,hu+2*dg-3f,0,fy+.25f),L(c,ax,hu+dg,dg-3f,fy+.25f),Up,C(76,142,64));
                var rot=Quaternion.LookRotation(new Vector3(-ax.y,0,ax.x))*Quaternion.Euler(0,45,0);
                foreach(var bp in new[]{new Vector2(hu,0),new Vector2(hu+dg,-dg),new Vector2(hu+2*dg,0),new Vector2(hu+dg,dg)})mb.Box(Plain,L(c,ax,bp.x,bp.y,fy+.3f),new Vector3(.8f,.12f,.8f),rot,C(245,245,245));
                mb.Box(Plain,L(c,ax,hu+dg,0,fy+.3f),new Vector3(5f,.3f,5f),Quaternion.identity,dirt);
            }
            // Floodlight towers outside the rim, lamps facing the field.
            int towers=ball?6:4;
            for(int k=0;k<towers;k++){
                float th=(ball?30f+k*60f:45f+k*90f)*Mathf.Deg2Rad;var foot=E(a+5f,b+5f,th,0);if(OnRoad(XZ(foot)))continue;foot.y=ChangwonData.Height(foot.x,foot.z)-1f;float h=top+24f-foot.y;
                mb.Box(Solid,foot+Up*h*.5f,new Vector3(1.6f,h,1.6f),Quaternion.identity,C(150,154,160));
                var look=L(c,ax,0,0,fy)-(foot+Up*h);look.y=0;
                mb.Box(Plain,foot+Up*(h+.2f),new Vector3(10.6f,5.6f,.6f),Quaternion.LookRotation(look),C(70,74,80));
                mb.Box(Glow,foot+Up*(h+.2f)+look.normalized*.4f,new Vector3(10f,5f,.3f),Quaternion.LookRotation(look),C(255,248,222));
            }
            var front=new Vector3(ax.y,0,-ax.x);
            Sign(s.lm.name,s.go.transform,E(a+.3f,b+.3f,-Mathf.PI*.5f,top-4f),front,ball?5f:4f,Color.white);
            Sign(s.lm.name,s.go.transform,E(a+.3f,b+.3f,Mathf.PI*.5f,top-4f),-front,ball?5f:4f,Color.white);
        }

        // ------------------------------------------------------------------ 용지호수 음악분수
        void Fountain(Site s,MeshBuild mb)
        {
            ChangwonData.Lake lake=null;float best=600f*600f;
            foreach(var l in ChangwonData.Lakes){
                if(l.ring.Length<3)continue;
                if(Polygon.Contains(l.ring,s.lm.pos)){lake=l;break;}
                float d=(Centroid(l.ring)-s.lm.pos).sqrMagnitude;if(d<best){best=d;lake=l;}
            }
            if(lake==null)return;
            var c=Centroid(lake.ring);if(!Polygon.Contains(lake.ring,c))c=lake.ring[0]+(c-lake.ring[0])*.5f;
            float y=lake.surface;var at=new Vector3(c.x,y,c.y);
            for(int i=0;i<24;i++){
                float a0=i*Mathf.PI/12f,a1=(i+1)*Mathf.PI/12f;var p0=new Vector3(Mathf.Cos(a0),0,Mathf.Sin(a0))*15f;var p1=new Vector3(Mathf.Cos(a1),0,Mathf.Sin(a1))*15f;
                mb.Box(Plain,at+(p0+p1)*.5f+Up*.1f,new Vector3(.8f,.4f,(p1-p0).magnitude+.1f),Quaternion.LookRotation(p1-p0),C(70,74,78));
            }
            mb.Box(Plain,at+Up*.1f,new Vector3(3.5f,.4f,3.5f),Quaternion.identity,C(70,74,78));
            // Three jet groups whose heights the animation plays (local y scale), lit by the unlit material at night.
            var groups=new Transform[3];
            for(int g=0;g<3;g++){
                var jm=new MeshBuild(3);
                if(g==0){jm.Box(Glow,Up*12f,new Vector3(1f,24f,1f),Quaternion.identity,C(214,240,255));jm.Box(Glow,Up*3f,new Vector3(2.6f,6f,2.6f),Quaternion.identity,C(180,222,250));}
                else{
                    int n=g==1?10:20;float r=g==1?6f:12f,h=g==1?11f:5.5f;
                    for(int k=0;k<n;k++){float a=k*2*Mathf.PI/n;var d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));Strut(jm,Glow,d*r,d*r*(g==1?1.35f:1.15f)+Up*h,g==1?.5f:.4f,g==1?.5f:.4f,new Vector3(-d.z,0,d.x),g==1?C(170,220,255):C(196,170,255));}
                }
                var o=Part(s,s.go.transform,"분수 물줄기",jm,false,false);o.transform.position=at;groups[g]=o.transform;
                o.SetActive(DayCycle.Night);
            }
            s.parts=groups;s.anim=1;
        }

        // ------------------------------------------------------------------ 경화역 벚꽃길 / 여좌천
        void Gyeonghwa(Site s,MeshBuild mb)
        {
            ChangwonData.Road rail=null;float best=300f*300f,along=0;Vector3 sp=Vector3.zero;var rails=new List<ChangwonData.Road>();
            foreach(var r in RoadsNear(s.lm.pos,550f))if(r.IsRail){rails.Add(r);Vector3 q;float al;float d=Closest(r,s.lm.pos,out q,out al);if(d<best){best=d;rail=r;sp=q;along=al;}}
            if(rail==null)return;
            var near=BuildingsAround(XZ(sp),320f);var rnd=new System.Random(11);float spacing=PerformanceRuntime.Level==0?14f:10f;
            foreach(var r in rails)for(float d=0;d<=r.length;d+=spacing){
                Vector3 f;var p=r.At(d,out f);if((XZ(p)-XZ(sp)).sqrMagnitude>250f*250f)continue;
                f.y=0;if(f.sqrMagnitude<1e-4f)continue;f.Normalize();var right=new Vector3(f.z,0,-f.x);
                for(int side=-1;side<=1;side+=2){var q=XZ(p+right*side*6.5f);if(!Blocked(near,q))Cherry(mb,G(q),rnd);}
            }
            // The display coach on its own short track beside the line, with the station name board.
            Vector3 fw;rail.At(along,out fw);fw.y=0;fw.Normalize();var rt=new Vector3(fw.z,0,-fw.x);
            for(int side=-1;side<=1;side+=2){
                var c=XZ(sp+rt*side*13f);
                if(InBuilding(near,c+XZ(fw)*11f)||InBuilding(near,c-XZ(fw)*11f)||Blocked(near,c))continue;
                var g=G(c);var rot=Quaternion.LookRotation(fw);
                mb.Box(Plain,g+Up*.1f,new Vector3(3.4f,.3f,27f),rot,C(120,112,104));
                for(int k=-1;k<=1;k+=2)mb.Box(Plain,g+rt*k*.75f+Up*.33f,new Vector3(.12f,.16f,27f),rot,C(90,90,94));
                for(int k=-1;k<=1;k+=2)mb.Box(Plain,g+fw*k*7f+Up*.8f,new Vector3(2.4f,.9f,2.8f),rot,C(44,44,48));
                mb.Box(Solid,g+Up*2.7f,new Vector3(3f,2.9f,20f),rot,C(236,232,220));
                mb.Box(Plain,g+Up*3f,new Vector3(3.04f,.9f,18.6f),rot,C(46,62,84));
                mb.Box(Plain,g+Up*2f,new Vector3(3.04f,.25f,20.02f),rot,C(196,42,40));
                mb.Box(Plain,g+Up*4.3f,new Vector3(2.6f,.35f,19.6f),rot,C(150,152,156));
                var board=XZ(sp+rt*side*4f);var bg=G(board);
                mb.Box(Solid,bg+Up*1.1f,new Vector3(.15f,2.2f,.15f),rot,C(70,70,74));
                mb.Box(Plain,bg+Up*2.4f,new Vector3(.1f,.8f,2.6f),rot,C(250,250,250));
                Sign("경화역",s.go.transform,bg+Up*2.45f+rt*.06f,rt,.42f,new Color(.1f,.2f,.45f));
                Sign("경화역",s.go.transform,bg+Up*2.45f-rt*.06f,-rt,.42f,new Color(.1f,.2f,.45f));
                break;
            }
        }
        void Yeojwa(Site s,MeshBuild mb)
        {
            var roads=new List<ChangwonData.Road>();float total=0;const float R=800f;
            foreach(var r in RoadsNear(s.lm.pos,R))if(r.name=="여좌천로"&&r.cls<=ChangwonData.Tertiary){Vector3 q;float al;if(Closest(r,s.lm.pos,out q,out al)<R*R){roads.Add(r);total+=r.length;}}
            int max=PerformanceRuntime.Level==0?110:190;float spacing=Mathf.Max(PerformanceRuntime.Level==0?16f:11f,total*2f/max);
            var near=BuildingsAround(s.lm.pos,R);var rnd=new System.Random(14);int count=0;
            foreach(var r in roads)for(float d=spacing*.5f;d<r.length&&count<max;d+=spacing){
                Vector3 f;var p=r.At(d,out f);if((XZ(p)-s.lm.pos).sqrMagnitude>R*R)continue;
                f.y=0;if(f.sqrMagnitude<1e-4f)continue;f.Normalize();var right=new Vector3(f.z,0,-f.x);
                for(int side=-1;side<=1;side+=2){var q=XZ(p+right*side*(r.width*.5f+3f));if(!Blocked(near,q)){Cherry(mb,G(q),rnd);count++;}}
            }
        }

        // ------------------------------------------------------------------ 마산어시장
        // Rows of stalls with striped awnings along the 어시장 alleys, and a gate with the market's name.
        void Market(Site s,MeshBuild mb)
        {
            var near=BuildingsAround(s.lm.pos,260f);var rnd=new System.Random(37);int count=0,max=PerformanceRuntime.Level==0?60:120;const float R=170f;
            var alleys=new List<ChangwonData.Road>();ChangwonData.Road gate=null;float gd=float.MaxValue,galong=0;Vector3 gp=Vector3.zero;
            foreach(var r in RoadsNear(s.lm.pos,R))if(r.name.Contains("어시장")){alleys.Add(r);Vector3 q;float al;float d=Closest(r,s.lm.pos,out q,out al);if(d<gd){gd=d;gate=r;gp=q;galong=al;}}
            if(gate==null)return;
            Vector3 gf;gate.At(galong,out gf);gf.y=0;gf.Normalize();var gr=new Vector3(gf.z,0,-gf.x);
            for(int k=-1;k<=1;k+=2){var post=G(XZ(gp+gr*k*(gate.width*.5f+.5f)));mb.Box(Solid,post+Up*2.9f,new Vector3(.6f,5.8f,.6f),Quaternion.identity,C(40,80,150));}
            var beam=new Vector3(gp.x,ChangwonData.Height(gp.x,gp.z)+5.6f,gp.z);
            mb.Box(Plain,beam,new Vector3(gate.width+1.8f,1.1f,.5f),Quaternion.LookRotation(gf),C(40,80,150));
            Sign("마산어시장",s.go.transform,beam+gf*.3f,gf,.7f,Color.white);Sign("마산어시장",s.go.transform,beam-gf*.3f,-gf,.7f,Color.white);
            Color32[] stripes={C(200,40,40),C(30,90,170),C(30,140,80),C(230,130,30)};
            foreach(var r in alleys)for(float d=4f;d<r.length-4f&&count<max;d+=3.3f){
                Vector3 f;var p=r.At(d,out f);if((XZ(p)-s.lm.pos).sqrMagnitude>R*R||(p-gp).sqrMagnitude<25f)continue;
                f.y=0;if(f.sqrMagnitude<1e-4f)continue;f.Normalize();var right=new Vector3(f.z,0,-f.x);
                for(int side=-1;side<=1;side+=2){
                    var o=right*side;var q=XZ(p+o*(r.width*.5f+1.5f));
                    if(InBuilding(near,q+XZ(o)*1.2f)||Blocked(near,q))continue;
                    Stall(mb,G(q),o,f,stripes[rnd.Next(stripes.Length)],rnd);count++;
                }
            }
        }
        // A stall whose open front faces the alley (-o); f runs along the alley.
        static void Stall(MeshBuild mb,Vector3 b,Vector3 o,Vector3 f,Color32 stripe,System.Random rnd)
        {
            var rot=Quaternion.LookRotation(-o);
            mb.Box(Solid,b+Up*.45f,new Vector3(2.9f,.9f,1.3f),rot,C(120,110,100));
            mb.Box(Plain,b+Up*.96f-o*.05f,new Vector3(2.7f,.12f,1.1f),rot,C(200,214,224)); // ice tray
            mb.Box(Plain,b+Up*1.12f-o*.1f+f*((float)rnd.NextDouble()-.5f),new Vector3(.9f,.25f,.6f),rot,rnd.Next(2)==0?C(190,50,40):C(40,90,170)); // fish tub
            for(int k=-1;k<=1;k+=2)mb.Box(Plain,b-o*.75f+f*k*1.4f+Up*1.05f,new Vector3(.08f,2.1f,.08f),rot,C(150,150,150));
            for(int k=0;k<4;k++){
                float x=-1.08f+k*.72f;var w=f*.36f;Vector3 hi=b+o*.8f+Up*2.6f+f*x,lo=b-o*1.1f+Up*2.1f+f*x;var col=(k&1)==0?stripe:C(245,245,240);
                Face(mb,Plain,hi-w,hi+w,lo+w,lo-w,Up,col);Face(mb,Plain,hi-w,hi+w,lo+w,lo-w,-Up,Shade(col,.8f));
            }
        }

        // ------------------------------------------------------------------ 산 정상석
        void Summit(Site s,MeshBuild mb)
        {
            var top=SummitOf(s.lm.pos,300f);var b=G(top);var rnd=new System.Random(s.lm.name.GetHashCode());s.at=top;
            mb.Box(Solid,b+Up*.1f,new Vector3(1.8f,.9f,1.2f),Quaternion.identity,C(112,110,104));
            mb.Box(Solid,b+Up*1.3f,new Vector3(1.1f,1.5f,.45f),Quaternion.Euler(0,0,1.5f),C(156,152,144));
            mb.Box(Solid,b+Up*2.13f,new Vector3(.95f,.18f,.4f),Quaternion.identity,C(146,142,134));
            for(int k=0;k<7;k++){
                float a=(float)rnd.NextDouble()*Mathf.PI*2,r=2f+(float)rnd.NextDouble()*1.5f,sz=.25f+(float)rnd.NextDouble()*.35f;var p=G(top+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r);
                mb.Box(Plain,p+Up*sz*.3f,new Vector3(sz*1.3f,sz,sz),Quaternion.Euler(0,a*57f,0),C(128,124,116));
            }
            string text=s.lm.name+"\n"+s.lm.height.ToString("0.#",CultureInfo.InvariantCulture)+"m";var ink=new Color(.1f,.1f,.1f);
            Sign(text,s.go.transform,b+Up*1.35f+Vector3.back*.235f,Vector3.back,.2f,ink);
            Sign(text,s.go.transform,b+Up*1.35f+Vector3.forward*.235f,Vector3.forward,.2f,ink);
        }
    }
}
