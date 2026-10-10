using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeninsulaTime
{
    // A 20-minute day: 10 minutes of daylight (06:00–18:00 on the game clock) and 10 minutes of night.
    public static class DayCycle
    {
        public const float DaySeconds=600f,NightSeconds=600f,Length=DaySeconds+NightSeconds;
        public static float Seconds=100f; // into the cycle; the game opens in the morning
        public static bool Night{get{return Seconds>=DaySeconds;}}
        public static void Advance(float seconds){Seconds=Mathf.Repeat(Seconds+seconds,Length);}
        // Game clock: day maps to 06:00–18:00, night to 18:00–06:00.
        public static float Hours{get{return Night?(18f+(Seconds-DaySeconds)/NightSeconds*12f)%24f:6f+Seconds/DaySeconds*12f;}}
        public static string Clock{get{float h=Hours;int m=(int)(h*60);return (m/60).ToString("00")+":"+(m%60).ToString("00");}}
        // 0..1 through the current half (day or night).
        public static float Phase{get{return Night?(Seconds-DaySeconds)/NightSeconds:Seconds/DaySeconds;}}
    }

    // Sun and moon, sky, ambient light and fog for the 3D street views, and street lamps that come on at dusk.
    public partial class WorldBuilder
    {
        const int LampLights=16;
        public bool dayNight; // set by the street views (districts, city streets, the KTX ride); maps stay in daylight
        Material sky;readonly List<Light> lampLights=new List<Light>();float lampRefresh;bool lampsLit,skySearched;
        float daylightNext;Light daylightSun;bool daylightInside,daylightMap;int daylightQuality=-1,lampQuality=-1;
        readonly StreetLamp[] nearestLamps=new StreetLamp[LampLights];
        readonly float[] nearestLampDistances=new float[LampLights];
        static readonly string[] GlowingMaterials={"city-light","city-glass_lit","shop-sign-glow"};
        static readonly System.Predicate<Light> GoneLamp=l=>l==null;
        static readonly int ExposureId=Shader.PropertyToID("_Exposure"),AtmosphereId=Shader.PropertyToID("_AtmosphereThickness"),SunSizeId=Shader.PropertyToID("_SunSize"),SkyTintId=Shader.PropertyToID("_SkyTint"),GroundColorId=Shader.PropertyToID("_GroundColor");
        public int DaylightUpdateCount{get;private set;}
        public int ActiveStreetLightCount{get{int count=0;foreach(var light in lampLights)if(light!=null&&light.enabled)count++;return count;}}
        static readonly Color DayAmbient=new Color(.50f,.57f,.64f),NightAmbient=new Color(.09f,.11f,.17f),DuskTint=new Color(1f,.62f,.38f);
        static readonly Color DayFog=new Color(.64f,.74f,.82f),NightFog=new Color(.035f,.045f,.07f);

        // Called every frame from LateUpdate with the eye position.
        void ApplyDaylight(Vector3 eye)
        {
            if(sun==null)return;
            bool inside=Indoors(eye);
            bool map=!dayNight||worldCamera.orthographic;
            SetLamps(!map&&DayCycle.Night&&!inside,eye);
            // The sky takes twenty minutes to cycle. Refreshing its material and
            // lighting five times a second is enough; entering a floor is immediate.
            bool changed=daylightSun!=sun||daylightInside!=inside||daylightMap!=map||daylightQuality!=PerformanceRuntime.Level;
            if(!changed&&Time.unscaledTime<daylightNext)return;
            daylightNext=Time.unscaledTime+.2f;daylightSun=sun;daylightInside=inside;daylightMap=map;daylightQuality=PerformanceRuntime.Level;
            DaylightUpdateCount++;
            if(map)
            {
                if(!sun.enabled){sun.enabled=true;RenderSettings.ambientLight=outdoorAmbient;}
                return;
            }
            // The sun crosses from east (-x) through south (+z) to west; at night the moon follows the same arc.
            float phase=DayCycle.Phase,arc=Mathf.Sin(phase*Mathf.PI);
            float elevation=arc*(DayCycle.Night?42f:62f),azimuth=Mathf.Lerp(-80f,80f,phase);
            // Azimuth 0 points south (+z); negative turns toward the east (-x), positive toward the west.
            var toLight=Quaternion.Euler(0,azimuth,0)*(Quaternion.Euler(-elevation,0,0)*Vector3.forward);
            sun.transform.rotation=Quaternion.LookRotation(-toLight);
            float light=DayCycle.Night?0:Mathf.SmoothStep(0,1,arc*4f);         // 0 night .. 1 full day
            float dusk=DayCycle.Night?0:Mathf.Clamp01(1f-arc*3.2f);            // low sun at dawn and dusk
            // Keep the directional light alive while indoors. The roof casts the interior shadow, while scenery
            // visible through an entrance remains lit by the same outdoor sun.
            sun.enabled=true;
            sun.color=DayCycle.Night?new Color(.55f,.64f,.9f):Color.Lerp(new Color(1f,.94f,.84f),DuskTint,dusk);
            sun.intensity=DayCycle.Night?.18f+.1f*arc:Mathf.Lerp(.25f,1.3f,light);
            sun.shadows=DayCycle.Night||!PerformanceRuntime.Shadows?LightShadows.None:LightShadows.Soft;
            var ambient=Color.Lerp(NightAmbient,DayAmbient,light);ambient=Color.Lerp(ambient,ambient*DuskTint*1.2f,dusk*.5f);
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=inside?IndoorAmbient:ambient*1.15f;
            RenderSettings.ambientEquatorColor=inside?IndoorAmbient:ambient;
            RenderSettings.ambientGroundColor=inside?IndoorAmbient*.8f:ambient*.55f;
            RenderSettings.ambientLight=inside?IndoorAmbient:ambient;
            var fog=Color.Lerp(NightFog,DayFog,light);fog=Color.Lerp(fog,new Color(.86f,.62f,.48f),dusk*.55f);
            RenderSettings.fogColor=inside?new Color(.08f,.08f,.09f):fog;
            if(!skySearched){sky=Resources.Load<Material>("Sky");skySearched=true;}
            if(sky!=null&&!inside)
            {
                RenderSettings.skybox=sky;RenderSettings.sun=sun;
                sky.SetFloat(ExposureId,Mathf.Lerp(.08f,1.25f,light)+dusk*.25f);
                sky.SetFloat(AtmosphereId,1f+dusk*.7f);
                sky.SetFloat(SunSizeId,DayCycle.Night?.025f:.045f);
                sky.SetColor(SkyTintId,DayCycle.Night?new Color(.25f,.3f,.5f):new Color(.5f,.5f,.5f));
                sky.SetColor(GroundColorId,Color.Lerp(new Color(.05f,.06f,.08f),new Color(.42f,.44f,.42f),light));
                worldCamera.clearFlags=CameraClearFlags.Skybox;
            }
            else if(inside&&Carved!=null){worldCamera.clearFlags=CameraClearFlags.Skybox;RenderSettings.skybox=sky;RenderSettings.sun=sun;}
            else{worldCamera.clearFlags=CameraClearFlags.SolidColor;worldCamera.backgroundColor=inside?new Color(.05f,.05f,.06f):fog;}
        }
        // Lamp heads and car lights glow while lit; the nearest lamps also light the street.
        void SetLamps(bool on,Vector3 eye=default(Vector3))
        {
            if(on!=lampsLit)
            {
                lampsLit=on;
                lampRefresh=0;
                foreach(var key in GlowingMaterials)
                {
                    Material m;if(!materials.TryGetValue(key,out m))continue;
                    if(on){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",new Color(1f,.82f,.55f)*1.6f);}
                    else{m.SetColor("_EmissionColor",Color.black);m.DisableKeyword("_EMISSION");}
                }
                if(!on)foreach(var l in lampLights)if(l!=null)l.enabled=false;
            }
            bool qualityChanged=lampQuality!=PerformanceRuntime.Level;
            if(!on||(!qualityChanged&&Time.time<lampRefresh))return;
            lampQuality=PerformanceRuntime.Level;
            lampRefresh=Time.time+.5f;
            lampLights.RemoveAll(GoneLamp);
            int limit=Mathf.Clamp(PerformanceRuntime.MaxLocalLights,0,LampLights);
            while(lampLights.Count<limit)
            {
                var light=new GameObject("가로등 불빛").AddComponent<Light>();light.transform.SetParent(root.transform,false);
                light.type=LightType.Point;light.range=14f;light.intensity=1.8f;light.color=new Color(1f,.84f,.6f);light.shadows=LightShadows.None;
                lampLights.Add(light);
            }
            // Only the nearest small pool is used: select it without copying or
            // sorting every lamp into an allocated list twice a second.
            for(int i=0;i<limit;i++){nearestLamps[i]=null;nearestLampDistances[i]=120f*120f;}
            foreach(var lamp in StreetLamp.All)
            {
                if(lamp==null||limit==0)continue;
                float distance=(lamp.head-eye).sqrMagnitude;
                if(distance>=nearestLampDistances[limit-1])continue;
                int at=limit-1;
                while(at>0&&distance<nearestLampDistances[at-1]){nearestLampDistances[at]=nearestLampDistances[at-1];nearestLamps[at]=nearestLamps[at-1];at--;}
                nearestLampDistances[at]=distance;nearestLamps[at]=lamp;
            }
            for(int i=0;i<lampLights.Count;i++)
            {
                bool use=i<limit&&nearestLamps[i]!=null;
                lampLights[i].enabled=use;if(use)lampLights[i].transform.position=nearestLamps[i].head;
            }
        }
    }
}
