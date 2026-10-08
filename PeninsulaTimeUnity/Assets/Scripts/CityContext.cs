using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
namespace PeninsulaTime {
    // Offline OSM footprint context. Heights without tags are estimates, not surveyed facades/interiors.
    public class CityContext : MonoBehaviour {
        public class Feature {public string id,name;public bool building;public float lon,lat,size;public Vector2[] points;}
        static List<Feature> source;
        public Transform viewer;public float lon,lat;public Func<Feature,GameObject> create;
        readonly Dictionary<string,GameObject> shown=new Dictionary<string,GameObject>();Vector3 previous=new Vector3(float.MaxValue,0,0);float next;
        public static List<Feature> Source {
            get {if(source!=null)return source;source=new List<Feature>();var text=Resources.Load<TextAsset>("Geo/ChangwonCity");if(text==null)return source;
                foreach(var row in text.text.Split('\n')){var f=row.Split('|');if(f.Length<7)continue;var pairs=f[6].Split(';');var points=new Vector2[pairs.Length];
                    for(int i=0;i<pairs.Length;i++){var xy=pairs[i].Split(',');points[i]=new Vector2(Number(xy[0]),Number(xy[1]));}
                    source.Add(new Feature{building=f[0]=="b",id=f[1],lon=Number(f[2]),lat=Number(f[3]),size=Number(f[4]),name=f[5],points=points});
                }return source;
            }
        }
        static float Number(string s){return float.Parse(s,CultureInfo.InvariantCulture);}
        void Update(){if(Time.time<next||viewer==null)return;next=Time.time+1;if((viewer.position-previous).sqrMagnitude>120*120)Refresh(viewer.position);}
        public void Refresh(Vector3 position){
            previous=position;float sx=111320*Mathf.Cos(lat*Mathf.Deg2Rad);var wanted=new HashSet<string>();
            foreach(var f in Source){float x=(f.lon-lon)*sx-position.x,z=(f.lat-lat)*111320-position.z;if(x*x+z*z>600*600)continue;
                wanted.Add(f.id);if(!shown.ContainsKey(f.id))shown[f.id]=create(f);
            }
            var remove=new List<string>();foreach(var pair in shown)if(!wanted.Contains(pair.Key)){if(Application.isPlaying)Destroy(pair.Value);else DestroyImmediate(pair.Value);remove.Add(pair.Key);}foreach(var id in remove)shown.Remove(id);
        }
    }
    public partial class WorldBuilder {
        void AddCityContext(NetStation at,Vector3 origin){
            if(at.lon<128.3f||at.lon>128.9f||at.lat<35||at.lat>35.45f)return;
            var context=root.AddComponent<CityContext>();context.lon=at.lon;context.lat=at.lat;context.viewer=Viewer;
            float sx=111320*Mathf.Cos(at.lat*Mathf.Deg2Rad);
            context.create=f=>{
                var group=new GameObject((f.building?"OSM 건물 ":"OSM 도로 ")+f.id+" "+f.name);group.transform.SetParent(root.transform,false);
                var points=new List<Vector3>();foreach(var p in f.points)points.Add(origin+new Vector3((p.x-at.lon)*sx,0,(p.y-at.lat)*111320));
                if(f.building){
                    if(points.Count>1&&(points[0]-points[points.Count-1]).sqrMagnitude<.01f)points.RemoveAt(points.Count-1);
                    var v=new List<Vector3>();var t=new List<int>();
                    for(int i=0;i<points.Count;i++){var a=points[i];var b=points[(i+1)%points.Count];PassageQuad(v,t,a,b,b+Vector3.up*f.size,a+Vector3.up*f.size,true);}
                    PassageMesh(group.transform,"외벽",v,t,Mat("cw-building",new Color(.65f,.67f,.67f),0,"concrete",3),true);
                    var roof=new List<Vector3>();foreach(var p in points)roof.Add(p+Vector3.up*f.size);
                    PassageMesh(group.transform,"지붕",roof,TriangulateParking(roof),Mat("cw-roof",new Color(.4f,.43f,.44f)),true);
                }else{
                    RoadRibbon(group.transform,points,Mathf.Clamp(f.size,1.5f,35),"OSM 도로",Mat("cw-asphalt",new Color(.22f,.24f,.25f),0,"asphalt",4),true);
                }return group;
            };
            context.Refresh(origin);
        }
        void RoadRibbon(Transform parent,List<Vector3> path,float width,string name,Material material,bool collider){
            if(path.Count<2)return;var v=new List<Vector3>();var t=new List<int>();
            for(int k=0;k<path.Count;k++){
                var before=(path[k]-path[Mathf.Max(0,k-1)]).normalized;var after=(path[Mathf.Min(path.Count-1,k+1)]-path[k]).normalized;
                if(k==0)before=after;if(k==path.Count-1)after=before;
                var normal=Vector3.Cross(Vector3.up,(before+after).normalized);if(normal.sqrMagnitude<.01f)normal=Vector3.Cross(Vector3.up,after);
                float correction=Mathf.Max(.3f,Vector3.Dot(normal,Vector3.Cross(Vector3.up,after)));
                v.Add(path[k]+normal*width*.5f/correction);v.Add(path[k]-normal*width*.5f/correction);
                if(k>0){int n=k*2;t.AddRange(new[]{n-2,n-1,n,n-1,n+1,n});}
            }PassageMesh(parent,name,v,t,material,collider);
        }
    }
}
