using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace PeninsulaTime
{
    // Trains on the real Changwon rail lines (경전선, 진해선 …): platforms with a ticket machine at stations near the
    // player, passenger rides along the track to any connected station, and a couple of passing trains for atmosphere.
    public class ChangwonTrains : MonoBehaviour, IChangwonUse, IChangwonRide
    {
        const float MaxSpeed=40f;
        class Station {public string name;public Vector2 pos;public int v;public Vector3 track,dir;public float side;public GameObject go;}
        class Train {public GameObject go;public Quaternion baseRot;public Vector3[] pts;public float[] along;public float s,speed,max,park;public int hint;public bool placed;public Vector3 pos,fwd=Vector3.forward;public List<float> rev;}

        // Rail graph: every rail polyline point is a vertex; road end nodes are shared; gaps in the data are bridged.
        List<Vector3> V;List<List<int>> adj;readonly List<Station> stations=new List<Station>();volatile bool ready;
        readonly List<Train> trains=new List<Train>();readonly List<int> candidates=new List<int>();
        Transform holder;Mesh platformMesh;Material mat;float nextCheck,nextAmbient=10f;
        // ride
        Train ride;Station from,to;bool chase;float lookYaw,lookPitch,hudAt;Vector3 chasePos;string hud="";
        // destination dialog
        Station dialog;readonly List<KeyValuePair<Station,float>> choices=new List<KeyValuePair<Station,float>>();float[] dist;int[] prev;Vector2 scroll;

        static float Flat(Vector3 v){return v.x*v.x+v.z*v.z;}
        static long Key(Vector3 p){return ((long)Mathf.FloorToInt(p.x/50f)<<32)^(uint)Mathf.FloorToInt(p.z/50f);}
        static int Find(int[] parent,int x){while(parent[x]!=x){parent[x]=parent[parent[x]];x=parent[x];}return x;}
        // "경화역 벚꽃길" -> "경화역", "진영역 / Jinyeong Stn." -> "진영역"; null when no word ends in 역.
        static string Clean(string raw){foreach(var w in raw.Split(' ','(',')','/',','))if(w.Length>1&&w.EndsWith("역"))return w;return null;}

        void Start()
        {
            holder=new GameObject("철도").transform;holder.SetParent(transform,false);holder.gameObject.AddComponent<ChangwonUseRelay>().owner=this;
            ChangwonSession.Overlay+=Draw;
            Debug.Assert(Clean("경화역 벚꽃길")=="경화역"&&Clean("진영역 / Jinyeong Stn.")=="진영역"&&Clean("Haman Station")==null,"Clean");
            var zig=new[]{Vector3.zero,new Vector3(0,0,100),new Vector3(3,0,40)};var zr=Reversals(zig,new[]{0f,100f,160.07f});
            Debug.Assert(zr.Count==1&&zr[0]==100f&&Reversals(new[]{Vector3.zero,new Vector3(0,0,50),new Vector3(10,0,99)},new[]{0f,50f,100f}).Count==0,"Reversals");
            new Thread(()=>{try{Build();ready=true;}catch(System.Exception e){Debug.LogWarning("Changwon trains: "+e);}}){IsBackground=true,Name="Changwon rail graph"}.Start();
        }
        void OnDestroy()
        {
            ChangwonSession.Overlay-=Draw;
            if(ReferenceEquals(ChangwonSession.Ride,this))ChangwonSession.Ride=null;
            if(dialog!=null)ChangwonSession.UiCapture=false;
            if(platformMesh!=null)Destroy(platformMesh);if(mat!=null)Destroy(mat);
        }

        // Worker thread.
        void Build()
        {
            V=new List<Vector3>();adj=new List<List<int>>();var nodeVertex=new Dictionary<int,int>();
            System.Func<Vector3,int> add=p=>{V.Add(p);adj.Add(new List<int>(2));return V.Count-1;};
            System.Action<int,int> join=(a,b)=>{if(a==b)return;adj[a].Add(b);adj[b].Add(a);};
            foreach(var road in ChangwonData.Roads)
            {
                if(!road.IsRail||road.pts.Length<2)continue;var pts=road.pts;int n=pts.Length;
                int last;if(!nodeVertex.TryGetValue(road.a,out last))nodeVertex[road.a]=last=add(pts[0]);
                for(int k=1;k<n-1;k++){int v=add(pts[k]);join(last,v);last=v;}
                int end;if(!nodeVertex.TryGetValue(road.b,out end))nodeVertex[road.b]=end=add(pts[n-1]);
                join(last,end);
            }
            // The rail data breaks into ~80 pieces (yards, missing junction nodes); join pieces whose tracks pass within
            // 40 m of each other, closest first, once per pair of pieces. This links 창원·창원중앙·마산·진해 into one network.
            int count=V.Count;var parent=new int[count];for(int i=0;i<count;i++)parent[i]=i;
            for(int u=0;u<count;u++)foreach(int v in adj[u])parent[Find(parent,u)]=Find(parent,v);
            var grid=new Dictionary<long,List<int>>();
            for(int i=0;i<count;i++){long k=Key(V[i]);List<int> l;if(!grid.TryGetValue(k,out l))grid[k]=l=new List<int>();l.Add(i);}
            var pairs=new List<KeyValuePair<float,long>>();
            for(int u=0;u<count;u++)for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
            {
                List<int> l;if(!grid.TryGetValue(Key(V[u]+new Vector3(dx*50f,0,dz*50f)),out l))continue;
                foreach(int v in l)
                {
                    if(v<=u||Find(parent,u)==Find(parent,v)||Mathf.Abs(V[u].y-V[v].y)>5f)continue;
                    float d=Vector3.Distance(V[u],V[v]);if(d<40f)pairs.Add(new KeyValuePair<float,long>(d,((long)u<<32)|(uint)v));
                }
            }
            pairs.Sort((a,b)=>a.Key.CompareTo(b.Key));
            foreach(var pr in pairs){int u=(int)(pr.Value>>32),v=(int)(pr.Value&0xffffffff);int a=Find(parent,u),b=Find(parent,v);if(a!=b){parent[a]=b;join(u,v);}}
            // Curated stations first, then station places named …역 that lie on a track.
            foreach(var lm in ChangwonLandmarks.All)if(lm.kind=="station")AddStation(Clean(lm.name)??lm.name,lm.pos,400f);
            foreach(var pl in ChangwonData.Places)if(pl.kind=="station"&&!pl.name.Contains("폐역")){var name=Clean(pl.name);if(name!=null)AddStation(name,pl.pos,150f);}
        }
        // A station becomes a vertex on the nearest track segment (split in two), so rides start and end exactly there.
        void AddStation(string name,Vector2 pos,float reach)
        {
            foreach(var s in stations)if(s.name==name||(s.pos-pos).sqrMagnitude<500f*500f)return;
            float best=reach*reach,bt=0;int bu=-1,bv=-1;int count=V.Count;
            for(int u=0;u<count;u++)foreach(int v in adj[u])
            {
                if(v<u)continue;Vector3 a=V[u],b=V[v];float abx=b.x-a.x,abz=b.z-a.z,l2=abx*abx+abz*abz;
                float t=l2>1e-4f?Mathf.Clamp01(((pos.x-a.x)*abx+(pos.y-a.z)*abz)/l2):0,qx=a.x+abx*t-pos.x,qz=a.z+abz*t-pos.y,d=qx*qx+qz*qz;
                if(d<best){best=d;bu=u;bv=v;bt=t;}
            }
            if(bu<0)return;
            var track=Vector3.Lerp(V[bu],V[bv],bt);int sv=V.Count;V.Add(track);adj.Add(new List<int>{bu,bv});adj[bu].Add(sv);adj[bv].Add(sv);
            var dir=V[bv]-V[bu];dir.y=0;dir=dir.sqrMagnitude>1e-4f?dir.normalized:Vector3.forward;
            var right=new Vector3(dir.z,0,-dir.x);var toBuilding=new Vector3(pos.x-track.x,0,pos.y-track.z);
            stations.Add(new Station{name=name,pos=pos,v=sv,track=track,dir=dir,side=Vector3.Dot(toBuilding,right)>=0?1f:-1f});
        }
        void Dijkstra(int source)
        {
            int n=V.Count;dist=new float[n];prev=new int[n];for(int i=0;i<n;i++){dist[i]=float.MaxValue;prev[i]=-1;}
            var open=new SortedSet<KeyValuePair<float,int>>(Comparer<KeyValuePair<float,int>>.Create((x,y)=>x.Key!=y.Key?x.Key.CompareTo(y.Key):x.Value.CompareTo(y.Value)));
            dist[source]=0;open.Add(new KeyValuePair<float,int>(0,source));
            while(open.Count>0)
            {
                var top=open.Min;open.Remove(top);int u=top.Value;if(top.Key>dist[u])continue;
                foreach(int v in adj[u]){float d=dist[u]+Vector3.Distance(V[u],V[v]);if(d<dist[v]){dist[v]=d;prev[v]=u;open.Add(new KeyValuePair<float,int>(d,v));}}
            }
        }

        void Update()
        {
            if(!ready||!ChangwonSession.Active)return;
            if(dialog!=null&&Input.GetKeyDown(KeyCode.Escape))Close();
            if(ride!=null&&!ReferenceEquals(ChangwonSession.Ride,this)){Destroy(ride.go);ride=null;} // the ride was ended elsewhere
            float dt=Time.deltaTime;var p=ChangwonSession.PlayerFeet;
            for(int i=trains.Count-1;i>=0;i--)
            {
                var t=trains[i];
                if(t.go==null){trains.RemoveAt(i);continue;}
                if(t.park>0){if(Time.time>t.park){Destroy(t.go);trains.RemoveAt(i);}continue;}
                t.s+=t.speed*dt;Place(t,dt);
                if(t.s>=t.along[t.along.Length-1]||Flat(t.pos-p)>2500f*2500f){Destroy(t.go);trains.RemoveAt(i);}
            }
            if(Time.time>=nextCheck)
            {
                nextCheck=Time.time+1f;
                foreach(var st in stations){float d=Flat(st.track-p);if(st.go==null&&d<600f*600f)Platform(st);else if(st.go!=null&&d>800f*800f){Destroy(st.go);st.go=null;}}
                if(ride==null&&Time.time>=nextAmbient){nextAmbient=Time.time+Random.Range(20f,40f);Ambient(p);}
            }
        }

        // ------------------------------------------------------------------ trains
        Train MakeTrain(List<Vector3> path,string name,float max)
        {
            var B=ChangwonSession.Builder;if(B==null||path.Count<2)return null;
            var go=B.CityModel("Ktx",path[0]);if(go==null)return null;
            go.name=name;go.transform.SetParent(holder,true);
            var rs=go.GetComponentsInChildren<Renderer>();
            if(rs.Length>0)
            {
                var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);
                var box=go.AddComponent<BoxCollider>();box.center=go.transform.InverseTransformPoint(bounds.center);
                var size=go.transform.InverseTransformVector(bounds.size);box.size=new Vector3(Mathf.Abs(size.x),Mathf.Abs(size.y),Mathf.Abs(size.z));
            }
            var t=new Train{go=go,baseRot=go.transform.rotation,pts=path.ToArray(),max=max};
            t.along=new float[t.pts.Length];for(int i=1;i<t.pts.Length;i++)t.along[i]=t.along[i-1]+Vector3.Distance(t.pts[i-1],t.pts[i]);
            t.rev=Reversals(t.pts,t.along);
            Sfx.Attach(go,"rumble",.4f,80f);Place(t,0);return t;
        }
        // Where a ride path doubles back (the shortest route runs through a crossover or a wye), measured over ±15 m.
        static List<float> Reversals(Vector3[] pts,float[] along)
        {
            var list=new List<float>();float lastDot=0;int h1=0,h2=0;
            for(int i=1;i<pts.Length-1;i++)
            {
                var a=pts[i]-ChangwonBuses.PathAt(pts,along,along[i]-15f,ref h1);var b=ChangwonBuses.PathAt(pts,along,along[i]+15f,ref h2)-pts[i];a.y=b.y=0;
                if(a.sqrMagnitude<1f||b.sqrMagnitude<1f)continue;
                float dot=Vector3.Dot(a.normalized,b.normalized);if(dot>-.3f)continue;
                if(list.Count>0&&along[i]-list[list.Count-1]<20f){if(dot<lastDot){list[list.Count-1]=along[i];lastDot=dot;}continue;}
                list.Add(along[i]);lastDot=dot;
            }
            return list;
        }
        // Next reversal (or the end) after s.
        static float NextStop(Train t){if(t.rev!=null)foreach(float r in t.rev)if(r>t.s+.01f)return r;return t.along[t.along.Length-1];}
        void Place(Train t,float dt)
        {
            var p=ChangwonBuses.PathAt(t.pts,t.along,t.s,ref t.hint);
            // The heading chord stays on the current leg, so it never straddles a reversal.
            float lo=0,hi=NextStop(t);if(t.rev!=null)foreach(float r in t.rev)if(r<=t.s+.01f)lo=r;
            int h1=t.hint,h2=t.hint;var f=ChangwonBuses.PathAt(t.pts,t.along,Mathf.Min(t.s+20f,hi),ref h1)-ChangwonBuses.PathAt(t.pts,t.along,Mathf.Max(t.s-20f,lo),ref h2);
            f=f.sqrMagnitude>.01f?f.normalized:t.fwd;
            t.pos=p+Vector3.up*.3f;t.fwd=f;
            var rot=Quaternion.LookRotation(f)*t.baseRot;
            if(t.placed&&Quaternion.Angle(t.go.transform.rotation,rot)>90f)rot=Quaternion.LookRotation(-f)*t.baseRot; // cab at both ends: reverse, don't spin
            t.go.transform.SetPositionAndRotation(t.pos,t.placed?Quaternion.Slerp(t.go.transform.rotation,rot,1f-Mathf.Exp(-6f*dt)):rot);t.placed=true;
        }
        // A passing train: starts 0.7–1.5 km away on a plain stretch and follows the straightest track for up to 5 km.
        void Ambient(Vector3 p)
        {
            int moving=0;foreach(var t in trains)if(t.park<=0)moving++;
            if(moving>=(PerformanceRuntime.Level==0?1:2))return;
            candidates.Clear();
            for(int i=0;i<V.Count;i++){if(adj[i].Count!=2)continue;float d=Flat(V[i]-p);if(d>700f*700f&&d<1500f*1500f)candidates.Add(i);}
            if(candidates.Count==0)return;
            int start=candidates[Random.Range(0,candidates.Count)];
            var path=Walk(start,adj[start][Random.Range(0,2)],5000f);
            if(path.Count<3)return;
            var train=MakeTrain(path,"운행 중인 열차",Random.Range(20f,30f));
            if(train!=null){train.speed=train.max;trains.Add(train);}
        }
        List<Vector3> Walk(int last,int cur,float length)
        {
            var path=new List<Vector3>{V[last]};var seen=new HashSet<int>{last};float total=0;
            while(total<length)
            {
                path.Add(V[cur]);seen.Add(cur);total+=Vector3.Distance(V[last],V[cur]);
                var dir=V[cur]-V[last];dir.y=0;dir.Normalize();int next=-1;float bestDot=.5f;
                foreach(int v in adj[cur]){if(seen.Contains(v))continue;var d=V[v]-V[cur];d.y=0;if(d.sqrMagnitude<1e-4f)continue;float dot=Vector3.Dot(dir,d.normalized);if(dot>bestDot){bestDot=dot;next=v;}}
                if(next<0)break;last=cur;cur=next;
            }
            return path;
        }

        // ------------------------------------------------------------------ platforms
        void Platform(Station st)
        {
            if(st.go!=null)return;
            var go=new GameObject(st.name+" 승강장");go.transform.SetParent(holder,false);
            go.transform.SetPositionAndRotation(st.track,Quaternion.LookRotation(st.side>0?st.dir:-st.dir)); // local +x: the platform side
            go.AddComponent<MeshFilter>().sharedMesh=PlatformMesh();go.AddComponent<MeshRenderer>().sharedMaterial=PropMaterial();
            var slab=new GameObject("platform");slab.transform.SetParent(go.transform,false); // the name lets the player step up and cars drive on it
            var box=slab.AddComponent<BoxCollider>();box.center=new Vector3(4f,-1f,0);box.size=new Vector3(4f,4f,80f);
            var kiosk=new GameObject("승차권 발매기");kiosk.transform.SetParent(go.transform,false);kiosk.transform.localPosition=new Vector3(5.3f,1.9f,0);
            kiosk.AddComponent<BoxCollider>().size=new Vector3(.9f,1.8f,1.2f);
            var thing=kiosk.AddComponent<ChangwonThing>();thing.kind="train";thing.hint="KTX·무궁화 타기 (F)";thing.title=st.name;thing.payload=st;
            var B=ChangwonSession.Builder;
            if(B!=null)
            {
                var t=go.transform;
                B.ChangwonSign(st.name,t,t.TransformPoint(new Vector3(4f,3.7f,8f)),-t.right,.7f,Color.white);
                B.ChangwonSign(st.name,t,t.TransformPoint(new Vector3(4f,3.7f,-8f)),t.right,.7f,Color.white);
                B.ChangwonSign("KTX·무궁화 승차권",t,t.TransformPoint(new Vector3(4.8f,3.05f,0)),-t.right,.24f,new Color(1f,.85f,.4f));
                // On an embankment the top can be more than a step (1.25 m) above the ground: stairs beside the ticket machine.
                var foot=t.TransformPoint(new Vector3(6.6f,0,0));float drop=1f-(ChangwonData.Height(foot.x,foot.z)-st.track.y);int n=Mathf.CeilToInt(drop/1.1f)-1;
                for(int i=1;i<=n&&i<6;i++){float top=1f-drop*i/(n+1);B.ChangwonBlock("platform",t,new Vector3(4.8f+i*1.2f,top-4f,-4f),new Vector3(6f+i*1.2f,top,4f),B.ChangwonMaterial("platform-step",new Color(.6f,.6f,.57f),"concrete"));}
            }
            st.go=go;
        }
        Material PropMaterial()
        {
            if(mat==null){var sh=Shader.Find("Peninsula/VertexTerrain");mat=sh!=null?new Material(sh):new Material(Shader.Find("Standard"));mat.name="승강장 (정점 색)";}
            return mat;
        }
        // 80 m platform 1 m above the rails on the +x side, yellow safety line, canopy and a ticket machine.
        Mesh PlatformMesh()
        {
            if(platformMesh!=null)return platformMesh;
            var mb=new MeshBuild(1);var q=Quaternion.identity;
            mb.Box(0,new Vector3(4f,-1f,0),new Vector3(4f,4f,80f),q,new Color32(168,166,158,255)); // 3 m deep so it reaches the ground on embankments
            mb.Box(0,new Vector3(2.3f,1.01f,0),new Vector3(.3f,.02f,80f),q,new Color32(235,190,40,255));
            for(float z=-30f;z<=30f;z+=10f)mb.Box(0,new Vector3(5.2f,2.6f,z),new Vector3(.18f,3.2f,.18f),q,new Color32(90,96,104,255));
            mb.Box(0,new Vector3(4.4f,4.3f,0),new Vector3(3.8f,.18f,66f),q,new Color32(70,92,120,255),true);
            mb.Box(0,new Vector3(5.3f,1.9f,0),new Vector3(.9f,1.8f,1.2f),q,new Color32(30,100,200,255));
            return platformMesh=mb.ToMesh("승강장");
        }

        // ------------------------------------------------------------------ riding
        public bool Handles(ChangwonThing thing){return thing!=null&&thing.kind=="train";}
        public void Use(ChangwonThing thing)
        {
            if(!Handles(thing)){ChangwonBuses.PassOn(this,thing);return;}
            var st=thing.payload as Station;if(st==null||!ready||ride!=null||ChangwonSession.Ride!=null)return;
            Dijkstra(st.v);choices.Clear();
            foreach(var s in stations)if(s!=st&&dist[s.v]<float.MaxValue&&dist[s.v]>400f)choices.Add(new KeyValuePair<Station,float>(s,dist[s.v]));
            choices.Sort((a,b)=>a.Value.CompareTo(b.Value));
            dialog=st;scroll=Vector2.zero;ChangwonSession.UiCapture=true;Sfx.Play("tap",.4f);
        }
        void Close(){dialog=null;ChangwonSession.UiCapture=false;}
        void Depart(Station a,Station b)
        {
            var path=new List<Vector3>();int v=b.v;
            while(v>=0){path.Add(V[v]);if(v==a.v)break;v=prev[v];}
            if(v!=a.v){ChangwonSession.Toast("선로가 이어지지 않습니다");return;}
            path.Reverse();
            var t=MakeTrain(path,"KTX "+a.name+" → "+b.name,MaxSpeed);if(t==null){ChangwonSession.Toast("열차를 준비하지 못했습니다");return;}
            ride=t;from=a;to=b;chase=false;lookYaw=lookPitch=0;hudAt=0;ChangwonSession.Ride=this;
            ChangwonSession.Toast(a.name+" → "+b.name+" 열차가 출발합니다 · T 시점 · Space 빨리 가기");Sfx.Play("chime",.6f);
        }
        void Arrive()
        {
            var t=ride;var st=to;ride=null;if(ReferenceEquals(ChangwonSession.Ride,this))ChangwonSession.Ride=null;
            t.park=Time.time+20f;trains.Add(t); // stands at the platform for a while
            Platform(st);
            var right=new Vector3(st.dir.z,0,-st.dir.x)*st.side;var facing=new Vector3(t.fwd.x,0,t.fwd.z);
            ChangwonSession.TeleportPlayer?.Invoke(st.track+right*4f+Vector3.up*1.05f,facing.sqrMagnitude>1e-4f?facing.normalized:st.dir);
            ChangwonSession.Progress.score+=40;ChangwonSession.Toast(st.name+"에 도착했습니다 · +40점");Sfx.Play("arrival",.6f);
        }
        public Vector3 Eye{get{return ride!=null?ride.pos+Vector3.up*2.2f:ChangwonSession.PlayerFeet+Vector3.up*1.65f;}}
        public string Hud{get{
            if(ride==null)return "";
            if(Time.unscaledTime>=hudAt){hudAt=Time.unscaledTime+.25f;hud=from.name+" → "+to.name+" · 남은 거리 "+((ride.along[ride.along.Length-1]-ride.s)/1000f).ToString("0.0")+" km · "+Mathf.RoundToInt(ride.speed*3.6f)+" km/h";}
            return hud;}}
        public void UpdateRide(Camera camera,float dt)
        {
            var t=ride;if(t==null||t.go==null){ride=null;if(ReferenceEquals(ChangwonSession.Ride,this))ChangwonSession.Ride=null;return;}
            if(Input.GetKeyDown(KeyCode.T)){chase=!chase;chasePos=camera.transform.position;}
            float step=Input.GetKey(KeyCode.Space)?dt*4f:dt; // long trips: hold Space to fast-forward
            float end=t.along[t.along.Length-1],stop=NextStop(t),remain=stop-t.s; // brakes for each reversal as for the terminus
            float target=Mathf.Min(MaxSpeed,Mathf.Sqrt(2f*.9f*Mathf.Max(0,remain))+.5f);
            t.speed=Mathf.MoveTowards(t.speed,target,(target>t.speed?.9f:2.5f)*step);
            t.s=Mathf.Min(stop,t.s+t.speed*step);Place(t,step);
            if(end-t.s<.3f){Arrive();return;}
            lookYaw=Mathf.Clamp(lookYaw+Input.GetAxis("Mouse X")*2.2f,-160f,160f);lookPitch=Mathf.Clamp(lookPitch-Input.GetAxis("Mouse Y")*1.6f,-35f,35f);
            var flat=new Vector3(t.fwd.x,0,t.fwd.z);flat=flat.sqrMagnitude>1e-4f?flat.normalized:Vector3.forward;var cam=camera.transform;
            // The end wall is at the nose of this model. Put the passenger beside a side window.
            if(!chase)
            {
                var right=new Vector3(flat.z,0,-flat.x);
                cam.position=t.pos+Vector3.up*2.25f+t.fwd*3f+right*1.1f;
                cam.rotation=Quaternion.LookRotation((flat+right*.7f).normalized)*Quaternion.Euler(lookPitch,lookYaw,0);
            }
            else
            {
                var orbit=Quaternion.LookRotation(flat)*Quaternion.Euler(14f+lookPitch*.5f,lookYaw,0);var want=t.pos+Vector3.up*3f-orbit*Vector3.forward*60f;
                if(t.pos.y>ChangwonData.Height(t.pos.x,t.pos.z)-2f)
                    want.y=Mathf.Max(want.y,ChangwonData.Height(want.x,want.z)+2f);
                chasePos=Vector3.Lerp(chasePos,want,1f-Mathf.Exp(-5f*dt));
                cam.position=chasePos;cam.rotation=Quaternion.LookRotation(t.pos+Vector3.up*2f+flat*10f-chasePos);
            }
        }
        void Draw(float w,float h)
        {
            var st=dialog;if(st==null)return;
            float bw=Mathf.Min(640f,w-40f),bh=Mathf.Min(560f,h-40f);var box=new Rect((w-bw)*.5f,(h-bh)*.5f,bw,bh);
            GUI.Box(box,"",ChangwonSession.Box);
            GUI.Label(new Rect(box.x+24,box.y+16,bw-48,40),st.name+" · 열차 타기",ChangwonSession.Title);
            GUI.Label(new Rect(box.x+24,box.y+58,bw-48,24),"도착역을 고르세요 · 실제 선로를 따라 달립니다 · 탑승 중 Space 빨리 가기",ChangwonSession.Small);
            var view=new Rect(box.x+16,box.y+90,bw-32,bh-170);
            if(choices.Count==0)GUI.Label(new Rect(view.x+8,view.y+8,view.width-16,60),"이 역에서 선로로 이어진 다른 역이 없습니다.",ChangwonSession.Body);
            scroll=GUI.BeginScrollView(view,scroll,new Rect(0,0,view.width-20,choices.Count*56f));
            for(int i=0;i<choices.Count;i++)
            {
                var c=choices[i];float y=i*56f;
                GUI.Label(new Rect(4,y+2,view.width-200,28),c.Key.name,ChangwonSession.Body);
                GUI.Label(new Rect(4,y+30,view.width-200,22),"선로 "+(c.Value/1000f).ToString("0.0")+" km · 약 "+Mathf.Max(1,Mathf.RoundToInt(c.Value/25f/60f))+"분",ChangwonSession.Small);
                if(GUI.Button(new Rect(view.width-174,y+6,150,42),"타기",ChangwonSession.Button)){Close();Depart(st,c.Key);break;}
            }
            GUI.EndScrollView();
            if(dialog!=null&&GUI.Button(new Rect(box.xMax-196,box.yMax-66,176,50),"닫기",ChangwonSession.Accent))Close();
        }
    }
}
