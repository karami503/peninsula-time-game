using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PeninsulaTime {
    // Run explicitly by the parent test runner. This file does not run or enter Play Mode on import.
    public static class StationAreaCheck {
        static int failures;
        static void Check(bool ok,string reason){if(!ok){failures++;Debug.LogError("StationAreaCheck: "+reason);}}
        static bool Finite(float f){return !float.IsNaN(f)&&!float.IsInfinity(f);}
        static bool Rail(NetLine l){return l.kind=="metro"||l.kind=="ktx"||l.kind=="mugunghwa";}
        static void CheckDataset(){
            var index=StationAreaData.Index;Check(index!=null&&index.stations!=null,"manifest loads as Unity JSON");if(index==null||index.stations==null)return;
            Check(index.version==1&&Mathf.Abs(index.radius-450)<.01f,"dataset version and radius");
            int rail=0;var keys=new HashSet<string>();
            foreach(var station in TransitNetwork.Stations)if(station.lines.Exists(Rail)){
                rail++;var entry=StationAreaData.ForStation(station);Check(entry!=null,station.id+" is covered");
                if(entry==null)continue;Check(entry.roads>0&&entry.tiles!=null&&entry.tiles.Length>0,station.name+" contains actual surrounding roads");
                foreach(string key in entry.tiles)keys.Add(key);
            }
            Check(rail==index.stations.Length,"all and only built-in rail station IDs have manifest entries");
            var ids=new HashSet<string>();int count=0;
            foreach(string key in keys){
                var tile=StationAreaData.LoadTile(key);Check(tile!=null,"gzip+JSON tile "+key);if(tile==null)continue;
                foreach(var f in tile.features){
                    count++;Check(ids.Add(f.id),"unique OSM feature "+f.id);
                    Check(f.points!=null&&f.points.Length>=2&&f.points.Length%2==0,"coordinates "+f.id);
                    if(f.points==null)continue;
                    foreach(float p in f.points)Check(Finite(p),"finite coordinate "+f.id);
                    foreach(int triangle in f.triangles??new int[0])Check(triangle>=0&&triangle<f.points.Length/2,"triangle index "+f.id);
                }
            }
            Debug.Log("StationAreaCheck: "+rail+" station IDs, "+keys.Count+" tiles, "+count+" features decoded");
            StationAreaData.ClearTileCache();
        }
        static NetStation Find(string stationName,string shortLine,out NetLine line){
            line=TransitNetwork.Lines.Find(l=>l.region=="수도권"&&l.shortName==shortLine&&l.stops.Exists(s=>TransitNetwork.Bare(s.name)==stationName));
            return line==null?null:line.stops.Find(s=>TransitNetwork.Bare(s.name)==stationName);
        }
        static void CheckBatchCollisions(StationAreaBuildResult area){
            Check(area!=null&&area.HasData&&area.root!=null,"station surroundings generated");if(area==null||area.root==null)return;
            Check(area.missingTiles==0&&area.roads>0&&area.buildings>0,"station surrounding geometry present");
            var meshes=area.root.GetComponentsInChildren<MeshFilter>();Check(meshes.Length>0&&meshes.Length<=8,"bounded feature-type mesh batching");
            bool ground=false;
            foreach(var filter in meshes){
                Check(filter.sharedMesh!=null&&filter.sharedMesh.vertexCount>0,"nonempty surroundings mesh");
                var collider=filter.GetComponent<MeshCollider>();
                if(filter.name.Contains("entrances")||filter.name.Contains("rails")){Check(collider==null,"map markers do not obstruct walking");continue;}
                Check(collider!=null&&collider.sharedMesh==filter.sharedMesh,"renderer/collider share geometry "+filter.name);
                if(collider==null||!filter.name.Contains("roads"))continue;
                var vertices=filter.sharedMesh.vertices;var indices=filter.sharedMesh.triangles;
                for(int k=0;k+2<indices.Length;k+=3){
                    var centre=filter.transform.TransformPoint((vertices[indices[k]]+vertices[indices[k+1]]+vertices[indices[k+2]])/3f);
                    RaycastHit hit;
                    if(collider.Raycast(new Ray(centre+Vector3.up*2,Vector3.down),out hit,4)){ground=true;break;}
                }
            }
            Check(ground,"mapped road mesh supports downward collision rays");
        }
        static void CheckStation(WorldBuilder world,string name,string shortLine,float expectedLength,string expectedType,string expectedFloor){
            NetLine line;var station=Find(name,shortLine,out line);Check(station!=null,"fixture station "+name+" "+shortLine);if(station==null)return;
            var official=StationAreaData.Architecture(station,line);
            Check(official!=null&&Mathf.Abs(official.platformLength-expectedLength)<.01f&&official.platformType==expectedType&&official.floorLabel==expectedFloor,"official architecture "+name);
            var service=world.BuildNetworkStation(station,line,0);Physics.SyncTransforms();
            CheckBatchCollisions(world.NetworkArea);
            var platforms=world.root.GetComponentsInChildren<NetworkPlatformGeometry>();Check(platforms.Length>=service.stops.Count,"all service platforms carry geometry metadata");
            foreach(var platform in platforms){
                var at=TransitNetwork.Station(platform.stationId);var route=TransitNetwork.Line(platform.lineId);
                var dimensions=StationAreaData.Architecture(at,route);float length=dimensions!=null?dimensions.platformLength:100;
                var collider=platform.GetComponent<Collider>();
                Check(collider!=null,"platform collider "+platform.stationId);if(collider==null)continue;
                Check(Mathf.Abs(collider.bounds.size.z-length)<.02f,"physical platform uses official length "+platform.stationId);
                Check(Mathf.Abs(collider.bounds.size.x-12)<.02f,"wider playable platform "+platform.stationId);
                Check(Mathf.Abs(collider.bounds.min.z-platform.nearZ)<.02f&&Mathf.Abs(collider.bounds.max.z-platform.farZ)<.02f,"platform extents match metadata");
                Check(Mathf.Abs(platform.rampBottom.z-collider.bounds.min.z)<.02f&&Mathf.Abs(platform.rampBottom.y-collider.bounds.max.y)<.02f,"ramp remains attached to the near platform edge");
                Check(platform.farZ<platform.nearZ+StationJourney.Spacing-35,"platform does not reach next station concourse");
                for(int n=0;n<=10;n++){
                    var p=Vector3.Lerp(platform.rampBottom,platform.rampTop,n/10f);bool floor=false;
                    foreach(var hit in Physics.RaycastAll(p+Vector3.up*.6f,Vector3.down,1.3f,~0,QueryTriggerInteraction.Ignore))
                        if(Mathf.Abs(hit.point.y-p.y)<.1f&&(hit.collider.name=="보행 연결 경사로"||hit.collider.name=="platform"||hit.collider.name=="역 대합실"||hit.collider.name=="공용 대합실")){floor=true;break;}
                    Check(floor,"continuous ramp floor "+platform.stationId+" sample "+n);
                }
                RaycastHit platformHit;
                var farFoot=new Vector3(collider.bounds.center.x,collider.bounds.max.y,collider.bounds.max.z-1);
                Check(collider.Raycast(new Ray(farFoot+Vector3.up,Vector3.down),out platformHit,2),"extended far platform remains walkable");
            }
            var next=service.stops[Mathf.Min(1,service.stops.Count-1)];world.RefreshNetworkArea(next,service.origin+Vector3.forward*StationJourney.Spacing);
            Check(world.NetworkArea.entry!=null&&world.NetworkArea.entry.id==next.id,"surroundings refresh on next stop");
            Debug.Log("StationAreaCheck: generated "+name+" "+shortLine+" "+expectedLength+"m, checked mapped mesh collisions and access ramps");
        }
        public static void Run(){
            failures=0;TransitNetwork.Build(null);CheckDataset();
            var camera=new GameObject("StationAreaCheck camera").AddComponent<Camera>();
            var world=new GameObject("StationAreaCheck world").AddComponent<WorldBuilder>();world.worldCamera=camera;
            try{
                CheckStation(world,"강남","2호선",205,"상대식","B2");
                CheckStation(world,"홍대입구","2호선",205,"섬식","B2");
                CheckStation(world,"김포공항","5호선",165,"상대식","B3");
                foreach(string id in new[]{"r12961370708","r368637440","r4691566557","r4753904292","r5197043949","r5208433886","scenario-광명시흥","scenario-풍양"}){
                    var at=TransitNetwork.Station(id);Check(at!=null,"supplement station exists "+id);if(at==null)continue;
                    world.BuildNetworkStation(at,at.lines.Find(Rail),0);Physics.SyncTransforms();
                    CheckBatchCollisions(world.NetworkArea);
                    bool authored=id=="r368637440"||id=="r4691566557";
                    Check(world.NetworkArea.authoredBuildings==(authored?(id=="r368637440"?3:4):0),"authored geometry is present and identified "+at.name);
                    if(id=="r4691566557"){
                        var red=world.NetworkArea.root.transform.Find("OSM authored-red 통합");
                        Check(red!=null&&red.GetComponent<Renderer>().sharedMaterial.color.r>.5f&&red.GetComponent<Renderer>().sharedMaterial.color.g<.3f,"Banseong red portal material");
                    }
                }
                var busan=TransitNetwork.Lines.Find(l=>l.region=="부산"&&l.shortName=="부산 1호선");
                Check(busan!=null&&StationAreaData.Architecture(busan.stops[0],busan)==null,"Seoul official dimensions never leak into Busan");
            }catch(Exception e){Check(false,e.ToString());}
            finally{Object.DestroyImmediate(world.gameObject);Object.DestroyImmediate(camera.gameObject);StationAreaData.ClearTileCache();}
            Debug.Log("StationAreaCheck: "+(failures==0?"passed":failures+" failed"));EditorApplication.Exit(failures==0?0:1);
        }
    }
}
