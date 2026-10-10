using UnityEngine;
namespace PeninsulaTime
{
    // Rail tickets: at a station, buy a ticket to any other stop on the same line, then travel TicketTripSeconds to it.
    // Buses and BRT run on the timetable without tickets; flights keep their boarding pass.
    // The fare comes from the Seoul budget, the same budget the 3D HUD and missions use. Leaving the station cancels the trip and refunds the fare.
    public partial class GameController
    {
        const float TicketTripSeconds=30f;
        const int FlightFare=12;
        sealed class TicketTrip
        {
            public NetStation station;
            public NetLine line;
            public int direction,fare;
            public float elapsed,startedAt;
        }
        TicketTrip ticketTrip;

        // Pure: buses and BRT have no tickets; every other mode (metro, KTX, 무궁화호, flights) does.
        public static bool TicketsSold(string lineKind){return lineKind!="bus"&&lineKind!="brt";}
        // Pure: fare by line kind; the fast KTX costs more than the metro.
        public static int TicketFare(string lineKind){return lineKind=="flight"?FlightFare:lineKind=="ktx"?8:lineKind=="mugunghwa"?6:3;}
        // Pure: the trip is done once TicketTripSeconds have passed.
        public static bool TripDone(float elapsed){return elapsed>=TicketTripSeconds;}
        // Pure: where a ticketed flight's animation starts. A long airport route skips its taxi so the take-off and climb
        // play at their own pace and still end, with the arrival fade, TicketTripSeconds after boarding.
        public static float FlightStart(float animationSeconds,float fadeSeconds){return Mathf.Max(0,animationSeconds-(TicketTripSeconds-fadeSeconds));}
        // Pure: animation pace from FlightStart to the end; never faster than real time.
        public static float FlightPace(float animationSeconds,float fadeSeconds){return (animationSeconds-FlightStart(animationSeconds,fadeSeconds))/(TicketTripSeconds-fadeSeconds);}

