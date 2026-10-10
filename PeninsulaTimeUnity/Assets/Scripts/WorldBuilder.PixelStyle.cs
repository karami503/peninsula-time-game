using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime {
    public partial class WorldBuilder {
        const float PixelGrid=.5f;

        GameObject CreatePixelTree(string id,Vector3 position,float scale,float yaw) {
            var tree=new GameObject(id+" model");tree.transform.SetParent(root.transform,false);
            tree.transform.position=position;tree.transform.rotation=Quaternion.Euler(0,yaw,0);
            var bark=Mat("pixel-tree-bark",new Color(.34f,.23f,.14f));
            var leaf=Mat(id=="Pine"?"pixel-pine":"pixel-leaves",id=="Pine"?new Color(.20f,.38f,.29f):new Color(.29f,.51f,.31f));
            Primitive(PrimitiveType.Cube,"Pixel trunk",tree.transform,Vector3.up*1.15f,new Vector3(.42f,2.3f,.42f)*scale,bark);
            if(id=="Pine") {
                RemoveCollider(Primitive(PrimitiveType.Cube,"Pixel crown lower",tree.transform,Vector3.up*2.55f,new Vector3(2.15f,1.05f,2.15f)*scale,leaf));
                RemoveCollider(Primitive(PrimitiveType.Cube,"Pixel crown upper",tree.transform,Vector3.up*3.45f,new Vector3(1.35f,.9f,1.35f)*scale,leaf));
            } else {
                RemoveCollider(Primitive(PrimitiveType.Cube,"Pixel crown",tree.transform,Vector3.up*2.85f,new Vector3(2.25f,1.85f,2.25f)*scale,leaf));
                RemoveCollider(Primitive(PrimitiveType.Cube,"Pixel crown top",tree.transform,new Vector3(.25f,3.9f,-.15f),new Vector3(1.35f,.65f,1.35f)*scale,leaf));
            }
            var furniture=tree.AddComponent<StreetFurnitureCollider>();furniture.visual=tree;
            return tree;
        }

        static void RemoveCollider(GameObject item) { var collider=item.GetComponent<Collider>();if(collider!=null)DestroyImmediate(collider); }

        GameObject CreatePixelPlotBuilding(int era,Vector3 p,string id,float scale) {
            var building=new GameObject("Pixel city block · "+id);building.transform.SetParent(root.transform,false);building.transform.position=p;
            int seed=era*397;foreach(char letter in id??"")seed=unchecked(seed*31+letter);seed&=0x7fffffff;
            var random=new System.Random(seed);
            float width=(3.8f+(float)random.NextDouble()*1.4f)*scale;
            float depth=(3.4f+(float)random.NextDouble()*1.5f)*scale;
            float height=(era>=9?7.5f:era>=7?5.2f:3.2f)+(float)random.NextDouble()*(era>=9?5f:2f);
            height*=scale;
            Color[] walls={new Color(.70f,.72f,.68f),new Color(.67f,.58f,.47f),new Color(.55f,.66f,.68f),new Color(.73f,.67f,.58f)};
            var wall=Mat("pixel-building-"+(seed%4),walls[seed%walls.Length],0,"concrete",1f);
            Primitive(PrimitiveType.Cube,"Pixel building body",building.transform,new Vector3(0,height*.5f,0),new Vector3(width,height,depth),wall);
            Primitive(PrimitiveType.Cube,"Pixel roof",building.transform,new Vector3(0,height+.18f,0),new Vector3(width+.25f,.36f,depth+.25f),Mat("pixel-flat-roof",new Color(.25f,.29f,.30f)));
            var glass=Mat("pixel-window",new Color(.16f,.29f,.35f),.15f);
            int floors=Mathf.Clamp(Mathf.FloorToInt(height/1.65f),1,7);
            for(int floor=0;floor<floors;floor++) {
                float y=1.15f+floor*1.55f;if(y>height-.55f)break;
                for(int column=-1;column<=1;column+=2) {
                    float x=column*width*.24f;
                    var window=Primitive(PrimitiveType.Cube,"Pixel window",building.transform,new Vector3(x,y,-depth*.5f-.015f),new Vector3(width*.26f,.62f,.06f),glass);
                    DestroyImmediate(window.GetComponent<Collider>());
                }
            }
            // Keep a clear, square entrance at the same front edge expected by AddDoor.
            var doorway=Primitive(PrimitiveType.Cube,"Pixel entrance",building.transform,new Vector3(0,.85f,-depth*.5f-.035f),new Vector3(.9f,1.7f,.08f),Mat("pixel-door",new Color(.13f,.20f,.22f),.1f));
            DestroyImmediate(doorway.GetComponent<Collider>());
            return building;
        }

        void ApplyPixelDistrictStyle(Transform district) {
            var filters=district.GetComponentsInChildren<MeshFilter>(true);
            var meshes=new Dictionary<Mesh,Mesh>();
            foreach(var filter in filters) {
                string n=filter.gameObject.name.ToLowerInvariant();
                if(!(n.StartsWith("building")||n.StartsWith("roof")||n.StartsWith("facade")))continue;
                var source=filter.sharedMesh;if(source==null||!source.isReadable)continue;
                Mesh pixel;
                if(!meshes.TryGetValue(source,out pixel)) {
                    pixel=Instantiate(source);pixel.name=source.name+" · pixel block";
                    var vertices=pixel.vertices;
                    for(int i=0;i<vertices.Length;i++)vertices[i]=new Vector3(
                        Mathf.Round(vertices[i].x/PixelGrid)*PixelGrid,
                        Mathf.Round(vertices[i].y/PixelGrid)*PixelGrid,
                        Mathf.Round(vertices[i].z/PixelGrid)*PixelGrid);
                    pixel.vertices=vertices;
                    // Grid snapping can collapse tiny facade triangles. Remove those triangles;
                    // Unity's dynamic batcher is not safe when a mesh contains a zero-area face.
                    for(int sub=0;sub<pixel.subMeshCount;sub++) {
                        var input=pixel.GetTriangles(sub);var output=new List<int>(input.Length);
                        for(int i=0;i+2<input.Length;i+=3) {
                            int a=input[i],b=input[i+1],c=input[i+2];
                            if(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).sqrMagnitude<.000001f)continue;
                            output.Add(a);output.Add(b);output.Add(c);
                        }
                        pixel.SetTriangles(output,sub,false);
                    }
                    pixel.RecalculateBounds();pixel.RecalculateNormals();meshes[source]=pixel;
                }
                filter.sharedMesh=pixel;
                var collider=filter.GetComponent<MeshCollider>();if(collider!=null)collider.sharedMesh=pixel;
                filter.gameObject.name+=" · PixelBlock";
            }
            var marker=new GameObject("Pixel city style applied");marker.transform.SetParent(district,false);
        }
    }
}
