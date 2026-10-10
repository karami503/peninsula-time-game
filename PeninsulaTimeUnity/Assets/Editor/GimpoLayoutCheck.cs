using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PeninsulaTime
{
    // Independent acceptance checks for the geographic Gimpo rebuild. These call
    // the normal walker/cabin code; no route is marked passed by teleporting over it.
    public static class GimpoLayoutCheck
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        const float EyeHeight=1.65f,Dt=1f/60f;
        static readonly MethodInfo Move=typeof(GameController).GetMethod("MoveWalkerStep",Flags);
        static readonly MethodInfo Enter=typeof(GameController).GetMethod("TryEnterCabin",Flags);
        static int failures,walkedRoutes,boardedSides;
        static float walkedMetres;
        static object Get(object o,string name){return o.GetType().GetField(name,Flags).GetValue(o);}
        static void Set(object o,string name,object value){o.GetType().GetField(name,Flags).SetValue(o,value);}
        static object Call(object o,string name,params object[] arguments){return o.GetType().GetMethod(name,Flags).Invoke(o,arguments);}
        static void Check(bool ok,string reason){if(!ok){failures++;Debug.LogError("GimpoLayoutCheck: "+reason);}}
        static Vector3 Feet(Transform eye){return eye.position-Vector3.up*EyeHeight;}
        static string Point(Vector3 v){return v.ToString("F3");}
        static void Reset(GameController game,Transform eye,Vector3 feet)
        {
            Set(game,"cabin",null);Set(game,"grounded",true);Set(game,"verticalSpeed",0f);Set(game,"jumpQueued",false);
            eye.position=feet+Vector3.up*EyeHeight;
        }

        static string Obstruction(Vector3 feet,Vector3 toward)
        {
            var rows=new List<string>();var step=toward-feet;step.y=0;
            if(step.sqrMagnitude>.001f)
                foreach(var hit in Physics.SphereCastAll(feet+Vector3.up*.85f,.3f,step.normalized,.8f,~0,QueryTriggerInteraction.Ignore))
                    rows.Add("ahead "+Description(hit.collider)+" distance="+hit.distance.ToString("F3"));
            foreach(var collider in Physics.OverlapCapsule(feet+Vector3.up*.48f,feet+Vector3.up*1.62f,.29f,~0,QueryTriggerInteraction.Ignore))
                rows.Add("overlap "+Description(collider));
            RaycastHit ground;
            if(Physics.Raycast(feet+Vector3.up*1.6f,Vector3.down,out ground,65,~0,QueryTriggerInteraction.Ignore))
                rows.Add("ground "+Description(ground.collider)+" hit="+Point(ground.point));
            else rows.Add("no ground within 65m");
            return string.Join(" | ",rows.ToArray());
        }
        static string Description(Collider c)
        {
            return c.name+" parent="+(c.transform.parent?c.transform.parent.name:"none")+" centre="+Point(c.bounds.center)+" size="+Point(c.bounds.size);
        }

        static bool Walk(GameController game,Transform eye,Vector3 target,string label)
        {
            int stalled=0;float distance=Vector3.Distance(Feet(eye),target);
            int budget=Mathf.Max(1000,Mathf.CeilToInt(distance/.025f)+800);
            for(int step=0;step<budget;step++)
            {
                var feet=Feet(eye);var delta=target-feet;delta.y=0;
                if(delta.magnitude<.065f)
                {
                    for(int settle=0;settle<30;settle++)Move.Invoke(game,new object[]{Vector3.zero,Dt});
                    var actual=Feet(eye);bool correct=Mathf.Abs(actual.y-target.y)<.32f;
                    Check(correct,label+" wrong floor "+Point(actual)+" expected "+Point(target)+" | "+Obstruction(actual,target));
                    return correct;
                }
                var amount=delta.normalized*Mathf.Min(.06f,delta.magnitude);
                Move.Invoke(game,new object[]{amount,Dt});
                float travelled=Vector3.Distance(feet,Feet(eye));walkedMetres+=travelled;
                if(travelled<.0005f)stalled++;else stalled=0;
                if(stalled>=15)
                {
                    Check(false,label+" blocked at "+Point(Feet(eye))+" toward "+Point(target)+" | "+Obstruction(Feet(eye),target));return false;
                }
            }
            Check(false,label+" timeout at "+Point(Feet(eye))+" toward "+Point(target)+" | "+Obstruction(Feet(eye),target));return false;
        }

        static void CheckExits(WorldBuilder world)
        {
            // Independent expected coordinates from the researched OSM node projection.
            var expected=new[]{new Vector3(143.57f,0,-302.81f),new Vector3(120.99f,0,-522.34f),new Vector3(59.81f,0,-420.11f),new Vector3(35.17f,0,-309.91f)};
            var portals=(List<StationPortal>)Get(world,"mappedPortals");Check(portals.Count==4,"exactly four mapped station exits; got "+portals.Count);
            Check(world.MappedEntranceCount==4,"UI mapped entrance count must be four; got "+world.MappedEntranceCount);
            for(int i=0;i<Mathf.Min(portals.Count,expected.Length);i++)
            {
                var entry=portals[i].transform.parent;var actual=entry.position;actual.y=0;
                Check(Vector3.Distance(actual,expected[i])<.06f,"exit "+(i+1)+" moved away from map: "+Point(actual)+" expected "+Point(expected[i]));
                Check(portals[i].walkThrough,"exit "+(i+1)+" must use physical stairs");
                var route=entry.GetComponent<StationWalkRoute>();Check(route!=null&&route.points.Length>=3,"exit "+(i+1)+" must have a walking route");
                if(route!=null&&route.points.Length>0)
                    Check(Vector3.Distance(route.points[0],entry.TransformPoint(new Vector3(0,.14f,2.05f)))<.08f,"exit "+(i+1)+" stairs must start at its actual mouth");
            }
        }

        static List<Transform> CheckPlatforms(WorldBuilder world)
        {
            var modules=(List<Transform>)Get(world,"gimpoIslands");Check(modules.Count==5,"five physical platform levels/modules");
            Check(world.StationTrains.Count==10&&world.PlatformSides.Count==10,"ten train sides retained after shared-platform regrouping");
            var yaw=new[]{170f,94f,94f,94f,170f};
            for(int i=0;i<Mathf.Min(5,modules.Count);i++)
            {
                var module=modules[i];var wanted=Quaternion.Euler(0,yaw[i],0)*Vector3.forward;
                Check(Vector3.Dot(module.forward,wanted)>.995f,"module "+i+" longitudinal direction does not follow the geographic footprint");
                var trains=module.GetComponentsInChildren<SubwayTrain>();Check(trains.Length==2,"module "+i+" needs two train sides");
                bool sidePlatform=i==0||i==3||i==4;
                foreach(var train in trains)
                {
                    Check(Mathf.Abs(Mathf.Abs(train.trackX)-(sidePlatform?2.6f:7.6f))<.02f,"module "+i+" incorrect side/island track position");
                    foreach(var consist in train.consists)
                        Check(consist.cabin!=null&&consist.cabin.doorSide==(sidePlatform?Mathf.Sign(train.trackX):-Mathf.Sign(train.trackX)),"module "+i+" doors open away from its platform");
                }
                if(i==1||i==2)
                {
                    bool nine=false,arex=false;
                    foreach(var side in world.PlatformSides)if(side.island==i)
                    {
                        if(side.line=="9호선"){nine=true;Check(side.toward.Contains("중앙보훈")==(i==1),"shared level "+i+" Line 9 direction mismatch");}
                        if(side.line=="공항철도"){arex=true;Check(side.toward.Contains("서울행")==(i==1),"shared level "+i+" AREX direction mismatch");}
                    }
                    Check(nine&&arex,"shared level "+i+" must contain both Line 9 and AREX");
                }
            }
            if(modules.Count>=3)
            {
                Check(Vector3.Distance(modules[1].position,modules[2].position)<.02f,"shared B3/B4 platforms stack in the same footprint");
                Check(world.IslandFloor(2)<world.IslandFloor(1)-5f,"shared B4 is below B3 with usable headroom");
            }
            return modules;
        }

        static void CheckBoardFit(List<Transform> modules)
        {
            int checkedBoards=0;
            foreach(var module in modules)foreach(var board in module.GetComponentsInChildren<MeshRenderer>())
            {
                if(board.name!="안내판")continue;
                var p=board.transform.parent;int next=board.transform.GetSiblingIndex()+1;
                if(next>=p.childCount)continue;var text=p.GetChild(next).GetComponent<TextMesh>();
                if(text==null)continue;var renderer=text.GetComponent<MeshRenderer>();var bounds=renderer.localBounds;
                bool contained=true;
                foreach(float x in new[]{bounds.min.x,bounds.max.x})foreach(float y in new[]{bounds.min.y,bounds.max.y})
                {
                    var local=board.transform.InverseTransformPoint(text.transform.TransformPoint(new Vector3(x,y,bounds.center.z)));
                    if(Mathf.Abs(local.x)>.51f||Mathf.Abs(local.y)>.51f)contained=false;
                }
                Check(contained,"text overflows board: "+text.text.Replace('\n',' '));checkedBoards++;
            }
            Check(checkedBoards>10,"platform direction/name boards were checked; got "+checkedBoards);
        }

        static void CheckHeadroom(WorldBuilder world,StationWalkRoute[] routes,List<Transform> modules)
        {
            var obstructions=new List<Renderer>();
            foreach(var renderer in world.root.GetComponentsInChildren<Renderer>())
            {
                string n=renderer.name;
                if(n=="안내판"||n=="도착 안내 화면"||n=="방면 안내"||n=="승강장 조명"||n=="기둥"||n=="승강장 기둥")obstructions.Add(renderer);
            }
            var paths=new List<Vector3[]>();foreach(var route in routes)paths.Add(route.points);
            // Include each long side-platform route from its stair mouth to the far end.
            for(int i=0;i<modules.Count;i++)if(i==0||i==3||i==4)
            {
                float half=0;
                foreach(var renderer in modules[i].GetComponentsInChildren<Renderer>())if(renderer.name=="상대식 승강장 바닥"){half=renderer.transform.localScale.z*.5f;break;}
                Check(half>=70,"side-platform "+i+" has a full-length walking floor");
                foreach(float x in new[]{-9.3f,9.3f})paths.Add(new[]{modules[i].TransformPoint(new Vector3(x,world.IslandFloor(i),-half+.5f)),modules[i].TransformPoint(new Vector3(x,world.IslandFloor(i),half-1))});
            }
            var reported=new HashSet<Renderer>();
            foreach(var path in paths)for(int k=1;k<path.Length;k++)
            {
                int samples=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(path[k-1],path[k])/.6f));
                for(int step=0;step<=samples;step++)
                {
                    var feet=Vector3.Lerp(path[k-1],path[k],step/(float)samples);var body=new Bounds(feet+Vector3.up*1.05f,new Vector3(.5f,1.7f,.5f));
                    foreach(var renderer in obstructions)
                    {
                        if(reported.Contains(renderer)||!renderer.enabled||!body.Intersects(renderer.bounds))continue;
                        // Test in the renderer's own axes, avoiding false positives from
                        // broad world AABBs around long rotated signs and light strips.
                        var bounds=renderer.localBounds;bool intersects=false;
                        foreach(float h in new[]{.4f,.8f,1.2f,1.65f,1.85f})
                        {
                            var local=renderer.transform.InverseTransformPoint(feet+Vector3.up*h);
                            var scale=renderer.transform.lossyScale;
                            var padded=bounds;padded.Expand(new Vector3(.5f/Mathf.Max(.01f,Mathf.Abs(scale.x)),.05f/Mathf.Max(.01f,Mathf.Abs(scale.y)),.5f/Mathf.Max(.01f,Mathf.Abs(scale.z))));
                            if(padded.Contains(local)){intersects=true;break;}
                        }
                        if(intersects){reported.Add(renderer);Check(false,"walking headroom obstructed by "+renderer.name+" at feet "+Point(feet)+" bounds "+renderer.bounds+" parent="+renderer.transform.parent.name);}
                    }
                }
            }
        }

        static void WalkAllRoutes(WorldBuilder world,GameController game,Transform eye,StationWalkRoute[] routes)
        {
            foreach(var gate in world.FareGates){gate.Open();if(gate.blocker!=null)gate.blocker.enabled=false;}
            Physics.SyncTransforms();
            foreach(var route in routes)
            {
                if(route.points==null||route.points.Length<2){Check(false,route.name+" has no continuous walking path");continue;}
                Reset(game,eye,route.points[0]);bool forward=true,backward=true;
                for(int k=0;k<route.points.Length;k++)if(!Walk(game,eye,route.points[k],route.name+" forward point "+k)){forward=false;break;}
                // A separate start only follows a failed outward path, so reverse
                // failures can still be diagnosed without claiming a through walk.
                if(!forward)Reset(game,eye,route.points[route.points.Length-1]);
                for(int k=route.points.Length-2;k>=0;k--)if(!Walk(game,eye,route.points[k],route.name+" return point "+k)){backward=false;break;}
                if(forward&&backward)walkedRoutes++;
                Debug.Log("GimpoLayoutCheck route "+route.name+": forward="+forward+", return="+backward+", points="+route.points.Length);
            }
            Check(walkedRoutes==routes.Length,"all station stair/exit/transfer/airport routes must walk both ways: "+walkedRoutes+"/"+routes.Length);
        }

        static void CheckBoarding(WorldBuilder world,GameController game,Transform eye)
        {
            // All sides are tested physically with their real lines: boarding starts the ride, which stays at
            // the platform until the doors close. No door/collider is bypassed.
            for(int index=0;index<world.StationTrains.Count;index++)
            {
                Call(game,"ClearRides");var train=world.StationTrains[index];var original=train.line;
                                train.Begin(SubwayTrain.Approach+4f);Physics.SyncTransforms();
                var consist=train.Active;Check(consist!=null&&consist.cabin!=null,"side "+index+" has an active cabin");
                if(consist==null||consist.cabin==null){train.line=original;continue;}
                var cabin=consist.cabin;float sign=cabin.doorSide;float door=cabin.doors[3];
                var start=cabin.transform.TransformPoint(new Vector3(sign*5.6f,cabin.floor,door));Reset(game,eye,start);Set(game,"farePaid",true);
                var amount=cabin.transform.TransformDirection(Vector3.left*(sign*.06f));
                for(int step=0;step<110&&Get(game,"cabin")==null;step++)
                {
                    Enter.Invoke(game,new object[]{amount});if(Get(game,"cabin")==null)Move.Invoke(game,new object[]{amount,Dt});
                }
                bool entered=Get(game,"cabin")==cabin;
                Check(entered,"walk into rotated platform side "+index+" "+original.line+" failed at "+Point(Feet(eye))+" | "+Obstruction(Feet(eye),cabin.transform.TransformPoint(new Vector3(0,cabin.floor,door))));
                if(entered)
                {
                    boardedSides++;var local=cabin.transform.InverseTransformPoint(Feet(eye));
                    Check(Mathf.Abs(local.x)<cabin.halfWidth&&Mathf.Abs(local.y-cabin.floor)<.03f,"side "+index+" actual cabin state/feet must be inside the carriage");
                    // Move through the open car doorway, then continue over the lip.
                    for(int k=0;k<30&&Get(game,"cabin")!=null;k++)Call(game,"MoveInCabin",-amount);
                    Check(Get(game,"cabin")==null,"side "+index+" can walk back out of the open train");
                    var off=Feet(eye);RaycastHit ground;
                    bool supported=Physics.Raycast(off+Vector3.up*.15f,Vector3.down,out ground,.35f,~0,QueryTriggerInteraction.Ignore);
                    Check(supported,"side "+index+" exit lands on a platform lip rather than the tracks: "+Point(off));
                }
                Call(game,"ClearRides");train.line=original;train.Begin(SubwayTrain.Approach+4f);Physics.SyncTransforms();
            }
        }

        static void CheckSweptBoardingAndEdges(WorldBuilder world,GameController game,Transform eye)
        {
            int swept=0,protectedCases=0;
            for(int index=0;index<world.StationTrains.Count;index++)
            {
                Call(game,"ClearRides");var train=world.StationTrains[index];var original=train.line;
                                try
                {
                    train.Begin(SubwayTrain.Approach+4f);Physics.SyncTransforms();
                    var active=train.Active;Check(active!=null&&active.cabin!=null,"swept side "+index+" requires an active cabin");
                    if(active==null||active.cabin==null)continue;
                    var c=active.cabin;float sign=c.doorSide,door=c.doors[3];
                    // A two-metre diagonal step crosses an open door but ends
                    // beyond its 0.62 m half-width. The old endpoint-only test
                    // fails to board this path on BOTH island and side platforms.
                    var before=new Vector3(sign*3f,c.floor,door);
                    var after=new Vector3(sign*1f,c.floor,door+.8f);
                    Check(!c.DoorAt(after.z),"swept regression endpoint must lie outside the door");
                    var start=c.transform.TransformPoint(before);var step=c.transform.TransformDirection(after-before);
                    Reset(game,eye,start);Set(game,"farePaid",true);
                    bool consumed=(bool)Enter.Invoke(game,new object[]{step});
                    bool boarded=Get(game,"cabin")==c;
                    Check(consumed&&boarded,"slow diagonal step must board side "+index+" "+original.line+" (end outside door, swept crossing inside) at "+Point(Feet(eye))+" | "+Obstruction(Feet(eye),start+step));
                    if(boarded)
                    {
                        swept++;var local=c.transform.InverseTransformPoint(Feet(eye));
                        Check(c.DoorAt(local.z)&&Mathf.Abs(local.x)<c.halfWidth,"swept boarding places feet inside the actual doorway, not at the invalid endpoint");
                    }
                    Call(game,"ClearRides");
                    foreach(float phase in new[]{SubwayTrain.Approach+.1f,SubwayTrain.Approach+SubwayTrain.Dwell+2f})
                    {
                        train.Begin(phase);Physics.SyncTransforms();
                        Check(!train.Active.cabin.Open,"closed/departing edge fixture must have shut doors");
                        // Stand on the static concrete lip, already through the
                        // PSD opening, as can happen after missing a narrower car
                        // doorway. Deliberately step across the track edge in one
                        // slow frame using the exact normal player call sequence.
                        var feet=train.transform.TransformPoint(new Vector3(sign*2.02f,c.floor,door));
                        var inward=train.transform.TransformDirection(new Vector3(-sign*.8f,0,.12f));
                        Reset(game,eye,feet);Set(game,"farePaid",true);
                        bool handled=(bool)Enter.Invoke(game,new object[]{inward});
                        if(!handled)Move.Invoke(game,new object[]{inward,.2f});
                        var actual=train.transform.InverseTransformPoint(Feet(eye));
                        bool stayed=Get(game,"cabin")==null&&actual.x*sign>=1.9f&&Mathf.Abs(actual.y-c.floor)<.15f;
                        Check(handled&&stayed,"closed/departing train must retain walker on lip: side="+index+" phase="+phase+" handled="+handled+" actual="+Point(actual));
                        if(handled&&stayed)protectedCases++;
                    }
                    // No visible train: the guard belongs to the static platform,
                    // not to a departing consist's moving transform or collider.
                    foreach(var consist in train.consists)consist.suppressed=true;
                    train.Begin(SubwayTrain.Approach+4f);Physics.SyncTransforms();
                    Reset(game,eye,train.transform.TransformPoint(new Vector3(sign*2.02f,c.floor,door)));
                    var emptyStep=train.transform.TransformDirection(new Vector3(-sign*.8f,0,0));
                    bool emptyHandled=(bool)Enter.Invoke(game,new object[]{emptyStep});
                    if(!emptyHandled)Move.Invoke(game,new object[]{emptyStep,.2f});
                    Check(emptyHandled&&Get(game,"cabin")==null&&train.transform.InverseTransformPoint(Feet(eye)).x*sign>=1.9f,"empty platform retains walker at track edge on side "+index);
                    // Protection must not stop retreating toward the platform.
                    var outward=train.transform.TransformDirection(new Vector3(sign*.2f,0,0));
                    bool blocksRetreat=(bool)Call(game,"CrossesUnboardedPlatformEdge",Feet(eye),Feet(eye)+outward);
                    Check(!blocksRetreat,"edge guard allows retreat on side "+index);
                }
                finally
                {
                    Call(game,"ClearRides");foreach(var consist in train.consists){consist.suppressed=false;consist.manual=false;}
                    train.line=original;train.Begin(SubwayTrain.Approach+4f);Physics.SyncTransforms();
                }
            }
            Check(swept==world.StationTrains.Count,"all rotated platform sides accept swept doorway entry: "+swept);
            Check(protectedCases==world.StationTrains.Count*2,"all closed/departing cases retain platform support: "+protectedCases);
            Debug.Log("GimpoLayoutCheck swept regressions: diagonal boarding="+swept+", closed/departing protection="+protectedCases+", empty-track and retreat checks completed");
        }

        static void CheckFullPlatformEdges(WorldBuilder world,GameController game,Transform eye)
        {
            int checkedPositions=0;
            for(int index=0;index<world.StationTrains.Count;index++)
            {
                Call(game,"ClearRides");var train=world.StationTrains[index];
                var c=train.consists[0].cabin;float sign=c.doorSide,half=world.PlatformHalfLength(train);
                Check(half>45f,"long-platform edge fixture has an extension on side "+index);
                // 39.7 is already beyond Cabin.front/back but still on the lip.
                // 40.15 passes its end; 44.4 reproduces the manual B3 fall; the
                // final pair checks each ACTUAL platform endpoint, not fixed 70m.
                foreach(float along in new[]{-39.7f,39.7f,-40.15f,40.15f,-44.4f,44.4f,-half+.75f,half-.75f})
                {
                    var start=train.transform.TransformPoint(new Vector3(sign*4.2f,c.floor,along));
                    var inward=train.transform.TransformDirection(new Vector3(-sign,0,0));
                    Reset(game,eye,start);Set(game,"farePaid",true);Physics.SyncTransforms();
                    bool hasScreen=false;
                    foreach(var hit in Physics.SphereCastAll(start+Vector3.up*.95f,.2f,inward,4.4f,~0,QueryTriggerInteraction.Ignore))
                    {
                        var screen=hit.collider.GetComponent<ScreenDoor>();
                        if(screen!=null&&screen.train==train&&hit.collider.name=="스크린도어 고정벽"){hasScreen=true;break;}
                    }
                    Check(hasScreen,"physical fixed screen must enclose long platform: side="+index+" z="+along+" half="+half);
                    // Input path: a slow frame must be consumed before feet can
                    // leave the slab even beyond the train's 39.6 m cabin bounds.
                    bool consumed=(bool)Enter.Invoke(game,new object[]{inward*3.4f});
                    if(!consumed)Move.Invoke(game,new object[]{inward*3.4f,.3f});
                    var local=train.transform.InverseTransformPoint(Feet(eye));
                    Check(consumed&&Get(game,"cabin")==null&&local.x*sign>=4.1f&&Mathf.Abs(local.y-c.floor)<.15f,"long-platform safety guard missed edge: side="+index+" z="+along+" actual="+Point(local));
                    // Independently exercise the physical fence with the regular
                    // collision motor, deliberately without the boarding guard.
                    Reset(game,eye,start);
                    for(int n=0;n<80;n++)Move.Invoke(game,new object[]{inward*.06f,Dt});
                    local=train.transform.InverseTransformPoint(Feet(eye));
                    RaycastHit support;bool supported=Physics.Raycast(Feet(eye)+Vector3.up*.12f,Vector3.down,out support,.3f,~0,QueryTriggerInteraction.Ignore);
                    Check(supported&&local.x*sign>2.7f&&Mathf.Abs(local.y-c.floor)<.15f,"fixed screen must physically stop walker before the track: side="+index+" z="+along+" actual="+Point(local)+" | "+Obstruction(Feet(eye),Feet(eye)+inward));
                    Check(world.NearestSide(start+Vector3.up*EyeHeight)==index,"nearest-side selection must retain current floor at platform extension: side="+index+" z="+along);
                    checkedPositions++;
                }
            }
            Debug.Log("GimpoLayoutCheck full platform edge regressions: "+checkedPositions+" positions with physical screen, normal motor, safety guard and nearest-side checks");
        }

        public static void Run()
        {
            failures=walkedRoutes=boardedSides=0;walkedMetres=0;
            try
            {
                var camera=new GameObject("Gimpo layout camera").AddComponent<Camera>();
                var world=new GameObject("Gimpo layout world").AddComponent<WorldBuilder>();world.worldCamera=camera;
                world.BuildDistrict(3);Physics.SyncTransforms();
                var host=new GameObject("Gimpo layout walker");host.SetActive(false);var game=host.AddComponent<GameController>();game.state=new GameState();game.cardBalance=0;
                var eye=new GameObject("Gimpo layout eye").transform;Set(game,"world",world);Set(game,"eye",eye);
                CheckExits(world);var modules=CheckPlatforms(world);CheckBoardFit(modules);
                var routes=world.root.GetComponentsInChildren<StationWalkRoute>();Check(routes.Length>=18,"all mapped entrance and platform access routes exist; got "+routes.Length);
                CheckHeadroom(world,routes,modules);WalkAllRoutes(world,game,eye,routes);CheckBoarding(world,game,eye);CheckSweptBoardingAndEdges(world,game,eye);CheckFullPlatformEdges(world,game,eye);
                Object.DestroyImmediate(host);Object.DestroyImmediate(eye.gameObject);
            }
            catch(Exception error){Check(false,"uncaught test exception "+error);}
            Debug.Log("GimpoLayoutCheck: "+(failures==0?"passed":failures+" failed")+" · both-way routes="+walkedRoutes+", actual boarding sides="+boardedSides+", motor distance="+walkedMetres.ToString("F1")+"m");
            EditorApplication.Exit(failures==0?0:1);
        }
    }
}
