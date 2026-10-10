using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // One building or one road segment: the unit the world is split into. Render batching never crosses a Structure,
    // and each Structure gets its own LODGroup: full detail near, a single low-poly box far away (buildings), culled
    // when it is only a few pixels tall.
    public sealed class Structure : MonoBehaviour
    {
        public const float DetailScreenHeight=.05f,ProxyScreenHeight=.008f,CullScreenHeight=.004f;
        public bool proxy; // true for buildings: one box stands in for the detail at a distance
        public Renderer ProxyRenderer{get;private set;}
        static Material proxyMaterial;

        // Builds the LODGroup from the renderers drawn under this structure now; returns the proxy (or null).
        public Renderer ApplyLod()
        {
            var detail=new List<Renderer>();
            foreach(var r in GetComponentsInChildren<Renderer>())
                if(r!=ProxyRenderer&&r.enabled&&!(r is ParticleSystemRenderer)&&r.GetComponent<TextMesh>()==null)detail.Add(r);
            if(detail.Count==0)return null;
            var group=GetComponent<LODGroup>();if(group==null)group=gameObject.AddComponent<LODGroup>();
            if(proxy)
            {
                var bounds=detail[0].bounds;foreach(var r in detail)bounds.Encapsulate(r.bounds);
                if(ProxyRenderer==null)ProxyRenderer=MakeProxy(bounds);
                group.SetLODs(new[]{new LOD(DetailScreenHeight,detail.ToArray()),new LOD(ProxyScreenHeight,new[]{ProxyRenderer})});
            }
            else group.SetLODs(new[]{new LOD(CullScreenHeight,detail.ToArray())});
            group.RecalculateBounds();
            return ProxyRenderer;
        }
        // An axis-aligned box over the building, without a collider: walking and clicking always use the detailed model.
        Renderer MakeProxy(Bounds bounds)
        {
            if(proxyMaterial==null){var shader=Shader.Find("Standard");proxyMaterial=new Material(shader!=null?shader:Shader.Find("Sprites/Default")){name="Structure proxy",color=new Color(.6f,.62f,.62f)};}
            var box=GameObject.CreatePrimitive(PrimitiveType.Cube);box.name="저상세 블록";
            DestroyImmediate(box.GetComponent<Collider>());
            box.transform.SetParent(transform,false);
            box.transform.position=bounds.center;box.transform.rotation=Quaternion.identity;
            float scale=Mathf.Max(1e-4f,transform.lossyScale.x);
            box.transform.localScale=bounds.size/scale;
            var renderer=box.GetComponent<MeshRenderer>();renderer.sharedMaterial=proxyMaterial;
            return renderer;
        }
    }
}
