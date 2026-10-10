using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // One side of an island platform: the line, where its trains go and which district that is.
    // net/direction/index tie it to the national network's timetable; ahead lists the districts the train
    // reaches in order (the rider may get off at any of them); destination is the default, -1 if none.
    public struct StationLine
    {
        public string line,toward;public Color color;public int destination;
        // parity: at a terminus both sides serve the one direction and take alternate trips (0/1); -1 elsewhere.
        public NetLine net;public int direction,index,island,parity;public int[] ahead;
        public StationLine(string line,string toward,Color color,int destination)
        {this.line=line;this.toward=toward;this.color=color;this.destination=destination;net=null;direction=index=island=0;parity=-1;ahead=destination>=0?new[]{destination}:new int[0];}
    }

    // A lit sign whose text is refreshed every second (arrival screens, bus information terminals).
    public class TransitBoard : MonoBehaviour
    {
        public TextMesh text;public System.Func<string> compose;float next;
        void Update(){if(Time.time<next||compose==null||text==null)return;next=Time.time+1f;text.text=compose();}
        public void Refresh(){if(compose!=null&&text!=null)text.text=compose();}
    }

    // Fare gate lane or airport security lane: a blocker that opens for a few seconds after a tap.
    public class Barrier : Interactable
    {
        public const float OpenSeconds=4f;
        public string kind="fare";          // "fare" (transit card) or "security" (boarding pass)
        public Collider blocker;
        public Transform[] flaps=new Transform[0];
        Quaternion[] closed;float openFor,amount;
        public bool IsOpen{get{return openFor>0;}}
        public override string Hint{get{return kind=="fare"?"개찰구 통과":"탑승권·신분증 확인";}}
        void Remember(){if(closed!=null&&closed.Length==flaps.Length)return;closed=new Quaternion[flaps.Length];for(int i=0;i<flaps.Length;i++)closed[i]=flaps[i].localRotation;}
        public void Open(){Remember();openFor=OpenSeconds;if(blocker!=null)blocker.enabled=false;}
        void Update(){Step(Time.deltaTime);}
        public void Step(float seconds)
        {
            Remember();
            if(openFor>0){openFor-=seconds;if(openFor<=0&&blocker!=null)blocker.enabled=true;}
            float nextAmount=Mathf.MoveTowards(amount,openFor>0?1:0,seconds*3f);
            if(nextAmount==amount)return;
            amount=nextAmount;
            for(int i=0;i<flaps.Length;i++)flaps[i].localRotation=closed[i]*Quaternion.Euler(0,(i%2==0?1:-1)*85f*amount,0);
        }
    }

    // Clickable fixtures handled by GameController by kind: card machine, check-in, gate desk, products, shops.
    public class Fixture : Interactable
    {
        public string kind,hint,detail;public int number;
        public override string Hint{get{return hint;}}
    }

    // Card taps, refusals and chimes (kept for the older call sites; see Sfx).
    public static class Beep
    {
        public static void Play(string key){Sfx.Play(key);}
    }

    // The walkable inside of a bus or train, in this transform's space (x across, z along the vehicle, floor at
    // y=floor), with door openings on one side or both. The vehicle sets `open` (0 shut .. 1 open) as its doors
    // move; the player walks in and out through an open door.
    public class Cabin : MonoBehaviour
    {
        public static readonly List<Cabin> All=new List<Cabin>();
        public string kind="bus";             // "bus", "metro" or "ktx"
        public float halfWidth=1.1f,back=-5f,front=5f,floor=.35f;
        public float exitDistance=.8f;        // how far outside the side wall a rider steps off
        public float[] doors=new float[0];    // z centres of the door openings
        public float doorHalf=.55f;
        public int doorSide=1;                // +1: doors on local +x, -1: on local -x, 0: both sides
        public float aisle;                   // >0: away from the doors, seats leave only |x| < aisle free
        public float open;
        public bool Open{get{return open>.8f;}}
        public void Register(){if(!All.Contains(this))All.Add(this);}
        void OnEnable(){if(doors.Length>0)Register();}
        void OnDisable(){All.Remove(this);}
        void OnDestroy(){All.Remove(this);}
        public bool DoorAt(float z,float margin=0){foreach(float d in doors)if(Mathf.Abs(z-d)<=doorHalf+margin)return true;return false;}
        public bool SideOpen(float x){return doorSide==0||(x>0?1:-1)==doorSide;}
        // Keeps a step inside the walls and out of the seats: beside the seats only the aisle is free.
        public Vector3 Clamp(Vector3 local,Vector3 from)
        {
            var p=new Vector3(Mathf.Clamp(local.x,-halfWidth+.3f,halfWidth-.3f),floor,Mathf.Clamp(local.z,back+.3f,front-.3f));
            if(aisle>0&&Mathf.Abs(p.x)>aisle&&!DoorAt(p.z,.25f))
            {
                if(DoorAt(from.z,.25f))p.z=from.z;else p.x=Mathf.Clamp(p.x,-aisle,aisle);
            }
            return p;
        }
    }

    // Sliding door leaves of a bus or KTX and the cabin they open; amount 0 shut .. 1 open.
    public class VehicleDoors : MonoBehaviour
    {
        public Cabin cabin;
        public readonly List<Transform> leaves=new List<Transform>();
        public readonly List<Vector3> closed=new List<Vector3>(),opened=new List<Vector3>();
        public float amount;
        public readonly List<float> leafSides=new List<float>();
        public int side; // 0: every leaf opens; +1/-1: only leaves on that local side (and untagged ones)
        int appliedLeaves=-1;
        public void Add(Transform leaf,Vector3 slide,float leafSide=0){leaves.Add(leaf);closed.Add(leaf.localPosition);opened.Add(leaf.localPosition+slide);leafSides.Add(leafSide);}
        public void UseSide(int open){side=open;if(cabin!=null)cabin.doorSide=open;appliedLeaves=-1;Set(amount);}
        public void Set(float value)
        {
            if(Mathf.Approximately(value,amount)&&appliedLeaves==leaves.Count){if(cabin!=null)cabin.open=amount;return;}
            amount=value;
            appliedLeaves=leaves.Count;
            for(int i=0;i<leaves.Count;i++)if(leaves[i]!=null)leaves[i].localPosition=Vector3.Lerp(closed[i],opened[i],side==0||leafSides[i]==0||leafSides[i]==side?amount:0);
            if(cabin!=null)cabin.open=amount;
        }
        // Moves toward open or shut at a door's pace; returns the new amount.
        public float Move(bool open,float seconds,float pace=.8f){Set(Mathf.MoveTowards(amount,open?1:0,seconds/pace));return amount;}
    }

    // One four-car train of a SubwayTrain: the cars, the door leaves on the platform side and the walkable inside.
    public class TrainConsist : MonoBehaviour
    {
        public SubwayTrain train;public Cabin cabin;public Transform lead;
        public readonly List<Transform> doorLeaves=new List<Transform>();
        public readonly List<Vector3> doorClosed=new List<Vector3>(),doorOpen=new List<Vector3>();
        public float clock=SubwayTrain.Hidden;
        public bool manual,suppressed; // manual: moved by a ride; suppressed: kept out of sight while another train holds the platform
        public float offset,doorAmount;
        // Every door leaf on both sides, with its side (+1/-1) and open slide, so the working side can change.
        public readonly List<Transform> sideLeaves=new List<Transform>();
        public readonly List<float> leafSides=new List<float>();public readonly List<Vector3> leafSlides=new List<Vector3>(),leafRest=new List<Vector3>();
        float appliedDoors=float.NaN;int appliedLeaves=-1;
        // Opens the doors on local side `side` from now on (a train arriving at a platform on its other side).
        public void UseDoorSide(int side)
        {
            for(int i=0;i<doorLeaves.Count;i++)doorLeaves[i].localPosition=doorClosed[i];
            doorLeaves.Clear();doorClosed.Clear();doorOpen.Clear();
            for(int i=0;i<sideLeaves.Count;i++)if((int)leafSides[i]==side){doorLeaves.Add(sideLeaves[i]);doorClosed.Add(leafRest[i]);doorOpen.Add(leafRest[i]+leafSlides[i]);}
            if(cabin!=null)cabin.doorSide=side;
            appliedLeaves=-1;
        }
        // Moves along the parent's local Z by `offset` times `sign` and sets the door leaves (0 shut .. 1 open).
        public void Place(float along,float doors,float sign)
        {
            offset=along;doorAmount=doors;
            var position=new Vector3(0,0,along*sign);
            if(transform.localPosition!=position)transform.localPosition=position;
            if(appliedDoors!=doors||appliedLeaves!=doorLeaves.Count)
            {
                for(int i=0;i<doorLeaves.Count;i++)doorLeaves[i].localPosition=Vector3.Lerp(doorClosed[i],doorOpen[i],doors);
                appliedDoors=doors;appliedLeaves=doorLeaves.Count;
            }
            if(cabin!=null)cabin.open=doors;
        }
    }

    // A subway line's trains at one side of an island platform. Two consists take alternate trips so that one
    // arrives every 30 seconds. Travel is accelerated by the central gameplay multiplier, while the
    // platform screen doors and 12-second stand retain their normal pace. Runs along the station's local Z.
    public class SubwayTrain : Interactable
    {
        public const float Approach=15f/TransitSpeed.RailMultiplier,Dwell=12f,Depart=15f/TransitSpeed.RailMultiplier,
            Brake=1.5f*TransitSpeed.RailMultiplier*TransitSpeed.RailMultiplier,Cycle=60f,Run=300f;
        public const float Shown=Approach+Dwell+Depart,Hidden=Cycle; // clock past Shown: out of sight in the tunnel
        public const float CruiseSpeed=Brake*Approach;                // 90 m/s at the default 4x travel speed
        public static readonly float[] CarCentres={-30f,-10f,10f,30f},DoorOffsets={-7.55f,-2.52f,2.52f,7.55f};
        public StationLine line;
        public float trackX,floorY,direction=1f;
        public bool Reverse{get{return line.parity>=0;}} // a terminus: trains leave back the way they came
        public float TravelSign{get{return direction*(Reverse?-1:1);}}
        public readonly List<Transform> leaves=new List<Transform>();      // platform screen door leaves
        public readonly List<Vector3> leafClosed=new List<Vector3>(),leafOpen=new List<Vector3>();
        public readonly List<TrainConsist> consists=new List<TrainConsist>();
        public float heldDoors; // screen doors held open for a train standing here that is not one of its own consists
        double untilNext=-1;float baseClock;
        List<Arrival> arrivalCache;double arrivalCacheTime;
        NetLine arrivalLine;int arrivalDirection,arrivalIndex,arrivalParity,arrivalVersion=-1;
        float appliedScreenDoors=float.NaN;int appliedScreenLeaves=-1;
        public int ScheduleQueryCount{get;private set;}
        bool Scheduled{get{return line.net!=null&&Application.isPlaying;}}
        public override string Hint{get{return "";}}
        // The consist at the platform, or the one coming in next.
        public TrainConsist Active
        {
            get
            {
                TrainConsist best=null;float score=float.MaxValue;
                foreach(var c in consists)
                {
                    float s=c.clock>=Shown?1e6f:c.clock<Approach?Approach-c.clock:c.clock<Approach+Dwell?0:1000+c.clock;
                    if(s<score){score=s;best=c;}
                }
                return best;
            }
        }
        public Transform leadCar{get{var a=Active;return a!=null?a.lead:null;}}
        public float Clock{get{var a=Active;return a!=null?a.clock:Hidden;}}
        public static bool DoorsOpenAt(float clock){return clock>Approach+1.7f&&clock<Approach+Dwell-2.2f;}
        public bool Boardable{get{foreach(var c in consists)if(!c.manual&&DoorsOpenAt(c.clock))return true;return false;}}
        // Seconds until doors open for the next train (from the timetable when the line has one).
        public float SecondsToBoarding
        {
            get
            {
                if(Boardable)return 0;
                if(untilNext>=0)return (float)untilNext+1.7f;
                float best=float.MaxValue,open=Approach+1.7f;
                foreach(var c in consists)if(!c.manual)best=Mathf.Min(best,c.clock<open?open-c.clock:Cycle-c.clock+open);
                return best==float.MaxValue?0:best;
            }
        }
        public void Begin(float startClock){baseClock=startClock;Step(0);}
        void Update(){if(Scheduled)Follow(TransitSchedule.Now);else Step(Time.deltaTime);}
        // Without a timetable the two consists simply alternate half a cycle apart.
        public void Step(float seconds)
        {
            baseClock=Mathf.Repeat(baseClock+seconds,Cycle);
            for(int k=0;k<consists.Count;k++)
            {
                var c=consists[k];if(c.manual)continue;
                float t=Mathf.Repeat(baseClock+k*Cycle*.5f,Cycle);c.clock=c.suppressed||t>=Shown?Hidden:t;
            }
            Place();
        }
        // Runs to the line's timetable at this station: each consist shows its trip standing here or coming in.
        public void Follow(double now)
        {
            // Keep absolute arrival times for one second. Position and door phase
            // still use the exact current clock on EVERY frame, including skips.
            if(arrivalCache==null||now<arrivalCacheTime||now-arrivalCacheTime>=1d||arrivalLine!=line.net||arrivalDirection!=line.direction||arrivalIndex!=line.index||arrivalParity!=line.parity||arrivalVersion!=TransitNetwork.Version)
            {
                arrivalCache=Arrivals(now);arrivalCacheTime=now;arrivalLine=line.net;arrivalDirection=line.direction;arrivalIndex=line.index;arrivalParity=line.parity;arrivalVersion=TransitNetwork.Version;
                ScheduleQueryCount++;
            }
            untilNext=-1;float shown0=-1,shown1=-1;
            double elapsed=now-arrivalCacheTime;
            foreach(var a in arrivalCache)
            {
                double remaining=a.seconds-elapsed;float since=(float)-remaining;int k=ConsistOf(a.trip);
                if(since>=-Approach&&since<=Dwell+Depart)
                {
                    if(k==0&&shown0<0)shown0=Approach+since;
                    else if(k==1&&shown1<0)shown1=Approach+since;
                }
                if(remaining>0&&untilNext<0)untilNext=remaining;
            }
            for(int k=0;k<consists.Count&&k<2;k++){var c=consists[k];float shown=k==0?shown0:shown1;if(!c.manual)c.clock=shown>=0&&!c.suppressed?shown:Hidden;}
            Place();
        }
        int ConsistOf(int trip){return (line.parity<0?trip:trip/2)%2;}
        // Seconds from `now` until each of the next trains reaches this platform (negative: standing or pulling out).
        List<Arrival> Arrivals(double now)
        {
            var result=new List<Arrival>();const float window=Dwell+Depart;
            foreach(var a in TransitSchedule.Next(line.net,line.direction,line.index,6,now-window))
                if(line.parity<0||a.trip%2==line.parity){var b=a;b.seconds-=window;result.Add(b);}
            return result;
        }
        // The platform's arrival screen: this train and the one after, like Seoul's LED boards.
        public string BoardText()
        {
            string head=line.line+"  "+line.toward;
            if(line.net==null)return head+"\n"+(Boardable?"열차가 도착했습니다":"다음 열차 "+ScreenDoor.Wait(SecondsToBoarding)+" 후");
            var rows=new List<string>();
            foreach(var a in TransitSchedule.Next(line.net,line.direction,line.index,4,TransitSchedule.Now))
            {
                if(line.parity>=0&&a.trip%2!=line.parity)continue;
                rows.Add((rows.Count==0?"이번  ":"다음  ")+TransitSchedule.Describe(a));
                if(rows.Count==2)break;
            }
            if(rows.Count==0)rows.Add("오늘 운행이 끝났습니다");
            return head+"\n"+string.Join("\n",rows);
        }
        // Waiting is skipped by moving the shared timetable clock, so boards and trains stay in step.
        public void ArriveNow()
        {
            if(Scheduled)
            {
                foreach(var a in Arrivals(TransitSchedule.Now))if(a.seconds>-1f){TransitSchedule.Skip+=a.seconds+2f;break;}
                Follow(TransitSchedule.Now);return;
            }
            baseClock=Approach+2f;Step(0);
        }
        // Distance along the track from the stopping point: braking in, standing, accelerating away.
        public float Offset(float clock)
        {
            if(clock<Approach){float u=Approach-clock;return -.5f*Brake*u*u;}
            if(clock<Approach+Dwell)return 0;
            if(clock<Shown){float t=clock-Approach-Dwell;return .5f*Brake*t*t*(Reverse?-1:1);}
            return (Run+60f)*(Reverse?-1:1);
        }
        public static float DoorAmount(float clock)
        {
            if(clock<=Approach||clock>=Approach+Dwell)return 0;
            return Mathf.Clamp01(Mathf.Min(clock-Approach-.5f,Approach+Dwell-.5f-clock)/1.2f);
        }
        void Place()
        {
            float screenDoors=heldDoors;
            foreach(var c in consists)
            {
                bool visible=c.manual||c.clock<Shown;
                if(c.gameObject.activeSelf!=visible)c.gameObject.SetActive(visible);
                if(c.manual){if(c.transform.parent==transform&&Mathf.Abs(c.offset)<1f)screenDoors=Mathf.Max(screenDoors,c.doorAmount);continue;}
                if(!visible)continue;
                c.Place(Offset(c.clock),DoorAmount(c.clock),direction);
                screenDoors=Mathf.Max(screenDoors,c.doorAmount);
            }
            if(appliedScreenDoors!=screenDoors||appliedScreenLeaves!=leaves.Count)
            {
                for(int i=0;i<leaves.Count;i++)leaves[i].localPosition=Vector3.Lerp(leafClosed[i],leafOpen[i],screenDoors);
                appliedScreenDoors=screenDoors;appliedScreenLeaves=leaves.Count;
            }
        }
    }

    // Platform screen door panel: tells when the next train comes, or to walk in while one stands here.
    public class ScreenDoor : Interactable
    {
        public SubwayTrain train;
        public override bool Informational{get{return true;}}
        public override string Hint{get{return train==null?"":train.Boardable?"열차가 서 있습니다 · 열린 문으로 걸어 들어가 타세요":"다음 열차 "+train.line.line+" "+train.line.toward+" · "+Wait(train.SecondsToBoarding)+" 후 도착";}}
        public static string Wait(float seconds){int s=Mathf.CeilToInt(seconds);return s>=60?s/60+"분 "+s%60+"초":s+"초";}
    }

    // Marks a KTX or 무궁화호 set on the Seoul Station tracks: walk in through an open door while it waits at a platform.
    public class KtxTrain : Interactable
    {
        public string destination="busan",destinationName="부산";
        public RailVehicle rail;
        public override bool Informational{get{return true;}}
        // The train this set stands in for: the next one leaving 서울역 by the timetable (KTX or 무궁화호).
        public static bool NextTrip(out Arrival trip,string kind=null)
        {
            trip=default(Arrival);
            foreach(var a in TransitSchedule.TrainsFrom(TransitSchedule.RailStation("서울"),TransitSchedule.Now,kind)){trip=a;return true;}
            return false;
        }
        public override string Hint
        {
            get
            {
                if(rail==null||!rail.Dwelling)return "";
                Arrival trip;if(!NextTrip(out trip))return "열차 · "+destinationName+"행 · 열린 문으로 타세요";
                var end=trip.line.Terminus(trip.direction);
                return TransitSchedule.TrainNumber(trip.line,trip.direction,trip.trip)+" "+TransitNetwork.Bare(end.name)+"행 "+TransitSchedule.Clock(trip.scheduled)+" 출발 · 열린 문으로 타세요";
            }
        }
    }
}