        // Shown in the station panel: every other stop on the line, each with its fare.
        void DrawTicketPurchase()
        {
            var line=stationJourney.line;
            if(!TicketsSold(line.kind))return;
            Label("표 구매 · 종착지 선택 · "+TicketTripSeconds+"초 이동",headingStyle);
            if(ticketTrip!=null){Label("표: "+ticketTrip.station.name+"역 · 열린 문으로 열차에 타면 바로 갑니다",bodyStyle);return;}
            int fare=TicketFare(line.kind);
            for(int i=0;i<stationJourney.stops.Count;i++)
            {
                if(i==stationJourney.index)continue;
                var stop=stationJourney.stops[i];
                if(Button(stop.name+"역 · 요금 "+fare))BuyTicketTo(stop,stationJourney.direction);
            }
        }
        void BuyTicketTo(NetStation destination,int direction)
        {
            var line=stationJourney.line;
            if(ticketTrip!=null){Toast("이동 중입니다. 도착한 뒤 표를 살 수 있습니다.");return;}
            if(!TicketsSold(line.kind)){Toast("버스·BRT는 표 없이 정류장 시간표로 탑니다.");return;}
            int fare=TicketFare(line.kind);var economy=Economy("seoul");
            if(economy.budget<fare){Toast("표 요금 "+fare+"이 도시 예산보다 큽니다.");return;}
            economy.budget-=fare;
            ticketTrip=new TicketTrip{station=destination,line=line,direction=direction,fare=fare,elapsed=0,startedAt=Time.unscaledTime};
            Debug.Log("Ticket bought kind="+line.kind+" line="+line.name+" from="+stationJourney.Current.name+" to="+destination.name+" fare="+fare);
            Beep.Play("tap");
            Toast(line.name+" 표 구매 · 이 노선 열차를 타면 "+destination.name+"역까지 바로 갑니다");
        }
        void UpdateTicketTrip(float dt)
        {
            if(ticketTrip==null)return;
            ticketTrip.elapsed+=dt;
        }
        // Leaving the station before arrival cancels the trip and refunds the fare.
        void CancelTicketTrip()
        {
            if(ticketTrip==null)return;
            Economy("seoul").budget+=ticketTrip.fare;
            Toast("이동을 취소하고 표 요금을 환불했습니다.");
            ticketTrip=null;
        }
        // Flights: the check-in counter opens a destination list for that airline. A ticket costs FlightFare from the Seoul budget;
        // security and the gate follow as before, and the flight arrives TicketTripSeconds after boarding.
        int flightDeskAirline=-1;bool kioskDesk;
        public int FlightDeskAirline{get{return flightDeskAirline;}}
        public bool KioskDeskOpen{get{return kioskDesk;}}
        // The airport the player stands in: a generated destination terminal, or Gimpo's district.
        string CurrentAirport(){return world.DestinationAirport!=null?world.DestinationAirport.iata:mode=="district"&&state.district==3?KoreanAirports.Gimpo:"";}
        // Self check-in kiosks sell a seat to every other Korean airport.
        System.Collections.Generic.List<KoreanAirport> KioskDestinations()
        {
            var list=new System.Collections.Generic.List<KoreanAirport>();string here=CurrentAirport();
            foreach(var a in KoreanAirports.All)if(a.iata!=here)list.Add(a);
            return list;
        }
        // Pure: the gate a kiosk ticket boards from. At Gimpo a route the airline counters also fly keeps that route's gate.
        public static int KioskGate(string from,KoreanAirport to,int gates)
        {
            if(from==KoreanAirports.Gimpo)for(int i=0;i<WorldBuilder.Flights.GetLength(0);i++)if(WorldBuilder.Flights[i,2]==to.cityId&&to.cityId.Length>0)return i+1;
            int hash=0;foreach(char c in from+to.iata)hash=unchecked(hash*31+c);
            return (hash&0x7fffffff)%Mathf.Max(1,gates)+1;
        }
        System.Collections.Generic.List<int> AirlineFlights(int airline)
        {
            var own=new System.Collections.Generic.List<int>();
            string code=WorldBuilder.AirlineCodes[airline%WorldBuilder.AirlineCodes.Length];
            for(int i=0;i<WorldBuilder.Flights.GetLength(0);i++)if(WorldBuilder.Flights[i,0].StartsWith(code))own.Add(i);
            return own;
        }
        // Same frame as the street menu (GTA V interaction menu proportions): banner, subtitle row, one row per destination.
        void DrawFlightDesk(float w,float h)
        {
            EnsureNetworkStyles();EnsureHudStyles();
            var own=kioskDesk?new System.Collections.Generic.List<int>():AirlineFlights(flightDeskAirline);
            var airports=kioskDesk?KioskDestinations():new System.Collections.Generic.List<KoreanAirport>();
            int rows=own.Count+airports.Count;
            float x=streetMenu?MenuLeft()+MenuWidth+16f:MenuLeft();
            float height=MenuBannerHeight+MenuSubtitleHeight+(rows+1)*MenuRowHeight;
            boardRect=new Rect(x,MenuTop,MenuWidth,height);
            UiTexture(boardRect,inkTexture);
            UiTexture(new Rect(x,MenuTop,MenuWidth,MenuBannerHeight),goldTexture);
            UiLabel(new Rect(x+20,MenuTop,MenuWidth-40,MenuBannerHeight),kioskDesk?"키오스크":"항공권",menuBannerStyle);
            UiLabel(new Rect(x+16,MenuTop+MenuBannerHeight,MenuWidth-32,MenuSubtitleHeight),(kioskDesk?"목적지 공항 선택":"종착지 선택")+" · "+TicketTripSeconds+"초 도착 · 요금 "+FlightFare,menuSubtitleStyle);
            GUILayout.BeginArea(new Rect(x,MenuTop+MenuBannerHeight+MenuSubtitleHeight,MenuWidth,(rows+1)*MenuRowHeight));
            int chosen=-1;KoreanAirport airport=null;
            foreach(var i in own)if(DumpButton(WorldBuilder.Flights[i,1]+" · "+WorldBuilder.Flights[i,0],menuRowStyle,GUILayout.Height(MenuRowHeight)))chosen=i;
            foreach(var a in airports)if(DumpButton(a.name+" · "+a.iata,menuRowStyle,GUILayout.Height(MenuRowHeight)))airport=a;
            bool close=DumpButton("닫기",menuRowStyle,GUILayout.Height(MenuRowHeight));
            GUILayout.EndArea();
            if(chosen>=0)BuyFlightTicket(chosen);
            if(airport!=null)BuyAirportTicket(airport);
            if(close||chosen>=0||airport!=null){flightDeskAirline=-1;kioskDesk=false;boardRect=new Rect(0,0,0,0);}
        }
        public void BuyFlightTicket(int pick)
        {
            var economy=Economy("seoul");
            if(economy.budget<FlightFare){Toast("항공권 요금 "+FlightFare+"이 도시 예산보다 큽니다.");return;}
            economy.budget-=FlightFare;
            boardingGate=pick+1;boardingFlight=WorldBuilder.Flights[pick,0];boardingDestination=WorldBuilder.Flights[pick,1];boardingCity=WorldBuilder.Flights[pick,2];
            boardingAirport=KoreanAirports.ForCity(boardingCity).iata;
            Debug.Log("Ticket bought kind=flight flight="+boardingFlight+" to="+boardingDestination+" fare="+FlightFare);
            Beep.Play("tap");Announcer.Say("bell",Announcer.FlightBoarding(boardingFlight,boardingDestination,boardingGate));
            Toast("항공권 구매 · "+boardingFlight+" "+boardingDestination+"행 · "+boardingGate+"번 탑승구 · 3층 보안검색 후 탑승");
        }
        // A kiosk seat to any other airport. Regional terminals screen passengers at the gate, so only Gimpo still needs security.
        public void BuyAirportTicket(KoreanAirport to)
        {
            var economy=Economy("seoul");
            if(economy.budget<FlightFare){Toast("항공권 요금 "+FlightFare+"이 도시 예산보다 큽니다.");return;}
            string from=CurrentAirport();
            if(from.Length==0||to==null||to.iata==from){Toast("이 공항에서는 그 목적지 항공권을 살 수 없습니다.");return;}
            economy.budget-=FlightFare;
            boardingAirport=to.iata;boardingFlight=KoreanAirports.FlightNumber(from,to.iata);boardingDestination=to.name;boardingCity=to.City;
            boardingGate=KioskGate(from,to,world.DestinationAirport!=null?world.DestinationAirport.Gates:8);
            securityPassed=world.DestinationAirport!=null;
            Debug.Log("Ticket bought kind=flight flight="+boardingFlight+" from="+from+" to="+to.iata+" gate="+boardingGate+" fare="+FlightFare);
            Beep.Play("tap");Announcer.Say("bell",Announcer.FlightBoarding(boardingFlight,to.name,boardingGate));
            Toast("항공권 구매 · "+boardingFlight+" "+to.name+"행 · "+boardingGate+"번 탑승구"+(securityPassed?"에서 탑승":" · 3층 보안검색 후 탑승"));
        }

        // Progress strip across the top of the street view while a ticketed trip runs.
        void DrawTicketTrip(float w)
        {
            if(ticketTrip==null)return;
            // Under the objective box when a mission runs, otherwise in its place (top-left safe zone).
            float sx=ObjectiveLeft(),top=hudHeight*SafeZone+(mission!=null?40f:0f);
            UiTexture(new Rect(sx,top,440,34),softTexture);
            UiLabel(new Rect(sx+10,top+6,420,24),ticketTrip.line.name+" 이동 중 · "+ticketTrip.station.name+"역 · 남은 "+Mathf.CeilToInt(TicketTripSeconds-ticketTrip.elapsed)+"초",smallStyle);
        }
    }
}
