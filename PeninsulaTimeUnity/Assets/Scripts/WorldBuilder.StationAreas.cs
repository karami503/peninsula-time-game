using System;
using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime {
    public partial class WorldBuilder {
        // Geographic east = +X, north = +Z before rotation; exclusions are WORLD bounds.
        // Buildings and walkways are batched. Underground OSM roads/rails are never put on streets.
        public StationAreaBuildResult BuildStationArea(NetStation station,Vector3 worldOrigin,
            Quaternion eastNorthRotation,IList<Bounds> excludedWorldBounds=null,float radius=450f){
            var result=new StationAreaBuildResult{entry=StationAreaData.ForStation(station)};
            if(result.entry==null||root==null)return result;
            radius=Mathf.Clamp(radius,50,StationAreaData.Index.radius);
            var areaRoot=new GameObject("OSM 역 주변 · "+station.name);areaRoot.transform.SetParent(root.transform,false);result.root=areaRoot;
            var batches=new Dictionary<string,AreaMeshBatch>();
            Func<string,AreaMeshBatch> batch=key=>{AreaMeshBatch b;if(!batches.TryGetValue(key,out b))batches[key]=b=new AreaMeshBatch();return b;};
            var seen=new HashSet<string>();
            foreach(string key in result.entry.tiles??new string[0]){
                var tile=StationAreaData.LoadTile(key);if(tile==null){result.missingTiles++;continue;}
                foreach(var feature in tile.features){
                    if(feature==null||feature.points==null||feature.points.Length<2||!seen.Add(feature.id))continue;
                    if(feature.underground&&feature.kind!="e"){result.skippedUnderground++;continue;}
                    var points=new List<Vector3>();
                    for(int i=0;i<feature.points.Length/2;i++)points.Add(StationAreaData.Project(tile,feature,i,result.entry,worldOrigin,eastNorthRotation));
                    if(!AreaNear(points,worldOrigin,radius))continue;
                    if(feature.kind=="e"){
                        var p=points[0];p.y=worldOrigin.y;
                        result.entrances.Add(new StationAreaEntrance{id=feature.id,name=feature.name,number=feature.@ref,position=p});
                        // Flat, nonblocking locator; the station builder decides whether a walkable exit exists.
                        AreaDisc(batch("entrances"),p+Vector3.up*.047f,.65f,12,excludedWorldBounds);
                        continue;
                    }
                    bool polygon=feature.triangles!=null&&feature.triangles.Length>=3;
                    if(feature.kind=="b"){
                        if(!polygon)continue;
                        if(AreaBlocked(points,excludedWorldBounds,feature.h)){result.excludedBuildings++;continue;}
                        float bottom=Mathf.Max(0,feature.@base),top=Mathf.Max(bottom+.5f,feature.h);
                        bool redPortal=feature.subtype=="authored_station_portal";
                        var walls=batch(redPortal?"authored-red":"walls");int from=0;
                        foreach(int end in feature.rings??new int[0]){
                            if(end>points.Count||end<=from)break;
                            for(int i=from;i<end;i++){
                                var a=points[i];var b=points[i+1<end?i+1:from];
                                walls.Quad(a+Vector3.up*bottom,b+Vector3.up*bottom,b+Vector3.up*top,a+Vector3.up*top,true);
                            }from=end;
                        }
                        AreaPolygon(batch(redPortal?"authored-red":"roofs"),points,feature.triangles,top,null);result.buildings++;
                        if(feature.id.StartsWith("authored098:",StringComparison.Ordinal))result.authoredBuildings++;
                    }else if(feature.kind=="r"||feature.kind=="p"||feature.kind=="rail"){
                        string group=feature.kind=="rail"?"rails":feature.kind=="p"?"platforms":AreaIsFootpath(feature.subtype)?"footpaths":"roads";
                        // OSM layer is topological. Vertical bridge clearance is a visual estimate, never a surveyed level.
                        float elevation=feature.bridge||feature.layer>0?Mathf.Max(1,feature.layer)*5:0;
                        float y=feature.kind=="p"?.13f:feature.kind=="rail"?.07f:group=="footpaths"?.055f:.025f;
                        var b=batch(group);
                        if(polygon)AreaPolygon(b,points,feature.triangles,elevation+y,excludedWorldBounds);
                        else{
                            float width=feature.kind=="rail"?1.65f:feature.w;
                            for(int i=1;i<points.Count;i++){
                                var a=points[i-1]+Vector3.up*(elevation+y);var c=points[i]+Vector3.up*(elevation+y);
                                if(!AreaClipRadius(ref a,ref c,worldOrigin,radius))continue;
                                var d=c-a;d.y=0;if(d.sqrMagnitude<.0001f)continue;
                                var n=Vector3.Cross(Vector3.up,d.normalized)*width*.5f;
                                AreaFlatPolygon(b,new List<Vector3>{a+n,c+n,c-n,a-n},excludedWorldBounds);
                                if(i<points.Count-1&&(c-worldOrigin).sqrMagnitude<radius*radius)AreaDisc(b,c,width*.5f,8,excludedWorldBounds);
                            }
                        }
                        if(feature.kind=="p")result.platforms++;else if(feature.kind=="rail")result.rails++;else result.roads++;
                    }
                }
            }
            foreach(var item in batches){
                var b=item.Value;if(b.triangles.Count==0)continue;
                Color color=item.Key=="roads"?new Color(.25f,.27f,.28f):item.Key=="footpaths"?new Color(.68f,.68f,.64f):item.Key=="walls"?new Color(.68f,.70f,.69f):item.Key=="roofs"?new Color(.43f,.46f,.47f):item.Key=="entrances"?new Color(.94f,.73f,.13f):item.Key=="rails"?new Color(.27f,.29f,.28f):new Color(.69f,.69f,.66f);
                // Source accuracy: these shared facade/roof textures are generic visual detail, not
                // surveyed window positions or actual exteriors. OSM footprints and heights stay unchanged.
                var material=item.Key=="authored-red"?Mat("area-authored-station-red",new Color(.64f,.16f,.12f)):item.Key=="walls"?Mat("area-walls-facade",Color.white,0,"facade_grey"):
                    item.Key=="roofs"?Mat("area-roofs-textured",Color.white,0,"roof_flat"):Mat("area-"+item.Key,color);
                // Root can itself be transformed; generated positions were world coordinates.
                for(int i=0;i<b.vertices.Count;i++)b.vertices[i]=areaRoot.transform.InverseTransformPoint(b.vertices[i]);
                int child=areaRoot.transform.childCount;
                PassageMesh(areaRoot.transform,"OSM "+item.Key+" 통합",b.vertices,b.triangles,material,item.Key!="entrances"&&item.Key!="rails");
                // Keep the existing single wall batch and its collider; only replace its texture coordinates.
                if(b.wallUv.Count==b.vertices.Count)areaRoot.transform.GetChild(child).GetComponent<MeshFilter>().sharedMesh.SetUVs(0,b.wallUv);
                result.meshes++;
            }
            return result;
        }
        class AreaMeshBatch {
            public readonly List<Vector3> vertices=new List<Vector3>();public readonly List<int> triangles=new List<int>();
            public readonly List<Vector2> wallUv=new List<Vector2>();
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,bool both=false){
                PassageQuad(vertices,triangles,a,b,c,d,both);
                // Existing facade_grey has four 3m bays and four 3.2m floors per repeat.
                // Use each wall's horizontal length rather than world X, so rotated walls also tile correctly.
                float width=Vector3.Distance(a,b)/12f;
                var ua=new Vector2(0,a.y/12.8f);var ub=new Vector2(width,b.y/12.8f);
                var uc=new Vector2(width,c.y/12.8f);var ud=new Vector2(0,d.y/12.8f);
                wallUv.Add(ua);wallUv.Add(ub);wallUv.Add(uc);wallUv.Add(ud);
                if(both){wallUv.Add(ud);wallUv.Add(uc);wallUv.Add(ub);wallUv.Add(ua);}
            }
        }
        static bool AreaIsFootpath(string type){return type=="footway"||type=="path"||type=="pedestrian"||type=="steps"||type=="cycleway"||type=="living_street";}
        static bool AreaNear(List<Vector3> p,Vector3 origin,float radius){
            if(p.Count==0)return false;var bounds=new Bounds(p[0],Vector3.zero);foreach(var v in p)bounds.Encapsulate(v);origin.y=bounds.center.y;return bounds.SqrDistance(origin)<=radius*radius;
        }
        static bool AreaBlocked(List<Vector3> points,IList<Bounds> holes,float height){
            if(holes==null||points.Count==0)return false;
            var b=new Bounds(points[0],Vector3.zero);foreach(var p in points){b.Encapsulate(p);b.Encapsulate(p+Vector3.up*height);}
            foreach(var hole in holes)if(b.Intersects(hole))return true;return false;
        }
        static void AreaPolygon(AreaMeshBatch batch,List<Vector3> points,int[] triangles,float height,IList<Bounds> holes){
            for(int i=0;i+2<triangles.Length;i+=3){
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];if(a<0||b<0||c<0||a>=points.Count||b>=points.Count||c>=points.Count)continue;
                AreaFlatPolygon(batch,new List<Vector3>{points[a]+Vector3.up*height,points[b]+Vector3.up*height,points[c]+Vector3.up*height},holes);
            }
        }
        static void AreaFlatPolygon(AreaMeshBatch batch,List<Vector3> points,IList<Bounds> holes){
            if(points.Count<3)return;float y=points[0].y;var poly=new List<Vector2>();foreach(var p in points)poly.Add(new Vector2(p.x,p.z));
            // Subtraction helper expects counterclockwise footprints.
            float signed=0;for(int i=0;i<poly.Count;i++){var a=poly[i];var b=poly[(i+1)%poly.Count];signed+=a.x*b.y-b.x*a.y;}if(signed<0)poly.Reverse();
            var fragments=new List<List<Vector2>>{poly};
            if(holes!=null)foreach(var h in holes){
                if(y<h.min.y-.05f||y>h.max.y+.05f)continue;
                var mask=new List<Vector2>{new Vector2(h.min.x,h.min.z),new Vector2(h.max.x,h.min.z),new Vector2(h.max.x,h.max.z),new Vector2(h.min.x,h.max.z)};
                var next=new List<List<Vector2>>();foreach(var part in fragments)next.AddRange(SubtractPassage(part,mask));fragments=next;if(fragments.Count==0)return;
            }
            foreach(var part in fragments){int offset=batch.vertices.Count;foreach(var p in part)batch.vertices.Add(new Vector3(p.x,y,p.y));for(int i=1;i<part.Count-1;i++)batch.triangles.AddRange(new[]{offset,offset+i+1,offset+i});}
        }
        static void AreaDisc(AreaMeshBatch batch,Vector3 center,float radius,int segments,IList<Bounds> holes){
            var points=new List<Vector3>();for(int i=0;i<segments;i++){float a=i*Mathf.PI*2/segments;points.Add(center+new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));}AreaFlatPolygon(batch,points,holes);
        }
        static bool AreaClipRadius(ref Vector3 a,ref Vector3 b,Vector3 center,float radius){
            var offset=a-center;offset.y=0;var d=b-a;d.y=0;float aa=Vector3.Dot(d,d);if(aa<.00001f)return offset.sqrMagnitude<=radius*radius;
            float bb=2*Vector3.Dot(offset,d),cc=Vector3.Dot(offset,offset)-radius*radius,disc=bb*bb-4*aa*cc;if(disc<0)return false;
            float lo=Mathf.Max(0,(-bb-Mathf.Sqrt(disc))/(2*aa)),hi=Mathf.Min(1,(-bb+Mathf.Sqrt(disc))/(2*aa));if(hi<lo)return false;var start=a;var delta=b-a;a=start+delta*lo;b=start+delta*hi;return true;
        }
    }
}
