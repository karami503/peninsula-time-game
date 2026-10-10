using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeninsulaTime
{
    // Thread-safe mesh accumulation: worker threads fill the lists, the main thread turns them into Meshes.
    public class MeshBuild
    {
        public readonly List<Vector3> v=new List<Vector3>();public readonly List<Vector3> n=new List<Vector3>();
        public readonly List<Vector2> uv=new List<Vector2>();public readonly List<Color32> c=new List<Color32>();
        public readonly List<int>[] sub;
        public MeshBuild(int submeshes){sub=new List<int>[submeshes];for(int i=0;i<submeshes;i++)sub[i]=new List<int>();}
        public bool Empty{get{foreach(var s in sub)if(s.Count>0)return false;return true;}}
        public int Vertex(Vector3 p,Vector3 normal,Vector2 t,Color32 color){v.Add(p);n.Add(normal);uv.Add(t);c.Add(color);return v.Count-1;}
        public void Tri(int s,int a,int b,int d){var l=sub[s];l.Add(a);l.Add(b);l.Add(d);}
        // Quad a-b-c-d listed clockwise as seen from the visible side.
        public void Quad(int s,Vector3 a,Vector3 b,Vector3 c2,Vector3 d,Vector2 ta,Vector2 tb,Vector2 tc,Vector2 td,Color32 color){
            var normal=Vector3.Cross(b-a,d-a).normalized;
            int i=Vertex(a,normal,ta,color);Vertex(b,normal,tb,color);Vertex(c2,normal,tc,color);Vertex(d,normal,td,color);
            Tri(s,i,i+1,i+2);Tri(s,i,i+2,i+3);
        }
        // Axis-aligned box (all six faces) in vertex colour; used for low-poly props.
        public void Box(int s,Vector3 center,Vector3 size,Quaternion rotation,Color32 color,bool bottom=false){
            Vector3 h=size*.5f;var p=new Vector3[8];
            for(int i=0;i<8;i++)p[i]=center+rotation*new Vector3((i&1)!=0?h.x:-h.x,(i&2)!=0?h.y:-h.y,(i&4)!=0?h.z:-h.z);
            Quad(s,p[2],p[6],p[7],p[3],Vector2.zero,Vector2.up,Vector2.one,Vector2.right,color); // top (+y)
            Quad(s,p[0],p[2],p[3],p[1],Vector2.zero,Vector2.up,Vector2.one,Vector2.right,color); // -z
            Quad(s,p[5],p[7],p[6],p[4],Vector2.zero,Vector2.up,Vector2.one,Vector2.right,color); // +z
            Quad(s,p[4],p[6],p[2],p[0],Vector2.zero,Vector2.up,Vector2.one,Vector2.right,color); // -x
            Quad(s,p[1],p[3],p[7],p[5],Vector2.zero,Vector2.up,Vector2.one,Vector2.right,color); // +x
            if(bottom)Quad(s,p[0],p[1],p[5],p[4],Vector2.zero,Vector2.up,Vector2.one,Vector2.right,color);
        }
        public Mesh ToMesh(string name){
            var m=new Mesh{name=name};if(v.Count>65000)m.indexFormat=IndexFormat.UInt32;
            m.SetVertices(v);m.SetNormals(n);m.SetUVs(0,uv);m.SetColors(c);m.subMeshCount=sub.Length;
            for(int i=0;i<sub.Length;i++)m.SetTriangles(sub[i],i,false);
            m.RecalculateBounds();return m;
        }
        // Mesh of the given submeshes only (for colliders).
        public Mesh ToCollider(string name,params int[] which){
            var tris=new List<int>();foreach(int s in which)tris.AddRange(sub[s]);
            if(tris.Count==0)return null;
            var m=new Mesh{name=name};if(v.Count>65000)m.indexFormat=IndexFormat.UInt32;m.SetVertices(v);m.SetTriangles(tris,0,true);return m;
        }
    }

    public static class Polygon
    {
        public static float SignedArea(IList<Vector2> p){float a=0;for(int i=0,j=p.Count-1;i<p.Count;j=i++)a+=(p[j].x*p[i].y-p[i].x*p[j].y);return a*.5f;}
        // Ear clipping for a simple polygon; returns index triples counter-clockwise (seen from above, z up).
        public static List<int> Triangulate(IList<Vector2> pts){
            var result=new List<int>();int n=pts.Count;if(n<3)return result;
            var idx=new List<int>(n);bool ccw=SignedArea(pts)>0;for(int i=0;i<n;i++)idx.Add(ccw?i:n-1-i);
            int guard=0;
            while(idx.Count>3&&guard++<n*n){
                bool clipped=false;
                for(int i=0;i<idx.Count;i++){
                    int a=idx[(i+idx.Count-1)%idx.Count],b=idx[i],c=idx[(i+1)%idx.Count];
                    Vector2 A=pts[a],B=pts[b],C=pts[c];
                    if((B.x-A.x)*(C.y-A.y)-(B.y-A.y)*(C.x-A.x)<=1e-6f)continue;
                    bool inside=false;
                    for(int k=0;k<idx.Count&&!inside;k++){int q=idx[k];if(q==a||q==b||q==c)continue;inside=InTri(pts[q],A,B,C);}
                    if(inside)continue;
                    result.Add(a);result.Add(b);result.Add(c);idx.RemoveAt(i);clipped=true;break;
                }
                if(!clipped)break;
            }
            if(idx.Count==3){result.Add(idx[0]);result.Add(idx[1]);result.Add(idx[2]);}
            else if(idx.Count>3)for(int i=1;i<idx.Count-1;i++){result.Add(idx[0]);result.Add(idx[i]);result.Add(idx[i+1]);}
            return result;
        }
        static bool InTri(Vector2 p,Vector2 a,Vector2 b,Vector2 c){
            float d1=(p.x-b.x)*(a.y-b.y)-(a.x-b.x)*(p.y-b.y),d2=(p.x-c.x)*(b.y-c.y)-(b.x-c.x)*(p.y-c.y),d3=(p.x-a.x)*(c.y-a.y)-(c.x-a.x)*(p.y-a.y);
            bool neg=d1<0||d2<0||d3<0,pos=d1>0||d2>0||d3>0;return !(neg&&pos);
        }
        public static bool Contains(IList<Vector2> poly,Vector2 p){
            bool inside=false;for(int i=0,j=poly.Count-1;i<poly.Count;j=i++)
                if((poly[i].y>p.y)!=(poly[j].y>p.y)&&p.x<(poly[j].x-poly[i].x)*(p.y-poly[i].y)/(poly[j].y-poly[i].y)+poly[i].x)inside=!inside;
            return inside;
        }
    }
}
