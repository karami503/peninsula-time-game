using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Heading-up minimap for walking in the Seoul districts, stations and the airport terminal:
    // a small top-down camera under the ceiling indoors, high above the roofs outdoors, plus labelled markers.
    public partial class GameController
    {
        // GTA V's radar is a wide rectangle (270x177 px on a 1920x1080 screenshot, about 1.52:1) with a three-segment bar under it.
        const int MinimapPixelsWide=488,MinimapPixelsHigh=320;
        const float MinimapWidth=225f,MinimapHeight=148f,RadarBarHeight=9f,RadarBarGap=3f,IndoorRange=24f,OutdoorRange=70f,DrivingRange=140f;
        Camera minimapCamera;RenderTexture minimapTexture;
        bool showMinimap=true;
        GUIStyle minimapLabel,minimapTitle,minimapArrow,minimapExit;
        float minimapNextRender;
        Vector3 minimapEye;Quaternion minimapTurn;
        float minimapRange;bool minimapWasActive;
        GameObject minimapWorld;
        string minimapPlace;
        struct MinimapCaption {public GUIContent content;public Vector2 size;public bool exit;}
        readonly Dictionary<string,MinimapCaption> minimapCaptions=new Dictionary<string,MinimapCaption>();
        public int MinimapRenderCount{get;private set;}

        bool MinimapActive()
        {
            return showMinimap&&(mode=="district"||mode=="rail")&&world!=null&&!world.aerialDistrict&&tab=="3D"&&!TransitRideActive();
        }
        bool Indoors(Vector3 eye){return mode=="carved"?world.IsJinhaeHallInterior(eye):eye.y<-2f||WorldBuilder.IsDomesticAirportInterior(eye)||WorldBuilder.IsInternationalAirportInterior(eye);}

        // The small map does not need a second full world render on every frame.
        // Keep its markers on the same sampled pose as the texture between refreshes.
        void RenderMinimap()
        {
            if(!MinimapActive()){minimapWasActive=false;return;}
            float now=Time.unscaledTime;
            bool changedWorld=minimapWorld!=world.root;
            if(minimapWasActive&&!changedWorld&&now<minimapNextRender)return;
            minimapWasActive=true;minimapWorld=world.root;
            minimapNextRender=now+PerformanceRuntime.MiniMapInterval;
            if(changedWorld)minimapCaptions.Clear();
            if(minimapCamera==null)
            {
                minimapTexture=new RenderTexture(MinimapPixelsWide,MinimapPixelsHigh,16);
                minimapCamera=new GameObject("Minimap camera").AddComponent<Camera>();
                minimapCamera.enabled=false;minimapCamera.orthographic=true;minimapCamera.targetTexture=minimapTexture;
                minimapCamera.clearFlags=CameraClearFlags.SolidColor;minimapCamera.backgroundColor=new Color(.08f,.10f,.11f);
                minimapCamera.allowHDR=false;minimapCamera.allowMSAA=false;
            }
            var eye=this.eye.position;bool inside=Indoors(eye);
            // Indoors the camera sits just under the ceiling of the current floor; outdoors high above the roofs.
            minimapCamera.transform.position=eye+Vector3.up*(inside?.8f:150f);
            minimapCamera.transform.rotation=Quaternion.Euler(90,this.eye.eulerAngles.y,0);
            // GTA V keeps the radar in a car and zooms it out while driving.
            minimapCamera.orthographicSize=inside?IndoorRange:InVehicle()?DrivingRange:OutdoorRange;
            minimapCamera.nearClipPlane=.05f;minimapCamera.farClipPlane=inside?12f:400f;
            minimapEye=eye;minimapTurn=Quaternion.Euler(0,-this.eye.eulerAngles.y,0);
            minimapRange=minimapCamera.orthographicSize;minimapPlace=PlaceName();
            bool fog=RenderSettings.fog;RenderSettings.fog=false;
            try{minimapCamera.Render();MinimapRenderCount++;}
            finally{RenderSettings.fog=fog;}
        }

        void DrawMinimap(float w,float h)
        {
            // The H menu covers the radar's corner, so the radar steps aside while it is open.
            if(Event.current.type!=EventType.Repaint||streetMenu||!MinimapActive()||minimapTexture==null||minimapWorld!=world.root)return;
            if(minimapLabel==null)
            {
                minimapLabel=new GUIStyle(GUI.skin.label){fontSize=11,alignment=TextAnchor.MiddleCenter,wordWrap=false,clipping=TextClipping.Overflow,font=koreanFont};
                minimapLabel.normal.textColor=Color.white;minimapLabel.normal.background=Solid(new Color(0,0,0,.62f));minimapLabel.padding=new RectOffset(4,4,1,1);
                minimapTitle=new GUIStyle(minimapLabel){fontSize=12,alignment=TextAnchor.MiddleLeft};minimapTitle.normal.background=null;minimapTitle.normal.textColor=new Color(.95f,.82f,.52f);
                minimapExit=new GUIStyle(minimapLabel){font=koreanBoldFont};minimapExit.normal.background=Solid(new Color(.98f,.78f,.12f));minimapExit.normal.textColor=new Color(.1f,.1f,.1f);
                minimapArrow=new GUIStyle(minimapLabel){fontSize=18};minimapArrow.normal.background=null;minimapArrow.normal.textColor=new Color(1f,.78f,.25f);
            }
            // GTA V: the radar has no plate. It sits bottom-left inside the safe zone with its three-segment bar underneath;
            // the place name is shadowed text above it.
            float barY=h-h*SafeZone-RadarBarHeight;
            var frame=new Rect(w*SafeZone,barY-RadarBarGap-MinimapHeight,MinimapWidth,MinimapHeight);
            DrawRadarBar(new Rect(frame.x,barY,frame.width,RadarBarHeight));
            ShadowLabel(new Rect(frame.x,frame.y-22,frame.width,20),minimapPlace,minimapTitle);
            UiTexture(frame,minimapTexture);
            var eye=minimapEye;bool inside=Indoors(eye);
            float range=minimapRange,feet=eye.y-EyeHeight,aspect=frame.width/frame.height;
            var turn=minimapTurn;
            foreach(var marker in world.MapMarkers)
            {
                if(Mathf.Abs(marker.position.y-feet)>(inside?3f:8f))continue;
                var local=turn*(marker.position-eye);
                float mx=local.x/(range*aspect),my=local.z/range;
                if(Mathf.Abs(mx)>.95f||Mathf.Abs(my)>.95f)continue;
                var at=new Vector2(frame.center.x+mx*frame.width*.5f,frame.center.y-my*frame.height*.5f);
                // Bare numbers are station exits: yellow, as on Seoul exit signs.
                if(string.IsNullOrEmpty(marker.label))continue;
                MinimapCaption caption;
                if(!minimapCaptions.TryGetValue(marker.label,out caption))
                {
                    caption.exit=marker.label.Length<=3&&char.IsDigit(marker.label[0]);
                    caption.content=new GUIContent(marker.label);
                    caption.size=(caption.exit?minimapExit:minimapLabel).CalcSize(caption.content);
                    minimapCaptions.Add(marker.label,caption);
                }
                var size=caption.size;
                UiLabel(new Rect(at.x-size.x*.5f,at.y-size.y*.5f,size.x,size.y),caption.content,caption.exit?minimapExit:minimapLabel);
            }
            // You: always at the centre, facing up. North marker on the rim.
            UiLabel(new Rect(frame.center.x-12,frame.center.y-12,24,24),"▲",minimapArrow);
            var north=turn*Vector3.back; // district world: x = -east, z = -north
            // Pin the N to the rim of the wide frame along the north direction.
            float reachX=Mathf.Abs(north.x)<.001f?float.MaxValue:(frame.width*.5f-10f)/Mathf.Abs(north.x);
            float reachY=Mathf.Abs(north.z)<.001f?float.MaxValue:(frame.height*.5f-10f)/Mathf.Abs(north.z);
            float reach=Mathf.Min(reachX,reachY);
            UiLabel(new Rect(frame.center.x+north.x*reach-8,frame.center.y-north.z*reach-8,16,16),"N",minimapLabel);
        }

        // Health, armour and special-ability segments under the GTA V radar. The game has no combat, so the segments read city
        // figures: Seoul's happiness, its budget (full at 500) and the time left on a running mission.
        void DrawRadarBar(Rect bar)
        {
            var economy=Economy("seoul");
            float[] levels={economy.happiness/100f,economy.budget/500f,mission!=null?mission.timeLeft/DeliverySeconds:1f};
            Color[] colours={new Color(.31f,.65f,.33f),new Color(.24f,.48f,.66f),new Color(.78f,.62f,.2f)};
            // GTA V screenshot: health takes half the bar, armour and the special ability a quarter each.
            float[] shares={.5f,.25f,.25f};
            float room=bar.width-RadarBarGap*2f,left=bar.x;
            for(int i=0;i<3;i++)
            {
                var cell=new Rect(left,bar.y,room*shares[i],bar.height);left=cell.xMax+RadarBarGap;
                GUI.color=new Color(0,0,0,.55f);UiTexture(cell,Texture2D.whiteTexture);
                GUI.color=colours[i];UiTexture(new Rect(cell.x,cell.y,cell.width*Mathf.Clamp01(levels[i]),cell.height),Texture2D.whiteTexture);
                GUI.color=Color.white;
            }
        }

        void OnDestroy()
        {
            if(minimapCamera!=null){minimapCamera.targetTexture=null;if(Application.isPlaying)Destroy(minimapCamera.gameObject);else DestroyImmediate(minimapCamera.gameObject);}
            if(minimapTexture!=null){minimapTexture.Release();if(Application.isPlaying)Destroy(minimapTexture);else DestroyImmediate(minimapTexture);}
        }

        // Where the walker is, for the minimap title.
        string PlaceName()
        {
            var eye=this.eye.position;
            if(mode=="carved")
            {
                if(world.IsJinhaeHallInterior(eye))return "진해역 · 대합실";
                if(cabin!=null)return "진해선 셔틀";
                if(world.GyeonghwaStation!=null&&Vector3.Distance(eye,world.GyeonghwaStation.position)<70f)return "경화역";
                if(world.JinhaeStation!=null&&Vector3.Distance(eye,world.JinhaeStation.position)<90f)return "진해역 · 역 주변";
                return "진해 시내";
            }
            if(mode=="rail"&&stationJourney!=null){
                if(stationJourney.Bus)return stationJourney.Current.name+" · 정류장 주변";
                string name=world.NetworkHubStation!=null?world.NetworkHubStation.name:stationJourney.Current.name;
                float floor=eye.y-EyeHeight;
                if(Mathf.Abs(floor-world.NetworkHallY)<.6f)return name+" · 환승 대합실";
                foreach(var route in world.NetworkTransfers)
                    if(Mathf.Abs(floor-route.platformPoint.y)<.6f&&Mathf.Abs(eye.x-route.platformPoint.x)<8f&&eye.z>=route.rampBottom.z&&eye.z<=route.rampBottom.z+route.platformLength)
                        return name+" · "+route.line.shortName+" 승강장";
                return name+(floor<-.5f?" · 출입·환승 통로":" · 역 주변");
            }
            if(WorldBuilder.IsInternationalAirportInterior(eye))return "김포공항 국제선 · "+(eye.y<0?"지하 연결 통로":(Mathf.FloorToInt((eye.y-EyeHeight)/6)+1)+"층");
            if(WorldBuilder.IsDomesticAirportInterior(eye))
            {
                float y=eye.y-EyeHeight;
                return "김포공항 국내선 · "+(y>WorldBuilder.Floor3-1?"3층 탑승구":y>WorldBuilder.Floor2-1?"2층 출발":"1층 도착");
            }
            if(eye.y<-2f)return WorldBuilder.StationTitle(state.district)+" · "+(state.district==2&&eye.y<-25?"공항철도 승강장":eye.y<WorldBuilder.ConcourseY-3f?"승강장":"대합실·출입 통로");
            return GameContent.DistrictNames[state.district]+" 거리";
        }
    }
}
