using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeninsulaTime {
    public class FloorGuideRoute : MonoBehaviour {
        public string destination,lineId;
        public Vector3[] points;
        public int labels;
    }
    public partial class WorldBuilder {
        Material floorGuideMaterial;
        GameObject floorGuideLayoutRoot;
        readonly List<Bounds> floorGuideLabelBounds=new List<Bounds>();
        // Paint lies on the walking surface. It never creates a pole, raised bar or collider.
        public FloorGuideRoute PaintFloorGuide(Transform parent,string label,string lineId,Color color,IList<Vector3> points,float offset=0f){
            if(points==null||points.Count<2)return null;
            if(floorGuideLayoutRoot!=root){floorGuideLayoutRoot=root;floorGuideLabelBounds.Clear();}
            var go=new GameObject("바닥 안내 · "+label);go.transform.SetParent(parent,false);
            var record=go.AddComponent<FloorGuideRoute>();record.destination=label;record.lineId=lineId;
            record.points=new Vector3[points.Count];for(int i=0;i<points.Count;i++)record.points[i]=points[i];
            var vertices=new List<Vector3>();var triangles=new List<int>();var colors=new List<Color>();
            float sinceArrow=0,sinceLabel=50;
            for(int i=1;i<points.Count;i++){
                var a=points[i-1];var b=points[i];var d=b-a;float length=d.magnitude;if(length<.025f)continue;
                var flat=d;flat.y=0;if(flat.sqrMagnitude<.001f)continue;flat.Normalize();
                var side=Vector3.Cross(Vector3.up,flat);var normal=Vector3.Cross(d.normalized,side).normalized;
                a+=side*offset+normal*.065f;b+=side*offset+normal*.065f;
                GuideQuad(vertices,triangles,colors,a-side*.095f,a+side*.095f,b+side*.095f,b-side*.095f,color);
                for(float at=Mathf.Max(1.5f,10-sinceArrow);at<length-1;at+=10){
                    var tip=Vector3.Lerp(a,b,at/length);var tail=tip-d.normalized*1.1f;
                    GuideTriangle(vertices,triangles,colors,tail-side*.45f,tail+side*.45f,tip,color);
                }
                sinceArrow=(sinceArrow+length)%10;
                // Labels fit within a 2.6m-wide path, placed only on level landings.
                Vector3 labelAt;
                if(Mathf.Abs(d.y)<.08f&&length>4.5f&&sinceLabel>24&&record.labels<12&&GuideLabelPosition(a,b,side,flat,out labelAt)){
                    // Shared routes cross these panels. Keep the opaque paint just above
                    // every route stripe so depth testing masks arrows beneath the lettering.
                    var at=labelAt+Vector3.up*.025f;
                    GuideQuad(vertices,triangles,colors,at-side*1.3f-flat*.62f,at+side*1.3f-flat*.62f,at+side*1.3f+flat*.62f,at-side*1.3f+flat*.62f,new Color(.055f,.065f,.075f));
                    var text=Sign(label+" ↑",go.transform,at+Vector3.up*.009f,Vector3.up,.27f,Color.white);
                    text.transform.rotation=Quaternion.LookRotation(Vector3.down,flat);
                    var size=text.GetComponent<MeshRenderer>().localBounds.size;
                    text.transform.localScale=Vector3.one*Mathf.Min(1,Mathf.Min(2.4f/Mathf.Max(.01f,size.x),1.02f/Mathf.Max(.01f,size.y)));
                    record.labels++;sinceLabel=0;
                }
                sinceLabel+=length;
            }
            for(int i=0;i<vertices.Count;i++)vertices[i]=go.transform.InverseTransformPoint(vertices[i]);
            var mesh=new Mesh{name="floor guide "+lineId,indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetColors(colors);mesh.RecalculateBounds();mesh.RecalculateNormals();
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<AccessMeshOwner>().mesh=mesh;
            if(floorGuideMaterial==null)floorGuideMaterial=new Material(Shader.Find("Peninsula/FloorGuide"));
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=floorGuideMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            return record;
        }
        // Shared exit/transfer trunks may carry many routes. Reserve a separate panel
        // on a clear section of each route instead of drawing labels on top of each other.
        bool GuideLabelPosition(Vector3 a,Vector3 b,Vector3 side,Vector3 forward,out Vector3 position){
            float length=Vector3.Distance(a,b);
            for(float along=2.2f;along<length-1.6f;along+=3f){
                var at=Vector3.Lerp(a,b,along/length);
                var extent=new Vector3(Mathf.Abs(side.x)*1.42f+Mathf.Abs(forward.x)*.74f,.12f,Mathf.Abs(side.z)*1.42f+Mathf.Abs(forward.z)*.74f);
                var candidate=new Bounds(at,extent*2);bool occupied=false;
                foreach(var previous in floorGuideLabelBounds)if(previous.Intersects(candidate)){occupied=true;break;}
                if(occupied)continue;
                floorGuideLabelBounds.Add(candidate);position=at;return true;
            }
            position=Vector3.zero;return false;
        }
        static void GuideTriangle(List<Vector3> v,List<int> t,List<Color> c,Vector3 a,Vector3 b,Vector3 d,Color color){
            int n=v.Count;v.Add(a);v.Add(b);v.Add(d);c.Add(color);c.Add(color);c.Add(color);t.Add(n);t.Add(n+2);t.Add(n+1);
        }
        static void GuideQuad(List<Vector3> v,List<int> t,List<Color> c,Vector3 a,Vector3 b,Vector3 d,Vector3 e,Color color){
            GuideTriangle(v,t,c,a,b,d,color);GuideTriangle(v,t,c,a,d,e,color);
        }
    }
}
