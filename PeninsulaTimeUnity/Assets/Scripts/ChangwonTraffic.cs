using System;
using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // NPC road traffic around the player: cars follow the surveyed road polylines (bridges and tunnels included) in the
    // right-hand lane, keep their distance, slow through junctions, give way at busy crossroads and stop for the player.
    // Any of them can be borrowed with F (GameController calls Release; the driver gets out and walks off).
    public class ChangwonTraffic : MonoBehaviour
    {
        class Npc
        {
            public ChangwonCar car;public Quaternion rest;public ChangwonData.Road road,next;public int dir,nextDir,seg,key=-1;
            public float d,joinLength,hold,stuck,ghost,dodge;public Vector3 join,offset;public bool slowAtEnd,engine;
        }
        static readonly string[] Models={"Car","CarRed","CarWhite","CarBlack","CarBlue"};
        static readonly float[] Cruise={24,19,15,13,11,8,5};            // m/s by road class
        static readonly float[] Chance={1,1,1,1,.8f,.45f,.2f};          // spawn/turn preference: most traffic is on the arterials
        static readonly int[] Counts={18,32,48};
        readonly List<Npc> cars=new List<Npc>();
        readonly Dictionary<ChangwonCar,Npc> byCar=new Dictionary<ChangwonCar,Npc>();
        readonly Dictionary<int,List<Npc>> lanes=new Dictionary<int,List<Npc>>(); // road*2+direction -> cars on it
        readonly List<Vector3> blockers=new List<Vector3>();int hardBlockers;
        readonly List<ChangwonCar> released=new List<ChangwonCar>(); // borrowed cars, removed once left far behind
        readonly System.Random rnd=new System.Random();
        float nextSound;

        // Is any car (NPC, player's, parked, bus) within 'distance' ahead of pos, roughly in its lane? For buses and missions.
        public static bool CarAhead(Vector3 pos,Vector3 forward,float distance)
        {
            forward.y=0;if(forward.sqrMagnitude<1e-6f)return false;forward.Normalize();
            var all=ChangwonCar.All;
            for(int i=0;i<all.Count;i++){
                if(all[i]==null)continue;var v=all[i].transform.position-pos;if(Mathf.Abs(v.y)>3f)continue;
                float f=v.x*forward.x+v.z*forward.z;if(f<.5f||f>distance)continue;
                if(Mathf.Abs(v.x*forward.z-v.z*forward.x)<2f)return true;
            }
            return false;
        }

        // The player borrows this car: stop simulating it and let the driver step out.
        public void Release(ChangwonCar car)
        {
            Npc n;if(car==null||!byCar.TryGetValue(car,out n))return;
            Remove(n);car.speed=0;car.npc=false;released.Add(car);
            var thing=car.GetComponent<ChangwonThing>();if(thing!=null)thing.hint="차 타기 (F)";
            var people=GetComponent<ChangwonPeople>();if(people!=null)people.Driver(car,n.road,n.dir,n.d);
        }

        void Update()
        {
            if(!ChangwonData.Loaded||!ChangwonSession.Active||ChangwonSession.Builder==null)return;
            float dt=Mathf.Min(Time.deltaTime,.1f);var player=ChangwonSession.PlayerFeet;
            for(int i=0;i<2&&cars.Count<Counts[PerformanceRuntime.Level];i++)Spawn(player);
            // What NPCs must not drive into: the player (always), then other cars that are not ours (parked, buses, left behind).
            blockers.Clear();
            if(ChangwonSession.PlayerCar!=null)blockers.Add(ChangwonSession.PlayerCar.transform.position);
            else if(ChangwonSession.Ride==null)blockers.Add(player);
            hardBlockers=blockers.Count;
            var all=ChangwonCar.All;
            for(int i=0;i<all.Count;i++){var c=all[i];if(c!=null&&c!=ChangwonSession.PlayerCar&&!byCar.ContainsKey(c)&&Flat(c.transform.position-player)<600f*600f)blockers.Add(c.transform.position);}
            for(int i=cars.Count-1;i>=0;i--){
                var n=cars[i];
                if(n.car==null){Remove(n);continue;}
                if(Flat(n.car.transform.position-player)>550f*550f||!Step(n,dt)){Remove(n);Destroy(n.car.gameObject);}
            }
            for(int i=released.Count-1;i>=0;i--){
                var c=released[i];if(c==null){released.RemoveAt(i);continue;}
                if(c!=ChangwonSession.PlayerCar&&Flat(c.transform.position-player)>550f*550f){released.RemoveAt(i);Destroy(c.gameObject);}
            }
            if(Time.time>=nextSound){nextSound=Time.time+.5f;Sounds(player);}
        }

        bool Step(Npc n,float dt)
        {
            var car=n.car;var pos=car.transform.position;var fwd=car.forward;
            float remaining=n.road.length-n.d,target=Cruise[n.road.cls];
            if(n.slowAtEnd)target=Mathf.Min(target,6f+Mathf.Max(0,remaining-4f)*.6f);
            if(n.hold>0&&remaining<15f){target=Mathf.Min(target,Mathf.Max(0,remaining-2f)*1.2f);if(car.speed<.3f)n.hold-=dt;}
            // Car ahead in the same lane (this road and the one we turn into), then the player and other cars.
            float gap=Ahead(n);bool soft=false;n.ghost-=dt;n.dodge=0;
            float laneLat=n.road.OneWay?0:n.road.width*.25f,myLat=n.offset.x*fwd.z-n.offset.z*fwd.x; // metres right of the centreline
            for(int i=0;i<blockers.Count;i++){
                if(i>=hardBlockers&&n.ghost>0)break;
                var v=blockers[i]-pos;if(Mathf.Abs(v.y)>3f)continue;
                float f=v.x*fwd.x+v.z*fwd.z;if(f<-5f||f>40f)continue;
                float side=v.x*fwd.z-v.z*fwd.x;
                // A parked car or bus in our lane: pull out round it from 30 m before until it is 5 m behind.
                if(i>=hardBlockers&&f<30f){float s=Pass(side+myLat,laneLat,n.road.width*.5f,n.road.OneWay);if(Mathf.Abs(s)>Mathf.Abs(n.dodge))n.dodge=s;}
                if(f>0&&f<gap&&Mathf.Abs(side)<2f){gap=f;soft=i>=hardBlockers;}
            }
            target=Mathf.Min(target,Mathf.Max(0,(gap-7f)*.9f));
            // Still blocked (no room to pass): wait a while, then squeeze through it.
            if(soft&&car.speed<.3f){n.stuck+=dt;if(n.stuck>6f){n.ghost=4f;n.stuck=0;}}else if(!soft)n.stuck=0;
            car.speed=Mathf.MoveTowards(Mathf.Max(0,car.speed),target,(target<car.speed?9f:2.2f)*dt);
            n.d+=car.speed*dt;
            for(int guard=0;n.d>=n.road.length&&guard<8;guard++){
                if(n.next==null){ // one-way dead end: wait there until the player looks away, then vanish
                    n.d=n.road.length;car.speed=0;
                    var v=pos-ChangwonSession.PlayerFeet;v.y=0;float dist=v.magnitude;
                    if(dist>200f||Vector3.Dot(v/Mathf.Max(dist,.01f),ChangwonSession.PlayerForward)<.3f)return false;
                    break;
                }
                n.d-=n.road.length;Enter(n,n.next,n.nextDir,true);
            }
            Place(n,dt,false);
            return true;
        }

        float Ahead(Npc n)
        {
            float gap=float.MaxValue;List<Npc> list;
            if(lanes.TryGetValue(n.key,out list))for(int i=0;i<list.Count;i++){var o=list[i];if(o!=n&&o.d>n.d&&o.d-n.d<gap)gap=o.d-n.d;}
            if(n.next!=null&&lanes.TryGetValue(Key(n.next,n.nextDir),out list)){
                float left=n.road.length-n.d;
                for(int i=0;i<list.Count;i++){var o=list[i];if(o!=n&&left+o.d<gap)gap=left+o.d;}
            }
            return gap;
        }

        // Moves n onto road (direction dir); 'join' bridges the small gap some roads leave at their shared node.
        void Enter(Npc n,ChangwonData.Road road,int dir,bool join)
        {
            Leave(n);
            if(join){
                n.join=EndPoint(n.road,n.dir>0);float gap=Mathf.Sqrt(Flat(StartPoint(road,dir>0)-n.join));
                n.joinLength=gap>.3f?gap:0;n.d-=n.joinLength;
            }
            n.road=road;n.dir=dir;n.seg=dir>0?1:road.pts.Length-1;n.key=Key(road,dir);
            List<Npc> list;if(!lanes.TryGetValue(n.key,out list))lanes[n.key]=list=new List<Npc>();list.Add(n);
            int node=dir>0?road.b:road.a,drivable=0;
            foreach(int ri in ChangwonData.NodeRoads[node])if(ChangwonData.Roads[ri].cls<=ChangwonData.Service)drivable++;
            int nextDir;n.next=Next(road,dir,rnd,r=>r.cls<=ChangwonData.Service,out nextDir);n.nextDir=nextDir;
            if(n.next==null&&!road.OneWay){n.next=road;n.nextDir=-dir;} // dead end: turn round
            n.slowAtEnd=n.next!=null&&(drivable>=3&&road.cls>=ChangwonData.Primary||Vector3.Dot(EndDir(road,dir>0),StartDir(n.next,n.nextDir>0))<.7f);
            n.hold=drivable>=4&&road.cls>=ChangwonData.Primary&&rnd.NextDouble()<.3?.8f+(float)rnd.NextDouble()*2.2f:0;
        }
        void Leave(Npc n){List<Npc> list;if(n.key>=0&&lanes.TryGetValue(n.key,out list))list.Remove(n);n.key=-1;}
        void Remove(Npc n){Leave(n);cars.Remove(n);byCar.Remove(n.car);}
        static int Key(ChangwonData.Road road,int dir){return road.index*2+(dir>0?1:0);}

        void Place(Npc n,float dt,bool snap)
        {
            Vector3 p,fwd;
            if(n.d<0&&n.joinLength>0){var s=StartPoint(n.road,n.dir>0);p=Vector3.Lerp(n.join,s,1f+n.d/n.joinLength);fwd=(s-n.join).normalized;}
            else{p=Sample(n.road,ref n.seg,n.dir>0?n.d:n.road.length-n.d,out fwd);fwd*=n.dir;}
            var car=n.car;var flat=new Vector3(fwd.x,0,fwd.z);
            if(flat.sqrMagnitude<1e-6f){flat=car.forward;fwd=flat;}
            flat.Normalize();
            // Drive on the right: a quarter of the width off the centreline of two-way roads, on the centreline of one-way ones.
            var lane=new Vector3(flat.z,0,-flat.x)*((n.road.OneWay?0:n.road.width*.25f)+n.dodge);
            n.offset=snap?lane:Vector3.MoveTowards(n.offset,lane,Mathf.Max(3f,car.speed*.6f)*dt);
            car.forward=flat;
            var look=Quaternion.LookRotation(fwd)*n.rest;
            car.transform.SetPositionAndRotation(p+n.offset+Vector3.up*(Lift(n.road.cls)+car.rideHeight),snap?look:Quaternion.Slerp(car.transform.rotation,look,1f-Mathf.Exp(-8f*dt)));
        }

        void Spawn(Vector3 player)
        {
            var c=ChangwonData.ChunkOf(player.x,player.z);var look=ChangwonSession.PlayerForward;
            for(int tries=0;tries<12;tries++){
                int cx=c.x+rnd.Next(-1,2),cz=c.y+rnd.Next(-1,2);if(cx<0||cz<0||cx>=ChangwonData.CX||cz>=ChangwonData.CZ)continue;
                var ids=ChangwonData.RoadsInChunk[cz*ChangwonData.CX+cx];if(ids.Length==0)continue;
                var road=ChangwonData.Roads[ids[rnd.Next(ids.Length)]];
                if(road.cls>ChangwonData.Service||road.length<15f||rnd.NextDouble()>Chance[road.cls])continue;
                int seg=1;Vector3 tangent;float along=(float)rnd.NextDouble()*road.length;var p=Sample(road,ref seg,along,out tangent);
                var v=p-player;v.y=0;float dist=v.magnitude;if(dist<90f||dist>450f)continue;
                // Out of sight: behind or beside the player, or far off (a failed try just waits for the next frame).
                if(dist<380f&&Vector3.Dot(v/dist,look)>.3f)continue;
                int dir=road.OneWay||rnd.Next(2)==0?1:-1;float d=dir>0?along:road.length-along;
                if(Occupied(road,dir,d))continue;
                Create(road,dir,d,p,tangent*dir);return;
            }
        }
        bool Occupied(ChangwonData.Road road,int dir,float d)
        {
            List<Npc> list;if(!lanes.TryGetValue(Key(road,dir),out list))return false;
            foreach(var o in list)if(Mathf.Abs(o.d-d)<15f)return true;
            return false;
        }
        void Create(ChangwonData.Road road,int dir,float d,Vector3 at,Vector3 facing)
        {
            string model=Models[rnd.Next(Models.Length)];
            var go=ChangwonSession.Builder.ChangwonVehicle(model,at,"차 빌리기 (F)");if(go==null)return;
            var n=new Npc{rest=go.transform.rotation,d=d};
            var car=go.AddComponent<ChangwonCar>();car.model=model;car.npc=true;n.car=car;
            car.Init(at,facing);car.speed=Cruise[road.cls]*.6f;
            Enter(n,road,dir,false);Place(n,0,true);
            cars.Add(n);byCar[car]=n;
        }

        // Engine sound only for the two nearest cars.
        void Sounds(Vector3 player)
        {
            Npc a=null,b=null;float da=60f*60f,db=60f*60f;
            foreach(var n in cars){float d=(n.car.transform.position-player).sqrMagnitude;if(d<da){b=a;db=da;a=n;da=d;}else if(d<db){b=n;db=d;}}
            foreach(var n in cars){bool on=n==a||n==b;if(on!=n.engine){n.engine=on;n.car.SetEngine(on);}}
        }

        // Sideways shift from the lane line (laneLat) that clears a stopped car at lateral 'at' (both metres right of the centreline
        // of a carriageway half metres wide): round its left, or its right on a one-way street. 0 when it is clear of the lane
        // or there is no room.
        internal static float Pass(float at,float laneLat,float half,bool oneWay)
        {
            if(Mathf.Abs(at-laneLat)>=2.3f)return 0;
            if(at-2.3f>=1f-half)return at-2.3f-laneLat;
            if(oneWay&&at+2.3f<=half-1f)return at+2.3f-laneLat;
            return 0;
        }

        // ---------------------------------------------------------------- road helpers (shared with ChangwonPeople)
        internal static float Flat(Vector3 v){return v.x*v.x+v.z*v.z;}
        // Height of the drawn road surface above the surveyed centreline (matches ChangwonWorld's ribbon lift).
        internal static float Lift(byte cls){return .06f+(cls<=8?8-cls:0)*.012f;}
        // Point at 'along' metres from end a; seg caches the polyline segment between calls. tangent is the unit a->b direction.
        internal static Vector3 Sample(ChangwonData.Road r,ref int seg,float along,out Vector3 tangent)
        {
            var A=r.along;var P=r.pts;int last=P.Length-1;
            seg=Mathf.Clamp(seg,1,last);
            while(seg<last&&A[seg]<along)seg++;
            while(seg>1&&A[seg-1]>along)seg--;
            tangent=P[seg]-P[seg-1];float m=tangent.magnitude;tangent=m>1e-4f?tangent/m:Vector3.forward;
            return Vector3.Lerp(P[seg-1],P[seg],Mathf.InverseLerp(A[seg-1],A[seg],along));
        }
        internal static Vector3 StartPoint(ChangwonData.Road r,bool aToB){return aToB?r.pts[0]:r.pts[r.pts.Length-1];}
        internal static Vector3 EndPoint(ChangwonData.Road r,bool aToB){return aToB?r.pts[r.pts.Length-1]:r.pts[0];}
        internal static Vector3 StartDir(ChangwonData.Road r,bool aToB){var p=r.pts;int n=p.Length;return aToB?Dir(p[0],p[1]):Dir(p[n-1],p[n-2]);}
        internal static Vector3 EndDir(ChangwonData.Road r,bool aToB){var p=r.pts;int n=p.Length;return aToB?Dir(p[n-2],p[n-1]):Dir(p[1],p[0]);}
        static Vector3 Dir(Vector3 from,Vector3 to){var v=to-from;v.y=0;return v.sqrMagnitude>1e-6f?v.normalized:Vector3.forward;}

        // Another road leaving the node at the end of 'road' (travelled in direction dir), respecting one-way streets,
        // preferring straight on and bigger roads. Skips roads whose polyline starts far from where this one ends. Null at a dead end.
        internal static ChangwonData.Road Next(ChangwonData.Road road,int dir,System.Random rnd,Func<ChangwonData.Road,bool> ok,out int nextDir)
        {
            int node=dir>0?road.b:road.a;var end=EndDir(road,dir>0);var endPoint=EndPoint(road,dir>0);
            ChangwonData.Road best=null;nextDir=0;float total=0;
            foreach(int ri in ChangwonData.NodeRoads[node]){
                var r=ChangwonData.Roads[ri];if(r==road||!ok(r))continue;
                int d=r.a==node?1:-1;if(d<0&&r.OneWay)continue;
                if(Flat(StartPoint(r,d>0)-endPoint)>25f*25f)continue;
                float w=(.15f+Mathf.Max(0,Vector3.Dot(end,StartDir(r,d>0))))*Chance[Mathf.Min((int)r.cls,6)];
                total+=w;if(rnd.NextDouble()*total<w){best=r;nextDir=d;}
            }
            return best;
        }
    }
}
