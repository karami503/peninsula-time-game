using UnityEngine;

namespace PeninsulaTime
{
    // The walker in the 3D views. `eye` is the player's head: it walks on floors with collisions, gravity and
    // jumps, or stands inside a bus or train (a Cabin) and is carried along. The mouse turns the view without a
    // button while the cursor is locked (Tab or Esc frees it for the side panel). T switches to third person,
    // where the camera follows behind a figure of the player. The camera is placed after all game logic.
    public partial class GameController
    {
        const float WalkSpeed=4.2f,RunSpeed=7.5f,JumpSpeed=4.8f,Gravity=15f,LookSensitivity=2.2f,KeyTurnSpeed=110f,FlySpeed=9f;
        const float ThirdPersonDistance=3.4f,ThirdPersonLift=.3f,StepLength=.75f;
        Transform eye;
        float verticalSpeed;bool grounded=true,jumpQueued;
        bool thirdPerson,lookLocked=true,wasWalking,clickConsumed,menuHadPointer;
        float stepTravel,walkPace;
        GameObject avatar,avatarGun;Transform[] avatarLegs,avatarArms;Quaternion[] avatarLegRest,avatarArmRest;float avatarPhase;
        int playerShirt,playerTrousers;bool playerGunHeld;

        float Yaw{get{return eye.eulerAngles.y;}}
        float Pitch{get{return Mathf.DeltaAngle(0,eye.eulerAngles.x);}}
        Vector3 Feet{get{return eye.position-Vector3.up*EyeHeight;}}
        void SetLook(float yaw,float pitch){eye.rotation=Quaternion.Euler(Mathf.Clamp(pitch,-80f,80f),yaw,0);}
        void CreatePlayer(){eye=new GameObject("Player").transform;world.viewer=eye;eye.position=viewCamera.transform.position;}

        // On foot in a street view, a station, a terminal, a shop, or standing in a bus or train.
        bool OnFoot(){return (StreetView()||InOpenWorld)&&!InVehicle()&&flight==null;}
        bool LookLocked{get{return Cursor.lockState==CursorLockMode.Locked;}}
        bool PointerMenuOpen(){return streetMenu||(InOpenWorld&&(cwMenuOpen||cwMapOpen||ChangwonSession.UiCapture));}
        bool CharacterPreviewOpen(){return streetMenu||(InOpenWorld&&cwMenuOpen);}
        // The ray through the crosshair while the cursor is locked, otherwise through the mouse.
        Ray AimRay(){return LookLocked?viewCamera.ViewportPointToRay(new Vector3(.5f,.5f,0)):viewCamera.ScreenPointToRay(Input.mousePosition);}

        // Locks the cursor while walking (on desktop); Tab or Esc frees it, a click on the 3D view locks it again.
        void UpdateCursor()
        {
            if(PointerMenuOpen())
            {
                lookLocked=false;menuHadPointer=true;wasWalking=false;clickConsumed=Input.GetMouseButtonDown(0);
                Cursor.lockState=CursorLockMode.None;Cursor.visible=true;return;
            }
            bool walking=OnFoot()&&GUIUtility.keyboardControl==0;
            if(menuHadPointer){lookLocked=walking;menuHadPointer=false;}
            if(walking&&!wasWalking)lookLocked=true;
            wasWalking=walking;clickConsumed=false;
            if(walking&&Input.GetKeyDown(KeyCode.Tab))lookLocked=!lookLocked;
            if(walking&&!LookLocked&&Input.GetMouseButtonDown(0)&&PointerOverWorld()&&!Application.isMobilePlatform){lookLocked=true;clickConsumed=true;}
            bool lockIt=walking&&lookLocked&&!Application.isMobilePlatform&&!Application.isBatchMode&&Application.isFocused;
            var wanted=lockIt?CursorLockMode.Locked:CursorLockMode.None;
            if(Cursor.lockState!=wanted){Cursor.lockState=wanted;Cursor.visible=!lockIt;}
        }
        // Esc while the cursor is locked only frees the cursor.
        bool ReleaseCursorOnEscape()
        {
            if(!LookLocked)return false;
            lookLocked=false;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;return true;
        }

