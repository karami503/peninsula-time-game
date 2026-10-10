using UnityEngine;

namespace PeninsulaTime
{
    // Clicking things in the 3D street views: doors, street lights, people, vehicles, stations and airport fixtures;
    // walking on floors, stairs and bridges with collisions.
    public partial class GameController
    {
        const float InteractRange=60f,ClickRadius=.5f,Reach=3.2f;
        const float EyeHeight=1.65f,StepUp=.5f,PlatformClimb=1.25f,BodyRadius=.3f,FallSpeed=9f;
        string hoverHint="";Vector3 hoverAnchor;bool hoverFar,hoverInformational;
        GUIStyle hintStyle;
        TrafficVehicle ridingCar;
        bool undergroundWalk;

        bool StreetView(){return cityStreet||mode=="interior"||mode=="carved"||mode=="rail"||(mode=="district"&&!world.aerialDistrict);}
        bool InVehicle(){return ridingCar!=null;}
        int CurrentHappiness()
        {
            if(mode=="spectate"&&spectating!=null)return spectating.happiness;
            return Economy(mode=="district"?"seoul":state.selectedCity).happiness;
        }
        // Things used by hand (gates, counters, doors to other areas) must be within reach; vehicles and people may be further.
        static bool NeedsReach(Interactable target){return !target.Informational;}
        void UpdateInteraction()
        {
            hoverHint="";hoverInformational=false;
            if(TransitRideActive()){UpdateTransitRide();return;}
            if(InVehicle()){FollowVehicle();return;}
            if(!StreetView()||(!LookLocked&&!PointerOverWorld()))return;
            RaycastHit hit;
            // A thick ray so walking people and moving cars are easy to click.
            if(!Physics.SphereCast(AimRay(),ClickRadius,out hit,InteractRange,~0,QueryTriggerInteraction.Ignore))return;
            var target=hit.collider.GetComponentInParent<Interactable>();
            if(target==null||target.Hint.Length==0)return;
            hoverHint=target.Hint;hoverInformational=target.Informational;hoverAnchor=hit.point+Vector3.up*.45f;
            hoverFar=NeedsReach(target)&&Vector3.Distance(eye.position,hit.point)>Reach;
            if(!(Input.GetKeyDown(KeyCode.F)||Input.GetMouseButtonDown(0)&&!clickConsumed))return;
            if(hoverFar){Toast("조금 더 가까이 가세요.");return;}
            if(target.Informational){Toast(target.Hint);return;}
            if(target is DoorInteract)
            {
                var door=(DoorInteract)target;
                if(door.IsOpen&&door.hasInterior){mode="interior";cityStreet=false;Teleport(door.entry,Vector3.forward);Toast("건물 안으로 들어왔습니다 · 문을 눌러 나갈 수 있습니다");}
                else Toast(door.Toggle());
            }
            else if(target is InteriorExit)
            {
                mode="city";cityStreet=true;Teleport(((InteriorExit)target).street,Vector3.back);Toast("거리로 나왔습니다.");
            }
            else if(target is AirportExit){var exit=(AirportExit)target;ArriveCity(exit.city,exit.airport+"에서 나왔습니다.");}
            else if(target is Pedestrian)Toast(((Pedestrian)target).Talk(eye.position,CurrentHappiness(),PlaceName(),DayCycle.Night));
            else if(target is Barrier)TapBarrier((Barrier)target);
            else if(target is Fixture)UseFixture((Fixture)target);
            else if(target is StationPortal)
            {
                var portal=(StationPortal)target;
                if(portal.walkThrough){Toast("계단과 통로를 따라 걸어서 이동하세요");return;}
                Teleport(portal.destination,portal.facing);
                undergroundWalk=portal.destination.y<-1f;
                Toast(undergroundWalk?"지하철 대합실입니다 · 자동 개찰구를 지나 승강장으로 이동하세요":portal.arrival.Length>0?portal.arrival:portal.label);
            }
            else if(target is VehicleInteract)BoardVehicle(target.GetComponent<TrafficVehicle>(),((VehicleInteract)target).bus);
        }
        // First-person walking: blocked by walls, gates and vehicles; follows floors, stairs, ramps and bridges.

