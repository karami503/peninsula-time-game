using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Ships moored at Changwon's ports (마산항 cargo ships, 진해 navy grey ships and 부산항 신항 container ships),
    // built procedurally when the player is within a few kilometres.
    public class ChangwonHarbor : MonoBehaviour
    {
        class Port {public ChangwonData.Place spot;public string name;public Vector2 pos;public GameObject go;}
        readonly List<Port> ports=new List<Port>();float next;bool ready;

        void Update()
        {
            if(!ChangwonSession.Active||!ChangwonData.Loaded||Time.time<next)return;next=Time.time+2f;
            if(!ready){ready=true;foreach(var lm in ChangwonLandmarks.All)if(lm.kind=="port"||lm.name.Contains("해군"))ports.Add(new Port{name=lm.name,pos=lm.pos});}
            var p=new Vector2(ChangwonSession.PlayerFeet.x,ChangwonSession.PlayerFeet.z);
            foreach(var port in ports){
                bool near=(port.pos-p).sqrMagnitude<4500f*4500f;
                if(near&&port.go==null)port.go=Build(port);
                else if(!near&&port.go!=null){Destroy(port.go);port.go=null;}
            }
        }
        GameObject Build(Port port)
        {
            var go=new GameObject("항구 선박 · "+port.name);go.transform.SetParent(ChangwonSession.Root,false);
            var rnd=new System.Random(port.name.GetHashCode());var mb=new MeshBuild(1);int placed=0;
            bool navy=port.name.Contains("해군")||port.name.Contains("속천"),containers=port.name.Contains("신항");
            for(int attempt=0;attempt<400&&placed<(containers?5:4);attempt++){
                float a=(float)rnd.NextDouble()*Mathf.PI*2,r=120f+(float)rnd.NextDouble()*650f;
                var at=port.pos+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r;
                // Deep enough water all along the hull.
                float yaw=(float)rnd.NextDouble()*360f;var dir=new Vector2(Mathf.Sin(yaw*Mathf.Deg2Rad),Mathf.Cos(yaw*Mathf.Deg2Rad));
                float len=containers?190f:navy?95f:110f;bool ok=true;
                for(float s=-len*.6f;s<=len*.6f&&ok;s+=15f){var q=at+dir*s;var l=ChangwonData.LandAt(q.x,q.y);if(l!=ChangwonData.Sea&&l!=ChangwonData.Water||ChangwonData.Height(q.x,q.y)>-3f)ok=false;}
                if(!ok)continue;
                Ship(mb,new Vector3(at.x,0,at.y),Quaternion.Euler(0,yaw,0),len,navy,containers,rnd);placed++;
            }
            if(placed==0){Destroy(go);return null;}
            var o=new GameObject("선박");o.transform.SetParent(go.transform,false);o.AddComponent<MeshFilter>().sharedMesh=mb.ToMesh("선박");
            var sh=Shader.Find("Peninsula/VertexTerrain");o.AddComponent<MeshRenderer>().sharedMaterial=sh!=null?new Material(sh):new Material(Shader.Find("Standard"));
            return go;
        }
        static void Ship(MeshBuild mb,Vector3 at,Quaternion rot,float len,bool navy,bool containers,System.Random rnd)
        {
            float beam=len*(containers?.16f:.15f),depth=len*.09f;
            var hull=navy?new Color32(118,124,130,255):containers?new Color32(40,60,92,255):new Color32(150,40,36,255);
            var deck=navy?new Color32(98,104,110,255):new Color32(120,120,116,255);
            // Hull in three blocks tapering to the bow (+z).
            mb.Box(0,at+rot*new Vector3(0,depth*.25f,-len*.1f),new Vector3(beam,depth*1.4f,len*.8f),rot,hull,true);
            mb.Box(0,at+rot*new Vector3(0,depth*.3f,len*.36f),new Vector3(beam*.7f,depth*1.3f,len*.16f),rot,hull);
            mb.Box(0,at+rot*new Vector3(0,depth*.35f,len*.47f),new Vector3(beam*.35f,depth*1.2f,len*.08f),rot,hull);
            mb.Box(0,at+rot*new Vector3(0,depth*.96f,0),new Vector3(beam*.96f,.3f,len*.9f),rot,deck);
            // Superstructure (bridge) at the stern, a mast, and on navy ships a gun turret forward.
            var white=navy?new Color32(140,146,152,255):new Color32(236,236,230,255);
            mb.Box(0,at+rot*new Vector3(0,depth+len*.06f,-len*.36f),new Vector3(beam*.8f,len*.12f,len*.12f),rot,white);
            mb.Box(0,at+rot*new Vector3(0,depth+len*.15f,-len*.38f),new Vector3(.6f,len*.08f,.6f),rot,white);
            if(navy){mb.Box(0,at+rot*new Vector3(0,depth+1.5f,len*.25f),new Vector3(4f,2.2f,5f),rot,white);mb.Box(0,at+rot*new Vector3(0,depth+2f,len*.3f+4f),new Vector3(.5f,.5f,7f),rot,white);}
            if(containers){
                Color32[] colors={new Color32(196,58,44,255),new Color32(40,110,170,255),new Color32(230,180,40,255),new Color32(60,140,80,255),new Color32(220,220,214,255),new Color32(150,90,60,255)};
                for(float z=-len*.24f;z<len*.36f;z+=12.5f)for(float x=-beam*.38f;x<=beam*.38f+.1f;x+=2.6f){
                    int stack=2+rnd.Next(4);for(int s=0;s<stack;s++)mb.Box(0,at+rot*new Vector3(x,depth+1.3f+s*2.6f,z),new Vector3(2.45f,2.5f,12f),rot,colors[rnd.Next(colors.Length)]);
                }
            }else if(!navy){
                // Cargo hatches.
                for(float z=-len*.2f;z<len*.3f;z+=len*.12f)mb.Box(0,at+rot*new Vector3(0,depth+.7f,z),new Vector3(beam*.7f,1.2f,len*.08f),rot,new Color32(60,80,90,255));
            }
        }
        void OnDestroy(){foreach(var p in ports)if(p.go!=null)Destroy(p.go);}
    }
}
