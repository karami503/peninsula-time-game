using UnityEngine;

namespace PeninsulaTime {
    public partial class WorldBuilder {
        partial void BuildNetworkTransferWayfinding(){
            foreach(var route in NetworkTransfers){
                string name=route.line.name+"\n"+route.toward+(route.line.planned?" · 계획":"");
                PaintFloorGuide(root.transform,name,route.line.id,route.line.color,route.Points,.45f);
                PaintFloorGuide(root.transform,"환승 · 다른 노선\n공용 대합실",route.line.id+"/transfer",new Color(.12f,.72f,.68f),
                    new[]{route.platformPoint,route.rampBottom,route.rampTop,route.hallPoint},.45f);
            }
            if(NetworkTransfers.Count>1){
                var a=new Vector3(-6,NetworkHallY,-77.5f);var b=new Vector3(NetworkHallRight-5,NetworkHallY,-77.5f);
                PaintFloorGuide(root.transform,"다른 노선 환승\n노선 색상과 방면 확인","network-transfer-hall",new Color(.12f,.72f,.68f),new[]{a,b});
                PaintFloorGuide(root.transform,"다른 노선 환승\n노선 색상과 방면 확인","network-transfer-hall/reverse",new Color(.12f,.72f,.68f),new[]{b+Vector3.back,a+Vector3.back});
            }
        }
    }
}
