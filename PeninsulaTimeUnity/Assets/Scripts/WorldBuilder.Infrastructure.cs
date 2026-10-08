using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PeninsulaTime
{
    // Stairs between the street, the station concourse and the airport terminal.
    public class StationPortal : Interactable
    {
        public Vector3 destination,facing=Vector3.forward;
        public bool downstairs,walkThrough;
        public string label="",arrival=""; // hint before use; message after arriving
        public override string Hint{get{return label.Length>0?label:downstairs?"지하철역 들어가기":"지상으로 나가기";}}
    }

    // A KTX set on one measured OSM railway way at Seoul Station.
    public class RailVehicle : MonoBehaviour
    {
        // Platform service on a 30 s cycle per set: brake in from the far end of the track, stand with the doors
        // open, then leave back the same way (Seoul Station is a terminus). Two sets on different tracks, half a
        // cycle apart, stagger separate platform arrivals by 15 seconds. A set without a platform shuttles along its track.
        public const float Approach=8f/TransitSpeed.RailMultiplier,DwellSeconds=10f,Depart=8f/TransitSpeed.RailMultiplier,Cycle=30f,Shown=Approach+DwellSeconds+Depart;
        Vector3[] path;
        float[] distances;
        float length,travelled,speed;
        Quaternion modelRotation=Quaternion.identity;Renderer[] renderers;bool shown=true;
        public float stopAt=-1f,clock,reach=100f;
        public int outward=1;                 // +1: the far end of the track lies at larger distances along it
        public bool manual;public float manualOffset; // a rider's train: moved by the ride, not by the clock
        public VehicleDoors doors;
        public bool Dwelling{get{return stopAt>=0&&(manual?doors!=null&&doors.amount>.8f:clock>Approach+1.5f&&clock<Approach+DwellSeconds-2f);}}
        public float DwellLeft{get{return Approach+DwellSeconds-clock;}}
        public Vector3 Direction{get;private set;}
        public float Length{get{return length;}}
        public void Begin(List<Vector3> points,float metresPerSecond)
        {
            modelRotation=transform.rotation;
            path=points.ToArray();distances=new float[path.Length];speed=metresPerSecond*TransitSpeed.RailMultiplier;
            for(int i=1;i<path.Length;i++){length+=Vector3.Distance(path[i-1],path[i]);distances[i]=length;}
            travelled=0;PlaceAt(0,1);
        }
        // Serves the platform `at` metres along the track; `phase` seconds into its cycle.
        public void Serve(float at,float phase)
        {
            stopAt=at;clock=phase;outward=length-at>at?1:-1;
            reach=Mathf.Clamp((outward>0?length-at:at)-35f,40f,160f);
            Step(0);
        }
        float At(float t){return t<=length?t:2f*length-t;}
        void Update(){Step(Time.deltaTime);}
        public void Step(float delta)
        {
            if(path==null||length<.1f)return;
            if(stopAt<0)
            {
                travelled=Mathf.Repeat(travelled+delta*speed,length*2f);
                PlaceAt(At(travelled),travelled>length?-1:1);return;
            }
            float off;
            if(manual)off=manualOffset;
            else
            {
                clock=Mathf.Repeat(clock+delta,Cycle);off=Offset(clock);
                if(doors!=null)doors.Set(Mathf.Clamp01(Mathf.Min(clock-Approach-1f,Approach+DwellSeconds-1.5f-clock)/.9f));
            }
            Show(manual||clock<Shown);
            PlaceAt(stopAt+outward*off,-outward); // the front faces the buffer end; it leaves rear first
        }
        // Distance from the stopping point: braking in, standing, accelerating away.
        public float Offset(float t)
        {
            if(t<Approach){float u=(Approach-t)/Approach;return reach*u*u;}
            if(t<Approach+DwellSeconds)return 0;
            if(t<Shown){float d=(t-Approach-DwellSeconds)/Depart;return reach*d*d;}
            return reach+40f;
        }
        public void ArriveIn(float seconds){if(stopAt<0||manual)return;if(clock>Approach+DwellSeconds||clock<Approach-seconds)clock=Approach-seconds;}
        void Show(bool on)
        {
            if(on==shown)return;shown=on;
            if(renderers==null)renderers=GetComponentsInChildren<Renderer>(true);
            foreach(var r in renderers)if(r!=null)r.enabled=on;
        }
        void PlaceAt(float at,int facing)
        {
            if(path==null||path.Length<2)return;
            at=Mathf.Clamp(at,0,length);
            int i=1;while(i<distances.Length-1&&distances[i]<at)i++;
            var direction=(path[i]-path[i-1]).normalized*facing;
            transform.position=Vector3.Lerp(path[i-1],path[i],Mathf.InverseLerp(distances[i-1],distances[i],at));
            Direction=direction;
            if(direction.sqrMagnitude>.01f)transform.rotation=Quaternion.LookRotation(direction)*modelRotation;
        }
        // Distance along the path closest to p, or -1 when the path passes further than 12 m away.
        public float Nearest(Vector3 p)
        {
            float best=float.MaxValue,at=-1;
            for(int i=1;i<path.Length;i++)
            {
                var a=path[i-1];var b=path[i];var ab=b-a;float t=Mathf.Clamp01(Vector3.Dot(p-a,ab)/Mathf.Max(.001f,ab.sqrMagnitude));
                float d=Vector3.Distance(a+ab*t,p);if(d<best){best=d;at=distances[i-1]+t*ab.magnitude;}
            }
            return best<12f?at:-1f;
        }
    }

    public partial class WorldBuilder
    {
        static readonly CultureInfo NumberCulture=CultureInfo.InvariantCulture;
        readonly List<StationPortal> mappedPortals=new List<StationPortal>();
        readonly List<string> entranceRefs=new List<string>();
        readonly List<Vector3> railPlatforms=new List<Vector3>();
        TrafficGraph districtGraph;
        public int MappedParkingCount{get;private set;}
        public int MappedTrackCount{get;private set;}
        public int MappedEntranceCount{get;private set;}
        public int MovingTrainCount{get;private set;}
        public int BusStopCount{get;private set;}
        public Vector3 FirstBusStop{get;private set;}public Vector3 FirstBusStopFacing{get;private set;}bool firstStopIsBrt; // a BRT island if there is one
        public int AircraftCount{get;private set;}
        public Vector3 FirstParkingPosition{get;private set;}
        public Vector3 FirstEntrancePosition{get;private set;}
        public Vector3 FirstEntranceFacing{get;private set;}
        public Vector3 TerminalEntrance{get;private set;}
        public Vector3 TerminalFacing{get;private set;}
        RailVehicle firstKtx;Vector3 firstKtxPlatform;int platformTrains;
        // The platform where the first KTX stops (else any mapped platform).
        public Vector3 FirstRailPlatform{get{return firstKtx!=null?firstKtxPlatform:railPlatforms.Count>0?railPlatforms[railPlatforms.Count/2]:Vector3.zero;}}
        public void KtxArriveSoon(){if(firstKtx!=null)firstKtx.ArriveIn(8f);}
        public readonly List<RailVehicle> PlatformTrains=new List<RailVehicle>();

        static List<Vector3> FeaturePoints(string text,float height)
        {
            var points=new List<Vector3>();
            foreach(var pair in text.Split(';'))
            {
                var values=pair.Split(',');float x,z;
                if(values.Length==2&&float.TryParse(values[0],NumberStyles.Float,NumberCulture,out x)&&float.TryParse(values[1],NumberStyles.Float,NumberCulture,out z))
                    points.Add(new Vector3(x,height,z));
            }
            return points;
        }
        static Vector3 Centre(List<Vector3> points){var c=Vector3.zero;foreach(var p in points)c+=p;return points.Count>0?c/points.Count:c;}
        static List<Vector3> ClipParking(List<Vector3> boundary)
        {
            var result=new List<Vector3>(boundary);
            const float extent=310f;
            for(int axis=0;axis<2;axis++)for(int side=0;side<2;side++)
            {
                var source=result;result=new List<Vector3>();if(source.Count==0)break;
                float bound=side==0?-extent:extent;
                for(int i=0;i<source.Count;i++)
                {
                    var a=source[(i+source.Count-1)%source.Count];var b=source[i];
                    float av=axis==0?a.x:a.z,bv=axis==0?b.x:b.z;
                    bool ain=side==0?av>=bound:av<=bound,bin=side==0?bv>=bound:bv<=bound;
                    if(ain!=bin)result.Add(Vector3.Lerp(a,b,(bound-av)/(bv-av)));
                    if(bin)result.Add(b);
                }
            }
            return result;
        }
        void SurfaceParking(string id,List<Vector3> boundary)
        {
            if(boundary.Count<4)return;
            var clean=ClipParking(boundary);
            if(Vector3.Distance(clean[0],clean[clean.Count-1])<.1f)clean.RemoveAt(clean.Count-1);
            if(clean.Count<3)return;
            var surface=new GameObject("OSM 주차장 "+id);surface.transform.SetParent(root.transform,false);
            var mesh=new Mesh();mesh.vertices=clean.ToArray();
            mesh.triangles=TriangulateParking(clean).ToArray();mesh.RecalculateNormals();
            surface.AddComponent<MeshFilter>().sharedMesh=mesh;
            surface.AddComponent<MeshRenderer>().sharedMaterial=Mat("mapped-parking",new Color(.24f,.27f,.27f));
            if(MappedParkingCount==0)FirstParkingPosition=Centre(clean);
            for(int i=0;i<clean.Count;i++)Line(clean[i],clean[(i+1)%clean.Count],.035f,Mat("parking-border",new Color(.85f,.83f,.72f)),"주차장 경계");
            MappedParkingCount++;
        }
        static float Cross2(Vector3 a,Vector3 b,Vector3 c){return (b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x);}
        static bool InTriangle(Vector3 p,Vector3 a,Vector3 b,Vector3 c,float sign)
        {
            return Cross2(a,b,p)*sign>=0&&Cross2(b,c,p)*sign>=0&&Cross2(c,a,p)*sign>=0;
        }
        static List<int> TriangulateParking(List<Vector3> points)
        {
            float area=0;
            for(int i=0;i<points.Count;i++){var a=points[i];var b=points[(i+1)%points.Count];area+=a.x*b.z-b.x*a.z;}
            float sign=area>=0?1f:-1f;
            var remaining=new List<int>();for(int i=0;i<points.Count;i++)remaining.Add(i);
            var triangles=new List<int>();int attempts=0;
            while(remaining.Count>2&&attempts++<points.Count*points.Count)
            {
                bool found=false;
                for(int k=0;k<remaining.Count;k++)
                {
                    int a=remaining[(k+remaining.Count-1)%remaining.Count],b=remaining[k],c=remaining[(k+1)%remaining.Count];
                    if(Cross2(points[a],points[b],points[c])*sign<=.0001f)continue;
                    bool contains=false;
                    foreach(int other in remaining)if(other!=a&&other!=b&&other!=c&&InTriangle(points[other],points[a],points[b],points[c],sign)){contains=true;break;}
                    if(contains)continue;
                    if(sign>0){triangles.Add(c);triangles.Add(b);triangles.Add(a);}
                    else{triangles.Add(a);triangles.Add(b);triangles.Add(c);}
                    remaining.RemoveAt(k);found=true;break;
                }
                if(!found)break;
            }
            return triangles;
        }
        // Surface tracks are meshed by build_seoul_osm.py; tunnels stay hidden. Long surface ways get a KTX
        // that halts at a mapped platform and can be boarded there.
        void MappedTrack(string id,List<Vector3> points,bool underground)
        {
            if(underground)return;
            var clipped=new List<Vector3>();float total=0;
            foreach(var point in points)
            {
                if(Mathf.Abs(point.x)>300||Mathf.Abs(point.z)>300)continue;
                var p=new Vector3(point.x,.19f,point.z);
                if(clipped.Count>0)total+=Vector3.Distance(clipped[clipped.Count-1],p);
                clipped.Add(p);
            }
            if(clipped.Count<2||total<7f)return;
            MappedTrackCount++;
            if(total<120f||MovingTrainCount>=3)return;
            var train=CityModel("Ktx",clipped[0]);
            if(train==null)return;
            train.name="KTX "+id;
            var rail=train.AddComponent<RailVehicle>();rail.Begin(clipped,14f);
            FitBoxCollider(train);
            foreach(var platform in railPlatforms)
            {
                float at=rail.Nearest(platform);if(at<=40f||at>=rail.Length-40f||platformTrains>=2)continue;
                rail.Serve(at,platformTrains*RailVehicle.Cycle*.5f);platformTrains++;PlatformTrains.Add(rail);
                rail.doors=AttachKtxCabin(train,MovingTrainCount*53+7);
                Sfx.Attach(train,"rumble",.35f,60f);
                if(firstKtx==null){firstKtx=rail;firstKtxPlatform=platform;DepartureScreen(platform,TrackDirection(clipped,platform));}
                Marker(platform,"KTX");
                break;
            }
            var ktx=train.AddComponent<KtxTrain>();ktx.rail=rail;
            if(MovingTrainCount==1){ktx.destination="gwangju";ktx.destinationName="광주송정";}
            if(MovingTrainCount==2){ktx.destination="gangneung";ktx.destinationName="강릉";}
            MovingTrainCount++;
        }
        static Vector3 TrackDirection(List<Vector3> track,Vector3 near)
        {
            var best=Vector3.forward;float nearest=float.MaxValue;
            for(int i=1;i<track.Count;i++)
            {
                var a=track[i-1];var b=track[i];var ab=b-a;
                float u=Mathf.Clamp01(Vector3.Dot(near-a,ab)/Mathf.Max(.01f,ab.sqrMagnitude));float d=(a+ab*u-near).sqrMagnitude;
                if(d<nearest&&ab.sqrMagnitude>.01f){nearest=d;best=ab.normalized;}
            }
            best.y=0;return best.normalized;
        }
        // Departure screen hung over the KTX platform (both faces): the next KTX and 무궁화호 trains from the
        // timetable, as on the boards at Seoul Station.
        void DepartureScreen(Vector3 at,Vector3 along)
        {
            var station=TransitSchedule.RailStation("서울");if(station==null)return;
            // Along the platform from where the player arrives, so it is in view rather than overhead.
            var centre=new Vector3(at.x,at.y+4.2f,at.z)+along*14f;at+=along*14f;
            var screen=Primitive(PrimitiveType.Cube,"열차 출발 안내 화면",root.transform,Vector3.zero,new Vector3(6.4f,1.9f,.14f),Mat("arrival-screen",new Color(.03f,.03f,.04f)));
            screen.transform.position=centre;screen.transform.rotation=Quaternion.LookRotation(along);DestroyImmediate(screen.GetComponent<Collider>());
            foreach(float side in new[]{-.9f,.9f})
            {
                var post=Primitive(PrimitiveType.Cube,"안내 화면 기둥",root.transform,Vector3.zero,new Vector3(.12f,4.2f,.12f),Mat("rail-steel",new Color(.70f,.76f,.80f),.55f));
                post.transform.position=new Vector3(at.x,at.y+2.1f,at.z)+Vector3.Cross(Vector3.up,along)*side*3f;DestroyImmediate(post.GetComponent<Collider>());
            }
            foreach(var face in new[]{along,-along})
            {
                var text=Sign(TransitSchedule.DepartureText(station,5),root.transform,centre+face*.08f,face,.16f,new Color(1f,.62f,.12f));
                var board=text.gameObject.AddComponent<TransitBoard>();board.text=text;board.compose=()=>TransitSchedule.DepartureText(station,5);
            }
            Marker(at,"출발 안내");
        }
        // Seoul-style street entrance: low granite walls round a stair well under a glass canopy, opening toward
        // the nearest road, with the station name and exit number over the opening.
        void SurfaceEntrance(string id,string reference,Vector3 position,Vector3? surveyedDirection=null)
        {
            var open=surveyedDirection??TowardStreet(position);
            // Roadview 2026-06: Gangnam exit 11 opens along Gangnam-daero, not into traffic.
            bool surveyedArch=StationIndex==0&&reference=="11";
            if(surveyedArch){open=Vector3.Cross(Vector3.up,open).normalized;if(open.z>0)open=-open;}
            if(!surveyedDirection.HasValue)position=ClearEntranceAnchor(position,open);
            var entrance=new GameObject("지하철 "+reference+"번 출입구 "+id).transform;entrance.SetParent(root.transform,false);
            entrance.position=position;entrance.rotation=Quaternion.LookRotation(open);
            var stone=Mat("entrance-granite",new Color(.62f,.62f,.60f),0,"granite",1f);var steel=Mat("shelter-steel",new Color(.35f,.40f,.44f),.5f);
            var glass=Mat("station-glass",new Color(.55f,.72f,.78f),.3f);var dark=Mat("entrance-dark",new Color(.12f,.13f,.14f));
            var stepMat=Mat("stair-stone",new Color(.70f,.70f,.68f),0,"granite",1f);
            System.Func<string,Vector3,Vector3,Material,GameObject> part=(name,centre,size,mat)=>
            {var o=Primitive(PrimitiveType.Cube,name,entrance,centre,size,mat);o.transform.localRotation=Quaternion.identity;return o;};
            foreach(float side in new[]{-1.35f,1.35f})part("출입구 벽",new Vector3(side,.5f,-.1f),new Vector3(.25f,1f,4.2f),stone);
            // The old end cap crossed the open stairwell. The shaft below now encloses it.
            
            
            if(surveyedArch)BuildSurveyedEntranceRoof(entrance,steel,glass);
            else
            {
            foreach(float side in new[]{-1.35f,1.35f})foreach(float z in new[]{1.9f,-2.1f})DestroyImmediate(part("캐노피 기둥",new Vector3(side,1.85f,z),new Vector3(.12f,1.7f,.12f),steel).GetComponent<Collider>());
            DestroyImmediate(part("캐노피 유리",new Vector3(0,2.75f,-.1f),new Vector3(3.1f,.06f,4.4f),glass).GetComponent<Collider>());
            DestroyImmediate(part("캐노피 틀",new Vector3(0,2.75f,2.1f),new Vector3(3.2f,.18f,.12f),steel).GetComponent<Collider>());
            }
            var line=PlatformLines(StationIndex)[0];
            if(!surveyedArch)
            {
            var board=part("출입구 안내판",new Vector3(0,3.15f,2.1f),new Vector3(3.0f,.6f,.1f),Glow("entrance-board",new Color(.16f,.17f,.19f),.2f));DestroyImmediate(board.GetComponent<Collider>());
            DestroyImmediate(part("노선색 띠",new Vector3(0,2.9f,2.16f),new Vector3(3.0f,.08f,.02f),Mat("line-band-"+line.line,line.color)).GetComponent<Collider>());
            Sign(StationTitle(StationIndex)+"  "+reference+"번 출구",entrance,entrance.TransformPoint(new Vector3(0,3.15f,2.17f)),open,.24f,new Color(1f,.84f,.2f));
            }
            if(surveyedArch)
            {
                var pylon=part("강남11 세로 안내기둥",new Vector3(-1.8f,1.9f,1.9f),new Vector3(.44f,3.8f,.32f),steel);
                Sign("11\n강남\nGangnam",entrance,entrance.TransformPoint(new Vector3(-1.8f,2.4f,2.07f)),open,.17f,Color.white);
            }
            // The clickable stair opening; it also stops walkers stepping into the well.
            var well=new GameObject("출입구 계단 입구");well.transform.SetParent(entrance,false);well.transform.localPosition=new Vector3(0,.6f,-.1f);
            // No blocker or trigger: the physical stairs below carry the walker.
            var portal=well.AddComponent<StationPortal>();portal.downstairs=true;portal.label=reference+"번 출입구로 내려가기";
            ClearStreetFurniture(position+open*2f,6f); // the entrance and the pavement in front of it
            Marker(position,"지하철 "+reference);
            if(MappedEntranceCount==0){FirstEntrancePosition=position;FirstEntranceFacing=open;}
            mappedPortals.Add(portal);entranceRefs.Add(reference);MappedEntranceCount++;
        }
        // Horizontal direction from a point to the nearest drivable road (back if none).
        Vector3 TowardStreet(Vector3 position)
        {
            if(districtGraph==null)return Vector3.back;
            float best=float.MaxValue;var target=position+Vector3.back;
            for(int a=0;a<districtGraph.nodes.Count;a++)foreach(int b in districtGraph.links[a])
            {
                var p=districtGraph.nodes[a];var q=districtGraph.nodes[b];var ab=q-p;
                float t=Mathf.Clamp01(Vector3.Dot(position-p,ab)/Mathf.Max(.001f,ab.sqrMagnitude));var c=p+ab*t;
                float d=(new Vector2(c.x-position.x,c.z-position.z)).sqrMagnitude;if(d<best){best=d;target=c;}
            }
            var dir=target-position;dir.y=0;return dir.sqrMagnitude>.01f?dir.normalized:Vector3.back;
        }
        // Trees and lamps planted before the mapped facilities were known are moved out of their way.
        void ClearStreetFurniture(Vector3 centre,float radius)
        {
            foreach(Transform child in root.transform)
            {
                if(child.name!="Tree model"&&child.name!="Pine model"&&child.name!="StreetLight model"&&child.name!="Tree trunk collider")continue;
                var d=child.position-centre;d.y=0;if(d.magnitude<radius)child.gameObject.SetActive(false);
            }
        }
        // Concourse exits, one per mapped street entrance, along the walls of the unpaid area.
        void LinkStationExits()
        {
            if(StationIndex==3){LinkGimpoExits();return;}
            var spots=new List<Vector3>();var outward=new List<Vector3>();
            var dark=Mat("entrance-dark",new Color(.12f,.13f,.14f));var frameMat=Mat("exit-frame",new Color(.30f,.32f,.34f),.4f);
            var stone=Mat("stair-stone",new Color(.70f,.70f,.68f),0,"granite",1f);
            // Numbered exits run in order along the wall, as in a real concourse.
            var order=new List<int>();for(int i=0;i<mappedPortals.Count;i++)order.Add(i);
            order.Sort((a,b)=>{int x,y;int.TryParse(entranceRefs[a],out x);int.TryParse(entranceRefs[b],out y);return x!=y?x.CompareTo(y):a.CompareTo(b);});
            // Choose a hall opening on the side facing the real street entrance.
            // The shared east/west openings are on the unpaid side of the gates.
            int south=0;
            for(int n=0;n<order.Count;n++){
                var at=mappedPortals[order[n]].transform.position;
                bool side=Mathf.Abs(at.x)>Mathf.Abs(at.z)*.6f||at.z>0;
                if(side){float sign=at.x<0?-1:1;spots.Add(new Vector3(sign*HalfX,ConcourseY,-9.5f));outward.Add(Vector3.right*sign);}
                else {float x=-HalfX+7+south*4; south++;if(x>-6&&x<6)x+=12;
                    spots.Add(new Vector3(Mathf.Min(x,HalfX-7),ConcourseY,-HalfZ));outward.Add(Vector3.back);}
            }
            var usedSpots=new HashSet<Vector3>();
            int count=Mathf.Min(order.Count,spots.Count);
            for(int n=0;n<count;n++)
            {
                int i=order[n];var spot=spots[n];var o=outward[n];mappedPortals[i].destination=spot-o*2.4f+Vector3.up*1.65f;mappedPortals[i].facing=-o;if(!usedSpots.Add(spot))continue;var right=Vector3.Cross(Vector3.up,o);
                var exit=new GameObject(entranceRefs[i]+"번 출구");exit.transform.SetParent(station,false);exit.transform.localPosition=spot;
                exit.transform.rotation=Quaternion.LookRotation(o);
                foreach(float side in new[]{-1.3f,1.3f})
                {var post=Primitive(PrimitiveType.Cube,"출구 틀",station,spot+right*side+Vector3.up*1.4f,new Vector3(.2f,2.8f,.5f),frameMat);post.transform.rotation=exit.transform.rotation;DestroyImmediate(post.GetComponent<Collider>());}
                
                var refs=new List<string>();for(int k=0;k<count;k++)if(spots[k]==spot)refs.Add(entranceRefs[order[k]]);
                Board(string.Join("·",refs)+"번 출구",station,spot+Vector3.up*3.2f-o*.05f,-o,new Vector2(2.2f,.6f),new Color(.12f,.12f,.12f),new Color(1f,.82f,.1f),.3f);
                var up=exit.AddComponent<StationPortal>();up.downstairs=false;up.walkThrough=true;up.label=entranceRefs[i]+"번 출구 · 걸어서 나가기";up.arrival=StationTitle(StationIndex)+" "+entranceRefs[i]+"번 출구로 나왔습니다";
                Marker(spot,entranceRefs[i]);
                var street=mappedPortals[i];
                var front=street.transform.parent!=null?street.transform.parent.forward:Vector3.back;
                up.destination=street.transform.position+front*3.4f+Vector3.up*1.05f;up.facing=front;
                street.destination=spot-o*2.4f+Vector3.up*1.65f;street.facing=-o;
            }
            // Any entrances beyond the concourse's exit openings share them.
            for(int n=count;n<order.Count&&count>0;n++)
            {var extra=mappedPortals[order[n]];var shared=mappedPortals[order[n%count]];extra.destination=shared.destination;extra.facing=shared.facing;}
            ConnectWalkingExits(spots,order,outward);
        }
        // Bus stop shelters; stops beside bus lanes become BRT island stops where blue buses halt.
        void BusStop(string id,string name,Vector3 position)
        {
            if(districtGraph==null||Mathf.Abs(position.x)>295||Mathf.Abs(position.z)>295)return;
            int bestA=-1,bestB=-1;float best=float.MaxValue,along=0;bool lane=false;
            for(int a=0;a<districtGraph.nodes.Count;a++)foreach(int b in districtGraph.links[a])
            {
                if(!districtGraph.Allowed(a,b))continue;
                var pa=districtGraph.nodes[a];var pb=districtGraph.nodes[b];var ab=pb-pa;
                float t=Mathf.Clamp01(Vector3.Dot(position-pa,ab)/Mathf.Max(.01f,ab.sqrMagnitude));
                float d=Vector3.Distance(new Vector3(position.x,pa.y,position.z),pa+ab*t);
                bool isLane=districtGraph.BusOnly(a,b);
                // Kerb stops serve the direction that has them on its right; bus lanes serve their own direction.
                if(!isLane&&Cross2(pa,pb,position)>=0)continue;
                float score=d-(isLane?6f:0);
                if(d<14f&&score<best&&t>.05f&&t<.95f){best=score;bestA=a;bestB=b;along=t*ab.magnitude;lane=isLane;}
            }
            if(bestA<0){Shelter(id,name,position,Vector3.forward,false);BusStopCount++;return;}
            districtGraph.AddStop(bestA,bestB,along,name.Length>0?name:"정류장");
            var start=districtGraph.nodes[bestA];var dir=(districtGraph.nodes[bestB]-start).normalized;
            var kerb=start+dir*along+Vector3.Cross(Vector3.up,dir)*(districtGraph.Width(bestA,bestB)*.5f+(lane?1.4f:2f));
            Shelter(id,name,new Vector3(kerb.x,0,kerb.z),dir,lane);
            var station=StopStation(id,name);
            if(station!=null)busStops.Add(new BusStopInfo{id=id,name=name,a=bestA,b=bestB,along=along,lane=lane,position=new Vector3(kerb.x,0,kerb.z),station=station});
            BusStopCount++;
        }
        void Shelter(string id,string name,Vector3 p,Vector3 along,bool island)
        {
            var parent=new GameObject((island?"BRT 정류장 ":"버스 정류장 ")+name).transform;parent.SetParent(root.transform,false);parent.position=p;
            parent.rotation=Quaternion.LookRotation(along);
            ClearStreetFurniture(p,island?3.2f:2.8f);
            Marker(p,island?"BRT":"버스");
            var steel=Mat("shelter-steel",new Color(.35f,.40f,.44f),.5f);var glass=Mat("station-glass",new Color(.55f,.72f,.78f),.3f);
            var roofMat=Mat("shelter-roof",new Color(.20f,.45f,.70f));
            if(island)Block("BRT 승강장",parent,new Vector3(-1.2f,0,-14),new Vector3(1.2f,.22f,14),Mat("platform-concrete",new Color(.70f,.69f,.65f),0,"concrete",2f));
            float y=island?.22f:.14f,half=island?5f:2.5f;
            Block("정류장 지붕",parent,new Vector3(-1.1f,y+2.55f,-half),new Vector3(1.1f,y+2.67f,half),roofMat);
            foreach(float z in new[]{-half+.3f,half-.3f})Block("정류장 기둥",parent,new Vector3(.85f,y,z-.05f),new Vector3(.95f,y+2.6f,z+.05f),steel);
            Block("정류장 유리",parent,new Vector3(.93f,y+.5f,-half+.4f),new Vector3(.97f,y+2.3f,half-.4f),glass);
            Block("정류장 의자",parent,new Vector3(.4f,y+.42f,-1.2f),new Vector3(.85f,y+.5f,1.2f),Mat("bench",new Color(.55f,.40f,.26f),0,"timber",1f));
            Block("정류장 이름판",parent,new Vector3(-.03f,y+2.75f,-1.4f),new Vector3(.03f,y+3.15f,1.4f),roofMat,false);
            var toStreet=-Vector3.Cross(Vector3.up,parent.forward);
            Sign((island?"BRT · ":"")+(name.Length>0?name:"버스 정류장"),parent,parent.TransformPoint(new Vector3(-.06f,y+2.95f,0)),toStreet,.22f,Color.white);
            BusInformation(id,name,parent,y,half);
            if(FirstBusStop==Vector3.zero||island&&!firstStopIsBrt){FirstBusStop=p+parent.forward*4.55f;FirstBusStopFacing=toStreet;firstStopIsBrt=island;} // looking up the lane
        }
        // BIS screen under the shelter roof: next buses from the network timetable; click it for the full board.
        // The network stop for a mapped bus stop (by OSM id, else by name).
        static NetStation StopStation(string id,string name)
        {
            var stop=TransitNetwork.Station("b"+id);
            if(stop==null||stop.lines.Count==0)
                foreach(var s in TransitNetwork.Named(name))if(s.lines.Exists(l=>l.kind=="bus"||l.kind=="brt")){stop=s;break;}
            return stop;
        }
        void BusInformation(string id,string name,Transform shelter,float y,float half)
        {
            var stop=StopStation(id,name);
            if(stop==null)return;
            var panel=Block("버스 도착 안내 단말기",shelter,new Vector3(-.95f,y+1.75f,half-.75f),new Vector3(.95f,y+2.5f,half-.65f),Mat("arrival-screen",new Color(.03f,.03f,.04f)));
            var text=Sign(BusStops.Text(stop,4),shelter,shelter.TransformPoint(new Vector3(0,y+2.12f,half-.77f)),-shelter.forward,.1f,new Color(1f,.62f,.12f));
            var board=text.gameObject.AddComponent<TransitBoard>();board.text=text;board.compose=()=>BusStops.Text(stop,4);
            AddFixture(panel,"bis","버스 도착 정보 보기",stop.id);
        }
        void BuildMappedInfrastructure(string district)
        {
            MappedParkingCount=MappedTrackCount=MappedEntranceCount=MovingTrainCount=BusStopCount=AircraftCount=0;MapMarkers.Clear();
            mappedPortals.Clear();entranceRefs.Clear();railPlatforms.Clear();firstKtx=null;platformTrains=0;PlatformTrains.Clear();busStops.Clear();
            FirstParkingPosition=FirstEntrancePosition=TerminalEntrance=FirstBusStop=Vector3.zero;firstStopIsBrt=false;
            var asset=Resources.Load<TextAsset>("Geo/"+district+"Features");
            if(asset==null)return;
            var rows=new List<string[]>();
            foreach(var raw in asset.text.Split('\n'))
            {
                if(raw.Length<2||raw[0]=='#')continue;
                var fields=raw.Trim().Split('|');
                if(fields.Length<4)continue;
                rows.Add(fields);
                if(fields[0]=="R")railPlatforms.Add(Centre(FeaturePoints(fields[fields.Length-1],0)));
            }
            StationIndex=Mathf.Max(0,System.Array.IndexOf(new[]{"Gangnam","SeoulStation","Hongdae","GimpoAirport"},district));
            var stands=new List<Vector3>();var bridges=new List<List<Vector3>>();
            foreach(var fields in rows)
            {
                var points=FeaturePoints(fields[fields.Length-1],0);
                switch(fields[0])
                {
                    case "P":SurfaceParking(fields[1],FeaturePoints(fields[3],.115f));break;
                    case "T":MappedTrack(fields[1],points,fields[2]=="-5");break;
                    case "E":if(StationIndex!=3&&points.Count>0)SurfaceEntrance(fields[1],fields[2],points[0]);break;
                    case "S":if(points.Count>0)BusStop(fields[1],fields[2],points[0]);break;
                    case "A":if(points.Count>0)stands.Add(Centre(points));break;
                    case "J":if(points.Count>1)bridges.Add(points);break;
                    case "X":if(points.Count>2)TerminalEntrance=TerminalDoor(points);break;
                }
            }
            int index=System.Array.IndexOf(new[]{"Gangnam","SeoulStation","Hongdae","GimpoAirport"},district);
            if(index==3)BuildGimpoEntrances();
            BuildStation(Mathf.Max(0,index));
            LinkStationExits();
            BuildDistrictTransferGuides();
            if(district=="GimpoAirport")BuildAirport(stands,bridges);
        }
    }
}
