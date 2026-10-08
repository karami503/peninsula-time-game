using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Changing weather (맑음 → 구름 → 비) with rain streaks around the camera, shorter fog and wet, shinier roads;
    // and speed cameras (과속 단속) on motorways and trunk roads.
    public class ChangwonWeather : MonoBehaviour
    {
        public static string Now="맑음";public static float Rain;
        float target,next;ParticleSystem rain;Material dropMat;float baseFogEnd=-1;readonly List<Material> wet=new List<Material>();readonly List<float> drySmooth=new List<float>();

        void Start(){next=Time.time+Random.Range(240f,420f);}
        void Update()
        {
            if(!ChangwonSession.Active)return;
            if(Time.time>=next){
                next=Time.time+Random.Range(240f,480f);float roll=Random.value;
                float was=target;target=roll<.55f?0f:roll<.8f?.35f:1f;
                if(target>.6f&&was<.6f)ChangwonSession.Toast("비가 내리기 시작합니다 · 도로가 미끄러울 수 있어요");
                else if(target<.1f&&was>.6f)ChangwonSession.Toast("비가 그쳤습니다");
            }
            Rain=Mathf.MoveTowards(Rain,target,Time.deltaTime*.04f);
            Now=Rain>.6f?"비":Rain>.25f?"흐림":"맑음";
            var cam=Camera.main;if(cam==null)return;
            if(baseFogEnd<0)baseFogEnd=RenderSettings.fogEndDistance;
            RenderSettings.fogEndDistance=Mathf.Lerp(baseFogEnd,2200f,Rain);RenderSettings.fogStartDistance=Mathf.Lerp(1200f,250f,Rain);
            bool raining=Rain>.45f;
            if(raining&&rain==null)MakeRain();
            if(rain!=null){
                rain.transform.position=cam.transform.position+Vector3.up*14f+cam.transform.forward*6f;
                var em=rain.emission;em.rateOverTime=raining?Mathf.Lerp(0,PerformanceRuntime.Level==0?700:1600,(Rain-.45f)/.55f):0;
            }
            if(Time.frameCount%30==0)Wet(Mathf.Clamp01((Rain-.3f)/.7f));
        }
        void MakeRain()
        {
            var sh=Shader.Find("Sprites/Default");if(sh==null)return;
            var go=new GameObject("비");go.transform.SetParent(ChangwonSession.Root,false);rain=go.AddComponent<ParticleSystem>();
            rain.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=rain.main;main.loop=true;main.startLifetime=1.1f;main.startSpeed=24f;main.startSize=.05f;main.maxParticles=4000;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.startColor=new Color(.78f,.82f,.9f,.55f);main.gravityModifier=.6f;
            var shape=rain.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(46,1,46);shape.rotation=new Vector3(90,0,0);
            var r=go.GetComponent<ParticleSystemRenderer>();r.renderMode=ParticleSystemRenderMode.Stretch;r.lengthScale=3.5f;r.velocityScale=.04f;
            dropMat=new Material(sh);dropMat.mainTexture=Texture2D.whiteTexture;r.sharedMaterial=dropMat;
            go.transform.rotation=Quaternion.identity;rain.Play();
        }
        // Wet roads: raise the smoothness of the road and pavement materials of loaded chunks.
        void Wet(float amount)
        {
            if(ChangwonWorld.Active==null)return;
            if(wet.Count==0){
                foreach(var mr in ChangwonWorld.Active.GetComponentsInChildren<MeshRenderer>(true)){
                    if(mr.name!="도로")continue;
                    foreach(var m in mr.sharedMaterials)if(m!=null&&!wet.Contains(m)&&m.HasProperty("_Glossiness")){wet.Add(m);drySmooth.Add(m.GetFloat("_Glossiness"));}
                    if(wet.Count>0)break;
                }
            }
            for(int i=0;i<wet.Count;i++)if(wet[i]!=null)wet[i].SetFloat("_Glossiness",Mathf.Lerp(drySmooth[i],.82f,amount));
        }
        void OnDestroy(){for(int i=0;i<wet.Count;i++)if(wet[i]!=null)wet[i].SetFloat("_Glossiness",drySmooth[i]);if(baseFogEnd>0){RenderSettings.fogEndDistance=baseFogEnd;}Rain=0;Now="맑음";}
    }

    public class ChangwonSpeedCameras : MonoBehaviour
    {
        class Cam {public Vector3 at,forward;public int limit;public GameObject go;public float cooldown;}
        readonly List<Cam> cams=new List<Cam>();Vector2Int center=new Vector2Int(int.MinValue,0);float next,flash;
        public static int Points; // 벌점 (penalty points) this session

        void Update()
        {
            if(!ChangwonSession.Active||!ChangwonData.Loaded)return;
            if(Time.time>=next){next=Time.time+1f;Refresh();}
            var car=ChangwonSession.PlayerCar;if(car==null)return;
            foreach(var c in cams){
                if(Time.time<c.cooldown)continue;var d=car.transform.position-c.at;d.y=0;if(d.sqrMagnitude>28*28)continue;
                float kmh=ChangwonCar.Kmh(car.speed);if(kmh<=c.limit+10)continue;
                c.cooldown=Time.time+20f;flash=Time.time+.35f;Points+=kmh>c.limit+40?30:15;
                ChangwonSession.Progress.score=Mathf.Max(0,ChangwonSession.Progress.score-20);
                Sfx.Play("scan",.9f,.8f);ChangwonSession.Toast("과속 단속! 제한 "+c.limit+"km/h · 측정 "+kmh.ToString("0")+"km/h · 벌점 "+(kmh>c.limit+40?30:15)+"점");
            }
        }
        void OnGUI()
        {
            if(Time.time<flash){var c=GUI.color;GUI.color=new Color(1,1,1,(flash-Time.time)/.35f*.8f);GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);GUI.color=c;}
        }
        // Cameras on motorway/trunk/primary roads roughly every 2.5 km, placed deterministically by road id, within 1.5 km of the player.
        void Refresh()
        {
            var p=ChangwonSession.PlayerFeet;var c=ChangwonData.ChunkOf(p.x,p.z);if(c==center)return;center=c;
            foreach(var cam in cams)if(cam.go!=null)Destroy(cam.go);cams.Clear();
            var seen=new HashSet<int>();
            for(int dz=-3;dz<=3;dz++)for(int dx=-3;dx<=3;dx++){
                int x=c.x+dx,z=c.y+dz;if(x<0||z<0||x>=ChangwonData.CX||z>=ChangwonData.CZ)continue;
                foreach(int id in ChangwonData.RoadsInChunk[z*ChangwonData.CX+x]){
                    var r=ChangwonData.Roads[id];if(r.cls>ChangwonData.Primary||r.length<400||!seen.Add(id))continue;
                    if((id*2654435761u>>16)%6!=0)continue;
                    float s=r.length*.5f;Vector3 f;var at=r.At(s,out f);if(r.kind[r.kind.Length/2]!=ChangwonData.Ground)continue;
                    f.y=0;f.Normalize();var right=new Vector3(f.z,0,-f.x);int limit=r.cls==ChangwonData.Motorway?100:r.cls==ChangwonData.Trunk?80:60;
                    cams.Add(new Cam{at=at,forward=f,limit=limit,go=Model(at+right*(r.width*.5f+1.5f),f,limit)});
                }
            }
        }
        GameObject Model(Vector3 at,Vector3 facing,int limit)
        {
            var b=ChangwonSession.Builder;if(b==null)return null;
            var go=new GameObject("과속 단속 카메라 "+limit);go.transform.SetParent(ChangwonSession.Root,false);
            var mb=new MeshBuild(1);var grey=new Color32(170,174,178,255);
            mb.Box(0,at+Vector3.up*3f,new Vector3(.25f,6f,.25f),Quaternion.identity,grey);
            mb.Box(0,at+Vector3.up*6.1f-facing*.2f,new Vector3(.7f,.6f,1.1f),Quaternion.LookRotation(facing),new Color32(225,228,230,255));
            mb.Box(0,at+Vector3.up*6.1f-facing*.8f,new Vector3(.4f,.35f,.2f),Quaternion.LookRotation(facing),new Color32(40,44,50,255));
            var o=new GameObject("기둥");o.transform.SetParent(go.transform,false);o.AddComponent<MeshFilter>().sharedMesh=mb.ToMesh("카메라");
            var sh=Shader.Find("Peninsula/VertexTerrain");o.AddComponent<MeshRenderer>().sharedMaterial=sh!=null?new Material(sh):b.ChangwonMaterial("speedcam",Color.grey);
            b.ChangwonSign("과속단속 "+limit,go.transform,at+Vector3.up*4.4f-facing*12f,-facing,.7f,new Color(1f,.85f,.2f));
            return go;
        }
        void OnDestroy(){foreach(var cam in cams)if(cam.go!=null)Destroy(cam.go);cams.Clear();}
    }
}
