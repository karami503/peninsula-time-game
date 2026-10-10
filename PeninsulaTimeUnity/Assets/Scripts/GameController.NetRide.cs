using UnityEngine;

namespace PeninsulaTime
{
    // Rides on the national network: KTX/무궁화호 from Seoul Station, every district subway line, and any train
    // boarded at a network station. A stop that is a detailed district (강남, 김포 ...) is that district, built the same
    // way, with the train running into its own platform track; any other stop is a network hub. The rider's train runs on along a RailCorridor;
    // the station it stops at next is built while the train is between stations, out of sight of both, and the
    // corridor is moved onto that station's platform. The rider never leaves the car and nothing fades; stepping off
    // at a stop leaves them on that station's platform, and staying aboard carries them on to the next stop.
    // A ticket bought at a station makes the next train of that line run straight to its destination.
    public partial class GameController
    {
        const float NetLeg=3000f,NetSwap=1700f,MetroLeg=1400f,MetroSwap=700f,NetBrake=15f,NetDwell=10f,NetBoardHold=6f;
        bool NetMetro{get{return netLine!=null&&netLine.kind=="metro";}}
        float Leg{get{return NetMetro?MetroLeg:NetLeg;}}
        float Swap{get{return NetMetro?MetroSwap:NetSwap;}}
        Transform netVehicle;VehicleDoors netDoors;TrainConsist netConsist;RailVehicle netRail;
        StationJourney netOrigin,netHidden; // the boarded station's own service (frozen), the arrival station's own train (hidden)
        SubwayTrain netTrack; // the district platform track the train stands at; its own trains wait until this one has gone
        bool netForeign; // the train came in along a corridor: not one of netTrack's own, it leaves for good
        // Seoul Station's surface platforms: the last netReach metres run along that platform's mapped track, whose own
        // KTX is hidden meanwhile; netModel is the vehicle's rotation relative to its heading.
        RailVehicle netPlatformRail;float netReach;Quaternion netModel;
        float NetEnd{get{return Leg+(netPlatformRail!=null?netReach:0);}}
        NetLine netLine;int netDirection;NetStation netFrom,netNext;
        RailCorridor netCorridor;string netPhase="";float netAlong,netSpeed,netTimer,netDoorsOpen;bool netLoaded,netTicketed;
        public bool NetRideActive{get{return netPhase.Length>0;}}
        public string NetRidePhase{get{return netPhase;}}
        Cabin NetCabin{get{return netConsist!=null?netConsist.cabin:netDoors!=null?netDoors.cabin:null;}}
        bool RiderAboard{get{return cabin!=null&&cabin==NetCabin;}}
        static float NetCruise(string kind){return kind=="ktx"?140f:kind=="mugunghwa"?110f:SubwayTrain.CruiseSpeed;}
        static float GradeY(NetStation s){return s.grade=="underground"?-12f:s.grade=="elevated"?8f:0f;}
        // A stop's rail height above its ground as this ride meets it: Seoul Station's KTX/무궁화호 platforms are on its
        // surface tracks, and a subway train calls at the detailed districts' underground platforms.
        float NetGrade(NetStation s)
        {
            if(s==null)return 0f;
            int district=WorldBuilder.DistrictOf(s.name);
            if(district==SeoulStationDistrict&&(netLine.kind=="ktx"||netLine.kind=="mugunghwa"))return 0f;
            return district>=0&&netLine.kind=="metro"?-12f:GradeY(s);
        }
        // Tunnel or open line toward the next stop, and the ground easing toward its level.
        void StyleNetLeg()
        {
            float grade=NetGrade(netNext);
            netCorridor.endTunnel=grade<-2f;netCorridor.portal=Leg*.7f;netCorridor.groundEnd=-grade;netCorridor.groundSpan=Leg;
        }
        NetStation NextStop(NetStation from)
        {
            if(netLine==null||from==null)return null;
            var next=StationJourney.Next(netLine,from,netDirection,1);
            return next.Count>1?next[1]:null;
        }

