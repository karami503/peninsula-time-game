using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PeninsulaTime {
    public static class NetworkTransferCheck {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static int failures,walked,boarded;
        static void Check(bool ok,string text){if(!ok){failures++;Debug.LogError("NetworkTransferCheck: "+text);}}
        static object Call(object o,string method,params object[] values){return o.GetType().GetMethod(method,Flags).Invoke(o,values);}
        static void Set(object o,string field,object value){o.GetType().GetField(field,Flags).SetValue(o,value);}
        static T Get<T>(object o,string field){return (T)o.GetType().GetField(field,Flags).GetValue(o);}
        static NetStation Find(string name,string shortName=null,string region=null){
            return TransitNetwork.Stations.Find(s=>StationTransferCatalog.StationKey(s.name)==name&&s.lines.Exists(l=>StationTransferCatalog.Rail(l)&&(shortName==null||l.shortName==shortName)&&(region==null||l.region==region)));
        }
        static bool Walk(GameController game,Transform eye,Vector3 target,string label){
            for(int n=0;n<10000;n++){
                var feet=eye.position-Vector3.up*1.65f;var delta=target-feet;delta.y=0;
                if(delta.magnitude<.07f){
                    for(int k=0;k<10;k++)Call(game,"MoveWalkerStep",Vector3.zero,1f/60f);
                    Check(Mathf.Abs(eye.position.y-1.65f-target.y)<.25f,label+" floor continuous");walked++;return true;
                }
                var step=delta.normalized*Mathf.Min(.1f,delta.magnitude);Call(game,"MoveWalkerStep",step,1f/60f);
                if((eye.position-Vector3.up*1.65f-feet).sqrMagnitude<.0000005f){
                    Check(false,label+" obstructed at "+feet+" toward "+target);return false;
                }
            }
            Check(false,label+" timeout");return false;
        }
        static void Catalog(){
            foreach(var station in TransitNetwork.Stations){
                if(!station.lines.Exists(StationTransferCatalog.Rail))continue;
                var services=StationTransferCatalog.Services(station);var ids=new HashSet<string>();
                foreach(var pair in services){
                    Check(ids.Add(pair.Value.id),station.id+" no duplicate service ID");
                    Check(pair.Value.stops.Contains(pair.Key)&&StationTransferCatalog.SameStation(station,pair.Key),station.id+" transfer serves this physical station");
                }
                foreach(var line in station.lines)if(StationTransferCatalog.Rail(line))Check(services.Exists(s=>s.Value==line),station.id+" own service retained "+line.id);
            }
            var seoul=Find("서울");var routes=StationTransferCatalog.Services(seoul);
            Check(routes.Exists(p=>p.Value.kind=="ktx")&&routes.Exists(p=>p.Value.kind=="mugunghwa")&&routes.Exists(p=>p.Value.shortName=="1호선")&&routes.Exists(p=>p.Value.shortName=="공항철도"),"Seoul all rail families");
            Check(StationTransferCatalog.Services(Find("이수","4호선")).Exists(p=>p.Value.shortName=="7호선"),"총신대입구(이수) → 이수 7호선");
            var far=new NetStation{name=seoul.name,lon=129,lat=35};Check(!StationTransferCatalog.SameStation(seoul,far),"same name in another city never connects");
            var gangnam=Find("강남","2호선");Check(StationTransferCatalog.Services(gangnam).Exists(p=>p.Value.shortName=="신분당선"),"Gangnam Shinbundang transfer");
        }
        static void WalkHub(WorldBuilder world,GameController game,Transform eye,NetStation station,NetLine line){
            var initial=world.BuildNetworkStation(station,line,0);Physics.SyncTransforms();Set(game,"stationJourney",initial);Set(game,"cabin",null);
            int initialCount=world.root.GetComponentsInChildren<NetworkPlatformGeometry>().Length;
            Check(initialCount==world.NetworkTransfers.Count,"only current interchange platforms loaded at "+station.name);
            foreach(var pair in StationTransferCatalog.Services(station))for(int direction=0;direction<2;direction++)
                if(StationTransferCatalog.CanDepart(pair.Key,pair.Value,direction))Check(world.NetworkTransfers.Exists(r=>r.line.id==pair.Value.id&&r.direction==direction),station.name+" direction available "+pair.Value.name+"/"+direction);
            var home=world.NetworkTransfers[0].hallPoint;eye.position=home+Vector3.up*1.65f;Set(game,"grounded",true);
            foreach(var route in world.NetworkTransfers){
                bool reached=Walk(game,eye,route.hallPoint,route.line.name+" along hall");
                foreach(var point in route.Points)reached=Walk(game,eye,point,route.line.name+" down to platform")&&reached;
                Check(world.NetworkServiceAt(route.platformPoint,initial)==route.service,"selection follows stationary platform "+route.line.name);
                if(route.service.train!=null){var old=route.service.train.position;route.service.train.position+=Vector3.forward*700;
                    Check(world.NetworkServiceAt(route.platformPoint,initial)==route.service,"selection stable when train departs");route.service.train.position=old;}
                if(reached&&route.service.doors!=null){
                    var service=route.service;service.startDelay=0;service.clock=0;service.Step(0);service.doors.Set(1);Physics.SyncTransforms();
                    var c=service.doors.cabin;float door=c.doors[c.doors.Length-1];
                    var outside=c.transform.TransformPoint(new Vector3(4.5f,c.floor,door));outside.y=route.platformPoint.y;
                    Walk(game,eye,outside,route.line.name+" door approach");
                    for(int k=0;k<100&&Get<Cabin>(game,"cabin")==null;k++){
                        var step=c.transform.TransformDirection(Vector3.left*.06f);
                        if(!(bool)Call(game,"TryEnterCabin",step))Call(game,"MoveWalkerStep",step,1f/60f);
                    }
                    Check(Get<Cabin>(game,"cabin")==c,route.line.name+" board by walking through open door");
                    if(Get<Cabin>(game,"cabin")==c){boarded++;for(int k=0;k<100&&Get<Cabin>(game,"cabin")!=null;k++)Call(game,"MoveInCabin",c.transform.TransformDirection(Vector3.right*.06f));}
                    Check(Get<Cabin>(game,"cabin")==null,route.line.name+" alight by walking out");
                    Call(game,"UpdateNetworkJourney");Check(!service.started,route.line.name+" immediate alight resumes current-stop timetable");
                    service.occupied=false;
                }
                for(int i=route.Points.Length-1;i>=0;i--)Walk(game,eye,route.Points[i],route.line.name+" return to hall");
            }
            Check(world.root.GetComponentsInChildren<NetworkPlatformGeometry>().Length<=initialCount+world.NetworkTransfers.Count*3,"future geometry bounded by boarded services");
        }
        static void ArriveAndTransfer(WorldBuilder world,GameController game,Transform eye,int destinationIndex){
            var line=TransitNetwork.Lines.Find(l=>l.shortName=="2호선"&&l.loop);var station=line.stops.Find(s=>StationTransferCatalog.StationKey(s.name)=="강남");
            var journey=world.BuildNetworkStation(station,line,0);world.PrepareNetworkJourney(journey);Physics.SyncTransforms();
            var oldRoot=world.root;var c=journey.doors.cabin;
            Set(game,"stationJourney",journey);Set(game,"stationAnnounced",0);Set(game,"cabin",c);Set(game,"cabinFeet",new Vector3(c.halfWidth-.4f,c.floor,c.doors[c.doors.Length-1]));Set(game,"cabinYaw",0f);
            journey.Board();
            for(int step=0;step<30000&&journey.index<destinationIndex;step++)journey.Step(1f/30f);
            Check(journey.index==destinationIndex,"ride reaches downstream index "+destinationIndex);
            journey.doors.Set(1);journey.speed=0;journey.wait=10;Call(game,"CarryInCabin");Physics.SyncTransforms();
            var arrived=journey.Current;
            Call(game,"UpdateNetworkJourney");Check(world.root==oldRoot&&Get<Cabin>(game,"cabin")==c,"arrival keeps occupied cabin alive");
            for(int k=0;k<120&&Get<Cabin>(game,"cabin")!=null;k++)Call(game,"MoveInCabin",c.transform.TransformDirection(Vector3.right*.06f));
            Check(Get<Cabin>(game,"cabin")==null,"downstream exit through open door");
            var localFeet=eye.position-Vector3.up*1.65f-journey.origin-Vector3.forward*(destinationIndex*StationJourney.Spacing);
            Call(game,"UpdateNetworkJourney");Physics.SyncTransforms();
            var next=Get<StationJourney>(game,"stationJourney");
            Check(world.root!=oldRoot&&world.NetworkHubStation==arrived&&next.index==0&&next.Current==arrived,"alighting loads actual station interchange");
            Check((eye.position-Vector3.up*1.65f-next.origin-localFeet).magnitude<.05f,"rebuild preserves exact platform foot position");
            foreach(var pair in StationTransferCatalog.Services(arrived))Check(world.NetworkTransfers.Exists(r=>r.line.id==pair.Value.id),"arrival transfer added "+pair.Value.name);
            Walk(game,eye,world.NetworkTransfers[0].rampBottom,"arrival platform to ramp");Walk(game,eye,world.NetworkTransfers[0].rampTop,"arrival ramp");Walk(game,eye,world.NetworkTransfers[0].hallPoint,"arrival hall");
        }
        public static void Run(){
            failures=walked=boarded=0;TransitNetwork.Build(null);Catalog();
            var camera=new GameObject("transfer test camera").AddComponent<Camera>();var world=new GameObject("transfer test world").AddComponent<WorldBuilder>();world.worldCamera=camera;
            var obj=new GameObject("transfer test player");obj.SetActive(false);var game=obj.AddComponent<GameController>();game.state=new GameState();game.state.era=9;game.state.wallet=0;
            var eye=new GameObject("transfer test eye").transform;Set(game,"world",world);Set(game,"eye",eye);Set(game,"viewCamera",camera);
            try{
                foreach(var spec in new[]{new[]{"강남","2호선","수도권"},new[]{"서울","공항철도","수도권"},new[]{"서면","부산 1호선","부산"}}){
                    var station=Find(spec[0],spec[1],spec[2]);Check(station!=null,"fixture "+spec[0]);if(station==null)continue;
                    WalkHub(world,game,eye,station,station.lines.Find(l=>l.shortName==spec[1]));
                }
                ArriveAndTransfer(world,game,eye,1);ArriveAndTransfer(world,game,eye,3);
            }catch(Exception e){Check(false,e.ToString());}
            finally{Object.DestroyImmediate(obj);Object.DestroyImmediate(eye.gameObject);Object.DestroyImmediate(world.gameObject);Object.DestroyImmediate(camera.gameObject);StationAreaData.ClearTileCache();}
            Debug.Log("NetworkTransferCheck: "+(failures==0?"passed":failures+" failed")+"; "+walked+" connected walking legs; "+boarded+" door board/alight pairs");EditorApplication.Exit(failures==0?0:1);
        }
    }
}
