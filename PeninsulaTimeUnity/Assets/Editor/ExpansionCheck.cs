using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
namespace PeninsulaTime {
public static class ExpansionCheck {
static int failures,exitRoundTrips; const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
static void Check(bool ok,string message){if(!ok){failures++;Debug.LogError("ExpansionCheck: "+message);}}
static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,Flags).Invoke(o,args);}
static void Set(object o,string name,object v){o.GetType().GetField(name,Flags).SetValue(o,v);}
static bool Walk(GameController game,Transform eye,Vector3 target,string name){
 for(int n=0;n<10000;n++){var feet=eye.position-Vector3.up*1.65f;var d=target-feet;d.y=0;
 if(d.magnitude<.1f){for(int s=0;s<60;s++)Call(game,"MoveWalkerStep",Vector3.zero,1f/60);bool height=Mathf.Abs(eye.position.y-1.65f-target.y)<.4f;Check(height,name+" wrong height "+(eye.position.y-1.65f)+" expected "+target.y);return height;}
 Call(game,"MoveWalkerStep",d.normalized*Mathf.Min(.07f,d.magnitude),1f/60);
 if(Vector3.Distance(eye.position-Vector3.up*1.65f,feet)<.001f){Check(false,name+" blocked "+feet+" target "+target);
 var step=d.normalized*Mathf.Min(.07f,d.magnitude);
 foreach(var hit in Physics.SphereCastAll(feet+Vector3.up*.85f,.3f,step.normalized,step.magnitude+.05f))Debug.LogError("Expansion blocker "+hit.collider.name+" hit "+hit.point+" normal "+hit.normal+" bounds "+hit.collider.bounds);
 foreach(var sample in new[]{feet,feet+step})foreach(var hit in Physics.RaycastAll(sample+Vector3.up*1.35f,Vector3.down,61.25f))Debug.LogError("Expansion ground "+sample+" / "+hit.collider.name+" hit "+hit.point+" normal "+hit.normal);
 return false;}}
 Check(false,name+" timeout");return false;}
