using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace PeninsulaTime
{
    // GameController asks a thing's parents for an IChangwonUse before asking world.root, and several systems share
    // world.root, so bus and train props sit under a holder carrying one of these that points at their owner.
    public class ChangwonUseRelay : MonoBehaviour, IChangwonUse
    {
        public IChangwonUse owner;
        public bool Handles(ChangwonThing thing){return owner!=null&&owner.Handles(thing);}
        public void Use(ChangwonThing thing){if(owner!=null)owner.Use(thing);}
    }

    // Changwon city buses on the 191 real BIS routes: a few buses run along the routes near the player and stop at
    // every stop, stops near the player get a shelter with an arrival board, and the player can ride as a passenger.
    public class ChangwonBuses : MonoBehaviour, IChangwonUse, IChangwonRide
    {
        const float Cell=250f,Cruise=2f,DwellTime=10f;
        class Stop {public string name;public Vector3 pos,dir;public readonly List<int> routes=new List<int>(),index=new List<int>();public bool kerbed;public Vector3 lane,kerb,side;public GameObject shelter;}
        class Bus {public int route,next,hint;public GameObject go;public ChangwonCar car;public Quaternion baseRot;public float s,speed,cruise,dwell,wait;public bool placed,done,announced;public Vector3 pos,fwd=Vector3.forward;public float[] lat;}

        List<ChangwonData.BusRoute> routes;float[][] stopAlong;int[][] stopOf;
        readonly Dictionary<long,List<int>> segGrid=new Dictionary<long,List<int>>(),stopGrid=new Dictionary<long,List<int>>();
        readonly List<Stop> stops=new List<Stop>();volatile bool ready;
        readonly List<Bus> buses=new List<Bus>();readonly List<Stop> shelters=new List<Stop>();
        readonly List<int> nearby=new List<int>();float[] nearD,nearS;Vector3 nearAt=new Vector3(1e9f,0,0);float nearUntil,nextSpawn,nextShelters;
        Transform holder;Mesh shelterMesh;Material mat;readonly RaycastHit[] hits=new RaycastHit[8];
        // ride
        Bus riding;float leftAt=-100f;bool bell,chase;float lookYaw,lookPitch;Vector3 chasePos;string hud="";int hudKey=-1;
        // arrival board
        Stop board;readonly List<int> rows=new List<int>();Vector2 scroll;

        static int MaxBuses{get{return PerformanceRuntime.Level==0?4:PerformanceRuntime.Level==1?7:10;}}
        static long Key(int cx,int cz){return ((long)cx<<32)^(uint)cz;}
        static long CellKey(float x,float z){return Key(Mathf.FloorToInt(x/Cell),Mathf.FloorToInt(z/Cell));}
        static float Flat(Vector3 v){return v.x*v.x+v.z*v.z;}
        static void Add(Dictionary<long,List<int>> grid,long key,int value){List<int> l;if(!grid.TryGetValue(key,out l))grid[key]=l=new List<int>();l.Add(value);}

        // Point `s` metres along a polyline; `k` is a segment hint kept by the caller so walking along is O(1).
        public static Vector3 PathAt(Vector3[] pts,float[] along,float s,ref int k)
        {
            int n=pts.Length;if(n==1)return pts[0];
            if(k<0||k>n-2)k=0;
            while(k<n-2&&along[k+1]<s)k++;
            while(k>0&&along[k]>s)k--;
            float t=along[k+1]>along[k]?Mathf.Clamp01((s-along[k])/(along[k+1]-along[k])):0;
            return Vector3.Lerp(pts[k],pts[k+1],t);
        }
        // A thing that reached this system but belongs to another one on world.root.
        public static void PassOn(IChangwonUse self,ChangwonThing thing)
        {
            if(ChangwonSession.Root!=null)foreach(var h in ChangwonSession.Root.GetComponents<IChangwonUse>())if(!ReferenceEquals(h,self)&&h.Handles(thing)){h.Use(thing);return;}
            if(!string.IsNullOrEmpty(thing.detail))ChangwonSession.Toast(thing.detail);
        }

        void Start()
        {
            holder=new GameObject("시내버스").transform;holder.SetParent(transform,false);holder.gameObject.AddComponent<ChangwonUseRelay>().owner=this;
            if(GetComponent<ChangwonTrains>()==null)gameObject.AddComponent<ChangwonTrains>(); // GameController only adds the bus system
            ChangwonSession.Overlay+=DrawBoard;
            routes=ChangwonData.Buses;nearD=new float[routes.Count];nearS=new float[routes.Count];
            int k=0;var line=new[]{Vector3.zero,new Vector3(0,0,10),new Vector3(0,0,30)};
            Debug.Assert(PathAt(line,new[]{0f,10f,30f},20f,ref k)==new Vector3(0,0,20)&&k==1,"PathAt");
            new Thread(()=>{try{Index();ready=true;}catch(System.Exception e){Debug.LogWarning("Changwon buses: "+e);}}){IsBackground=true,Name="Changwon bus index"}.Start();
        }
        void OnDestroy()
        {
            ChangwonSession.Overlay-=DrawBoard;
            if(ReferenceEquals(ChangwonSession.Ride,this))ChangwonSession.Ride=null;
            if(board!=null)ChangwonSession.UiCapture=false;
            if(shelterMesh!=null)Destroy(shelterMesh);if(mat!=null)Destroy(mat);
        }

        // Worker thread: route segments and stops on a 250 m grid; stops of different routes sharing a kerb merged.
        void Index()
        {
            int count=routes.Count;stopAlong=new float[count][];stopOf=new int[count][];
            var byName=new Dictionary<string,List<int>>();
            for(int r=0;r<count;r++)
            {
                var route=routes[r];var sh=route.shape;int n=sh.Length;
                for(int k=0;k+1<n&&k<65535;k++)
                {
                    Vector3 a=sh[k],b=sh[k+1];int steps=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z))/100f));long last=long.MinValue;
                    for(int q=0;q<=steps;q++){var p=Vector3.Lerp(a,b,q/(float)steps);long key=CellKey(p.x,p.z);if(key==last)continue;last=key;Add(segGrid,key,(r<<16)|k);}
                }
                int m=route.stopIndex.Length;stopAlong[r]=new float[m];stopOf[r]=new int[m];
                for(int i=0;i<m;i++)
                {
                    int k=route.stopIndex[i];stopAlong[r][i]=route.along[k];
                    var p=sh[k];var d=sh[Mathf.Min(n-1,k+1)]-sh[Mathf.Max(0,k-1)];d.y=0;d=d.sqrMagnitude>1e-4f?d.normalized:Vector3.forward;
                    string name=route.stopNames[i];List<int> same;if(!byName.TryGetValue(name,out same))byName[name]=same=new List<int>();
                    int found=-1;foreach(int c in same){var st=stops[c];if((st.pos-p).sqrMagnitude<35f*35f&&Vector3.Dot(st.dir,d)>0){found=c;break;}}
                    if(found<0){found=stops.Count;stops.Add(new Stop{name=name,pos=p,dir=d});same.Add(found);Add(stopGrid,CellKey(p.x,p.z),found);}
                    var stop=stops[found];if(!stop.routes.Contains(r)){stop.routes.Add(r);stop.index.Add(i);}
                    stopOf[r][i]=found;
                }
            }
        }

        void Update()
        {
            if(!ready||!ChangwonSession.Active)return;
            if(riding!=null&&!ReferenceEquals(ChangwonSession.Ride,this))riding=null; // the ride was ended elsewhere
            float dt=Time.deltaTime;var player=ChangwonSession.PlayerFeet;
            for(int i=buses.Count-1;i>=0;i--)
            {
                var b=buses[i];
                if(b.go==null){buses.RemoveAt(i);continue;}
                if(b==riding)continue; // moved by UpdateRide so the seat camera never lags a frame behind
                Step(b,dt);
                if(b.done||Flat(b.pos-player)>700f*700f){Destroy(b.go);buses.RemoveAt(i);continue;}
                if(b.dwell>1f&&b.dwell<DwellTime-.5f&&CanBoard(b,player))Board(b);
            }
            if(ChangwonSession.Ride!=null&&!ReferenceEquals(ChangwonSession.Ride,this))return; // on a train: no buses or shelters along the line
            if(Time.time>=nextSpawn){nextSpawn=Time.time+1.5f;Nearby(player);Spawn(player);}
            if(Time.time>=nextShelters){nextShelters=Time.time+.7f;Shelters(player);}
        }

        // ------------------------------------------------------------------ buses
        void Nearby(Vector3 p)
        {
            if(Flat(p-nearAt)<60f*60f&&Time.time<nearUntil)return;
            nearAt=p;nearUntil=Time.time+10f;nearby.Clear();
            for(int r=0;r<nearD.Length;r++)nearD[r]=float.MaxValue;
            int cx=Mathf.FloorToInt(p.x/Cell),cz=Mathf.FloorToInt(p.z/Cell);
            for(int dz=-2;dz<=2;dz++)for(int dx=-2;dx<=2;dx++)
            {
                List<int> list;if(!segGrid.TryGetValue(Key(cx+dx,cz+dz),out list))continue;
                foreach(int e in list)
                {
                    int r=e>>16,k=e&0xffff;var route=routes[r];Vector3 a=route.shape[k],b=route.shape[k+1];
                    float abx=b.x-a.x,abz=b.z-a.z,l2=abx*abx+abz*abz,t=l2>1e-4f?Mathf.Clamp01(((p.x-a.x)*abx+(p.z-a.z)*abz)/l2):0;
                    float qx=a.x+abx*t-p.x,qz=a.z+abz*t-p.z,d=qx*qx+qz*qz;
                    if(d<nearD[r]){nearD[r]=d;nearS[r]=Mathf.Lerp(route.along[k],route.along[k+1],t);}
                }
            }
            for(int r=0;r<nearD.Length;r++)if(nearD[r]<500f*500f)nearby.Add(r);
        }
        void Spawn(Vector3 p)
        {
            if(buses.Count>=MaxBuses||nearby.Count==0)return;
            for(int tries=0;tries<4;tries++)
            {
                int r=nearby[Random.Range(0,nearby.Count)];bool busy=false;
                foreach(var b in buses)if(b.route==r){busy=true;break;}
                float s=Mathf.Max(0,nearS[r]-Random.Range(200f,400f));int h=0;
                if(busy||Flat(PathAt(routes[r].shape,routes[r].along,s,ref h)-p)>600f*600f)continue; // a winding route: the start would despawn at once
                SpawnBus(r,s);return;
            }
        }
        static string ModelFor(Color c){return c.r>c.g&&c.r>c.b?"BusRed":c.g>c.b?"Bus":"BusBlue";}
        Bus SpawnBus(int r,float s)
        {
            var B=ChangwonSession.Builder;if(B==null)return null;
            var route=routes[r];int hint=0;var p=PathAt(route.shape,route.along,s,ref hint);
            string model=ModelFor(route.color),hint2=route.number+"번 버스 · 정류장에서 F로 탑승";
            var go=B.ChangwonVehicle(model,p,hint2);if(go==null){model="Bus";go=B.ChangwonVehicle(model,p,hint2);}if(go==null)return null;
            go.name=route.number+"번 버스";go.transform.SetParent(holder,true);
            var b=new Bus{route=r,go=go,s=s,hint=hint,cruise=Random.Range(9f,12f),baseRot=go.transform.rotation};
            var thing=go.GetComponent<ChangwonThing>();if(thing!=null){thing.kind="bus";thing.title=route.title;thing.payload=b;}
            RouteSigns(go,route.number);
            var sa=stopAlong[r];b.lat=new float[sa.Length];for(int i=0;i<sa.Length;i++)b.lat[i]=float.NaN;
            while(b.next<sa.Length&&sa[b.next]<s+1f)b.next++;
            int h1=hint,h2=hint;var f=PathAt(route.shape,route.along,s+6f,ref h1)-PathAt(route.shape,route.along,s-6f,ref h2);f.y=0;
            var car=go.AddComponent<ChangwonCar>();car.model=model;car.bus=true;car.npc=true;car.length=11f;car.width=2.5f;car.wheelBase=5.8f;car.maxSpeed=14f;
            car.Init(p,f.sqrMagnitude>1e-4f?f:Vector3.forward);car.SetEngine(true);b.car=car;
            Place(b,0);buses.Add(b);return b;
        }
        // Route number on the front, back and both sides, in a child frame without the FBX root's scale.
        void RouteSigns(GameObject bus,string number)
        {
            var B=ChangwonSession.Builder;if(B==null)return;
            var frame=new GameObject("노선 번호").transform;frame.SetParent(bus.transform,false);
            var ls=bus.transform.lossyScale;frame.localScale=new Vector3(1f/ls.x,1f/ls.y,1f/ls.z);
            frame.SetPositionAndRotation(bus.transform.position,Quaternion.identity); // the model faces +z until ChangwonCar.Init turns it
            var amber=new Color(1f,.62f,.12f);var o=frame.position;
            B.ChangwonSign(number,frame,o+new Vector3(0,2.62f,5.6f),Vector3.forward,.32f,amber);
            B.ChangwonSign(number,frame,o+new Vector3(0,2.62f,-5.6f),Vector3.back,.32f,amber);
            B.ChangwonSign(number,frame,o+new Vector3(1.3f,2.35f,1.8f),Vector3.right,.36f,amber);
            B.ChangwonSign(number,frame,o+new Vector3(-1.3f,2.35f,1.8f),Vector3.left,.36f,amber);
        }
        void Step(Bus b,float dt)
        {
            var sa=stopAlong[b.route];var route=routes[b.route];
            if(b.dwell>0)
            {
                b.dwell-=dt;b.speed=0;
                if(b.dwell<=0)
                {
                    float at=b.next<sa.Length?sa[b.next]:b.s;b.next++;b.announced=false;
                    while(b.next<sa.Length&&sa[b.next]<at+25f)b.next++; // the same kerb listed twice
                }
            }
            else
            {
                float stopAt=b.next<sa.Length?sa[b.next]-2f:route.length,remain=stopAt-b.s;
                if(remain<=.05f)Arrive(b);
                else
                {
                    float target=Mathf.Min(b.cruise,Mathf.Sqrt(2f*1.6f*remain)+.4f);
                    if(Blocked(b,dt))target=0;
                    b.speed=Mathf.MoveTowards(b.speed,target,(target>b.speed?1.4f:4f)*dt);
                    b.s=Mathf.Min(b.s+b.speed*dt,stopAt);
                    if(b.s>=stopAt-.05f)Arrive(b);
                }
                if(b==riding&&!b.announced&&b.next<sa.Length&&sa[b.next]-b.s<120f)
                {
                    b.announced=true;
                    Announce("이번 정류장은 "+route.stopNames[b.next]+"입니다"+(b.next+1<sa.Length?" · 다음 정류장은 "+route.stopNames[b.next+1]+"입니다":" · 이번 정류장은 종점입니다"));
                }
            }
            Place(b,dt);
        }
        void Arrive(Bus b)
        {
            if(b.next>=stopAlong[b.route].Length){b.done=true;b.speed=0;return;}
            b.dwell=b==riding&&!bell?5f:DwellTime;b.speed=0; // doors open; a ridden bus without the bell only stops briefly
            if(Flat(b.pos-ChangwonSession.PlayerFeet)<60f*60f)Sfx.PlayAt("air",b.pos,.5f);
        }
        bool Blocked(Bus b,float dt)
        {
            var f=b.fwd;
            if(ChangwonSession.Ride==null&&ChangwonSession.OnFoot&&Ahead(ChangwonSession.PlayerFeet-b.pos,f,1.8f))return true; // never drive into the player
            bool car=false;
            foreach(var c in ChangwonCar.All){if(c==null||c==b.car||!Ahead(c.transform.position-b.pos,f,2.2f))continue;if(c.PlayerDriving)return true;car=true;}
            b.wait=car?b.wait+dt:0;
            return car&&b.wait<6f; // a parked or stuck car: squeeze past after a while
        }
        static bool Ahead(Vector3 d,Vector3 f,float half){float a=d.x*f.x+d.z*f.z;return a>4f&&a<15f&&Mathf.Abs(d.x*f.z-d.z*f.x)<half&&Mathf.Abs(d.y)<4f;}
        void Place(Bus b,float dt)
        {
            var route=routes[b.route];
            var p=PathAt(route.shape,route.along,b.s,ref b.hint);
            int h1=b.hint,h2=b.hint;var ahead=PathAt(route.shape,route.along,b.s+6f,ref h1);var behind=PathAt(route.shape,route.along,b.s-6f,ref h2);
            var f=ahead-behind;f.y=0;f=f.sqrMagnitude>1e-4f?f.normalized:b.fwd;
            var pos=p+new Vector3(f.z,0,-f.x)*Lateral(b);
            // Shape heights are surveyed road heights; small bumps between far-apart shape points come from the terrain.
            float y=p.y,ground=ChangwonData.Height(pos.x,pos.z);if(ground>y&&ground-y<3f)y=ground;
            if(Flat(pos-ChangwonSession.PlayerFeet)<400f*400f)y=RayGround(pos,y);
            pos.y=b.placed?Mathf.MoveTowards(b.pos.y,y+.06f,6f*dt):y+.06f;
            var rot=Quaternion.LookRotation(new Vector3(f.x,(ahead.y-behind.y)/12f,f.z))*b.baseRot;
            b.go.transform.SetPositionAndRotation(pos,b.placed?Quaternion.Slerp(b.go.transform.rotation,rot,1f-Mathf.Exp(-8f*dt)):rot);
            b.pos=pos;b.fwd=f;b.placed=true;b.car.forward=f;b.car.speed=b.speed;
        }
        float RayGround(Vector3 at,float guess)
        {
            int n=Physics.RaycastNonAlloc(new Vector3(at.x,guess+3f,at.z),Vector3.down,hits,8f,~0,QueryTriggerInteraction.Ignore);
            float best=float.MinValue;
            for(int i=0;i<n;i++)if(ChangwonCar.IsGround(hits[i].collider)&&hits[i].point.y>best)best=hits[i].point.y;
            return best>float.MinValue?best:guess;
        }
        // Right of the shape: the lane while cruising, easing to the kerb over 45 m around each stop.
        float Lateral(Bus b)
        {
            var sa=stopAlong[b.route];float lat=Cruise,best=45f;
            if(b.next<sa.Length&&sa[b.next]-b.s<best){best=Mathf.Max(0,sa[b.next]-b.s);lat=Mathf.Lerp(StopLat(b,b.next),Cruise,best/45f);}
            if(b.next>0&&b.next-1<sa.Length&&b.s-sa[b.next-1]<best){float d=Mathf.Max(0,b.s-sa[b.next-1]);lat=Mathf.Lerp(StopLat(b,b.next-1),Cruise,d/45f);}
            return lat;
        }
        float StopLat(Bus b,int i)
        {
            if(!float.IsNaN(b.lat[i]))return b.lat[i];
            var st=stops[stopOf[b.route][i]];Kerb(st);
            var route=routes[b.route];int h=Mathf.Max(0,route.stopIndex[i]-1),h1=h,h2=h;float s=stopAlong[b.route][i];
            var p=PathAt(route.shape,route.along,s,ref h);var f=PathAt(route.shape,route.along,s+6f,ref h1)-PathAt(route.shape,route.along,s-6f,ref h2);f.y=0;
            var right=f.sqrMagnitude>1e-4f?new Vector3(f.z,0,-f.x).normalized:Vector3.right;var d=st.lane-p;
            return b.lat[i]=Mathf.Clamp(d.x*right.x+d.z*right.z,.5f,9f);
        }
        // Kerb lane and shelter spot beside the road nearest the stop, on the right of the direction of travel.
        void Kerb(Stop st)
        {
            if(st.kerbed)return;st.kerbed=true;
            var right=new Vector3(st.dir.z,0,-st.dir.x);
            ChangwonData.Road road;float along;Vector3 point;
            if(ChangwonData.NearestRoad(st.pos+right*5f,30f,out road,out along,out point))
            {
                Vector3 f;road.At(along,out f);var side=new Vector3(f.z,0,-f.x).normalized;if(side.sqrMagnitude<.5f)side=right;if(Vector3.Dot(side,right)<0)side=-side;
                float half=road.width*.5f;st.lane=point+side*Mathf.Max(1.7f,half-1.7f);st.kerb=point+side*(half+1.8f);st.side=side;
            }
            else{st.lane=st.pos+right*2.5f;st.kerb=st.pos+right*6f;st.side=right;}
        }
        bool CanBoard(Bus b,Vector3 p)
        {
            if(ChangwonSession.Ride!=null||!ChangwonSession.OnFoot||ChangwonSession.UiCapture||Time.time-leftAt<12f)return false;
            var d=p-(b.pos+b.fwd*3.8f+new Vector3(b.fwd.z,0,-b.fwd.x)*1.8f); // front door, kerb side
            return d.x*d.x+d.z*d.z<36f&&Mathf.Abs(d.y)<3f;
        }

        // ------------------------------------------------------------------ riding
        void Board(Bus b)
        {
            if(ChangwonSession.Ride!=null||b.done)return;
            riding=b;ChangwonSession.Ride=this;bell=false;chase=false;lookYaw=lookPitch=0;hudKey=-1;b.announced=false;
            var route=routes[b.route];var sa=stopAlong[b.route];
            ChangwonSession.Toast(route.number+"번 버스에 탔습니다 · "+route.title+(b.next<sa.Length?" · 다음 정류장 "+route.stopNames[b.next]:"")+" · F 하차 벨 · T 시점");
            Sfx.Play("tap",.7f);
        }
        void Alight(Bus b,string note)
        {
            riding=null;if(ReferenceEquals(ChangwonSession.Ride,this))ChangwonSession.Ride=null;
            leftAt=Time.time;
            var right=new Vector3(b.fwd.z,0,-b.fwd.x);var spot=b.pos+right*2.7f+b.fwd*3.5f;
            foreach(var c in new[]{spot,b.pos+right*2.7f+b.fwd*7f,b.pos+right*2.7f-b.fwd*3f,b.pos+right*4.5f})
                if(!Physics.CheckCapsule(c+Vector3.up*.5f,c+Vector3.up*1.6f,.3f,~0,QueryTriggerInteraction.Ignore)){spot=c;break;}
            Vector3 n;spot.y=ChangwonCar.Ground(spot,spot.y+2.5f,null,out n);
            ChangwonSession.TeleportPlayer?.Invoke(spot,b.fwd);
            ChangwonSession.Progress.score+=20;ChangwonSession.Toast(note+" · +20점");Sfx.Play("coin",.5f);
        }
        void Announce(string text){ChangwonSession.Toast(text);Sfx.Play("chime",.5f);}
        string StopName(Bus b){var names=routes[b.route].stopNames;return b.next<names.Length?names[b.next]:"종점";}

        public Vector3 Eye{get{return riding!=null?riding.pos+Vector3.up*2.4f:ChangwonSession.PlayerFeet+Vector3.up*1.65f;}}
        public string Hud{get{
            var b=riding;if(b==null)return "";
            int key=b.next*4+(bell?1:0)+(b.dwell>0?2:0);
            if(key!=hudKey){hudKey=key;hud=routes[b.route].number+"번 · "+(b.dwell>0?"정차 중 "+StopName(b)+" · F: 지금 하차":"다음 정류장 "+StopName(b)+(bell?" · 하차 벨 ✓":" · F: 다음 정류장에서 하차"));}
            return hud;}}
        public void UpdateRide(Camera camera,float dt)
        {
            var b=riding;if(b==null||b.go==null){riding=null;if(ReferenceEquals(ChangwonSession.Ride,this))ChangwonSession.Ride=null;return;}
            if(Input.GetKeyDown(KeyCode.T)){chase=!chase;chasePos=camera.transform.position;}
            if(Input.GetKeyDown(KeyCode.F))
            {
                if(b.dwell>0){Alight(b,StopName(b)+"에서 내렸습니다");return;}
                if(!bell){bell=true;Sfx.Play("bell",.6f);ChangwonSession.Toast("하차 벨을 눌렀습니다 · 다음 정류장에서 내립니다");}
            }
            Step(b,dt);
            if(b.done){Alight(b,"종점입니다. 모두 내리세요");return;}
            if(bell&&b.dwell>0&&b.dwell<DwellTime-1.5f){Alight(b,StopName(b)+"에서 내렸습니다");return;}
            lookYaw=Mathf.Clamp(lookYaw+Input.GetAxis("Mouse X")*2.2f,-160f,160f);lookPitch=Mathf.Clamp(lookPitch-Input.GetAxis("Mouse Y")*1.6f,-35f,35f);
            var cam=camera.transform;
            if(!chase){cam.position=b.pos+Vector3.up*2.4f+b.fwd*2f;cam.rotation=Quaternion.LookRotation(b.fwd)*Quaternion.Euler(lookPitch,lookYaw,0);}
            else
            {
                var orbit=Quaternion.LookRotation(b.fwd)*Quaternion.Euler(12f+lookPitch*.5f,lookYaw,0);var want=b.pos+Vector3.up*2f-orbit*Vector3.forward*15f;
                want.y=Mathf.Max(want.y,ChangwonData.Height(want.x,want.z)+1.5f);chasePos=Vector3.Lerp(chasePos,want,1f-Mathf.Exp(-6f*dt));
                cam.position=chasePos;cam.rotation=Quaternion.LookRotation(b.pos+Vector3.up*1.6f-chasePos);
            }
        }

        // ------------------------------------------------------------------ stops
        void Shelters(Vector3 p)
        {
            float reach=PerformanceRuntime.Level==0?180f:250f,drop=(reach+70f)*(reach+70f);
            for(int i=shelters.Count-1;i>=0;i--){var st=shelters[i];if(st.shelter==null||Flat(st.kerb-p)>drop){if(st.shelter!=null)Destroy(st.shelter);st.shelter=null;shelters.RemoveAt(i);}}
            int cx=Mathf.FloorToInt(p.x/Cell),cz=Mathf.FloorToInt(p.z/Cell),made=0;
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
            {
                List<int> list;if(!stopGrid.TryGetValue(Key(cx+dx,cz+dz),out list))continue;
                foreach(int c in list){var st=stops[c];if(made>=4||st.shelter!=null||Flat(st.pos-p)>reach*reach)continue;MakeShelter(st);shelters.Add(st);made++;}
            }
        }
        void MakeShelter(Stop st)
        {
            Kerb(st);Vector3 n;float y=ChangwonCar.Ground(st.kerb,st.kerb.y+3f,null,out n);
            var go=new GameObject("버스 정류장 "+st.name);go.transform.SetParent(holder,false);
            go.transform.SetPositionAndRotation(new Vector3(st.kerb.x,y,st.kerb.z),Quaternion.LookRotation(-st.side)); // open side toward the road
            go.AddComponent<MeshFilter>().sharedMesh=ShelterMesh();go.AddComponent<MeshRenderer>().sharedMaterial=PropMaterial();
            var box=go.AddComponent<BoxCollider>();box.center=new Vector3(0,1.3f,-.75f);box.size=new Vector3(4.2f,2.6f,.3f); // back panel only: people can stand inside
            var thing=go.AddComponent<ChangwonThing>();thing.kind="busstop";thing.title=st.name;thing.hint=st.name+" 정류장 · 버스 도착 안내 (F)";thing.detail="정차 노선: "+RouteNumbers(st,40);thing.payload=st;
            var B=ChangwonSession.Builder;
            if(B!=null)
            {
                var t=go.transform;
                B.ChangwonSign(st.name,t,t.TransformPoint(new Vector3(0,2.78f,.96f)),t.forward,.34f,Color.white);
                B.ChangwonSign(st.name,t,t.TransformPoint(new Vector3(0,2.78f,-.96f)),-t.forward,.34f,Color.white);
                B.ChangwonSign(RouteNumbers(st,8),t,t.TransformPoint(new Vector3(0,1.9f,-.7f)),t.forward,.18f,new Color(.08f,.12f,.2f));
            }
            st.shelter=go;
        }
        string RouteNumbers(Stop st,int max)
        {
            var parts=new List<string>();for(int i=0;i<st.routes.Count&&i<max;i++)parts.Add(routes[st.routes[i]].number);
            return string.Join(" · ",parts)+(st.routes.Count>max?" 외 "+(st.routes.Count-max)+"개":"");
        }
        Material PropMaterial()
        {
            if(mat==null){var sh=Shader.Find("Peninsula/VertexTerrain");mat=sh!=null?new Material(sh):new Material(Shader.Find("Standard"));mat.name="정류장 (정점 색)";}
            return mat;
        }
        // Glass shelter facing +z: roof, back and side panels, bench and a blue bus-stop sign post.
        Mesh ShelterMesh()
        {
            if(shelterMesh!=null)return shelterMesh;
            var mb=new MeshBuild(1);var q=Quaternion.identity;
            Color32 frame=new Color32(64,70,78,255),glass=new Color32(160,196,214,255),roof=new Color32(44,104,70,255),wood=new Color32(150,108,70,255),blue=new Color32(30,92,170,255);
            mb.Box(0,new Vector3(0,2.5f,0),new Vector3(4.4f,.12f,1.9f),q,roof,true);
            mb.Box(0,new Vector3(0,1.35f,-.75f),new Vector3(4f,2.1f,.06f),q,glass);
            foreach(float x in new[]{-2.05f,2.05f})
            {
                mb.Box(0,new Vector3(x,1.25f,-.75f),new Vector3(.1f,2.5f,.1f),q,frame);mb.Box(0,new Vector3(x,1.25f,.7f),new Vector3(.1f,2.5f,.1f),q,frame);
                mb.Box(0,new Vector3(x,1.35f,-.05f),new Vector3(.05f,1.9f,1.3f),q,glass);
                mb.Box(0,new Vector3(x*.7f,.24f,-.45f),new Vector3(.08f,.48f,.38f),q,frame);
            }
            mb.Box(0,new Vector3(0,.5f,-.45f),new Vector3(3f,.07f,.42f),q,wood);
            mb.Box(0,new Vector3(2.7f,1.5f,.6f),new Vector3(.08f,3f,.08f),q,frame);mb.Box(0,new Vector3(2.7f,2.75f,.6f),new Vector3(.7f,.7f,.06f),q,blue);
            return shelterMesh=mb.ToMesh("버스 정류장");
        }

        // ------------------------------------------------------------------ arrival board
        public bool Handles(ChangwonThing thing){return thing!=null&&(thing.kind=="busstop"||thing.kind=="bus");}
        public void Use(ChangwonThing thing)
        {
            if(!Handles(thing)){PassOn(this,thing);return;}
            if(thing.kind=="bus")
            {
                var b=thing.payload as Bus;if(b==null)return;
                if(b.dwell>0||b.speed<.5f)Board(b);else ChangwonSession.Toast("버스가 정류장에 서면 탈 수 있어요 · 정류장에서 기다리세요");
                return;
            }
            board=thing.payload as Stop;if(board==null)return;
            rows.Clear();for(int i=0;i<board.routes.Count;i++)rows.Add(i);
            var st=board;rows.Sort((x,y)=>Num(routes[st.routes[x]].number).CompareTo(Num(routes[st.routes[y]].number)));
            scroll=Vector2.zero;ChangwonSession.UiCapture=true;Sfx.Play("tap",.4f);
        }
        static int Num(string s){int v=0;foreach(char ch in s){if(ch<'0'||ch>'9')break;v=v*10+(ch-'0');}return v;}
        static string Short(string title){int a=title.IndexOf('('),b=title.LastIndexOf(')');return a>=0&&b>a?title.Substring(a+1,b-a-1):title;}
        void CloseBoard(){board=null;ChangwonSession.UiCapture=false;}
        // Seconds until a simulated bus of route r reaches its i-th stop, or a headway estimate when none is on the way.
        string Eta(int r,int i)
        {
            float at=stopAlong[r][i],best=-1;
            foreach(var b in buses)
            {
                if(b.route!=r||b.s>at+1f)continue;
                if(b.dwell>0&&b.next==i)return "정차 중 · 지금 타세요";
                float t=(at-b.s)/10.5f+Mathf.Max(0,b.dwell)+DwellTime*Mathf.Max(0,i-b.next-(b.dwell>0?1:0));if(best<0||t<best)best=t;
            }
            if(best>=0)return best<20f?"곧 도착":"약 "+Mathf.CeilToInt(best)+"초 후 도착 (운행 중)";
            return "약 "+(10+Mathf.FloorToInt(Mathf.Repeat(r*37+i*11-Time.time,50f)))+"초 후 도착 예상";
        }
        void Wait(int r,int i)
        {
            CloseBoard();var route=routes[r];float at=stopAlong[r][i];
            foreach(var b in buses)if(b.route==r&&b.s<=at&&at-b.s<800f){ChangwonSession.Toast(route.number+"번 버스가 오고 있습니다 · "+Eta(r,i));return;}
            if(buses.Count>=MaxBuses+2)
            {
                Bus far=null;foreach(var b in buses)if(b!=riding&&(far==null||Flat(b.pos-ChangwonSession.PlayerFeet)>Flat(far.pos-ChangwonSession.PlayerFeet)))far=b;
                if(far!=null){Destroy(far.go);buses.Remove(far);}
            }
            var nb=SpawnBus(r,Mathf.Max(0,at-250f));
            ChangwonSession.Toast(nb!=null?route.number+"번 버스가 곧 도착합니다 · 앞문 가까이에서 기다리세요":"버스를 부르지 못했습니다");
        }
        void DrawBoard(float w,float h)
        {
            var st=board;if(st==null)return;
            float bw=Mathf.Min(640f,w-40f),bh=Mathf.Min(560f,h-40f);var box=new Rect((w-bw)*.5f,(h-bh)*.5f,bw,bh);
            GUI.Box(box,"",ChangwonSession.Box);
            GUI.Label(new Rect(box.x+24,box.y+16,bw-48,40),st.name+" 정류장",ChangwonSession.Title);
            GUI.Label(new Rect(box.x+24,box.y+58,bw-48,24),"정차 노선 "+st.routes.Count+"개 · 창원 BIS · 버스가 서면 앞문 가까이에서 탑승",ChangwonSession.Small);
            var view=new Rect(box.x+16,box.y+90,bw-32,bh-170);
            scroll=GUI.BeginScrollView(view,scroll,new Rect(0,0,view.width-20,rows.Count*58f));
            for(int k=0;k<rows.Count;k++)
            {
                int j=rows[k],r=st.routes[j],i=st.index[j];var route=routes[r];float y=k*58f;
                GUI.Label(new Rect(4,y,view.width-230,28),route.number+"번 · "+Short(route.title),ChangwonSession.Body);
                GUI.Label(new Rect(4,y+28,view.width-230,24),Eta(r,i),ChangwonSession.Small);
                if(GUI.Button(new Rect(view.width-214,y+6,190,44),"이 버스 기다리기",ChangwonSession.Button)){Wait(r,i);break;}
            }
            GUI.EndScrollView();
            if(board!=null&&GUI.Button(new Rect(box.xMax-196,box.yMax-66,176,50),"닫기",ChangwonSession.Accent))CloseBoard();
        }
    }
}
