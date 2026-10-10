using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // The 진해선 shuttle in the carved 진해: carved coaches running on the mapped line between its stops (진해역 and
    // 경화역), stopping with the doors open on the platform side for Dwell seconds, then closing them and running on at
    // up to MaxSpeed. The player walks in through an open door (a Cabin, as on buses and trains elsewhere), is carried
    // along standing inside, and walks out at the other stop. Timetable and speed are game values, not the real service.
    public sealed class JinhaeTrain : MonoBehaviour
    {
        public const float MaxSpeed=16f,Accel=.8f,Dwell=30f,CarLength=20f,Coupling=1.5f;
        public CarvedDistrict district;
        Vector2[] path;float[] along;
        public readonly List<Transform> cars=new List<Transform>();
        public readonly List<VehicleDoors> doors=new List<VehicleDoors>();
        public string[] names;public float[] stops;public Vector2[] platforms; // per stop: its place on the line, a point on its platform
        public int at,next;public float s,speed,clock;public string phase="dwell";
        public System.Action<string> arrived,leaving; // the stop's name
        public bool Stopped{get{return phase=="dwell"||phase=="open";}}
        public bool Open{get{return phase=="dwell";}}
        public string Here{get{return names[at];}}
        public float Length{get{return along[along.Length-1];}}

        // The named line chained from its mapped ways (joined where their ends meet within 2 m), grown both ways from
        // the way passing nearest `near`.
        public static List<Vector2> Chain(List<KeyValuePair<string,Vector2[]>> rails,string name,Vector2 near)
        {
            var ways=new List<Vector2[]>();foreach(var r in rails)if(r.Key==name&&r.Value.Length>=2)ways.Add(r.Value);
            var line=new List<Vector2>();if(ways.Count==0)return line;
            int first=0;float best=float.MaxValue;
            for(int w=0;w<ways.Count;w++)foreach(var p in ways[w]){float d=(p-near).sqrMagnitude;if(d<best){best=d;first=w;}}
            line.AddRange(ways[first]);ways.RemoveAt(first);
            for(bool grew=true;grew;)
            {
                grew=false;
                for(int w=0;w<ways.Count&&!grew;w++)
                {
                    var way=new List<Vector2>(ways[w]);Vector2 head=line[0],tail=line[line.Count-1];
                    if((way[way.Count-1]-tail).sqrMagnitude<4f||(way[0]-head).sqrMagnitude<4f)way.Reverse();
                    if((way[0]-tail).sqrMagnitude<4f){way.RemoveAt(0);line.AddRange(way);grew=true;}
                    else if((way[way.Count-1]-head).sqrMagnitude<4f){way.RemoveAt(way.Count-1);line.InsertRange(0,way);grew=true;}
                    if(grew)ways.RemoveAt(w);
                }
            }
            return line;
        }
        public void Lay(List<Vector2> line)
        {
            path=line.ToArray();along=new float[path.Length];
            for(int i=1;i<path.Length;i++)along[i]=along[i-1]+Vector2.Distance(path[i-1],path[i]);
        }
        // How far along the line its nearest point to `p` is.
        public float Project(Vector2 p)
        {
            float found=0,best=float.MaxValue;
            for(int k=1;k<path.Length;k++)
            {
                var a=path[k-1];var d=path[k]-a;float l2=d.sqrMagnitude;if(l2<1e-6f)continue;
                float t=Mathf.Clamp01(Vector2.Dot(p-a,d)/l2);float gap=(a+d*t-p).sqrMagnitude;
                if(gap<best){best=gap;found=along[k-1]+t*Mathf.Sqrt(l2);}
            }
            return found;
        }
        public Vector2 Point(float distance)
        {
            distance=Mathf.Clamp(distance,0,Length);
            int k=System.Array.BinarySearch(along,distance);if(k<0)k=~k;k=Mathf.Clamp(k,1,along.Length-1);
            float l=along[k]-along[k-1];return l<1e-4f?path[k]:Vector2.Lerp(path[k-1],path[k],(distance-along[k-1])/l);
        }
        public Vector2 Tangent(float distance){var d=Point(distance+4f)-Point(distance-4f);return d.sqrMagnitude<1e-6f?Vector2.up:d.normalized;}
        // A point on the line at its own level (rail top less bed and rail, over bridges and in cuttings too): coach
        // floors are measured from here.
        public Vector3 OnLine(float distance){var p=Point(distance);return new Vector3(p.x,district.LineHeight(p),p.y);}

        // Coaches of `body`, each with two doors a side of two sliding `leaf` leaves, walkable inside.
        public void Make(Mesh body,Mesh leaf,Material material,int count)
        {
            for(int c=0;c<count;c++)
            {
                var car=new GameObject("진해선 객차 "+(c+1));car.transform.SetParent(transform,false);
                car.AddComponent<MeshFilter>().sharedMesh=body;car.AddComponent<MeshRenderer>().sharedMaterial=material;car.AddComponent<MeshCollider>().sharedMesh=body;
                var cabin=car.AddComponent<Cabin>();
                cabin.kind="jinhae";cabin.halfWidth=1.2f; // riders keep .35 m off the walls and doors, past the camera's near plane
                cabin.back=-9.75f;cabin.front=9.75f;cabin.floor=.75f;cabin.exitDistance=.8f;
                cabin.doors=new[]{-6.5f,6.5f};cabin.doorHalf=.6f;cabin.aisle=.6f;cabin.Register();
                var set=car.AddComponent<VehicleDoors>();set.cabin=cabin;
                foreach(float side in new[]{-1f,1f})foreach(float z in cabin.doors)foreach(float half in new[]{-1f,1f})
                {
                    var l=new GameObject("진해선 객차 문");l.transform.SetParent(car.transform,false);
                    l.transform.localPosition=new Vector3(side*1.4f,0,z); /* proud of the wall by 2.5 cm: no shared faces, and clear of a rider standing at the door */l.transform.localRotation=Quaternion.Euler(0,half>0?180f:0,0);
                    l.AddComponent<MeshFilter>().sharedMesh=leaf;l.AddComponent<MeshRenderer>().sharedMaterial=material;
                    var box=l.AddComponent<BoxCollider>();box.center=new Vector3(0,1.875f,-.375f);box.size=new Vector3(.25f,2.25f,.75f);
                    set.Add(l.transform,new Vector3(0,0,half*.75f),side);
                }
                cars.Add(car.transform);doors.Add(set);
            }
        }
        // Stands at stop `stop` with the doors open, Dwell seconds before leaving.
        public void Begin(int stop)
        {
            at=stop;next=(stop+1)%stops.Length;s=stops[stop];speed=0;clock=0;phase="dwell";
            Place();UseSide();foreach(var d in doors)d.Set(1f);
        }

        void Update(){Step(Time.deltaTime);}
        public void Step(float dt)
        {
            switch(phase)
            {
                case "dwell":clock+=dt;if(clock>=Dwell){phase="close";if(leaving!=null)leaving(names[next]);}break;
                case "close":if(Doors(false,dt)<=.001f)phase="run";break; // VehicleDoors.Set stops a hair short of 0 and 1
                case "run":
                {
                    float target=stops[next],left=Mathf.Abs(target-s);
                    speed=Mathf.Min(MaxSpeed,speed+Accel*dt,Mathf.Sqrt(2f*Accel*left));
                    float move=Mathf.Max(speed,.3f)*dt;
                    if(move>=left){s=target;speed=0;at=next;next=(at+1)%stops.Length;phase="open";}else s+=Mathf.Sign(target-s)*move;
                    Place();if(phase=="open"){UseSide();if(arrived!=null)arrived(names[at]);}
                    Physics.SyncTransforms();break;
                }
                case "open":if(Doors(true,dt)>=.999f){phase="dwell";clock=0;}break;
            }
        }
        float Doors(bool open,float dt){float amount=0;foreach(var d in doors)amount=d.Move(open,dt);return amount;}
        // Each coach on the line by its two bogie points (8 m either side of its middle), so it follows curves and grades.
        public void Place()
        {
            for(int c=0;c<cars.Count;c++)
            {
                float middle=s+(c-(cars.Count-1)*.5f)*(CarLength+Coupling);
                Vector3 back=OnLine(middle-8f),front=OnLine(middle+8f);
                cars[c].SetPositionAndRotation((back+front)*.5f,Quaternion.LookRotation(front-back));
            }
        }
        // The doors on the side of this stop's platform.
        void UseSide()
        {
            foreach(var d in doors)
            {
                var car=d.transform;var toward=new Vector3(platforms[at].x,car.position.y,platforms[at].y)-car.position;
                d.UseSide(Vector3.Dot(toward,car.right)>0?1:-1);
            }
        }
    }
}