// Keep using the real player motor for the whole platform -> hall -> street -> platform trip.
// Exit waypoints identify bends; they do not move the player or bypass collision/height checks.
static void CheckExits(WorldBuilder world,GameController game,Transform eye,string name){
 float hall=world.NetworkHallY,right=world.NetworkHallRight;
 var sourceIds=new HashSet<string>();if(world.NetworkArea!=null)foreach(var e in world.NetworkArea.entrances)sourceIds.Add(e.id);
 Check(world.NetworkMappedExits.Count==sourceIds.Count,name+" all source exits connected");
 // The moving belt's speed is covered by TransitSpeedCheck. These walks exercise both
 // directions of the ordinary passage without motor assistance hiding a broken floor.
 foreach(var belt in world.root.GetComponentsInChildren<MovingWalkway>())belt.enabled=false;
 if(sourceIds.Count>0){
  foreach(var sign in world.root.GetComponentsInChildren<TextMesh>())Check(sign.text!="서쪽 출구 ←"&&sign.text!="동쪽 출구 →",name+" removed exit not advertised");
  foreach(var route in world.NetworkMappedExits){
   string label=name+" mapped exit "+route.exitNumber+" ("+route.osmId+")";
   var source=world.NetworkArea.entrances.Find(e=>e.id==route.osmId);
   Check(source!=null,label+" source retained");
   if(source!=null){var delta=source.position-route.surfacePoint;delta.y=0;Check(delta.magnitude<.001f,label+" surface coordinate retained");}
   Check(route.points!=null&&route.points.Length>=3,label+" connected route");if(route.points==null||route.points.Length<3)continue;
   bool reached=Walk(game,eye,new Vector3(route.hallPoint.x,hall,-75),label+" hall approach");
   reached=Walk(game,eye,route.hallPoint,label+" hall connection")&&reached;
   if(!reached)return;
   for(int i=route.points.Length-2;i>=0;i--)if(!Walk(game,eye,route.points[i],label+" out segment "+i))return;
   var outward=route.points[0]-route.points[1];outward.y=0;outward.Normalize();
   if(!Walk(game,eye,route.surfacePoint+outward*.8f,label+" street landing"))return;
   for(int i=0;i<route.points.Length;i++)if(!Walk(game,eye,route.points[i],label+" in segment "+i))return;
   if(!Walk(game,eye,new Vector3(route.hallPoint.x,hall,-75),label+" return concourse"))return;
   exitRoundTrips++;
  }
  // Replaced schematic exit mouths must be closed. Walk up to each former opening,
  // then press against it; falling off the hall or crossing the wall is a regression.
  foreach(float x in new[]{-10f,right})foreach(float z in new[]{-69f,-81f}){
   float side=x<0?-1:1;float inside=x-side*1.2f;
   if(!Walk(game,eye,new Vector3(inside,hall,-75),name+" former exit approach")||!Walk(game,eye,new Vector3(inside,hall,z),name+" former exit wall"))return;
   for(int i=0;i<45;i++)Call(game,"MoveWalkerStep",Vector3.right*side*.07f,1f/60);
   var feet=eye.position-Vector3.up*1.65f;
   bool safe=(feet.x-x)*side<-.1f&&Mathf.Abs(feet.y-hall)<.4f;
   Check(safe,name+" former exit cannot drop player "+feet);if(!safe)return;
   if(!Walk(game,eye,new Vector3(inside,hall,z),name+" leave former exit wall")||!Walk(game,eye,new Vector3(inside,hall,-75),name+" return hall spine"))return;
  }
 }else{
  // Stations without sourced entrances still need all four usable fallback exits.
  foreach(float x in new[]{-10f,right})foreach(float z in new[]{-69f,-81f}){
   float side=x<0?-1:1;var inside=new Vector3(x-side*3,hall,z);var mouth=new Vector3(x,hall,z);
   var street=new Vector3(x+side*(Mathf.Abs(hall)*3+8),0,z);string label=name+" fallback exit "+x+"/"+z;
   if(!Walk(game,eye,new Vector3(inside.x,hall,-75),label+" hall approach")||!Walk(game,eye,inside,label+" approach")||!Walk(game,eye,mouth,label+" mouth")||!Walk(game,eye,street,label+" street"))return;
   if(!Walk(game,eye,street+Vector3.right*side*.8f,label+" street landing")||!Walk(game,eye,mouth,label+" return mouth")||!Walk(game,eye,inside,label+" return approach")||!Walk(game,eye,new Vector3(inside.x,hall,-75),label+" return hall"))return;
   exitRoundTrips++;
  }
 }
 var home=world.NetworkTransfers[0];
 Walk(game,eye,home.hallPoint,name+" street return transfer hall");Walk(game,eye,home.rampTop,name+" street return ramp top");Walk(game,eye,home.rampBottom,name+" street return ramp bottom");Walk(game,eye,home.platformPoint,name+" street return platform");
}
static void CheckStation(WorldBuilder world,GameController game,Transform eye,NetStation at,string label){
 Check(at!=null,label+" fixture exists");if(at==null)return;
 Debug.Log("Expansion station "+label+" "+at.name+" "+at.id);var line=at.lines.Find(l=>l.kind=="metro");var service=world.BuildNetworkStation(at,line,0);Physics.SyncTransforms();
 Set(game,"stationJourney",service);Set(game,"cabin",null);Set(game,"grounded",true);
 var home=world.NetworkTransfers[0];eye.position=home.platformPoint+Vector3.up*1.65f;
 Walk(game,eye,home.rampBottom,label+" platform");Walk(game,eye,home.rampTop,label+" concourse ramp");Walk(game,eye,home.hallPoint,label+" concourse");
 foreach(var route in world.NetworkTransfers){
  Walk(game,eye,route.hallPoint,label+" transfer hall");Walk(game,eye,route.rampTop,label+" transfer ramp top");Walk(game,eye,route.rampBottom,label+" transfer platform");Walk(game,eye,route.rampTop,label+" transfer return");Walk(game,eye,route.hallPoint,label+" transfer return hall");
 }
 CheckExits(world,game,eye,label);
}
public static void Run(){
 failures=exitRoundTrips=0;TransitNetwork.Build(null);
 foreach(var l in TransitNetwork.Lines)if(l.id.StartsWith("cw-bus-")){
  Check(l.stopShapeIndices.Length==l.stops.Count,"ordered BIS stop matching "+l.id);
  for(int i=1;i<l.stopShapeIndices.Length;i++)Check(l.stopShapeIndices[i]>=l.stopShapeIndices[i-1]&&l.stopShapeIndices[i]<l.shapes[0].Count,"BIS stop order stays on outbound/return shape "+l.id);
 }
 var cam=new GameObject("camera").AddComponent<Camera>();var world=new GameObject("world").AddComponent<WorldBuilder>();world.worldCamera=cam;
 var player=new GameObject("player");player.SetActive(false);var game=player.AddComponent<GameController>();game.state=new GameState();game.state.wallet=0;Set(game,"world",world);Set(game,"mode","rail");
 var eye=new GameObject("eye").transform;Set(game,"eye",eye);world.viewer=eye;
 foreach(var grade in new[]{"underground","surface","elevated"}){
  var at=TransitNetwork.Stations.Find(s=>s.grade==grade&&s.lines.Exists(l=>l.kind=="metro"));
  CheckStation(world,game,eye,at,grade);
  // Preserve coverage of the fallback geometry as mapped fixtures become more complete.
  if(at!=null&&world.NetworkMappedExits.Count>0){
   var fallback=TransitNetwork.Stations.Find(s=>s.grade==grade&&s.lines.Exists(l=>l.kind=="metro")&&StationAreaData.ForStation(s)!=null&&StationAreaData.ForStation(s).entrances==0);
   if(fallback!=null)CheckStation(world,game,eye,fallback,grade+" unmapped");
  }
 }
 Check(exitRoundTrips>0,"at least one complete hall/street return walk");
 var busline=TransitNetwork.Lines.Find(l=>l.id.StartsWith("cw-bus-")&&l.shortName=="3000")??TransitNetwork.Lines.Find(l=>l.id.StartsWith("cw-bus-")&&l.stops.Count>10);
 Check(busline.shapes.Count>0&&busline.shapes[0].Count>busline.stops.Count,"BIS road curves loaded with more points than stops");
 var bus=world.BuildNetworkStation(busline.stops[2],busline,0);bus.Step(0);Physics.SyncTransforms();
 var cabin=bus.doors.cabin;Check(Vector3.Dot(cabin.transform.forward,(bus.path[1]-bus.path[0]).normalized)>.98f,"bus heading follows BIS path");
 float door=cabin.doors[cabin.doors.Length-1];var start=cabin.transform.TransformPoint(new Vector3(cabin.halfWidth+1,0,door));start.y=.02f;
 eye.position=start+Vector3.up*1.65f;Set(game,"grounded",true);Set(game,"cabin",null);bool boarded=false;
 for(int i=0;i<80;i++){var step=-cabin.transform.right*.06f;if((bool)Call(game,"TryEnterCabin",step)){boarded=true;break;}Call(game,"MoveWalkerStep",step,1f/60);}
 Check(boarded,"walking onto Changwon bus");if(!bus.started)bus.Board();
 for(int i=0;i<60000&&!bus.Finished;i++){var before=bus.train.position;bus.Step(.05f);Check(Vector3.Distance(before,bus.train.position)<.8f,"continuous bus motion");if(bus.speed>.01f)Check(bus.doors.amount<.01f,"bus moving with closed doors");}
 bus.Step(2);Check(bus.index==3&&bus.Stopped,"bus arrives at three stops and opens doors");
 Debug.Log("ExpansionCheck: "+(failures==0?"passed":failures+" failed")+"; "+exitRoundTrips+" complete exit round trips");EditorApplication.Exit(failures==0?0:1);
}}}
