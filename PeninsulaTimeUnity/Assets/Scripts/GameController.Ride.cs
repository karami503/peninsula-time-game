using UnityEngine;

namespace PeninsulaTime
{
    // Walking into buses and trains through their open doors, being carried inside, and walking out at a stop.
    // Subway: board at a platform and ride the same train (GameController.NetRide), stepping off at any station.
    // Bus: walk through an open door, ride, and walk out when the doors open at a stop.
    public partial class GameController
    {
        Cabin cabin;Vector3 cabinFeet;float cabinYaw,refusedAt=-10f;
        TrafficVehicle ridingBus;string announcedStop="";
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
                case "metro":StartNetRideFromConsist(c.GetComponentInParent<TrainConsist>());break;
                case "bus":BoardBus(c.GetComponentInParent<TrafficVehicle>());break;
                case "ktx":BoardRail(c);break;
                case "networkrail":BoardNetworkRail(c);break;
            }
            CarryInCabin();
        }
        void ExitCabin(int side)
        {
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
                    if(consist==netConsist&&netPhase=="leave")return Refuse("이 열차는 곧 출발합니다 · 다음 열차를 이용하세요",false);
                    if(consist.manual&&consist!=netConsist)return false;
                    if(consist.train.line.net==null)return Refuse("이 노선의 정차역 자료가 없습니다");
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
            if(NetRideActive&&cabin==NetCabin)return MayLeaveNetRide();
            // A national-network coach transferred into the carved Jinhae world no longer has its
            // StationJourney controller. Its open door is already aligned with the real platform,
            // so leaving must remain possible after the network ride has been torn down.
            if(cabin.kind=="networkrail")return mode=="carved"||(stationJourney!=null&&stationJourney.Stopped);
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
            UpdateBusRide();
            UpdateRailRide(dt);
            UpdateNetRide(dt);
            if(!NetRideActive)UpdateNetworkJourney();
        }
        // Leaving the district or the 3D view ends any ride.
        void ClearRides()
        {
            if(NetRideActive)EndNetRide();
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
            Sfx.Play("bell",.5f);Announcer.Say("door-chime",ridingBus.AtTerminus?Announcer.BusTerminus():Announcer.BusNext(stop,null));
            Toast(ridingBus.AtTerminus?"이 버스는 더 가지 않습니다 · 열린 문으로 내리세요":"이번 정류장은 "+stop+"입니다 · 열린 문으로 걸어 나가면 내립니다");
        }
    }
}
