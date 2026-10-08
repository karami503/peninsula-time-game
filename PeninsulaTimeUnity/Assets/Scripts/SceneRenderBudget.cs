using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace PeninsulaTime {
    // Rendering only: collision, doors, vehicle clocks and route state stay active.
    // Small spatial batches retain local culling instead of making one entire-world mesh.
    public sealed class SceneRenderBudget : MonoBehaviour {
        public WorldBuilder world;
        readonly List<Renderer> renderers=new List<Renderer>();
        readonly List<Light> lights=new List<Light>();
        readonly List<Mesh> owned=new List<Mesh>();
        readonly HashSet<Renderer> merged=new HashSet<Renderer>();
        readonly float[] distances=new float[12];
        readonly Light[] nearest=new Light[12];
        float nextRefresh;int previousLevel=-1;
        public int CombinedSources {get;private set;}
        public int BatchCount {get;private set;}
        public bool Ready {get;private set;}
        public int VisibleCount {get;private set;}
        struct Key:IEquatable<Key> {
            public Material material;public int x,y,z,shadow;
            public bool Equals(Key k){return material==k.material&&x==k.x&&y==k.y&&z==k.z&&shadow==k.shadow;}
            public override bool Equals(object o){return o is Key&&Equals((Key)o);}
            public override int GetHashCode(){unchecked{return (material.GetInstanceID()*397)^(x*73856093)^(y*19349663)^(z*83492791)^shadow;}}
        }
        bool StaticMesh(Renderer r,MeshFilter f) {
            if(f==null||f.sharedMesh==null||!f.sharedMesh.isReadable||f.sharedMesh.subMeshCount!=1||f.sharedMesh.vertexCount>50000||r.GetComponent<TextMesh>()!=null)return false;
            if(r.sharedMaterials.Length!=1||r.sharedMaterial==null||r.sharedMaterial.renderQueue>=3000)return false;
            for(var t=r.transform;t!=null&&t!=transform;t=t.parent)
                foreach(var script in t.GetComponents<MonoBehaviour>())
                    if(script!=null&&!(script is AccessMeshOwner))return false;
            return true;
        }
        IEnumerator Start() {
            yield return null;
            if(world==null||world.worldCamera==null||world.worldCamera.orthographic){Ready=true;yield break;}
            var groups=new Dictionary<Key,List<MeshFilter>>();
            foreach(var r in GetComponentsInChildren<Renderer>(true)) {
                renderers.Add(r);
                if(!r.enabled||!r.gameObject.activeInHierarchy)continue;
                var f=r.GetComponent<MeshFilter>();
                if(!(r is MeshRenderer)||!StaticMesh(r,f))continue;
                var p=r.bounds.center;
                var key=new Key{material=r.sharedMaterial,x=Mathf.FloorToInt(p.x/48),y=Mathf.FloorToInt(p.y/8),z=Mathf.FloorToInt(p.z/48),shadow=(int)r.shadowCastingMode};
                List<MeshFilter> list;if(!groups.TryGetValue(key,out list)){list=new List<MeshFilter>();groups.Add(key,list);}list.Add(f);
            }
            double slice=Time.realtimeSinceStartupAsDouble;
            foreach(var pair in groups) {
                if(pair.Value.Count<4)continue;
                var items=new List<CombineInstance>();int vertices=0;
                foreach(var f in pair.Value)vertices+=f.sharedMesh.vertexCount;
                // Avoid an allocation spike from detailed imported geometry.
                if(vertices>160000)continue;
                foreach(var f in pair.Value)items.Add(new CombineInstance{mesh=f.sharedMesh,transform=transform.worldToLocalMatrix*f.transform.localToWorldMatrix,subMeshIndex=0});
                var mesh=new Mesh{name="Spatial render batch",indexFormat=IndexFormat.UInt32};
                mesh.CombineMeshes(items.ToArray(),true,true);owned.Add(mesh);
                var go=new GameObject("렌더 묶음");go.transform.SetParent(transform,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=pair.Key.material;renderer.shadowCastingMode=(ShadowCastingMode)pair.Key.shadow;
                renderers.Add(renderer);BatchCount++;
                foreach(var f in pair.Value){var source=f.GetComponent<Renderer>();source.enabled=false;merged.Add(source);CombinedSources++;}
                if(Time.realtimeSinceStartupAsDouble-slice>.003){yield return null;slice=Time.realtimeSinceStartupAsDouble;}
            }
            renderers.RemoveAll(r=>r==null||merged.Contains(r));lights.AddRange(GetComponentsInChildren<Light>());
            Ready=true;Refresh();
        }
        void LateUpdate() {
            if(!Ready||world==null||world.worldCamera==null)return;
            if(Time.unscaledTime<nextRefresh&&previousLevel==PerformanceRuntime.Level)return;
            nextRefresh=Time.unscaledTime+.16f;previousLevel=PerformanceRuntime.Level;Refresh();
        }
        public void Refresh() {
            if(world==null||world.worldCamera==null)return;
            var camera=world.worldCamera;
            var eye=camera.transform.position;
            bool aerial=world.aerialDistrict||camera.orthographic;
            float range=aerial?1000:PerformanceRuntime.RenderDistance;
            VisibleCount=0;
            foreach(var r in renderers) {
                if(r==null)continue;
                var b=r.bounds;var delta=b.ClosestPoint(eye)-eye;
                bool show=delta.sqrMagnitude<range*range;
                // Earth and buildings cannot be seen through solid ceilings.
                // Keep a generous vertical band around stairs, avoiding floor pop-in.
                if(!aerial&&eye.y<-4&&b.min.y>2)show=false;
                if(!aerial&&eye.y>2&&b.max.y<-5)show=false;
                // Preserve vehicle timetable/door renderer states. Distance culling
                // must never resurrect a train hidden while waiting for its slot.
                r.forceRenderingOff=!show;if(show&&r.enabled&&r.gameObject.activeInHierarchy)VisibleCount++;
            }
            // Nearest point lights only; ambient and emissive fixtures keep distant
            // areas readable without hundreds of per-pixel light passes.
            int streetLights=world.ActiveStreetLightCount;
            int listedStreetLights=0;foreach(var light in lights)if(light!=null&&light.name=="가로등 불빛"&&light.enabled)listedStreetLights++;
            int budget=Mathf.Max(0,PerformanceRuntime.MaxLocalLights-Mathf.Max(streetLights,listedStreetLights));
            for(int i=0;i<nearest.Length;i++){distances[i]=float.MaxValue;nearest[i]=null;}
            foreach(var light in lights) {
                if(light==null||light.type==LightType.Directional||light.name=="가로등 불빛")continue;
                light.enabled=false;float d=(light.transform.position-eye).sqrMagnitude;
                if(d>Mathf.Pow(light.range+12,2))continue;
                for(int i=0;i<budget;i++)if(d<distances[i]){for(int j=budget-1;j>i;j--){distances[j]=distances[j-1];nearest[j]=nearest[j-1];}distances[i]=d;nearest[i]=light;break;}
            }
            foreach(var light in nearest)if(light!=null)light.enabled=true;
        }
        void OnDestroy(){foreach(var mesh in owned)if(mesh!=null)Destroy(mesh);}
    }
}
