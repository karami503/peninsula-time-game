using UnityEngine;

namespace PeninsulaTime
{
    // Public transport in the 3D districts: automatic station gates, the bus stop BIS, and
    // flights from Gimpo (check-in, security, gate). Trains and buses are boarded by walking in (GameController.Ride).
    public partial class GameController
    {
        const float FadeSeconds=1.2f;
        // Compatibility accessor for old saves/tools only; no gameplay flow reads or changes this value.
        public int cardBalance{get{return state.transitCard;}set{state.transitCard=value;}}
        bool farePaid,securityPassed;
        int boardingGate;string boardingFlight="",boardingDestination="",boardingCity="",boardingAirport="";
        PlaneFlight flight;string flightCity="",flightName="",flightAirport="";bool flightLandingAnnounced;
        const float FlightLandingCall=9f; // seconds before the arrival fade that the cabin announces the descent
        float rideTimer,fade,flightBoardedAt;

        // Watching a flight take off: the only ride shown from outside the vehicle.
        public bool TransitRideActive(){return flight!=null;}
        public int BoardingGate{get{return boardingGate;}}

        void Teleport(Vector3 position,Vector3 facing)
        {
            cabin=null;eye.position=position;facing.y=0;verticalSpeed=0;grounded=true;
            if(facing.sqrMagnitude>.01f)SetLook(Quaternion.LookRotation(facing).eulerAngles.y,0);
        }

        // Readers sit between lanes, so open the lane the walker is lined up with, whichever reader was clicked.
        Barrier LaneInLine(Barrier clicked)
        {
            if(clicked.transform.parent==null)return clicked;
            var best=clicked;float nearest=clicked.kind=="fare"?.6f:1.5f,x=eye.position.x; // half a lane
            foreach(var lane in clicked.transform.parent.GetComponentsInChildren<Barrier>())
            {
                float d=Mathf.Abs(lane.transform.position.x-x);
                if(lane.kind==clicked.kind&&d<nearest){nearest=d;best=lane;}
            }
            return best;
        }
        public void TapBarrier(Barrier barrier)
        {
            barrier=LaneInLine(barrier);
            if(barrier.kind=="fare")
            {
                // The unpaid concourse lies on the -Z side of the gate line.
                bool entering=eye.position.z<barrier.transform.position.z;
                if(entering&&farePaid){Beep.Play("tap");barrier.Open();Toast("이미 승차 처리되었습니다 · 들어가세요");return;}
                if(entering)
                {
                    farePaid=true;
                    Toast("승강장으로 들어가세요 · 열린 문으로 바로 탑승할 수 있습니다");
                }
                else{Toast("하차되었습니다 · 출구 안내를 따라 이동하세요");farePaid=false;}
                Beep.Play("tap");barrier.Open();return;
            }
            if(boardingGate==0){Beep.Play("deny");Toast("탑승권이 필요합니다. 2층 체크인 카운터나 키오스크에서 발급받으세요.");return;}
            Beep.Play("tap");barrier.Open();securityPassed=true;
            Toast("신분증·탑승권 확인 · "+boardingFlight+" "+boardingDestination+"행 · 3층 "+boardingGate+"번 탑승구로 가세요");
        }

        public void UseFixture(Fixture fixture)
        {
            switch(fixture.kind)
            {
                case "card":Beep.Play("tap");Toast("승강장에서 열린 열차 문으로 바로 탑승하세요. 바닥 안내선을 따라 환승할 수 있습니다.");break; // old scene compatibility
                case "checkin":flightDeskAirline=fixture.number;kioskDesk=false;Beep.Play("tap");break;
                case "kiosk":kioskDesk=true;flightDeskAirline=-1;Beep.Play("tap");break;
                case "bis":selectedStation=TransitNetwork.Station(fixture.detail)??world.LocalStation(fixture.detail);streetBoard=selectedStation!=null;if(streetBoard)Beep.Play("tap");break;
                case "gate":BoardAtGate(fixture.number);break;
                default:if(!UseShopFixture(fixture))Toast(fixture.detail);break;
            }
        }

