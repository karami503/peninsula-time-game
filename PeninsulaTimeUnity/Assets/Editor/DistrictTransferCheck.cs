using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace PeninsulaTime {
public static class DistrictTransferCheck {
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static int failures,walks,signs;
 static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,Flags).Invoke(o,args);}
 static void Set(object o,string name,object v){o.GetType().GetField(name,Flags).SetValue(o,v);}
 static T Get<T>(object o,string name){return (T)o.GetType().GetField(name,Flags).GetValue(o);}
 static void Check(bool ok,string text){if(!ok){failures++;Debug.LogError("DistrictTransferCheck: "+text);}}
 static bool Walk(GameController game,Transform eye,Vector3 target,string label){
  for(int n=0;n<14000;n++){
   var feet=eye.position-Vector3.up*1.65f;var delta=target-feet;delta.y=0;
   if(delta.magnitude<.065f){for(int k=0;k<20;k++)Call(game,"MoveWalkerStep",Vector3.zero,1f/60f);Check(Mathf.Abs(eye.position.y-1.65f-target.y)<.32f,label+" continuous floor");walks++;return true;}
   var step=delta.normalized*Mathf.Min(.07f,delta.magnitude);Call(game,"MoveWalkerStep",step,1f/60f);
   if((eye.position-Vector3.up*1.65f-feet).sqrMagnitude<.0000001f){Check(false,label+" blocked "+feet+" to "+target);return false;}
  }Check(false,label+" timeout");return false;
 }
 static void Guides(WorldBuilder world){
  var guides=world.root.GetComponentsInChildren<FloorGuideRoute>();Check(guides.Length>0,"floor guidance created");
  var panels=new List<Bounds>();
  foreach(var guide in guides){
   Check(guide.GetComponentsInChildren<Collider>().Length==0,"paint must never block path "+guide.destination);
   Check(guide.GetComponent<MeshFilter>().sharedMesh.vertexCount>0,"guide mesh present");
   foreach(var text in guide.GetComponentsInChildren<TextMesh>()){
    Check(Vector3.Dot(text.transform.forward,Vector3.down)>.99f,"text lies flat");
    var size=text.GetComponent<MeshRenderer>().localBounds.size;var scale=text.transform.localScale;
    Check(size.x*scale.x<=2.401f&&size.y*scale.y<=1.021f,"text fits floor panel "+text.text);signs++;
    var bounds=text.GetComponent<MeshRenderer>().bounds;bounds.Expand(.025f);
    foreach(var previous in panels)Check(!previous.Intersects(bounds),"floor labels must not overlap "+text.text);
    panels.Add(bounds);
   }
  }
 }
 static void OutboundHandoff(WorldBuilder world,GameController game,Transform eye){
  Call(game,"ClearRides");world.BuildDistrict(3,9);world.SetDistrictView(3,false);Set(game,"mode","district");
  var train=world.StationTrains.Find(t=>t.line.net!=null&&t.line.ahead.Length==0);Check(train!=null,"onward national-service fixture");if(train==null)return;
  var lineId=train.line.net.id;var stationId=train.line.net.stops[train.line.index].id;
  train.Begin(SubwayTrain.Approach+4f);Physics.SyncTransforms();var c=train.Active.cabin;float side=c.doorSide;
  eye.position=c.transform.TransformPoint(new Vector3(side*5.6f,c.floor,c.doors[3]))+Vector3.up*1.65f;Set(game,"grounded",true);Set(game,"cabin",null);
  var step=c.transform.TransformDirection(Vector3.left*(side*.06f));
  for(int i=0;i<110&&Get<Cabin>(game,"cabin")==null;i++)if(!(bool)Call(game,"TryEnterCabin",step))Call(game,"MoveWalkerStep",step,1f/60f);
  var boarded=Get<Cabin>(game,"cabin");Check(boarded!=null&&boarded.kind=="networkrail","real open door boards route beyond detailed districts");
  Check(world.NetworkHubStation!=null&&world.NetworkHubStation.id==stationId&&Get<StationJourney>(game,"stationJourney").line.id==lineId,"onward boarding retains real service and station");
 }
 public static void Run(){
  failures=walks=signs=0;TransitNetwork.Build(null);
  var cam=new GameObject("guide camera").AddComponent<Camera>();var world=new GameObject("guide world").AddComponent<WorldBuilder>();world.worldCamera=cam;
  var g=new GameObject("guide motor");g.SetActive(false);var game=g.AddComponent<GameController>();game.state=new GameState();game.state.era=9;
  var eye=new GameObject("guide eye").transform;Set(game,"world",world);Set(game,"eye",eye);Set(game,"viewCamera",cam);
  try {
   for(int district=0;district<4;district++){
    world.BuildDistrict(district,9);world.SetDistrictView(district,false);Set(game,"mode","district");Set(game,"cabin",null);Physics.SyncTransforms();Guides(world);
    foreach(var belt in world.root.GetComponentsInChildren<MovingWalkway>())belt.enabled=false;
    var portals=world.DistrictTransfers.ToArray();
    if(district<3)Check(portals.Length>0,"missing district services connected "+district);
    foreach(var portal in portals){
     eye.position=new Vector3(-3,WorldBuilder.ConcourseY+1.65f,-.8f);Set(game,"grounded",true);
     Walk(game,eye,new Vector3(portal.approach.x,WorldBuilder.ConcourseY,-.8f),portal.label+" cross concourse");
     Walk(game,eye,portal.approach,portal.label+" aisle");Walk(game,eye,portal.entry,portal.label+" transfer entry");
     Check(portal.Contains(eye.position-Vector3.up*1.65f),"actual walking reaches portal "+portal.label);
    }
    if(district==0){
     var p=world.DistrictTransfers.Find(x=>x.label=="신분당선");Check(p!=null,"Gangnam Shinbundang connected");
     if(p!=null){var id=p.stationId;var line=p.lineId;eye.position=p.entry+Vector3.up*1.65f;Call(game,"UpdateDistrictTransfers");Check(Get<string>(game,"mode")=="rail"&&world.NetworkHubStation.id==id&&Get<StationJourney>(game,"stationJourney").line.id==line,"walking portal enters matching station and service");}
    }
    if(district==1)Check(Array.Exists(portals,p=>p.label=="KTX")&&Array.Exists(portals,p=>p.label=="무궁화호"),"Seoul KTX/conventional links");
   }
   OutboundHandoff(world,game,eye);
  }catch(Exception e){Check(false,e.ToString());}
  finally{Object.DestroyImmediate(g);Object.DestroyImmediate(eye.gameObject);Object.DestroyImmediate(world.gameObject);Object.DestroyImmediate(cam.gameObject);}
  Debug.Log("DistrictTransferCheck: "+(failures==0?"passed":failures+" failed")+"; "+walks+" walking legs; "+signs+" fitted floor labels");EditorApplication.Exit(failures==0?0:1);
 }
}}
