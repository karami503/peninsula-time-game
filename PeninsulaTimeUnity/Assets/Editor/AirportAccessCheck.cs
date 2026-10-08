using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace PeninsulaTime {
public static class AirportAccessCheck {
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static int failures;
    static readonly MethodInfo Move=typeof(GameController).GetMethod("MoveWalkerStep",Flags);
    static void Set(object o,string name,object value){o.GetType().GetField(name,Flags).SetValue(o,value);}
    static void Check(bool ok,string message){if(!ok){failures++;Debug.LogError("AirportAccessCheck: "+message);}}
    static void Reset(GameController game,Transform eye,Vector3 feet){eye.position=feet+Vector3.up*1.65f;Set(game,"grounded",true);Set(game,"verticalSpeed",0f);}
    static bool Walk(GameController game,Transform eye,Vector3 target,string label){
        int stalled=0;
        for(int step=0;step<12000;step++){
            var feet=eye.position-Vector3.up*1.65f;var delta=target-feet;delta.y=0;
            if(delta.magnitude<.065f){
                for(int settle=0;settle<30;settle++)Move.Invoke(game,new object[]{Vector3.zero,1f/60});
                feet=eye.position-Vector3.up*1.65f;bool reached=Mathf.Abs(feet.y-target.y)<.25f;
                Check(reached,label+" reached wrong level "+feet+" expected "+target);return reached;
            }
            var amount=delta.normalized*Mathf.Min(.065f,delta.magnitude);
            Move.Invoke(game,new object[]{amount,1f/60});
            if((eye.position-Vector3.up*1.65f-feet).sqrMagnitude<.000001f)stalled++;else stalled=0;
            if(stalled>5){
                RaycastHit hit;Physics.SphereCast(feet+Vector3.up*.85f,.3f,amount.normalized,out hit,.2f);
                Check(false,label+" blocked at "+feet+" toward "+target+" by "+(hit.collider?hit.collider.name:"floor or headroom"));return false;
            }
        }
        Check(false,label+" timed out");return false;
    }
    static bool Path(GameController game,Transform eye,Vector3[] points,string name,bool reset=true){
        if(reset)Reset(game,eye,points[0]);
        foreach(var point in points)if(!Walk(game,eye,point,name))return false;
        return true;
    }
    static void CheckJetBridgeEnd(GameController game,Transform eye,WorldBuilder world,int gate){
        var end=world.AirportBoardingDoor(gate);
        var forward=new Vector3(-2.8f,0,28f).normalized;
        var right=Vector3.Cross(Vector3.up,forward);
        // Reproduce holding W past the end, across the usable width, with the real walking motor.
        foreach(float offset in new[]{-.65f,0f,.65f}){
            Reset(game,eye,end-forward*.4f+right*offset);
            for(int step=0;step<180;step++)Move.Invoke(game,new object[]{forward*.075f,1f/60});
            var feet=eye.position-Vector3.up*1.65f;
            Check(Mathf.Abs(feet.y-end.y)<.25f,"gate "+gate+" forward overshoot must remain on landing at offset "+offset+": "+feet);
            Check(Vector3.Dot(feet-end,forward)<.5f,"gate "+gate+" end wall must stop forward overshoot at offset "+offset);
            RaycastHit floor;
            bool supported=Physics.Raycast(feet+Vector3.up*.1f,Vector3.down,out floor,.3f);
            Check(supported&&(floor.collider.name=="항공기 연결 발판"||floor.collider.name=="탑승교 보행 바닥"||floor.collider.name=="객실 보행 바닥"),"gate "+gate+" stopped position has landing support at offset "+offset+" feet="+feet+" hit="+(floor.collider?floor.collider.name:"none"));
            // The stop must be recoverable by walking back and turning left through the aircraft door.
            if(Walk(game,eye,end,"gate "+gate+" recover from end wall")){
                var aisle=new Vector3(world.GatePlane(gate).x,3,WorldBuilder.AirportOrigin.z+57.8f);
                if(Walk(game,eye,aisle,"gate "+gate+" left cabin opening"))Walk(game,eye,aisle+Vector3.forward*3.2f,"gate "+gate+" cabin after overshoot");
            }
        }
    }
    public static void Run(){
        AirportModelImport.Prepare();failures=0;
        var camera=new GameObject("airport check camera").AddComponent<Camera>();
        var world=new GameObject("airport check world").AddComponent<WorldBuilder>();world.worldCamera=camera;
        world.BuildDistrict(3,9);Physics.SyncTransforms();
        var host=new GameObject("airport check walker");host.SetActive(false);
        var game=host.AddComponent<GameController>();game.state=new GameState();Set(game,"world",world);
        var eye=new GameObject("airport check eye").transform;Set(game,"eye",eye);
        var routes=world.root.GetComponentsInChildren<AirportWalkRoute>();
        Check(routes.Length==22,"six stair lanes plus eight bridges and eight cabin routes; got "+routes.Length);
        int steps=0,bridges=0,cabins=0;
        foreach(var route in routes){
            if(route.name.StartsWith("탑승교 →"))cabins++;
            else if(route.name.StartsWith("탑승교 보행"))bridges++;else steps++;
            if(Path(game,eye,route.points,route.name)){
                for(int i=route.points.Length-2;i>=0;i--)if(!Walk(game,eye,route.points[i],route.name+" return"))break;
            }
        }
        Check(steps==6&&bridges==8&&cabins==8,"stair/bridge/cabin route counts");
        for(int gate=1;gate<=8;gate++)CheckJetBridgeEnd(game,eye,world,gate);
        var origin=WorldBuilder.AirportOrigin;
        // One continuous walk from the actual metro doorway to check-in, upstairs, and the security gate.
        var toSecurity=new[]{origin+new Vector3(-59.4f,0,-16),origin+new Vector3(-55,0,-16),origin+new Vector3(-55,0,-28.8f),origin+new Vector3(-49.1f,0,-28.8f),origin+new Vector3(-49.1f,0,-28),origin+new Vector3(-49.1f,6,-14),origin+new Vector3(-49.1f,6,-4),origin+new Vector3(-5,6,-4),origin+new Vector3(-5,6,-25),origin+new Vector3(-1.1f,6,-25),origin+new Vector3(-1.1f,6,-24),origin+new Vector3(-1.1f,12,-10),origin+new Vector3(-1.1f,12,-4),origin+new Vector3(-10.5f,12,-4)};
        Path(game,eye,toSecurity,"metro to 3F security");
        int security=0;
        foreach(var barrier in world.root.GetComponentsInChildren<Barrier>())if(barrier.kind=="security"){
            security++;Check(Mathf.Abs(barrier.transform.position.y-WorldBuilder.Floor3)<.01f,"security belongs to 3F");
            barrier.blocker.enabled=false; // Physical walk is tested separately from boarding authorization.
        }
        Check(security==4,"two security entries, each with two lanes");Physics.SyncTransforms();
        Path(game,eye,new[]{origin+new Vector3(-10.5f,12,-4),origin+new Vector3(-10.5f,12,6),origin+new Vector3(-132,12,6),origin+new Vector3(-132,12,27.8f),origin+new Vector3(-135,12,27.8f),origin+new Vector3(-135,12,29.8f),world.AirportBoardingDoor(1),origin+new Vector3(-140,3,57.8f),origin+new Vector3(-140,3,61)},"3F security to aircraft seat");
        int gates=0;
        foreach(var fixture in world.root.GetComponentsInChildren<Fixture>())if(fixture.kind=="gate"){
            gates++;Check(fixture.transform.IsChildOf(world.GateAircraft(fixture.number).transform),"departure fixture must be aboard aircraft");
        }
        Check(gates==8,"eight aircraft boarding seats");
        foreach(var plane in new[]{world.GateAircraft(1),world.GateAircraft(8)}){
            Check(plane!=null,"parked aircraft exists");bool open=false;
            if(plane!=null)foreach(var mesh in plane.GetComponentsInChildren<MeshFilter>())if(mesh.sharedMesh.name=="Aircraft open boarding door")open=true;
            Check(open,"aircraft fuselage has an actual mesh opening");
        }
        Debug.Log("AirportAccessCheck: "+(failures==0?"passed":failures+" failed")+" · 3 floor connections, metro to security, all 8 bridges/cabins, 24 forward-overshoot and cabin-recovery walks");
        EditorApplication.Exit(failures==0?0:1);
    }
}}
