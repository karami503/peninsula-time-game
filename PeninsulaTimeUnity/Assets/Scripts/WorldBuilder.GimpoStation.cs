using System.Collections.Generic;
using UnityEngine;
namespace PeninsulaTime {
public partial class WorldBuilder {
    // The exit coordinates use the same terminal-centred projection as the OSM district.
    // Footprint orientations follow the supplied map; non-surveyed interior dimensions are estimates.
    public static readonly Vector3 GimpoHall=new Vector3(110,0,-455);
    static readonly float[] GimpoFloors={-19.6f,-29f,-36.5f,-26.4f,-83f};
    public static readonly Vector3[] GimpoExits={new Vector3(143.57f,0,-302.81f),new Vector3(120.99f,0,-522.34f),new Vector3(59.81f,0,-420.11f),new Vector3(35.17f,0,-309.91f)};
    readonly List<Transform> gimpoIslands=new List<Transform>();
    readonly List<Vector3> gimpoStairTops=new List<Vector3>();
    const float GimpoB1=-5.8f;
    public Vector3 PlatformAccess(int island,int side=0)=>StationIndex==0?new Vector3(side%2==0?GimpoSideWalkX:-GimpoSideWalkX,ConcourseY,1):StationIndex==3&&gimpoStairTops.Count>island?gimpoStairTops[island]:new Vector3(IslandCentre(island),ConcourseY,1);
    static List<StationLine> GimpoPlatformLines(List<StationLine> original){
        var list=new List<StationLine>();
        // B3 shared eastbound island, B4 shared westbound island; each retains its own timetable.
        for(int island=0;island<5;island++){
            foreach(var side in original){
                bool include=island==0?side.line=="5호선":island==3?side.line=="김포골드라인":island==4?side.line=="서해선":
                    (side.line=="9호선"&&(side.toward.Contains("중앙보훈")==(island==1)))||
                    (side.line=="공항철도"&&(side.toward.Contains("서울행")==(island==1)));
                if(!include)continue;var copy=side;copy.island=island;list.Add(copy);
            }
        }
        return list;
    }
    void BuildGimpoEntrances(){
        // Ground beyond the old 600 m tile, needed for the international-terminal-side exit.
        // Street/land details here remain schematic; surveyed exits are never shifted to fit scenery.
        var land=Primitive(PrimitiveType.Cube,"Surrounding land",root.transform,new Vector3(0,-.19f,-430),new Vector3(620,.18f,330),Mat("gimpo-land",new Color(.32f,.39f,.28f)));
        var directions=new[]{Vector3.forward,Vector3.back,Vector3.left,Vector3.forward};
        for(int i=0;i<GimpoExits.Length;i++){
            SurfaceEntrance("OSM-339641590"+(i==0?5:i==1?3:i==2?4:6),(i+1).ToString(),GimpoExits[i],directions[i]);
            // Accessible pavement follows the exit mouths; no entrance is relocated for collision avoidance.
            var p=GimpoExits[i]+directions[i]*7;
            WalkSlab(root.transform,"Footway",p-directions[i]*4,p+directions[i]*4,6,.15f,Mat("access-pavement",new Color(.64f,.65f,.62f)),true);
        }
        // Continuous pedestrian connection from the domestic-terminal tile to both southern entrances.
        var path=new[]{new Vector3(80,.02f,-245),new Vector3(80,.02f,-287),new Vector3(143.57f,.02f,-295)};
        for(int i=1;i<path.Length;i++)WalkSlab(root.transform,"Footway",path[i-1],path[i],4,.12f,Mat("access-pavement",new Color(.64f,.65f,.62f)),true);
        WalkSlab(root.transform,"Footway",path[1],GimpoExits[3]+Vector3.forward*8+Vector3.up*.02f,4,.12f,Mat("access-pavement",new Color(.64f,.65f,.62f)),true);
    }
    void BuildGimpoStation(){
        islandX.Clear();gimpoStairTops.Clear();gimpoAllTops.Clear();gimpoTopMouths.Clear();passages.Clear();stairMouths.Clear();
        var container=new GameObject("김포공항역 · 지도 좌표 배치").transform;container.SetParent(root.transform,false);station=container;
        var hall=new GameObject("B2 대합실 · 개찰구").transform;hall.SetParent(container,false);station=hall;
        BuildConcourse(3);BuildFareGates();
        var wall=Mat("station-wall",new Color(.90f,.90f,.87f),0,"white",2);
        // The B1 access corridor reaches the unpaid north side. Only the south opening is paid.
        foreach(float sign in new[]{-1f,1f}){
            Block("대합실 외곽 벽",station,new Vector3(sign*30-.15f,ConcourseY,-18),new Vector3(sign*30+.15f,ConcourseY+4.4f,18),wall);
            Block("대합실 외곽 벽",station,new Vector3(-30,ConcourseY,sign*18-.15f),new Vector3(-3,ConcourseY+4.4f,sign*18+.15f),wall);
            Block("대합실 외곽 벽",station,new Vector3(3,ConcourseY,sign*18-.15f),new Vector3(30,ConcourseY+4.4f,sign*18+.15f),wall);
        }
        // The generic wall map stood in the new north exit opening.
        foreach(Transform child in hall)if(child.name.Contains("노선도")||child.name=="노선"||child.name=="2호선 순환")child.gameObject.SetActive(false);
        hall.position=GimpoHall;station=container;
        Marker(GimpoHall+new Vector3(0,ConcourseY,GateZ),"개찰구");
        var centres=new[]{new Vector3(100,0,-415),new Vector3(-15,0,-345),new Vector3(-15,0,-345),new Vector3(-10,0,-390),new Vector3(-139,0,-323)};
        // Seoul Metro station architecture CSV: Line 5 platform length is 165 m.
        var yaw=new[]{170f,94f,94f,94f,170f};var halves=new[]{82.5f,125f,125f,120f,100f};
        int number=1;
        for(int i=0;i<5;i++){
            var sides=PlatformSides.FindAll(s=>s.island==i);
            var module=new GameObject(i==1?"B3 9호선·공항철도 서울방면":i==2?"B4 9호선·공항철도 인천방면":sides[0].line+" 상대식 승강장").transform;
            module.SetParent(container,false);station=module;
            if(i==1||i==2)BuildPlatform("김포공항","Gimpo Int'l Airport",sides,number,StationTrains,true,null,GimpoFloors[i],true,halves[i]);
            else BuildGimpoSidePlatforms(sides,number,GimpoFloors[i],halves[i]);
            module.position=centres[i];module.rotation=Quaternion.Euler(0,yaw[i],0);gimpoIslands.Add(module);islandX.Add(centres[i].x);station=container;
            if(i==1||i==2)BuildGimpoStairWell(module,i==1?-9f:-2.3f,0,halves[i],GimpoFloors[i],"B"+(i==1?3:4)+" 공유승강장",i,true);
            else foreach(float side in new[]{1f,-1f})BuildGimpoStairWell(module,side*9.3f,side*9.3f,halves[i],GimpoFloors[i],sides[0].line,i,side>0);
            Marker(module.TransformPoint(new Vector3(0,GimpoFloors[i],0)),sides[0].line+(i==1||i==2?"·공항철도":""));number+=sides.Count;
        }
        station=container;
        // A broad paid transfer passage connects separate station bodies, above their track levels.
        var origin=GimpoHall+new Vector3(0,ConcourseY,18f);var hub=new Vector3(110,ConcourseY,-402);
        AddGimpoPaidPath("개찰구 → 환승",new[]{origin,hub});
        foreach(var mouth in gimpoTopMouths){
            var top=mouth.a;var dir=mouth.b-mouth.a;dir.y=0;dir.Normalize();var approach=top-dir*7;
            var bypass=top.x>0?160f:-195f;
            var route=new[]{hub,new Vector3(bypass,ConcourseY,hub.z),new Vector3(bypass,ConcourseY,approach.z),approach,top};
            AddGimpoPaidPath("환승 연결",RoundGimpoPath(route,3f).ToArray());
        }
        var stone=Mat("stair-stone",new Color(.70f,.70f,.68f),0,"granite",1);
        stairMouths.Add(new Passage(origin,origin+Vector3.back*5));
        stairMouths.AddRange(gimpoTopMouths);
        BuildPassageUnion(station,passages,stairMouths,ConcourseY,stone,wall,false,2.5f,.02f,gimpoTopMouths);
        // Passage floor must end at the room edge, rather than doubling the concourse surface.
        gimpoAllTops.Clear();
    }
    readonly List<Vector3> gimpoAllTops=new List<Vector3>();
    readonly List<Passage> gimpoTopMouths=new List<Passage>();
    void AddGimpoPaidPath(string title,Vector3[] points){
        var r=new GameObject(title).AddComponent<StationWalkRoute>();r.transform.SetParent(station,false);r.exitNumber="환승";r.points=points;
        for(int k=1;k<points.Length;k++)if(Vector3.Distance(points[k-1],points[k])>.1f)passages.Add(new Passage(points[k-1],points[k]));
    }
    // Adjacent switchback flights prevent deep access shafts from piercing platforms above them.
    void BuildGimpoStairWell(Transform module,float shaftX,float arrivalX,float half,float floor,string label,int island,bool remember){
        int flights=Mathf.CeilToInt((ConcourseY-floor)/7f);if(flights%2==0)flights++;
        float drop=(ConcourseY-floor)/flights,outer=half+(island==2?40:24),inner=half+(island==2?21:5);
        float laneOther=shaftX+(island==2?4.7f:(shaftX<0?-4.7f:4.7f));var points=new List<Vector3>();
        var stone=Mat("stair-stone",new Color(.70f,.70f,.68f),0,"granite",1);var yellow=Mat("tactile",new Color(.95f,.78f,.15f));var wall=Mat("station-wall",new Color(.90f,.90f,.87f));
        for(int f=0;f<flights;f++){
            float x=f%2==0?shaftX:laneOther,z0=f%2==0?-outer:-inner,z1=f%2==0?-inner:-outer;
            var a=module.TransformPoint(new Vector3(x,ConcourseY-f*drop,z0));var b=module.TransformPoint(new Vector3(x,ConcourseY-(f+1)*drop,z1));
            points.Add(a);points.Add(b);StairFlight(station,a,b,3.6f,stone,yellow);
            if(f==0){gimpoTopMouths.Add(new Passage(a,a+(b-a).normalized*4));gimpoAllTops.Add(a);if(remember)gimpoStairTops.Add(a);}
            if(f<flights-1){
                float outward=f%2==0?2.5f:-2.5f;float nextX=f%2==0?laneOther:shaftX;
                var c=module.TransformPoint(new Vector3(x,b.y,z1+outward));var d=module.TransformPoint(new Vector3(nextX,b.y,z1+outward));var e=module.TransformPoint(new Vector3(nextX,b.y,z1));
                points.Add(c);points.Add(d);points.Add(e);
                var paths=new List<Passage>{new Passage(b,c),new Passage(c,d),new Passage(d,e)};
                var mouths=new List<Passage>{new Passage(b,b+(a-b).normalized*3),new Passage(e,e+module.forward*(f%2==0?-3:3))};
                BuildPassageUnion(station,paths,mouths,b.y,stone,wall,false,1.8f,.02f,new List<Passage>{new Passage(e,e+module.forward*(f%2==0?-4:4))});
            }
        }
        var last=points[points.Count-1];var entry=module.TransformPoint(new Vector3(arrivalX,floor,-half+1));
        var join=module.TransformPoint(new Vector3(arrivalX,floor,-half-2.5f));
        var tail=new List<Passage>{new Passage(last,join),new Passage(join,entry)};
        BuildPassageUnion(station,tail,new List<Passage>{new Passage(last,last-module.forward*4),new Passage(entry,entry+module.forward*4)},floor,stone,wall,false,1.45f,.02f);
        points.Add(join);points.Add(entry);
        var record=new GameObject(label+" · 계단 보행").AddComponent<StationWalkRoute>();record.transform.SetParent(station,false);record.exitNumber=label;record.points=points.ToArray();
        Board(label+" ↓",station,points[0]+Vector3.up*3f,-module.forward,new Vector2(5,.55f),new Color(.10f,.24f,.45f),Color.white,.24f);
        Marker(points[0],label+" ↓");
    }
    static List<Vector3> RoundGimpoPath(Vector3[] input,float radius){
        var points=new List<Vector3>{input[0]};
        for(int i=1;i<input.Length-1;i++){
            var p=input[i];var before=p-input[i-1];var after=input[i+1]-p;
            if(before.magnitude<.1f||after.magnitude<.1f)continue;
            float r=Mathf.Min(radius,Mathf.Min(before.magnitude,after.magnitude)*.3f);
            var a=p-before.normalized*r;var b=p+after.normalized*r;points.Add(a);
            for(int k=1;k<=6;k++){float t=k/6f;points.Add((1-t)*(1-t)*a+2*(1-t)*t*p+t*t*b);}
        }
        points.Add(input[input.Length-1]);return points;
    }
    void LinkGimpoExits(){
        var stone=Mat("stair-stone",new Color(.70f,.70f,.68f),0,"granite",1);var wall=Mat("station-wall",new Color(.90f,.90f,.87f));var yellow=Mat("tactile",new Color(.95f,.78f,.15f));
        var paths=new List<Passage>();var mouths=new List<Passage>();var hub=new Vector3(110,GimpoB1,-496);
        for(int i=0;i<mappedPortals.Count;i++){
            var portal=mappedPortals[i];var entry=portal.transform.parent;
            var a=entry.TransformPoint(new Vector3(0,.14f,2.05f));var b=entry.TransformPoint(new Vector3(0,GimpoB1,2.05f-2*(.14f-GimpoB1)));
            CutEntranceGround(entry);StairFlight(entry,a,b,2.4f,stone,yellow);
            var landing=b-entry.forward*4;var corner=new Vector3(landing.x,GimpoB1,hub.z);
            var line=RoundGimpoPath(new[]{landing,corner,hub},5);
            line.Insert(0,b);line.Insert(0,a);
            var record=entry.gameObject.AddComponent<StationWalkRoute>();record.exitNumber=(i+1).ToString();record.points=line.ToArray();
            for(int k=2;k<line.Count;k++)paths.Add(new Passage(line[k-1],line[k]));
            mouths.Add(new Passage(b,b+entry.forward*4));portal.walkThrough=true;portal.destination=ConcourseSpawn;portal.facing=Vector3.forward;
            Board((i+1)+"번 출구 ↑",station,landing+Vector3.up*2.8f,-entry.forward,new Vector2(3,.5f),new Color(.12f,.12f,.12f),new Color(1f,.82f,.1f),.25f);
        }
        // B1 -> B2 concourse, aligned with the unpaid entrance instead of through a platform shaft.
        var stairTop=new Vector3(110,GimpoB1,-490);var foot=new Vector3(110,ConcourseY,-478);StairFlight(station,stairTop,foot,5.8f,stone,yellow);
        paths.Add(new Passage(hub,stairTop));
        mouths.Add(new Passage(stairTop,stairTop+Vector3.forward*4));
        var hallMouth=new Vector3(110,ConcourseY,-473);var lower=new List<Passage>{new Passage(foot,hallMouth)};
        BuildPassageUnion(station,lower,new List<Passage>{new Passage(foot,foot+Vector3.back*4),new Passage(hallMouth,hallMouth+Vector3.forward*4)},ConcourseY,stone,wall,false,2.9f,.02f);
        var r=new GameObject("B1 → B2 대합실").AddComponent<StationWalkRoute>();r.transform.SetParent(station,false);r.exitNumber="대합실";r.points=new[]{hub,stairTop,foot,hallMouth,ConcourseSpawn-Vector3.up*1.65f};
        // Domestic terminal corridor joins the B1 unpaid network and retains the airport's west connector.
        var top=AirportOrigin+new Vector3(-60,0,-16);var bottom=top+new Vector3(-2*(0-GimpoB1),GimpoB1,0);
        StairFlight(station,top,bottom,2.4f,stone,yellow);
        var airportPath=RoundGimpoPath(new[]{bottom,bottom+Vector3.left*5,new Vector3(440,GimpoB1,-496),hub},5);
        var airportPassages=new List<Passage>();for(int k=1;k<airportPath.Count;k++)airportPassages.Add(new Passage(airportPath[k-1],airportPath[k]));
        // Build airport + exits in one union to remove crossing walls at their shared hub.
        paths.AddRange(airportPassages);mouths.Add(new Passage(bottom,bottom+Vector3.right*4));
        mouths.Add(new Passage(InternationalAirportConnector,InternationalAirportConnector+Vector3.back*8));
        BuildPassageUnion(station,paths,mouths,GimpoB1,stone,wall,false,1.25f,.02f,new List<Passage>{new Passage(stairTop,stairTop+Vector3.forward*5)});
        var airportRecord=new GameObject("국내선 청사 연결 통로").AddComponent<StationWalkRoute>();airportRecord.transform.SetParent(station,false);airportRecord.exitNumber="국내선";airportPath.Insert(0,top);airportRecord.points=airportPath.ToArray();
        passages.AddRange(paths);BuildMovingWalkways();Physics.SyncTransforms();
    }
}}
