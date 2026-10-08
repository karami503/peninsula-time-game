using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Pedestrians around the player: they walk the pavements of town streets (turning at junctions), stroll in parks,
    // crowd shopping streets and restaurant/cafe/market areas, thin out at night, stop to talk (F) and wait for cars.
    public class ChangwonPeople : MonoBehaviour
    {
        class Walker
        {
            public Pedestrian ped;public Transform t;public Transform[] legs,arms;public Quaternion[] legRest,armRest;public Quaternion facing;
            public ChangwonData.Road road;public int dir,seg,side;public float d,speed,phase,wait,talkUntil,rest,jitter;public Vector3 offset,home,target;
        }
        static readonly int[] Counts={16,30,45};
        readonly List<Walker> people=new List<Walker>();
        readonly List<Vector2> busy=new List<Vector2>(),parks=new List<Vector2>();
        readonly List<Vector3> cars=new List<Vector3>();
        readonly System.Random rnd=new System.Random();
        float nextScan;

        // The driver of a borrowed car steps out on the kerb side and walks to the pavement.
        public void Driver(ChangwonCar car)
        {
            if(car==null||ChangwonSession.Builder==null)return;
            var right=new Vector3(car.forward.z,0,-car.forward.x);var at=car.transform.position+right*(car.width*.5f+.7f);
            ChangwonData.Road road;float along;Vector3 point;
            if(!ChangwonData.NearestRoad(at,30f,out road,out along,out point)){Create(at,null,1,0,1);return;}
            int seg=1;Vector3 tangent;ChangwonTraffic.Sample(road,ref seg,along,out tangent);
            int dir=Vector3.Dot(tangent,car.forward)>=0?1:-1;
            var w=Create(at,road,dir,dir>0?along:road.length-along,seg);if(w==null)return;
            w.side=1;w.offset=at-point;w.offset.y=0;w.t.position=WalkStreet(w,0);
        }

        void Update()
        {
            if(!ChangwonData.Loaded||!ChangwonSession.Active||ChangwonSession.Builder==null)return;
            float dt=Mathf.Min(Time.deltaTime,.1f);var player=ChangwonSession.PlayerFeet;
            if(Time.time>=nextScan){nextScan=Time.time+5f;ScanPlaces(player);}
            int target=Counts[PerformanceRuntime.Level];if(DayCycle.Night)target=target*2/5;
            for(int i=0;i<2&&people.Count<target;i++)Spawn(player);
            cars.Clear();
            foreach(var c in ChangwonCar.All)if(c!=null&&ChangwonTraffic.Flat(c.transform.position-player)<260f*260f)cars.Add(c.transform.position);
            for(int i=people.Count-1;i>=0;i--){
                var w=people[i];if(w.t==null){people.RemoveAt(i);continue;}
                float dist=ChangwonTraffic.Flat(w.t.position-player);
                if(dist>260f*260f||people.Count>target&&dist>150f*150f){people.RemoveAt(i);Destroy(w.t.gameObject);continue;}
                Step(w,dt,player);
            }
        }

        void Step(Walker w,float dt,Vector3 player)
        {
            // Pedestrian.Talk turns them to face the player (its own pause never runs down here, as we move them ourselves).
            if(w.t.rotation!=w.facing)w.talkUntil=Time.time+4f;
            if(Time.time<w.talkUntil){Animate(w,0);w.facing=w.t.rotation;return;}
            var pos=w.t.position;
            // Wait for a car in the way (crossing at a junction) or the player standing in the path; give up after 4 s.
            bool blocked=Blocked(pos,w.t.forward,player);
            if(blocked&&w.wait<4f){w.wait+=dt;Animate(w,0);return;}
            if(!blocked)w.wait=0;
            var next=w.road!=null?WalkStreet(w,dt):WalkPark(w,dt);
            var delta=next-pos;delta.y=0;
            if(delta.sqrMagnitude>1e-8f)w.t.rotation=Quaternion.Slerp(w.t.rotation,Quaternion.LookRotation(delta),1f-Mathf.Exp(-10f*dt));
            w.t.position=next;w.facing=w.t.rotation;
            Animate(w,delta.magnitude);
        }

        bool Blocked(Vector3 pos,Vector3 heading,Vector3 player)
        {
            for(int i=0;i<cars.Count;i++){
                var v=cars[i]-pos;if(v.sqrMagnitude>100f)continue;
                float f=v.x*heading.x+v.z*heading.z;if(f>0&&Mathf.Abs(v.x*heading.z-v.z*heading.x)<2f)return true;
            }
            if(ChangwonSession.OnFoot&&ChangwonSession.Ride==null){
                var v=player-pos;float f=v.x*heading.x+v.z*heading.z;
                if(f>0&&f<1.5f&&Mathf.Abs(v.y)<2f&&Mathf.Abs(v.x*heading.z-v.z*heading.x)<.7f)return true;
            }
            return false;
        }

        // Along the pavement of the current street; at its end, on to another town street (or back at a dead end).
        Vector3 WalkStreet(Walker w,float dt)
        {
            Vector3 p;var want=Kerbside(w,out p);
            float apart=(w.offset-want).sqrMagnitude;bool crossing=apart>.09f,onKerb=apart<4f; // off the kerb only while crossing a road
            if(crossing)w.offset=Vector3.MoveTowards(w.offset,want,w.speed*dt); // over to the new pavement at a corner or junction
            else{
                w.offset=want;w.d+=w.speed*dt;
                if(w.d>=w.road.length){
                    var end=ChangwonTraffic.EndPoint(w.road,w.dir>0);int dir;
                    var next=ChangwonTraffic.Next(w.road,w.dir,rnd,r=>Walkable(r),out dir);
                    if(next==null){w.dir=-w.dir;w.side=-w.side;}
                    else{w.offset+=end-ChangwonTraffic.StartPoint(next,dir>0);w.offset.y=0;w.road=next;w.dir=dir;}
                    w.d=0;w.seg=w.dir>0?1:w.road.pts.Length-1;Kerbside(w,out p);
                }
            }
            var at=p+w.offset;var road=w.road;
            at.y=p.y+ChangwonTraffic.Lift(road.cls);
            if(road.kind[w.seg]==ChangwonData.Ground&&road.kind[w.seg-1]==ChangwonData.Ground)
                at.y=Mathf.Max(at.y+(onKerb&&Sidewalk(road)?.16f:0),ChangwonData.Height(at.x,at.z));
            return at;
        }
        Vector3 Kerbside(Walker w,out Vector3 p)
        {
            Vector3 tangent;p=ChangwonTraffic.Sample(w.road,ref w.seg,w.dir>0?w.d:w.road.length-w.d,out tangent);
            var f=new Vector3(tangent.x,0,tangent.z)*w.dir;if(f.sqrMagnitude<1e-6f)f=w.t.forward;f.Normalize();
            return new Vector3(f.z,0,-f.x)*((w.road.width*.5f+1.3f+w.jitter)*w.side);
        }

        // Strolling around a park: walk to a spot, stand a moment, pick another.
        Vector3 WalkPark(Walker w,float dt)
        {
            var pos=w.t.position;
            if(w.rest>0){w.rest-=dt;if(w.rest<=0)w.target=ParkPoint(w.home,25f);return pos;}
            var v=w.target-pos;v.y=0;float step=w.speed*dt;Vector3 next;
            if(v.sqrMagnitude<=step*step){next=w.target;w.rest=1f+(float)rnd.NextDouble()*3f;}else next=pos+v.normalized*step;
            next.y=ChangwonData.Height(next.x,next.z);
            return next;
        }
        Vector3 ParkPoint(Vector3 around,float radius)
        {
            for(int i=0;i<4;i++){
                float a=(float)rnd.NextDouble()*Mathf.PI*2,r=Mathf.Sqrt((float)rnd.NextDouble())*radius;
                var p=around+new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r);byte lc=ChangwonData.LandAt(p.x,p.z);
                if(lc!=ChangwonData.Sea&&lc!=ChangwonData.Water&&lc!=ChangwonData.Wetland){p.y=ChangwonData.Height(p.x,p.z);return p;}
            }
            return around;
        }

        static void Animate(Walker w,float moved)
        {
            float angle=0;if(moved>1e-4f){w.phase+=moved*3.5f;angle=Mathf.Sin(w.phase)*28f;}
            for(int i=0;i<w.legs.Length;i++)w.legs[i].localRotation=Quaternion.AngleAxis(i==0?angle:-angle,Vector3.right)*w.legRest[i];
            for(int i=0;i<w.arms.Length;i++)w.arms[i].localRotation=Quaternion.AngleAxis(i==0?-angle*.8f:angle*.8f,Vector3.right)*w.armRest[i];
        }

        // Restaurants, cafes, markets and malls draw crowds; parks get strollers. Refreshed every few seconds near the player.
        void ScanPlaces(Vector3 player)
        {
            busy.Clear();parks.Clear();var at=new Vector2(player.x,player.z);
            foreach(var place in ChangwonData.Places){
                if((place.pos-at).sqrMagnitude>300f*300f)continue;
                var k=place.kind;
                if(k=="food"||k=="cafe"||k=="market"||k=="mall")busy.Add(place.pos);else if(k=="park")parks.Add(place.pos);
            }
        }
        float Density(Vector3 p)
        {
            var at=new Vector2(p.x,p.z);
            foreach(var b in busy)if((b-at).sqrMagnitude<60f*60f)return 1f;
            switch(ChangwonData.LandAt(p.x,p.z)){
                case ChangwonData.Commercial:return 1f;case ChangwonData.Urban:return .7f;case ChangwonData.Campus:return .6f;case ChangwonData.Residential:return .35f;
                default:return .15f;
            }
        }
        static bool Urban(byte lc){return lc==ChangwonData.Residential||lc==ChangwonData.Commercial||lc==ChangwonData.Urban||lc==ChangwonData.Campus;}
        // ChangwonWorld draws raised pavements on urban Primary..Local roads, deciding "urban" from the road's first point.
        static bool Sidewalk(ChangwonData.Road r){return r.cls>=ChangwonData.Primary&&r.cls<=ChangwonData.Local&&Urban(ChangwonData.LandAt(r.pts[0].x,r.pts[0].z));}
        static bool Walkable(ChangwonData.Road r)
        {
            if(r.length<6f||!Sidewalk(r))return false;
            foreach(var k in r.kind)if(k!=ChangwonData.Ground)return false; // no bridges or tunnels
            return true;
        }

        void Spawn(Vector3 player)
        {
            var c=ChangwonData.ChunkOf(player.x,player.z);var look=ChangwonSession.PlayerForward;
            for(int tries=0;tries<10;tries++){
                ChangwonData.Road road=null;int dir=1,seg=1;float d=0;Vector3 p;
                if(parks.Count>0&&rnd.NextDouble()<.2){var park=parks[rnd.Next(parks.Count)];p=ParkPoint(new Vector3(park.x,0,park.y),35f);}
                else{
                    int cx=c.x+rnd.Next(-1,2),cz=c.y+rnd.Next(-1,2);if(cx<0||cz<0||cx>=ChangwonData.CX||cz>=ChangwonData.CZ)continue;
                    var ids=ChangwonData.RoadsInChunk[cz*ChangwonData.CX+cx];if(ids.Length==0)continue;
                    road=ChangwonData.Roads[ids[rnd.Next(ids.Length)]];if(!Walkable(road))continue;
                    Vector3 tangent;float along=(float)rnd.NextDouble()*road.length;p=ChangwonTraffic.Sample(road,ref seg,along,out tangent);
                    dir=rnd.Next(2)==0?1:-1;d=dir>0?along:road.length-along;
                }
                var v=p-player;v.y=0;float dist=v.magnitude;if(dist<40f||dist>220f)continue;
                if(tries<7&&dist<110f&&Vector3.Dot(v/dist,look)>.3f)continue; // not popping up in front of the player
                if(road!=null&&rnd.NextDouble()>Density(p))continue;
                Create(p,road,dir,d,seg);return;
            }
        }
        Walker Create(Vector3 at,ChangwonData.Road road,int dir,float d,int seg)
        {
            Transform[] legs,arms;var ped=ChangwonSession.Builder.ChangwonPerson(at,rnd,out legs,out arms);if(ped==null)return null;
            var w=new Walker{ped=ped,t=ped.transform,legs=legs,arms=arms,legRest=Rest(legs),armRest=Rest(arms),road=road,dir=dir,d=d,seg=seg,
                side=rnd.Next(2)==0?1:-1,speed=1.1f+(float)rnd.NextDouble()*.5f,phase=(float)rnd.NextDouble()*6f,jitter=((float)rnd.NextDouble()-.5f)*.6f,home=at,target=at};
            if(road!=null){Vector3 p;w.offset=Kerbside(w,out p);w.t.position=WalkStreet(w,0);}
            var f=road!=null?ChangwonTraffic.StartDir(road,dir>0):new Vector3((float)rnd.NextDouble()-.5f,0,(float)rnd.NextDouble()-.5f);
            if(f.sqrMagnitude>1e-6f)w.t.rotation=Quaternion.LookRotation(f);
            w.facing=w.t.rotation;people.Add(w);return w;
        }
        static Quaternion[] Rest(Transform[] parts){var r=new Quaternion[parts.Length];for(int i=0;i<parts.Length;i++)r[i]=parts[i].localRotation;return r;}
    }
}