        // ---------- boarding ----------
        void StartNetRideFromHub(StationJourney journey)
        {
            if(NetRideActive)EndNetRide(); // the train last ridden, still pulling out, leaves for good
            netLine=journey.line;netDirection=journey.direction;netFrom=journey.Current;
            netTicketed=ticketTrip!=null&&ticketTrip.line==journey.line&&ticketTrip.direction==journey.direction;
            netNext=netTicketed?ticketTrip.station:NextStop(netFrom);
            if(netNext==null){Toast("종착역입니다 · 반대 방향 열차를 이용하세요");return;}
            netOrigin=journey;journey.enabled=false; // its timetable stops; this ride moves the train now
            netVehicle=journey.train;netDoors=journey.doors;netConsist=null;netRail=null;netHidden=null;
            BeginNetDwell(NetBoardHold);
            Toast(netLine.name+" 승차 · "+(netTicketed?netNext.name+"까지 바로 갑니다":"다음 역 "+netNext.name)+" · 문이 닫히면 출발");
        }
        void StartNetRideFromConsist(TrainConsist c)
        {
            if(c==null||c==netConsist||c.train==null)return; // back aboard the train being ridden
            var side=c.train.line;
            if(side.net==null||side.index<0||side.index>=side.net.stops.Count)return;
            if(NetRideActive)EndNetRide();
            netLine=side.net;netDirection=side.direction;netFrom=netLine.stops[side.index];
            netTicketed=ticketTrip!=null&&ticketTrip.line==netLine&&ticketTrip.direction==netDirection;
            netNext=netTicketed?ticketTrip.station:NextStop(netFrom);
            if(netNext==null){Toast("종착역입니다");return;}
            netConsist=c;netTrack=c.train;netVehicle=c.transform;netDoors=null;netRail=null;netOrigin=null;netHidden=null;
            c.manual=true;foreach(var other in c.train.consists)if(other!=c)other.suppressed=true;
            BeginNetDwell(Mathf.Max(5f,SubwayTrain.Approach+SubwayTrain.Dwell-2.2f-c.clock));
            Toast(side.line+" "+side.toward+" 열차에 탔습니다 · "+(netTicketed?netNext.name+"까지 바로 갑니다":"다음 역 "+netNext.name)+" · 어느 역에서든 열린 문으로 내립니다");
        }
        // Seoul Station's KTX, already pulling out along its mapped track: the ride continues from where it is.
        void StartNetRideFromRail(RailVehicle rail,NetLine line,int direction,NetStation from,Vector3 travel,float speed)
        {
            netLine=line;netDirection=direction;netFrom=from;netTicketed=false;netNext=NextStop(from);
            if(netNext==null)return;
            netRail=rail;rail.enabled=false;netVehicle=rail.transform;netDoors=rail.doors;netConsist=null;netOrigin=null;netHidden=null;
            travel.y=0;if(travel.sqrMagnitude<1e-4f)travel=rail.transform.forward;
            netCorridor=world.BeginNetworkCorridor(netVehicle.position,Quaternion.LookRotation(travel.normalized),0,false,-netVehicle.position.y);
            StyleNetLeg();
            netVehicle.SetParent(netCorridor.transform,true);
            netAlong=0;netSpeed=speed;netLoaded=false;netPhase="run";netTimer=0;netDoorsOpen=0;
            world.StreamCorridor(netCorridor,netAlong);
        }
        void BeginNetDwell(float hold){netPhase="dwell";netTimer=-hold;netDoorsOpen=1;PlaceNet(1);}

