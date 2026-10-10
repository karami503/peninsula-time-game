using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeninsulaTime
{
    // The OSM district FBX files hold one mesh per surface kind for the whole district (all glass facades, all roads...).
    // Split cuts them into Structures: one per building (its roof, walls and facade details together) and one per
    // RoadSegment-metre stretch of road, footway or platform. Nothing is drawn twice: each triangle lands in exactly one chunk.
    // A building is found from its roof: build_seoul_osm.py writes each footprint as one roof polygon at the building's
    // height, so a connected piece of the Roof mesh is one building (two touching roofs at the same height stay one).
    public static class ModularMesh
    {
        public const float RoadSegment=60f;
        const float Margin=1.5f,IndexCell=24f;

        static string Kind(MeshFilter f){return f.gameObject.name.ToLowerInvariant();}
        static bool Roof(string kind){return kind.StartsWith("roof");}
        static bool Shell(string kind){return kind.StartsWith("building");}
        static bool BuildingPart(string kind){return Shell(kind)||kind.StartsWith("facade");}

        // Returns the number of Structures made.
        public static int Split(Transform district)
        {
            // The district is one authored/carved city mass. Its render chunks stay below this single root so
            // distance culling can hide individual blocks without changing the continuous exterior design.
            var mass=new GameObject("진해 기준 통합 도시 매스").transform;mass.SetParent(district,false);
            var sources=new List<MeshFilter>();
            foreach(var f in district.GetComponentsInChildren<MeshFilter>())
            {
                var r=f.GetComponent<MeshRenderer>();
                if(f.sharedMesh!=null&&f.sharedMesh.isReadable&&r!=null&&r.enabled)sources.Add(f);
            }
            // 1. Every connected roof is one building; its XZ footprint and height claim the walls and facade pieces below it.
            var footprints=new List<Rect>();var tops=new List<float>();
            var keys=new Dictionary<MeshFilter,string[]>();
            foreach(var f in sources)if(Roof(Kind(f)))keys[f]=RoofKeys(f,footprints,tops);
            var index=new Dictionary<Vector2Int,List<int>>();
            for(int b=0;b<footprints.Count;b++)
            {
                var r=footprints[b];
                for(int x=Mathf.FloorToInt((r.xMin-Margin)/IndexCell);x<=Mathf.FloorToInt((r.xMax+Margin)/IndexCell);x++)
                for(int z=Mathf.FloorToInt((r.yMin-Margin)/IndexCell);z<=Mathf.FloorToInt((r.yMax+Margin)/IndexCell);z++)
                {List<int> list;var cell=new Vector2Int(x,z);if(!index.TryGetValue(cell,out list))index[cell]=list=new List<int>();list.Add(b);}
            }
            // 2. Everything else: building parts go to the building under them, the rest to its road segment.
            foreach(var f in sources)
            {
                if(keys.ContainsKey(f))continue;
                var mesh=f.sharedMesh;var v=mesh.vertices;var t=mesh.triangles;var m=f.transform.localToWorldMatrix;
                bool part=BuildingPart(Kind(f)),shell=Shell(Kind(f));
                var k=new string[t.Length/3];
                for(int i=0;i<k.Length;i++)
                {
                    Vector3 p0=m.MultiplyPoint3x4(v[t[i*3]]),p1=m.MultiplyPoint3x4(v[t[i*3+1]]),p2=m.MultiplyPoint3x4(v[t[i*3+2]]);
                    var c=(p0+p1+p2)/3f;
                    int b=part?BuildingAt(c,Mathf.Max(p0.y,Mathf.Max(p1.y,p2.y)),shell,footprints,tops,index):-1;
                    k[i]=b>=0?"b"+b:"r"+Mathf.FloorToInt(c.x/RoadSegment)+","+Mathf.FloorToInt(c.z/RoadSegment);
                }
                keys[f]=k;
            }
            // 3. One child mesh per (structure, surface kind), with the source's materials and transform.
            var structures=new Dictionary<string,Transform>();
            foreach(var pair in keys)Cut(pair.Key,pair.Value,mass,structures);
            return structures.Count;
        }

        // Union-find over shared vertex indices: each connected roof becomes building n, key "b"+n per triangle.
        static string[] RoofKeys(MeshFilter f,List<Rect> footprints,List<float> tops)
        {
            var mesh=f.sharedMesh;var v=mesh.vertices;var t=mesh.triangles;var m=f.transform.localToWorldMatrix;
            var world=new Vector3[v.Length];for(int i=0;i<v.Length;i++)world[i]=m.MultiplyPoint3x4(v[i]);
            var parent=new int[v.Length];for(int i=0;i<parent.Length;i++)parent[i]=i;
            for(int i=0;i<t.Length;i+=3){Union(parent,t[i],t[i+1]);Union(parent,t[i],t[i+2]);}
            var building=new Dictionary<int,int>();var keys=new string[t.Length/3];
            for(int i=0;i<keys.Length;i++)
            {
                int root=Find(parent,t[i*3]),b;
                if(!building.TryGetValue(root,out b)){b=footprints.Count;building[root]=b;footprints.Add(new Rect(world[t[i*3]].x,world[t[i*3]].z,0,0));tops.Add(world[t[i*3]].y);}
                for(int j=0;j<3;j++){var p=world[t[i*3+j]];footprints[b]=Grow(footprints[b],p.x,p.z);tops[b]=Mathf.Max(tops[b],p.y);}
                keys[i]="b"+b;
            }
            return keys;
        }
        static Rect Grow(Rect r,float x,float z){return Rect.MinMaxRect(Mathf.Min(r.xMin,x),Mathf.Min(r.yMin,z),Mathf.Max(r.xMax,x),Mathf.Max(r.yMax,z));}
        static int Find(int[] p,int i){while(p[i]!=i){p[i]=p[p[i]];i=p[i];}return i;}
        static void Union(int[] p,int a,int b){a=Find(p,a);b=Find(p,b);if(a!=b)p[a]=b;}
        // The building a piece belongs to, among footprints (plus Margin) containing its centre, not lower than its top:
        // a wall reaches exactly its own roof, so walls take the closest roof height; facade details take the smallest footprint.
        // -1 if none (the piece then joins its road segment).
        static int BuildingAt(Vector3 c,float top,bool shell,List<Rect> footprints,List<float> tops,Dictionary<Vector2Int,List<int>> index)
        {
            List<int> list;if(!index.TryGetValue(new Vector2Int(Mathf.FloorToInt(c.x/IndexCell),Mathf.FloorToInt(c.z/IndexCell)),out list))return -1;
            int best=-1;float score=float.MaxValue;
            foreach(int b in list)
            {
                var r=footprints[b];
                if(c.x<r.xMin-Margin||c.x>r.xMax+Margin||c.z<r.yMin-Margin||c.z>r.yMax+Margin)continue;
                float s=(tops[b]<top-.3f?1e7f:0)+(shell?Mathf.Abs(tops[b]-top)*1000f:0)+r.width*r.height;
                if(s<score){score=s;best=b;}
            }
            return best;
        }

        static void Cut(MeshFilter f,string[] keys,Transform district,Dictionary<string,Transform> structures)
        {
            var mesh=f.sharedMesh;var renderer=f.GetComponent<MeshRenderer>();
            var groups=new Dictionary<string,List<int>[]>();
            for(int s=0;s<mesh.subMeshCount;s++)
            {
                var t=mesh.GetTriangles(s);int offset=(int)mesh.GetSubMesh(s).indexStart/3;
                for(int i=0;i<t.Length/3;i++)
                {
                    // Triangle numbering across submeshes follows mesh.triangles, which lists submeshes in order.
                    string key=keys[Mathf.Min(keys.Length-1,offset+i)];
                    List<int>[] lists;if(!groups.TryGetValue(key,out lists)){lists=new List<int>[mesh.subMeshCount];for(int j=0;j<lists.Length;j++)lists[j]=new List<int>();groups[key]=lists;}
                    lists[s].Add(t[i*3]);lists[s].Add(t[i*3+1]);lists[s].Add(t[i*3+2]);
                }
            }
            var v=mesh.vertices;var n=mesh.normals;var uv=mesh.uv;var uv2=mesh.uv2;var colors=mesh.colors;var tangents=mesh.tangents;
            foreach(var pair in groups)
            {
                Transform owner;
                if(!structures.TryGetValue(pair.Key,out owner))
                {
                    bool building=pair.Key[0]=='b';
                    owner=new GameObject(building?"건물 "+pair.Key.Substring(1):"도로 구간 "+pair.Key.Substring(1)).transform;
                    owner.SetParent(district,false);owner.gameObject.AddComponent<Structure>().proxy=building;
                    structures[pair.Key]=owner;
                }
                var remap=new Dictionary<int,int>();var cv=new List<Vector3>();var cn=new List<Vector3>();var cu=new List<Vector2>();var cu2=new List<Vector2>();var cc=new List<Color>();var ct=new List<Vector4>();
                var subs=new List<int>[pair.Value.Length];
                for(int s=0;s<subs.Length;s++)
                {
                    subs[s]=new List<int>(pair.Value[s].Count);
                    foreach(int old in pair.Value[s])
                    {
                        int now;
                        if(!remap.TryGetValue(old,out now))
                        {
                            now=cv.Count;remap[old]=now;cv.Add(v[old]);
                            if(n.Length==v.Length)cn.Add(n[old]);if(uv.Length==v.Length)cu.Add(uv[old]);if(uv2.Length==v.Length)cu2.Add(uv2[old]);
                            if(colors.Length==v.Length)cc.Add(colors[old]);if(tangents.Length==v.Length)ct.Add(tangents[old]);
                        }
                        subs[s].Add(now);
                    }
                }
                var chunk=new Mesh{name=mesh.name+" "+owner.name,indexFormat=cv.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
                chunk.SetVertices(cv);if(cn.Count>0)chunk.SetNormals(cn);if(cu.Count>0)chunk.SetUVs(0,cu);if(cu2.Count>0)chunk.SetUVs(1,cu2);
                if(cc.Count>0)chunk.SetColors(cc);if(ct.Count>0)chunk.SetTangents(ct);
                chunk.subMeshCount=subs.Length;for(int s=0;s<subs.Length;s++)chunk.SetTriangles(subs[s],s,false);
                chunk.RecalculateBounds();
                var go=new GameObject(f.gameObject.name);go.transform.SetParent(owner,false);
                go.transform.SetPositionAndRotation(f.transform.position,f.transform.rotation);
                go.transform.localScale=f.transform.lossyScale/Mathf.Max(1e-4f,owner.lossyScale.x);
                go.AddComponent<MeshFilter>().sharedMesh=chunk;go.AddComponent<AccessMeshOwner>().mesh=chunk;
                var r=go.AddComponent<MeshRenderer>();r.sharedMaterials=renderer.sharedMaterials;
                r.shadowCastingMode=renderer.shadowCastingMode;r.receiveShadows=renderer.receiveShadows;
            }
            if(f.transform.childCount==0)Object.DestroyImmediate(f.gameObject);
            else{Object.DestroyImmediate(renderer);Object.DestroyImmediate(f);}
        }
    }
}
