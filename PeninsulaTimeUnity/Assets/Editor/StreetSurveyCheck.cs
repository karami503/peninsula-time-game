using UnityEditor;
using UnityEngine;
namespace PeninsulaTime
{
    public static class StreetSurveyCheck
    {
        static int failures;
        static void Check(bool ok,string why){if(!ok){failures++;Debug.LogError("StreetSurveyCheck: "+why);}}
        public static void Run()
        {
            var camera=new GameObject("Survey camera").AddComponent<Camera>();
            var world=new GameObject("Survey world").AddComponent<WorldBuilder>();world.worldCamera=camera;
            for(int i=0;i<4;i++)
            {
                world.BuildDistrict(i);Physics.SyncTransforms();
                int frames=0,vertices=0;
                foreach(var f in world.root.GetComponentsInChildren<MeshFilter>())
                {if(f.name=="FacadeFrame")frames++;if(f.name.StartsWith("Facade"))vertices+=f.sharedMesh.vertexCount;}
                Check(frames>0,"missing facade geometry in district "+i);
                Debug.Log("StreetSurveyCheck: district "+i+" detail vertices "+vertices);
                foreach(var portal in world.root.GetComponentsInChildren<StationPortal>())
                {
                    if(!portal.downstairs)continue;
                    var entrance=portal.transform.parent;
                    if((i==0&&entrance.name.StartsWith("지하철 11번"))||(i==2&&entrance.name.StartsWith("지하철 3번")))
                    {
                        Vector3 front=entrance.position+entrance.forward*3.3f;
                        foreach(var hit in Physics.RaycastAll(front+Vector3.up*100f,Vector3.down,100f))
                        {
                            var name=hit.collider.name.ToLowerInvariant();
                            if(name.StartsWith("building"))Check(hit.point.y>2.1f,entrance.name+" approach covered by "+name);
                            Check(name!="road"&&name!="busway",entrance.name+" approach on carriageway at "+front);
                        }
                        foreach(var hit in Physics.SphereCastAll(front+Vector3.up*1.5f,.28f,-entrance.forward,2.1f))
                            Check(!hit.collider.name.ToLowerInvariant().StartsWith("building"),entrance.name+" walk-in path blocked by building");
                        if(i==0)Check(entrance.GetComponentInChildren<MeshCollider>()!=null,"missing curved canopy collider");
                        Debug.Log("StreetSurveyCheck: "+entrance.name+" anchor "+entrance.position+" front "+front);
                    }
                }
            }
            Debug.Log("StreetSurveyCheck: "+(failures==0?"passed":failures+" failed"));EditorApplication.Exit(failures==0?0:1);
        }
    }
}