        void UpdateLook(float dt)
        {
            if(PointerMenuOpen())return;
            float yaw=Yaw,pitch=Pitch;
            yaw+=((Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.LeftArrow)?1:0)+touchYaw)*KeyTurnSpeed*dt;
            pitch+=((Input.GetKey(KeyCode.DownArrow)?1:0)-(Input.GetKey(KeyCode.UpArrow)?1:0))*KeyTurnSpeed*.6f*dt;
            if(LookLocked||(Input.GetMouseButton(1)&&PointerOverWorld()))
            {yaw+=Input.GetAxis("Mouse X")*LookSensitivity;pitch-=Input.GetAxis("Mouse Y")*LookSensitivity;}
            // Touch screens: drag one finger across the 3D view (above the movement buttons) to look around.
            if(Application.isMobilePlatform&&Input.touchCount==1)
            {
                var touch=Input.GetTouch(0);float scale=Mathf.Max(.55f,Mathf.Min(Screen.width/1440f,Screen.height/860f));
                if(touch.phase==TouchPhase.Moved&&touch.position.x/scale>WorldLeft()&&touch.position.x/scale<WorldRight()&&(Screen.height-touch.position.y)/scale<Screen.height/scale-220f)
                {yaw+=touch.deltaPosition.x*.18f;pitch-=touch.deltaPosition.y*.18f;}
            }
            SetLook(yaw,pitch);
        }

        // First-person walking for districts; fly=true also allows changing height for city street views.
        void WalkCamera(float limit,bool fly)
        {
            if(PointerMenuOpen()){walkPace=0;return;}
            float dt=Time.deltaTime;
            if(cabin!=null&&!CarryInCabin())cabin=null;
            UpdateLook(dt);
            if(Input.GetKeyDown(KeyCode.U)&&OnFoot()&&GUIUtility.keyboardControl==0)TogglePlayerGun();
            if(cabin!=null)cabinYaw=Yaw-cabin.transform.eulerAngles.y;
            float move=(Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0)+touchMove;
            float strafe=(Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0)+touchStrafe;
            var direction=Vector3.ClampMagnitude(Quaternion.Euler(0,Yaw,0)*new Vector3(strafe,0,move),1f);
            if(fly)
            {
                var p=eye.position+direction*FlySpeed*dt;
                p.x=Mathf.Clamp(p.x,-limit,limit);p.z=Mathf.Clamp(p.z,-limit,limit);
                float rise=(Input.GetKey(KeyCode.E)?1:0)-(Input.GetKey(KeyCode.Q)?1:0);
                if(PointerOverWorld())rise+=Input.GetAxis("Mouse ScrollWheel")*25f;
                p.y=Mathf.Clamp(p.y+rise*8*dt,1.7f,45f);
                eye.position=p;return;
            }
            if(Input.GetKeyDown(KeyCode.Space))jumpQueued=true;
            bool run=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
            var step=direction*(run?RunSpeed:WalkSpeed)*dt;
            // Streets stay inside the mapped tile; stations, shops and the terminal lie outside it.
            var next=eye.position+step;
            if(mode=="district"&&state.district==3)limit=Mathf.Max(limit,650f);
            if(mode=="district"&&eye.position.y>-2f&&Mathf.Abs(eye.position.x)<400f&&(Mathf.Abs(next.x)>limit||Mathf.Abs(next.z)>limit))step=Vector3.zero;
            var before=Feet;
            if(cabin!=null)MoveInCabin(step);else if(!TryEnterCabin(step)){WalkThroughFareGate(step);MoveWalker(step);}
            var moved=Feet-before;moved.y=0;
            walkPace=dt>0?moved.magnitude/dt:0;
            Footsteps(moved.magnitude,run);
        }

