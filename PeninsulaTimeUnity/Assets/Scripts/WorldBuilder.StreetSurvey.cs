using UnityEngine;

namespace PeninsulaTime
{
    public partial class WorldBuilder
    {
        // Authored from direct Kakao Roadview observation, June 2026 imagery.
        // Dimensions remain estimates; the mapped entrance anchor stays in its OSM location.
        void BuildSurveyedEntranceRoof(Transform entrance,Material steel,Material glass)
        {
            const int steps=12;
            var vertices=new Vector3[(steps+1)*2];var triangles=new int[steps*12];
            for(int i=0;i<=steps;i++)
            {
                float angle=Mathf.PI*i/steps;
                float x=1.50f*Mathf.Cos(angle),y=1.12f+2.0f*Mathf.Sin(angle);
                vertices[i*2]=new Vector3(x,y,-2.3f);vertices[i*2+1]=new Vector3(x,y,2.1f);
                if(i==steps)continue;
                int a=i*2,b=a+1,c=a+2,d=a+3,k=i*12;
                int[] face={a,c,b,b,c,d,b,c,a,d,c,b};
                for(int j=0;j<12;j++)triangles[k+j]=face[j];
            }
            var mesh=new Mesh{name="Gangnam exit 11 curved canopy"};mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();
            var canopy=new GameObject("캐노피 강남11 곡면 유리");canopy.transform.SetParent(entrance,false);
            canopy.AddComponent<MeshFilter>().sharedMesh=mesh;canopy.AddComponent<MeshRenderer>().sharedMaterial=glass;
            canopy.AddComponent<MeshCollider>().sharedMesh=mesh;
            foreach(float z in new[]{-2.3f,-.83f,.64f,2.1f})
            {
                for(int i=0;i<steps;i++)
                {
                    var a=vertices[i*2];a.z=z;var b=vertices[(i+1)*2];b.z=z;
                    SurveyBeam("캐노피 곡선 프레임",entrance,a,b,.06f,steel);
                }
            }
            for(int i=0;i<=steps;i+=3)SurveyBeam("캐노피 세로 프레임",entrance,vertices[i*2],vertices[i*2+1],.055f,steel);
            foreach(float side in new[]{-1.10f,1.10f})
                SurveyBeam("출입구 손잡이",entrance,new Vector3(side,1.02f,-1.9f),new Vector3(side,1.02f,1.7f),.045f,steel);
            var tactile=Mat("survey-tactile",new Color(.88f,.67f,.16f));
            for(int row=0;row<2;row++)for(int col=0;col<8;col++)
            {
                // Thin plates on the existing pavement, no extra height in the walking route.
                var tile=Primitive(PrimitiveType.Cube,"출입구 점자블록",entrance,new Vector3(-1.05f+col*.30f,.16f,2.35f+row*.30f),new Vector3(.29f,.018f,.29f),tactile);
                DestroyImmediate(tile.GetComponent<Collider>());
                for(int x=0;x<3;x++)for(int z=0;z<3;z++)
                {
                    var dot=Primitive(PrimitiveType.Cylinder,"점자 돌기",entrance,tile.transform.localPosition+new Vector3((x-1)*.075f,.018f,(z-1)*.075f),new Vector3(.025f,.005f,.025f),tactile);
                    DestroyImmediate(dot.GetComponent<Collider>());
                }
            }
        }
        // At most eight metres of local correction when an OSM point is on the generated carriageway.
        // Never relocate a mapped entrance into a building to escape a road overlap.
        Vector3 ClearEntranceAnchor(Vector3 original,Vector3 opening)
        {
            Physics.SyncTransforms();
            var away=-TowardStreet(original);
            for(float shift=0;shift<=8f;shift+=.5f)
            {
                var candidate=original+away*shift;bool blocked=false;
                var side=Vector3.Cross(Vector3.up,opening);
                foreach(float x in new[]{-1.55f,0f,1.55f})foreach(float z in new[]{-2.3f,0f,3.6f})
                {
                    var sample=candidate+side*x+opening*z;
                    foreach(var hit in Physics.RaycastAll(sample+Vector3.up*1.8f,Vector3.down,2.1f,~0,QueryTriggerInteraction.Ignore))
                    {
                        string n=hit.collider.name.ToLowerInvariant();
                        if(n=="road"||n=="busway"||n=="junction"){blocked=true;break;}
                    }
                    foreach(var hit in Physics.RaycastAll(sample+Vector3.up*200f,Vector3.down,200f,~0,QueryTriggerInteraction.Ignore))
                    {
                        string n=hit.collider.name.ToLowerInvariant();
                        if(n.StartsWith("building")||n.StartsWith("roof")){blocked=true;break;}
                    }
                    if(blocked)break;
                }
                if(!blocked)return candidate;
            }
            return original; // retain map location when a safe local adjustment cannot be found
        }
        void SurveyBeam(string name,Transform parent,Vector3 a,Vector3 b,float width,Material mat)
        {
            var beam=Primitive(PrimitiveType.Cube,name,parent,(a+b)*.5f,new Vector3(width,width,Vector3.Distance(a,b)),mat);
            beam.transform.localRotation=Quaternion.LookRotation(b-a);
        }
    }
}
