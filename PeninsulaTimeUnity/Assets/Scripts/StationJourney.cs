using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Names/order are network data. Distances and station architecture are a playable approximation.
    public class StationJourney : MonoBehaviour
    {
        public const float Spacing=450f,Headway=30f;
        public NetLine line;
        public readonly List<NetStation> stops=new List<NetStation>();
        public VehicleDoors doors;
        public Transform train;
        public int direction,index;
        public bool occupied,started;
        public float speed,clock,wait=8f;
        public Vector3[] path;public float[] pathLengths,stopDistances;
        public float travelled,startDelay;public Vector3 origin;
        float segmentElapsed,segmentStart;int segmentIndex=-1;bool initialDwell=true;
        Quaternion modelRotation;bool capturedRotation;
        Renderer[] vehicleRenderers;Collider[] vehicleColliders;bool visible=true;
        void Show(bool on){if(on==visible)return;visible=on;
            if(vehicleRenderers==null)vehicleRenderers=train.GetComponentsInChildren<Renderer>(true);
            if(vehicleColliders==null)vehicleColliders=train.GetComponentsInChildren<Collider>(true);
            foreach(var r in vehicleRenderers)if(r!=null)r.enabled=on;
            foreach(var c in vehicleColliders)if(c!=null)c.enabled=on;
        }
        public bool Bus=>line!=null&&(line.kind=="bus"||line.kind=="brt");
        public float Period=>Bus?50f:Headway;
        public void SetPath(Vector3[] points,float[] stopsAt){path=points;stopDistances=stopsAt;pathLengths=new float[path.Length];for(int i=1;i<path.Length;i++)pathLengths[i]=pathLengths[i-1]+Vector3.Distance(path[i-1],path[i]);}
        void Place(float distance){
            if(!capturedRotation){modelRotation=train.rotation;capturedRotation=true;}
            if(path==null||path.Length<2){train.position=origin+new Vector3(0,0,distance);return;}
            int k=1;while(k<path.Length-1&&pathLengths[k]<distance)k++;
            float t=(distance-pathLengths[k-1])/Mathf.Max(.001f,pathLengths[k]-pathLengths[k-1]);
            train.position=Vector3.LerpUnclamped(path[k-1],path[k],t);
            var heading=path[k]-path[k-1];if(heading.sqrMagnitude>.001f)train.rotation=Quaternion.LookRotation(heading)*modelRotation;
        }
        public bool Stopped{get{return speed<.01f&&doors!=null&&doors.cabin.Open;}}
        public bool Finished{get{return started&&index==stops.Count-1&&speed<.01f;}}
        public NetStation Current{get{return stops[Mathf.Clamp(index,0,stops.Count-1)];}}
        public static List<NetStation> Next(NetLine line,NetStation start,int direction,int count=3)
        {
            var result=new List<NetStation>{start};
            int at=line.stops.IndexOf(start),step=direction==0?1:-1;
            if(at<0)return result;
            for(int k=0;k<count;k++)
            {
                at+=step;
                if(line.loop)at=(at+line.stops.Count)%line.stops.Count;
                if(at<0||at>=line.stops.Count||line.stops[at]==start)break;
                result.Add(line.stops[at]);
            }
            return result;
        }
        void Update(){Step(Time.deltaTime);}
        public void Board(){Show(true);occupied=true;started=true;wait=Mathf.Max(wait,5f);}
        public void Step(float dt)
        {
            if(train==null||doors==null||stops.Count==0||float.IsNaN(dt)||float.IsInfinity(dt)||dt<0)return;
            if(!started)
            {
                // Consume the actual delay even on a long frame: each line retains its 15-second phase.
                if(startDelay>0){float delayed=Mathf.Min(startDelay,dt);startDelay-=delayed;dt-=delayed;if(startDelay>0){Show(false);return;}}
                if(clock+dt>=Period)initialDwell=false;
                clock=Mathf.Repeat(clock+dt,Period);
                float span=10f/TransitSpeed.Multiplier(line!=null?line.kind:"metro");
                bool departure=clock>=10&&clock<10+span,approach=clock>=Period-span;
                float z=0;
                if(departure){float t=(clock-10)/span;z=80f*t*t;speed=160f*t/span;}
                else if(approach){float t=(Period-clock)/span;z=-80f*t*t;speed=160f*t/span;}
                else{z=clock<10?0:-80f;speed=0;}
                Show(clock<10||departure||approach);Place(z);
                doors.Set(clock<8f?(initialDwell?1f:Mathf.Clamp01(clock/.8f)):Mathf.Clamp01((8.8f-clock)/.8f));
                return;
            }
            // Consume complete states, rather than losing excess frame time at a dwell/arrival boundary.
            // Bounded by the remaining stops; no per-frame temporary lists or fixed-step busy loop.
            for(int transition=0;transition<stops.Count*4+8;transition++)
            {
                if(index>=stops.Count-1){speed=0;doors.Move(true,dt);return;}
                if(wait>0)
                {
                    speed=0;
                    float available=wait>1.2f?wait-1.2f:wait;
                    float consumed=Mathf.Min(dt,available);
                    doors.Move(wait>1.2f,consumed);wait=Mathf.Max(0,wait-consumed);dt-=consumed;
                    if(dt<=0)return;
                    continue;
                }
                if(doors.amount>.001f)
                {
                    speed=0;float consumed=Mathf.Min(dt,doors.amount*.8f);
                    doors.Move(false,consumed);dt-=consumed;
                    if(dt<=0)return;
                    continue;
                }
                string kind=line!=null?line.kind:"metro";
                if(segmentIndex!=index){segmentIndex=index;segmentStart=travelled;segmentElapsed=0;}
                float target=stopDistances!=null?stopDistances[index+1]:(index+1)*Spacing;
                float distance=Mathf.Max(0,target-segmentStart);
                float duration=TransitSpeed.JourneyDuration(kind,distance);
                float moving=Mathf.Min(dt,Mathf.Max(0,duration-segmentElapsed));
                segmentElapsed+=moving;dt-=moving;
                travelled=segmentStart+TransitSpeed.JourneyPosition(kind,distance,segmentElapsed,out speed);Place(travelled);
                if(segmentElapsed>=duration)
                {
                    travelled=target;Place(target);index++;speed=0;wait=10f;
                    Sfx.PlayAt("chime",train.position,.65f);
                    if(dt>0)continue;
                }
                return;
            }
        }
    }
}