        // ---------- every frame ----------
        void UpdateNetRide(float dt)
        {
            if(!NetRideActive)return;
            if(netVehicle==null||NetCabin==null){EndNetRide();return;} // the world was rebuilt under it
            netTimer+=dt;
            switch(netPhase)
            {
                case "dwell":
                    if(!RiderAboard){CancelNetRide();return;} // stepped back out before departure
                    PlaceNet(Mathf.MoveTowards(netDoorsOpen,1,dt/1.2f));
                    if(netTimer>=0){netPhase="close";netTimer=0;Sfx.PlayAt("door-chime",eye.position,.8f);}
                    break;
                case "close":
                    PlaceNet(Mathf.MoveTowards(netDoorsOpen,0,dt/1.6f));
                    if(netTimer>2.2f)BeginNetLeg();
                    break;
                case "run":RunNetLeg(dt);break;
                case "arrived":
                    PlaceNet(Mathf.MoveTowards(netDoorsOpen,1,dt/1.2f));
                    if(!RiderAboard&&netTimer>1f){netPhase="leave";netTimer=0;break;}
                    if(RiderAboard&&netTimer>NetDwell&&netNext!=null){netPhase="close";netTimer=0;Sfx.PlayAt("door-chime",eye.position,.8f);}
                    break;
                case "leave": // empty, on to the next station and out of sight
                    if(netTimer<4f)break;
                    PlaceNet(Mathf.MoveTowards(netDoorsOpen,0,dt/1.6f));
                    if(netDoorsOpen>0)break;
                    if(netCorridor!=null&&netVehicle.parent!=netCorridor.transform)netVehicle.SetParent(netCorridor.transform,true); // off the district platform
                    if(netPlatformRail!=null) // a terminus: it backs out the way it came
                    {
                        netSpeed=Mathf.Min(NetCruise(netLine.kind)*.3f,netSpeed+NetBrake*dt);netAlong-=netSpeed*dt;PlaceNet(0);
                        if(netAlong<Leg-200f){EndNetRide();return;}
                        break;
                    }
                    netSpeed=Mathf.Min(NetCruise(netLine.kind),netSpeed+NetBrake*dt);netAlong+=netSpeed*dt;PlaceNet(0);
                    if(netAlong>Leg+320f){EndNetRide();return;}
                    break;
            }
            if(RiderAboard)CarryInCabin();
        }
        // Leaves the platform the train stands at: a new corridor starting there.
        void BeginNetLeg()
        {
            var old=netCorridor;
            if(netTrack!=null)
            {
                // From a district platform: the corridor starts on its track (where the train already stands, if it came in along one).
                var track=netTrack;
                netCorridor=world.BeginCorridor(track,track.TravelSign);
                netCorridor.startTunnel=netCorridor.endTunnel=true;netCorridor.groundY=-track.transform.position.y;
                netAlong=old==null?netConsist.offset*track.direction*track.TravelSign:0;
            }
            else
            {
                // From a network station: its own track runs on to the end of its service's stops.
                var service=world.NetworkServices.Find(s=>s.line==netLine)??(world.NetworkServices.Count>0?world.NetworkServices[0]:null);
                float cover=service!=null?(service.stops.Count-1)*StationJourney.Spacing+300f:300f;
                bool under=netFrom!=null&&NetGrade(netFrom)<-2f;
                netCorridor=world.BeginNetworkCorridor(netVehicle.position,Quaternion.identity,cover,under,netFrom!=null?-NetGrade(netFrom):-netVehicle.position.y);
                netAlong=0;
            }
            StyleNetLeg();
            netVehicle.SetParent(netCorridor.transform,true);
            if(old!=null)DestroyImmediate(old.gameObject);
            netSpeed=0;netLoaded=false;netPhase="run";netTimer=0;
            PlaceNet(0);world.StreamCorridor(netCorridor,netAlong);
        }
        void RunNetLeg(float dt)
        {
            float brake=Mathf.Sqrt(2f*NetBrake*Mathf.Max(0,NetEnd-netAlong));
            netSpeed=Mathf.Min(Mathf.Min(NetCruise(netLine.kind),netSpeed+NetBrake*dt),Mathf.Max(.8f,brake));
            netAlong=Mathf.Min(NetEnd,netAlong+netSpeed*dt);
            if(!netLoaded&&netAlong>=Swap){LoadNetStop();if(!NetRideActive)return;}
            // The station left behind gets its own trains back once this one is well away.
            if(!netLoaded&&netAlong>400f)ShowHidden();
            PlaceNet(0);
            world.StreamCorridor(netCorridor,netAlong);
            if(netAlong<NetEnd)return;
            netSpeed=0;netPhase="arrived";netTimer=0;
            if(netForeign&&netTrack!=null)
            {
                // At a district platform: the train stands on its track, so the screen doors open with its doors.
                netVehicle.SetParent(netTrack.transform,true);netConsist.Place(0,0,netTrack.direction);
                if(!netTrack.consists.Contains(netConsist))netTrack.consists.Add(netConsist);
            }
            netFrom=netNext;
            if(netTicketed&&ticketTrip!=null){Debug.Log("Ticket arrived kind="+netLine.kind+" line="+netLine.name+" to="+netFrom.name+" after="+(Time.unscaledTime-ticketTrip.startedAt).ToString("F1")+"s");Log(netFrom.name+"역 도착 · "+netLine.name);ticketTrip=null;}
            netTicketed=false;
            netNext=netPlatformRail!=null?null:NextStop(netFrom); // Seoul Station's surface platforms end at buffers
            state.selectedCity=WorldBuilder.NearestCity(TransitNetwork.Bare(netFrom.name));
            string here=TransitNetwork.Bare(netFrom.name);
            if(netLine.kind=="ktx"||netLine.kind=="mugunghwa")Announcer.Say("chime",Announcer.TrainNext(netLine.kind=="ktx"?"KTX":"무궁화호",here));
            else Announcer.Say("chime",Announcer.MetroNext(here));
            Toast(netFrom.name+" 도착 · 문이 열리면 걸어서 내립니다"+(netNext!=null?" · 계속 타면 "+netNext.name+"까지":" · 종착역"));
        }
        // Between stations: the station behind is gone from sight, so it is unloaded and the next one is built;
        // then the corridor (train and rider inside) is moved onto that station's platform track.
        void LoadNetStop()
        {
            netLoaded=true;
            // 진해구 is one carved world of its own: the ride ends on its station's platform there.
            var jinhae=JinhaeStopNear(netNext,netLine);if(jinhae!=null){ArriveJinhaeByTrain(jinhae);return;}
            var c=netCorridor;c.transform.position=new Vector3(50000,0,0); // away from what is being built
            netOrigin=null;netHidden=null;netTrack=null;stationJourney=null;networkPassengerJourney=null;
            if(LoadNetDistrict(c)||LoadNetTerminal(c))return;
            var hub=world.BuildNetworkStation(netNext,netLine,netDirection);
            mode="rail";tab="3D";streetBoard=false;selectedStation=null;world.dayNight=true;cityStreet=false;undergroundWalk=false;
            stationJourney=hub;stationAnnounced=hub.index;
            var service=world.NetworkServices.Find(s=>s.line==netLine)??hub;
            netHidden=service;service.enabled=false;if(service.train!=null)service.train.gameObject.SetActive(false);
            world.ReleaseCorridorStart(c);
            float grade=GradeY(netNext);
            world.AlignCorridorTo(c,service.origin,Quaternion.identity,1f,Leg,grade<-2f?85f:300f);
            // Network platforms are on the track's +x side.
            if(netConsist!=null)
            {
                int inHub=netConsist.cabin.doorSide*(Vector3.Dot(netConsist.transform.right,Vector3.right)>0?1:-1);
                if(inHub!=1)netConsist.UseDoorSide(-netConsist.cabin.doorSide);
            }
            else if(netDoors!=null&&netDoors.cabin!=null&&netDoors.cabin.doorSide!=0)
                netDoors.UseSide(Vector3.Dot(netDoors.cabin.transform.right,Vector3.right)>0?1:-1);
            PlaceNet(0);
            Physics.SyncTransforms();CarryInCabin();PlaceViewCamera();
        }
        // A detailed district on this line and direction: built instead of a hub, the corridor ending on its platform track.
        bool LoadNetDistrict(RailCorridor c)
        {
            if(netConsist==null&&(netLine.kind!="metro"||netDoors==null||netDoors.cabin==null))return false;
            int district=WorldBuilder.DistrictOf(netNext.name);if(district<0)return false;
            int side=WorldBuilder.PlatformLines(district).FindIndex(s=>s.net==netLine&&s.direction==netDirection);
            if(side<0)return false;
            state.district=district;world.BuildDistrict(district);world.SetDistrictView(district,false);
            mode="district";tab="3D";streetBoard=false;selectedStation=null;world.dayNight=true;cityStreet=false;undergroundWalk=false;
            var track=side<world.StationTrains.Count?world.StationTrains[side]:null;
            if(track==null)return false;
            foreach(var k in track.consists)k.suppressed=true; // the platform is held for this train
            world.ReleaseCorridorStart(c);
            world.AlignCorridor(c,track,Leg);
            // Open the doors on the side this platform's own trains open (in world space: the car may face either way).
            // A network car stands at the platform centre, where its four doors meet a car's screen doors.
            var own=track.consists.Find(k=>k!=null&&k.cabin!=null&&k.cabin.doorSide!=0);
            if(own!=null)
            {
                var car=netConsist!=null?netConsist.cabin:netDoors.cabin;
                int open=Vector3.Dot(car.transform.right,own.cabin.transform.right*own.cabin.doorSide)>0?1:-1;
                if(open!=car.doorSide){if(netConsist!=null)netConsist.UseDoorSide(open);else netDoors.UseSide(open);}
            }
            netTrack=track;
            if(netConsist!=null){netConsist.train=track;netForeign=true;}
            PlaceNet(0);
            Physics.SyncTransforms();CarryInCabin();PlaceViewCamera();
            return true;
        }
        // KTX/무궁화호 into Seoul Station: the corridor ends where a platform's own KTX starts braking in along its mapped
        // track; this train follows that track to the platform while the platform's own set is held out of sight.
        bool LoadNetTerminal(RailCorridor c)
        {
            if(netLine.kind!="ktx"&&netLine.kind!="mugunghwa")return false;
            int district=WorldBuilder.DistrictOf(netNext.name);if(district!=SeoulStationDistrict)return false;
            state.district=district;world.BuildDistrict(district);world.SetDistrictView(district,false);
            mode="district";tab="3D";streetBoard=false;selectedStation=null;world.dayNight=true;cityStreet=false;undergroundWalk=false;
            var rail=world.PlatformTrains.Find(r=>r!=null&&r.doors!=null);
            Vector3 at,heading;
            if(rail==null||!rail.ApproachPose(rail.reach,out at,out heading)||heading.sqrMagnitude<.01f)return false;
            rail.gameObject.SetActive(false);
            netPlatformRail=rail;netReach=rail.reach;
            world.ReleaseCorridorStart(c);
            world.AlignCorridorTo(c,at,Quaternion.LookRotation(heading),1f,Leg,0f);
            netModel=Quaternion.Inverse(Quaternion.LookRotation(heading))*netVehicle.rotation;
            if(netDoors!=null&&netDoors.cabin!=null)netDoors.UseSide(0); // like Seoul's own sets: either side
            PlaceNet(0);
            Physics.SyncTransforms();CarryInCabin();PlaceViewCamera();
            return true;
        }
        void PlaceNet(float doors)
        {
            netDoorsOpen=doors;
            bool inCorridor=netCorridor!=null&&netVehicle.parent==netCorridor.transform;
            if(netConsist!=null)
            {
                if(inCorridor)netConsist.Place(netAlong,doors,netCorridor.sign);
                else netConsist.Place(netConsist.offset,doors,netConsist.train!=null?netConsist.train.direction:1f);
                return;
            }
            Vector3 at,heading;
            if(inCorridor&&netPlatformRail!=null&&netAlong>Leg&&netPlatformRail.ApproachPose(netReach-(netAlong-Leg),out at,out heading))
                netVehicle.SetPositionAndRotation(at,Quaternion.LookRotation(heading)*netModel);
            else if(inCorridor)netVehicle.localPosition=netCorridor.Point(netAlong);
            if(netDoors!=null)netDoors.Set(doors);
            if(netTrack!=null)netTrack.heldDoors=doors; // a network car at a district platform: its screen doors follow
        }
        void ShowHidden()
        {
            if(netTrack!=null&&netPhase!="arrived"){foreach(var k in netTrack.consists)if(k!=netConsist)k.suppressed=false;netTrack.heldDoors=0;netTrack=null;}
            if(netHidden==null)return;
            netHidden.enabled=true;if(netHidden.train!=null)netHidden.train.gameObject.SetActive(true);netHidden=null;
        }
        // Stepped back off before the doors closed: the train goes back to its own timetable.
        void CancelNetRide()
        {
            if(netOrigin!=null)netOrigin.enabled=true;
            if(netConsist!=null){netConsist.manual=false;if(netConsist.train!=null)foreach(var other in netConsist.train.consists)other.suppressed=false;}
            if(netRail!=null)netRail.enabled=true;
            ResetNetRide();
        }
        // The ride is over: the train that came along the corridor leaves for good.
        void EndNetRide()
        {
            if(netTrack!=null){netTrack.heldDoors=0;if(netForeign)netTrack.consists.Remove(netConsist);}
            if(netPlatformRail!=null){netPlatformRail.clock=RailVehicle.Shown+.1f;netPlatformRail.gameObject.SetActive(true);} // back after this one has gone
            ShowHidden();
            if(!netForeign&&netConsist!=null&&netConsist.train!=null&&netConsist.transform.parent==netConsist.train.transform)
            {netConsist.manual=false;foreach(var other in netConsist.train.consists)other.suppressed=false;}
            else if(netVehicle!=null&&!RiderAboard&&(netForeign||netCorridor!=null&&netVehicle.parent==netCorridor.transform))DestroyImmediate(netVehicle.gameObject);
            if(netCorridor!=null)DestroyImmediate(netCorridor.gameObject);
            ResetNetRide();
        }
        void ResetNetRide()
        {
            netCorridor=null;netPhase="";netTrack=null;netForeign=false;netPlatformRail=null;netVehicle=null;netDoors=null;netConsist=null;netRail=null;netOrigin=null;netHidden=null;netTicketed=false;netLoaded=false;
        }
        bool MayLeaveNetRide(){return netPhase=="dwell"||netPhase=="arrived"?true:Refuse("열차가 달리고 있습니다 · 정차한 뒤 열린 문으로 내리세요",false);}
    }
}
