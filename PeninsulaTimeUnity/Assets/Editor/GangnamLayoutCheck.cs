using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;

namespace PeninsulaTime {
    // Physical acceptance test of the reviewed side-platform topology. All stairs,
    // bypass aisles and door crossings use the normal player movement methods.
    public static class GangnamLayoutCheck {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        const float Dt=1f/60f;
        static int failures,legs,boardings;
        static float metres;
        static object Call(object o,string method,params object[] args)=>o.GetType().GetMethod(method,Flags).Invoke(o,args);
        static T Get<T>(object o,string field)=>(T)o.GetType().GetField(field,Flags).GetValue(o);
        static void Set(object o,string field,object value)=>o.GetType().GetField(field,Flags).SetValue(o,value);
        static void Check(bool ok,string reason){if(!ok){failures++;Debug.LogError("GangnamLayoutCheck: "+reason);}}
        static Vector3 Feet(Transform eye)=>eye.position-Vector3.up*1.65f;
        static void Start(GameController game,Transform eye,Vector3 at){Set(game,"cabin",null);Set(game,"grounded",true);Set(game,"verticalSpeed",0f);eye.position=at+Vector3.up*1.65f;}
        static bool Walk(GameController game,Transform eye,Vector3 target,string name){
            int stalled=0;
            for(int n=0;n<10000;n++){
                var feet=Feet(eye);var delta=target-feet;delta.y=0;
                if(delta.magnitude<.065f){
                    for(int k=0;k<25;k++)Call(game,"MoveWalkerStep",Vector3.zero,Dt);
                    bool floor=Mathf.Abs(Feet(eye).y-target.y)<.32f;Check(floor,name+" wrong floor "+Feet(eye)+" expected "+target);legs++;return floor;
                }
                Call(game,"MoveWalkerStep",delta.normalized*Mathf.Min(.06f,delta.magnitude),Dt);
                float moved=Vector3.Distance(feet,Feet(eye));metres+=moved;stalled=moved<.0005f?stalled+1:0;
                if(stalled<15)continue;
                RaycastHit hit;Physics.SphereCast(feet+Vector3.up*.85f,.3f,delta.normalized,out hit,.5f);
                Check(false,name+" blocked at "+Feet(eye)+" toward "+target+" by "+(hit.collider?hit.collider.name:"floor/headroom"));return false;
            }
            Check(false,name+" timeout");return false;
        }
        static void Geometry(WorldBuilder world){
            Check(world.StationTrains.Count==2&&world.GangnamPlatformRoutes.Count==2,"two separately accessible directions");
            Check(world.root.transform.Find("강남역 · B1 대합실/강남 2호선 · B2 상대식 승강장 · 205m")!=null,"B1 concourse and B2 platform labels");
            foreach(var train in world.StationTrains){
                Check(Mathf.Abs(Mathf.Abs(train.trackX)-2.6f)<.01f,"tracks are central");
                Check(Mathf.Abs(world.PlatformHalfLength(train)*2-205)<.01f,"published 205 m platform length");
                foreach(var consist in train.consists)Check(consist.cabin.doorSide==Mathf.Sign(train.trackX),"doors open out toward side platform");
            }
            foreach(float x in new[]{-9.3f,9.3f})foreach(float z in new[]{-100f,-70f,70f,100f}){
                RaycastHit hit;bool floor=Physics.Raycast(new Vector3(x,WorldBuilder.PlatformY+.2f,z),Vector3.down,out hit,.4f);
                Check(floor&&Mathf.Abs(hit.point.y-WorldBuilder.PlatformY)<.05f,"side platform supported through its full length at "+x+","+z);
            }
            RaycastHit middle;bool bed=Physics.Raycast(new Vector3(0,WorldBuilder.PlatformY+.1f,-70),Vector3.down,out middle,2);
            Check(bed&&middle.point.y<WorldBuilder.PlatformY-.8f,"no residual island between tracks");
            for(int side=0;side<2;side++){
                Vector3 facing;var spawn=world.PlatformSpawn(side,out facing);RaycastHit floor;
                Check(Mathf.Abs(spawn.x)>8&&Physics.Raycast(spawn,Vector3.down,out floor,2)&&Mathf.Abs(floor.point.y-WorldBuilder.PlatformY)<.05f,"arriving passenger spawns on side platform "+side);
                Check(Mathf.Sign(facing.x)==-Mathf.Sign(spawn.x),"arrival view looks toward train");
                Check(Mathf.Abs(world.PlatformAccess(0,side).x-(side==0?9.3f:-9.3f))<.01f,"direction-specific stair access");
            }
            var director=Array.Find(world.root.GetComponentsInChildren<TrafficDirector>(),d=>d.name=="역 보행자");
            Check(director!=null,"station pedestrians present");
            if(director!=null)foreach(var node in director.graph.nodes)if(node.y<WorldBuilder.ConcourseY-2)Check(Mathf.Abs(node.x)>=6.5f,"NPC route never enters central tracks");
            int directions=0;
            foreach(var guide in world.root.GetComponentsInChildren<FloorGuideRoute>())if(guide.destination.Contains("2호선")&&guide.destination.Contains("타는 곳"))directions++;
            Check(directions==2,"both directions have floor wayfinding");
            foreach(var text in world.StationTrains[0].transform.parent.GetComponentsInChildren<TextMesh>())Check(!text.text.Contains("김포공항"),"shared side-platform renderer uses Gangnam station name");
        }
        static void RoutesAndBoarding(WorldBuilder world,GameController game,Transform eye){
            for(int side=0;side<world.GangnamPlatformRoutes.Count;side++){
                var route=world.GangnamPlatformRoutes[side];Start(game,eye,route.points[0]);
                foreach(var point in route.points)if(!Walk(game,eye,point,route.exitNumber+" down"))return;
                var train=world.StationTrains[side];var original=train.line;
                // Keep this local-collider test in the current scene; national onward
                // handoff is independently exercised by DistrictTransferCheck.
                var fixture=original;fixture.net=null;fixture.ahead=new[]{2};fixture.destination=2;train.line=fixture;
                try{
                    train.Begin(SubwayTrain.Approach+4f);Physics.SyncTransforms();var c=train.Active.cabin;float sign=c.doorSide,door=c.doors[3];
                    var approach=c.transform.TransformPoint(new Vector3(sign*3.9f,c.floor,door));
                    Walk(game,eye,new Vector3(approach.x,WorldBuilder.PlatformY,-24),"door aisle");Walk(game,eye,approach,"door alignment");
                    var step=c.transform.TransformDirection(Vector3.left*(sign*.06f));
                    for(int n=0;n<100&&Get<Cabin>(game,"cabin")==null;n++)if(!(bool)Call(game,"TryEnterCabin",step))Call(game,"MoveWalkerStep",step,Dt);
                    bool boarded=Get<Cabin>(game,"cabin")==c;Check(boarded,"walk through open door on side "+side);
                    if(boarded){
                        boardings++;for(int n=0;n<40&&Get<Cabin>(game,"cabin")!=null;n++)Call(game,"MoveInCabin",-step);
                        Check(Get<Cabin>(game,"cabin")==null,"walk back out of side "+side);
                        RaycastHit floor;Check(Physics.Raycast(Feet(eye)+Vector3.up*.15f,Vector3.down,out floor,.4f),"alighting remains supported");
                    }
                    Call(game,"ClearRides");
                    Walk(game,eye,approach,"retreat onto platform");Walk(game,eye,new Vector3(approach.x,WorldBuilder.PlatformY,-24),"return aisle");Walk(game,eye,route.points[route.points.Length-1],"return route");
                    for(int k=route.points.Length-2;k>=0;k--)if(!Walk(game,eye,route.points[k],route.exitNumber+" up"))return;
                }finally{Call(game,"ClearRides");train.line=original;train.Begin(SubwayTrain.Approach+4f);}
            }
            Check(boardings==2,"both side platforms physically board and alight");
            var portal=world.DistrictTransfers.Find(p=>p.label=="신분당선");Check(portal!=null,"Shinbundang transfer connection remains");
            if(portal!=null){
                Walk(game,eye,new Vector3(-3,WorldBuilder.ConcourseY,-.8f),"transfer starting aisle");
                Walk(game,eye,new Vector3(portal.approach.x,WorldBuilder.ConcourseY,-.8f),"transfer cross concourse");Walk(game,eye,portal.approach,"transfer aisle");Walk(game,eye,portal.entry,"transfer entrance");
                Check(portal.Contains(Feet(eye)),"walking reaches Shinbundang transfer");
            }
        }
        public static void Run(){
            failures=legs=boardings=0;metres=0;TransitNetwork.Build(null);
            var camera=new GameObject("Gangnam check camera").AddComponent<Camera>();var world=new GameObject("Gangnam check world").AddComponent<WorldBuilder>();world.worldCamera=camera;
            var host=new GameObject("Gangnam check player");host.SetActive(false);var game=host.AddComponent<GameController>();game.state=new GameState();game.state.era=9;
            var eye=new GameObject("Gangnam check eye").transform;Set(game,"world",world);Set(game,"eye",eye);Set(game,"viewCamera",camera);Set(game,"mode","district");
            try{
                world.BuildDistrict(0,9);world.SetDistrictView(0,false);Physics.SyncTransforms();Geometry(world);
                foreach(var belt in world.root.GetComponentsInChildren<MovingWalkway>())belt.enabled=false;
                RoutesAndBoarding(world,game,eye);
            }catch(Exception e){Check(false,e.ToString());}
            finally{Object.DestroyImmediate(host);Object.DestroyImmediate(eye.gameObject);Object.DestroyImmediate(world.gameObject);Object.DestroyImmediate(camera.gameObject);}
            Debug.Log("GangnamLayoutCheck: "+(failures==0?"passed":failures+" failed")+"; "+legs+" walking legs; "+boardings+" board/alight pairs; "+metres.ToString("F1")+" m");EditorApplication.Exit(failures==0?0:1);
        }
    }
}
