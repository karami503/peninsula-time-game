using UnityEngine;
namespace PeninsulaTime {
    public partial class WorldBuilder {
        void BuildMovingWalkways(){
            // Keep junctions, stair landings and short sections clear. Opposing belts fit the corridor.
            var built=new System.Collections.Generic.List<Vector3>();
            foreach(var route in root.GetComponentsInChildren<StationWalkRoute>())for(int k=1;k<route.points.Length;k++){
                var a=route.points[k-1];var b=route.points[k];var delta=b-a;
                if(Mathf.Abs(delta.y)>.05f||delta.magnitude<35)continue;
                var forward=delta.normalized;var right=Vector3.Cross(Vector3.up,forward);
                // Split at every crossing so a belt never blocks a side passage.
                float length=delta.magnitude;
                for(float start=5;start<length-15;start+=28){
                    float end=Mathf.Min(start+22,length-5);if(end-start<10)continue;
                    var centre=a+forward*((start+end)*.5f);
                    if(built.Exists(p=>Vector3.Distance(p,centre)<26))continue;
                    if(StationIndex==3&&gimpoTopMouths.Exists(m=>Mathf.Abs(m.a.y-centre.y)<1&&FlatSegmentDistance(m.a,a+forward*start,a+forward*end)<8))continue;
                    bool crossing=false;
                    foreach(var other in passages){
                        var dir=(other.b-other.a).normalized;if(Mathf.Abs(Vector3.Dot(dir,forward))>.95f)continue;
                        for(float t=start-2;t<=end+2;t+=1)if(FlatSegmentDistance(a+forward*t,other.a,other.b)<2.5f){crossing=true;break;}
                        if(crossing)break;
                    }
                    if(crossing)continue;built.Add(centre);
                    foreach(float side in new[]{-1f,1f}){
                        var pos=centre+right*side*.59f;
                        var belt=Primitive(PrimitiveType.Cube,"무빙워크 · 열린 진입부",station,Vector3.zero,new Vector3(.95f,.05f,end-start),Mat("walkway-belt",new Color(.18f,.21f,.23f),.45f,"metal",8));
                        belt.transform.position=pos+Vector3.up*.025f;belt.transform.rotation=Quaternion.LookRotation(forward*side);
                        var carrier=belt.AddComponent<MovingWalkway>();carrier.length=end-start;carrier.width=.95f;carrier.Register();
                        foreach(float t in new[]{start+.5f,end-.5f})WalkSlab(station,"무빙워크 끝 안전선",a+forward*t+right*side*.59f+Vector3.up*.06f,a+forward*(t+.08f)+right*side*.59f+Vector3.up*.06f,.95f,.01f,Mat("walkway-warning",new Color(.95f,.75f,.12f)),false);
                        for(float t=start+2;t<end-1;t+=4){var p=a+forward*t+right*side*.59f+Vector3.up*.055f;var tip=p+forward*side*.4f;
                            foreach(float wing in new[]{-1f,1f})WalkSlab(station,"무빙워크 진행 화살표",p+right*wing*.22f,tip,.045f,.01f,Glow("walkway-arrow",new Color(.3f,.9f,.55f)),false);
                        }
                        SurveyBeam("무빙워크 바깥 손잡이",station,a+forward*start+right*side*1.15f+Vector3.up*.9f,a+forward*end+right*side*1.15f+Vector3.up*.9f,.06f,Mat("walkway-handrail",new Color(.12f,.14f,.16f)));
                    }
                    Board("무빙워크  ↑ ↓",station,centre+Vector3.up*2.7f,-forward,new Vector2(2,.35f),new Color(.12f,.22f,.28f),Color.white,.17f);
                }
            }
        }
    }
}
