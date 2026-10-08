using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Something in the Changwon open world the player can use with F or a click (cars, shops, bus stops, bikes, mission points).
    public class ChangwonThing : Interactable
    {
        public string kind="",hint="",title="",detail;public object payload;public bool informational;
        public override string Hint{get{return hint;}}
        public override bool Informational{get{return informational;}}
    }

    // Arcade driving for the open world: follows the ground (roads, bridges, tunnels, terrain) with raycasts,
    // is blocked by buildings, trees and other vehicles, and drives anywhere in the city.
    public class ChangwonCar : MonoBehaviour
    {
        public static readonly List<ChangwonCar> All=new List<ChangwonCar>();
        public string model="Car",plate="";public float maxSpeed=50f,acceleration=7.5f,brake=16f,length=4.3f,width=1.9f,wheelBase=2.7f,rideHeight=.06f;
        public bool bike,npc,bus;
        public float speed,steer;public Vector3 forward=Vector3.forward;
        public bool PlayerDriving{get;set;}
        public float lastHit;
        Quaternion modelRotation;bool initialised;AudioSource engine;
        static readonly RaycastHit[] hits=new RaycastHit[16];

        public void Init(Vector3 position,Vector3 facing){
            modelRotation=transform.rotation;facing.y=0;forward=facing.sqrMagnitude>0?facing.normalized:Vector3.forward;
            transform.position=position;transform.rotation=Quaternion.LookRotation(forward)*modelRotation;initialised=true;
            if(!All.Contains(this))All.Add(this);
        }
        void OnDisable(){All.Remove(this);}
        void OnEnable(){if(initialised&&!All.Contains(this))All.Add(this);}

        public void SetEngine(bool on){
            if(on&&engine==null)engine=Sfx.Attach(gameObject,bus?"bus-engine":"engine",.5f,60f);
            if(engine!=null){engine.enabled=on;if(on&&!engine.isPlaying)engine.Play();}
        }

        // Ground under a point: the highest walkable collider within reach below 'top', else road data, else terrain.
        public static float Ground(Vector3 at,float top,Transform ignore,out Vector3 normal){
            normal=Vector3.up;float best=float.MinValue;
            int n=Physics.RaycastNonAlloc(new Vector3(at.x,top,at.z),Vector3.down,hits,top-at.y+30f,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<n;i++){
                var c=hits[i].collider;if(ignore!=null&&c.transform.IsChildOf(ignore))continue;
                if(!IsGround(c))continue;
                if(hits[i].point.y>best){best=hits[i].point.y;normal=hits[i].normal;}
            }
            if(best>float.MinValue)return best;
            ChangwonData.Road road;float along;Vector3 p;
            if(ChangwonData.NearestRoad(new Vector3(at.x,top-2.5f,at.z),Mathf.Max(4f,6f),out road,out along,out p,false)&&Mathf.Abs(p.y-(top-2.5f))<8f)return p.y+.08f;
            return ChangwonData.Height(at.x,at.z);
        }
        public static bool IsGround(Collider c){var n=c.name;return n=="지형"||n=="도로"||n.StartsWith("platform");}

        // One frame of player (or AI) input. throttle -1..1, steer -1..1.
        public void Drive(float throttle,float steerInput,bool handbrake,float dt){
            if(dt<=0)return;
            float target=throttle*(throttle>=0?maxSpeed:maxSpeed*.28f);
            if(handbrake)speed=Mathf.MoveTowards(speed,0,brake*1.2f*dt);
            else if(Mathf.Abs(throttle)<.05f)speed=Mathf.MoveTowards(speed,0,(bike?2.5f:3.2f)*dt);
            else if(Mathf.Sign(throttle)!=Mathf.Sign(speed)&&Mathf.Abs(speed)>.5f)speed=Mathf.MoveTowards(speed,0,brake*dt);
            else speed=Mathf.MoveTowards(speed,target,acceleration*(1f-.55f*Mathf.Abs(speed)/maxSpeed)*dt);
            // Steering: tighter at low speed, gentle at motorway speed.
            float maxAngle=Mathf.Lerp(34f,6f,Mathf.Abs(speed)/maxSpeed)*(handbrake?1.5f:1f);
            steer=Mathf.MoveTowards(steer,steerInput*maxAngle,140f*dt);
            float yawRate=speed/wheelBase*Mathf.Tan(steer*Mathf.Deg2Rad)*Mathf.Rad2Deg;
            forward=Quaternion.Euler(0,yawRate*dt,0)*forward;forward.y=0;forward.Normalize();
            Move(forward*speed*dt,dt);
        }

        public void Move(Vector3 step,float dt){
            var pos=transform.position;
            float distance=step.magnitude;
            if(distance>1e-4f){
                // Blocked by buildings, trees, lamps and other vehicles (not by the ground it is driving on).
                var dir=step/distance;var center=pos+Vector3.up*1.0f;var half=new Vector3(width*.45f,.45f,.3f);
                int n=Physics.BoxCastNonAlloc(center+dir*(length*.5f-.3f),half,dir,hits,Quaternion.LookRotation(dir),distance+.15f,~0,QueryTriggerInteraction.Ignore);
                float allowed=distance;Collider blocker=null;
                for(int i=0;i<n;i++){
                    var c=hits[i].collider;if(c.transform.IsChildOf(transform)||IsGround(c))continue;
                    if(hits[i].distance<=0&&hits[i].point==Vector3.zero)continue; // starting overlap: let the car back out
                    if(hits[i].distance<allowed){allowed=hits[i].distance;blocker=c;}
                }
                if(blocker!=null){
                    pos+=dir*Mathf.Max(0,allowed-.05f);
                    float impact=Mathf.Abs(speed);speed=-speed*.25f;
                    if(impact>4f&&Time.time-lastHit>.6f){lastHit=Time.time;Sfx.PlayAt("deny",pos,Mathf.Clamp01(impact/25f));var other=blocker.GetComponentInParent<ChangwonCar>();if(other!=null&&other.npc)other.Bump(dir*impact*.3f);}
                }else pos+=step;
            }
            Vector3 normal;float ground=Ground(pos,pos.y+2.6f,transform,out normal);
            // Falls (off a ledge) are smoothed; climbing a kerb or ramp is immediate.
            pos.y=ground>pos.y-rideHeight?ground+rideHeight:Mathf.MoveTowards(pos.y,ground+rideHeight,Mathf.Max(6f,Mathf.Abs(speed))*dt*1.5f);
            transform.position=pos;
            // Pitch and roll follow the ground under the wheels.
            Vector3 frontN,backN;float front=Ground(pos+forward*wheelBase*.5f,pos.y+2.6f,transform,out frontN),back=Ground(pos-forward*wheelBase*.5f,pos.y+2.6f,transform,out backN);
            var along=new Vector3(forward.x,(front-back)/Mathf.Max(.5f,wheelBase),forward.z);
            var up=Vector3.Slerp(Vector3.up,normal,.5f);
            var look=Quaternion.LookRotation(along.normalized,up);
            transform.rotation=Quaternion.Slerp(transform.rotation,look*modelRotation,1f-Mathf.Exp(-12f*dt));
            if(engine!=null)engine.pitch=.7f+Mathf.Abs(speed)/maxSpeed*1.4f;
        }
        public void Bump(Vector3 push){if(npc)speed=0;transform.position+=push*.05f;}

        public static float Kmh(float metresPerSecond){return Mathf.Abs(metresPerSecond)*3.6f;}
    }
}