        // Walks with collisions: blocked by walls, gates and vehicles; follows floors, stairs, ramps and
        // platforms; jumps (Space) and falls under gravity.
        float nextAutoTap;
        void WalkThroughFareGate(Vector3 step)
        {
            if(world==null||eye.position.y>-2||step.sqrMagnitude<.000001f||Time.time<nextAutoTap)return;
            foreach(var gate in world.FareGates)
            {
                if(gate==null||gate.blocker==null||!gate.blocker.enabled)continue;
                var delta=-gate.transform.InverseTransformPoint(Feet);
                var localStep=gate.transform.InverseTransformDirection(step);
                if(Mathf.Abs(delta.y)>.5f||Mathf.Abs(delta.x)>.35f||Mathf.Abs(delta.z)>1.15f||delta.z*localStep.z<=0)continue;
                TapBarrier(gate);nextAutoTap=Time.time+1f;break;
            }
        }
        void MoveWalker(Vector3 step){MoveWalkerStep(step,Time.deltaTime);}
        void MoveWalkerStep(Vector3 step,float dt)
        {
            var feet=Feet;var start=feet;
            if(grounded&&!jumpQueued&&cabin==null)step+=MovingWalkway.VelocityAt(feet)*dt;
            bool hill=step.sqrMagnitude>1e-8f&&HillStep(feet+step.normalized*(BodyRadius+.05f),feet.y);
            if(step.sqrMagnitude>1e-8f)
            {
                var origin=feet+Vector3.up*(StepUp+BodyRadius+.05f);RaycastHit hit;
                if(Physics.SphereCast(origin,BodyRadius,step.normalized,out hit,step.magnitude+.05f,~0,QueryTriggerInteraction.Ignore)&&!Climbable(hit.collider)&&!hill)
                {
                    var slide=Vector3.ProjectOnPlane(step,hit.normal);slide.y=0;
                    if(slide.sqrMagnitude<1e-8f||Physics.SphereCast(origin,BodyRadius,slide.normalized,out hit,slide.magnitude+.05f,~0,QueryTriggerInteraction.Ignore))slide=Vector3.zero;
                    step=slide;
                }
                feet+=step;
            }
            RaycastHit ground;
            bool found=GroundBelow(feet,out ground);
            if(!found){feet=start;found=GroundBelow(feet,out ground);}
            if(!found){eye.position=start+Vector3.up*EyeHeight;verticalSpeed=0;grounded=true;jumpQueued=false;return;}
            float floor=ground.point.y;
            if(grounded)
            {
                float rise=floor-start.y;
                if(rise>StepUp&&!((Climbable(ground.collider)||hill)&&rise<=PlatformClimb)){feet=start;floor=start.y;} // a wall or a high step
                else if(rise<-.45f){grounded=false;verticalSpeed=0;} // walked off a ledge
                else feet.y=floor;
            }
            if(jumpQueued&&grounded){grounded=false;verticalSpeed=JumpSpeed;Sfx.Play("jump",.5f);}
            jumpQueued=false;
            if(!grounded)
            {
                verticalSpeed-=Gravity*dt;feet.y+=verticalSpeed*dt;
                RaycastHit head;
                if(verticalSpeed>0&&Physics.SphereCast(feet+Vector3.up*(EyeHeight-.25f),.2f,Vector3.up,out head,.3f+verticalSpeed*dt,~0,QueryTriggerInteraction.Ignore))verticalSpeed=0;
                if(feet.y<=floor){if(verticalSpeed<-3f)Sfx.Play("land",.6f);feet.y=floor;verticalSpeed=0;grounded=true;}
            }
            eye.position=feet+Vector3.up*EyeHeight;
        }
        static bool GroundBelow(Vector3 feet,out RaycastHit ground)
        {
            return Physics.Raycast(feet+Vector3.up*(PlatformClimb+.1f),Vector3.down,out ground,PlatformClimb+60f,~0,QueryTriggerInteraction.Ignore);
        }
        void Footsteps(float distance,bool run)
        {
            if(!grounded||distance<=0){if(distance<=0)stepTravel=Mathf.Min(stepTravel,StepLength*.6f);return;}
            stepTravel+=distance;
            if(stepTravel<(run?StepLength*1.35f:StepLength))return;
            stepTravel=0;Sfx.Play(cabin!=null||eye.position.y<-2f?"step-hard":"step",run?.55f:.4f);
        }

