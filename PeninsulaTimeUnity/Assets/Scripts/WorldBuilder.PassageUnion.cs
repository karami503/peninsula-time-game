using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace PeninsulaTime {
public partial class WorldBuilder {
    // Subtract earlier convex footprints before triangulating. Shared passages have exactly one
    // floor/ceiling surface and walls only along the boundary of their combined footprint.
    static float EdgeSide(Vector2 a,Vector2 b,Vector2 p){var d=b-a;var v=p-a;return d.x*v.y-d.y*v.x;}
    static List<Vector2> ClipPassage(List<Vector2> polygon,Vector2 a,Vector2 b,bool inside){
        var result=new List<Vector2>();
        for(int i=0;i<polygon.Count;i++){
            var v=polygon[i];var w=polygon[(i+1)%polygon.Count];float dv=EdgeSide(a,b,v),dw=EdgeSide(a,b,w);
            bool keep=inside?dv>=0:dv<=0,other=inside?dw>=0:dw<=0;
            if(keep)result.Add(v);
            if(keep!=other)result.Add(Vector2.Lerp(v,w,dv/(dv-dw)));
        }return result;
    }
    static List<List<Vector2>> SubtractPassage(List<Vector2> polygon,List<Vector2> hole){
        var outside=new List<List<Vector2>>();var remain=polygon;
        for(int edge=0;edge<hole.Count&&remain.Count>=3;edge++){
            var a=hole[edge];var b=hole[(edge+1)%hole.Count];var rejected=ClipPassage(remain,a,b,false);
            if(PassageArea(rejected)>.00001f)outside.Add(rejected);
            remain=ClipPassage(remain,a,b,true);
        }return outside;
    }
    static float PassageArea(List<Vector2> poly){float area=0;for(int i=0;i<poly.Count;i++){var a=poly[i];var b=poly[(i+1)%poly.Count];area+=a.x*b.y-b.x*a.y;}return Mathf.Abs(area)*.5f;}
    static List<Vector2> PassageFootprint(Passage p,float halfWidth,float extend=0){
        var a=new Vector2(p.a.x,p.a.z);var b=new Vector2(p.b.x,p.b.z);var d=(b-a).normalized;var r=new Vector2(d.y,-d.x)*halfWidth;a-=d*extend;b+=d*extend;
        return new List<Vector2>{a+r,b+r,b-r,a-r};
    }
    static bool PassageInside(Vector2 point,List<Vector2> polygon){
        for(int i=0;i<polygon.Count;i++)if(EdgeSide(polygon[i],polygon[(i+1)%polygon.Count],point)<-.0001f)return false;return true;
    }
    void PassageMesh(Transform parent,string name,List<Vector3> vertices,List<int> triangles,Material mat,bool collision){
        if(triangles.Count==0)return;
        var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);
        var uv=new List<Vector2>();foreach(var v in vertices)uv.Add(new Vector2(v.x*.4f,(name.Contains("벽")?v.y:v.z)*.4f));mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;go.AddComponent<AccessMeshOwner>().mesh=mesh;
        if(collision)go.AddComponent<MeshCollider>().sharedMesh=mesh;
    }
    static void PassageQuad(List<Vector3> v,List<int> t,Vector3 a,Vector3 b,Vector3 c,Vector3 d,bool both=false){
        int n=v.Count;v.Add(a);v.Add(b);v.Add(c);v.Add(d);t.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
        if(both){n=v.Count;v.Add(d);v.Add(c);v.Add(b);v.Add(a);t.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
    }
    void BuildPassageUnion(Transform parent,List<Passage> paths,List<Passage> mouths,float floor,Material stone,Material wall,bool excludeHall,float halfWidth=1.25f,float extend=1.25f,List<Passage> floorHoles=null){
        var footprints=new List<List<Vector2>>();foreach(var p in paths)if((p.b-p.a).sqrMagnitude>.001f)footprints.Add(PassageFootprint(p,halfWidth,extend));
        if(extend<.1f){
            var centres=new List<Vector2>();
            foreach(var path in paths)foreach(var point in new[]{path.a,path.b}){
                var centre=new Vector2(point.x,point.z);if(centres.Exists(c=>(c-centre).sqrMagnitude<.0025f))continue;centres.Add(centre);
                var round=new List<Vector2>();for(int k=0;k<16;k++){float angle=k*Mathf.PI*2/16;round.Add(centre+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*halfWidth);}footprints.Add(round);
            }
        }
        var hall=new List<Vector2>{new Vector2(-HalfX,-HalfZ),new Vector2(HalfX,-HalfZ),new Vector2(HalfX,HalfZ),new Vector2(-HalfX,HalfZ)};
        var blockers=new List<List<Vector2>>();if(excludeHall)blockers.Add(hall);if(floorHoles!=null)foreach(var hole in floorHoles)blockers.Add(PassageFootprint(hole,halfWidth+.05f,0));
        var fv=new List<Vector3>();var ft=new List<int>();var cv=new List<Vector3>();var ct=new List<int>();
        foreach(var polygon in footprints){
            var fragments=new List<List<Vector2>>{polygon};
            foreach(var hole in blockers){var next=new List<List<Vector2>>();foreach(var piece in fragments)next.AddRange(SubtractPassage(piece,hole));fragments=next;if(fragments.Count==0)break;}
            foreach(var piece in fragments){
                int f=fv.Count,c=cv.Count;foreach(var v in piece){fv.Add(new Vector3(v.x,floor,v.y));cv.Add(new Vector3(v.x,floor+3.1f,v.y));}
                for(int k=1;k<piece.Count-1;k++){ft.AddRange(new[]{f,f+k+1,f+k});ct.AddRange(new[]{c,c+k,c+k+1});}
            }blockers.Add(polygon);
        }
        PassageMesh(parent,"연결 통로 통합 바닥",fv,ft,stone,true);PassageMesh(parent,"연결 통로 통합 천장",cv,ct,wall,false);
        var wv=new List<Vector3>();var wt=new List<int>();var already=new HashSet<string>();
        for(int i=0;i<footprints.Count;i++)for(int edge=0;edge<footprints[i].Count;edge++){
            var a=footprints[i][edge];var b=footprints[i][(edge+1)%footprints[i].Count];var d=b-a;var cuts=new List<float>{0,1};
            var masks=new List<List<Vector2>>(footprints);if(excludeHall)masks.Add(hall);foreach(var mouth in mouths)masks.Add(PassageFootprint(mouth,halfWidth+.02f,.08f));
            foreach(var poly in masks)for(int k=0;k<poly.Count;k++){
                float da=EdgeSide(poly[k],poly[(k+1)%poly.Count],a),db=EdgeSide(poly[k],poly[(k+1)%poly.Count],b);
                if(Mathf.Abs(da-db)>.000001f){float t=da/(da-db);if(t>0&&t<1)cuts.Add(t);}
            }
            cuts.Sort();var outward=new Vector2(d.y,-d.x).normalized;
            for(int k=1;k<cuts.Count;k++){
                if(cuts[k]-cuts[k-1]<.00001f)continue;var mid=a+d*((cuts[k]+cuts[k-1])*.5f)+outward*.015f;
                bool shared=false;for(int j=0;j<masks.Count;j++)if(j!=i&&PassageInside(mid,masks[j])){shared=true;break;}if(shared)continue;
                var start=a+d*cuts[k-1];var end=a+d*cuts[k];
                string key=Mathf.RoundToInt(start.x*1000)+","+Mathf.RoundToInt(start.y*1000)+":"+Mathf.RoundToInt(end.x*1000)+","+Mathf.RoundToInt(end.y*1000);
                if(!already.Add(key))continue;
                PassageQuad(wv,wt,new Vector3(start.x,floor-.05f,start.y),new Vector3(end.x,floor-.05f,end.y),new Vector3(end.x,floor+3.1f,end.y),new Vector3(start.x,floor+3.1f,start.y),true);
            }
        }
        PassageMesh(parent,"연결 통로 외곽 벽",wv,wt,wall,true);
        var lamps=new List<Vector3>();
        foreach(var p in paths){var direction=(p.b-p.a).normalized;float length=Vector3.Distance(p.a,p.b);
            for(float at=3;at<length;at+=12){var point=p.a+direction*at;if(lamps.Exists(v=>Vector3.Distance(v,point)<9))continue;if(excludeHall&&PassageInside(new Vector2(point.x,point.z),hall))continue;lamps.Add(point);
                WalkSlab(parent,"통로 조명",point+Vector3.up*3.03f,point+direction*1.2f+Vector3.up*3.03f,.24f,.025f,Glow("access-light",Color.white,1.3f),false);
                var lamp=new GameObject("통로 등").AddComponent<Light>();lamp.transform.SetParent(parent,false);lamp.transform.position=point+Vector3.up*2.7f;lamp.range=10;lamp.intensity=.65f;
            }
        }
    }
}}
