using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PeninsulaTime {
    // Explicitly invoked by the parent runner. Uses the actual walking motor, not teleport checks.
    public static class NetworkMappedExitCheck {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static int failures,walks;
        static float metres;
        static readonly MethodInfo Move=typeof(GameController).GetMethod("MoveWalkerStep",Flags);
        static void Check(bool ok,string text){if(!ok){failures++;Debug.LogError("NetworkMappedExitCheck: "+text);}}
        static void Set(object target,string field,object value){target.GetType().GetField(field,Flags).SetValue(target,value);}
        static bool Walk(GameController game,Transform eye,Vector3 target,string label){
            for(int step=0;step<14000;step++){
                var feet=eye.position-Vector3.up*1.65f;var delta=target-feet;delta.y=0;
                if(delta.magnitude<.07f){
                    for(int n=0;n<10;n++)Move.Invoke(game,new object[]{Vector3.zero,1f/60f});
                    Check(Mathf.Abs(eye.position.y-1.65f-target.y)<.45f,label+" floor "+(eye.position.y-1.65f)+" target "+target.y);return true;
                }
                var move=delta.normalized*Mathf.Min(.075f,delta.magnitude);
                Move.Invoke(game,new object[]{move,1f/60f});float distance=Vector3.Distance(eye.position-Vector3.up*1.65f,feet);metres+=distance;
                if(distance<.0003f){
                    RaycastHit hit;Physics.SphereCast(feet+Vector3.up*.85f,.28f,move.normalized,out hit,.19f);
                    RaycastHit floor;bool hasFloor=Physics.Raycast(feet+Vector3.up*1.35f,Vector3.down,out floor,61.25f,~0,QueryTriggerInteraction.Ignore);
                    RaycastHit nextFloor;bool hasNext=Physics.Raycast(feet+move+Vector3.up*1.35f,Vector3.down,out nextFloor,61.25f,~0,QueryTriggerInteraction.Ignore);
                    string obstacles="";foreach(var obstacle in Physics.SphereCastAll(feet+Vector3.up*.85f,.3f,move.normalized,move.magnitude+.05f,~0,QueryTriggerInteraction.Ignore))obstacles+="; "+obstacle.collider.name+" p="+obstacle.point+" n="+obstacle.normal;
                    Debug.Log("NetworkMappedExitCheck diagnostic: next ground="+(hasNext?nextFloor.collider.name+" p="+nextFloor.point:"none")+" sphere hits="+obstacles);
                    Check(false,label+" blocked at "+feet+" toward "+target+" collider "+(hit.collider==null?"floor/headroom":hit.collider.name)+" ground="+(hasFloor?floor.collider.name+" at "+floor.point:"none"));return false;
                }
            }
            Check(false,label+" walking timeout");return false;
        }
        static NetStation Find(string name,string grade){
            return TransitNetwork.Stations.Find(s=>TransitNetwork.Bare(s.name)==name&&(grade==null||s.grade==grade)&&s.lines.Exists(l=>l.kind!="bus"&&l.kind!="brt"));
        }
        static void CheckHub(WorldBuilder world,GameController game,Transform eye,NetStation station){
            if(station==null){Check(false,"fixture station exists");return;}
            var line=station.lines.Find(l=>l.kind!="bus"&&l.kind!="brt");world.BuildNetworkStation(station,line,0);
            // Integration must call this automatically, with the current hall footprint.
            Check(world.NetworkArea!=null,"area loaded "+station.name);if(world.NetworkArea==null)return;
            var sourceIds=new HashSet<string>();foreach(var entry in world.NetworkArea.entrances)sourceIds.Add(entry.id);
            Check(world.NetworkMappedExits.Count==sourceIds.Count,station.name+" every mapped entrance is a usable route");
            Physics.SyncTransforms();
            // Belt motion is tested separately by TransitSpeedCheck. Turn it off only in this walk
            // to test both directions of the ordinary clear pedestrian path deterministically.
            foreach(var belt in world.root.GetComponentsInChildren<MovingWalkway>())belt.enabled=false;
            foreach(var route in world.NetworkMappedExits){
                var original=world.NetworkArea.entrances.Find(e=>e.id==route.osmId);
                Check(original!=null&&route.exitNumber==original.number,"OSM exit ref retained "+route.osmId);
                if(original!=null){var offset=route.surfacePoint-original.position;offset.y=0;Check(offset.magnitude<.001f,"source surface coordinate retained "+route.osmId);}
                Check(route.points.Length>=3&&route.reconstructedPassage,"route is explicitly reconstructed "+route.osmId);
                var outward=route.points[0]-route.points[1];outward.y=0;outward.Normalize();
                eye.position=route.points[0]+outward*.8f+Vector3.up*1.65f;Set(game,"grounded",true);Set(game,"cabin",null);
                bool clear=true;
                foreach(var point in route.points)if(!Walk(game,eye,point,station.name+" exit "+route.exitNumber+" toward hall")){clear=false;break;}
                if(clear)for(int i=route.points.Length-2;i>=0;i--)if(!Walk(game,eye,route.points[i],station.name+" exit "+route.exitNumber+" toward street")){clear=false;break;}
                if(clear)clear=Walk(game,eye,route.points[0]+outward*.8f,station.name+" exit street landing");
                if(clear)walks++;
            }
            Debug.Log("NetworkMappedExitCheck: "+station.name+" "+station.grade+" mapped exits "+world.NetworkMappedExits.Count);
        }
        public static void Run(){
            failures=walks=0;metres=0;TransitNetwork.Build(null);
            var camera=new GameObject("mapped exit camera").AddComponent<Camera>();var world=new GameObject("mapped exit world").AddComponent<WorldBuilder>();world.worldCamera=camera;
            var go=new GameObject("mapped exit motor");go.SetActive(false);var game=go.AddComponent<GameController>();game.state=new GameState();Set(game,"world",world);
            var eye=new GameObject("mapped exit eye").transform;Set(game,"eye",eye);
            try{
                CheckHub(world,game,eye,Find("강남","underground"));
                CheckHub(world,game,eye,Find("성수","elevated"));
                CheckHub(world,game,eye,Find("서면","underground"));
                var surface=TransitNetwork.Stations.Find(s=>s.grade=="surface"&&s.lines.Exists(l=>l.kind!="bus"&&l.kind!="brt")&&StationAreaData.ForStation(s)!=null&&StationAreaData.ForStation(s).entrances>=1&&StationAreaData.ForStation(s).entrances<=3);
                CheckHub(world,game,eye,surface);
                var noSource=TransitNetwork.Stations.Find(s=>s.lines.Exists(l=>l.kind!="bus"&&l.kind!="brt")&&StationAreaData.ForStation(s)!=null&&StationAreaData.ForStation(s).entrances==0);
                if(noSource!=null){world.BuildNetworkStation(noSource,noSource.lines.Find(l=>l.kind!="bus"&&l.kind!="brt"),0);Check(world.NetworkMappedExits.Count==0,"no invented numbered exit without map source");Check(world.root.GetComponentsInChildren<NetworkMappedExitRoute>().Length==0,"old hub entrances released");}
                Check(walks>0,"at least one actual mapped route walked");
            }catch(Exception e){Check(false,e.ToString());}
            finally{Object.DestroyImmediate(world.gameObject);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(go);Object.DestroyImmediate(eye.gameObject);StationAreaData.ClearTileCache();}
            Debug.Log("NetworkMappedExitCheck: "+(failures==0?"passed":failures+" failed")+"; "+walks+" complete bidirectional routes; motor distance "+metres.ToString("F1")+"m");
            EditorApplication.Exit(failures==0?0:1);
        }
    }
}
