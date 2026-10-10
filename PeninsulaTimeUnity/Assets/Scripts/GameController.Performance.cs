using UnityEngine;
namespace PeninsulaTime {
    public partial class GameController {
        float performanceTime;int performanceFrames;
        static readonly string[] QualityNames={"빠르게","균형","선명하게"};
        string performanceText="성능 측정 중";
        void UpdatePerformanceMeter(){
            performanceTime+=Time.unscaledDeltaTime;performanceFrames++;
            if(performanceTime<1)return;
            performanceText=Mathf.RoundToInt(performanceFrames/performanceTime)+" FPS";
            performanceTime=0;performanceFrames=0;
        }
        void DrawPerformanceSettings(){
            Label("화면 품질 · "+performanceText,smallStyle);
            GUILayout.BeginHorizontal();
            for(int i=0;i<3;i++)if(DumpButton(QualityNames[i]+(PerformanceRuntime.Level==i?" ✓":""),buttonStyle,GUILayout.Height(30)))PerformanceRuntime.Apply(i);
            GUILayout.EndHorizontal();
            if(PerformanceRuntime.Level==0)Label("가까운 풍경 위주로 표시 · 조명과 그림자 간소화",smallStyle);
            GUILayout.Space(8);
        }
    }
}
