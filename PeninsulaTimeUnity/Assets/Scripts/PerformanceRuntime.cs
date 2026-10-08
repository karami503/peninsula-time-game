using UnityEngine;
namespace PeninsulaTime {
    public static class PerformanceRuntime {
        static int level=-1;
        public static int Level {get {if(level<0)level=Mathf.Clamp(PlayerPrefs.GetInt("graphics-profile",Application.isMobilePlatform?0:1),0,2);return level;}}
        public static float RenderDistance {get{return Level==0?170:Level==1?260:420;}}
        public static float MiniMapInterval {get{return Level==0?.2f:Level==1?.1f:.0667f;}}
        public static int MaxLocalLights {get{return Level==0?4:Level==1?8:12;}}
        public static bool Shadows {get{return Level>0;}}
        public static string Name {get{return Level==0?"빠르게":Level==1?"균형":"선명하게";}}
        public static void Apply(int choice=-1) {
            if(choice>=0){level=Mathf.Clamp(choice,0,2);PlayerPrefs.SetInt("graphics-profile",level);PlayerPrefs.Save();}
            QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
            QualitySettings.antiAliasing=Level==2?2:0;
            QualitySettings.pixelLightCount=Level==0?2:4;
            QualitySettings.shadows=Shadows?ShadowQuality.HardOnly:ShadowQuality.Disable;
            QualitySettings.shadowDistance=Level==0?0:Level==1?45:80;
            QualitySettings.shadowCascades=0;QualitySettings.realtimeReflectionProbes=false;
            QualitySettings.lodBias=Level==0?.65f:Level==1?1:1.5f;
            QualitySettings.anisotropicFiltering=AnisotropicFiltering.Enable;
            QualitySettings.streamingMipmapsActive=true;QualitySettings.streamingMipmapsMemoryBudget=Application.isMobilePlatform?192:384;
        }
    }
}
