using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace PeninsulaTime
{
    // Progress in the Changwon open world, kept in its own file next to save-v1.json (never inside GameState).
    [Serializable] public class ChangwonProgress
    {
        public float x=float.NaN,z=float.NaN,yaw;public int score;public float distance;
        public List<string> stamps=new List<string>();public List<string> done=new List<string>();
        public float clock=100f;public int missionsDone;public float bestRace;
    }

    // Shared state for the open-world systems (traffic, people, buses, missions) so they need not reach into GameController.
    public static class ChangwonSession
    {
        public static bool Active;public static GameController Game;public static WorldBuilder Builder;public static Transform Root;
        public static Vector3 PlayerFeet;public static Vector3 PlayerForward=Vector3.forward;public static ChangwonCar PlayerCar;public static bool OnFoot;
        public static ChangwonProgress Progress=new ChangwonProgress();
        public static Action<string> ToastHandler;
        public static void Toast(string message){if(ToastHandler!=null)ToastHandler(message);}
        public static Vector2? Waypoint;public static string WaypointName="";
        public static string Objective=""; // shown on the HUD by the mission system
        public static readonly List<KeyValuePair<Vector3,Color>> Blips=new List<KeyValuePair<Vector3,Color>>(); // extra radar/map markers, rebuilt each frame by systems
        // While set, the player is a passenger (bus, train, ferry): GameController skips walking/driving and calls UpdateRide.
        public static IChangwonRide Ride;
        // A system's dialog is open: the cursor is free and the player does not move.
        // A card or board owns the mouse; UiJustReleased tells Esc handling that a card closed itself this frame.
        static bool uiCapture;static int uiReleased=-1;
        public static bool UiCapture{get{return uiCapture;}set{if(uiCapture&&!value)uiReleased=Time.frameCount;uiCapture=value;}}
        public static bool UiJustReleased{get{return uiReleased==Time.frameCount;}}
        public static readonly List<string> HudLines=new List<string>(); // extra HUD lines under the objective, rebuilt each frame
        public static event Action<float,float> Overlay; // custom IMGUI drawing in GUI units (w,h), called from GameController.OnGUI
        public static void DrawOverlay(float w,float h){if(Overlay!=null)Overlay(w,h);}
        public static Action<Vector3,Vector3> TeleportPlayer; // feet position, facing
        public static Action<ChangwonCar> EnterCar;public static Action LeaveCar;
        public static Action<Vector2?,string> SetWaypoint; // null clears it; otherwise the GPS route is planned to it
        public static GUIStyle Title,Heading,Body,Small,Button,Accent,Box;public static Texture2D Ink,Soft,Gold;
        public static void Reset(){Ride=null;UiCapture=false;HudLines.Clear();Blips.Clear();Overlay=null;Objective="";Waypoint=null;}
    }
    public interface IChangwonRide
    {
        Vector3 Eye{get;}           // where the player's eye is (streaming and the radar follow it)
        string Hud{get;}            // one line for the HUD, e.g. "105번 · 다음 정류장 창원시청"
        void UpdateRide(Camera camera,float dt); // place the camera; read input (F to get off is up to the ride)
    }

    public partial class GameController
    {
        ChangwonWorld cw;ChangwonCar cwCar;bool cwReady,cwMapOpen,cwMenuOpen,cwFirstPerson;string cwStage="";
        Texture2D cwMap,cwDot,cwRadarMask;Vector2 cwMapCenter;float cwMapZoom=1f;Vector3 cwCamPos;float cwCamYaw,cwCamPitch=12f;float cwCamYawUser;
        string cwArea="",cwAreaShown="";float cwAreaUntil,cwNextArea;List<Vector3> cwRoute;volatile bool cwRouting;float cwNextRoute;
        bool cwRequestedSpawn;Vector2 cwRequestedGeo;
        GUIStyle cwBig,cwHud,cwHudSmall,cwSpeed;Vector3 cwLastFeet;Light cwHeadlight;bool cwTown;Vector3 cwDry,cwDryForward=Vector3.forward;bool cwHasDry;float cwWaterToast;
        static string ChangwonSavePath{get{return Path.Combine(SaveDirectory,"changwon-openworld.json");}}

        bool InOpenWorld{get{return mode=="openworld";}}
        static readonly bool cwDebug=Array.IndexOf(Environment.GetCommandLineArgs(),"--changwon-debug")>=0; // QA: log feet, ground and blocker

        void EnterOpenWorld()
        {
            if(state.era<9)return;
            ClearRides();networkPassengerJourney=null;ridingCar=null;flight=null;cityStreet=false;undergroundWalk=false;riding=false;
            spectating=null;spectateSession++;selectedStation=null;streetBoard=false;if(onlineMode=="battle")LeaveBattle();
            mode="openworld";tab="서울 3D";cwReady=false;cwMapOpen=cwMenuOpen=false;cwCar=null;cwRoute=null;
            ChangwonSession.Reset();ChangwonSession.Active=true;ChangwonSession.Game=this;ChangwonSession.Builder=world;ChangwonSession.ToastHandler=Toast;ChangwonSession.PlayerCar=null;
            ChangwonSession.TeleportPlayer=(feet,facing)=>{if(cwCar!=null)LeaveChangwonCar();Teleport(feet+Vector3.up*EyeHeight,facing);};
            ChangwonSession.EnterCar=EnterChangwonCar;ChangwonSession.LeaveCar=LeaveChangwonCar;thirdPerson=true;
            ChangwonSession.SetWaypoint=(at,name)=>{if(at.HasValue)SetChangwonWaypoint(at.Value,name);else{ChangwonSession.Waypoint=null;ChangwonSession.WaypointName="";cwRoute=null;}};
            LoadChangwonProgress();
            cw=world.BuildChangwonWorld(eye);ChangwonSession.Root=world.root.transform;world.root.AddComponent<ChangwonLandmarkModels>(); // before the data loads, so it flattens the replaced footprints first
            StartCoroutine(OpenWorldStart());
        }
        void EnterOpenWorldAt(float lon,float lat){cwRequestedSpawn=true;cwRequestedGeo=new Vector2(lon,lat);EnterOpenWorld();}
        IEnumerator OpenWorldStart()
        {
            cwStage="창원 지형·도로·건물 자료를 불러오는 중…";
            yield return ChangwonData.Load();
            if(!InOpenWorld)yield break;
            if(!ChangwonData.Loaded){Toast(ChangwonData.Error??"창원 자료를 불러오지 못했습니다.");ExitOpenWorld();yield break;}
            if(cwMap==null)cwMap=LoadChangwonMap();
            var p=ChangwonSession.Progress;Vector3 spawn;Vector3 facing;
            if(!float.IsNaN(p.x)&&ChangwonData.Inside(p.x,p.z)){spawn=new Vector3(p.x,ChangwonData.Height(p.x,p.z),p.z);facing=Quaternion.Euler(0,p.yaw,0)*Vector3.forward;}
            else ChangwonSpawn(out spawn,out facing);
            if(cwRequestedSpawn)
            {
                var requestedXZ=ChangwonData.ToXZ(cwRequestedGeo.x,cwRequestedGeo.y);var probe=new Vector3(requestedXZ.x,ChangwonData.Height(requestedXZ.x,requestedXZ.y),requestedXZ.y);
                ChangwonData.Road requestedRoad;float requestedAlong;Vector3 requestedPoint;
                if(ChangwonData.NearestRoad(probe,250f,out requestedRoad,out requestedAlong,out requestedPoint))
                {
                    Vector3 f;requestedRoad.At(requestedAlong,out f);f.y=0;f.Normalize();var right=new Vector3(f.z,0,-f.x);
                    spawn=requestedPoint+right*(requestedRoad.width*.5f+2.5f);facing=-right;
                }
                else{spawn=probe;facing=Vector3.forward;}
                cwRequestedSpawn=false;
            }
            // QA: --changwon-at x,z,yaw stands the player at a world position (metres east/north of 128.62°E 35.20°N).
            var argv=Environment.GetCommandLineArgs();int at=Array.IndexOf(argv,"--changwon-at");
            if(at>=0&&at+1<argv.Length){var v=argv[at+1].Split(',');float ax,az,ay=0;
                if(v.Length>=2&&float.TryParse(v[0],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out ax)&&float.TryParse(v[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out az)&&ChangwonData.Inside(ax,az)){
                    if(v.Length>2)float.TryParse(v[2],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out ay);
                    spawn=new Vector3(ax,ChangwonData.Height(ax,az),az);facing=Quaternion.Euler(0,ay,0)*Vector3.forward;}}
            DayCycle.Seconds=Mathf.Repeat(p.clock,DayCycle.Length);
            // Hold the player in the air above the spawn while the chunk and its colliders are generated.
            eye.position=spawn+Vector3.up*(EyeHeight+1f);SetLook(Quaternion.LookRotation(facing).eulerAngles.y,0);
            cwStage="창원 거리를 만드는 중…";
            float started=Time.realtimeSinceStartup;
            while(InOpenWorld&&!cw.Ready&&Time.realtimeSinceStartup-started<90f)yield return null;
            if(!InOpenWorld)yield break;
            yield return null;Physics.SyncTransforms();
            RaycastHit hit;var top=spawn+Vector3.up*60f;
            if(Physics.Raycast(top,Vector3.down,out hit,140f,~0,QueryTriggerInteraction.Ignore))spawn=hit.point;
            Teleport(spawn+Vector3.up*EyeHeight,facing);cwLastFeet=spawn;
            cwCamPos=eye.position-facing*6+Vector3.up*3;
            foreach(var sys in new Type[]{typeof(ChangwonTraffic),typeof(ChangwonPeople),typeof(ChangwonBuses),typeof(ChangwonMissions),typeof(ChangwonParked),typeof(ChangwonWeather),typeof(ChangwonSpeedCameras),typeof(ChangwonHarbor),typeof(ChangwonFerry)})if(world.root.GetComponent(sys)==null)world.root.AddComponent(sys);
            ChangwonParkedCars(spawn,facing);
            var cli=Environment.GetCommandLineArgs();int ts=Array.IndexOf(cli,"--changwon-tour-start");int t0;if(ts>=0&&ts+1<cli.Length&&int.TryParse(cli[ts+1],out t0))cwTour=t0-1;
            cwReady=true;cwStage="";fade=1;
            if(playtest&&Array.IndexOf(cli,"--changwon-qa-train")>=0)StartCoroutine(ChangwonPlaytestTrain());
            if(playtest&&Array.IndexOf(cli,"--changwon-qa-bus")>=0)StartCoroutine(ChangwonPlaytestBus());
            Toast("창원특례시에 오신 것을 환영합니다 · F: 차 타기/상호작용 · M: 지도 · Esc: 메뉴");
        }
        IEnumerator ChangwonPlaytestTrain()
        {
            ChangwonTrains trains=null;
            for(int i=0;i<3600;i++)
            {
                trains=world.root.GetComponent<ChangwonTrains>();
                if(trains!=null&&trains.StartPlaytestRide()){Debug.Log("Changwon QA train ride started");yield break;}
                if(i==600||i==1800)Debug.Log("Changwon QA train waiting: "+(trains!=null?trains.PlaytestStatus:"component missing"));
                yield return null;
            }
            Debug.LogError("Changwon QA train ride could not start: "+(trains!=null?trains.PlaytestStatus:"component missing"));
        }
        IEnumerator ChangwonPlaytestBus()
        {
            var buses=world.root.GetComponent<ChangwonBuses>();
            for(int i=0;i<1800&&buses!=null;i++)
            {
                if(buses.StartPlaytestRide()){Debug.Log("Changwon QA bus ride started");yield break;}
                yield return null;
            }
            Debug.LogError("Changwon QA bus ride could not start");
        }
        // Default start: in front of 창원시청 by 창원광장, on the pavement of 중앙대로.
        void ChangwonSpawn(out Vector3 spawn,out Vector3 facing)
        {
            var at=ChangwonData.ToXZ(128.68185,35.22765);var probe=new Vector3(at.x,ChangwonData.Height(at.x,at.y),at.y);
            ChangwonData.Road road;float along;Vector3 point;
            if(ChangwonData.NearestRoad(probe,400f,out road,out along,out point)){
                Vector3 f;road.At(along,out f);f.y=0;f.Normalize();var right=new Vector3(f.z,0,-f.x);
                // Start clear of the lamp post planted at the closest point on the kerb.
                spawn=point+f*7f+right*(road.width*.5f+2.5f);facing=-right;
            }else{spawn=probe;facing=Vector3.forward;}
        }
        void ChangwonParkedCars(Vector3 near,Vector3 facing)
        {
            ChangwonData.Road road;float along;Vector3 point;
            if(!ChangwonData.NearestRoad(near,200f,out road,out along,out point))return;
            string[] models={"CarRed","CarWhite","CarBlue"};
            for(int i=0;i<3;i++){
                float s=Mathf.Clamp(along+10f+i*8f,1,road.length-1);Vector3 f;var p=road.At(s,out f);f.y=0;f.Normalize();var right=new Vector3(f.z,0,-f.x);
                var pos=p+right*(road.width*.5f-1.3f);
                var go=world.ChangwonVehicle(models[i],pos,"차 타기 (F)");if(go==null)continue;
                var car=go.AddComponent<ChangwonCar>();car.model=models[i];car.Init(pos,f);go.transform.SetParent(world.root.transform,true);
                car.Move(Vector3.zero,.02f);
            }
        }
        Texture2D LoadChangwonMap()
        {
            var t=Resources.Load<Texture2D>("Changwon/cw_map");if(t!=null)return t;
            string path=Path.Combine(Path.Combine(Application.streamingAssetsPath,"Changwon"),"cw_map.png");
            if(path.Contains("://")||!File.Exists(path))return null;
            var tex=new Texture2D(2,2,TextureFormat.RGB24,false);tex.LoadImage(File.ReadAllBytes(path));tex.wrapMode=TextureWrapMode.Clamp;return tex;
        }
        void ExitOpenWorld(){ReturnMap();}
        // Called by ReturnMap (and so by every way out of the mode) before the scene is torn down.
        void LeaveOpenWorldState()
        {
            Time.timeScale=1f;SaveChangwonProgress();
            ChangwonSession.Active=false;ChangwonSession.PlayerCar=null;ChangwonSession.Reset();
            if(cwCar!=null){cwCar.PlayerDriving=false;cwCar.SetEngine(false);}
            cwCar=null;cwReady=false;cwMapOpen=cwMenuOpen=false;cwRoute=null;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            if(viewCamera!=null){viewCamera.farClipPlane=1000f;viewCamera.nearClipPlane=.3f;}
            Sfx.StopAmbience();if(cwHeadlight!=null){Destroy(cwHeadlight.gameObject);cwHeadlight=null;}
        }

        // ------------------------------------------------------------------ persistence
        void LoadChangwonProgress()
        {
            try{if(File.Exists(ChangwonSavePath)){var p=JsonUtility.FromJson<ChangwonProgress>(File.ReadAllText(ChangwonSavePath));if(p!=null){if(p.stamps==null)p.stamps=new List<string>();if(p.done==null)p.done=new List<string>();ChangwonSession.Progress=p;return;}}}
            catch(Exception e){Debug.LogWarning("Changwon progress load failed: "+e.Message);}
            ChangwonSession.Progress=new ChangwonProgress();
        }
        void SaveChangwonProgress()
        {
            if(!ChangwonSession.Active||!cwReady)return;
            var p=ChangwonSession.Progress;var feet=cwCar!=null?cwCar.transform.position:Feet;p.x=feet.x;p.z=feet.z;p.yaw=Yaw;p.clock=DayCycle.Seconds;
            try{
                Directory.CreateDirectory(SaveDirectory);string tmp=ChangwonSavePath+".tmp";File.WriteAllText(tmp,JsonUtility.ToJson(p,true));
                if(File.Exists(ChangwonSavePath))File.Delete(ChangwonSavePath);File.Move(tmp,ChangwonSavePath);
            }catch(Exception e){Debug.LogWarning("Changwon progress save failed: "+e.Message);}
        }

        // ------------------------------------------------------------------ per frame
        // Returns true when Esc was handled by the open world (menus, leaving a car).
        bool OpenWorldEscape()
        {
            if(!InOpenWorld)return false;
            if(cwMapOpen){cwMapOpen=false;return true;}
            if(cwMenuOpen){cwMenuOpen=false;return true;}
            if(ChangwonSession.UiCapture||ChangwonSession.UiJustReleased)return true; // Esc closes the open card (each system handles it), not opens the menu
            if(LookLocked){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
            cwMenuOpen=true;return true;
        }
        void UpdateOpenWorld(bool typing)
        {
            // The pause menu and the full map stop the world (traffic, rides, mission timers).
            float scale=cwMenuOpen||cwMapOpen?0f:1f;if(Time.timeScale!=scale)Time.timeScale=scale;
            float dt=Time.deltaTime;
            if(world.dayNight)DayCycle.Advance(dt);
            if(!cwReady){hoverHint="";return;}
            bool ui=cwMapOpen||cwMenuOpen||typing||ChangwonSession.UiCapture;
            if(!typing&&Input.GetKeyDown(KeyCode.M)){cwMapOpen=!cwMapOpen;cwMenuOpen=false;if(cwMapOpen)cwMapCenter=new Vector2(Feet.x,Feet.z);}
            if(ui){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;hoverHint="";if(cwCar!=null)cwCar.Drive(0,0,true,dt);ChangwonSessionUpdate();return;}
            if(playtest&&Input.GetKeyDown(KeyCode.F11)&&ChangwonLandmarks.All.Count>0)ChangwonTourNext();
            if(playtest&&Input.GetKeyDown(KeyCode.F10)&&cwCar==null&&ChangwonSession.Ride==null)ChangwonTestCar();
            if(Input.GetKeyDown(KeyCode.P)&&!cwPhoto)StartCoroutine(ChangwonPhoto());
            if(cwDebug&&Time.frameCount%20==0){var f=Feet;RaycastHit dh,fh;string under=Physics.Raycast(f+Vector3.up*1.35f,Vector3.down,out dh,62f,~0,QueryTriggerInteraction.Ignore)?dh.collider.name+"@"+dh.point.y.ToString("0.00"):"none";string ahead=Physics.SphereCast(f+Vector3.up*.85f,.3f,eye.forward,out fh,1.5f,~0,QueryTriggerInteraction.Ignore)?fh.collider.name+"@"+fh.distance.ToString("0.00"):"none";Debug.Log("CWDBG feet "+f.ToString("F2")+" under "+under+" ahead "+ahead+" grounded "+grounded+" land "+ChangwonData.LandAt(f.x,f.z));}
            if(playtest&&Input.GetKeyDown(KeyCode.F12)){DayCycle.Advance(DayCycle.Length/8f);Debug.Log("Changwon clock "+DayCycle.Clock);}
            if(ChangwonSession.Ride!=null)
            {
                Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;hoverHint="";
                ChangwonSession.Ride.UpdateRide(viewCamera,dt);if(ChangwonSession.Ride!=null)eye.position=ChangwonSession.Ride.Eye;
                ChangwonSessionUpdate();return;
            }
            if(cwCar!=null)DriveChangwonCar(dt);
            else
            {
                UpdateCursor();
                if(Input.GetKeyDown(KeyCode.T))thirdPerson=!thirdPerson;
                WalkCamera(0,false);PlaceViewCamera();UpdateInteraction();
                TrafficVehicle.PlayerFeet=Feet;
            }
            ChangwonSessionUpdate();
            CheckChangwonWater();
            // Fell off the world (through a gap while chunks streamed): put the player back on the ground.
            if(cwCar==null&&Feet.y<ChangwonData.Height(Feet.x,Feet.z)-25f){var f=Feet;Teleport(new Vector3(f.x,ChangwonData.Height(f.x,f.z)+EyeHeight+.5f,f.z),eye.forward);}
            if(Time.unscaledTime>=cwNextArea){
                cwNextArea=Time.unscaledTime+1f;UpdateChangwonArea();
                byte land=ChangwonData.LandAt(Feet.x,Feet.z);cwTown=land==ChangwonData.Residential||land==ChangwonData.Commercial||land==ChangwonData.Urban||land==ChangwonData.Industrial;
            }
            Sfx.Ambience(DayCycle.Night?"city-night":"city-day",cwCar!=null?.15f:cwTown?.45f:.18f);
            if(cwRoute!=null&&Time.unscaledTime>=cwNextRoute&&ChangwonSession.Waypoint.HasValue&&!cwRouting){cwNextRoute=Time.unscaledTime+4f;PlanChangwonRoute();}
            if(ChangwonSession.Waypoint.HasValue){var w=ChangwonSession.Waypoint.Value;if((new Vector2(Feet.x,Feet.z)-w).sqrMagnitude<30*30){ChangwonSession.Waypoint=null;cwRoute=null;Toast("목적지에 도착했습니다: "+ChangwonSession.WaypointName);Sfx.Play("arrival",.6f);}}
        }
        // No swimming or boating yet: walking or driving into the sea, a lake or a river (not over a bridge) puts the player
        // back where they were last on dry ground.
        void CheckChangwonWater()
        {
            var p=cwCar!=null?cwCar.transform.position:Feet;byte land=ChangwonData.LandAt(p.x,p.z);
            float ground=ChangwonData.Height(p.x,p.z);bool onDeck=p.y>ground+2.2f||p.y>.8f&&land==ChangwonData.Sea&&p.y>ground+1.5f;
            // Piers, bridges and decks over water: anything solid underfoot that is not the terrain itself.
            RaycastHit under;if(!onDeck&&(land==ChangwonData.Sea||land==ChangwonData.Water)&&Physics.Raycast(p+Vector3.up*.6f,Vector3.down,out under,1.6f,~0,QueryTriggerInteraction.Ignore)&&under.collider.name!="지형")onDeck=true;
            bool wet=(land==ChangwonData.Sea||land==ChangwonData.Water)&&!onDeck&&(p.y<.15f||land==ChangwonData.Water);
            if(!wet){cwDry=p;cwDryForward=cwCar!=null?cwCar.forward:eye.forward;cwHasDry=true;return;}
            if(!cwHasDry)return;
            if(cwCar!=null){cwCar.speed=0;cwCar.transform.position=cwDry;cwCar.forward=cwDryForward;}
            else Teleport(cwDry+Vector3.up*EyeHeight,cwDryForward);
            if(Time.time>cwWaterToast){cwWaterToast=Time.time+3f;Toast(land==ChangwonData.Sea?"바다에는 들어갈 수 없습니다 · 다리나 배를 이용하세요":"물에 들어갈 수 없습니다");}
        }
        void ChangwonSessionUpdate()
        {
            var feet=cwCar!=null?cwCar.transform.position:Feet;
            ChangwonSession.Progress.distance+=Vector3.Distance(new Vector3(feet.x,0,feet.z),new Vector3(cwLastFeet.x,0,cwLastFeet.z))<60f?Vector3.Distance(feet,cwLastFeet):0;cwLastFeet=feet;
            ChangwonSession.PlayerFeet=feet;ChangwonSession.PlayerCar=cwCar;ChangwonSession.OnFoot=cwCar==null;
            var f=cwCar!=null?cwCar.forward:eye.forward;f.y=0;if(f.sqrMagnitude>0)ChangwonSession.PlayerForward=f.normalized;
        }
        void DriveChangwonCar(float dt)
        {
            Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;hoverHint="";
            float throttle=(Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.UpArrow)?1:0)-(Input.GetKey(KeyCode.S)||Input.GetKey(KeyCode.DownArrow)?1:0)+touchMove;
            float steerInput=(Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.LeftArrow)?1:0)+touchStrafe;
            cwCar.Drive(Mathf.Clamp(throttle,-1,1),Mathf.Clamp(steerInput,-1,1),Input.GetKey(KeyCode.Space),dt);
            if(Input.GetKeyDown(KeyCode.H))Sfx.Play("deny",.8f,1.6f);
            if(Input.GetKeyDown(KeyCode.T))cwFirstPerson=!cwFirstPerson;
            // Mouse orbits the chase camera; it drifts back behind the car while driving.
            cwCamYawUser+=Input.GetAxis("Mouse X")*2.2f;cwCamPitch=Mathf.Clamp(cwCamPitch-Input.GetAxis("Mouse Y")*1.6f,-5f,55f);
            if(Mathf.Abs(cwCar.speed)>3f)cwCamYawUser=Mathf.MoveTowards(cwCamYawUser,0,60f*dt);
            var car=cwCar.transform;var fwd=cwCar.forward;
            if(cwFirstPerson){viewCamera.transform.position=car.position+Vector3.up*(cwCar.bus?2.6f:1.25f)+fwd*(cwCar.bus?4.6f:.25f)-new Vector3(fwd.z,0,-fwd.x)*(cwCar.bus?0:.38f);viewCamera.transform.rotation=Quaternion.LookRotation(Quaternion.Euler(0,cwCamYawUser,0)*fwd)*Quaternion.Euler(4,0,0);}
            else{
                float back=cwCar.bus?13f:cwCar.bike?4.5f:7f,up=cwCar.bus?4.2f:2.4f;
                var orbit=Quaternion.Euler(cwCamPitch*.6f,Quaternion.LookRotation(fwd).eulerAngles.y+cwCamYawUser,0);
                var want=car.position+Vector3.up*up-(orbit*Vector3.forward)*back;
                // Keep the camera above the ground and out of walls.
                RaycastHit hit;var pivot=car.position+Vector3.up*1.8f;
                if(Physics.SphereCast(pivot,.3f,(want-pivot).normalized,out hit,Vector3.Distance(pivot,want),~0,QueryTriggerInteraction.Ignore)&&!hit.collider.transform.IsChildOf(car))want=pivot+(want-pivot).normalized*Mathf.Max(1.2f,hit.distance-.2f);
                if(car.position.y>ChangwonData.Height(car.position.x,car.position.z)-2f)want.y=Mathf.Max(want.y,ChangwonData.Height(want.x,want.z)+1f); // not in a tunnel: the hill above is not the floor
                cwCamPos=Vector3.Lerp(cwCamPos,want,1f-Mathf.Exp(-8f*dt));
                viewCamera.transform.position=cwCamPos;viewCamera.transform.rotation=Quaternion.LookRotation(car.position+Vector3.up*1.3f+fwd*2f-cwCamPos);
            }
            if(cwHeadlight!=null){cwHeadlight.transform.position=car.position+Vector3.up*1.0f+fwd*(cwCar.length*.5f);cwHeadlight.transform.rotation=Quaternion.LookRotation(fwd+Vector3.down*.12f);cwHeadlight.intensity=DayCycle.Night?2.4f:0f;}
            // The eye travels with the car so streaming, daylight and the radar follow it.
            eye.position=car.position+Vector3.up*EyeHeight;
            if(Input.GetKeyDown(KeyCode.F)&&Mathf.Abs(cwCar.speed)<4f)LeaveChangwonCar();
        }
        // QA: --playtest --changwon, F11 jumps to the next landmark (logged for screenshots).
        int cwTour=-1;
        void ChangwonTourNext()
        {
            cwTour=(cwTour+1)%ChangwonLandmarks.All.Count;var lm=ChangwonLandmarks.All[cwTour];ChangwonSession.Ride=null;if(cwCar!=null)LeaveChangwonCar();
            var p=new Vector3(lm.pos.x,ChangwonData.Height(lm.pos.x,lm.pos.y),lm.pos.y);
            ChangwonData.Road road;float along;Vector3 at;
            if(ChangwonData.NearestRoad(p,300f,out road,out along,out at)){Vector3 f;road.At(along,out f);var right=new Vector3(f.z,0,-f.x).normalized;p=at+right*(road.width*.5f+1.6f);}
            Teleport(p+Vector3.up*(EyeHeight+1.5f),new Vector3(lm.pos.x-p.x,0,lm.pos.y-p.z).sqrMagnitude>1?new Vector3(lm.pos.x-p.x,0,lm.pos.y-p.z).normalized:Vector3.forward);
            Debug.Log("Changwon tour "+cwTour+" "+lm.name+" at "+p+" · chunks "+cw.ChunksLoaded+" far "+cw.FarTilesLoaded+" · avg chunk build "+(ChangwonWorld.Builds>0?ChangwonWorld.BuildMillis/ChangwonWorld.Builds:0)+" ms over "+ChangwonWorld.Builds+" · fps "+(1f/Mathf.Max(.001f,Time.smoothDeltaTime)).ToString("F1"));
        }
        // QA: a car on the nearest road, already boarded.
        void ChangwonTestCar()
        {
            ChangwonData.Road road;float along;Vector3 at;
            if(!ChangwonData.NearestRoad(Feet,200f,out road,out along,out at))return;
            Vector3 f;road.At(along,out f);f.y=0;f.Normalize();var pos=at+new Vector3(f.z,0,-f.x)*Mathf.Min(road.width*.25f,3f);
            var go=world.ChangwonVehicle("CarRed",pos,"차 타기 (F)");if(go==null)return;go.transform.SetParent(world.root.transform,true);
            var car=go.AddComponent<ChangwonCar>();car.Init(pos,f);car.Move(Vector3.zero,.02f);EnterChangwonCar(car);
            Debug.Log("Changwon test car on "+road.name+" ("+road.cls+") at "+pos);
        }
        void LeaveChangwonCar()
        {
            var car=cwCar;if(car==null)return;
            car.PlayerDriving=false;car.SetEngine(false);car.speed=0;cwCar=null;if(cwHeadlight!=null){Destroy(cwHeadlight.gameObject);cwHeadlight=null;}
            var right=new Vector3(car.forward.z,0,-car.forward.x);
            foreach(var side in new[]{-1f,1f}){
                var spot=car.transform.position+right*side*(car.width*.5f+.9f);
                if(!Physics.CheckCapsule(spot+Vector3.up*.4f,spot+Vector3.up*1.6f,.3f,~0,QueryTriggerInteraction.Ignore)){
                    Vector3 n;float g=ChangwonCar.Ground(spot,spot.y+2.5f,null,out n);Teleport(new Vector3(spot.x,g,spot.z)+Vector3.up*EyeHeight,car.forward);Toast("차에서 내렸습니다.");return;
                }
            }
            Vector3 nn;var p=car.transform.position;Teleport(new Vector3(p.x,ChangwonCar.Ground(p,p.y+4f,car.transform,out nn)+2.4f,p.z)+Vector3.up*EyeHeight,car.forward);
        }
        void EnterChangwonCar(ChangwonCar car)
        {
            if(car==null)return;
            if(car.npc){var traffic=world.root.GetComponent<ChangwonTraffic>();if(traffic!=null)traffic.Release(car);Toast("운전자가 차를 빌려주었습니다. 안전 운전하세요!");}
            else Toast(car.bus?"버스를 직접 운전합니다":"운전 시작 · W/S 가속·후진 · A/D 조향 · Space 핸드브레이크 · H 경적 · F 하차");
            cwCar=car;car.PlayerDriving=true;car.npc=false;car.SetEngine(!car.bike);cwCamPos=viewCamera.transform.position;cwCamYawUser=0;
            if(cwHeadlight!=null)Destroy(cwHeadlight.gameObject);
            cwHeadlight=new GameObject("전조등").AddComponent<Light>();cwHeadlight.type=LightType.Spot;cwHeadlight.range=car.bike?25f:55f;cwHeadlight.spotAngle=70f;cwHeadlight.color=new Color(1f,.95f,.85f);cwHeadlight.shadows=LightShadows.None;cwHeadlight.intensity=0;
            hoverHint="";Sfx.Play("door-chime",.3f,1.4f);
        }
        void UseChangwonThing(ChangwonThing thing)
        {
            if(thing.kind=="car"){EnterChangwonCar(thing.GetComponent<ChangwonCar>()??thing.GetComponentInParent<ChangwonCar>());return;}
            var handler=thing.GetComponentInParent<IChangwonUse>();
            if(handler!=null){handler.Use(thing);return;}
            foreach(var h in world.root.GetComponents<IChangwonUse>())if(h.Handles(thing)){h.Use(thing);return;}
            if(!string.IsNullOrEmpty(thing.detail))Toast(thing.detail);
        }

        // ------------------------------------------------------------------ areas and routing
        void UpdateChangwonArea()
        {
            string name=ChangwonAreas.Name(Feet.x,Feet.z);
            if(name!=cwArea){cwArea=name;if(name.Length>0&&name!=cwAreaShown){cwAreaShown=name;cwAreaUntil=Time.unscaledTime+4f;}}
        }
        void SetChangwonWaypoint(Vector2 at,string name)
        {
            ChangwonSession.Waypoint=at;ChangwonSession.WaypointName=name;cwRoute=new List<Vector3>();cwNextRoute=0;PlanChangwonRoute();
            Toast("목적지 설정: "+name+" · 미니맵의 보라색 경로를 따라가세요");
        }
        void PlanChangwonRoute()
        {
            if(!ChangwonSession.Waypoint.HasValue)return;
            var from=cwCar!=null?cwCar.transform.position:Feet;var to=ChangwonSession.Waypoint.Value;cwRouting=true;
            new Thread(()=>{try{var r=ChangwonRouter.Route(from,new Vector3(to.x,0,to.y));if(r!=null)cwRoute=r;}catch(Exception e){Debug.LogWarning(e);}cwRouting=false;}){IsBackground=true}.Start();
        }

        // ------------------------------------------------------------------ HUD
        void SetupOpenWorldStyles()
        {
            if(cwHud!=null)return;
            cwBig=new GUIStyle(titleStyle){fontSize=34,alignment=TextAnchor.MiddleLeft};cwBig.normal.textColor=new Color(1,.93f,.78f);
            cwHud=new GUIStyle(bodyStyle){fontSize=16,wordWrap=false};cwHud.normal.textColor=Color.white;
            cwHudSmall=new GUIStyle(smallStyle){fontSize=13};cwHudSmall.normal.textColor=new Color(.88f,.9f,.92f);
            cwSpeed=new GUIStyle(titleStyle){fontSize=40,alignment=TextAnchor.MiddleRight};cwSpeed.normal.textColor=Color.white;
            cwDot=Solid(Color.white);
            ChangwonSession.Title=titleStyle;ChangwonSession.Heading=headingStyle;ChangwonSession.Body=bodyStyle;ChangwonSession.Small=smallStyle;ChangwonSession.Button=buttonStyle;ChangwonSession.Accent=accentButtonStyle;ChangwonSession.Box=boxStyle;
            ChangwonSession.Ink=inkTexture;ChangwonSession.Soft=softTexture;ChangwonSession.Gold=goldTexture;
        }
        // Photo mode: hide the HUD for a frame and save a screenshot next to the save file.
        bool cwPhoto;
        IEnumerator ChangwonPhoto()
        {
            cwPhoto=true;yield return null;
            string dir=Path.Combine(SaveDirectory,"changwon-photos");string file=null;
            try{Directory.CreateDirectory(dir);file=Path.Combine(dir,"창원_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".png");ScreenCapture.CaptureScreenshot(file);}catch(Exception e){Debug.LogWarning(e.Message);}
            yield return null;yield return null;cwPhoto=false;Sfx.Play("tap",.8f,.6f);
            if(file!=null)Toast("사진을 저장했습니다 · "+Path.GetFileName(file));
        }
        void DrawOpenWorldGUI(float w,float h)
        {
            SetupOpenWorldStyles();if(cwPhoto&&cwReady)return;
            if(!cwReady){
                GUI.DrawTexture(new Rect(0,0,w,h),inkTexture);
                GUI.Label(new Rect(w*.5f-320,h*.5f-80,640,50),"창원특례시 오픈월드",titleStyle);
                GUI.Label(new Rect(w*.5f-320,h*.5f-20,640,30),cwStage.Length>0?cwStage:ChangwonData.StatusText,bodyStyle);
                GUI.Label(new Rect(w*.5f-320,h*.5f+20,640,60),"의창구 · 성산구 · 마산합포구 · 마산회원구 · 진해구 전역 · 지형 고도(AWS Terrain Tiles), 건물·도로·장소(Overture Maps, OpenStreetMap ODbL), 시내버스(창원 BIS)",smallStyle);
                return;
            }
            DrawChangwonRadar(w,h);
            // Area banner, as when entering a district.
            if(Time.unscaledTime<cwAreaUntil){float a=Mathf.Clamp01((cwAreaUntil-Time.unscaledTime)/1f);var c=GUI.color;GUI.color=new Color(1,1,1,a);GUI.Label(new Rect(w-560,h-210,520,50),cwAreaShown,new GUIStyle(cwBig){alignment=TextAnchor.MiddleRight});GUI.color=c;}
            // Top right: clock, place, progress.
            GUI.DrawTexture(new Rect(w-330,14,316,86),softTexture);
            GUI.Label(new Rect(w-318,20,300,26),DayCycle.Clock+" "+ChangwonWeather.Now+"  ·  "+(cwArea.Length>0?cwArea:"창원특례시"),cwHud);
            var p=ChangwonSession.Progress;
            GUI.Label(new Rect(w-318,46,300,22),"명소 도장 "+p.stamps.Count+"/"+ChangwonLandmarks.All.Count+"  ·  점수 "+p.score.ToString("N0")+"  ·  이동 "+(p.distance/1000f).ToString("0.0")+"km",cwHudSmall);
            GUI.Label(new Rect(w-318,68,300,22),ChangwonSession.Objective.Length>0?ChangwonSession.Objective:"M 지도 · 지도를 눌러 목적지 설정",cwHudSmall);
            float hy=104;
            if(ChangwonSession.Ride!=null){GUI.DrawTexture(new Rect(w-330,hy,316,26),softTexture);GUI.Label(new Rect(w-318,hy+3,300,22),ChangwonSession.Ride.Hud,cwHudSmall);hy+=28;}
            foreach(var line in ChangwonSession.HudLines){GUI.DrawTexture(new Rect(w-330,hy,316,24),softTexture);GUI.Label(new Rect(w-318,hy+2,300,22),line,cwHudSmall);hy+=26;}
            if(cwCar==null&&ChangwonSession.Ride==null&&!cwMapOpen&&!cwMenuOpen){
                bool target=hoverHint.Length>0;var c=GUI.color;GUI.color=target?new Color(1f,.8f,.3f):new Color(1,1,1,.7f);
                GUI.DrawTexture(new Rect(w*.5f-1,h*.5f-7,2,14),cwDot);GUI.DrawTexture(new Rect(w*.5f-7,h*.5f-1,14,2),cwDot);GUI.color=c;
                GUI.Label(new Rect(280,h-30,w-560,24),"WASD 이동 · Shift 달리기 · Space 점프 · F 타기/대화/이용 · T 시점 · M 지도 · P 사진 · Esc 메뉴",cwHudSmall);
            }
            ChangwonSession.DrawOverlay(w,h);
            if(cwCar!=null){
                GUI.DrawTexture(new Rect(w-250,h-118,236,104),softTexture);
                GUI.Label(new Rect(w-246,h-114,200,58),ChangwonCar.Kmh(cwCar.speed).ToString("0"),cwSpeed);
                GUI.Label(new Rect(w-44,h-94,40,30),"km/h",cwHudSmall);
                string road=ChangwonRoadName(cwCar.transform.position);
                GUI.Label(new Rect(w-240,h-52,224,24),road.Length>0?road:"",cwHudSmall);
                GUI.Label(new Rect(w-240,h-32,224,24),"F 하차 · T 시점 · H 경적 · Space 브레이크",cwHudSmall);
            }
            if(Application.isMobilePlatform)DrawOpenWorldTouch(w,h);else touchMove=touchStrafe=touchYaw=0;
            if(cwMapOpen)DrawChangwonMap(w,h);
            if(cwMenuOpen)DrawChangwonMenu(w,h);
        }
        string ChangwonRoadName(Vector3 p)
        {
            ChangwonData.Road road;float along;Vector3 at;
            return ChangwonData.NearestRoad(p,12f,out road,out along,out at)&&road.name.Length>0?road.name:"";
        }
        void DrawOpenWorldTouch(float w,float h)
        {
            float cx=w*.5f;
            touchMove=(GUI.RepeatButton(new Rect(cx-170,h-184,72,72),"↑",buttonStyle)?1:0)-(GUI.RepeatButton(new Rect(cx-170,h-104,72,72),"↓",buttonStyle)?1:0);
            touchStrafe=(GUI.RepeatButton(new Rect(cx-90,h-104,72,72),"→",buttonStyle)?1:0)-(GUI.RepeatButton(new Rect(cx-250,h-104,72,72),"←",buttonStyle)?1:0);
            touchYaw=(GUI.RepeatButton(new Rect(cx+160,h-104,72,72),"회전 →",buttonStyle)?1:0)-(GUI.RepeatButton(new Rect(cx+80,h-104,72,72),"← 회전",buttonStyle)?1:0);
            if(GUI.Button(new Rect(cx+80,h-184,152,72),cwCar!=null?"하차 (F)":"타기·대화 (F)",buttonStyle)){
                if(cwCar!=null){if(Mathf.Abs(cwCar.speed)<4f)LeaveChangwonCar();}
                else{RaycastHit hit;if(Physics.SphereCast(AimRay(),ClickRadius,out hit,6f,~0,QueryTriggerInteraction.Ignore)){var t=hit.collider.GetComponentInParent<ChangwonThing>();if(t!=null)UseChangwonThing(t);}}
            }
            if(GUI.Button(new Rect(w-130,108,116,48),"지도 (M)",buttonStyle)){cwMapOpen=!cwMapOpen;cwMapCenter=new Vector2(Feet.x,Feet.z);}
        }

        Rect MapToScreen(Rect view,Vector2 world,Vector2 center,float metresPerPixel)
        {
            return new Rect(view.center.x+(world.x-center.x)/metresPerPixel,view.center.y-(world.y-center.y)/metresPerPixel,0,0);
        }
        Rect MapUV(Vector2 center,float halfWidth,float halfHeight)
        {
            float sx=ChangwonData.MaxX-ChangwonData.MinX,sz=ChangwonData.MaxZ-ChangwonData.MinZ;
            return new Rect((center.x-halfWidth-ChangwonData.MinX)/sx,(center.y-halfHeight-ChangwonData.MinZ)/sz,halfWidth*2/sx,halfHeight*2/sz);
        }
        // GTA-style radar: the map turns with the camera and the player arrow stays in the middle. The rotated map is drawn
        // with GL as a square whose corner UVs are rotated, so no GUI matrix tricks (which misplace the pivot when scaled).
        Material cwGLMat;
        Material GLMat(Texture texture){if(cwGLMat==null){var sh=Shader.Find("Sprites/Default");if(sh==null)sh=Shader.Find("UI/Default");cwGLMat=new Material(sh);}cwGLMat.mainTexture=texture;return cwGLMat;}
        void DrawChangwonRadar(float w,float h)
        {
            float size=Mathf.Min(250,h*.32f);var rect=new Rect(18,h-size-18,size,size);
            GUI.DrawTexture(new Rect(rect.x-4,rect.y-4,size+8,size+8),inkTexture);
            var feet=cwCar!=null?cwCar.transform.position:ChangwonSession.Ride!=null?ChangwonSession.Ride.Eye:Feet;var center=new Vector2(feet.x,feet.z);
            float metres=cwCar!=null?Mathf.Lerp(380,900,Mathf.Clamp01(Mathf.Abs(cwCar.speed)/40f)):300f;float mpp=metres*2/size;
            float heading=viewCamera.transform.eulerAngles.y*Mathf.Deg2Rad;
            var fwd=new Vector2(Mathf.Sin(heading),Mathf.Cos(heading));var right=new Vector2(Mathf.Cos(heading),-Mathf.Sin(heading));
            Func<Vector2,Vector2> toGui=p=>{var d=p-center;return rect.center+new Vector2(Vector2.Dot(d,right),-Vector2.Dot(d,fwd))/mpp;};
            if(Event.current.type==EventType.Repaint&&cwMap!=null)
            {
                float scale=Screen.height/h;var px=new Rect(rect.x*scale,Screen.height-(rect.y+size)*scale,size*scale,size*scale);
                GL.PushMatrix();GL.LoadPixelMatrix();GLMat(cwMap).SetPass(0);GL.Begin(GL.QUADS);GL.Color(Color.white);
                float sx=ChangwonData.MaxX-ChangwonData.MinX,sz=ChangwonData.MaxZ-ChangwonData.MinZ;
                foreach(var k in new[]{new Vector2(-1,-1),new Vector2(-1,1),new Vector2(1,1),new Vector2(1,-1)})
                {
                    var world2=center+right*(k.x*size*.5f*mpp)+fwd*(k.y*size*.5f*mpp);
                    GL.TexCoord2((world2.x-ChangwonData.MinX)/sx,(world2.y-ChangwonData.MinZ)/sz);GL.Vertex3(px.x+(k.x+1)*.5f*px.width,px.y+(k.y+1)*.5f*px.height,0);
                }
                GL.End();GL.PopMatrix();
                DrawRouteGL(rect,toGui,scale,h);
            }
            foreach(var lm in ChangwonLandmarks.All){var g=toGui(lm.pos);if(rect.Contains(g))Blip(g,ChangwonSession.Progress.stamps.Contains(lm.id)?new Color(.55f,.55f,.55f):new Color(1f,.78f,.25f),7);}
            foreach(var b in ChangwonSession.Blips){var g=toGui(new Vector2(b.Key.x,b.Key.z));if(rect.Contains(g))Blip(g,b.Value,8);}
            if(ChangwonSession.Waypoint.HasValue){var g=toGui(ChangwonSession.Waypoint.Value);var d=g-rect.center;float lim=size*.47f;if(d.magnitude>lim)g=rect.center+d.normalized*lim;Blip(g,new Color(.75f,.35f,1f),11);}
            // Player arrow (always up) and the north marker.
            Blip(rect.center,Color.white,10);Blip(rect.center+new Vector2(0,-6),new Color(.2f,.85f,1f),6);
            var north=toGui(center+new Vector2(0,1)*mpp*size*.44f);GUI.Label(new Rect(north.x-6,north.y-10,20,20),"N",cwHudSmall);
        }
        void Blip(Vector2 at,Color color,float size){var c=GUI.color;GUI.color=new Color(0,0,0,.6f);GUI.DrawTexture(new Rect(at.x-size*.5f-1.5f,at.y-size*.5f-1.5f,size+3,size+3),cwDot);GUI.color=color;GUI.DrawTexture(new Rect(at.x-size*.5f,at.y-size*.5f,size,size),cwDot);GUI.color=c;}
        // The GPS route as thick purple lines, clipped to the given GUI rectangle (call only during Repaint).
        void DrawRouteGL(Rect clip,Func<Vector2,Vector2> toGui,float scale,float h)
        {
            var route=cwRoute;if(route==null||route.Count<2)return;
            GL.PushMatrix();GL.LoadPixelMatrix();GLMat(cwDot).SetPass(0);GL.Begin(GL.QUADS);GL.Color(new Color(.72f,.33f,1f,.95f));
            for(int i=1;i<route.Count;i++){
                Vector2 a=toGui(new Vector2(route[i-1].x,route[i-1].z)),b=toGui(new Vector2(route[i].x,route[i].z));
                if(!ClipSegment(clip,ref a,ref b))continue;
                var d=(b-a);if(d.sqrMagnitude<.01f)continue;var n=new Vector2(-d.y,d.x).normalized*2.5f;
                foreach(var p in new[]{a+n,b+n,b-n,a-n})GL.Vertex3(p.x*scale,Screen.height-p.y*scale,0);
            }
            GL.End();GL.PopMatrix();
        }
        static bool ClipSegment(Rect r,ref Vector2 a,ref Vector2 b)
        {
            float t0=0,t1=1;var d=b-a;
            float[] p={-d.x,d.x,-d.y,d.y};float[] q={a.x-r.xMin,r.xMax-a.x,a.y-r.yMin,r.yMax-a.y};
            for(int i=0;i<4;i++){
                if(Mathf.Abs(p[i])<1e-6f){if(q[i]<0)return false;continue;}
                float t=q[i]/p[i];if(p[i]<0){if(t>t1)return false;if(t>t0)t0=t;}else{if(t<t0)return false;if(t<t1)t1=t;}
            }
            var a2=a+d*t0;b=a+d*t1;a=a2;return true;
        }
        void DrawChangwonMap(float w,float h)
        {
            var view=new Rect(40,40,w-80,h-80);GUI.DrawTexture(new Rect(view.x-6,view.y-6,view.width+12,view.height+12),inkTexture);
            float fullX=ChangwonData.MaxX-ChangwonData.MinX;float mpp=fullX/view.width/cwMapZoom;
            var e=Event.current;
            if(e.type==EventType.ScrollWheel&&view.Contains(e.mousePosition)){float before=mpp;cwMapZoom=Mathf.Clamp(cwMapZoom*(e.delta.y<0?1.25f:.8f),.8f,40f);e.Use();}
            mpp=fullX/view.width/cwMapZoom;
            if(e.type==EventType.MouseDrag&&view.Contains(e.mousePosition)&&e.button!=0||e.type==EventType.MouseDrag&&e.button==0&&e.delta.sqrMagnitude>4){cwMapCenter+=new Vector2(-e.delta.x,e.delta.y)*mpp;e.Use();}
            // Outside the mapped area: plain sea colour (a clamped texture would smear its edge pixels into streaks).
            var gc=GUI.color;GUI.color=new Color(.16f,.28f,.42f);GUI.DrawTexture(view,Texture2D.whiteTexture);GUI.color=gc;
            if(cwMap!=null){var uv=MapUV(cwMapCenter,view.width*.5f*mpp,view.height*.5f*mpp);float x0=Mathf.Max(0,uv.xMin),x1=Mathf.Min(1,uv.xMax),y0=Mathf.Max(0,uv.yMin),y1=Mathf.Min(1,uv.yMax);
                if(x1>x0&&y1>y0)GUI.DrawTextureWithTexCoords(Rect.MinMaxRect(view.x+(x0-uv.xMin)/uv.width*view.width,view.y+(uv.yMax-y1)/uv.height*view.height,view.x+(x1-uv.xMin)/uv.width*view.width,view.y+(uv.yMax-y0)/uv.height*view.height),cwMap,Rect.MinMaxRect(x0,y0,x1,y1));}
            if(e.type==EventType.Repaint){var c0=cwMapCenter;var vc=view.center;DrawRouteGL(view,p=>vc+new Vector2(p.x-c0.x,-(p.y-c0.y))/mpp,Screen.height/h,h);}
            GUI.BeginGroup(view);var local=new Rect(0,0,view.width,view.height);
            ChangwonLandmarks.Landmark hovered=null;
            foreach(var lm in ChangwonLandmarks.All){
                var d=(lm.pos-cwMapCenter)/mpp;var at=local.center+new Vector2(d.x,-d.y);if(!local.Contains(at))continue;
                bool got=ChangwonSession.Progress.stamps.Contains(lm.id);Blip(at,got?new Color(.6f,.6f,.6f):new Color(1f,.78f,.25f),cwMapZoom>2.5f?11:8);
                if(cwMapZoom>2.2f||(e.mousePosition-view.position-at).sqrMagnitude<100)GUI.Label(new Rect(at.x+8,at.y-10,220,22),lm.name,cwHudSmall);
                if((e.mousePosition-view.position-at).sqrMagnitude<100)hovered=lm;
            }
            foreach(var b in ChangwonSession.Blips){var d=(new Vector2(b.Key.x,b.Key.z)-cwMapCenter)/mpp;Blip(local.center+new Vector2(d.x,-d.y),b.Value,10);}
            if(ChangwonSession.Waypoint.HasValue){var d=(ChangwonSession.Waypoint.Value-cwMapCenter)/mpp;Blip(local.center+new Vector2(d.x,-d.y),new Color(.75f,.35f,1f),14);}
            var feet=cwCar!=null?cwCar.transform.position:Feet;var pd=(new Vector2(feet.x,feet.z)-cwMapCenter)/mpp;Blip(local.center+new Vector2(pd.x,-pd.y),Color.white,12);Blip(local.center+new Vector2(pd.x,-pd.y),new Color(.2f,.85f,1f),7);
            GUI.EndGroup();
            GUI.DrawTexture(new Rect(view.x,view.y,view.width,40),softTexture);
            GUI.Label(new Rect(view.x+14,view.y+8,view.width-28,26),"창원특례시 지도 · 휠 확대 · 끌어서 이동 · 클릭 목적지 · 노란 점 명소(도장) · M/Esc 닫기"+(hovered!=null?"   ▶ "+hovered.name+" — "+hovered.description:""),cwHud);
            if(GUI.Button(new Rect(view.xMax-300,view.yMax-56,136,44),"내 위치",buttonStyle)){cwMapCenter=new Vector2(feet.x,feet.z);cwMapZoom=Mathf.Max(cwMapZoom,6f);}
            if(GUI.Button(new Rect(view.xMax-156,view.yMax-56,140,44),"목적지 지우기",buttonStyle)){ChangwonSession.Waypoint=null;cwRoute=null;}
            if(e.type==EventType.MouseUp&&e.button==0&&view.Contains(e.mousePosition)&&!new Rect(view.xMax-300,view.yMax-56,284,44).Contains(e.mousePosition)){
                var at=e.mousePosition-view.center;var world2=cwMapCenter+new Vector2(at.x,-at.y)*mpp;
                if(hovered!=null)SetChangwonWaypoint(hovered.pos,hovered.name);else SetChangwonWaypoint(world2,ChangwonAreas.Name(world2.x,world2.y));
                e.Use();
            }
        }
        void DrawChangwonMenu(float w,float h)
        {
            GUI.DrawTexture(new Rect(0,0,w,h),softTexture);
            var box=new Rect(w*.5f-300,h*.5f-250,600,500);GUI.Box(box,"",boxStyle);
            GUI.Label(new Rect(box.x+30,box.y+24,540,44),"창원 오픈월드 · 일시정지",titleStyle);
            var p=ChangwonSession.Progress;
            GUI.Label(new Rect(box.x+30,box.y+80,540,90),"명소 도장 "+p.stamps.Count+"/"+ChangwonLandmarks.All.Count+"  ·  임무 완료 "+p.missionsDone+"  ·  점수 "+p.score.ToString("N0")+"\n총 이동 거리 "+(p.distance/1000f).ToString("0.0")+" km  ·  "+DayCycle.Clock+"\n"+(cwArea.Length>0?cwArea:"창원특례시"),bodyStyle);
            GUI.Label(new Rect(box.x+30,box.y+170,540,120),"조작: WASD 이동·운전 · Shift 달리기 · Space 점프/브레이크 · F 차 타기·내리기·대화·이용 · T 시점 · M 지도 · H 경적 · Tab 마우스\n차량 근처에서 F를 누르면 운전합니다. 버스 정류장에서 F로 시내버스를 기다려 탈 수 있습니다. 노란 명소에 가면 도장을 받고, 임무 지점(초록)에서 F로 택시·배달·레이스 임무를 시작합니다.",smallStyle);
            if(GUI.Button(new Rect(box.x+30,box.y+300,260,52),"계속하기",accentButtonStyle))cwMenuOpen=false;
            if(GUI.Button(new Rect(box.x+310,box.y+300,260,52),"지도 열기",buttonStyle)){cwMenuOpen=false;cwMapOpen=true;cwMapCenter=new Vector2(Feet.x,Feet.z);}
            if(GUI.Button(new Rect(box.x+30,box.y+364,260,52),"진행 저장",buttonStyle)){SaveChangwonProgress();Save();Toast("창원 오픈월드 진행을 저장했습니다.");}
            if(GUI.Button(new Rect(box.x+310,box.y+364,260,52),"창원시청으로 이동",buttonStyle)){Vector3 s,f;ChangwonSpawn(out s,out f);ChangwonSession.Ride=null;if(cwCar!=null)LeaveChangwonCar();RaycastHit hit;if(Physics.Raycast(s+Vector3.up*60,Vector3.down,out hit,140))s=hit.point;Teleport(s+Vector3.up*EyeHeight,f);cwMenuOpen=false;}
            if(GUI.Button(new Rect(box.x+30,box.y+428,540,52),"오픈월드 나가기 (한반도 지도로)",buttonStyle)){cwMenuOpen=false;ExitOpenWorld();}
        }
    }

    // Systems that answer ChangwonThing uses (shops, bus stops, missions) implement this on a component under world.root.
    public interface IChangwonUse {bool Handles(ChangwonThing thing);void Use(ChangwonThing thing);}

    // Shortest path over the drivable road graph (A*), run on a worker thread.
    public static class ChangwonRouter
    {
        public static List<Vector3> Route(Vector3 from,Vector3 to)
        {
            ChangwonData.Road ra,rb;float aa,ab;Vector3 pa,pb;
            if(!ChangwonData.NearestRoad(new Vector3(from.x,ChangwonData.Height(from.x,from.z),from.z),300f,out ra,out aa,out pa))return null;
            if(!ChangwonData.NearestRoad(new Vector3(to.x,ChangwonData.Height(to.x,to.z),to.z),900f,out rb,out ab,out pb))return new List<Vector3>{pa,new Vector3(to.x,pa.y,to.z)};
            var nodes=ChangwonData.Nodes;int n=nodes.Length;
            var g=new Dictionary<int,float>();var came=new Dictionary<int,int>();var cameRoad=new Dictionary<int,int>();
            var open=new SortedSet<KeyValuePair<float,int>>(Comparer<KeyValuePair<float,int>>.Create((x,y)=>x.Key!=y.Key?x.Key.CompareTo(y.Key):x.Value.CompareTo(y.Value)));
            Vector2 goal=ChangwonData.Nodes[rb.a];
            Func<int,float> heur=k=>Vector2.Distance(nodes[k],new Vector2(pb.x,pb.z));
            g[ra.a]=aa;g[ra.b]=ra.length-aa;open.Add(new KeyValuePair<float,int>(g[ra.a]+heur(ra.a),ra.a));open.Add(new KeyValuePair<float,int>(g[ra.b]+heur(ra.b),ra.b));
            came[ra.a]=-1;came[ra.b]=-1;
            int found=-1;int guard=0;
            while(open.Count>0&&guard++<400000){
                var top=open.Min;open.Remove(top);int k=top.Value;
                if(k==rb.a||k==rb.b){found=k;break;}
                var list=ChangwonData.NodeRoads[k];if(list==null)continue;
                foreach(int ri in list){
                    var road=ChangwonData.Roads[ri];if(!road.Drivable||road.cls==ChangwonData.Track)continue;
                    int next=road.a==k?road.b:road.a;if(road.OneWay&&road.a!=k)continue;
                    float cost=g[k]+road.length*(road.cls<=ChangwonData.Primary?.8f:road.cls>=ChangwonData.Service?1.6f:1f);
                    float old;if(g.TryGetValue(next,out old)&&old<=cost)continue;
                    if(g.ContainsKey(next))open.Remove(new KeyValuePair<float,int>(old+heur(next),next));
                    g[next]=cost;came[next]=k;cameRoad[next]=ri;open.Add(new KeyValuePair<float,int>(cost+heur(next),next));
                }
            }
            var path=new List<Vector3>();
            if(found<0){path.Add(pa);path.Add(pb);return path;}
            path.Add(pb);
            int at=found;
            while(came.ContainsKey(at)&&came[at]>=0){
                var road=ChangwonData.Roads[cameRoad[at]];
                if(road.b==at)for(int i=road.pts.Length-1;i>=0;i--)path.Add(road.pts[i]);else for(int i=0;i<road.pts.Length;i++)path.Add(road.pts[i]);
                at=came[at];
            }
            path.Add(pa);path.Reverse();return path;
        }
    }
}
