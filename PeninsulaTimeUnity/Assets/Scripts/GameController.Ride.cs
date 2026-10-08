using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Walking into buses and trains through their open doors, being carried inside, and walking out at a stop.
    // Subway: board at a platform, ride through the tunnel calling at up to three stations on the way (outside the
    // game area: the doors open but nobody may leave) and get off at the next district's platform.
    // Bus: walk through an open door, ride, and walk out when the doors open at a stop.
    public partial class GameController
    {
        const float HopAlong=281f,RideStopSeconds=9f;
        const int MaxStopsShown=3;
        Cabin cabin;Vector3 cabinFeet;float cabinYaw,refusedAt=-10f;
        TrafficVehicle ridingBus;string announcedStop="";
        // The subway ride in progress: the train, where it is and what it is doing.
        TrainConsist metroConsist;SubwayTrain metroFrom,metroTrack;StationLine metroLine;int metroTo;double metroBoarded;
        readonly List<string> metroStops=new List<string>();int metroShown;bool metroFinal;
        string metroPhase="";float metroAlong,metroSpeed,metroTimer,metroHold,metroDoors;
        bool holdFade; // a ride is fading to black; DrawFade must not fade back in yet

        // Puts the head where the feet stand in the cabin and turns the view with the vehicle.
        bool CarryInCabin()
        {
            if(cabin==null||!cabin.isActiveAndEnabled)return false;
            var t=cabin.transform;
            eye.position=t.TransformPoint(cabinFeet)+Vector3.up*EyeHeight;
            SetLook(t.eulerAngles.y+cabinYaw,Pitch);
            return true;
        }
        void MoveInCabin(Vector3 step)
        {
            var local=cabin.transform.InverseTransformDirection(step);local.y=0;
            var next=cabinFeet+local;
            // Out through a door on the door side.
            if(Mathf.Abs(next.x)>cabin.halfWidth-.29f&&Mathf.Abs(next.x)>Mathf.Abs(cabinFeet.x)&&cabin.SideOpen(next.x)&&cabin.DoorAt(next.z))
            {
                if(!cabin.Open)Refuse("문이 닫혀 있습니다.",false);
                else if(MayLeave()){ExitCabin(next.x>0?1:-1);return;}
            }
            cabinFeet=cabin.Clamp(next,cabinFeet);
        }
        // Walking into an open door from outside (platform, kerb). Returns true when the step was used (boarded or refused).
        bool TryEnterCabin(Vector3 step)
        {
            if(step.sqrMagnitude<1e-8f)return false;
            var feet=Feet;var target=feet+step;
            // Train/PSD transforms move in Update. Keep the crossing ray in the
            // same frame as Cabin.Open, even when no FixedUpdate has run yet.
            Physics.SyncTransforms();
            foreach(var c in Cabin.All)
            {
                if(c==null||!c.Open||!c.isActiveAndEnabled)continue;
                var t=c.transform;Vector3 local;
                if(!CabinEntryPoint(c,t.InverseTransformPoint(feet),t.InverseTransformPoint(target),out local))continue;
                // Never board through a wall, fence or another vehicle, even if its door is close.
                bool blocked=false;
                var entry=t.TransformPoint(new Vector3(Mathf.Sign(local.x)*(c.halfWidth-.3f),c.floor,local.z));
                var crossing=entry-feet;crossing.y=0;
                var vehicle=c.transform.parent;
                foreach(var hit in Physics.SphereCastAll(feet+Vector3.up*.95f,.22f,crossing.normalized,crossing.magnitude,~0,QueryTriggerInteraction.Ignore))
                {
                    if(vehicle!=null&&hit.collider.transform.IsChildOf(vehicle))continue;
                    if(hit.collider.bounds.max.y>feet.y+.55f){blocked=true;break;}
                }
                if(blocked)continue;
                if(MayBoard(c))EnterCabin(c,local);
                return true;
            }
            return CrossesUnboardedPlatformEdge(feet,target);
        }
        // Intersect the swept footstep with the outer boarding threshold. Testing
        // only its endpoint missed diagonal door crossings on slow frames.
        static bool CabinEntryPoint(Cabin c,Vector3 before,Vector3 after,out Vector3 entry)
        {
            entry=after;float sign=before.x>=0?1f:-1f;
            float reach=c.kind=="ktx"?1.6f:.65f,threshold=c.halfWidth+reach;
            float from=before.x*sign,to=after.x*sign;
            if(!c.SideOpen(sign)||from<=c.halfWidth-.3f||to>=from||to>threshold)return false;
            float fraction=from>threshold?(from-threshold)/(from-to):0f;
            entry=Vector3.Lerp(before,after,Mathf.Clamp01(fraction));
            if(Mathf.Abs(entry.y-c.floor)>.8f||entry.z<c.back||entry.z>c.front)return false;
            return c.DoorAt(entry.z);
        }
        // A passenger can get between a wide PSD opening and the narrower car
        // doorway without boarding. Preserve the platform lip as a boundary when
        // the train leaves; this also covers slow frames that overlap its collider.
        bool CrossesUnboardedPlatformEdge(Vector3 from,Vector3 to)
        {
            if(world==null)return false;
            foreach(var train in world.StationTrains)
            {
                if(train==null||train.consists.Count==0||train.consists[0].cabin==null)continue;
                var c=train.consists[0].cabin;var frame=train.transform;
                var a=frame.InverseTransformPoint(from);var b=frame.InverseTransformPoint(to);
                float half=world.PlatformHalfLength(train);
                if(Mathf.Abs(a.y-c.floor)>.8f||Mathf.Abs(a.z)>half+.05f)continue;
                float sign=c.doorSide;if(sign==0)continue;
                float oldDistance=a.x*sign,newDistance=b.x*sign;
                if(oldDistance<0||newDistance>=oldDistance)continue;
                // Use the actual supporting slab: outside the four-car door
                // zone there is no boarding lip, even on a much longer platform.
                float edge=world.PlatformEdgeDistance(train,Mathf.Clamp(b.z,-half,half))+BodyRadius;
                if(newDistance<edge)return true;
            }
            return false;
        }
        void EnterCabin(Cabin c,Vector3 local)
        {
            var inside=new Vector3(Mathf.Sign(local.x)*(c.halfWidth-.4f),c.floor,local.z);
            cabin=c;cabinFeet=c.Clamp(inside,inside);
            cabinYaw=Yaw-c.transform.eulerAngles.y;verticalSpeed=0;grounded=true;jumpQueued=false;
            switch(c.kind)
            {
                case "metro":StartMetroRide(c.GetComponentInParent<TrainConsist>());break;
                case "bus":BoardBus(c.GetComponentInParent<TrafficVehicle>());break;
                case "ktx":BoardRail(c);break;
                case "networkrail":BoardNetworkRail(c);break;
            }
            CarryInCabin();
        }
        void ExitCabin(int side)
        {
            if(TryLeaveIntermediateMetro())return;
            var c=cabin;
            var p=c.transform.TransformPoint(new Vector3(side*(c.halfWidth+c.exitDistance),c.floor,cabinFeet.z));
            RaycastHit ground;if(GroundBelow(p,out ground))p.y=ground.point.y;
            cabin=null;eye.position=p+Vector3.up*EyeHeight;verticalSpeed=0;grounded=true;
            switch(c.kind)
            {
                case "bus":LeaveBus();break;
                case "ktx":LeftRail();break;
                case "networkrail":if(stationJourney!=null)stationJourney.occupied=false;break;
            }
        }
        bool MayBoard(Cabin c)
        {
            switch(c.kind)
            {
                case "metro":
                {
                    var consist=c.GetComponentInParent<TrainConsist>();
                    if(consist==null||consist.train==null)return false;
                    if(consist==metroConsist&&metroPhase=="leave")return Refuse("이 열차는 곧 출발합니다 · 다음 열차를 이용하세요",false);
                    if(consist.manual&&consist!=metroConsist)return false;
                    var line=consist.train.line;
                    if(line.ahead.Length==0&&line.net==null)return Refuse("이 노선의 정차역 자료가 없습니다");
                    return true;
                }
                case "bus":
                    return true;
                case "ktx":return MayBoardRail(c);
                case "networkrail":return MayBoardNetworkRail(c);
            }
            return true;
        }
        bool MayLeave()
        {
            if(cabin.kind=="networkrail")return stationJourney!=null&&stationJourney.Stopped;
            if(cabin.kind=="ktx")return MayLeaveRail();
            return true;
        }
        // A refusal (message and buzzer) at most every 2.5 s while the player keeps pushing.
        bool Refuse(string message,bool buzz=true)
        {
            if(Time.time-refusedAt>2.5f){Toast(message);if(buzz)Beep.Play("deny");refusedAt=Time.time;}
            return false;
        }

        // Everything a ride needs every frame, whether or not the player is walking.
        void UpdateRides()
        {
            holdFade=false;
            float dt=Time.deltaTime;
            UpdateMetroRide(dt);
            UpdateBusRide();
            UpdateRailRide(dt);
            UpdateNetworkJourney();
        }
        // Leaving the district or the 3D view ends any ride.
        void ClearRides()
        {
            if(metroConsist!=null){metroConsist.manual=false;if(metroTrack!=null)foreach(var other in metroTrack.consists)other.suppressed=false;}
            metroConsist=null;metroPhase="";
            if(ridingBus!=null)ridingBus.CarryingPlayer=false;
            ridingBus=null;cabin=null;ClearRail();stationJourney=null;
        }

        // ---------- bus ----------
        void BoardBus(TrafficVehicle bus)
        {
            ridingBus=bus;if(bus!=null)bus.CarryingPlayer=true;
            Beep.Play("tap");announcedStop=bus!=null&&bus.Dwelling?bus.StopName:"";
            Toast("버스에 탑승했습니다"+(bus!=null&&bus.Route.Length>0?" · "+bus.Route+"번":"")+" · 정류장에서 문이 열리면 걸어 나가 내립니다");
        }
        void LeaveBus()
        {
            if(ridingBus!=null)ridingBus.CarryingPlayer=false;
            ridingBus=null;Beep.Play("tap");Toast("버스에서 내렸습니다");
        }
        void UpdateBusRide()
        {
            if(ridingBus==null)return;
            if(cabin==null||ridingBus.cabin!=cabin){ridingBus.CarryingPlayer=false;ridingBus=null;return;}
            string stop=ridingBus.Dwelling?(ridingBus.AtTerminus?"종점":ridingBus.StopName):"";
            if(stop==announcedStop)return;
            announcedStop=stop;if(stop.Length==0)return;
            Sfx.Play("bell",.5f);
            Toast(ridingBus.AtTerminus?"이 버스는 더 가지 않습니다 · 열린 문으로 내리세요":"이번 정류장은 "+stop+"입니다 · 열린 문으로 걸어 나가면 내립니다");
        }

        // ---------- subway ----------
        void StartMetroRide(TrainConsist c)
        {
            if(c==null||c==metroConsist)return;
            var train=c.train;
            if(train.line.ahead.Length==0){StartNetworkMetroRide(c);return;}
            metroConsist=c;metroFrom=metroTrack=train;metroLine=train.line;metroTo=train.line.ahead[0];metroFinal=false;
            metroBoarded=TransitSchedule.Now-Mathf.Max(0,c.clock-SubwayTrain.Approach);
            // The doors close when the timetable says, but never sooner than a few seconds after stepping in.
            metroHold=Mathf.Max(5f,SubwayTrain.Approach+SubwayTrain.Dwell-2.2f-c.clock);
            c.manual=true;metroAlong=0;metroDoors=1;metroPhase="board";metroTimer=0;
            foreach(var other in train.consists)if(other!=c)other.suppressed=true; // the next train waits until this one has gone
            PlanMetroStops();
            Toast(metroLine.line+" "+metroLine.toward+" 열차에 탔습니다 · "+WorldBuilder.StationTitle(metroTo)+"에서 내립니다"+(metroLine.ahead.Length>1?" (출발 전 왼쪽 패널에서 바꿀 수 있습니다)":""));
        }
        void PlanMetroStops(){metroStops.Clear();metroStops.AddRange(WorldBuilder.StopsBetween(metroLine,metroTo));metroShown=0;}
        void EndMetroRide()
        {
            if(metroConsist!=null){metroConsist.manual=false;if(metroTrack!=null)foreach(var other in metroTrack.consists)other.suppressed=false;}
            metroConsist=null;metroPhase="";
        }
        void UpdateMetroRide(float dt)
        {
            var c=metroConsist;if(c==null)return;
            if(c.cabin==null||metroTrack==null){metroConsist=null;metroPhase="";return;} // the world was rebuilt
            bool aboard=cabin!=null&&cabin==c.cabin;
            metroTimer+=dt;
            switch(metroPhase)
            {
                case "board":case "stop":case "arrived":
                    metroDoors=Mathf.MoveTowards(metroDoors,metroTimer>.6f||metroPhase=="board"?1:0,dt/1.2f);
                    if(metroPhase=="arrived"){if(!aboard&&metroTimer>1f){metroPhase="leave";metroTimer=0;}break;}
                    if(metroPhase=="board"&&!aboard){EndMetroRide();return;} // stepped back onto the platform
                    if(metroTimer>=metroHold){metroPhase="close";metroTimer=0;Sfx.PlayAt("door-chime",eye.position,.8f);}
                    break;
                case "close":
                    metroDoors=Mathf.MoveTowards(metroDoors,0,dt/1.6f);
                    if(metroTimer>2.2f){metroPhase="depart";metroTimer=0;metroSpeed=0;}
                    break;
                case "depart":
                    metroSpeed=Mathf.Min(SubwayTrain.CruiseSpeed,metroSpeed+SubwayTrain.Brake*dt);metroAlong+=metroSpeed*dt;
                    if(metroTrack==metroFrom&&metroAlong>120f)foreach(var other in metroFrom.consists)if(other!=c)other.suppressed=false;
                    if(metroAlong>HopAlong)NextMetroLeg();
                    break;
                case "approach":
                    float v=Mathf.Min(SubwayTrain.CruiseSpeed,Mathf.Sqrt(2*SubwayTrain.Brake*Mathf.Max(0,-metroAlong)));
                    metroAlong=Mathf.Min(0,metroAlong+Mathf.Max(.5f,v)*dt);
                    if(metroAlong>=0){metroPhase=metroFinal?"arrived":"stop";metroTimer=0;metroHold=RideStopSeconds;Sfx.PlayAt("psd",eye.position,.5f);}
                    break;
                case "leave":
                    if(metroTimer<4f){metroDoors=Mathf.MoveTowards(metroDoors,1,dt);break;}
                    metroDoors=Mathf.MoveTowards(metroDoors,0,dt/1.6f);
                    if(metroTimer>6.2f){metroSpeed=Mathf.Min(SubwayTrain.CruiseSpeed,metroSpeed+SubwayTrain.Brake*dt);metroAlong+=metroSpeed*dt;}
                    if(metroAlong>SubwayTrain.Run+60f){EndMetroRide();return;}
                    break;
                case "skip":
                    holdFade=true;fade=Mathf.MoveTowards(fade,1,dt/(metroStops.Count>metroShown?FadeSeconds:.5f));
                    metroAlong+=metroSpeed*dt;
                    if(fade>=1){ArriveByMetro();return;}
                    break;
            }
            c.Place(metroAlong,metroDoors,metroAlong<0?metroTrack.direction:metroTrack.TravelSign);
        }
        // In the tunnel past the platform: on to the next station on the way, or to the destination.
        void NextMetroLeg()
        {
            var c=metroConsist;
            if(metroShown<metroStops.Count&&metroShown<MaxStopsShown)
            {
                string name=metroStops[metroShown++];
                var track=world.RideTrack(metroFrom,metroLine,name);
                if(!track.consists.Contains(c))track.consists.Add(c);
                c.transform.SetParent(track.transform,false);metroTrack=track;
                metroAlong=-HopAlong;metroPhase="approach";metroTimer=0;
                Sfx.Play("arrival",.3f);Toast("이번 역은 "+WorldBuilder.Bare(name)+"역입니다 · 문이 열리면 내려서 다른 노선으로 환승할 수 있습니다");
                return;
            }
            int skipped=metroStops.Count-metroShown;
            if(skipped>0)Toast(skipped+"개 역을 지나 "+WorldBuilder.StationTitle(metroTo)+"으로 갑니다");
            metroPhase="skip";metroTimer=0;
        }
        // Rebuilds the destination district and rolls the rider in on that platform's train, at the timetable time.
        void ArriveByMetro()
        {
            var line=metroLine;int to=metroTo;var feet=cabinFeet;float yaw=cabinYaw;float sign=metroTrack.TravelSign;
            float run=WorldBuilder.RideSeconds(line,to);
            if(run>0){double behind=metroBoarded+run-SubwayTrain.Approach-TransitSchedule.Now;if(behind>0)TransitSchedule.Skip+=behind;}
            metroConsist=null;cabin=null;
            EnterDistrict(to,false);world.SetDistrictView(to,false);
            fade=1;
            int side=world.ArrivalSide(line);
            var train=side>=0?world.StationTrains[side]:null;
            if(train==null||train.consists.Count==0)
            {
                Vector3 facing;Teleport(world.PlatformSpawn(Mathf.Max(0,side),out facing),Vector3.back);
                Toast(WorldBuilder.StationTitle(to)+"입니다 · 계단과 자동 개찰구를 지나 출구로 나가세요");return;
            }
            var c=train.consists[0];foreach(var k in train.consists)if(k.clock>=SubwayTrain.Shown){c=k;break;}
            foreach(var k in train.consists)if(k!=c)k.suppressed=true;
            c.manual=true;c.gameObject.SetActive(true);
            metroConsist=c;metroFrom=metroTrack=train;metroLine=line;metroTo=to;metroFinal=true;
            metroAlong=-HopAlong;metroDoors=0;metroPhase="approach";metroTimer=0;metroSpeed=SubwayTrain.CruiseSpeed;
            // Facing the same way relative to the direction of travel as before the fade.
            if(Mathf.Sign(train.direction)!=Mathf.Sign(sign)){feet=new Vector3(-feet.x,feet.y,-feet.z);yaw+=180f;}
            cabin=c.cabin;cabinFeet=c.cabin.Clamp(feet,feet);cabinYaw=yaw;
            c.Place(metroAlong,0,train.direction);CarryInCabin();
            Toast("이번 역은 "+WorldBuilder.StationTitle(to)+"입니다 · 문이 열리면 내려서 계단으로 올라가세요");
        }
        // Before the doors close: the districts further along this train's line, to pick where to get off.
        void DrawRideChoices()
        {
            if(metroConsist==null||metroFinal)return;
            Label(metroLine.line+" "+metroLine.toward+" · 내릴 역: "+WorldBuilder.StationTitle(metroTo),bodyStyle);
            if(metroStops.Count>0)Label("지나는 역: "+string.Join(" · ",metroStops.ConvertAll(WorldBuilder.Bare).ToArray()),smallStyle);
            if(metroPhase!="board"||metroLine.ahead.Length<2)return;
            foreach(int d in metroLine.ahead)if(Button(WorldBuilder.StationTitle(d)+"에서 내리기",d==metroTo)){metroTo=d;PlanMetroStops();}
        }
    }
}
