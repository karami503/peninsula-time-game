using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime {
    public class DistrictTransferPortal : MonoBehaviour {
        public string stationId,lineId,label;
        public Vector3 approach,entry;
        public bool Contains(Vector3 feet){return Mathf.Abs(feet.y-entry.y)<.55f&&Mathf.Abs(feet.z-entry.z)<.75f&&Mathf.Abs(feet.x-entry.x)<.7f;}
    }
    public partial class WorldBuilder {
        public readonly List<DistrictTransferPortal> DistrictTransfers=new List<DistrictTransferPortal>();
        void BuildDistrictTransferGuides(){
            DistrictTransfers.Clear();
            if(StationIndex==3){BuildGimpoFloorGuides();return;}
            float y=ConcourseY;
            var present=new HashSet<string>();
            foreach(var side in PlatformSides)present.Add(side.line);
            // These paid-side links continue to the playable nationwide interchange scene.
            // Their corridor placement is a game connection, not a surveyed indoor map.
            var added=new HashSet<string>();int n=0;
            var anchor=TransitNetwork.Named(StationNames[StationIndex]).Find(s=>s.lines.Exists(l=>l.kind=="metro"));
            if(anchor!=null)foreach(var at in TransitNetwork.Named(anchor.name)){
                if(TransitNetwork.Kilometres(anchor,at)>.8f)continue;
                foreach(var line in at.lines){
                    if(line.kind=="bus"||line.kind=="brt"||present.Contains(line.shortName))continue;
                    string group=line.kind=="ktx"?"KTX":line.kind=="mugunghwa"?"무궁화호":line.shortName;
                    if(!added.Add(group))continue;
                    // The side aisles are clear of the existing stairs, shopfronts and pillars. Seoul Station's three
                    // islands put stairwells in those aisles, so its links line the north wall between the stairwells.
                    float z=2.8f+(n%6)*2.55f;float edge=n<6?-1:1;bool north=StationIndex==1;float x=edge*(7.6f+(n%6)*2f); // clear of the x=±6.5 pillars
                    var entry=north?new Vector3(x,y,17.3f):new Vector3(29*edge,y,z);var approach=north?new Vector3(x,y,16.3f):new Vector3(27*edge,y,z);
                    var go=new GameObject(group+" 환승 연결");go.transform.SetParent(root.transform,false);
                    var portal=go.AddComponent<DistrictTransferPortal>();portal.stationId=at.id;portal.lineId=line.id;portal.label=group;portal.entry=entry;portal.approach=approach;DistrictTransfers.Add(portal);
                    Board(group+" 갈아타는 곳 "+(north?"↑":edge<0?"←":"→"),root.transform,entry+Vector3.up*3f,north?Vector3.back:Vector3.left*edge,new Vector2(2.4f,.5f),line.color,Color.white,.23f);
                    var guide=north?new[]{new Vector3(-3,y,-.8f),new Vector3(12*edge,y,-.8f),new Vector3(12*edge,y,16.3f),approach,entry}:new[]{new Vector3(-3,y,-.8f),new Vector3(27*edge,y,-.8f),approach,entry};
                    PaintFloorGuide(root.transform,group+" 환승",line.id,line.color,guide,n*.08f);
                    Marker(entry,group+" 환승");n++;
                }
            }
            if(StationIndex==0){BuildGangnamFloorGuides();return;}
            for(int island=0;island<islandX.Count;island++){
                int slot=island;var side=PlatformSides.Find(s=>s.island==slot);float x=islandX[island];
                float floor=IslandFloor(island),end=2+Mathf.Max(14f,(y-floor)*2);
                PaintFloorGuide(root.transform,side.line+" 타는 곳",side.net!=null?side.net.id:side.line,side.color,
                    new[]{new Vector3(0,y,-.7f),new Vector3(x-2,y,-.7f),new Vector3(x-2,y,2),new Vector3(x-2,floor,end),new Vector3(x-2,floor,end+3)});
                PaintFloorGuide(root.transform,"환승 · 나가는 곳","transfer",new Color(.95f,.73f,.17f),
                    new[]{new Vector3(x-2,floor,end+6),new Vector3(x-2,floor,end),new Vector3(x-2,y,2),new Vector3(x-2,y,.1f)},.45f);
            }
        }
        void BuildGimpoFloorGuides(){
            var routes=root.GetComponentsInChildren<StationWalkRoute>();
            foreach(var stairs in routes){
                if(!stairs.name.EndsWith("계단 보행")||stairs.points==null||stairs.points.Length<2)continue;
                var at=stairs.points[0];StationWalkRoute link=null;
                foreach(var candidate in routes)if(candidate.name=="환승 연결"&&candidate.points.Length>1&&Vector3.Distance(candidate.points[candidate.points.Length-1],at)<.1f){link=candidate;break;}
                if(link==null)continue;
                string label=stairs.exitNumber.Contains("공유승강장")?"9호선 · 공항철도":stairs.exitNumber;
                Color color=label.Contains("공항철도")?Arex:Color.white;
                foreach(var side in PlatformSides)if(label.Contains(side.line)){color=side.color;break;}
                var points=new List<Vector3>{GimpoHall+new Vector3(0,ConcourseY,GateZ+2),GimpoHall+new Vector3(0,ConcourseY,18)};
                points.AddRange(link.points);points.AddRange(stairs.points);
                PaintFloorGuide(root.transform,label+" 타는 곳",label,color,points,.1f);
                var back=new List<Vector3>(points);back.Reverse();PaintFloorGuide(root.transform,"환승 · 출구","transfer",new Color(.95f,.73f,.17f),back,.45f);
            }
        }
    }
}
