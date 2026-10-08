using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PeninsulaTime
{
    // Advances the real batching coroutine manually, without entering play mode.
    public static class RenderBudgetCheck
    {
        const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
        static int failures;
        static readonly FieldInfo Quality=typeof(PerformanceRuntime).GetField("level",BindingFlags.Static|BindingFlags.NonPublic);
        static void Expect(bool condition,string message){if(!condition){failures++;Debug.LogError("RenderBudgetCheck: "+message);}}
        static Transform Child(Transform parent,string name,Vector3 at)
        {
            var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=at;return t;
        }
        sealed class Fixture:IDisposable
        {
            public readonly GameObject owner;
            public readonly WorldBuilder world;
            public readonly SceneRenderBudget budget;
            public readonly Material material;
            public Transform Root{get{return world.root.transform;}}
            public Camera Camera{get{return world.worldCamera;}}
            public Fixture()
            {
                owner=new GameObject("Render budget check");world=owner.AddComponent<WorldBuilder>();world.enabled=false;
                world.root=Child(owner.transform,"Generated fixture",Vector3.zero).gameObject;
                world.worldCamera=Child(owner.transform,"Test camera",new Vector3(15,1.65f,15)).gameObject.AddComponent<Camera>();
                world.worldCamera.enabled=false;world.worldCamera.orthographic=false;
                material=new Material(Shader.Find("Standard"));material.renderQueue=2000;
                budget=world.root.AddComponent<SceneRenderBudget>();budget.enabled=false;budget.world=world;
            }
            public Renderer Cube(Transform parent,string name,Vector3 at)
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
                go.transform.localPosition=at;go.transform.localScale=new Vector3(.7f,2,.5f);
                var r=go.GetComponent<Renderer>();r.sharedMaterial=material;return r;
            }
            public List<Renderer> Body(Transform parent)
            {
                var result=new List<Renderer>();
                // Four same-material pieces would be merged if the dynamic ancestor
                // exclusion were removed. Nested ownership must work as well.
                var nested=Child(parent,"Nested model",Vector3.zero);
                for(int i=0;i<4;i++)result.Add(Cube(nested,"Body "+i,new Vector3(i,1,0)));
                return result;
            }
            public void StartBudget()
            {
                var routine=(IEnumerator)typeof(SceneRenderBudget).GetMethod("Start",PrivateInstance).Invoke(budget,null);
                int yields=0;
                while(routine.MoveNext())if(++yields>10000)throw new InvalidOperationException("Batch coroutine did not finish");
                Expect(budget.Ready,"real batching coroutine did not reach Ready");
            }
            public void Dispose()
            {
                // Runtime uses deferred mesh destruction. Dispose its owned meshes
                // explicitly for this edit-mode harness, leaving production unchanged.
                var field=typeof(SceneRenderBudget).GetField("owned",PrivateInstance);
                var meshes=field!=null?field.GetValue(budget) as List<Mesh>:null;
                if(meshes!=null){foreach(var mesh in meshes)if(mesh!=null)Object.DestroyImmediate(mesh);meshes.Clear();}
                Object.DestroyImmediate(owner);Object.DestroyImmediate(material);
            }
        }
        static int DrawMeshes(Transform root)
        {
            int count=0;foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))if(Visible(r))count++;return count;
        }
        static bool Visible(Renderer r){return r!=null&&r.enabled&&!r.forceRenderingOff&&r.gameObject.activeInHierarchy;}
        static bool AllEnabled(List<Renderer> renderers,bool enabled)
        {
            foreach(var r in renderers)if(r.enabled!=enabled)return false;return true;
        }
        static void CheckStaticAndDynamic()
        {
            using(var f=new Fixture())
            {
                var walls=new List<Renderer>();
                for(int i=0;i<8;i++)walls.Add(f.Cube(f.Root,"Near wall "+i,new Vector3(4+i*3,1,10)));
                for(int i=0;i<4;i++)walls.Add(f.Cube(f.Root,"Far wall "+i,new Vector3(880+i*3,1,10)));
                var invisible=f.Cube(f.Root,"Invisible collision ramp",new Vector3(10,1,20));invisible.enabled=false;
                var models=new List<List<Renderer>>();var owners=new List<Transform>();
                for(int i=0;i<4;i++)
                {
                    var root=Child(f.Root,"Moving owner "+i,new Vector3(52+i*12,0,12));owners.Add(root);
                    if(i==0)root.gameObject.AddComponent<RenderMovingRoot>();
                    if(i==1)root.gameObject.AddComponent<Cabin>();
                    if(i==2)root.gameObject.AddComponent<VehicleDoors>();
                    if(i==3)root.gameObject.AddComponent<TrainConsist>();
                    models.Add(f.Body(root));
                }
                int before=DrawMeshes(f.Root);f.StartBudget();
                Expect(f.budget.CombinedSources==walls.Count,"batched a dynamic vehicle/door or missed simple static wall fixtures; sources="+f.budget.CombinedSources);
                Expect(f.budget.BatchCount==2,"near and far wall cells should produce two independent batches; got "+f.budget.BatchCount);
                Expect(DrawMeshes(f.Root)<before,"enabled mesh draw count did not decrease after wall batching");
                Expect(AllEnabled(walls,false),"merged source renderers must stay disabled to avoid duplicate drawing");
                Expect(!invisible.enabled&&invisible.GetComponent<Collider>().enabled,"hidden collision-only ramp became visible or lost its collider");
                for(int i=0;i<models.Count;i++)
                {
                    Expect(AllEnabled(models[i],true),"dynamic ancestor "+i+" was baked into a static mesh");
                    var old=models[i][0].bounds.center;
                    owners[i].position+=new Vector3(7,0,3);owners[i].rotation=Quaternion.Euler(0,35,0);
                    f.budget.Refresh();
                    Expect((models[i][0].bounds.center-old).sqrMagnitude>1,"dynamic model no longer follows its moving ancestor "+i);
                }
                var batches=new List<Renderer>();
                foreach(var r in f.Root.GetComponentsInChildren<Renderer>())if(r.gameObject.name=="렌더 묶음")batches.Add(r);
                Renderer near=null,far=null;
                foreach(var r in batches)if(r.bounds.center.x<100)near=r;else far=r;
                Expect(Visible(near)&&far!=null&&!Visible(far),"initial spatial culling did not show near/hide far walls");
                f.Camera.transform.position=new Vector3(2500,1.65f,2500);f.budget.Refresh();
                foreach(var batch in batches)Expect(!Visible(batch),"far rendering remained enabled");
                foreach(var wall in walls)Expect(wall.GetComponent<Collider>().enabled&&wall.gameObject.activeInHierarchy,"render culling disabled a static wall collider");
                Physics.SyncTransforms();RaycastHit hit;
                var collider=walls[0].GetComponent<Collider>();
                Expect(Physics.Raycast(collider.bounds.center+Vector3.up*4,Vector3.down,out hit,8)&&hit.collider==collider,"culled static wall is no longer physically hittable");
                f.Camera.transform.position=new Vector3(15,1.65f,15);f.budget.Refresh();
                Expect(Visible(near)&&far!=null&&!Visible(far),"returning near did not restore the static batch");
                Expect(AllEnabled(walls,false),"near refresh re-enabled merged source meshes");
                f.Camera.transform.position=new Vector3(910,1.65f,15);f.budget.Refresh();
                Expect(Visible(far)&&near!=null&&!Visible(near),"walking to the far cell did not restore its batch");
            }
        }
        static void CheckControllerVisibility()
        {
            using(var f=new Fixture())
            {
                var journey=Child(f.Root,"Journey vehicle",new Vector3(15,0,15)).gameObject.AddComponent<StationJourney>();
                journey.train=journey.transform;journey.doors=journey.gameObject.AddComponent<VehicleDoors>();journey.doors.cabin=journey.gameObject.AddComponent<Cabin>();
                journey.stops.Add(new NetStation{name="A"});journey.stops.Add(new NetStation{name="B"});
                var journeyBody=f.Body(journey.transform);
                var rail=Child(f.Root,"Rail vehicle",Vector3.zero).gameObject.AddComponent<RailVehicle>();var railBody=f.Body(rail.transform);
                rail.Begin(new List<Vector3>{new Vector3(10,0,10),new Vector3(210,0,10)},12);rail.Serve(100,RailVehicle.Approach+2);
                f.StartBudget();
                journey.startDelay=15;journey.Step(0); // the first-arrival stagger intentionally hides the vehicle
                Expect(AllEnabled(journeyBody,false),"journey fixture failed to enter its hidden first-arrival phase");
                f.Camera.transform.position=journey.train.position+Vector3.up*1.65f;f.budget.Refresh();
                Expect(AllEnabled(journeyBody,false),"render refresh resurrected a StationJourney vehicle hidden by its 15-second start delay");
                journey.startDelay=0;journey.clock=0;journey.Step(.1f);f.budget.Refresh();
                Expect(AllEnabled(journeyBody,true),"journey vehicle did not become visible on its next actual arrival");
                rail.Serve(100,RailVehicle.Shown+1);
                Expect(AllEnabled(railBody,false),"rail fixture failed to enter its hidden turnaround phase");
                f.Camera.transform.position=rail.transform.position+Vector3.up*1.65f;f.budget.Refresh();
                Expect(AllEnabled(railBody,false),"render refresh resurrected a RailVehicle during its hidden turnaround");
                rail.Serve(100,RailVehicle.Approach+2);f.budget.Refresh();
                Expect(AllEnabled(railBody,true),"rail vehicle did not reappear at its actual next arrival");
            }
        }
        static void CheckLights()
        {
            using(var f=new Fixture())
            {
                var points=new List<Light>();
                for(int i=0;i<24;i++)
                {
                    var light=Child(f.Root,"Local light "+i,new Vector3(15+i*.2f,3,15)).gameObject.AddComponent<Light>();
                    light.type=LightType.Point;light.range=35;points.Add(light);
                }
                var sun=Child(f.Root,"Sun",Vector3.zero).gameObject.AddComponent<Light>();sun.type=LightType.Directional;sun.enabled=false;
                f.StartBudget();
                for(int level=0;level<3;level++)
                {
                    Quality.SetValue(null,level);f.budget.Refresh();int active=0;
                    foreach(var light in points)if(light.enabled)active++;
                    int cap=level==0?4:level==1?8:12;
                    Expect(active==cap,"local fixture light cap incorrect for quality "+level+": "+active+" expected "+cap);
                    Expect(!sun.enabled,"render budget overrode the directional sun's indoor/off state");
                }
                // Actual daylight pool and ordinary lights can coexist outdoors.
                // Their combined per-pixel count must obey the advertised quality cap.
                for(int i=0;i<16;i++)
                {
                    var lamp=Child(f.Root,"Street lamp "+i,new Vector3(14+i*.2f,0,15)).gameObject.AddComponent<StreetLamp>();
                    lamp.head=lamp.transform.position+Vector3.up*4;
                    if(!StreetLamp.All.Contains(lamp))StreetLamp.All.Add(lamp);
                }
                for(int level=0;level<3;level++)
                {
                    Quality.SetValue(null,level);
                    typeof(WorldBuilder).GetMethod("SetLamps",PrivateInstance).Invoke(f.world,new object[]{true,f.Camera.transform.position});
                    f.budget.Refresh();int active=0;
                    foreach(var light in f.Root.GetComponentsInChildren<Light>())if(light.enabled&&light.type!=LightType.Directional)active++;
                    Expect(active<=PerformanceRuntime.MaxLocalLights,"street/fixture pools exceed the total local light cap at quality "+level+": "+active+" > "+PerformanceRuntime.MaxLocalLights);
                }
            }
        }
        public static void Run()
        {
            failures=0;int oldQuality=(int)Quality.GetValue(null);
            try{Quality.SetValue(null,1);CheckStaticAndDynamic();CheckControllerVisibility();CheckLights();}
            catch(Exception e){failures++;Debug.LogException(e);}
            finally{Quality.SetValue(null,oldQuality);}
            Debug.Log("RenderBudgetCheck: "+(failures==0?"passed":failures+" failed"));
            EditorApplication.Exit(failures==0?0:1);
        }
    }
}
