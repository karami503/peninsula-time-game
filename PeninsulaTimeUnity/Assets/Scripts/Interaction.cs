using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Something in the 3D street views that reacts to a click. GameController shows Hint under the crosshair.
    // Informational things (screen doors, trains, buses) only explain themselves: they are boarded by walking in.
    public abstract class Interactable : MonoBehaviour
    {
        public abstract string Hint{get;}
        public virtual bool Informational{get{return false;}}
    }

    // Door leaf swinging inward on its hinge.
    public class DoorInteract : Interactable
    {
        const float OpenAngle=95f,SwingSpeed=2.5f;
        Transform leaf;Quaternion closed;float sign,amount;bool open;
        public bool IsOpen{get{return open;}}
        public bool hasInterior;public Vector3 entry;
        public override string Hint{get{return open&&hasInterior?"건물 안으로 들어가기":open?"문 닫기":"문 열기";}}
        public void Init(Transform hingedLeaf,float swingSign){leaf=hingedLeaf;closed=leaf.rotation;sign=swingSign;}
        public string Toggle(){open=!open;return open?"문을 열었습니다.":"문을 닫았습니다.";}
        void Update(){Swing(Time.deltaTime);}
        public void Swing(float seconds)
        {
            float target=open?1:0;if(Mathf.Approximately(amount,target))return;
            amount=Mathf.MoveTowards(amount,target,SwingSpeed*seconds);
            leaf.rotation=Quaternion.Euler(0,sign*OpenAngle*amount,0)*closed;
        }
    }

    // Street light: switched on at dusk and off at dawn by DayCycle (the lamp heads glow; the nearest few also
    // light the street). Not clickable.
    public class StreetLamp : MonoBehaviour
    {
        public static readonly List<StreetLamp> All=new List<StreetLamp>();
        public Vector3 head;
        void OnEnable(){if(!All.Contains(this))All.Add(this);}
        void OnDisable(){All.Remove(this);}
    }

    // A car the player can drive, or a bus that explains how to board it.
    public class VehicleInteract : Interactable
    {
        public bool bus;
        public override bool Informational{get{return bus;}}
        public override string Hint
        {
            get
            {
                if(!bus)return "자동차 운전하기";
                var v=GetComponent<TrafficVehicle>();string route=v!=null&&v.Route.Length>0?v.Route+"번 버스":"버스";
                return v!=null&&v.doors!=null&&v.doors.amount>.8f?route+" · 열린 문으로 걸어 들어가 타세요":route+" · 정류장에서 문이 열리면 타세요";
            }
        }
    }

    // Walks the sidewalk beside a road graph, swinging legs and arms; crosses at nodes on foot.
    public class Pedestrian : Interactable
    {
        const float Swing=28f,TalkSeconds=4f;
        int persona,talks;
        // Personal space, how far ahead people look, how far they step aside, and how long they wait before squeezing past.
        const float Personal=.6f,LookAhead=1.8f,KeepRight=.5f,SideStep=1.2f,MaxWait=4f;
        // Every walking person, for cars that must stop for them and people who must not walk through each other.
        public static readonly List<Pedestrian> Walking=new List<Pedestrian>();
        TrafficGraph graph;int from,to;float travelled,speed,side,sideSign,height,phase,pause;
        bool crossing;Vector3 crossFrom,crossTo,crossCorner;float crossDone;
        float dodge,waited;Pedestrian waitingFor;
        Transform[] legs,arms;Quaternion[] legRest,armRest;System.Random random;
        public override string Hint{get{return "말 걸기";}}
        public bool Talking{get{return pause>0;}}
        public bool Stopped{get;private set;}
        // Registered once walking (also in edit-mode checks, where OnEnable does not run); destroyed people drop out.
        void OnEnable(){if(graph!=null&&!Walking.Contains(this))Walking.Add(this);}
        void OnDisable(){Walking.Remove(this);}

        public void Begin(TrafficGraph network,int start,float walkSpeed,float sideOffset,float groundHeight,int seed,Transform[] legParts,Transform[] armParts)
        {
            graph=network;from=start;speed=walkSpeed;side=sideOffset;sideSign=1f;height=groundHeight;random=new System.Random(seed);
            persona=random.Next(Dialogue.Personas.Length);
            if(!Walking.Contains(this))Walking.Add(this);
            legs=legParts;arms=armParts;legRest=Rest(legs);armRest=Rest(arms);
            to=graph.links[from][random.Next(graph.links[from].Count)];
            travelled=(float)random.NextDouble()*Length();phase=(float)random.NextDouble()*6f;
            Place();
        }
        static Quaternion[] Rest(Transform[] parts){var r=new Quaternion[parts.Length];for(int i=0;i<parts.Length;i++)r[i]=parts[i].localRotation;return r;}
        float Length(){return Mathf.Max(.01f,Vector3.Distance(graph.nodes[from],graph.nodes[to]));}
        Vector3 SidewalkPoint(int a,int b,float along,float sign)
        {
            var dir=(graph.nodes[b]-graph.nodes[a]).normalized;
            return graph.nodes[a]+dir*along+Vector3.Cross(Vector3.up,dir)*(graph.Width(a,b)*.5f+side)*sign+Vector3.up*height;
        }
        Vector3 SidewalkPoint(int a,int b,float along){return SidewalkPoint(a,b,along,sideSign);}
        public void Step(float seconds)
        {
            if(graph==null)return;
            if(pause>0){pause-=seconds;Stopped=true;Animate(0,seconds);return;}
            float pace=speed*Clearance(seconds);Stopped=pace<=0;
            if(crossing)
            {
                crossDone+=pace*seconds/Mathf.Max(.01f,Vector3.Distance(crossFrom,crossTo));
                if(crossDone<.5f){Face(crossCorner-crossFrom);transform.position=Vector3.Lerp(crossFrom,crossCorner,crossDone*2f)+transform.right*dodge;}
                else{Face(crossTo-crossCorner);transform.position=Vector3.Lerp(crossCorner,crossTo,(crossDone-.5f)*2f)+transform.right*dodge;}
                if(crossDone>=1)crossing=false;
                Animate(pace,seconds);return;
            }
            travelled+=pace*seconds;
            float length=Length();
            if(travelled>=length)
            {
                var end=SidewalkPoint(from,to,length);
                var options=graph.links[to];int next=options[0];float nextSign=sideSign;
                // Stay on the same side of the carriageway at a junction. A turn to the
                // opposite pavement requires a marked crossing and must not cut through traffic.
                float nearest=float.MaxValue;
                foreach(int candidate in options)
                {
                    if(candidate==from&&options.Count>1)continue;
                    foreach(float sign in new[]{-1f,1f})
                    {
                        var destination=SidewalkPoint(to,candidate,0,sign);
                        float distance=(end-destination).sqrMagnitude;
                        if(distance<nearest){nearest=distance;next=candidate;nextSign=sign;}
                    }
                }
                from=to;to=next;sideSign=nextSign;travelled=0;
                var start=SidewalkPoint(from,to,0);
                // Do not interpolate across the road: continue from the next sidewalk.
                if((end-start).sqrMagnitude>.01f)
                {
                    crossing=true;crossFrom=end;crossTo=start;crossDone=0;
                    var centre=graph.nodes[from]+Vector3.up*height;
                    var outward=(end-centre).normalized+(start-centre).normalized;
                    crossCorner=outward.sqrMagnitude>.01f?centre+outward.normalized*(graph.Width(from,to)*.72f+side):end;
                }
            }
            if(!crossing)Place();
            Animate(pace,seconds);
        }
        // Speed factor (0..1) for this step: slow behind people, stop for someone right in front or a car in the way,
        // and step aside — to the right (우측통행) for someone coming head-on, otherwise to the side with more room.
        float Clearance(float seconds)
        {
            var here=transform.position;var forward=transform.forward;forward.y=0;
            if(forward.sqrMagnitude<.01f)return 1f;
            forward.Normalize();var right=Vector3.Cross(Vector3.up,forward);
            float factor=1f,want=0f,nearest=LookAhead;Pedestrian blocker=null;
            foreach(var other in Walking)
            {
                if(other==this||other==null)continue;
                var o=other.transform.position-here;if(Mathf.Abs(o.y)>1.5f)continue;
                float ahead=Vector3.Dot(o,forward);if(ahead<=0||ahead>LookAhead)continue;
                float lateral=Vector3.Dot(o,right);if(Mathf.Abs(lateral)>Personal*1.5f)continue;
                if(ahead<nearest){nearest=ahead;float step=other.Stopped?Personal+.15f:KeepRight;want=Mathf.Abs(lateral)<.15f?step:lateral>0?-step:step;}
                if(Mathf.Abs(lateral)>=Personal)continue;
                bool oncoming=Vector3.Dot(other.transform.forward,forward)<-.2f;
                float allowed=oncoming?(ahead<Personal+.25f?0f:1f):Mathf.Clamp01((ahead-Personal)/(LookAhead-Personal));
                if(allowed<factor){factor=allowed;blocker=other;}
            }
            // The walking player is someone standing still: slow down and step round them.
            if(TrafficVehicle.PlayerFeet.HasValue)
            {
                var o=TrafficVehicle.PlayerFeet.Value-here;float ahead=Vector3.Dot(o,forward),lateral=Vector3.Dot(o,right);
                if(Mathf.Abs(o.y)<1.5f&&ahead>0&&ahead<LookAhead&&Mathf.Abs(lateral)<Personal*1.5f)
                {
                    if(ahead<nearest)want=lateral>0?-(Personal+.15f):Personal+.15f;
                    if(Mathf.Abs(lateral)<Personal)factor=Mathf.Min(factor,Mathf.Clamp01((ahead-Personal)/(LookAhead-Personal)));
                }
            }
            // Two people waiting for each other: the one with the lower id squeezes past slowly.
            if(factor<=0&&blocker!=null&&blocker.waitingFor==this&&GetInstanceID()<blocker.GetInstanceID())factor=.35f;
            // Never step onto a car; keep walking if a car is already over us so we get out from under it.
            var probe=here+forward*.55f;
            foreach(var car in TrafficVehicle.Active)if(car!=null&&car.Covers(probe,.35f)&&!car.Covers(here,0f)){factor=0;blocker=null;break;}
            waitingFor=factor<=0?blocker:null;
            waited=factor<=0?waited+seconds:0;
            if(waited>MaxWait&&blocker!=null)factor=.35f; // squeeze past someone who will not move
            dodge=Mathf.MoveTowards(dodge,want,SideStep*seconds);
            return factor;
        }
        void Place()
        {
            Face(graph.nodes[to]-graph.nodes[from]);
            transform.position=SidewalkPoint(from,to,travelled)+transform.right*dodge;
        }
        void Face(Vector3 direction){direction.y=0;if(direction.sqrMagnitude>1e-6f)transform.rotation=Quaternion.LookRotation(direction);}
        void Animate(float pace,float seconds)
        {
            phase+=pace*seconds*4.5f;
            float angle=pace>0?Mathf.Sin(phase)*Swing:0;
            for(int i=0;i<legs.Length;i++)legs[i].localRotation=Quaternion.AngleAxis(i==0?angle:-angle,Vector3.right)*legRest[i];
            for(int i=0;i<arms.Length;i++)arms[i].localRotation=Quaternion.AngleAxis(i==0?-angle*.8f:angle*.8f,Vector3.right)*armRest[i];
        }
        // Stops, turns to the viewer and answers: who they are, where they are, the time of day, how the city is doing,
        // and how often they have been asked already. No line repeats until many others have been said.
        public string Talk(Vector3 viewer,int happiness,string place,bool night)
        {
            pause=TalkSeconds;Face(viewer-transform.position);
            if(random==null)random=new System.Random(GetInstanceID());
            var line=Dialogue.Line(persona,talks++,happiness,place,night,random);
            return Dialogue.Personas[persona]+": “"+line+"”";
        }
    }
}
