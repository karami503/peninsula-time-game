using System.Reflection;
using UnityEngine;
using UnityEditor;
namespace PeninsulaTime {
public static class StationClearanceCheck {
    static int failures;
    static void Check(bool ok,string reason){if(!ok){failures++;Debug.LogError("StationClearanceCheck: "+reason);}}
    public static void Run(){
        var camera=new GameObject("camera").AddComponent<Camera>();var world=new GameObject("world").AddComponent<WorldBuilder>();world.worldCamera=camera;
        for(int district=0;district<4;district++){
            world.BuildDistrict(district,9);Physics.SyncTransforms();
            if(district==0)foreach(var route in world.GangnamPlatformRoutes){
                for(int segment=1;segment<route.points.Length;segment++)for(float t=0;t<=1;t+=.025f){
                    var feet=Vector3.Lerp(route.points[segment-1],route.points[segment],t);
                    var body=new Bounds(feet+Vector3.up*1.1f,new Vector3(.45f,1.5f,.45f));
                    foreach(var renderer in world.root.GetComponentsInChildren<Renderer>()){
                        if(renderer.name!="승강장 조명"&&renderer.name!="도착 안내 화면"&&renderer.name!="방면 안내"&&renderer.name!="기둥"&&renderer.name!="승강장 기둥"&&renderer.name!="안내판")continue;
                        Check(!body.Intersects(renderer.bounds),"Gangnam "+route.exitNumber+" obstructed by "+renderer.name+" at "+feet);
                    }
                }
            }
            for(int island=0;island<(district==0||district==3?0:district==2?2:1);island++){
                float x=world.IslandCentre(island),y=world.IslandFloor(island),end=2+Mathf.Max(14,(WorldBuilder.ConcourseY-y)*2);
                foreach(float lane in new[]{-2f,2f})for(float t=0;t<=1;t+=.025f){
                    var feet=Vector3.Lerp(new Vector3(x+lane,WorldBuilder.ConcourseY,2),new Vector3(x+lane,y,end),t);
                    var body=new Bounds(feet+Vector3.up*1.1f,new Vector3(.45f,1.5f,.45f));
                    foreach(var renderer in world.root.GetComponentsInChildren<Renderer>()){
                        if(renderer.name!="승강장 조명"&&renderer.name!="도착 안내 화면"&&renderer.name!="방면 안내"&&renderer.name!="기둥")continue;
                        Check(!body.Intersects(renderer.bounds),district+" island "+island+" stair obstructed by "+renderer.name+" at "+feet);
                    }
                }
            }
            var belts=world.root.GetComponentsInChildren<MovingWalkway>();
            if(district==3)Check(belts.Length>=10,"Gimpo long corridor has opposing moving walkways");
            foreach(var belt in belts){
                var feet=belt.transform.position+Vector3.up*.026f;
                Check(Vector3.Dot(MovingWalkway.VelocityAt(feet),belt.transform.forward)>.8f,"standing on belt is carried forward");
                Check(MovingWalkway.VelocityAt(feet+Vector3.up*1).sqrMagnitude==0,"jumping is not carried by belt");
                Check(MovingWalkway.VelocityAt(feet+belt.transform.forward*(belt.length*.5f+1)).sqrMagnitude==0,"clear landing between belts");
            }
            Debug.Log("StationClearanceCheck district "+district+": "+belts.Length+" belts, both stair lanes checked");
        }
        var testBelt=world.root.GetComponentInChildren<MovingWalkway>();
        var player=new GameObject("test pedestrian");player.SetActive(false);var game=player.AddComponent<GameController>();var eye=new GameObject("eye").transform;
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;typeof(GameController).GetField("eye",flags).SetValue(game,eye);
        eye.position=testBelt.transform.position+Vector3.up*(1.65f+.03f);var before=eye.position;
        var move=typeof(GameController).GetMethod("MoveWalkerStep",flags);
        for(int i=0;i<120;i++)move.Invoke(game,new object[]{Vector3.zero,1f/60});
        Check(Vector3.Dot(eye.position-before,testBelt.transform.forward)>1.6f,"idle pedestrian actually moves with belt through normal collision motor");
        Debug.Log("StationClearanceCheck: "+(failures==0?"passed":failures+" failed"));EditorApplication.Exit(failures==0?0:1);
    }
}
}