        // Rail and bus platforms are stepped onto as if by their stairs.
        static bool Climbable(Collider c){return c!=null&&c.gameObject.name.ToLowerInvariant()=="platform";}
        // In the carved 진해: is `p` on open ground (grass or pavement) a hillside step above `floor` the walker can climb
        // (up to PlatformClimb)? Steps over StepUp are where its terrain is steeper than 1 in 1.
        bool HillStep(Vector3 p,float floor)
        {
            float h;byte kind;var land=mode=="carved"&&world!=null?world.Carved:null;
            if(land==null||!land.Sample(p,out h,out kind)||kind!=CarvedDistrict.Grass&&kind!=CarvedDistrict.Earth)return false;
            float rise=h-floor;return rise>StepUp-.2f&&rise<=PlatformClimb;
        }
        void BoardVehicle(TrafficVehicle vehicle,bool bus)
        {
            if(vehicle==null)return;
            if(bus)return;
            ridingCar=vehicle;
            ridingCar.TakeWheel();
            Toast("자동차 운전 · W/S 가속·후진, A/D 방향, Esc 내리기");
        }
        // Chase camera behind and above the vehicle, looking down the road.
        void FollowVehicle()
        {
            float throttle=(Input.GetKey(KeyCode.W)?1f:0f)-(Input.GetKey(KeyCode.S)?1f:0f)+touchMove;
            float steer=(Input.GetKey(KeyCode.D)?1f:0f)-(Input.GetKey(KeyCode.A)?1f:0f)+touchStrafe;
            ridingCar.ManualDrive(Mathf.Clamp(throttle,-1f,1f),Mathf.Clamp(steer,-1f,1f),Time.deltaTime,mode=="city"?62f:315f);
            var heading=ridingCar.Heading.sqrMagnitude>0?ridingCar.Heading:Vector3.forward;
            var p=ridingCar.transform.position;float length=ridingCar.BodyLength;
            var goal=p-heading*length*1.2f+Vector3.up*(length*.4f+1.2f);
            RaycastHit obstruction;
            if(Physics.SphereCast(p+Vector3.up*1.2f,.25f,(goal-p-Vector3.up*1.2f).normalized,out obstruction,Vector3.Distance(goal,p+Vector3.up*1.2f),~0,QueryTriggerInteraction.Ignore)
                &&!obstruction.collider.transform.IsChildOf(ridingCar.transform))goal=obstruction.point+(p+Vector3.up*1.2f-goal).normalized*.35f;
            var t=viewCamera.transform;
            t.position=Vector3.Lerp(t.position,goal,1f-Mathf.Exp(-6f*Time.deltaTime));
            t.rotation=Quaternion.Slerp(t.rotation,Quaternion.LookRotation(p+heading*10f+Vector3.up-t.position),1f-Mathf.Exp(-6f*Time.deltaTime));
            if(Input.GetKeyDown(KeyCode.F))LeaveVehicle();
        }
        void LeaveVehicle()
        {
            if(ridingCar!=null)
            {
                var p=ridingCar.transform.position;var heading=ridingCar.Heading;heading.y=0;
                var side=Vector3.Cross(Vector3.up,heading.sqrMagnitude>0?heading.normalized:Vector3.forward);
                eye.position=world.SafeStreetSpawn(p+side*3f,heading)+Vector3.up*EyeHeight;
                if(heading.sqrMagnitude>0)eye.rotation=Quaternion.LookRotation(heading);
                Toast("내렸습니다.");
            }
            ridingCar=null;PlaceViewCamera();
        }
        // A small label pinned above the thing under the cursor, with a marker; dimmed when out of reach.
        void DrawHoverHint()
        {
            if(hoverHint.Length==0||viewCamera==null)return;
            var screen=viewCamera.WorldToScreenPoint(hoverAnchor);if(screen.z<=0)return;
            if(hintStyle==null)
            {
                hintStyle=new GUIStyle(GUI.skin.label){fontSize=15,alignment=TextAnchor.MiddleCenter,richText=true,wordWrap=false,clipping=TextClipping.Overflow,padding=new RectOffset(14,14,7,7),font=koreanFont};
                hintStyle.normal.background=Solid(new Color(.04f,.06f,.07f,.82f));hintStyle.normal.textColor=Color.white;
            }
            float scale=Mathf.Max(.55f,Mathf.Min(Screen.width/1440f,Screen.height/860f));
            float x=screen.x/scale,y=(Screen.height-screen.y)/scale;
            bool info=hoverInformational||hoverHint.StartsWith("다음 열차");
            var content=new GUIContent((info?"":hoverFar?"<color=#9aa7a6>가까이 가서 </color>":"<color=#e8b85a>클릭 </color> ")+hoverHint);
            var size=hintStyle.CalcSize(content);
            // Keep the label on screen; the stem still points at the target.
            float right=Mathf.Min(Screen.width/scale,WorldRight())-8;
            var box=new Rect(Mathf.Clamp(x-size.x*.5f,WorldLeft()+8,Mathf.Max(WorldLeft()+8,right-size.x)),Mathf.Max(90,y-size.y-16),size.x,size.y);
            UiLabel(box,content,hintStyle);
            UiTexture(new Rect(x-1,box.yMax,2,10),hoverFar?lineTexture:goldTexture);
            UiTexture(new Rect(x-4,y-5,8,8),hoverFar?lineTexture:goldTexture);
        }
    }
}
