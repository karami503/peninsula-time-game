using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Builds every generated Korean airport and checks: runway at its real heading and length, a walkable arrivals hall
    // that reaches the kiosks, a gate and the exit, one aircraft per gate whose departure lines up on the runway and
    // climbs away, and one LODGroup per structure. Then splits the Gangnam OSM district and checks the modular cut:
    // many building/road structures, no chunk larger than a block, and not one triangle lost or duplicated.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.KoreanAirportsCheck.Run
    public static class KoreanAirportsCheck
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static int failures;
        static readonly MethodInfo Move=typeof(GameController).GetMethod("MoveWalkerStep",Flags);
        static void Set(object o,string name,object value){o.GetType().GetField(name,Flags).SetValue(o,value);}
        static void Check(bool ok,string message){if(!ok){failures++;Debug.LogError("KoreanAirportsCheck: "+message);}}

        static bool Walk(GameController game,Transform eye,Vector3 target,string label)
        {
            int stalled=0;
            for(int step=0;step<12000;step++)
            {
                var feet=eye.position-Vector3.up*1.65f;var delta=target-feet;delta.y=0;
                if(delta.magnitude<.1f)return true;
                Move.Invoke(game,new object[]{delta.normalized*Mathf.Min(.075f,delta.magnitude),1f/60});
                var after=eye.position-Vector3.up*1.65f;
                if((after-feet).sqrMagnitude<.000001f)stalled++;else stalled=0;
                if(stalled>8)
                {
                    RaycastHit hit;Physics.SphereCast(feet+Vector3.up*.85f,.3f,delta.normalized,out hit,.35f);
                    Check(false,label+" blocked at "+feet+" by "+(hit.collider?hit.collider.name:"floor/step"));return false;
                }
            }
            Check(false,label+" timeout");return false;
        }
        // Stand point just inside the terminal in front of a fixture or door.
        static Vector3 Front(Component c,Transform site,float inward)
        {
            var p=c.GetComponent<Collider>().bounds.center;var local=site.InverseTransformPoint(p);
            local.z+=local.z<0?inward:-inward;local.y=0;return site.TransformPoint(local);
        }
        static void RunBudget(SceneRenderBudget budget)
        {
            var routine=(IEnumerator)typeof(SceneRenderBudget).GetMethod("Start",Flags).Invoke(budget,null);
            int yields=0;while(routine.MoveNext())if(++yields>100000){Check(false,"render budget did not finish");return;}
        }

        static void CheckAirport(KoreanAirport a,WorldBuilder world,GameController game,Transform eye)
        {
            world.BuildDestinationAirport(a);Physics.SyncTransforms();
            var site=world.root.transform.Find(a.name);
            Check(site!=null,a.iata+" site missing");if(site==null)return;
            // Runway: real length (±1 m) along the real heading.
            Transform runway=null;foreach(var t in site.GetComponentsInChildren<Transform>())if(t.name=="활주로 포장")runway=t;
            Check(runway!=null&&Mathf.Abs(runway.localScale.x-a.runway)<1f,a.iata+" runway length "+(runway?runway.localScale.x:0)+" expected "+a.runway);
            var along=site.right;float heading=(Mathf.Atan2(along.x,along.z)*Mathf.Rad2Deg+360f)%360f;
            Check(Mathf.Abs(Mathf.DeltaAngle(heading,a.heading))<.5f,a.iata+" runway heading "+heading+" expected "+a.heading);
            // Fixtures, gates, aircraft.
            var kiosks=new List<Fixture>();var gates=new List<Fixture>();
            foreach(var f in site.GetComponentsInChildren<Fixture>()){if(f.kind=="kiosk")kiosks.Add(f);if(f.kind=="gate")gates.Add(f);}
            Check(kiosks.Count>=2,a.iata+" kiosks "+kiosks.Count);
            Check(gates.Count==a.Gates,a.iata+" gate doors "+gates.Count+" expected "+a.Gates);
            var exit=site.GetComponentInChildren<AirportExit>();
            Check(exit!=null&&exit.city.Length>0,a.iata+" landside exit with a city");
            for(int g=1;g<=a.Gates;g++)Check(world.GateAircraft(g)!=null,a.iata+" gate "+g+" aircraft");
            // Arrival: on the floor, not inside anything, and the kiosks, a gate and the exit reachable on foot.
            RaycastHit floor;var spawn=world.DestinationArrivalSpawn;
            Check(Physics.Raycast(spawn,Vector3.down,out floor,3f)&&floor.point.y>-.05f&&floor.point.y<.05f,a.iata+" arrival spawn has no terminal floor");
            Check(!Physics.CheckSphere(spawn,.3f,~0,QueryTriggerInteraction.Ignore),a.iata+" arrival spawn inside a collider");
            eye.position=spawn;Set(game,"grounded",true);Set(game,"verticalSpeed",0f);
            // The route a player takes: hall centre to a kiosk, along the cross-aisle (no column row) to the last gate's lane,
            // up to its door, and back down the centre line (no columns) to the exit doors.
            var hall=spawn-Vector3.up*1.65f;
            var aisle=gates.Count>0?site.TransformPoint(new Vector3(site.InverseTransformPoint(gates[gates.Count-1].transform.position).x,0,site.InverseTransformPoint(hall).z)):hall;
            var route=new List<KeyValuePair<string,Vector3>>();
            kiosks.Sort((x,y)=>Mathf.Abs(site.InverseTransformPoint(x.transform.position).x).CompareTo(Mathf.Abs(site.InverseTransformPoint(y.transform.position).x)));
            if(kiosks.Count>0){route.Add(new KeyValuePair<string,Vector3>("to kiosk",Front(kiosks[0],site,1.6f)));route.Add(new KeyValuePair<string,Vector3>("back to hall",hall));}
            if(gates.Count>0){route.Add(new KeyValuePair<string,Vector3>("to gate aisle",aisle));route.Add(new KeyValuePair<string,Vector3>("to last gate",Front(gates[gates.Count-1],site,1.2f)));
                route.Add(new KeyValuePair<string,Vector3>("back to aisle",aisle));route.Add(new KeyValuePair<string,Vector3>("back to hall",hall));}
            if(exit!=null)route.Add(new KeyValuePair<string,Vector3>("to exit",Front(exit,site,1.2f)));
            foreach(var leg in route)if(!Walk(game,eye,leg.Value,a.iata+" "+leg.Key))break;
            // Departure: lines up on the runway, then climbs above 100 m along the runway heading.
            var plane=world.GateAircraft(1);
            if(plane!=null&&runway!=null)
            {
                var flight=plane.AddComponent<PlaneFlight>();world.ConfigureAirportFlight(flight,1);
                var last=flight.taxiPath[flight.taxiPath.Length-1];var onRunway=site.InverseTransformPoint(last);
                Check(Mathf.Abs(onRunway.z-runway.localPosition.z)<1f&&Mathf.Abs(onRunway.x)<a.runway*.5f,a.iata+" lineup not on the runway centre line: "+onRunway);
                flight.clock=flight.Duration;flight.Step(0);
                Check(flight.transform.position.y>100f&&Vector3.Dot(flight.transform.position-last,along)>700f,a.iata+" did not climb out along the runway: "+flight.transform.position);
                Object.DestroyImmediate(flight);
            }
            // Structures and LOD.
            var budget=world.root.AddComponent<SceneRenderBudget>();budget.world=world;budget.enabled=false;RunBudget(budget);
            var terminal=site.Find(a.name+" 여객터미널");
            Check(terminal!=null&&terminal.GetComponent<LODGroup>()!=null&&terminal.GetComponent<LODGroup>().lodCount==2,a.iata+" terminal has no two-level LOD");
            Check(budget.StructureCount>=7,a.iata+" structures "+budget.StructureCount);
            foreach(var r in site.GetComponentsInChildren<MeshRenderer>())
                if(r.enabled&&r.GetComponent<TextMesh>()==null&&r.GetComponentInParent<Structure>()==null&&r.GetComponentInParent<RenderMovingRoot>()==null)
                {Check(false,a.iata+" renderer outside any structure: "+r.name);break;}
            Debug.Log("KoreanAirportsCheck "+a.iata+" "+a.name+": gates "+a.Gates+", kiosks "+kiosks.Count+", structures "+budget.StructureCount+", batches "+budget.BatchCount);
        }

        static void CheckDistrictSplit()
        {
            var prefab=Resources.Load<GameObject>("Models/GangnamOSM");Check(prefab!=null,"GangnamOSM model missing");if(prefab==null)return;
            var source=Object.Instantiate(prefab);
            // As in BuildDistrict: the imported ground plane is hidden (the game lays its own) and is not split.
            foreach(var r in source.GetComponentsInChildren<MeshRenderer>())if(r.name.ToLowerInvariant().Contains("ground"))r.enabled=false;
            int before=0;foreach(var f in source.GetComponentsInChildren<MeshFilter>())if(f.sharedMesh!=null&&f.GetComponent<MeshRenderer>().enabled)for(int s=0;s<f.sharedMesh.subMeshCount;s++)before+=(int)f.sharedMesh.GetIndexCount(s)/3;
            int structures=ModularMesh.Split(source.transform);
            int after=0,buildings=0,roads=0,unsplit=0;float largest=0;
            foreach(var f in source.GetComponentsInChildren<MeshFilter>())
            {
                if(f.sharedMesh==null||!f.GetComponent<MeshRenderer>().enabled)continue;
                for(int s=0;s<f.sharedMesh.subMeshCount;s++)after+=(int)f.sharedMesh.GetIndexCount(s)/3;
                var renderer=f.GetComponent<MeshRenderer>();
                if(f.GetComponentInParent<Structure>()==null){if(renderer!=null&&renderer.enabled)unsplit++;continue;}
                var size=renderer.bounds.size;largest=Mathf.Max(largest,Mathf.Max(size.x,size.z));
            }
            foreach(var s in source.GetComponentsInChildren<Structure>())if(s.proxy)buildings++;else roads++;
            Check(before==after,"split lost or duplicated triangles: "+before+" -> "+after);
            Check(buildings>100&&roads>20,"too few structures: buildings "+buildings+", road segments "+roads);
            Check(unsplit==0,"district-wide meshes left unsplit: "+unsplit);
            Check(largest<400f,"a chunk spans "+largest+" m, larger than a block");
            Debug.Log("KoreanAirportsCheck Gangnam split: "+structures+" structures ("+buildings+" buildings, "+roads+" road segments), "+after+" triangles, largest chunk "+largest.ToString("F0")+" m");
            Object.DestroyImmediate(source);
        }

        public static void Run()
        {
            failures=0;
            try
            {
                var camera=new GameObject("airports check camera").AddComponent<Camera>();
                var world=new GameObject("airports check world").AddComponent<WorldBuilder>();world.worldCamera=camera;
                var host=new GameObject("airports check walker");host.SetActive(false);var game=host.AddComponent<GameController>();game.state=new GameState();Set(game,"world",world);
                var eye=new GameObject("airports check eye").transform;Set(game,"eye",eye);
                foreach(var a in KoreanAirports.All)if(a.iata!=KoreanAirports.Gimpo)CheckAirport(a,world,game,eye);
                CheckDistrictSplit();
            }
            catch(System.Exception e){failures++;Debug.LogException(e);}
            Debug.Log("KoreanAirportsCheck: "+(failures==0?"passed":failures+" failed"));
            EditorApplication.Exit(failures==0?0:1);
        }
    }
}