        bool UseShopFixture(Fixture fixture)
        {
            if(fixture.kind=="leisure")
            {
                var economy=Economy(state.selectedCity);economy.happiness=Mathf.Min(100,economy.happiness+2);
                Save();Sfx.Play("bell",.5f);Toast("오락기 이용 · 도시 행복도 +2");return true;
            }
            if(fixture.kind!="shop")return false;
            if(fixture.hint.Contains("문의")){Toast(fixture.detail);return true;}
            string item=string.IsNullOrEmpty(fixture.detail)||fixture.detail.EndsWith(".")?fixture.hint.Replace(" 들르기","").Replace(" 구매",""):fixture.detail;
            state.purchases.Add(item);
            Save();Beep.Play("tap");
            Toast(item+" 구매 완료");
            return true;
        }

        void BoardAtGate(int gate)
        {
            if(boardingGate==0){Beep.Play("deny");Toast("탑승권이 없습니다. 2층에서 체크인하세요.");return;}
            if(!securityPassed){Beep.Play("deny");Toast("3층 보안검색을 먼저 통과하세요.");return;}
            if(gate!=boardingGate){Beep.Play("deny");Toast("이 탑승구가 아닙니다. 탑승권의 탑승구는 "+boardingGate+"번입니다.");return;}
            var plane=world.GateAircraft(gate);
            if(plane==null){ArriveAirport(boardingAirport,boardingDestination+"에 도착했습니다.");return;}
            flight=plane.AddComponent<PlaneFlight>();
            world.ConfigureAirportFlight(flight,gate);
            flight.baseRotation=Quaternion.Inverse(Quaternion.LookRotation(world.GateFacing(gate)))*plane.transform.rotation;
            // The whole trip, boarding to arrival, takes TicketTripSeconds: the animation runs fast enough to finish before the fade.
            flight.clock=FlightStart(flight.Duration,FadeSeconds);flight.Step(0);
            flight.speed=FlightPace(flight.Duration,FadeSeconds);flightBoardedAt=Time.unscaledTime;
            flightCity=boardingCity;flightAirport=boardingAirport;flightName=boardingFlight+" "+boardingDestination;boardingGate=0;securityPassed=false;rideTimer=0;
            flightLandingAnnounced=false;Announcer.Say("chime",Announcer.FlightDeparture(boardingFlight,boardingDestination));
            Toast("탑승 완료 · "+flightName+"행이 출발합니다");
        }

        void UpdateTransitRide()
        {
            rideTimer+=Time.deltaTime;
            var t=viewCamera.transform;
            if(flight==null)return;
            var p=flight.transform.position;var forward=flight.transform.rotation*Quaternion.Inverse(flight.baseRotation)*Vector3.forward;
            // The first frame cuts to the runway (the taxi is skipped); after that the chase camera eases.
            t.position=Vector3.Lerp(t.position,p-forward*60f+Vector3.up*16f,rideTimer<=Time.deltaTime?1f:1f-Mathf.Exp(-2.5f*Time.deltaTime));t.LookAt(p+Vector3.up*4f);
            if(!flightLandingAnnounced&&flight.clock>=flight.Duration-FlightLandingCall*flight.speed)
            {flightLandingAnnounced=true;Announcer.Say("chime",Announcer.FlightLanding(KoreanAirports.ByCode(flightAirport).name));}
            if(flight.Done)
            {
                fade=Mathf.MoveTowards(fade,1,Time.deltaTime/FadeSeconds);
                if(fade>=1)
                {
                    var name=flightName;flight=null;
                    Debug.Log("Ticket arrived kind=flight flight="+name+" airport="+flightAirport+" after="+(Time.unscaledTime-flightBoardedAt).ToString("F1")+"s");
                    ArriveAirport(flightAirport,name+" 항공편이 도착했습니다.");
                }
            }
        }