        // Puts the camera at the head (first person) or behind and above it (third person), clear of walls.
        void PlaceViewCamera()
        {
            bool walking=OnFoot();
            bool preview=walking&&CharacterPreviewOpen();
            UpdateAvatar(walking&&(thirdPerson||preview));
            if(!walking)return;
            var look=eye.rotation;
            if(!thirdPerson&&!preview){viewCamera.transform.SetPositionAndRotation(eye.position,look);return;}
            var pivot=eye.position+Vector3.up*ThirdPersonLift;
            if(preview)
            {
                var front=look*Vector3.forward;float previewDistance=4.4f;RaycastHit previewHit;
                if(Physics.SphereCast(pivot,.22f,front,out previewHit,previewDistance,~0,QueryTriggerInteraction.Ignore))previewDistance=Mathf.Max(1.7f,previewHit.distance-.08f);
                var previewPosition=pivot+front*previewDistance+Vector3.up*.15f;
                viewCamera.transform.SetPositionAndRotation(previewPosition,Quaternion.LookRotation((eye.position+Vector3.up*.05f)-previewPosition,Vector3.up));return;
            }
            var back=look*Vector3.back;float distance=ThirdPersonDistance;RaycastHit hit;
            if(Physics.SphereCast(pivot,.22f,back,out hit,distance,~0,QueryTriggerInteraction.Ignore))distance=Mathf.Max(.35f,hit.distance-.08f);
            viewCamera.transform.SetPositionAndRotation(pivot+back*distance,look);
        }
        // The player's own figure, seen in third person; legs and arms swing with the walking pace.
        void UpdateAvatar(bool show)
        {
            if(!show){if(avatar!=null&&avatar.activeSelf)avatar.SetActive(false);return;}
            if(avatar==null)
            {
                Transform[] legs,arms;
                avatar=world.CreatePlayerCharacter(Feet,out legs,out arms);
                if(avatar==null)return;
                avatar.name="Player block figure";
                foreach(var c in avatar.GetComponentsInChildren<Collider>())DestroyImmediate(c);
                avatarLegs=legs;avatarArms=arms;avatarLegRest=new Quaternion[legs.Length];avatarArmRest=new Quaternion[arms.Length];
                for(int i=0;i<legs.Length;i++)avatarLegRest[i]=legs[i].localRotation;
                for(int i=0;i<arms.Length;i++)avatarArmRest[i]=arms[i].localRotation;
                ApplyPlayerAppearance();CreatePlayerGun();
            }
            if(!avatar.activeSelf)avatar.SetActive(true);
            avatar.transform.SetPositionAndRotation(Feet,Quaternion.Euler(0,Yaw,0));
            avatarPhase+=walkPace*Time.deltaTime*3.6f;
            float angle=walkPace>.2f&&grounded?Mathf.Sin(avatarPhase)*Mathf.Min(38f,walkPace*7f):0;
            for(int i=0;i<avatarLegs.Length;i++)avatarLegs[i].localRotation=Quaternion.AngleAxis(i==0?angle:-angle,Vector3.right)*avatarLegRest[i];
            for(int i=0;i<avatarArms.Length;i++)avatarArms[i].localRotation=playerGunHeld&&i==1?Quaternion.Euler(-72f,0,0)*avatarArmRest[i]:Quaternion.AngleAxis(i==0?-angle*.8f:angle*.8f,Vector3.right)*avatarArmRest[i];
        }
        void ApplyPlayerAppearance(){if(avatar!=null&&world!=null)world.TintPerson(avatar,playerShirt,playerTrousers);}
        void CyclePlayerShirt(int d){playerShirt=(playerShirt+d+WorldBuilder.ShirtCount)%WorldBuilder.ShirtCount;ApplyPlayerAppearance();}
        void CyclePlayerTrousers(int d){playerTrousers=(playerTrousers+d+WorldBuilder.TrouserCount)%WorldBuilder.TrouserCount;ApplyPlayerAppearance();}
        void TogglePlayerGun(){playerGunHeld=!playerGunHeld;if(avatarGun!=null)avatarGun.SetActive(playerGunHeld);Toast(playerGunHeld?"블록형 장비를 들었습니다.":"장비를 내렸습니다.");}
        void CreatePlayerGun()
        {
            if(avatar==null||avatarArms==null||avatarArms.Length<2||avatarGun!=null)return;
            avatarGun=new GameObject("Player block gun");avatarGun.transform.SetParent(avatarArms[1],false);avatarGun.transform.localPosition=new Vector3(0,-.37f,.24f);avatarGun.transform.localRotation=Quaternion.Euler(90,0,0);
            AddGunBlock("Body",new Vector3(0,0,.17f),new Vector3(.13f,.15f,.48f),JinhaeDesign.Slate);
            AddGunBlock("Barrel",new Vector3(0,0,.48f),new Vector3(.07f,.07f,.32f),JinhaeDesign.Aluminium);
            AddGunBlock("Grip",new Vector3(0,-.14f,.08f),new Vector3(.1f,.27f,.13f),JinhaeDesign.Timber);
            avatarGun.SetActive(playerGunHeld);
        }
        void AddGunBlock(string name,Vector3 at,Vector3 scale,Color color)
        {
            var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.SetParent(avatarGun.transform,false);o.transform.localPosition=at;o.transform.localScale=scale;
            var c=o.GetComponent<Collider>();if(c!=null)DestroyImmediate(c);var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");var m=new Material(shader);m.name="Jinhae player equipment";m.color=color;o.GetComponent<Renderer>().sharedMaterial=m;
        }

        // Crosshair at the centre of the 3D view while the cursor is locked, and the key help line.
        void DrawCrosshair(float w,float h)
        {
            if(!OnFoot())return;
            if(LookLocked)
            {
                float cx=(WorldLeft()+w)*.5f,cy=h*.5f;bool target=hoverHint.Length>0;
                var tex=target&&!hoverFar?goldTexture:Texture2D.whiteTexture;
                UiTexture(new Rect(cx-7,cy-1,5,2),tex);UiTexture(new Rect(cx+2,cy-1,5,2),tex);
                UiTexture(new Rect(cx-1,cy-7,2,5),tex);UiTexture(new Rect(cx-1,cy+2,2,5),tex);
            }
            if(Application.isMobilePlatform)return;
            UiLabel(new Rect(w*.5f-350f,h-68,700,22),LookLocked
                ?"마우스: 둘러보기 · 클릭/F: 상호작용 · WASD 이동 · Shift 달리기 · Space 점프 · T "+(thirdPerson?"1인칭":"3인칭")+" · Tab 커서"
                :"화면을 클릭하면 마우스로 둘러봅니다 · Tab 커서 잠금",smallStyle);
        }
    }
}
