using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // The 3D button starts inside Seoul Station's concourse, standing on its floor with every chunk around it
    // loaded; the district's chunks then load and unload around a moving focus without losing any structure.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.ChunkStreamerCheck.Run
    public static class ChunkStreamerCheck
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static int failures;
        static void Check(bool ok,string message){if(!ok){failures++;Debug.LogError("ChunkStreamerCheck: "+message);}}
        static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,Flags).Invoke(o,args);}
        static T Get<T>(object o,string name){return (T)o.GetType().GetField(name,Flags).GetValue(o);}
        static void Set(object o,string name,object value){o.GetType().GetField(name,Flags).SetValue(o,value);}

        static readonly MethodInfo Move=typeof(GameController).GetMethod("MoveWalkerStep",Flags);
        // Walks on the flat toward target's x/z (its y is ignored); false and a failure if blocked.
        static bool Walk(GameController game,Transform eye,Vector3 target,string label)
        {
            int stalled=0;
            for(int step=0;step<20000;step++)
            {
                var feet=eye.position-Vector3.up*1.65f;var delta=target-feet;delta.y=0;
                if(delta.magnitude<.12f)return true;
                Move.Invoke(game,new object[]{delta.normalized*Mathf.Min(.075f,delta.magnitude),1f/60});
                var after=eye.position-Vector3.up*1.65f;
                if((after-feet).sqrMagnitude<.000001f)stalled++;else stalled=0;
                if(stalled>8){RaycastHit hit;Physics.SphereCast(feet+Vector3.up*.85f,.3f,delta.normalized,out hit,.4f);Check(false,label+" blocked at "+feet+" by "+(hit.collider?hit.collider.name:"floor/step"));return false;}
            }
            Check(false,label+" timeout");return false;
        }
        public static void Run()
        {
            failures=0;
            try
            {
                TransitNetwork.Build(null);
                var camera=new GameObject("chunk check camera").AddComponent<Camera>();
                var world=new GameObject("chunk check world").AddComponent<WorldBuilder>();world.worldCamera=camera;
                var host=new GameObject("chunk check player");host.SetActive(false);
                var game=host.AddComponent<GameController>();game.state=new GameState();game.viewCamera=camera;game.world=world;
                var eye=new GameObject("chunk check eye").transform;Set(game,"eye",eye);
                // 3D button.
                Call(game,"Start3DAtMapClick");Physics.SyncTransforms();
                Check(game.state.district==GameController.SeoulStationDistrict&&Get<string>(game,"mode")=="district","3D button did not open Seoul Station");
                var feet=eye.position-Vector3.up*1.65f;RaycastHit floor;
                Check(Physics.Raycast(feet+Vector3.up*.5f,Vector3.down,out floor,1.5f)&&Mathf.Abs(floor.point.y-feet.y)<.1f,"3D start is not standing on the concourse floor at "+feet);
                Check(!Physics.CheckSphere(feet+Vector3.up*.9f,.3f,~0,QueryTriggerInteraction.Ignore),"3D start is inside something");
                Check(Vector3.Distance(feet,world.SeoulStationConcourse)<.5f,"3D start is not the concourse centre");
                // Chunks around the start are all loaded.
                var chunks=world.root.AddComponent<ChunkStreamer>();chunks.Index();
                int total=0;foreach(var s in world.root.GetComponentsInChildren<Structure>(true))total++;
                chunks.Stream(eye.position);
                Check(chunks.CellCount>4,"district not divided into chunks: "+chunks.CellCount);
                foreach(var s in world.root.GetComponentsInChildren<Structure>(true))
                {
                    var r=s.GetComponentInChildren<Renderer>(true);if(r==null)continue;
                    var c=new Vector2Int(Mathf.FloorToInt(r.bounds.center.x/ChunkStreamer.CellSize),Mathf.FloorToInt(r.bounds.center.z/ChunkStreamer.CellSize));
                    if(ChunkStreamer.Distance(c,eye.position)<ChunkStreamer.LoadRadius-ChunkStreamer.CellSize&&!s.gameObject.activeSelf){Check(false,"structure near the start not loaded: "+s.name);break;}
                }
                // Move the focus away: far chunks unload; coming back reloads all.
                int far=chunks.Stream(eye.position+new Vector3(3000,0,0));
                int active=0;foreach(var s in world.root.GetComponentsInChildren<Structure>(true))if(s.gameObject.activeSelf)active++;
                Check(far==0&&active==0,"chunks not unloaded away from the district: cells "+far+", structures "+active);
                int all=chunks.Stream(eye.position);
                int back=0;foreach(var s in world.root.GetComponentsInChildren<Structure>(true))back++;
                Check(back==total&&all>0,"structures lost while streaming: "+back+" of "+total);
                Debug.Log("ChunkStreamerCheck: "+total+" structures in "+chunks.CellCount+" chunks, "+all+" loaded at the start, feet "+feet);
                // Seoul Station: from the concourse centre, walk down every platform stair and out by both end stairs.
                Check(world.SeoulPlatformStairs.Count>=4,"Seoul Station hall has only "+world.SeoulPlatformStairs.Count+" platform stairs");
                foreach(var foot in world.SeoulPlatformStairs)
                {
                    eye.position=world.SeoulStationConcourse+Vector3.up*1.65f;Set(game,"grounded",true);Set(game,"verticalSpeed",0f);
                    var top=new Vector3(foot.x,WorldBuilder.SeoulHallFloor,74);
                    if(Walk(game,eye,new Vector3(foot.x,0,56),"concourse aisle to "+foot.x)&&Walk(game,eye,top,"stair top "+foot.x)&&Walk(game,eye,foot,"platform stair "+foot.x))
                        Check(Mathf.Abs(eye.position.y-1.65f-foot.y)<.4f,"platform stair "+foot.x+" did not reach the platform (feet "+(eye.position.y-1.65f)+", platform "+foot.y+")");
                }
                foreach(var door in new[]{world.SeoulHallEastDoor,world.SeoulHallWestDoor})
                {
                    eye.position=world.SeoulStationConcourse+Vector3.up*1.65f;Set(game,"grounded",true);Set(game,"verticalSpeed",0f);
                    var outside=door+(door.x<0?Vector3.left:Vector3.right)*(2*WorldBuilder.SeoulHallFloor+2);
                    if(Walk(game,eye,new Vector3(door.x,0,60),"to "+(door.x<0?"east":"west")+" door")&&Walk(game,eye,outside,"down the "+(door.x<0?"east":"west")+" stairs"))
                        Check(eye.position.y-1.65f<1.5f,"end stairs did not reach the ground (feet "+(eye.position.y-1.65f)+")");
                }
            }
            catch(System.Exception e){failures++;Debug.LogException(e);}
            Debug.Log("ChunkStreamerCheck: "+(failures==0?"passed":failures+" failed"));
            EditorApplication.Exit(failures==0?0:1);
        }
    }
}
