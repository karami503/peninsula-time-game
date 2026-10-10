using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeninsulaTime {
    // Only the surface point and its OSM ref are sourced. Passage alignments and shaft depths
    // are reconstructed playable links, not a claim that an underground survey was available.
    public sealed class NetworkMappedExitRoute : MonoBehaviour {
        public string osmId,exitNumber;
        public Vector3 surveyedPosition,surfacePoint,hallPoint;
        public Vector3[] points;
        public bool reconstructedPassage=true;
    }
    public partial class WorldBuilder {
        public readonly List<NetworkMappedExitRoute> NetworkMappedExits=new List<NetworkMappedExitRoute>();
        GameObject networkExitRoot;
        const float NetworkExitHalfWidth=1.7f;
        const float NetworkExitPassageHeight=2.2f;

        // Called after the current interchange and its OSM surroundings have been built.
        // RenderMovingRoot excludes this already grouped geometry from the delayed static batcher;
        // it is destroyed together with the current station, never retained for every railway stop.
        public void BuildNetworkMappedExits(Vector3 hallCenter,float hallWidth,float hallDepth=20f){
            NetworkMappedExits.Clear();
            if(networkExitRoot!=null)DestroyImmediate(networkExitRoot);
            networkExitRoot=null;
            if(NetworkArea==null||NetworkArea.entrances.Count==0||root==null)return;
            networkExitRoot=new GameObject("지도 출입구 · 재구성 연결 통로");
            networkExitRoot.transform.SetParent(root.transform,false);
            networkExitRoot.AddComponent<RenderMovingRoot>();
            var stone=Mat("network-exit-stone",new Color(.70f,.71f,.69f),0,"pavement",2);
            var wall=Mat("network-exit-wall",new Color(.79f,.80f,.77f));
            var dark=Mat("network-exit-belt",new Color(.15f,.18f,.20f),.4f,"metal",6);
            var yellow=Mat("network-exit-yellow",new Color(.95f,.75f,.12f));
            // Use a separate mezzanine above underground hall ceilings, with 2.2m clear
            // headroom under the street. Hall/platform transfer paths remain on their own level.
            float corridorFloor=-2.3f;
            RemoveSupersededNetworkExits(hallCenter,hallWidth);
            var gate=new Vector3(NetworkExitHallGateX(hallCenter,hallWidth),hallCenter.y,hallCenter.z-hallDepth*.5f);
            bool needsHallStair=Mathf.Abs(hallCenter.y-corridorFloor)>.25f;
            var hub=gate;
            hub.y=corridorFloor;
            if(needsHallStair)hub.z-=2f*Mathf.Abs(hallCenter.y-corridorFloor);
            float spineZ=hub.z-9f;
            var paths=new List<Passage>();var mouths=new List<Passage>();
            var clearances=new List<NetworkExitVolume>();var flights=new List<Passage>();
            var seen=new HashSet<string>();
            foreach(var entrance in NetworkArea.entrances){
                if(entrance==null||!seen.Add(entrance.id))continue;
                var top=entrance.position;top.y=.035f;
                var inward=new Vector3(hallCenter.x-top.x,0,hallCenter.z-top.z);
                if(inward.sqrMagnitude<1f)inward=Vector3.back;inward.Normalize();
                var bottom=top+inward*(2f*(top.y-corridorFloor));bottom.y=corridorFloor;
                var landing=bottom+inward*3f;
                var turn=new Vector3(landing.x,corridorFloor,spineZ);
                var trunk=new Vector3(hub.x,corridorFloor,spineZ);
                var route=new List<Vector3>{top,bottom,landing};
                var branch=NetworkExitDetour(landing,turn,entrance.id,hallCenter,corridorFloor);
                for(int i=1;i<branch.Count;i++)AppendExitPoint(route,branch[i]);
                AppendExitPoint(route,trunk);AppendExitPoint(route,hub);
                if(needsHallStair)AppendExitPoint(route,gate);
                AppendExitPoint(route,gate+Vector3.forward*2.2f);
                var go=new GameObject("출입구 "+(string.IsNullOrEmpty(entrance.number)?"지도 위치":entrance.number));go.transform.SetParent(networkExitRoot.transform,false);
                var record=go.AddComponent<NetworkMappedExitRoute>();record.osmId=entrance.id;
                record.exitNumber=entrance.number;record.surveyedPosition=entrance.position;record.surfacePoint=top;
                record.hallPoint=route[route.Count-1];record.points=route.ToArray();NetworkMappedExits.Add(record);
                flights.Add(new Passage(top,bottom));
                clearances.Add(new NetworkExitVolume(top-inward*.35f,bottom+inward*.25f,NetworkExitHalfWidth+.22f,-.3f,NetworkExitPassageHeight+.06f));
                clearances.Add(new NetworkExitVolume(top-inward*3.2f,top+inward*.15f,NetworkExitHalfWidth+.22f,.06f,NetworkExitPassageHeight+.06f));
                AddExitPath(paths,bottom,landing);for(int i=1;i<branch.Count;i++)AddExitPath(paths,branch[i-1],branch[i]);AddExitPath(paths,turn,trunk);AddExitPath(paths,trunk,hub);
                mouths.Add(new Passage(bottom-inward*3.6f,bottom+inward*.25f));
            }
            if(needsHallStair){
                flights.Add(gate.y>hub.y?new Passage(gate,hub):new Passage(hub,gate));
                clearances.Add(new NetworkExitVolume(gate,hub+Vector3.back*.4f,NetworkExitHalfWidth+.22f,-.3f,NetworkExitPassageHeight+.06f));
                mouths.Add(new Passage(hub,hub+Vector3.forward*4f));
            }else{
                AddExitPath(paths,hub,gate+Vector3.forward*2.2f);
                mouths.Add(new Passage(gate,gate+Vector3.forward*7f));
            }
            foreach(var path in paths)clearances.Add(new NetworkExitVolume(path.a,path.b,NetworkExitHalfWidth+.16f,.08f,NetworkExitPassageHeight+.015f));
            // The hall wall needs the full width but its floor remains in place.
            clearances.Add(new NetworkExitVolume(gate+Vector3.back*1f,gate+Vector3.forward*3f,NetworkExitHalfWidth+.16f,.06f,NetworkExitPassageHeight+.06f));
            CutNetworkExitVolumes(clearances);
            foreach(var flight in flights)BuildNetworkExitFlight(flight.a,flight.b,stone,yellow,wall);
            WalkSlab(networkExitRoot.transform,"대합실 출입 계단 접속부",gate+Vector3.back*.5f,gate+Vector3.forward*1f,NetworkExitHalfWidth*2,.12f,stone,true);
            var passageRoot=new GameObject("출입구 연결 중간층");passageRoot.transform.SetParent(networkExitRoot.transform,false);
            var floorHoles=new List<Passage>();
            if(needsHallStair&&hallCenter.y<corridorFloor)floorHoles.Add(new Passage(hub,gate));
            BuildPassageUnion(passageRoot.transform,paths,mouths,corridorFloor,stone,wall,false,NetworkExitHalfWidth,NetworkExitHalfWidth+.02f,floorHoles);
            // The shared builder uses 3.1m halls. Compress this distinct mezzanine vertically,
            // preserving its walking floor and leaving the original hall roof below untouched.
            float scale=NetworkExitPassageHeight/3.1f;
            passageRoot.transform.localScale=new Vector3(1,scale,1);
            passageRoot.transform.localPosition=Vector3.up*(corridorFloor*(1-scale));
            var corridorCuts=new List<NetworkExitVolume>();foreach(var path in paths) corridorCuts.Add(new NetworkExitVolume(path.a,path.b,NetworkExitHalfWidth+.12f,.045f,NetworkExitPassageHeight+.02f));
            CutNetworkExitVolumes(corridorCuts,true);
            foreach(var record in NetworkMappedExits){
                var top=record.surfacePoint;var direction=(record.points[1]-top);direction.y=0;direction.Normalize();
                // Outside landing is behind the first tread; it cannot seal the stair mouth.
                WalkSlab(networkExitRoot.transform,"지도 출입구 앞 보도",top-direction*3f,top+direction*.2f,NetworkExitHalfWidth*2+.7f,.12f,stone,true);
                string label=string.IsNullOrEmpty(record.exitNumber)?"지하철 출입구":"출입구 "+record.exitNumber;
                Board(label,networkExitRoot.transform,top-direction*.6f+Vector3.up*2.6f,-direction,new Vector2(3.1f,.55f),new Color(.08f,.19f,.26f),Color.white,.24f);
                var reversed=new List<Vector3>(record.points);reversed.Reverse();
                PaintFloorGuide(networkExitRoot.transform,label,"exit:"+record.osmId,new Color(.95f,.75f,.12f),reversed,-.3f);
            }
            BuildNetworkExitBelts(paths,dark,yellow);
            Physics.SyncTransforms();
        }
        float NetworkExitHallGateX(Vector3 hallCenter,float hallWidth){
            // Underground access rises from the hall, entirely above the live tracks. Keep its
            // existing shaft alignment; only a descending surface/elevated stair crosses rail level.
            if(hallCenter.y<0)return hallCenter.x;
            // At elevated/surface stations this stair descends through the track level. The
            // geometric hall centre can be exactly on a track-bed edge when services are paired.
            // Put the complete stair width between tracks instead of cutting working railway slabs.
            float clearance=2f+NetworkExitHalfWidth+.6f;
            float left=hallCenter.x-hallWidth*.5f+NetworkExitHalfWidth+.5f;
            float right=hallCenter.x+hallWidth*.5f-NetworkExitHalfWidth-.5f;
            var candidates=new List<float>{hallCenter.x,left,right};
            foreach(var service in NetworkServices){candidates.Add(service.origin.x-clearance);candidates.Add(service.origin.x+clearance);}
            float best=hallCenter.x,distance=float.MaxValue;
            foreach(float x in candidates){
                if(x<left||x>right)continue;bool clear=true;
                foreach(var service in NetworkServices)if(Mathf.Abs(x-service.origin.x)<clearance-.001f){clear=false;break;}
                if(clear&&Mathf.Abs(x-hallCenter.x)<distance){best=x;distance=Mathf.Abs(x-hallCenter.x);}
            }
            return best;
        }
        void RemoveSupersededNetworkExits(Vector3 hallCenter,float hallWidth){
            float left=hallCenter.x-hallWidth*.5f,right=hallCenter.x+hallWidth*.5f;
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>()){
                string name=filter.name;
                if(name!="보행 연결 경사로"&&name!="보행 난간"&&name!="출구 경사 통로 벽과 천장"&&name!="출구 앞 보도")continue;
                var renderer=filter.GetComponent<Renderer>();if(renderer==null)continue;var bounds=renderer.bounds;
                if((bounds.center.x<left-.1f||bounds.center.x>right+.1f)&&
                    (Mathf.Abs(bounds.center.z+69)<.3f||Mathf.Abs(bounds.center.z+81)<.3f))filter.gameObject.SetActive(false);
            }
        }
        List<Vector3> NetworkExitDetour(Vector3 a,Vector3 b,string ownId,Vector3 hallCenter,float floor){
            // A long corridor must go around other staircase shafts rather than intersect their
            // sloped floors (which form a head-level barrier when approached from below).
            var obstacles=new List<Rect>();
            foreach(var entrance in NetworkArea.entrances){
                if(entrance.id==ownId)continue;
                var top=entrance.position;top.y=.035f;var inward=hallCenter-top;inward.y=0;if(inward.sqrMagnitude<1)inward=Vector3.back;inward.Normalize();
                var bottom=top+inward*(2f*(top.y-floor));
                const float margin=3.65f;
                var obstacle=Rect.MinMaxRect(Mathf.Min(top.x,bottom.x)-margin,Mathf.Min(top.z,bottom.z)-margin,Mathf.Max(top.x,bottom.x)+margin,Mathf.Max(top.z,bottom.z)+margin);
                // Coincident entrances share their immediate landing; that landing is joined by
                // the passage union. Do not exclude its departure point from the route graph.
                if(obstacle.Contains(new Vector2(a.x,a.z))||obstacle.Contains(new Vector2(b.x,b.z)))continue;
                obstacles.Add(obstacle);
            }
            bool direct=true;foreach(var obstacle in obstacles)if(CrossesRect(a,b,obstacle)){direct=false;break;}
            if(direct)return new List<Vector3>{a,b};
            var nodes=new List<Vector3>{a,b};
            foreach(var obstacle in obstacles)foreach(var point in new[]{new Vector3(obstacle.xMin,floor,obstacle.yMin),new Vector3(obstacle.xMin,floor,obstacle.yMax),new Vector3(obstacle.xMax,floor,obstacle.yMin),new Vector3(obstacle.xMax,floor,obstacle.yMax)}){
                bool inside=false;foreach(var other in obstacles)if(point.x>other.xMin+.001f&&point.x<other.xMax-.001f&&point.z>other.yMin+.001f&&point.z<other.yMax-.001f){inside=true;break;}
                if(!inside)nodes.Add(point);
            }
            int count=nodes.Count;var distances=new float[count];var previous=new int[count];var visited=new bool[count];
            for(int i=0;i<count;i++){distances[i]=float.MaxValue;previous[i]=-1;}distances[0]=0;
            for(int step=0;step<count;step++){
                int current=-1;float best=float.MaxValue;for(int i=0;i<count;i++)if(!visited[i]&&distances[i]<best){best=distances[i];current=i;}
                if(current<0||current==1)break;visited[current]=true;
                for(int next=0;next<count;next++){
                    if(next==current||visited[next])continue;bool blocked=false;
                    foreach(var obstacle in obstacles){
                        // Slight inset treats a path along an expanded obstacle's boundary as clear.
                        var interior=new Rect(obstacle.xMin+.01f,obstacle.yMin+.01f,obstacle.width-.02f,obstacle.height-.02f);
                        if(CrossesRect(nodes[current],nodes[next],interior)){blocked=true;break;}
                    }
                    if(blocked)continue;float candidate=distances[current]+Vector3.Distance(nodes[current],nodes[next]);
                    if(candidate<distances[next]){distances[next]=candidate;previous[next]=current;}
                }
            }
            if(previous[1]<0)return new List<Vector3>{a,b};
            var route=new List<Vector3>();for(int at=1;at>=0;at=previous[at]){route.Add(nodes[at]);if(at==0)break;}route.Reverse();return route;
        }
        static void AppendExitPoint(List<Vector3> route,Vector3 point){if(Vector3.Distance(route[route.Count-1],point)>.05f)route.Add(point);}
        static void AddExitPath(List<Passage> paths,Vector3 a,Vector3 b){
            if(Vector3.Distance(a,b)<.05f)return;
            foreach(var path in paths)if((Vector3.Distance(a,path.a)<.05f&&Vector3.Distance(b,path.b)<.05f)||(Vector3.Distance(a,path.b)<.05f&&Vector3.Distance(b,path.a)<.05f))return;
            paths.Add(new Passage(a,b));
        }
        void BuildNetworkExitFlight(Vector3 a,Vector3 b,Material stone,Material yellow,Material wall){
            var delta=b-a;var direction=delta;direction.y=0;direction.Normalize();var side=Vector3.Cross(Vector3.up,direction);
            var fv=new List<Vector3>();var ft=new List<int>();var wv=new List<Vector3>();var wt=new List<int>();var tv=new List<Vector3>();var tt=new List<int>();
            // Smooth physical slope plus batched visual treads: stable motor contact and no hundreds
            // of separate BoxColliders. Nothing protrudes into the clear centre of the stairs.
            PassageQuad(fv,ft,a-side*NetworkExitHalfWidth,b-side*NetworkExitHalfWidth,b+side*NetworkExitHalfWidth,a+side*NetworkExitHalfWidth,true);
            int steps=Mathf.CeilToInt(Mathf.Abs(delta.y)/.17f);
            for(int i=0;i<steps;i++){
                float t=(float)i/steps;var start=Vector3.Lerp(a,b,t);var end=Vector3.Lerp(a,b,(float)(i+1)/steps);end.y=start.y;
                PassageQuad(tv,tt,start-side*NetworkExitHalfWidth,end-side*NetworkExitHalfWidth,end+side*NetworkExitHalfWidth,start+side*NetworkExitHalfWidth);
            }
            foreach(float edge in new[]{-1f,1f}){
                var offset=side*(NetworkExitHalfWidth+.05f)*edge;
                PassageQuad(wv,wt,a+offset-Vector3.up*.1f,b+offset-Vector3.up*.1f,b+offset+Vector3.up*NetworkExitPassageHeight,a+offset+Vector3.up*NetworkExitPassageHeight,true);
            }
            var roofStart=Vector3.Lerp(a,b,Mathf.Clamp01(NetworkExitPassageHeight/Mathf.Max(NetworkExitPassageHeight+.1f,Mathf.Abs(delta.y))));
            PassageQuad(wv,wt,roofStart-side*(NetworkExitHalfWidth+.05f)+Vector3.up*NetworkExitPassageHeight,b-side*(NetworkExitHalfWidth+.05f)+Vector3.up*NetworkExitPassageHeight,b+side*(NetworkExitHalfWidth+.05f)+Vector3.up*NetworkExitPassageHeight,roofStart+side*(NetworkExitHalfWidth+.05f)+Vector3.up*NetworkExitPassageHeight,true);
            PassageMesh(networkExitRoot.transform,"지도 출입구 경사면",fv,ft,stone,true);
            PassageMesh(networkExitRoot.transform,"지도 출입구 계단 디딤판",tv,tt,stone,false);
            PassageMesh(networkExitRoot.transform,"지도 출입구 계단실 벽",wv,wt,wall,true);
        }
        void BuildNetworkExitBelts(List<Passage> paths,Material beltMaterial,Material yellow){
            var occupied=new List<Vector3>();
            foreach(var path in paths){
                float length=Vector3.Distance(path.a,path.b);if(length<38f)continue;
                var direction=(path.b-path.a).normalized;var side=Vector3.Cross(Vector3.up,direction);
                for(float start=6f;start<length-18f;start+=30f){
                    float end=Mathf.Min(start+22f,length-6f);var middle=path.a+direction*((start+end)*.5f);
                    if(occupied.Exists(p=>Vector3.Distance(p,middle)<24f))continue;
                    bool junction=false;
                    foreach(var other in paths){
                        var d=(other.b-other.a).normalized;if(Mathf.Abs(Vector3.Dot(direction,d))>.95f)continue;
                        if(FlatSegmentDistance(other.a,path.a+direction*(start-3),path.a+direction*(end+3))<4f||FlatSegmentDistance(other.b,path.a+direction*(start-3),path.a+direction*(end+3))<4f){junction=true;break;}
                    }
                    if(junction)continue;occupied.Add(middle);
                    foreach(float edge in new[]{-1f,1f}){
                        var center=middle+side*(edge*.65f);
                        WalkSlab(networkExitRoot.transform,"출입 통로 무빙워크 벨트",path.a+direction*start+side*edge*.65f+Vector3.up*.025f,path.a+direction*end+side*edge*.65f+Vector3.up*.025f,1.03f,.025f,beltMaterial,false);
                        var go=new GameObject("출입 통로 무빙워크");go.transform.SetParent(networkExitRoot.transform,false);go.transform.position=center;go.transform.rotation=Quaternion.LookRotation(direction*edge);
                        var moving=go.AddComponent<MovingWalkway>();moving.length=end-start;moving.width=1.03f;moving.Register();
                    }
                }
            }
        }
        // A sloped prism subtracts only the walking clearance from the old fixed scene. This also
        // opens OSM sidewalk/building entrance triangles and the old underground ceiling collider.
        sealed class NetworkExitVolume {
            readonly Vector3 center,forward,right;readonly float halfLength,halfWidth,baseY,slope,low,high;
            public readonly Bounds bounds;
            public NetworkExitVolume(Vector3 a,Vector3 b,float width,float bottom,float top){
                center=(a+b)*.5f;forward=b-a;forward.y=0;float length=forward.magnitude;forward=length>.001f?forward/length:Vector3.forward;
                right=Vector3.Cross(Vector3.up,forward);halfLength=length*.5f+.035f;halfWidth=width;baseY=center.y;slope=length>.001f?(b.y-a.y)/length:0;low=bottom;high=top;
                bounds=new Bounds(a+Vector3.up*bottom,Vector3.zero);bounds.Encapsulate(b+Vector3.up*bottom);bounds.Encapsulate(a+Vector3.up*top);bounds.Encapsulate(b+Vector3.up*top);bounds.Expand(new Vector3(width*2+.1f,0,width*2+.1f));
            }
            public float Distance(Vector3 p,int plane){
                var local=p-center;float x=Vector3.Dot(local,right),z=Vector3.Dot(local,forward),y=p.y-baseY-slope*z;
                switch(plane){case 0:return x+halfWidth;case 1:return halfWidth-x;case 2:return z+halfLength;case 3:return halfLength-z;case 4:return y-low;default:return high-y;}
            }
        }
        static List<List<ClipVertex>> SubtractNetworkExit(List<ClipVertex> polygon,NetworkExitVolume volume){
            var outside=new List<List<ClipVertex>>();var remain=polygon;
            for(int plane=0;plane<6&&remain.Count>=3;plane++){
                var inside=new List<ClipVertex>();var rejected=new List<ClipVertex>();
                for(int i=0;i<remain.Count;i++){
                    var a=remain[i];var b=remain[(i+1)%remain.Count];float da=volume.Distance(a.p,plane),db=volume.Distance(b.p,plane);
                    if(da>=0)inside.Add(a);else rejected.Add(a);
                    if((da>=0)!=(db>=0)){float f=da/(da-db);var point=new ClipVertex(Vector3.Lerp(a.p,b.p,f),Vector2.Lerp(a.uv,b.uv,f));inside.Add(point);rejected.Add(point);}
                }
                if(rejected.Count>=3)outside.Add(rejected);remain=inside;
            }
            return outside;
        }
        // `pick`, when given, chooses the meshes to cut instead.
        void CutNetworkExitVolumes(List<NetworkExitVolume> volumes,bool ownStairWalls=false,System.Func<MeshFilter,bool> pick=null){
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>()){
                bool own=networkExitRoot!=null&&filter.transform.IsChildOf(networkExitRoot.transform);string name=filter.name;
                if(pick!=null){if(!pick(filter))continue;}
                else if(ownStairWalls){if(!own||name!="지도 출입구 계단실 벽")continue;}
                else{if(own)continue;if(!(name.StartsWith("OSM ")||name.Contains("지면")||name.Contains("지하역")||name.Contains("대합실")||name=="역 주변 지면"||name=="출구 앞 보도"))continue;}
                var mesh=filter.sharedMesh;var renderer=filter.GetComponent<MeshRenderer>();if(mesh==null||!mesh.isReadable||renderer==null)continue;
                var relevant=new List<NetworkExitVolume>();foreach(var v in volumes)if(v.bounds.Intersects(renderer.bounds))relevant.Add(v);if(relevant.Count==0)continue;
                var vertices=mesh.vertices;var uv=mesh.uv;var indices=mesh.triangles;var output=new List<Vector3>();var tex=new List<Vector2>();var triangles=new List<int>();bool changed=false;
                for(int k=0;k<indices.Length;k+=3){
                    var polygon=new List<ClipVertex>();for(int j=0;j<3;j++){int ix=indices[k+j];polygon.Add(new ClipVertex(filter.transform.TransformPoint(vertices[ix]),uv.Length==vertices.Length?uv[ix]:Vector2.zero));}
                    var fragments=new List<List<ClipVertex>>{polygon};
                    var bounds=new Bounds(polygon[0].p,Vector3.zero);foreach(var vertex in polygon)bounds.Encapsulate(vertex.p);
                    foreach(var volume in relevant){
                        if(!volume.bounds.Intersects(bounds))continue;
                        var next=new List<List<ClipVertex>>();foreach(var fragment in fragments)next.AddRange(SubtractNetworkExit(fragment,volume));
                        if(next.Count!=fragments.Count||!SameNetworkClip(fragments,next))changed=true;
                        fragments=next;if(fragments.Count==0)break;
                    }
                    foreach(var fragment in fragments)EmitClip(fragment,filter.transform,output,tex,triangles);
                }
                if(!changed)continue;
                var clipped=new Mesh{name=mesh.name+" mapped exit clearance",indexFormat=IndexFormat.UInt32};clipped.SetVertices(output);clipped.SetUVs(0,tex);clipped.SetTriangles(triangles,0);clipped.RecalculateNormals();clipped.RecalculateBounds();filter.sharedMesh=clipped;
                var owner=filter.GetComponent<AccessMeshOwner>();if(owner==null)owner=filter.gameObject.AddComponent<AccessMeshOwner>();var old=owner.mesh;owner.mesh=clipped;
                var colliders=filter.GetComponents<Collider>();bool physical=colliders.Length>0;foreach(var collider in colliders)DestroyImmediate(collider);
                if(physical&&triangles.Count>0)filter.gameObject.AddComponent<MeshCollider>().sharedMesh=clipped;
                if(old!=null)DestroyImmediate(old);
            }
        }
        static bool SameNetworkClip(List<List<ClipVertex>> a,List<List<ClipVertex>> b){
            if(a.Count!=b.Count)return false;
            for(int i=0;i<a.Count;i++){if(a[i].Count!=b[i].Count)return false;for(int k=0;k<a[i].Count;k++)if((a[i][k].p-b[i][k].p).sqrMagnitude>.000001f)return false;}return true;
        }
    }
}
