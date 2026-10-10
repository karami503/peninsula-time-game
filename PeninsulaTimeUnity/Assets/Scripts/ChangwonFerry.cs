using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Passenger ferries to the islands: 마산항 ↔ 돝섬 and 창원해양공원(음지도) ↔ 소쿠리섬. Piers appear near the player;
    // F on a pier sign starts a short crossing as a passenger ride.
    public class ChangwonFerry : MonoBehaviour, IChangwonUse, IChangwonRide
    {
        class Pier {public string name,to;public Vector3 land,sea;public float deckY;public GameObject go;public int route;}
        class Route {public string a,b;public Pier pa,pb;}
        readonly List<Route> routes=new List<Route>();readonly List<Pier> piers=new List<Pier>();bool ready;float next;
        GameObject boat;Vector3[] path;float travelled,length,dwell;Pier target;string hud="";float camYaw;bool outside=true;

        public Vector3 Eye{get{return boat!=null?boat.transform.position+Vector3.up*4.2f:ChangwonSession.PlayerFeet;}}
        public string Hud{get{return hud;}}
        public bool Handles(ChangwonThing thing){return thing.kind=="ferry";}

        void Update()
        {
            if(!ChangwonSession.Active||!ChangwonData.Loaded)return;
            if(!ready){ready=true;Setup();}
            if(Time.time<next)return;next=Time.time+1f;
            var p=ChangwonSession.PlayerFeet;
            foreach(var pier in piers){
                bool near=(pier.land-p).sqrMagnitude<900f*900f;
                if(near&&pier.go==null)pier.go=BuildPier(pier);else if(!near&&pier.go!=null){Destroy(pier.go);pier.go=null;}
            }
        }
        void Setup()
        {
            Link("마산항","돝섬");Link("창원해양공원","소쿠리섬");
        }
        void Link(string from,string to)
        {
            ChangwonLandmarks.Landmark a=null,b=null;
            foreach(var lm in ChangwonLandmarks.All){if(a==null&&lm.name.Contains(from))a=lm;if(b==null&&lm.name.Contains(to))b=lm;}
            if(a==null||b==null)return;
            Pier pa,pb;if(!Coast(a.pos,b.pos,out pa)||!Coast(b.pos,a.pos,out pb))return;
            pa.name=a.name;pa.to=b.name;pb.name=b.name;pb.to=a.name;
            var r=new Route{a=a.name,b=b.name,pa=pa,pb=pb};pa.route=pb.route=routes.Count;routes.Add(r);piers.Add(pa);piers.Add(pb);
        }
        // March from a landmark toward the other end until the sea starts: the pier sits at the last land point. Headings are
        // fanned out until the landing is clear of bridges (a coastal road on a viaduct has parapets nobody can step over).
        static bool Coast(Vector2 from,Vector2 toward,out Pier pier)
        {
            pier=null;var straight=(toward-from).normalized;
            foreach(float turn in new[]{0f,12f,-12f,24f,-24f,36f,-36f,48f,-48f}){
                var dir=(Vector2)(Quaternion.Euler(0,0,turn)*straight);Vector2 lastLand=from;bool sawLand=!Wet(from.x,from.y);
                for(float s=0;s<3000f;s+=4f){
                    var q=from+dir*s;if(!Wet(q.x,q.y)){lastLand=q;sawLand=true;continue;}
                    if(!sawLand)continue;
                    var land=new Vector3(lastLand.x,ChangwonData.Height(lastLand.x,lastLand.y),lastLand.y);var seaEnd=land+new Vector3(dir.x,0,dir.y)*28f;seaEnd.y=1.2f;
                    var approach=lastLand-dir*6f;var found=new Pier{land=land,sea=seaEnd,deckY=Mathf.Max(1.2f,ChangwonData.Height(approach.x,approach.y))};
                    if(pier==null)pier=found;
                    if(!NearBridge(lastLand,14f)&&!NearBridge(approach,10f)){pier=found;return true;}
                    break;
                }
            }
            return pier!=null;
        }
        static bool NearBridge(Vector2 p,float r)
        {
            var c=ChangwonData.ChunkOf(p.x,p.y);
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++){
                int x=c.x+dx,z=c.y+dz;if(x<0||z<0||x>=ChangwonData.CX||z>=ChangwonData.CZ)continue;
                foreach(int id in ChangwonData.RoadsInChunk[z*ChangwonData.CX+x]){
                    var rd=ChangwonData.Roads[id];
                    for(int i=1;i<rd.pts.Length;i++){
                        if(rd.kind[i]==ChangwonData.Ground&&rd.kind[i-1]==ChangwonData.Ground)continue;
                        Vector2 a=new Vector2(rd.pts[i-1].x,rd.pts[i-1].z),b=new Vector2(rd.pts[i].x,rd.pts[i].z),ab=b-a;
                        float t=Mathf.Clamp01(Vector2.Dot(p-a,ab)/Mathf.Max(.01f,ab.sqrMagnitude));
                        if((a+ab*t-p).sqrMagnitude<(r+rd.width*.5f)*(r+rd.width*.5f))return true;
                    }
                }
            }
            return false;
        }
        GameObject BuildPier(Pier pier)
        {
            var go=new GameObject("여객선 부두 · "+pier.name);go.transform.SetParent(ChangwonSession.Root,false);
            var mb=new MeshBuild(1);var dir=pier.sea-pier.land;dir.y=0;float len=dir.magnitude;dir/=Mathf.Max(.01f,len);var rot=Quaternion.LookRotation(dir);
            // The deck starts 6 m inland, flush with the ground there, so the shore (often a steep quay) walks straight onto it.
            var approach=pier.land-dir*6f;float deckY=pier.deckY;var mid=approach+dir*((len+6f)*.5f);mid.y=deckY-.25f;
            mb.Box(0,mid,new Vector3(4.5f,.5f,len+6f),rot,new Color32(150,120,90,255),true);
            for(float s=0;s<=len;s+=7f){var post=pier.land+dir*s;float floor=Mathf.Min(ChangwonData.Height(post.x,post.z),-.5f);post.y=(deckY-.5f+floor)*.5f;mb.Box(0,post,new Vector3(.5f,deckY-.5f-floor,.5f),rot,new Color32(90,72,56,255));}
            var side=new Vector3(dir.z,0,-dir.x);var signAt=pier.land+dir*2f+side*2.9f;signAt.y=deckY;
            mb.Box(0,signAt+Vector3.up*.6f,new Vector3(.2f,1.2f,.2f),rot,new Color32(70,70,74,255));
            var o=new GameObject("부두");o.transform.SetParent(go.transform,false);var mesh=mb.ToMesh("부두");o.AddComponent<MeshFilter>().sharedMesh=mesh;
            var sh=Shader.Find("Peninsula/VertexTerrain");o.AddComponent<MeshRenderer>().sharedMaterial=sh!=null?new Material(sh):new Material(Shader.Find("Standard"));
            o.AddComponent<MeshCollider>().sharedMesh=mesh;o.name="platform"; // walkable, and climbable from the shore like a kerb
            var board=GameObject.CreatePrimitive(PrimitiveType.Cube);board.name="여객선 안내판";board.transform.SetParent(go.transform,false);
            board.transform.position=signAt+Vector3.up*1.7f;board.transform.rotation=rot;board.transform.localScale=new Vector3(2.4f,1.1f,.12f);
            var mat=ChangwonSession.Builder.ChangwonMaterial("ferry-board",new Color(.12f,.32f,.55f));board.GetComponent<Renderer>().sharedMaterial=mat;
            var thing=board.AddComponent<ChangwonThing>();thing.kind="ferry";thing.hint=pier.to+" 가는 배 타기 (F)";thing.payload=pier;
            ChangwonSession.Builder.ChangwonSign(pier.to+" 가는 배",go.transform,signAt+Vector3.up*2.7f,-dir,.55f,Color.white);
            Physics.SyncTransforms();return go;
        }
        public void Use(ChangwonThing thing)
        {
            var from=thing.payload as Pier;if(from==null||ChangwonSession.Ride!=null)return;
            var r=routes[from.route];target=from==r.pa?r.pb:r.pa;
            // Straight crossing between the pier heads, nudged around land with a detour point if needed.
            var a=from.sea;var b=target.sea;a.y=b.y=0;path=SeaRoute(a,b).ToArray();
            length=0;for(int i=1;i<path.Length;i++)length+=Vector3.Distance(path[i-1],path[i]);
            travelled=0;dwell=3f;outside=true;camYaw=0;
            if(boat==null)boat=BuildBoat();boat.SetActive(true);Place(0);
            if(ChangwonSession.LeaveCar!=null&&ChangwonSession.PlayerCar!=null)ChangwonSession.LeaveCar();
            ChangwonSession.Ride=this;Sfx.Play("bell",.7f);ChangwonSession.Toast(target.name+"행 여객선에 탔습니다 · T: 시점 · 도착하면 자동으로 내립니다");
        }
        // Shortest water route on a 16 m grid (A*, 8-neighbour), then pulled taut: islands and reclaimed land sit between piers.
        static List<Vector3> SeaRoute(Vector3 a,Vector3 b)
        {
            var straight=new List<Vector3>{a,b};if(SeaLine(a,b))return straight;
            const float C=16f,Pad=2500f;float x0=Mathf.Min(a.x,b.x)-Pad,z0=Mathf.Min(a.z,b.z)-Pad;
            int nx=(int)((Mathf.Abs(a.x-b.x)+2*Pad)/C)+1,nz=(int)((Mathf.Abs(a.z-b.z)+2*Pad)/C)+1,n=nx*nz;
            int Cell(Vector3 p){return Mathf.Clamp(Mathf.RoundToInt((p.z-z0)/C),0,nz-1)*nx+Mathf.Clamp(Mathf.RoundToInt((p.x-x0)/C),0,nx-1);}
            Vector3 At(int c){return new Vector3(x0+(c%nx)*C,0,z0+(c/nx)*C);}
            var wet=new sbyte[n];bool Open(int c){if(wet[c]==0)wet[c]=(sbyte)(Clear(At(c),a,b)?1:-1);return wet[c]>0;}
            var cost=new float[n];var came=new int[n];for(int i=0;i<n;i++){cost[i]=float.MaxValue;came[i]=-1;}
            int start=Cell(a),goal=Cell(b);cost[start]=0;var heap=new List<KeyValuePair<float,int>>{new KeyValuePair<float,int>(0,start)};
            int[] dx={1,-1,0,0,1,1,-1,-1},dz={0,0,1,-1,1,-1,1,-1};float[] dc={C,C,C,C,C*1.414f,C*1.414f,C*1.414f,C*1.414f};
            while(heap.Count>0){
                var top=heap[0];int last=heap.Count-1;heap[0]=heap[last];heap.RemoveAt(last);
                for(int i=0;;){int l=i*2+1,r=l+1,m=i;if(l<heap.Count&&heap[l].Key<heap[m].Key)m=l;if(r<heap.Count&&heap[r].Key<heap[m].Key)m=r;if(m==i)break;var t=heap[i];heap[i]=heap[m];heap[m]=t;i=m;}
                int c=top.Value;if(c==goal)break;if(top.Key-Vector3.Distance(At(c),b)>cost[c]+.01f)continue;
                int cx=c%nx,cz=c/nx;
                for(int k=0;k<8;k++){
                    int ex=cx+dx[k],ez=cz+dz[k];if(ex<0||ez<0||ex>=nx||ez>=nz)continue;int e=ez*nx+ex;
                    if(e!=goal&&!Open(e))continue;float g=cost[c]+dc[k];if(g>=cost[e])continue;cost[e]=g;came[e]=c;
                    heap.Add(new KeyValuePair<float,int>(g+Vector3.Distance(At(e),b),e));
                    for(int i=heap.Count-1;i>0;){int p=(i-1)/2;if(heap[p].Key<=heap[i].Key)break;var t=heap[i];heap[i]=heap[p];heap[p]=t;i=p;}
                }
            }
            if(came[goal]<0)return straight; // ponytail: no water link found (shouldn't happen for the two island routes); sail straight
            var cells=new List<Vector3>();for(int c=goal;c>=0;c=came[c])cells.Add(At(c));cells.Reverse();cells[0]=a;cells[cells.Count-1]=b;
            var taut=new List<Vector3>{a};int at=0;
            while(at<cells.Count-1){int far=at+1;for(int j=cells.Count-1;j>at+1;j--)if(ClearLine(cells[at],cells[j],a,b)){far=j;break;}taut.Add(cells[far]);at=far;}
            return taut;
        }
        // Water with a 20 m margin to the shore, except next to the piers themselves.
        static bool Clear(Vector3 q,Vector3 a,Vector3 b)
        {
            if(!Wet(q.x,q.z))return false;if((q-a).sqrMagnitude<80f*80f||(q-b).sqrMagnitude<80f*80f)return true;
            return Wet(q.x+20f,q.z)&&Wet(q.x-20f,q.z)&&Wet(q.x,q.z+20f)&&Wet(q.x,q.z-20f);
        }
        static bool ClearLine(Vector3 p,Vector3 q,Vector3 a,Vector3 b){float d=Vector3.Distance(p,q);for(float s=0;s<=d;s+=10f)if(!Clear(Vector3.Lerp(p,q,s/d),a,b))return false;return true;}
        static bool SeaLine(Vector3 a,Vector3 b){float d=Vector3.Distance(a,b);for(float s=0;s<=d;s+=10f){var q=Vector3.Lerp(a,b,s/d);if(!Wet(q.x,q.z))return false;}return true;}
        // Inner Masan Bay is mapped as a water body rather than open sea; boats use both.
        static bool Wet(float x,float z){var l=ChangwonData.LandAt(x,z);return l==ChangwonData.Sea||l==ChangwonData.Water;}
        void Place(float s)
        {
            Vector3 pos=path[0],fwd=Vector3.forward;float acc=0;
            for(int i=1;i<path.Length;i++){float seg=Vector3.Distance(path[i-1],path[i]);if(acc+seg>=s||i==path.Length-1){float t=Mathf.Clamp01((s-acc)/Mathf.Max(.01f,seg));pos=Vector3.Lerp(path[i-1],path[i],t);fwd=(path[i]-path[i-1]).normalized;break;}acc+=seg;}
            float bob=Mathf.Sin(Time.time*1.3f)*.18f;boat.transform.position=pos+Vector3.up*(.2f+bob);
            boat.transform.rotation=Quaternion.LookRotation(fwd)*Quaternion.Euler(Mathf.Sin(Time.time*.9f)*1.5f,0,Mathf.Sin(Time.time*1.1f)*2f);
        }
        public void UpdateRide(Camera camera,float dt)
        {
            if(boat==null){ChangwonSession.Ride=null;return;}
            if(dwell>0)dwell-=dt;else travelled=Mathf.Min(length,travelled+dt*Mathf.Lerp(3f,12f,Mathf.Clamp01(Mathf.Min(travelled,length-travelled)/40f)));
            Place(travelled);
            float left=Mathf.Max(0,(length-travelled)/11f);hud=target.name+"행 여객선 · 도착까지 약 "+Mathf.CeilToInt(left)+"초";
            if(Input.GetKeyDown(KeyCode.T))outside=!outside;
            camYaw+=Input.GetAxis("Mouse X")*2.2f;
            var fwd=boat.transform.forward;fwd.y=0;fwd.Normalize();
            if(outside){var orbit=Quaternion.Euler(18,Quaternion.LookRotation(fwd).eulerAngles.y+camYaw,0);camera.transform.position=boat.transform.position+Vector3.up*5f-(orbit*Vector3.forward)*22f;camera.transform.LookAt(boat.transform.position+Vector3.up*2f);}
            else{camera.transform.position=boat.transform.position+Vector3.up*4.2f-fwd*3f;camera.transform.rotation=Quaternion.LookRotation(Quaternion.Euler(0,camYaw,0)*fwd);}
            if(travelled>=length-.01f){
                ChangwonSession.Ride=null;boat.SetActive(false);
                var facing=target.land-target.sea;facing.y=0;
                var dock=target.land+(target.sea-target.land).normalized*2f;dock.y=target.deckY+.05f;
                ChangwonSession.TeleportPlayer?.Invoke(dock,facing.sqrMagnitude>0?facing.normalized:Vector3.forward);
                ChangwonSession.Progress.score+=30;Sfx.Play("arrival",.6f);ChangwonSession.Toast(target.name+"에 도착했습니다");
            }
        }
        GameObject BuildBoat()
        {
            var go=new GameObject("여객선");go.transform.SetParent(ChangwonSession.Root,false);var mb=new MeshBuild(1);
            var white=new Color32(236,238,236,255);var blue=new Color32(34,80,140,255);
            mb.Box(0,new Vector3(0,.6f,0),new Vector3(6f,2.2f,20f),Quaternion.identity,blue,true);
            mb.Box(0,new Vector3(0,.8f,11f),new Vector3(3.6f,1.8f,3f),Quaternion.identity,blue);
            mb.Box(0,new Vector3(0,1.75f,0),new Vector3(5.8f,.2f,20f),Quaternion.identity,new Color32(150,150,146,255));
            mb.Box(0,new Vector3(0,3.1f,-2f),new Vector3(5f,2.6f,10f),Quaternion.identity,white);
            mb.Box(0,new Vector3(0,4.6f,-3.5f),new Vector3(3.2f,.5f,4f),Quaternion.identity,white);
            var o=new GameObject("선체");o.transform.SetParent(go.transform,false);o.AddComponent<MeshFilter>().sharedMesh=mb.ToMesh("여객선");
            var sh=Shader.Find("Peninsula/VertexTerrain");o.AddComponent<MeshRenderer>().sharedMaterial=sh!=null?new Material(sh):new Material(Shader.Find("Standard"));
            Sfx.Attach(go,"engine",.4f,80f);return go;
        }
        void OnDestroy(){if(ChangwonSession.Ride==(IChangwonRide)this)ChangwonSession.Ride=null;foreach(var p in piers)if(p.go!=null)Destroy(p.go);}
    }
}
