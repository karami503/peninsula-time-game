using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PeninsulaTime {
    // Regression for detailed Gangnam labels surviving a national-service transfer and alight.
    // Parent invokes this check after its interactive play session, without a graphics device.
    public static class StationMinimapCheck {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static int failures;
        static object Call(object target,string name,params object[] args){return target.GetType().GetMethod(name,Flags).Invoke(target,args);}
        static void Set(object target,string name,object value){target.GetType().GetField(name,Flags).SetValue(target,value);}
        static T Get<T>(object target,string name){return (T)target.GetType().GetField(name,Flags).GetValue(target);}
        static void Check(bool ok,string message){if(!ok){failures++;Debug.LogError("StationMinimapCheck: "+message);}}
        static void CheckHub(WorldBuilder world,NetStation at){
            Check(world.NetworkHubStation==at,"markers belong to current hub "+at.name);
            Check(!world.MapMarkers.Exists(m=>m.label=="개찰구"||m.label=="외선순환 · 역삼 방면"||m.label=="내선순환 · 교대 방면"||m.label=="previous station marker"),"detailed/previous station labels discarded");
            foreach(var route in world.NetworkTransfers){
                Check(world.MapMarkers.Exists(m=>(m.position-route.platformPoint).sqrMagnitude<.0001f&&m.label.StartsWith(route.line.shortName+" · ")),"current platform label "+route.line.id+"/"+route.direction);
                Check(world.MapMarkers.Exists(m=>(m.position-route.hallPoint).sqrMagnitude<.0001f&&m.label==route.line.shortName+" ↓"),"current hall label "+route.line.id+"/"+route.direction);
            }
            foreach(var exit in world.NetworkMappedExits){
                string label=string.IsNullOrEmpty(exit.exitNumber)?"지하철 출입구":exit.exitNumber;
                Check(world.MapMarkers.Exists(m=>(m.position-exit.surfacePoint).sqrMagnitude<.0001f&&m.label==label),"actual mapped exit label "+exit.osmId);
            }
        }
        public static void Run(){
            failures=0;TransitNetwork.Build(null);
            var camera=new GameObject("minimap state camera").AddComponent<Camera>();
            var world=new GameObject("minimap state world").AddComponent<WorldBuilder>();world.worldCamera=camera;
            var player=new GameObject("minimap state controller");player.SetActive(false);
            var game=player.AddComponent<GameController>();game.state=new GameState();game.state.era=9;
            var eye=new GameObject("minimap state eye").transform;
            Set(game,"world",world);Set(game,"eye",eye);Set(game,"viewCamera",camera);
            try{
                world.BuildDistrict(0);
                Check(world.MapMarkers.Exists(m=>m.label=="개찰구"),"detailed Gangnam marker fixture populated");
                var at=TransitNetwork.Named("강남").Find(s=>s.lines.Exists(l=>l.shortName=="신분당선"));
                Check(at!=null,"Gangnam Shinbundang fixture exists");
                if(at!=null){
                    var line=at.lines.Find(l=>l.shortName=="신분당선");
                    Call(game,"VisitNetworkStation",at,line,0);CheckHub(world,at);
                    var journey=Get<StationJourney>(game,"stationJourney");
                    Check(journey.stops.Count>1,"downstream fixture exists");
                    if(journey.stops.Count>1){
                        journey.index=Mathf.Min(3,journey.stops.Count-1);var destination=journey.Current;
                        world.MapMarkers.Add(new WorldBuilder.MapMarker(Vector3.zero,"previous station marker"));
                        var local=world.NetworkTransfers[0].platformPoint-journey.origin;
                        eye.position=journey.origin+Vector3.forward*(journey.index*StationJourney.Spacing)+local+Vector3.up*1.65f;
                        Call(game,"RebuildNetworkInterchangeAfterAlighting");CheckHub(world,destination);
                        string place=(string)Call(game,"PlaceName");
                        Check(place.StartsWith(destination.name+" · ")&&place.Contains("신분당선 승강장"),"minimap title follows downstream platform: "+place);
                    }
                }
                world.MapMarkers.Add(new WorldBuilder.MapMarker(Vector3.zero,"previous station marker"));
                world.BuildDistrict(2);
                Check(!world.MapMarkers.Exists(m=>m.label=="previous station marker"),"returning to a district also clears national marker state");
            }catch(Exception exception){Check(false,exception.ToString());}
            finally{Object.DestroyImmediate(player);Object.DestroyImmediate(eye.gameObject);Object.DestroyImmediate(world.gameObject);Object.DestroyImmediate(camera.gameObject);StationAreaData.ClearTileCache();}
            Debug.Log("StationMinimapCheck: "+(failures==0?"passed":failures+" failed"));EditorApplication.Exit(failures==0?0:1);
        }
    }
}