        // Lands in the destination airport's arrivals hall: Gimpo's surveyed terminal, or the generated terminal of any other airport.
        void ArriveAirport(string iata,string message)
        {
            var a=KoreanAirports.ByCode(iata);
            flight=null;ClearRides();fade=1;
            if(a==null){ArriveCity(flightCity,message);return;}
            if(a.iata==KoreanAirports.Gimpo){EnterDistrict(3,false);Teleport(world.AirportArrivalSpawn,Vector3.forward);Toast(message+" · 김포국제공항 국내선 1층 도착");return;}
            mode="interior";cityStreet=false;riding=false;undergroundWalk=false;tab="3D";world.dayNight=true;
            world.BuildDestinationAirport(a);Teleport(world.DestinationArrivalSpawn,world.DestinationArrivalFacing);
            state.selectedCity=a.City;
            Debug.Log("Airport arrival "+a.iata+" "+a.name+" gates="+a.Gates);
            Toast(message+" · "+a.name+" 도착 홀 · 키오스크에서 다음 공항을 고르거나 출구로 나가세요");
        }
        void ArriveCity(string cityId,string message)
        {
            flight=null;
            ReturnMap();
            var city=GameContent.City(cityId);
            if(city!=null&&city.id==cityId)SelectCity(city);
            fade=1;Toast(message);
        }

        void DrawFade(float w,float h)
        {
            if(!TransitRideActive()&&!holdFade)fade=Mathf.MoveTowards(fade,0,Time.deltaTime/FadeSeconds);
            if(fade<=0)return;
            var previous=GUI.color;GUI.color=new Color(0,0,0,fade);
            UiTexture(new Rect(0,0,w,h),Texture2D.whiteTexture);GUI.color=previous;
        }

        // Shortcuts inside a station or the terminal; each one respects the fare gate and security rules.
        void DrawIndoorShortcuts()
        {
            var here=eye.position;
            if(here.y<-2f)
            {
                Label("계단과 연결 통로를 따라 이동하세요. 개찰구는 가까이 걸어가면 자동으로 열립니다.",smallStyle);
                for(int i=0;i<world.PlatformSides.Count;i++)
                {
                    var side=world.PlatformSides[i];if(i>0&&side.line==world.PlatformSides[i-1].line)continue;
                    Label(side.line+(state.district==2&&side.island==1?" · 아래층 승강장":" · 승강장"),bodyStyle);
                }
                if(here.y<WorldBuilder.PlatformY+4f&&world.StationTrains.Count>0)
                {
                    var train=world.StationTrains[world.NearestSide(here)];
                    if(!train.Boardable&&Button("다음 열차까지 기다리기 ("+ScreenDoor.Wait(train.SecondsToBoarding)+")")){train.ArriveNow();Toast(train.line.line+" "+train.line.toward+" 열차가 들어왔습니다");}
                }
                Label("노란 출구 안내판을 따라 걸어서 지상으로 나갈 수 있습니다.",smallStyle);
            }
            else if(here.x>WorldBuilder.AirportOrigin.x-200f&&here.x<WorldBuilder.RideOrigin.x-200f)
            {
                var o=WorldBuilder.AirportOrigin;
                if(Button("1층 도착장 · 지하철 연결통로"))Teleport(world.AirportArrivalSpawn,Vector3.forward);
                if(Button("2층 출발 · 체크인 카운터"))Teleport(o+new Vector3(-23.5f,WorldBuilder.Floor2+1.65f,-18),Vector3.left); // facing a free desk on island A's desks
                if(boardingGate>0&&!securityPassed&&Button("보안검색대 앞으로"))Teleport(world.AirportSecuritySpawn,Vector3.forward); // lane 2, the sign over the lanes in view
                if(securityPassed&&Button("3층 탑승구"+(boardingGate>0?" "+boardingGate+"번":"")))Teleport(world.GateSpawn(boardingGate>0?boardingGate:1),Vector3.forward);
            }
        }

        string TransitStatus()
        {
            string text="상점에서 바로 구매 · 열린 차량 문으로 바로 탑승";
            if(boardingGate>0)text+="  ·  탑승권 "+boardingFlight+" "+boardingDestination+" "+boardingGate+"번 탑승구";
            return text;
        }
    }
}
