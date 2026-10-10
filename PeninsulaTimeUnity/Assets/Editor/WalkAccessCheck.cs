using System.Reflection;
using UnityEngine;
using UnityEditor;
namespace PeninsulaTime {
public static class WalkAccessCheck {
static int failures; const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
static void Check(bool ok,string message){if(!ok){failures++;Debug.LogError("WalkAccessCheck: "+message);}}
static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,Flags).Invoke(o,args);}
static void Walk(GameController game,Transform eye,Vector3 target,string name){
 for(int n=0;n<12000;n++){
  var feet=eye.position-Vector3.up*1.65f;var delta=target-feet;delta.y=0;
  if(delta.magnitude<.09f){for(int settle=0;settle<60;settle++)Call(game,"MoveWalkerStep",Vector3.zero,1f/60f);feet=eye.position-Vector3.up*1.65f;Check(Mathf.Abs(feet.y-target.y)<.4f,name+" floor "+feet+" expected "+target);return;}
  var step=delta.normalized*Mathf.Min(.07f,delta.magnitude);Call(game,"MoveWalkerStep",step,1f/60f);
  if(Vector3.Distance(eye.position-Vector3.up*1.65f,feet)<.001f){
   RaycastHit hit;Physics.SphereCast(feet+Vector3.up*.85f,.3f,step.normalized,out hit,.15f);
   Check(false,name+" blocked at "+feet+" toward "+target+" by "+(hit.collider==null?"floor/headroom":hit.collider.name+" at "+hit.collider.bounds.center+" parent "+hit.collider.transform.parent.name));return;
  }
 }
 Check(false,name+" walking timeout");
}
static void Set(object o,string name,object v){o.GetType().GetField(name,Flags).SetValue(o,v);}
public static void Run(){
var cam=new GameObject("camera").AddComponent<Camera>();
var world=new GameObject("world").AddComponent<WorldBuilder>();world.worldCamera=cam;
var g=new GameObject("player");g.SetActive(false);var game=g.AddComponent<GameController>();game.state=new GameState();game.cardBalance=0;Set(game,"world",world);
var eye=new GameObject("eye").transform;Set(game,"eye",eye);
world.BuildDistrict(0);
foreach(var bus in world.root.GetComponentsInChildren<TrafficVehicle>()){
 if(!bus.Bus||bus.cabin==null)continue;
 // A distant isolated road patch keeps this test focused on the real vehicle collider, at every heading.
 var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.position=new Vector3(1500,-.1f,1500);ground.transform.localScale=new Vector3(40,.2f,40);
 bus.transform.position=new Vector3(1500,.06f,1500);bus.doors.Set(1);Physics.SyncTransforms();
 foreach(float door in bus.cabin.doors){
 var c=bus.cabin;var start=c.transform.TransformPoint(new Vector3(3.5f,0,door));start.y=0;
 eye.position=start+Vector3.up*1.65f;Set(game,"cabin",null);Set(game,"grounded",true);
 bool boarded=false;
 for(int i=0;i<65;i++){var step=c.transform.TransformDirection(Vector3.left*.06f);if((bool)Call(game,"TryEnterCabin",step)){boarded=true;break;}Call(game,"MoveWalkerStep",step,1f/60f);}
 var local=c.transform.InverseTransformPoint(eye.position-Vector3.up*1.65f);
 Debug.Log("WalkAccessCheck bus "+bus.name+" door "+door+" reached "+local+" boarded "+boarded);
 Check(boarded,"walk from pavement through bus door "+door);
 }
 Object.DestroyImmediate(ground);break;
}
Set(game,"cabin",null);
foreach(int district in new[]{0,1,2}){
 world.BuildDistrict(district);Physics.SyncTransforms();
 Debug.Log("WalkAccessCheck district "+district+": walking "+world.root.GetComponentsInChildren<StationWalkRoute>().Length+" exits both ways");
 foreach(var route in world.root.GetComponentsInChildren<StationWalkRoute>()){

  eye.position=route.points[0]+route.transform.forward*.8f+Vector3.up*1.65f;Set(game,"grounded",true);
  foreach(var point in route.points)Walk(game,eye,point,route.name+" down");
  for(int k=route.points.Length-2;k>=0;k--)Walk(game,eye,route.points[k],route.name+" up");
  Walk(game,eye,route.points[0]+route.transform.forward*.8f,route.name+" street");
 }
 if(world.FareGates.Count>0){
  var gate=world.FareGates[3];var top=gate.transform.position;
  eye.position=top+Vector3.back*.95f+Vector3.up*1.65f;Set(game,"nextAutoTap",-1f);Set(game,"farePaid",false);int balance=game.cardBalance;
  Call(game,"WalkThroughFareGate",Vector3.forward*.06f);Check(!gate.blocker.enabled,"walking auto taps fare gate");
  Call(game,"WalkThroughFareGate",Vector3.forward*.06f);Check(game.cardBalance==balance&&balance==0&&gate.IsOpen,"zero-balance entry and repeated taps do not charge");
 }
 // Gangnam's two actual stair routes were walked above; neither lands at the old island centre.
 if(district==0)Check(world.GangnamPlatformRoutes.Count==2,"Gangnam both direction stairs checked as walking routes");
 int islands=district==0?0:district==3?5:district==2?2:1;
 for(int i=0;i<islands;i++){
  float x=world.IslandCentre(i);float y=world.IslandFloor(i);float end=2+Mathf.Max(14,(WorldBuilder.ConcourseY-y)*2);
  var top=new Vector3(x-2,WorldBuilder.ConcourseY,1);var bottom=new Vector3(x-2,y,end+1);
  if(i==0)eye.position=top+Vector3.up*1.65f;else Walk(game,eye,top,"walking transfer between lines");Set(game,"grounded",true);
  Walk(game,eye,bottom,"platform "+i+" down");Walk(game,eye,top,"platform "+i+" up");
 }
 if(district==2)Check(Mathf.Abs(world.StationTrains[0].transform.position.y-world.StationTrains[2].transform.position.y)>8,"Hongdae lines on distinct levels");
}
Debug.Log("WalkAccessCheck: "+(failures==0?"passed":failures+" failed"));EditorApplication.Exit(failures==0?0:1);
}}
}
