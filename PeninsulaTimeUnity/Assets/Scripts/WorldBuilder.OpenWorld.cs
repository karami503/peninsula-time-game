using UnityEngine;

namespace PeninsulaTime
{
    // Scene setup and model helpers for the Changwon open world (GameController.OpenWorld.cs).
    public partial class WorldBuilder
    {
        public bool openWorld; // the Changwon open world owns the scene: no airport interiors, no render budget culling
        public ChangwonWorld BuildChangwonWorld(Transform focus)
        {
            Clear();SetupLight(new Color(.5f,.56f,.62f),Color.white);openWorld=true;dayNight=true;
            worldCamera.orthographic=false;worldCamera.fieldOfView=62;worldCamera.nearClipPlane=.35f;worldCamera.farClipPlane=9500f;
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=1200f;RenderSettings.fogEndDistance=9000f;
            var cw=root.AddComponent<ChangwonWorld>();cw.Init(root.transform,focus);return cw;
        }
        // A drivable car or bus model from Resources/Models/City, facing +z before ChangwonCar.Init turns it.
        public GameObject ChangwonVehicle(string id,Vector3 position,string hint)
        {
            var go=CityModel(id,position,1f,0f);if(go==null)return null;
            FitBoxCollider(go);var thing=go.AddComponent<ChangwonThing>();thing.kind="car";thing.hint=hint;return go;
        }
        public Pedestrian ChangwonPerson(Vector3 position,System.Random random,out Transform[] legs,out Transform[] arms){return CreatePerson(position,1f,random,out legs,out arms);}
        public Material ChangwonMaterial(string key,Color color,string texture=null,float tiling=1f){return Mat("cw-"+key,color,0,texture,tiling);}
        public TextMesh ChangwonSign(string text,Transform parent,Vector3 position,Vector3 toViewer,float height,Color color){return Sign(text,parent,position,toViewer,height,color);}
        public GameObject ChangwonBlock(string name,Transform parent,Vector3 min,Vector3 max,Material material,bool collide=true){return Block(name,parent,min,max,material,collide);}
    }
}
