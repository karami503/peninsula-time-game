using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeninsulaTime
{
    // The cabin belongs to the parked aircraft, so it travels with the departure animation.
    public partial class WorldBuilder
    {
        void BuildAirportCabin(int gate,GameObject plane)
        {
            float x=GateX(gate);
            var doorBounds=new Bounds(AirportOrigin+new Vector3(x+1.95f,4.18f,57.8f),new Vector3(1.5f,2.55f,2.7f));
            CutAircraftDoor(plane,doorBounds);
            var cabinRoot=new GameObject("걸어 들어가는 항공기 객실 "+gate).transform;
            cabinRoot.SetParent(airport,false);cabinRoot.localPosition=new Vector3(x,0,0);
            var wall=Mat("aircraft-interior",new Color(.88f,.88f,.84f));
            var floor=Mat("aircraft-carpet",new Color(.16f,.22f,.29f));
            var seat=Mat("aircraft-seat",new Color(.19f,.30f,.48f));
            Block("객실 보행 바닥",cabinRoot,new Vector3(-1.75f,2.85f,56.4f),new Vector3(1.75f,3,84),floor);
            Block("객실 천장",cabinRoot,new Vector3(-1.65f,5.4f,56.4f),new Vector3(1.65f,5.52f,84),wall,false);
            Block("객실 왼쪽 벽",cabinRoot,new Vector3(-1.85f,3,56.4f),new Vector3(-1.7f,5.42f,84),wall);
            Block("객실 오른쪽 벽",cabinRoot,new Vector3(1.7f,3,59.15f),new Vector3(1.85f,5.42f,84),wall);
            Block("객실 앞벽",cabinRoot,new Vector3(-1.8f,3,56.3f),new Vector3(1.85f,5.42f,56.45f),wall);
            Block("객실 뒤벽",cabinRoot,new Vector3(-1.8f,3,83.8f),new Vector3(1.85f,5.42f,84),wall);
            for(int row=0;row<16;row++)
            {
                float z=61+row*1.35f;
                foreach(float side in new[]{-1f,1f})
                {
                    float sx=side*1.12f;
                    Block("항공기 좌석",cabinRoot,new Vector3(sx-.48f,3,z-.43f),new Vector3(sx+.48f,3.48f,z+.35f),seat);
                    var back=Block("항공기 좌석 등받이",cabinRoot,new Vector3(sx-.48f,3.43f,z+.23f),new Vector3(sx+.48f,4.12f,z+.4f),seat);
                    if(row==0&&side>0)AddFixture(back,"gate","좌석에 앉아 출발 · "+Flights[gate-1,0],"",gate);
                    Block("객실 창문",cabinRoot,new Vector3(side*1.68f-.01f,4.2f,z-.2f),new Vector3(side*1.68f+.01f,4.85f,z+.24f),Mat("aircraft-window",new Color(.32f,.63f,.80f)),false);
                }
            }
            Block("기내 통로 조명",cabinRoot,new Vector3(-.24f,5.36f,57),new Vector3(.24f,5.39f,83),Glow("aircraft-light",new Color(1f,.95f,.85f),1.4f),false);
            var lamp=new GameObject("기내 조명").AddComponent<Light>();lamp.transform.SetParent(cabinRoot,false);lamp.transform.localPosition=new Vector3(0,5,64);lamp.range=35;lamp.intensity=1.25f;lamp.type=LightType.Point;
            Board("기내 좌석에서 출발",cabinRoot,AirportOrigin+new Vector3(x,5.05f,60.5f),Vector3.back,new Vector2(2.5f,.35f),new Color(.1f,.24f,.45f),Color.white,.18f);
            var record=new GameObject("탑승교 → 객실 "+gate).AddComponent<AirportWalkRoute>();record.transform.SetParent(cabinRoot,false);
            record.points=new[]{AirportBoardingDoor(gate),AirportOrigin+new Vector3(x,3,57.8f),AirportOrigin+new Vector3(x,3,61)};
            cabinRoot.SetParent(plane.transform,true);
        }

        // Keep the FBX materials and outer aircraft. Cut a real open doorway, rather than asking the player
        // to walk through the fuselage. Imported FBX coordinates are converted through TransformPoint.
        void CutAircraftDoor(GameObject plane,Bounds opening)
        {
            foreach(var filter in plane.GetComponentsInChildren<MeshFilter>())
            {
                var original=filter.sharedMesh;if(original==null||!original.isReadable)continue;
                var vertices=original.vertices;var uv=original.uv;
                var output=new List<Vector3>();var tex=new List<Vector2>();var submeshes=new List<int[]>();bool changed=false;
                for(int sub=0;sub<original.subMeshCount;sub++)
                {
                    var indices=original.GetTriangles(sub);var triangles=new List<int>();
                    for(int k=0;k<indices.Length;k+=3)
                    {
                        var poly=new List<ClipVertex>();
                        for(int j=0;j<3;j++){int ix=indices[k+j];poly.Add(new ClipVertex(filter.transform.TransformPoint(vertices[ix]),uv.Length==vertices.Length?uv[ix]:Vector2.zero));}
                        var remain=poly;var outside=new List<List<ClipVertex>>();
                        for(int edge=0;edge<6&&remain.Count>=3;edge++)
                        {
                            var kept=new List<ClipVertex>();var rejected=new List<ClipVertex>();
                            for(int j=0;j<remain.Count;j++)
                            {
                                var v=remain[j];var w=remain[(j+1)%remain.Count];
                                float dv=AircraftDoorDistance(v.p,opening,edge),dw=AircraftDoorDistance(w.p,opening,edge);
                                if(dv>=0)kept.Add(v);else rejected.Add(v);
                                if((dv>=0)!=(dw>=0)){float t=dv/(dv-dw);var at=new ClipVertex(Vector3.Lerp(v.p,w.p,t),Vector2.Lerp(v.uv,w.uv,t));kept.Add(at);rejected.Add(at);}
                            }
                            if(rejected.Count>=3)outside.Add(rejected);remain=kept;
                        }
                        if(remain.Count>=3){changed=true;foreach(var piece in outside)EmitClip(piece,filter.transform,output,tex,triangles);}
                        else EmitClip(poly,filter.transform,output,tex,triangles);
                    }
                    submeshes.Add(triangles.ToArray());
                }
                if(!changed)continue;
                var mesh=new Mesh{name="Aircraft open boarding door",indexFormat=IndexFormat.UInt32};
                mesh.SetVertices(output);mesh.SetUVs(0,tex);mesh.subMeshCount=submeshes.Count;
                for(int i=0;i<submeshes.Count;i++)mesh.SetTriangles(submeshes[i],i);
                mesh.RecalculateNormals();mesh.RecalculateBounds();filter.sharedMesh=mesh;
                filter.gameObject.AddComponent<AccessMeshOwner>().mesh=mesh;
                var collider=filter.GetComponent<MeshCollider>();if(collider!=null)collider.sharedMesh=mesh;
            }
        }
        static float AircraftDoorDistance(Vector3 p,Bounds b,int edge)
        {
            int axis=edge/2;return edge%2==0?p[axis]-b.min[axis]:b.max[axis]-p[axis];
        }
    }
}
