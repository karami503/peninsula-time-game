using UnityEditor;
using UnityEngine;

namespace PeninsulaTime {
    public static class PixelCityStyleCheck {
        static int failures;
        static void Check(bool ok,string message) { if(!ok){failures++;Debug.LogError("PixelCityStyleCheck: "+message);} }
        public static void Run() {
            failures=0;
            var camera=new GameObject("Pixel check camera").AddComponent<Camera>();
            var world=new GameObject("Pixel check world").AddComponent<WorldBuilder>();world.worldCamera=camera;
            for(int district=0;district<4;district++) {
                world.BuildDistrict(district);
                int pixelMeshes=0,markers=0,roundCrowns=0;
                foreach(var item in world.root.GetComponentsInChildren<Transform>(true)) {
                    var filter=item.GetComponent<MeshFilter>();if(filter!=null&&filter.sharedMesh!=null&&filter.sharedMesh.name.EndsWith("pixel block"))pixelMeshes++;
                    if(item.name=="Pixel city style applied")markers++;
                    if(item.name=="Tree crown"&&item.GetComponent<SphereCollider>()!=null)roundCrowns++;
                }
                Check(markers==1,"district "+district+" did not apply pixel conversion");
                Check(pixelMeshes>0,"district "+district+" has no grid-quantized building meshes");
                Check(roundCrowns==0,"district "+district+" still has round tree geometry");
            }
            var state=new GameState();state.selectedCity="busan";
            state.buildings.Add(new PlacedBuilding{id="apartment",city="busan"});
            state.roads.Add(new RoadSegment{city="busan",axis=0,row=2,column=2});
            world.BuildCityPlot(state);
            int blocks=0,cubeCrowns=0;
            foreach(var item in world.root.GetComponentsInChildren<Transform>(true)) {
                if(item.name.StartsWith("Pixel city block"))blocks++;
                if(item.name.StartsWith("Pixel crown"))cubeCrowns++;
            }
            Check(blocks==1,"map-click city did not use procedural pixel building");
            Check(cubeCrowns>0,"map-click city did not use cube foliage");
            Debug.Log("PixelCityStyleCheck: "+(failures==0?"passed":failures+" failed"));
            Object.DestroyImmediate(world.gameObject);Object.DestroyImmediate(camera.gameObject);
            EditorApplication.Exit(failures==0?0:1);
        }
    }
}
