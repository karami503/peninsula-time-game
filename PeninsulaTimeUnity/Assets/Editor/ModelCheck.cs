using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Batch check: every building has a Blender model that sits on the ground inside its lot,
    // and vehicles are oriented lengthwise along Unity Z.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.ModelCheck.Run
    public static class ModelCheck
    {
        const float LotSize=10f;
        public static void Run()
        {
            int failed=0;
            var world=new GameObject("Check World").AddComponent<WorldBuilder>();
            world.root=new GameObject("Check Root");
            foreach(var info in GameContent.Buildings)
            {
                var model=world.CityModel(info.id,Vector3.zero);
                if(model==null){failed++;Debug.LogError("ModelCheck: missing model "+info.id);continue;}
                var b=Bounds(model);
                bool ok=b.size.x<=LotSize&&b.size.z<=LotSize&&Mathf.Abs(b.min.y)<.05f&&b.size.y>1f;
                if(!ok){failed++;Debug.LogError("ModelCheck: "+info.id+" bounds "+b);}
            }
            foreach(var id in new[]{"Bus","BusBlue","BusRed","BusYellow","Metro","Ktx","Airplane","Car","CarRed","CarWhite","CarBlack","CarBlue","BusCabin","MetroCabin"})
            {
                var model=world.CityModel(id,Vector3.zero);
                var b=model!=null?Bounds(model):new Bounds();
                if(model==null||b.size.z<=b.size.x||Mathf.Abs(b.min.y)>.05f){failed++;Debug.LogError("ModelCheck: "+id+" bounds "+b);}
            }
            foreach(var id in new[]{"Bus","Metro","Ktx","Airplane"})
            {
                var model=world.CityModel(id,Vector3.zero);bool transparent=false;
                if(model!=null)foreach(var renderer in model.GetComponentsInChildren<Renderer>())foreach(var material in renderer.sharedMaterials)
                    if(material!=null&&material.renderQueue>=3000){transparent=true;break;}
                if(!transparent){failed++;Debug.LogError("ModelCheck: "+id+" has no transparent passenger window");}
            }
            // Doors hang on the street-facing (-Z) wall, inside the lot.
            foreach(var id in WorldBuilder.DoorBuildings)
            {
                var building=world.CityModel(id,new Vector3(40,0,0));
                var door=world.AddDoor(building);
                var offset=door!=null?door.transform.position-building.transform.position:Vector3.zero;
                if(door==null||offset.z>.5f||offset.z<-LotSize*.5f||Mathf.Abs(offset.x)>LotSize*.5f){failed++;Debug.LogError("ModelCheck: door on "+id+" at "+offset);}
            }
            // A person stands 1.6-1.95 m tall with two legs and two arms.
            Transform[] legs,arms;
            var person=world.CreatePerson(new Vector3(80,0,0),1f,new System.Random(1),out legs,out arms);
            var pb=person!=null?Bounds(person.gameObject):new Bounds();
            if(person==null||legs.Length!=2||arms.Length!=2||pb.size.y<1.6f||pb.size.y>1.95f||Mathf.Abs(pb.min.y)>.05f){failed++;Debug.LogError("ModelCheck: person bounds "+pb);}
            foreach(var id in new[]{"Tree","Pine","StreetLight","Road","DoorFrame","DoorLeaf"})
                if(world.CityModel(id,Vector3.zero)==null){failed++;Debug.LogError("ModelCheck: missing model "+id);}
            Debug.Log("ModelCheck: "+(failed==0?"passed":failed+" failed"));
            Object.DestroyImmediate(world.root);Object.DestroyImmediate(world.gameObject);
            EditorApplication.Exit(failed==0?0:1);
        }
        static Bounds Bounds(GameObject model)
        {
            var renderers=model.GetComponentsInChildren<Renderer>();
            var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);return b;
        }
    }
}
