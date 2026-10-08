using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace PeninsulaTime
{
    // Every surveyed street entrance has a continuous stair/ramp and a passage to the unpaid concourse.
    // These are playable connections, not a claim that an unmeasured underground plan is a survey.
    public class AccessMeshOwner : MonoBehaviour
    { public Mesh mesh; void OnDestroy(){if(mesh!=null)DestroyImmediate(mesh);} }
    public class StationWalkRoute : MonoBehaviour
    {
        public string exitNumber;
        public Vector3[] points;
    }
    public partial class WorldBuilder
    {
        struct Passage { public Vector3 a,b; public Passage(Vector3 from,Vector3 to){a=from;b=to;} }
        readonly List<Passage> passages=new List<Passage>();
        readonly List<Passage> stairMouths=new List<Passage>();
        void ConnectWalkingExits(List<Vector3> spots,List<int> order,List<Vector3> outward)
        {
            passages.Clear();stairMouths.Clear();
            var stone=Mat("stair-stone",new Color(.70f,.70f,.68f),0,"granite",1);
            var wall=Mat("station-wall",new Color(.90f,.90f,.87f),0,"white",2);
            var yellow=Mat("tactile",new Color(.95f,.78f,.15f));
            int count=Mathf.Min(spots.Count,order.Count);
            // Build all outer walls once, with openings only at the selected exit mouths.
            foreach(var normal in new[]{Vector3.back,Vector3.forward,Vector3.left,Vector3.right}){
                bool vertical=normal.x!=0;float edge=vertical?normal.x*HalfX:normal.z*HalfZ;
                float extent=vertical?HalfZ:HalfX;var holes=new List<float>();
                for(int n=0;n<count;n++)if(outward[n]==normal){float at=vertical?spots[n].z:spots[n].x;if(!holes.Contains(at))holes.Add(at);}
                if(StationIndex==3&&normal==Vector3.right&&!holes.Contains(-9.5f))holes.Add(-9.5f);
                holes.Sort();float from=-extent;
                foreach(float h in holes){ExitWall(vertical,edge,from,h-1.25f,wall);from=h+1.25f;}
                ExitWall(vertical,edge,from,extent,wall);
                foreach(float h in holes){
                    var min=vertical?new Vector3(edge-.15f,ConcourseY+3.1f,h-1.25f):new Vector3(h-1.25f,ConcourseY+3.1f,edge-.15f);
                    var max=vertical?new Vector3(edge+.15f,ConcourseY+ConcourseHeight,h+1.25f):new Vector3(h+1.25f,ConcourseY+ConcourseHeight,edge+.15f);
                    Block("출구 상부 벽",station,min,max,wall,false);
                }
            }
            // Clear the full walking width through former shop/locker frontage.
            Physics.SyncTransforms();
            var clearanceSpots=new List<Vector3>(spots);var clearanceOut=new List<Vector3>(outward);
            if(StationIndex==3){clearanceSpots.Add(new Vector3(HalfX,ConcourseY,-9.5f));clearanceOut.Add(Vector3.right);}
            foreach(var fixture in station.GetComponentsInChildren<Collider>()){
                if(fixture.name=="대합실 외곽 벽"||fixture.name.Contains("바닥")||fixture.name.Contains("천장")||fixture.name.Contains("펜스"))continue;
                for(int n=0;n<clearanceSpots.Count;n++)if(clearanceOut[n].x!=0){
                    var centre=clearanceSpots[n]-clearanceOut[n]*3+Vector3.up*1.2f;
                    var clearance=new Bounds(centre,new Vector3(6.4f,2.4f,2.4f));
                    if(fixture.bounds.Intersects(clearance)){fixture.gameObject.SetActive(false);break;}
                }
            }
            var signs=new List<Vector3>();
            for(int n=0;n<order.Count&&count>0;n++)
            {
                int index=order[n];var portal=mappedPortals[index];var entry=portal.transform.parent;
                // Local +Z is outside. A 1:2 stair descends into the station, with headroom below the street.
                var a=entry.TransformPoint(new Vector3(0,.14f,2.05f));
                var b=entry.TransformPoint(new Vector3(0,ConcourseY,2.05f-2*(.14f-ConcourseY)));
                CutEntranceGround(entry);
                foreach(var collider in station.GetComponentsInChildren<Collider>()){
                    if(collider.name!="기둥"&&collider.name!="고객안내센터"&&collider.name!="고객안내센터 벽")continue;
                    var at=collider.transform.position;
                    if(FlatSegmentDistance(at,a,b)<3.1f){collider.gameObject.SetActive(false);}
                }
                StairFlight(entry,a,b,2.4f,stone,yellow);
                var towards=b-a;towards.y=0;towards.Normalize();
                stairMouths.Add(new Passage(b-towards*4,b));

                var target=spots[n%count];target.y=ConcourseY;target+=outward[n%count]*4;
                var landing=b+towards*3f;
                var route=PassagePath(landing,target);route.Insert(0,b);route.Insert(0,a);
                if(Mathf.Abs(landing.x)<HalfX-2&&Mathf.Abs(landing.z)<HalfZ-2){route=new List<Vector3>{a,b,landing};}
                else route.Add(spots[n%count]-outward[n%count]*2);
                var record=entry.gameObject.AddComponent<StationWalkRoute>();record.exitNumber=entranceRefs[index];record.points=route.ToArray();
                for(int k=1;k<route.Count-1;k++){
                    var toward=(route[k+1]-route[k]).normalized;
                    if(Vector3.Distance(route[k],route[k+1])<5)continue;
                    var at=route[k]+toward*2+Vector3.up*2.5f;
                    if(signs.Exists(existing=>Vector3.Distance(existing,at)<8))continue;
                    signs.Add(at);
                    Board("대합실 · 타는 곳 ↑",station,at,-toward,new Vector2(2.2f,.45f),new Color(.10f,.24f,.45f),Color.white,.16f);
                    Sign("지상 출구 ↑",station,at+toward*.055f,toward,.16f,new Color(1f,.82f,.1f));
                }
                for(int k=1;k<route.Count-1;k++)passages.Add(new Passage(route[k],route[k+1]));
                portal.walkThrough=true;portal.label=entranceRefs[index]+"번 출입구 · 계단으로 걸어 내려가세요";
            }
            if(StationIndex==3)ConnectAirportWalk(stone,yellow);
            BuildPassageUnion(station,passages,stairMouths,ConcourseY,stone,wall,true);
            BuildMovingWalkways();
            Physics.SyncTransforms();
        }
        void ConnectAirportWalk(Material stone,Material yellow){
            // Floor plan topology comes from the supplied Gimpo station guide; distances remain provisional.
            var top=AirportOrigin+new Vector3(-60,0,-16);
            var foot=top+new Vector3(-2*(0-ConcourseY),ConcourseY,0);
            var route=new[]{top+Vector3.right*2,top,foot,foot+Vector3.left*3,
                new Vector3(foot.x-3,ConcourseY,-80),new Vector3(HalfX+16,ConcourseY,-80),
                new Vector3(HalfX+16,ConcourseY,-9.5f),new Vector3(HalfX-6,ConcourseY,-9.5f)};
            StairFlight(station,top,foot,2.4f,stone,yellow);
            stairMouths.Add(new Passage(foot,foot+Vector3.right*4));
            for(int k=2;k<route.Length-1;k++)passages.Add(new Passage(route[k],route[k+1]));
            var record=new GameObject("국내선 청사 보행 연결").AddComponent<StationWalkRoute>();record.transform.SetParent(station,false);record.exitNumber="국내선";record.points=route;
            Board("국내선 청사 →",station,new Vector3(HalfX-.1f,ConcourseY+3.3f,-9.5f),Vector3.left,new Vector2(5,.55f),new Color(.12f,.12f,.12f),new Color(1f,.82f,.1f),.28f);
        }
        void ExitWall(bool vertical,float edge,float from,float to,Material wall){
            if(to-from<.001f)return;
            var min=vertical?new Vector3(edge-.15f,ConcourseY,from):new Vector3(from,ConcourseY,edge-.15f);
            var max=vertical?new Vector3(edge+.15f,ConcourseY+ConcourseHeight,to):new Vector3(to,ConcourseY+ConcourseHeight,edge+.15f);
            Block("대합실 외곽 벽",station,min,max,wall);
        }
        static float FlatSegmentDistance(Vector3 p,Vector3 a,Vector3 b){p.y=a.y=b.y=0;var ab=b-a;return Vector3.Distance(p,a+ab*Mathf.Clamp01(Vector3.Dot(p-a,ab)/Mathf.Max(.001f,ab.sqrMagnitude)));}
        // Visibility graph around the concourse. Corridors never cross its solid side walls.
        List<Vector3> PassagePath(Vector3 a,Vector3 b)
        {
            
            var obstacles=new List<Rect>{new Rect(-HalfX-1.8f,-HalfZ-1.8f,2*(HalfX+1.8f),2*(HalfZ+1.8f))};
            foreach(var portal in mappedPortals){
                var e=portal.transform.parent;var foot=e.TransformPoint(new Vector3(0,ConcourseY,2.05f-2*(.14f-ConcourseY)));
                var tip=foot+e.forward*6f;var v=foot+e.forward*.5f;
                obstacles.Add(Rect.MinMaxRect(Mathf.Min(v.x,tip.x)-1.5f,Mathf.Min(v.z,tip.z)-1.5f,Mathf.Max(v.x,tip.x)+1.5f,Mathf.Max(v.z,tip.z)+1.5f));
            }
            var nodes=new List<Vector3>{a,b};
            foreach(var rect in obstacles){float pad=.15f;foreach(float cx in new[]{rect.xMin-pad,rect.xMax+pad})foreach(float cz in new[]{rect.yMin-pad,rect.yMax+pad})nodes.Add(new Vector3(cx,ConcourseY,cz));}
            int n=nodes.Count;var cost=new float[n];var previous=new int[n];var done=new bool[n];
            for(int i=0;i<n;i++){cost[i]=float.MaxValue;previous[i]=-1;}cost[0]=0;
            for(int step=0;step<n;step++){
                int best=-1;for(int i=0;i<n;i++)if(!done[i]&&(best<0||cost[i]<cost[best]))best=i;
                if(best<0||cost[best]==float.MaxValue)break;done[best]=true;
                for(int j=0;j<n;j++)if(!done[j]&&!obstacles.Exists(rect=>CrossesRect(nodes[best],nodes[j],rect))){
                    float d=cost[best]+Vector3.Distance(nodes[best],nodes[j]);if(d<cost[j]){cost[j]=d;previous[j]=best;}
                }
            }
            var path=new List<Vector3>();int cursor=1;
            while(cursor>=0){path.Insert(0,nodes[cursor]);cursor=previous[cursor];}
            if(path[0]!=a)path.Insert(0,a);return path;
        }
        static bool CrossesRect(Vector3 a,Vector3 b,Rect rect)
        {
            float enter=0,leave=1;var d=b-a;
            foreach(int axis in new[]{0,2}){
                float min=axis==0?rect.xMin:rect.yMin,max=axis==0?rect.xMax:rect.yMax;
                if(Mathf.Abs(d[axis])<.0001f){if(a[axis]<=min||a[axis]>=max)return false;continue;}
                float t0=(min-a[axis])/d[axis],t1=(max-a[axis])/d[axis];if(t0>t1){float t=t0;t0=t1;t1=t;}
                enter=Mathf.Max(enter,t0);leave=Mathf.Min(leave,t1);if(enter>=leave)return false;
            }return leave>0&&enter<1;
        }
        void WalkSlab(Transform parent,string name,Vector3 a,Vector3 b,float width,float depth,Material material,bool collision)
        {
            var d=b-a;float length=d.magnitude;if(length<.001f)return;
            var slab=Primitive(PrimitiveType.Cube,name,parent,(a+b)*.5f-Vector3.up*depth*.5f,new Vector3(width,depth,length+.02f),material);
            slab.transform.position=(a+b)*.5f-Vector3.up*depth*.5f;slab.transform.rotation=Quaternion.LookRotation(d);if(!collision)DestroyImmediate(slab.GetComponent<Collider>());
        }
        void StairFlight(Transform parent,Vector3 a,Vector3 b,float width,Material stone,Material yellow)
        {
            var d=b-a;var flat=d;flat.y=0;float run=flat.magnitude;var forward=flat.normalized;var right=Vector3.Cross(Vector3.up,forward);
            WalkSlab(parent,"출입구 경사면",a,b,width,.16f,stone,true);
            int count=Mathf.CeilToInt((a.y-b.y)/.17f);
            for(int i=0;i<count;i++){
                var t0=Vector3.Lerp(a,b,(float)i/count);var t1=Vector3.Lerp(a,b,(float)(i+1)/count);t1.y=t0.y;
                WalkSlab(parent,"출입구 디딤판",t0,t1,width,.07f,stone,false);
                WalkSlab(parent,"계단 안전띠",t0,t0+forward*.04f,width,.014f,yellow,false);
            }
            var railEnd=b;
            foreach(float side in new[]{-1f,1f}){
                var edge=right*side*(width*.5f+.08f);int pieces=12;
                for(int k=0;k<pieces;k++){
                    var p0=Vector3.Lerp(a,railEnd,(float)k/pieces)+edge;
                    var p1=Vector3.Lerp(a,railEnd,(float)(k+1)/pieces)+edge;
                    float top0=Mathf.Min(.95f,p0.y+3.1f),top1=Mathf.Min(.95f,p1.y+3.1f);
                    var mesh=new Mesh{name="Vertical stair shaft wall"};
                    var vertices=new[]{parent.InverseTransformPoint(p0-Vector3.up*.2f),parent.InverseTransformPoint(p1-Vector3.up*.2f),parent.InverseTransformPoint(new Vector3(p1.x,top1,p1.z)),parent.InverseTransformPoint(new Vector3(p0.x,top0,p0.z))};
                    mesh.vertices=new[]{vertices[0],vertices[1],vertices[2],vertices[3],vertices[0],vertices[1],vertices[2],vertices[3]};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up,Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.triangles=new[]{0,1,2,0,2,3,6,5,4,7,6,4};mesh.RecalculateNormals();
                    var go=new GameObject("계단실 옆벽");go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=stone;go.AddComponent<MeshCollider>().sharedMesh=mesh;go.AddComponent<AccessMeshOwner>().mesh=mesh;
                }
                SurveyBeam("계단 손잡이",parent,parent.InverseTransformPoint(a+right*side*(width*.5f-.12f)+Vector3.up*.95f),parent.InverseTransformPoint(railEnd+right*side*(width*.5f-.12f)+Vector3.up*.95f),.055f,Mat("handrail",new Color(.18f,.20f,.21f),.6f));
            }
            var covered=Vector3.Lerp(a,b,.27f);
            WalkSlab(parent,"출입 계단 천장",covered+Vector3.up*3.1f,b+Vector3.up*3.1f,width+.25f,.15f,stone,false);
            for(float t=.3f;t<1;t+=.23f){
                var pos=Vector3.Lerp(a,b,t)+Vector3.up*2.8f;
                WalkSlab(parent,"계단 조명",pos,pos+forward*.6f,1.3f,.025f,Glow("access-light",Color.white,1.3f),false);
                var lamp=new GameObject("계단 조명").AddComponent<Light>();lamp.transform.SetParent(parent,false);lamp.transform.position=pos;lamp.range=11;lamp.intensity=.85f;
            }
        }

        struct ClipVertex { public Vector3 p;public Vector2 uv;public ClipVertex(Vector3 at,Vector2 tex){p=at;uv=tex;} }
        // Carves the stair mouth out of actual rendered and physical pavement, instead of hiding a teleport.
        void CutEntranceGround(Transform entrance)
        {
            var filters=root.GetComponentsInChildren<MeshFilter>();
            foreach(var filter in filters){
                string name=filter.name.ToLowerInvariant();
                if(!(name=="district ground"||name=="surrounding land"||name=="sidewalk"||name=="footway"||name=="platform"||name=="road"||name=="junction"||name=="busway"||name.StartsWith("building")||name=="대합실 천장"))continue;
                var mesh=filter.sharedMesh;if(mesh==null||!mesh.isReadable)continue;
                var vertices=mesh.vertices;var uv=mesh.uv;var output=new List<Vector3>();var tex=new List<Vector2>();var triangles=new List<int>();bool changed=false;
                var indices=mesh.triangles;
                for(int k=0;k<indices.Length;k+=3){
                    var poly=new List<ClipVertex>();
                    for(int j=0;j<3;j++){int ix=indices[k+j];poly.Add(new ClipVertex(filter.transform.TransformPoint(vertices[ix]),uv.Length==vertices.Length?uv[ix]:Vector2.zero));}
                    float low=name=="대합실 천장"?-8.5f:-.6f,high=name=="대합실 천장"?-6.5f:.7f;
                    bool near=poly.Exists(v=>v.p.y>low&&v.p.y<high);
                    // Only surfaces at street level, including the thin ground cube's underside; never the facade.
                    if(!near||poly.Exists(v=>v.p.y>high||v.p.y<low)){EmitClip(poly,filter.transform,output,tex,triangles);continue;}
                    var remain=poly;var outside=new List<List<ClipVertex>>();
                    for(int edge=0;edge<4&&remain.Count>=3;edge++){
                        var kept=new List<ClipVertex>();var rejected=new List<ClipVertex>();
                        for(int j=0;j<remain.Count;j++){
                            var v=remain[j];var w=remain[(j+1)%remain.Count];float dv=HoleDistance(entrance.InverseTransformPoint(v.p),edge,name=="대합실 천장"?24:6.5f),dw=HoleDistance(entrance.InverseTransformPoint(w.p),edge,name=="대합실 천장"?24:6.5f);
                            if(dv>=0)kept.Add(v);else rejected.Add(v);
                            if((dv>=0)!=(dw>=0)){float t=dv/(dv-dw);var intersection=new ClipVertex(Vector3.Lerp(v.p,w.p,t),Vector2.Lerp(v.uv,w.uv,t));kept.Add(intersection);rejected.Add(intersection);}
                        }
                        if(rejected.Count>=3)outside.Add(rejected);remain=kept;
                    }
                    if(remain.Count>=3){changed=true;foreach(var piece in outside)EmitClip(piece,filter.transform,output,tex,triangles);}
                    else EmitClip(poly,filter.transform,output,tex,triangles);
                }
                if(!changed)continue;
                var cut=new Mesh{name=mesh.name+" walk opening",indexFormat=IndexFormat.UInt32};cut.SetVertices(output);cut.SetUVs(0,tex);cut.SetTriangles(triangles,0);cut.RecalculateNormals();cut.RecalculateBounds();filter.sharedMesh=cut;
                var owner=filter.GetComponent<AccessMeshOwner>();if(owner==null)owner=filter.gameObject.AddComponent<AccessMeshOwner>();
                var previous=owner.mesh;owner.mesh=cut;
                var colliders=filter.GetComponents<Collider>();bool collide=colliders.Length>0;foreach(var collider in colliders)DestroyImmediate(collider);
                if(collide)filter.gameObject.AddComponent<MeshCollider>().sharedMesh=cut;
                if(previous!=null)DestroyImmediate(previous);
            }
        }
        static float HoleDistance(Vector3 p,int edge,float back){switch(edge){case 0:return p.x+1.25f;case 1:return 1.25f-p.x;case 2:return p.z+back;default:return 2.07f-p.z;}}
        static void EmitClip(List<ClipVertex> poly,Transform t,List<Vector3> vertices,List<Vector2> uv,List<int> indices){int start=vertices.Count;foreach(var v in poly){vertices.Add(t.InverseTransformPoint(v.p));uv.Add(v.uv);}for(int i=1;i<poly.Count-1;i++){indices.Add(start);indices.Add(start+i);indices.Add(start+i+1);}}
    }
}
