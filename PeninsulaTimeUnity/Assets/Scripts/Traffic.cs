using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PeninsulaTime
{
    // Road network as nodes and links. Junctions with three or more links run a two-phase signal.
    public class TrafficGraph
    {
        public const int Red=0,Yellow=1,Green=2;
        public const float GreenSeconds=10f,YellowSeconds=2.5f;
        public readonly List<Vector3> nodes=new List<Vector3>();
        public readonly List<List<int>> links=new List<List<int>>();
        public readonly Dictionary<long,float> widths=new Dictionary<long,float>();
        public float clock;
        readonly Dictionary<Vector3Int,int> index=new Dictionary<Vector3Int,int>();
        readonly HashSet<long> busOnly=new HashSet<long>(),blocked=new HashSet<long>();
        readonly Dictionary<long,float> stops=new Dictionary<long,float>();
        readonly Dictionary<long,string> stopNames=new Dictionary<long,string>();
        const float Snap=.5f,LevelSnap=2f;

        public int Node(Vector3 position)
        {
            // Bridges keep their own nodes where they pass over a road.
            var key=new Vector3Int(Mathf.RoundToInt(position.x/Snap),Mathf.RoundToInt(position.z/Snap),Mathf.RoundToInt(position.y/LevelSnap));
            int id;
            if(index.TryGetValue(key,out id))return id;
            id=nodes.Count;index[key]=id;nodes.Add(position);links.Add(new List<int>());
            return id;
        }
        public void Link(Vector3 a,Vector3 b,float width,bool bus=false,bool oneway=false)
        {
            int i=Node(a),j=Node(b);
            if(i==j||links[i].Contains(j))return;
            turnPaths.Clear();
            links[i].Add(j);links[j].Add(i);widths[Key(i,j)]=width;widths[Key(j,i)]=width;
            if(bus){busOnly.Add(Key(i,j));busOnly.Add(Key(j,i));}
            if(oneway)blocked.Add(Key(j,i));
        }
        // Bus lanes (OSM highway=busway) carry buses only; one-way lanes are driven in their mapped direction.
        public bool BusOnly(int a,int b){return busOnly.Contains(Key(a,b));}
        public bool Allowed(int a,int b){return !blocked.Contains(Key(a,b));}
        public bool HasBusLanes{get{return busOnly.Count>0;}}
        public void AddStop(int a,int b,float along,string name=""){stops[Key(a,b)]=along;stopNames[Key(a,b)]=name;}
        public bool StopOn(int a,int b,out float along){return stops.TryGetValue(Key(a,b),out along);}
        public string StopName(int a,int b){string n;return stopNames.TryGetValue(Key(a,b),out n)?n:"";}
        // Links leading into node b that a vehicle may drive (for buses sent toward a stop from upstream).
        public List<int> Into(int b,bool bus){var r=new List<int>();foreach(int a in links[b])if(Allowed(a,b)&&(bus||!BusOnly(a,b)))r.Add(a);return r;}
        public int StopCount{get{return stops.Count;}}
        public float Width(int a,int b){float w;return widths.TryGetValue(Key(a,b),out w)?w:3.2f;}
        static long Key(int a,int b){return ((long)a<<32)|(uint)b;}

        public bool IsSignal(int node){return links[node].Count>=3;}
        public float JunctionRadius(int node)
        {
            float widest=0;foreach(int other in links[node])widest=Mathf.Max(widest,Width(node,other));
            return widest*.5f+.6f;
        }
        // Approaches roughly parallel to the junction's first link share phase 0; the others phase 1.
        public int Group(int node,int fromNode)
        {
            var reference=(nodes[links[node][0]]-nodes[node]).normalized;
            var approach=(nodes[fromNode]-nodes[node]).normalized;
            return Mathf.Abs(Vector3.Dot(reference,approach))>=.7071f?0:1;
        }
        public int LampState(int node,int group)
        {
            float phase=GreenSeconds+YellowSeconds;
            float t=Mathf.Repeat(clock+node*3.7f,phase*2)-group*phase;
            if(t<0)t+=phase*2;
            return t<GreenSeconds?Green:t<phase?Yellow:Red;
        }
        public bool MayEnter(int node,int fromNode){return !IsSignal(node)||LampState(node,Group(node,fromNode))==Green;}
        public float Lane(int a,int b){return blocked.Contains(Key(b,a))?0:Mathf.Clamp(Width(a,b)*.25f,.8f,3f);}
        public float StopLine(int from,int to,float bodyLength){return Vector3.Distance(nodes[from],nodes[to])-JunctionRadius(to)-1.5f-bodyLength*.5f;}

        // Junction box reservations: a movement (from -> node -> next) may enter only when its curve
        // keeps clear of every reserved curve and does not merge into the same exit.
        readonly Dictionary<int,List<TrafficVehicle>> reserved=new Dictionary<int,List<TrafficVehicle>>();
        readonly Dictionary<Vector3Int,List<Vector2>> turnPaths=new Dictionary<Vector3Int,List<Vector2>>();
        public bool CanReserve(int node,int from,int next,TrafficVehicle self)
        {
            List<TrafficVehicle> inside;
            if(!reserved.TryGetValue(node,out inside)||inside.Count==0)return true;
            var mine=TurnPath(from,node,next);
            foreach(var other in inside)
            {
                if(other==null||other==self)continue;
                float clearance=.8f*(Lane(from,node)+Lane(other.HeldFrom,node));
                if(other.HeldNext==next||PathsNear(mine,TurnPath(other.HeldFrom,node,other.HeldNext),clearance))return false;
            }
            return true;
        }
        public void Reserve(int node,TrafficVehicle vehicle)
        {
            List<TrafficVehicle> inside;
            if(!reserved.TryGetValue(node,out inside))reserved[node]=inside=new List<TrafficVehicle>();
            if(!inside.Contains(vehicle))inside.Add(vehicle);
        }
        public void Release(int node,TrafficVehicle vehicle){List<TrafficVehicle> inside;if(reserved.TryGetValue(node,out inside))inside.Remove(vehicle);}

        // Smooth lane-to-lane curve through a node: s=0 at the entry edge of the box, s=1 at the exit edge.
        public float TurnRadius(int other,int node){return Mathf.Min(JunctionRadius(node),Vector3.Distance(nodes[other],nodes[node])*.45f);}
        public Vector3 TurnPoint(int from,int node,int next,float s,out Vector3 facing)
        {
            var centre=nodes[node];var inward=(centre-nodes[from]).normalized;var outward=(nodes[next]-centre).normalized;
            float rin=TurnRadius(from,node),rout=TurnRadius(next,node),span=rin+rout;
            var p0=centre-inward*rin+Vector3.Cross(Vector3.up,inward)*Lane(from,node);
            var p1=centre+outward*rout+Vector3.Cross(Vector3.up,outward)*Lane(node,next);
            Vector3 m0=inward*span*.55f,m1=outward*span*.55f;
            float s2=s*s,s3=s2*s;
            facing=(6*s2-6*s)*p0+(3*s2-4*s+1)*m0+(-6*s2+6*s)*p1+(3*s2-2*s)*m1;
            return (2*s3-3*s2+1)*p0+(s3-2*s2+s)*m0+(-2*s3+3*s2)*p1+(s3-s2)*m1;
        }
        List<Vector2> TurnPath(int from,int node,int next)
        {
            var key=new Vector3Int(from,node,next);List<Vector2> points;
            if(turnPaths.TryGetValue(key,out points))return points;
            points=new List<Vector2>(13);Vector3 facing;
            for(int i=0;i<=12;i++){var p=TurnPoint(from,node,next,i/12f,out facing);points.Add(new Vector2(p.x,p.z));}
            turnPaths.Add(key,points);
            return points;
        }
        static bool PathsNear(List<Vector2> a,List<Vector2> b,float clearance)
        {
            for(int i=1;i<a.Count;i++)for(int j=1;j<b.Count;j++)
            {
                if(SegmentsCross(a[i-1],a[i],b[j-1],b[j]))return true;
                if(PointToSegment(a[i],b[j-1],b[j])<clearance||PointToSegment(b[j],a[i-1],a[i])<clearance)return true;
            }
            return false;
        }
        static float PointToSegment(Vector2 p,Vector2 a,Vector2 b)
        {
            var ab=b-a;float t=ab.sqrMagnitude<1e-6f?0:Mathf.Clamp01(Vector2.Dot(p-a,ab)/ab.sqrMagnitude);
            return Vector2.Distance(p,a+ab*t);
        }
        static float Side(Vector2 a,Vector2 b,Vector2 p){return (b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x);}
        static bool SegmentsCross(Vector2 a,Vector2 b,Vector2 c,Vector2 d)
        {
            float d1=Side(c,d,a),d2=Side(c,d,b),d3=Side(a,b,c),d4=Side(a,b,d);
            return d1*d2<=0&&d3*d4<=0;
        }

        // Lines of "[B]width[>] x,z[,y] ..." written by AssetSources/build_seoul_osm.py: B bus only, > one way, y on bridges.
        public static TrafficGraph FromRoadText(string text)
        {
            var graph=new TrafficGraph();
            foreach(var raw in text.Split('\n'))
            {
                var line=raw.Trim();if(line.Length==0||line[0]=='#')continue;
                var parts=line.Split(' ');
                if(parts.Length<3)continue;
                bool bus=parts[0].StartsWith("B"),oneway=parts[0].EndsWith(">");
                float width;if(!float.TryParse(parts[0].Trim('B','>'),NumberStyles.Float,CultureInfo.InvariantCulture,out width))continue;
                Vector3? previous=null;
                for(int i=1;i<parts.Length;i++)
                {
                    var xz=parts[i].Split(',');float x,z,y=0;
                    if(xz.Length<2||!float.TryParse(xz[0],NumberStyles.Float,CultureInfo.InvariantCulture,out x)||!float.TryParse(xz[1],NumberStyles.Float,CultureInfo.InvariantCulture,out z))continue;
                    if(xz.Length>2)float.TryParse(xz[2],NumberStyles.Float,CultureInfo.InvariantCulture,out y);
                    var p=new Vector3(x,y,z);
                    if(previous.HasValue)graph.Link(previous.Value,p,width,bus,oneway);
                    previous=p;
                }
            }
            return graph;
        }
    }

    // Steps every vehicle and repaints the signal lamps.
    public class TrafficDirector : MonoBehaviour
    {
        public TrafficGraph graph;
        readonly List<TrafficVehicle> vehicles=new List<TrafficVehicle>();
        readonly List<Pedestrian> walkers=new List<Pedestrian>();
        readonly List<Renderer> lamps=new List<Renderer>();
        readonly List<Vector2Int> lampSignals=new List<Vector2Int>();
        readonly List<int> lampStates=new List<int>();
        float cleanupIn;
        Material[] lampMaterials;

        public List<TrafficVehicle> Vehicles{get{return vehicles;}}
        public void Init(TrafficGraph network,Material red,Material yellow,Material green){graph=network;lampMaterials=new[]{red,yellow,green};}
        public void Add(TrafficVehicle vehicle)
        {
            if(!vehicle.FindClearStart(vehicles))
            {
                DestroyImmediate(vehicle.gameObject);
                return;
            }
            vehicles.Add(vehicle);
        }
        // A vehicle already placed on a planned path (a bus sent to a stop): no search for a clear start.
        public void AddPlaced(TrafficVehicle vehicle){vehicles.Add(vehicle);}
        public void AddWalker(Pedestrian walker){walkers.Add(walker);}
        public List<Pedestrian> Walkers{get{return walkers;}}
        public void AddLamp(Renderer lamp,int node,int group){lamps.Add(lamp);lampSignals.Add(new Vector2Int(node,group));lampStates.Add(-1);Paint(lamps.Count-1);}
        void Update(){Step(Time.deltaTime);}
        static readonly System.Predicate<Pedestrian> GonePerson=p=>p==null;
        static readonly System.Predicate<TrafficVehicle> GoneVehicle=v=>v==null;
        public void Step(float seconds)
        {
            if(graph==null)return;
            cleanupIn-=seconds;
            if(cleanupIn<=0)
            {
                cleanupIn=1f;
                Pedestrian.Walking.RemoveAll(GonePerson);TrafficVehicle.Active.RemoveAll(GoneVehicle);vehicles.RemoveAll(GoneVehicle);walkers.RemoveAll(GonePerson);
            }
            graph.clock+=seconds;
            foreach(var vehicle in vehicles)if(vehicle!=null)vehicle.Step(seconds,vehicles);
            foreach(var walker in walkers)if(walker!=null)walker.Step(seconds);
            for(int i=0;i<lamps.Count;i++)Paint(i);
        }
        void Paint(int i)
        {
            int state=graph.LampState(lampSignals[i].x,lampSignals[i].y);
            if(lampStates[i]==state||lamps[i]==null)return;
            lampStates[i]=state;lamps[i].sharedMaterial=lampMaterials[state];
        }
    }

    // Keeps to the right-hand lane, follows the vehicle ahead, stops at red lights and reserves its junction path.
    public class TrafficVehicle : MonoBehaviour
    {
        public const float Acceleration=4f,Clearance=2.5f,DwellSeconds=10f,HalfWidth=1.05f,YieldGap=1.8f;
        // Every live vehicle, for pedestrians that must not walk into one.
        public static readonly List<TrafficVehicle> Active=new List<TrafficVehicle>();
        // The walking player's feet, or null when the player is not on foot in a street view.
        public static Vector3? PlayerFeet;
        // Registered once driving (also in edit-mode checks, where OnEnable does not run); destroyed vehicles drop out.
        void OnEnable(){if(graph!=null&&!Active.Contains(this))Active.Add(this);}
        void OnDisable(){Active.Remove(this);}
        // True when p is on this vehicle's footprint, widened by margin.
        public bool Covers(Vector3 p,float margin)
        {
            var o=p-transform.position;if(Mathf.Abs(o.y)>2.5f)return false;
            var forward=Heading.sqrMagnitude>.5f?Heading:transform.forward;forward.y=0;forward.Normalize();
            var right=Vector3.Cross(Vector3.up,forward);
            return Mathf.Abs(Vector3.Dot(o,forward))<BodyLength*.5f+margin&&Mathf.Abs(Vector3.Dot(o,right))<HalfWidth+margin;
        }
        // Distance the vehicle may still drive before a person standing or walking in its lane ahead.
        float PedestrianRoom()
        {
            if(Heading.sqrMagnitude<.5f)return float.MaxValue;
            float room=float.MaxValue;
            foreach(var walker in Pedestrian.Walking)if(walker!=null)room=Mathf.Min(room,RoomBefore(walker.transform.position));
            if(PlayerFeet.HasValue)room=Mathf.Min(room,RoomBefore(PlayerFeet.Value));
            return room;
        }
        float RoomBefore(Vector3 person)
        {
            var o=person-transform.position;if(Mathf.Abs(o.y)>2.5f)return float.MaxValue;
            float ahead=Vector3.Dot(o,Heading),reach=BodyLength*.5f+speed*speed/(2*Acceleration)+YieldGap+4f;
            if(ahead<0||ahead>reach||Mathf.Abs(Vector3.Dot(o,Vector3.Cross(Vector3.up,Heading)))>HalfWidth+.45f)return float.MaxValue;
            return ahead-BodyLength*.5f-YieldGap;
        }
        // Buses use bus lanes when they can and halt at stops; cars never enter bus lanes.
        public bool Bus{get;set;}
        // Buses: route number on the signs, the stop being served, the walkable inside and its doors.
        public string Route="";
        public string StopName{get{return stopName;}}
        string stopName="";
        public Cabin cabin;public VehicleDoors doors;
        // The player is aboard: at the end of a one-way street the bus stops (종점) instead of re-entering elsewhere.
        public bool CarryingPlayer,AtTerminus;
        // A player driven car stays where it is parked after the player gets out.
        public bool PlayerControlled{get;private set;}
        float manualSpeed;
        public float Born; // when it was made; dispatched buses are kept a while before removal
        readonly Queue<int> route=new Queue<int>(); // nodes to drive through next (a bus sent toward a stop)
        readonly List<int> routeOptions=new List<int>(8);
        readonly Collider[] drivingOverlaps=new Collider[64];
        public bool Dwelling{get{return dwell>0;}}
        public int From{get;private set;}
        public int To{get;private set;}
        public float Travelled{get;private set;}
        public float BodyLength{get;private set;}
        public int HeldFrom{get;private set;}
        public int HeldNext{get;private set;}
        public Vector3 Heading{get;private set;}
        int heldNode=-1,previous=-1;float dwell;long servedStop=-1;float kerb;
        TrafficGraph graph;int next;float cruise,speed,height;Quaternion modelRotation;System.Random random;

        public void Begin(TrafficGraph network,int start,float cruiseSpeed,float roadHeight,float bodyLength,int seed)
        {
            graph=network;From=start;cruise=cruiseSpeed;height=roadHeight;BodyLength=bodyLength;random=new System.Random(seed);
            if(!Active.Contains(this))Active.Add(this);
            modelRotation=transform.rotation;
            To=PickFirst();
            next=PickNext();
            float limit=graph.IsSignal(To)?Mathf.Max(0,graph.StopLine(From,To,BodyLength)):Length();
            Travelled=(float)random.NextDouble()*limit;
            Place();
            if(!Usable(From,To))Reenter(Active); // started at the end of a one-way street
        }
        // Starts on a planned path (node list), `travelled` metres along its first link, then drives on freely.
        public void BeginOnPath(TrafficGraph network,List<int> path,float travelled,float cruiseSpeed,float roadHeight,float bodyLength,int seed)
        {
            graph=network;cruise=cruiseSpeed;height=roadHeight;BodyLength=bodyLength;random=new System.Random(seed);Born=Time.time;
            if(!Active.Contains(this))Active.Add(this);
            modelRotation=transform.rotation;
            From=path[0];To=path[1];route.Clear();for(int i=2;i<path.Count;i++)route.Enqueue(path[i]);
            next=PickNext();Travelled=travelled;speed=cruise*.8f;Place();
        }
        // A one-way street that leaves the mapped area: the car drives off the map and enters again elsewhere,
        // instead of turning round against the traffic.
        void Reenter(List<TrafficVehicle> traffic)
        {
            if(heldNode>=0){graph.Release(heldNode,this);heldNode=-1;}
            for(int attempt=0;attempt<40;attempt++)
            {
                int start=random.Next(graph.nodes.Count);
                if(!graph.links[start].Exists(o=>Usable(start,o)))continue;
                From=start;To=PickFirst();next=PickNext();previous=-1;Travelled=0;speed=0;Place();
                bool clear=true;
                foreach(var other in traffic)
                    if(other!=this&&other!=null&&Vector3.Distance(transform.position,other.transform.position)<(BodyLength+other.BodyLength)*.5f+Clearance){clear=false;break;}
                if(clear)return;
            }
        }
        bool Usable(int a,int b){return graph.Allowed(a,b)&&(Bus||!graph.BusOnly(a,b));}
        int PickFirst()
        {
            var options=graph.links[From];
            routeOptions.Clear();foreach(int candidate in options)if(Usable(From,candidate))routeOptions.Add(candidate);
            return routeOptions.Count>0?routeOptions[random.Next(routeOptions.Count)]:options[random.Next(options.Count)];
        }
        public bool FindClearStart(List<TrafficVehicle> existing)
        {
            for(int attempt=0;attempt<80;attempt++)
            {
                bool clear=true;
                foreach(var other in existing)
                {
                    if(Vector3.Distance(transform.position,other.transform.position)<(BodyLength+other.BodyLength)*.5f+Clearance){clear=false;break;}
                }
                if(clear)return true;
                From=random.Next(graph.nodes.Count);
                if(graph.links[From].Count==0)continue;
                To=PickFirst();next=PickNext();previous=-1;
                float limit=graph.IsSignal(To)?Mathf.Max(0,graph.StopLine(From,To,BodyLength)):Length();
                Travelled=(float)random.NextDouble()*limit;Place();
            }
            return false;
        }
        float Length(){return Mathf.Max(.01f,Vector3.Distance(graph.nodes[From],graph.nodes[To]));}
        public void Step(float seconds,List<TrafficVehicle> traffic)
        {
            if(graph==null||PlayerControlled)return;
            float length=Length();
            float room=float.MaxValue;bool queued=false;
            foreach(var other in traffic)
            {
                if(other==this||other==null)continue;
                float spacing=(BodyLength+other.BodyLength)*.5f+Clearance;
                if(other.PlayerControlled)
                {
                    var offset=other.transform.position-transform.position;
                    float ahead=Vector3.Dot(offset,Heading);
                    if(ahead>0&&Mathf.Abs(Vector3.Dot(offset,Vector3.Cross(Vector3.up,Heading)))<HalfWidth*2f)
                        room=Mathf.Min(room,ahead-spacing);
                    continue;
                }
                if(other.From==From&&other.To==To&&(other.Travelled>Travelled||(other.Travelled==Travelled&&other.GetInstanceID()<GetInstanceID())))
                {room=Mathf.Min(room,other.Travelled-Travelled-spacing);queued=true;}
                else if(other.From==To&&other.To==next)
                    room=Mathf.Min(room,length-Travelled+other.Travelled-spacing);
            }
            float stopLine=graph.StopLine(From,To,BodyLength);
            if(graph.IsSignal(To)&&heldNode!=To&&Travelled<=stopLine+.2f)
            {
                // The first car in the queue enters on green, with room to clear the box, away from reserved paths.
                float toLine=stopLine-Travelled;
                bool go=!queued&&graph.MayEnter(To,From)&&room>toLine+graph.JunctionRadius(To)*2+BodyLength&&graph.CanReserve(To,From,next,this);
                if(go&&toLine<=speed*speed/(2*Acceleration)+2f){if(heldNode>=0)graph.Release(heldNode,this);graph.Reserve(To,this);heldNode=To;HeldFrom=From;HeldNext=next;}
                else room=Mathf.Min(room,toLine);
            }
            if(Bus)room=Mathf.Min(room,StopRoom(seconds));
            room=Mathf.Min(room,PedestrianRoom());
            float target=room<=0?0:Mathf.Min(cruise,room*1.5f);
            speed=target<speed?target:Mathf.MoveTowards(speed,target,Acceleration*seconds);
            Travelled+=Mathf.Clamp(speed*seconds,0,Mathf.Max(0,room));
            if(Travelled>=length&&!Usable(To,next))
            {
                // With the player aboard the bus ends here, doors open, until they get off.
                if(CarryingPlayer||AtTerminus){Travelled=length;speed=0;AtTerminus=CarryingPlayer;dwell=AtTerminus?1f:0;Doors(seconds);Place(seconds);if(AtTerminus)return;}
                Reenter(traffic);return;
            }
            if(Travelled>=length){Travelled-=length;previous=From;From=To;To=next;next=PickNext();}
            if(heldNode>=0&&heldNode!=To&&(heldNode!=From||Travelled>graph.JunctionRadius(heldNode)+BodyLength*.5f)){graph.Release(heldNode,this);heldNode=-1;}
            Doors(seconds);
            Place(seconds);
        }
        public void TakeWheel(){if(Bus)return;PlayerControlled=true;manualSpeed=0;speed=0;if(heldNode>=0){graph.Release(heldNode,this);heldNode=-1;}}
        // Drive relative to the car's current heading. The box checks buildings, people and vehicles,
        // while low road and pavement surfaces stay below the collision volume.
        public void ManualDrive(float throttle,float steer,float seconds,float limit=245f)
        {
            if(!PlayerControlled||Bus||seconds<=0)return;
            float target=throttle>=0?throttle*13f:throttle*5f;
            manualSpeed=Mathf.MoveTowards(manualSpeed,target,(Mathf.Abs(throttle)>.01f?5f:9f)*seconds);
            var heading=Heading.sqrMagnitude>.5f?Heading:transform.forward;
            heading.y=0;heading.Normalize();
            float turn=steer*75f*seconds*Mathf.Clamp01(Mathf.Abs(manualSpeed)/4f)*Mathf.Sign(manualSpeed);
            heading=Quaternion.Euler(0,turn,0)*heading;
            var proposed=transform.position+heading*manualSpeed*seconds;
            if(Mathf.Abs(proposed.x)>limit||Mathf.Abs(proposed.z)>limit){manualSpeed=0;return;}
            var boxCenter=proposed+Vector3.up*1.25f;
            int overlapCount=Physics.OverlapBoxNonAlloc(boxCenter,new Vector3(HalfWidth*.85f,.8f,Mathf.Max(1f,BodyLength*.45f)),drivingOverlaps,Quaternion.LookRotation(heading),~0,QueryTriggerInteraction.Ignore);
            // A full buffer may have omitted an obstacle: stop conservatively.
            if(overlapCount==drivingOverlaps.Length){manualSpeed=0;return;}
            for(int i=0;i<overlapCount;i++)
            {
                var obstacle=drivingOverlaps[i];
                if(obstacle==null||obstacle.transform.IsChildOf(transform))continue;
                if(obstacle.bounds.max.y<proposed.y+.48f)continue;
                manualSpeed=0;return;
            }
            Heading=heading;transform.position=proposed;
            transform.rotation=Quaternion.LookRotation(heading)*modelRotation;
        }
        // Doors open once the bus has stood a moment at a stop and close before it pulls away.
        void Doors(float seconds)
        {
            if(doors==null)return;
            bool open=AtTerminus||(dwell>.8f&&dwell<DwellSeconds-1.2f);
            float before=doors.amount;doors.Move(open,seconds,.9f);
            if(before<=0&&doors.amount>0)Sfx.PlayAt("air",transform.position,.7f);
        }
        // Distance a bus may still drive before its stop on this link; holds it there for DwellSeconds.
        float StopRoom(float seconds)
        {
            float stopAt;long edge=((long)From<<32)|(uint)To;
            if(edge==servedStop||!graph.StopOn(From,To,out stopAt)||Travelled>stopAt+.5f)return float.MaxValue;
            if(stopAt-Travelled>.4f)return stopAt-Travelled;
            if(dwell==0)stopName=graph.StopName(From,To);
            dwell+=seconds;
            if(dwell<DwellSeconds)return 0;
            dwell=0;servedStop=edge;return float.MaxValue;
        }
        int PickNext()
        {
            while(route.Count>0){int planned=route.Dequeue();if(graph.links[To].Contains(planned)&&Usable(To,planned))return planned;route.Clear();}
            var options=graph.links[To];
            routeOptions.Clear();
            foreach(int o in options)if((o!=From||options.Count==1)&&Usable(To,o))routeOptions.Add(o);
            if(Bus)
            {
                int busLanes=0;
                foreach(int candidate in routeOptions)if(graph.BusOnly(To,candidate))busLanes++;
                if(busLanes>0)
                {
                    int pick=random.Next(busLanes);
                    foreach(int candidate in routeOptions)if(graph.BusOnly(To,candidate)&&pick--==0)return candidate;
                }
            }
            if(routeOptions.Count==0)return Usable(To,From)||options.Count==1?From:options[random.Next(options.Count)];
            return routeOptions[random.Next(routeOptions.Count)];
        }
        void Place(float seconds=0)
        {
            var a=graph.nodes[From];var b=graph.nodes[To];var direction=(b-a).normalized;
            float length=Length(),remaining=length-Travelled;
            Vector3 position,facing=direction;
            if(next!=From&&graph.links[To].Count>=2&&remaining<graph.TurnRadius(From,To))
            {
                float rin=graph.TurnRadius(From,To),rout=graph.TurnRadius(next,To);
                position=graph.TurnPoint(From,To,next,(rin-remaining)/(rin+rout),out facing);
            }
            else if(previous>=0&&previous!=To&&graph.links[From].Count>=2&&Travelled<graph.TurnRadius(To,From))
            {
                float rin=graph.TurnRadius(previous,From),rout=graph.TurnRadius(To,From);
                position=graph.TurnPoint(previous,From,To,(rin+Travelled)/(rin+rout),out facing);
            }
            else
            {
                // Buses pull over to the kerb for a stop on an ordinary street.
                float stopAt,lane=graph.Lane(From,To);
                bool pull=Bus&&!graph.BusOnly(From,To)&&graph.StopOn(From,To,out stopAt)&&Mathf.Abs(Travelled-stopAt)<28f;
                kerb=Mathf.MoveTowards(kerb,pull?1:0,seconds*.6f);
                lane=Mathf.Lerp(lane,Mathf.Max(lane,graph.Width(From,To)*.5f-1.5f),kerb);
                position=Vector3.Lerp(a,b,Travelled/length)+Vector3.Cross(Vector3.up,direction).normalized*lane;
            }
            transform.position=position+Vector3.up*height;
            facing.y=0;
            float rise=(b.y-a.y)/Mathf.Max(.01f,new Vector2(b.x-a.x,b.z-a.z).magnitude);
            if(facing.sqrMagnitude>1e-6f){Heading=facing.normalized;transform.rotation=Quaternion.LookRotation(Heading+Vector3.up*rise)*modelRotation;}
        }
    }
}
