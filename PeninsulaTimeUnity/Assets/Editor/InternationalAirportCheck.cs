using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PeninsulaTime {
public static class InternationalAirportCheck {
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static int failures;
    static readonly MethodInfo Move=typeof(GameController).GetMethod("MoveWalkerStep",Flags);
    static void Set(object o,string name,object value){o.GetType().GetField(name,Flags).SetValue(o,value);}
    static void Check(bool ok,string message){if(!ok){failures++;Debug.LogError("InternationalAirportCheck: "+message);}}
    static void Reset(GameController game,Transform eye,Vector3 feet){eye.position=feet+Vector3.up*1.65f;Set(game,"grounded",true);Set(game,"verticalSpeed",0f);}
    static bool Walk(GameController game,Transform eye,Vector3 target,string label){
        int stalled=0;
        for(int step=0;step<16000;step++){
            var feet=eye.position-Vector3.up*1.65f;var delta=target-feet;delta.y=0;
            if(delta.magnitude<.085f){
                // Belts move while idle, so only settle where the target is not on a live belt.
                if(MovingWalkway.VelocityAt(feet).sqrMagnitude<.001f)for(int k=0;k<12;k++)Move.Invoke(game,new object[]{Vector3.zero,1f/60});
                feet=eye.position-Vector3.up*1.65f;bool ok=Mathf.Abs(feet.y-target.y)<.24f;
                Check(ok,label+" wrong level at "+feet+", expected "+target);return ok;
            }
            Move.Invoke(game,new object[]{delta.normalized*Mathf.Min(.075f,delta.magnitude),1f/60});
            var after=eye.position-Vector3.up*1.65f;
            if((after-feet).sqrMagnitude<.000001f)stalled++;else stalled=0;
            if(stalled>8){
                RaycastHit hit;Physics.SphereCast(feet+Vector3.up*.85f,.3f,delta.normalized,out hit,.35f);
                Check(false,label+" blocked "+feet+" -> "+target+" by "+(hit.collider?hit.collider.name:"floor/step"));return false;
            }
        }Check(false,label+" timeout toward "+target);return false;
    }
    static bool Path(GameController game,Transform eye,Vector3[] points,string name,bool reset=true){
        if(reset)Reset(game,eye,points[0]);foreach(var p in points)if(!Walk(game,eye,p,name))return false;return true;
    }
    static int CheckGatePierBeltEdges(GameController game,Transform eye){
        int walks=0;var o=WorldBuilder.InternationalAirportOrigin;
        // Check the full usable length including entry/exit landings, with pedestrian body width.
        // Centre-only movement missed the former row at z=40: its backrest reached z=40.54.
        foreach(float beltZ in new[]{41f,47f})foreach(float offset in new[]{-.3f,.3f})foreach(bool reverse in new[]{false,true}){
            var a=o+new Vector3(reverse?297:168,12,beltZ+offset);
            var b=o+new Vector3(reverse?168:297,12,beltZ+offset);
            string label="gate-pier belt z="+beltZ+" edge="+offset+(reverse?" westward":" eastward");
            var delta=b-a;RaycastHit hit;
            bool blocked=Physics.CapsuleCast(a+Vector3.up*.35f,a+Vector3.up*1.55f,.28f,delta.normalized,out hit,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
            Check(!blocked,label+" body clearance blocked by "+(hit.collider?hit.collider.name:"none")+" at "+hit.point);
            Path(game,eye,new[]{a,b},label);walks++;
        }
        return walks;
    }
    public static void Run(){
        failures=0;
        var camera=new GameObject("international check camera").AddComponent<Camera>();
        var world=new GameObject("international check world").AddComponent<WorldBuilder>();world.worldCamera=camera;
        world.BuildDistrict(3);world.BuildInternationalAirport();Physics.SyncTransforms();
        var terminal=world.InternationalAirportRoot;Check(terminal!=null,"terminal exists");
        var host=new GameObject("international check walker");host.SetActive(false);var game=host.AddComponent<GameController>();game.state=new GameState();Set(game,"world",world);
        var eye=new GameObject("international check eye").transform;Set(game,"eye",eye);
        var routes=terminal.GetComponentsInChildren<InternationalWalkRoute>();
        Check(routes.Length>=15,"route records cover all four floors and B1; got "+routes.Length);
        foreach(var route in routes){
            if(Path(game,eye,route.points,route.name))for(int i=route.points.Length-2;i>=0;i--)if(!Walk(game,eye,route.points[i],route.name+" reverse"))break;
        }
        var o=WorldBuilder.InternationalAirportOrigin;
        // Continuous walk never teleports between the terminal entrance, check-in, security and long pier.
        var journey=new[]{o+new Vector3(-140,0,56),o+new Vector3(-140,0,50),o+new Vector3(-130,0,50),o+new Vector3(-130,0,-56),o+new Vector3(-105,0,-56),o+new Vector3(-105,0,-48),o+new Vector3(-105,6,-24),o+new Vector3(-105,6,-18),o+new Vector3(0,6,-18),o+new Vector3(0,6,-8),o+new Vector3(0,6,-18),o+new Vector3(-105,6,-18),o+new Vector3(-99,6,-18),o+new Vector3(-99,6,-24),o+new Vector3(-99,12,-48),o+new Vector3(-99,12,-56),o+new Vector3(0,12,-56),o+new Vector3(0,12,-22),o+new Vector3(0,12,12),o+new Vector3(0,12,44),o+new Vector3(307,12,44)};
        Path(game,eye,journey,"continuous B1 arrival/check-in/security/gate39");
        // Actual station seam: if root forgot the mouth in LinkGimpoExits this reproduces the closed wall.
        var mouth=WorldBuilder.InternationalAirportConnector;
        Path(game,eye,new[]{mouth+Vector3.left*12,mouth+Vector3.left*3,mouth,mouth+Vector3.back*9},"existing B1 union -> international branch");
        int belts=0;
        foreach(var belt in terminal.GetComponentsInChildren<MovingWalkway>()){
            belts++;Reset(game,eye,belt.transform.position);var before=eye.position;
            for(int i=0;i<60;i++)Move.Invoke(game,new object[]{Vector3.zero,1f/60});
            var moved=eye.position-before;Check(Vector3.Dot(moved,belt.transform.forward)>.9f,"moving walkway carries idle passenger "+belt.transform.position);
            Check(Mathf.Abs(moved.y)<.15f,"moving walkway stays on floor");
        }
        Check(belts>=10,"B1 and long gate pier have opposing walkways; got "+belts);
        int beltEdgeWalks=CheckGatePierBeltEdges(game,eye);
        // Test headroom at all stair profiles, rather than checking only their endpoints.
        foreach(var route in routes)if(route.name.Contains("↔")){
            var a=route.points[1];var b=route.points[2];
            for(int i=1;i<20;i++){
                var p=Vector3.Lerp(a,b,i/20f);RaycastHit hit;
                bool blocked=Physics.SphereCast(p+Vector3.up*.4f,.28f,Vector3.up,out hit,1.5f,~0,QueryTriggerInteraction.Ignore);
                Check(!blocked,route.name+" headroom blocked at "+p+" by "+(hit.collider?hit.collider.name:"none"));
            }
        }
        // Sliding into the side of the 4F notch must not fall down the atrium.
        Reset(game,eye,o+new Vector3(-40,18,-45));
        for(int i=0;i<180;i++)Move.Invoke(game,new object[]{Vector3.right*.075f,1f/60});
        var guardFeet=eye.position-Vector3.up*1.65f;Check(guardFeet.x<o.x-36&&Mathf.Abs(guardFeet.y-18)<.2f,"4F notch guard prevents fall");
        foreach(var p in new[]{world.InternationalArrivalSpawn,world.InternationalCheckInSpawn,world.InternationalDepartureSpawn,o+new Vector3(250,13.65f,44),mouth+Vector3.back*20+Vector3.up*1.65f})
            Check(WorldBuilder.IsInternationalAirportInterior(p),"indoor lighting includes "+p);
        foreach(var p in new[]{o+new Vector3(0,26,0),o+new Vector3(0,1.65f,-90),WorldBuilder.AirportOrigin+new Vector3(1600,2,322.5f),new Vector3(450,70,-500)})
            Check(!WorldBuilder.IsInternationalAirportInterior(p)&&!WorldBuilder.IsDomesticAirportInterior(p),"outdoor sky/apron remains outdoors at "+p);
        int renderers=terminal.GetComponentsInChildren<Renderer>().Length,objects=terminal.GetComponentsInChildren<Transform>().Length,lights=terminal.GetComponentsInChildren<Light>().Length;
        Check(renderers<260,"batched renderer budget <260, got "+renderers);Check(objects<320,"terminal object budget <320, got "+objects);Check(lights==0,"emissive strips add no per-frame point-light work");
        int initial=terminal.GetComponentsInChildren<Transform>().Length;world.BuildInternationalAirport();Check(initial==terminal.GetComponentsInChildren<Transform>().Length,"construction is idempotent");
        Debug.Log("InternationalAirportCheck: "+(failures==0?"passed":failures+" failed")+"; "+routes.Length+" bidirectional paths; "+belts+" moving walkways; "+beltEdgeWalks+" belt-edge walks; "+renderers+" renderers / "+objects+" objects");
        EditorApplication.Exit(failures==0?0:1);
    }
}}
