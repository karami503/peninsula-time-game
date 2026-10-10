using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeninsulaTime
{
    // Carves an object out of one lump: a block of voxels (cell metres each) that stone is added to and cut away from,
    // by boxes, balls, rods or any inside test, each voxel keeping its own colour. Mesh() turns what is left into a single
    // mesh of its outer faces only, merged where neighbouring faces share a colour, coloured per vertex.
    public sealed class Sculpt
    {
        public readonly Vector3 min;public readonly float cell;public readonly int nx,ny,nz;
        readonly byte[] voxels; // 0: air, else a palette index
        readonly List<Color32> palette=new List<Color32>{new Color32(0,0,0,0)};

        public Sculpt(Vector3 min,Vector3 max,float cell)
        {
            this.min=min;this.cell=cell;
            nx=Mathf.Max(1,Mathf.CeilToInt((max.x-min.x)/cell-.001f));ny=Mathf.Max(1,Mathf.CeilToInt((max.y-min.y)/cell-.001f));nz=Mathf.Max(1,Mathf.CeilToInt((max.z-min.z)/cell-.001f));
            voxels=new byte[nx*ny*nz];
        }
        int Index(int x,int y,int z){return (y*nz+z)*nx+x;}
        byte Paint(Color colour)
        {
            Color32 c=colour;
            int i=palette.FindIndex(p=>p.r==c.r&&p.g==c.g&&p.b==c.b);
            if(i>=0)return (byte)i;
            if(palette.Count==256)throw new InvalidOperationException("Sculpt: more than 255 colours");
            palette.Add(new Color32(c.r,c.g,c.b,255));return (byte)(palette.Count-1);
        }
        // Every voxel in `region` whose centre passes `inside` becomes `colour`, or air when colour is null (carved).
        public Sculpt Shape(Bounds region,Func<Vector3,bool> inside,Color? colour)
        {
            byte value=colour.HasValue?Paint(colour.Value):(byte)0;
            int x0=Mathf.Max(0,Mathf.FloorToInt((region.min.x-min.x)/cell)),x1=Mathf.Min(nx-1,Mathf.CeilToInt((region.max.x-min.x)/cell));
            int y0=Mathf.Max(0,Mathf.FloorToInt((region.min.y-min.y)/cell)),y1=Mathf.Min(ny-1,Mathf.CeilToInt((region.max.y-min.y)/cell));
            int z0=Mathf.Max(0,Mathf.FloorToInt((region.min.z-min.z)/cell)),z1=Mathf.Min(nz-1,Mathf.CeilToInt((region.max.z-min.z)/cell));
            for(int y=y0;y<=y1;y++)for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                if(inside(min+new Vector3((x+.5f)*cell,(y+.5f)*cell,(z+.5f)*cell)))voxels[Index(x,y,z)]=value;
            return this;
        }
        public Sculpt Box(Vector3 a,Vector3 b,Color? colour)
        {
            Vector3 lo=Vector3.Min(a,b),hi=Vector3.Max(a,b);var region=new Bounds((lo+hi)*.5f,hi-lo);
            return Shape(region,p=>p.x>lo.x&&p.x<hi.x&&p.y>lo.y&&p.y<hi.y&&p.z>lo.z&&p.z<hi.z,colour);
        }
        public Sculpt Ball(Vector3 centre,Vector3 radii,Color? colour)
        {
            return Shape(new Bounds(centre,radii*2),p=>{var d=p-centre;return d.x*d.x/(radii.x*radii.x)+d.y*d.y/(radii.y*radii.y)+d.z*d.z/(radii.z*radii.z)<1f;},colour);
        }
        // A round bar from a to b.
        public Sculpt Rod(Vector3 a,Vector3 b,float radius,Color? colour)
        {
            var region=new Bounds(a,Vector3.zero);region.Encapsulate(b);region.Expand(radius*2);
            var d=b-a;float length=d.sqrMagnitude;
            return Shape(region,p=>{float t=length>0?Mathf.Clamp01(Vector3.Dot(p-a,d)/length):0;return (a+d*t-p).sqrMagnitude<radius*radius;},colour);
        }
        public bool Solid(Vector3 p)
        {
            int x=Mathf.FloorToInt((p.x-min.x)/cell),y=Mathf.FloorToInt((p.y-min.y)/cell),z=Mathf.FloorToInt((p.z-min.z)/cell);
            return x>=0&&y>=0&&z>=0&&x<nx&&y<ny&&z<nz&&voxels[Index(x,y,z)]!=0;
        }

        // The outer faces, greedily merged per slice; positions in the sculpt's own frame.
        public Mesh Mesh(string name)
        {
            var data=new MeshData();AppendTo(data,Matrix4x4.identity);return data.Build(name);
        }
        public void AppendTo(MeshData data,Matrix4x4 place)
        {
            var size=new[]{nx,ny,nz};var cellAt=new int[3];
            for(int d=0;d<3;d++)
            {
                int u=(d+1)%3,v=(d+2)%3;var mask=new byte[size[u]*size[v]];
                foreach(int dir in new[]{-1,1})
                {
                    var normal=Vector3.zero;normal[d]=dir;
                    for(int s=0;s<size[d];s++)
                    {
                        bool any=false;
                        for(int j=0;j<size[v];j++)for(int i=0;i<size[u];i++)
                        {
                            cellAt[d]=s;cellAt[u]=i;cellAt[v]=j;byte here=voxels[Index(cellAt[0],cellAt[1],cellAt[2])];
                            byte face=0;
                            if(here!=0)
                            {
                                int t=s+dir;cellAt[d]=t;
                                if(t<0||t>=size[d]||voxels[Index(cellAt[0],cellAt[1],cellAt[2])]==0){face=here;any=true;}
                            }
                            mask[j*size[u]+i]=face;
                        }
                        if(!any)continue;
                        float plane=(s+(dir>0?1:0))*cell;
                        for(int j=0;j<size[v];j++)for(int i=0;i<size[u];)
                        {
                            byte c=mask[j*size[u]+i];if(c==0){i++;continue;}
                            int w=1;while(i+w<size[u]&&mask[j*size[u]+i+w]==c)w++;
                            int h=1;bool grow=true;
                            while(j+h<size[v]&&grow){for(int k=0;k<w;k++)if(mask[(j+h)*size[u]+i+k]!=c){grow=false;break;}if(grow)h++;}
                            for(int y=0;y<h;y++)for(int k=0;k<w;k++)mask[(j+y)*size[u]+i+k]=0;
                            Vector3 a=Vector3.zero;a[d]=plane;a[u]=i*cell;a[v]=j*cell;a+=min;
                            Vector3 du=Vector3.zero;du[u]=w*cell;Vector3 dv=Vector3.zero;dv[v]=h*cell;
                            data.Quad(place,a,a+du,a+du+dv,a+dv,normal,palette[c]);
                            i+=w;
                        }
                    }
                }
            }
        }
    }

    // Vertices, colours and normals gathered from several lumps into one mesh.
    public sealed class MeshData
    {
        public readonly List<Vector3> vertices=new List<Vector3>();public readonly List<Vector3> normals=new List<Vector3>();
        public readonly List<Color32> colours=new List<Color32>();public readonly List<int> triangles=new List<int>();
        // a,b,c,d go round the face; it is wound to face along `normal` (both given before `place`).
        public void Quad(Matrix4x4 place,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal,Color32 colour)
        {
            int n=vertices.Count;
            Vector3 pa=place.MultiplyPoint3x4(a),pb=place.MultiplyPoint3x4(b),pc=place.MultiplyPoint3x4(c),pd=place.MultiplyPoint3x4(d);
            vertices.Add(pa);vertices.Add(pb);vertices.Add(pc);vertices.Add(pd);
            var facing=place.MultiplyVector(normal).normalized;for(int k=0;k<4;k++){normals.Add(facing);colours.Add(colour);}
            if(Vector3.Dot(Vector3.Cross(pb-pa,pc-pa),facing)<0)triangles.AddRange(new[]{n,n+2,n+1,n,n+3,n+2});else triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
        }
        // An upright bar standing on the line a→b (y ignored), half width across, from y0 up to y1; no bottom face.
        public void Bar(Vector3 a,Vector3 b,float halfWidth,float y0,float y1,Color32 colour){Bar(a,b,halfWidth,y0,y1,y0,y1,colour);}
        // The same with its own bottom and top at each end (a bar laid on a slope).
        public void Bar(Vector3 a,Vector3 b,float halfWidth,float a0,float a1,float b0,float b1,Color32 colour)
        {
            var along=b-a;along.y=0;if(along.sqrMagnitude<1e-6f)return;along.Normalize();var side=new Vector3(along.z,0,-along.x)*halfWidth;
            a.y=b.y=0;var m=Matrix4x4.identity;Vector3 lowA=Vector3.up*a0,highA=Vector3.up*a1,lowB=Vector3.up*b0,highB=Vector3.up*b1;
            Quad(m,a-side+highA,a+side+highA,b+side+highB,b-side+highB,Vector3.up,colour);
            Quad(m,a+side+lowA,b+side+lowB,b+side+highB,a+side+highA,side,colour);
            Quad(m,a-side+lowA,b-side+lowB,b-side+highB,a-side+highA,-side,colour);
            Quad(m,a-side+lowA,a+side+lowA,a+side+highA,a-side+highA,-along,colour);
            Quad(m,b-side+lowB,b+side+lowB,b+side+highB,b-side+highB,along,colour);
        }
        public Mesh Build(string name)
        {
            var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetColors(colours);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
            return mesh;
        }
    }
}
