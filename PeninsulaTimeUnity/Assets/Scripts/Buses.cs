using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    public struct BusArrival{public string route;public NetLine line;public double seconds,at;public int stopsAway;}

    // Game timetable: route headway has priority. First calls stagger 15 seconds; every route repeats at 50 seconds.
    public static class BusStops
    {
        public const double Gap=15,Period=50,Standing=10;
        public const int Slots=20;
        static readonly Dictionary<string,List<NetLine>> routes=new Dictionary<string,List<NetLine>>();
        static int builtFor=-1;
        static int Stable(string text){int h=17;foreach(char c in text)h=unchecked(h*31+c);return h&0x7fffffff;}
        static bool IsBus(NetLine l){return l.kind=="bus"||l.kind=="brt";}

        public static List<NetLine> RoutesAt(NetStation stop)
        {
            if(builtFor!=TransitNetwork.Version){routes.Clear();builtFor=TransitNetwork.Version;}
            List<NetLine> list;if(routes.TryGetValue(stop.id,out list))return list;
            list=new List<NetLine>();var names=new HashSet<string>();
            foreach(var l in stop.lines)if(IsBus(l)&&names.Add(l.shortName))list.Add(l);
            // Exact BIS memberships take precedence. Legacy mapped stops without memberships may
            // use routes with another stop within 150 m; never borrow routes from another city.
            if(list.Count>0){routes[stop.id]=list;return list;}
            var others=new List<KeyValuePair<float,NetLine>>();float scale=Mathf.Cos(stop.lat*Mathf.Deg2Rad);
            foreach(var l in TransitNetwork.Lines)
            {
                if(!IsBus(l)||names.Contains(l.shortName))continue;
                float best=float.MaxValue;
                foreach(var s in l.stops){float dx=(s.lon-stop.lon)*scale,dy=s.lat-stop.lat;best=Mathf.Min(best,dx*dx+dy*dy);}
                if(best<=Mathf.Pow(150f/111320f,2))others.Add(new KeyValuePair<float,NetLine>(best,l));
            }
            others.Sort((a,b)=>a.Key.CompareTo(b.Key));
            foreach(var o in others){if(list.Count>=Slots)break;if(names.Add(o.Value.shortName))list.Add(o.Value);}
            routes[stop.id]=list;return list;
        }
        // The next `count` buses at the stop, soonest first; one standing at the stop now is included (seconds < 0).
        public static List<BusArrival> Next(NetStation stop,double now,int count)
        {
            var result=new List<BusArrival>();var list=RoutesAt(stop);
            if(list.Count==0)return result;
            int slots=list.Count;
            double phase=TransitSchedule.ServiceEpoch;
            for(int k=0;k<slots;k++)
            {
                var line=list[k];
                double slot=phase+Gap*k,n=System.Math.Max(0,System.Math.Ceiling((now-Standing-slot)/Period)),at=slot+n*Period;
                result.Add(new BusArrival{route=line.shortName,line=line,at=at,seconds=at-now,stopsAway=Mathf.Max(1,Mathf.CeilToInt((float)(at-now)/90f))});
            }
            result.Sort((a,b)=>a.seconds.CompareTo(b.seconds));
            if(result.Count>count)result.RemoveRange(count,result.Count-count);
            return result;
        }
        public static string Describe(BusArrival a)
        {
            if(a.seconds<=0)return "정류장 도착";
            if(a.seconds<60)return "곧 도착";
            return Mathf.FloorToInt((float)a.seconds/60)+"분 "+Mathf.FloorToInt((float)a.seconds%60)+"초 후 · "+a.stopsAway+"번째 전";
        }
        // A stop's BIS screen ("143  1분 12초 후 · 1번째 전").
        public static string Text(NetStation stop,int rows)
        {
            if(stop==null)return "";
            var text=stop.name+"   "+TransitSchedule.Clock(TransitSchedule.Now);
            var next=Next(stop,TransitSchedule.Now,rows);
            foreach(var a in next)text+="\n"+a.route+"  "+Describe(a);
            if(next.Count==0)text+="\n도착 정보 없음";
            return text;
        }
    }

    // A mapped bus stop on the district road graph: link a->b, metres along it, the network stop for its timetable.
    public class BusStopInfo{public string id,name;public int a,b;public float along;public bool lane;public Vector3 position;public NetStation station;}

    // Buses to the stop nearest the player, on the stop's timetable: each one is sent from about 80 m up the road,
    // with its route on the signs, and taken away once it is far behind the player.
    public partial class WorldBuilder
    {
        const float DispatchLead=11f,DispatchRange=160f,BusSpeed=8.5f;
        const int MaxRouteBuses=10;
        readonly List<BusStopInfo> busStops=new List<BusStopInfo>();
        readonly List<TrafficVehicle> routeBuses=new List<TrafficVehicle>();
        readonly HashSet<string> dispatched=new HashSet<string>();
        float dispatchCheck;
        public IList<BusStopInfo> BusStopList{get{return busStops;}}

        void Update(){DispatchBuses();}
        void DispatchBuses()
        {
            if(busStops.Count==0||districtGraph==null||Time.time<dispatchCheck)return;
            dispatchCheck=Time.time+.25f;
            var eye=Viewer.position;
            routeBuses.RemoveAll(b=>b==null);
            foreach(var bus in routeBuses)
                if(!bus.CarryingPlayer&&(bus.transform.position-eye).sqrMagnitude>220f*220f&&Time.time-bus.Born>25f)Destroy(bus.gameObject);
            if(eye.y<-2f||Mathf.Abs(eye.x)>400f)return;
            BusStopInfo stop=null;float best=DispatchRange*DispatchRange;
            foreach(var s in busStops){float d=(s.position-eye).sqrMagnitude;if(d<best){best=d;stop=s;}}
            if(stop==null)return;
            foreach(var a in BusStops.Next(stop.station,TransitSchedule.Now,3))
            {
                if(a.seconds<-BusStops.Standing*.5||a.seconds>DispatchLead)continue;
                string key=stop.id+"/"+a.route+"/"+(long)a.at;
                if(dispatched.Contains(key)||routeBuses.Count>=MaxRouteBuses)continue;
                if(SendBus(stop,a.route,(float)a.seconds))dispatched.Add(key);
            }
        }
        // A bus of `route` placed upstream of the stop so that it reaches it in about `seconds`.
        bool SendBus(BusStopInfo stop,string route,float seconds)
        {
            var director=root.GetComponentInChildren<TrafficDirector>();if(director==null)return false;
            float distance=Mathf.Clamp(BusSpeed*seconds,20f,95f);
            var path=new List<int>{stop.a,stop.b};float travelled=stop.along-distance;
            // Back along the roads leading into the stop's link, preferring the straightest.
            int guard=0;
            while(travelled<0&&guard++<6)
            {
                int head=path[0],after=path[1];
                var into=districtGraph.Into(head,true);into.Remove(after);
                if(into.Count==0){travelled=0;break;}
                var ahead=(districtGraph.nodes[after]-districtGraph.nodes[head]).normalized;int pick=into[0];float straight=-2f;
                foreach(int p in into){float dot=Vector3.Dot((districtGraph.nodes[head]-districtGraph.nodes[p]).normalized,ahead);if(dot>straight){straight=dot;pick=p;}}
                path.Insert(0,pick);travelled+=Vector3.Distance(districtGraph.nodes[pick],districtGraph.nodes[head]);
            }
            travelled=Mathf.Max(0,travelled);
            var start=Vector3.Lerp(districtGraph.nodes[path[0]],districtGraph.nodes[path[1]],travelled/Mathf.Max(.01f,Vector3.Distance(districtGraph.nodes[path[0]],districtGraph.nodes[path[1]])));
            foreach(var v in TrafficVehicle.Active)if(v!=null&&(v.transform.position-start).sqrMagnitude<9f*9f)return false; // retry while the road is occupied
            var bus=CityModel(BusModel(route),Vector3.zero);if(bus==null)bus=CityModel("Bus",Vector3.zero);if(bus==null)return false;
            bus.name=route+"번 버스";MakeVehicleInteractive(bus,true);
            var driver=bus.AddComponent<TrafficVehicle>();driver.Bus=true;driver.Route=route;
            int seed=route.GetHashCode()&0xffff;
            driver.BeginOnPath(districtGraph,path,travelled,BusSpeed,.06f,11f,seed);
            driver.doors=AttachBusCabin(bus,seed);driver.cabin=driver.doors.cabin;
            RouteSign(bus,route);Sfx.Attach(bus,"bus-engine",.45f,35f);
            director.AddPlaced(driver);routeBuses.Add(driver);
            return true;
        }
    }
}
