using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Walks from a district concourse through a transfer portal's passage into the line's own station and back. The
    // station ahead is loaded while the walker is in the passage, and the walker never jumps: their place in the passage
    // is the same before and after every load, and they walk on into the hub concourse and back to the district floor.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.TransferPassageCheck.Run
    public static class TransferPassageCheck
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        const float Dt=1f/60f;
        static int failures,loads;
        static void Check(bool ok,string message){if(!ok){failures++;Debug.LogError("TransferPassageCheck: "+message);}}
        static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,Flags).Invoke(o,args);}
        static T Get<T>(object o,string name){return (T)o.GetType().GetField(name,Flags).GetValue(o);}
        static void Set(object o,string name,object value){o.GetType().GetField(name,Flags).SetValue(o,value);}

        // Walks on the flat toward `target` (re-evaluated each step, as the passage may move), running the transfer
        // update every step; fails on a block or a jump of the walker within the passage.
        static bool Walk(GameController game,Transform eye,Func<Vector3> target,string label)
        {
            int stalled=0;
            for(int step=0;step<6000;step++)
            {
                var feet=eye.position-Vector3.up*1.65f;var delta=target()-feet;delta.y=0;
                if(delta.magnitude<.12f)return true;
                Call(game,"MoveWalkerStep",delta.normalized*Mathf.Min(.075f,delta.magnitude),Dt);
                var passage=Get<TransferPassage>(game,"passage");
                Vector3 before=passage!=null?passage.transform.InverseTransformPoint(eye.position):Vector3.zero;bool hub=passage!=null&&passage.atHub;
                Call(game,"UpdateDistrictTransfers");
                passage=Get<TransferPassage>(game,"passage");
                if(passage!=null&&passage.atHub!=hub)
                {
                    loads++;
                    Check((passage.transform.InverseTransformPoint(eye.position)-before).sqrMagnitude<1e-4f,label+": the walker moved within the passage when the station loaded");
                }
                var after=eye.position-Vector3.up*1.65f;
                if((after-feet).sqrMagnitude<1e-7f)stalled++;else stalled=0;
                if(stalled>8){RaycastHit hit;Physics.SphereCast(feet+Vector3.up*.9f,.3f,delta.normalized,out hit,.5f);RaycastHit down;Physics.Raycast(feet+delta.normalized*.1f+Vector3.up*1.3f,Vector3.down,out down,4f,~0,QueryTriggerInteraction.Ignore);
                    Check(false,label+" blocked at "+feet+" by "+(hit.collider?hit.collider.name+" "+hit.collider.bounds:"floor/step")+(down.collider?" (ground ahead: "+down.collider.name+" at "+down.point.y+")":""));return false;}
            }
            Check(false,label+" timeout");return false;
        }
        static Func<Vector3> InPassage(GameController game,Vector3 local){return ()=>Get<TransferPassage>(game,"passage").transform.TransformPoint(local);}

        public static void Run()
        {
            failures=0;loads=0;
            try{Scenario();}
            catch(Exception e){failures++;Debug.LogException(e);}
            Debug.Log("TransferPassageCheck: "+(failures==0?"passed":failures+" failed"));
            EditorApplication.Exit(failures==0?0:1);
        }
        static void Scenario()
        {
            TransitNetwork.Build(null);
            var camera=new GameObject("passage check camera").AddComponent<Camera>();
            var world=new GameObject("passage check world").AddComponent<WorldBuilder>();world.worldCamera=camera;
            var host=new GameObject("passage check player");host.SetActive(false);
            var game=host.AddComponent<GameController>();game.state=new GameState();game.viewCamera=camera;game.world=world;
            var eye=new GameObject("passage check eye").transform;Set(game,"eye",eye);
            Call(game,"EnterDistrict",GameController.SeoulStationDistrict,false);
            Walkthrough(game,world,eye,"GTX-A",null);
            // The same walk to a surface and to an elevated station: the stair hall rises through the hub's ground.
            Walkthrough(game,world,eye,"경의·중앙선","");
            Walkthrough(game,world,eye,"GTX-B","elevated");
        }
        // Walks into the portal `label`, through its passage into the hub concourse and back; `grade` (when not null)
        // stands in for the station's own while it runs.
        static void Walkthrough(GameController game,WorldBuilder world,Transform eye,string label,string grade)
        {
            loads=0;
            var portal=world.DistrictTransfers.Find(p=>p.label==label);
            Check(portal!=null,"Seoul Station has no "+label+" transfer portal");if(portal==null)return;
            var station=TransitNetwork.Station(portal.stationId);var entry=portal.entry;string own=station.grade;
            if(grade!=null)station.grade=grade;
            try
            {
                eye.position=portal.approach+Vector3.up*1.65f;Set(game,"grounded",true);Set(game,"verticalSpeed",0f);
                const float O=TransferPassage.Out;
                if(!Walk(game,eye,()=>entry,"into the "+label+" portal"))return;
                var passage=Get<TransferPassage>(game,"passage");
                Check(passage!=null&&passage.lineId==portal.lineId&&Get<string>(game,"mode")=="district",label+": the portal did not open a passage (or jumped)");
                if(passage==null||passage.lineId!=portal.lineId)return;
                float rise=passage.rise,A=TransferPassage.Across;var end=passage.End;
                bool there=Walk(game,eye,InPassage(game,new Vector3(0,0,O)),label+" passage out")
                    &&Walk(game,eye,InPassage(game,new Vector3(A,0,O)),label+" passage across")
                    &&Walk(game,eye,InPassage(game,end+Vector3.forward*3),label+" passage up into the hub concourse");
                Check(Get<string>(game,"mode")=="rail"&&world.NetworkHubStation==station,label+": the station did not load on the way");
                float feet=eye.position.y-1.65f;
                Check(Mathf.Abs(feet-world.NetworkHallY)<.3f,label+": did not reach the hub concourse floor (feet "+feet+", hall "+world.NetworkHallY+")");
                if(!there)return;
                bool back=Walk(game,eye,InPassage(game,new Vector3(A,0,O)),label+" back down the passage")
                    &&Walk(game,eye,InPassage(game,new Vector3(0,0,O)),label+" back across")
                    &&Walk(game,eye,InPassage(game,Vector3.zero),label+" back out of the passage")
                    &&Walk(game,eye,InPassage(game,new Vector3(0,0,-2.5f)),label+" back onto the concourse");
                Check(Get<string>(game,"mode")=="district"&&game.state.district==GameController.SeoulStationDistrict,label+": walking back did not load Seoul Station again");
                feet=eye.position.y-1.65f;
                Check(back&&Mathf.Abs(feet-WorldBuilder.ConcourseY)<.3f,label+": did not reach the district concourse floor (feet "+feet+")");
                Check(loads==2,label+": stations loaded "+loads+" times (expected 2)");
                Debug.Log("TransferPassageCheck: Seoul Station ↔ "+station.name+" "+label+" ("+(grade??own)+"), rise "+rise+" m");
            }
            finally{station.grade=own;}
        }
    }
}
