using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime {
    public partial class WorldBuilder {
        // Seoul Metro's architecture data and the reviewed station diagram establish
        // a 205 m, B2 side platform for Line 2. Widths, metric depths, stair positions
        // and the detailed connecting passages remain playable estimates, not CAD.
        public const float GangnamPlatformLength=205f;
        const float GangnamStairHalf=2.1f;
        public readonly List<StationWalkRoute> GangnamPlatformRoutes=new List<StationWalkRoute>();

        void BuildGangnamStation(){
            islandX.Clear();islandX.Add(0);
            var hall=new GameObject("강남역 · B1 대합실").transform;hall.SetParent(root.transform,false);station=hall;
            BuildConcourse(0);BuildFareGates();
            var platform=new GameObject("강남 2호선 · B2 상대식 승강장 · 205m").transform;platform.SetParent(hall,false);station=platform;
            BuildGimpoSidePlatforms(PlatformSides,1,PlatformY,GangnamPlatformLength*.5f,"강남","Gangnam",true);
            for(int side=0;side<2;side++)BuildGangnamPlatformStairs(side);
            station=hall;BuildGangnamPeople();
            Marker(new Vector3(0,ConcourseY,GateZ),"개찰구");
            Marker(new Vector3(-HalfX+1.5f,ConcourseY,-13.6f),"교통 안내");
            Marker(new Vector3(-9.5f,ConcourseY,GateZ-3f),"안내");
        }

        void BuildGangnamPlatformCeiling(float floor,float half,Material material){
            float top=floor+5f,end=2+Mathf.Max(14f,(ConcourseY-floor)*2f);
            Block("상대식 승강장 천장",station,new Vector3(-13,top,-half),new Vector3(13,top+.3f,1),material,false);
            Block("상대식 승강장 천장",station,new Vector3(-13,top,end+1),new Vector3(13,top+.3f,half),material,false);
            float from=-13;
            foreach(float x in new[]{-GimpoSideWalkX,GimpoSideWalkX}){
                Block("상대식 승강장 천장",station,new Vector3(from,top,1),new Vector3(x-GangnamStairHalf,top+.3f,end+1),material,false);from=x+GangnamStairHalf;
            }
            Block("상대식 승강장 천장",station,new Vector3(from,top,1),new Vector3(13,top+.3f,end+1),material,false);
        }

        void BuildGangnamPlatformStairs(int side){
            float sign=side==0?1:-1,x=sign*GimpoSideWalkX;
            float end=2+Mathf.Max(14f,(ConcourseY-PlatformY)*2f);
            var stone=Mat("stair-stone",new Color(.70f,.70f,.68f),0,"granite",1f);
            var yellow=Mat("tactile",new Color(.95f,.78f,.15f));
            var a=new Vector3(x,ConcourseY,2);var b=new Vector3(x,PlatformY,end);
            StairFlight(station,a,b,3.6f,stone,yellow);
            var glass=Mat("station-glass",new Color(.55f,.72f,.78f),.3f);
            foreach(float edge in new[]{x-GangnamStairHalf,x+GangnamStairHalf})
                Block("계단실 난간",station,new Vector3(edge-.05f,ConcourseY,2),new Vector3(edge+.05f,ConcourseY+1.1f,16),glass);
            Block("계단실 난간",station,new Vector3(x-GangnamStairHalf,ConcourseY,15.95f),new Vector3(x+GangnamStairHalf,ConcourseY+1.1f,16.05f),glass);
            // The inner 2.3 m aisle bypasses the stair shaft without crossing tracks.
            var points=new[]{new Vector3(0,ConcourseY,-.7f),new Vector3(x,ConcourseY,-.7f),new Vector3(x,ConcourseY,1),a,b,
                new Vector3(x,PlatformY,end+3),new Vector3(sign*6.5f,PlatformY,end+3),new Vector3(sign*6.5f,PlatformY,-2),new Vector3(sign*6.5f,PlatformY,-24),new Vector3(x,PlatformY,-24)};
            var line=PlatformSides[side];
            var route=new GameObject("강남 2호선 · "+line.toward+" · 계단 보행").AddComponent<StationWalkRoute>();route.transform.SetParent(station,false);route.exitNumber=line.line+" · "+line.toward;route.points=points;GangnamPlatformRoutes.Add(route);
            Board(line.line+" · "+line.toward+" ↓",station,new Vector3(x,ConcourseY+3.4f,1.35f),Vector3.back,new Vector2(5.8f,.65f),line.color,Color.white,.28f);
            Board("환승 · 나가는 곳 ↑",station,new Vector3(x,PlatformY+3.3f,end+1.5f),Vector3.forward,new Vector2(4.8f,.6f),new Color(.12f,.13f,.15f),new Color(1f,.82f,.1f),.28f);
            Marker(new Vector3(x,ConcourseY,1),line.toward+" ↓");Marker(new Vector3(sign*6.5f,PlatformY,-40),line.line+" · "+line.toward);
        }

        void BuildGangnamFloorGuides(){
            for(int i=0;i<GangnamPlatformRoutes.Count;i++){
                var route=GangnamPlatformRoutes[i];var line=PlatformSides[i];string id=line.net!=null?line.net.id:line.line;
                PaintFloorGuide(root.transform,route.exitNumber+" 타는 곳",id,line.color,route.points,.08f);
                var back=new List<Vector3>(route.points);back.Reverse();
                PaintFloorGuide(root.transform,"신분당선 환승 · 출구","transfer",new Color(.95f,.73f,.17f),back,.4f);
            }
        }

        void BuildGangnamPeople(){
            var graph=new TrafficGraph();
            System.Action<Vector3[]> loop=points=>{for(int i=0;i<points.Length;i++)graph.Link(points[i],points[(i+1)%points.Length],0);};
            loop(new[]{new Vector3(-20,ConcourseY,-14),new Vector3(20,ConcourseY,-14),new Vector3(20,ConcourseY,-7),new Vector3(-20,ConcourseY,-7)});
            // Each direction stays outside the rails and clear of its stair shaft.
            foreach(float sign in new[]{-1f,1f}){
                foreach(var span in new[]{new Vector2(-95,-10),new Vector2(28,95)})
                    loop(new[]{new Vector3(sign*8f,PlatformY,span.x),new Vector3(sign*8f,PlatformY,span.y),new Vector3(sign*10.5f,PlatformY,span.y),new Vector3(sign*10.5f,PlatformY,span.x)});
            }
            var director=new GameObject("역 보행자").AddComponent<TrafficDirector>();director.transform.SetParent(station,false);director.Init(graph,null,null,null);SpawnPeople(director,34,0,0,1f);
        }
    }
}
