using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Batch check for the national transit network: lines and stops from the data, timetables and arrival boards,
    // and the player's add/remove edits.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.NetworkCheck.Run
    public static class NetworkCheck
    {
        static int failed;
        static void Expect(bool condition,string message){if(!condition){failed++;Debug.LogError("NetworkCheck failed: "+message);}}
        public static void Run()
        {
            failed=0;
            TransitNetwork.Build(null);
            int ktx=0,mugunghwa=0,bus=0;
            foreach(var line in TransitNetwork.Lines)
            {
                if(line.kind=="ktx"){ktx++;Expect(line.stops.Count>=5,line.name+" has "+line.stops.Count+" stops");}
                if(line.kind=="mugunghwa")mugunghwa++;
                if(line.kind=="bus")bus++;
                Expect(line.offsets.Length==line.stops.Count&&(line.offsets.Length<2||line.offsets[line.offsets.Length-1]>0),line.name+" has running times");
            }
            Expect(ktx>=6&&mugunghwa>=8,"KTX "+ktx+" and 무궁화호 "+mugunghwa+" lines kept apart");
            Expect(bus>=20,"Seoul bus routes ("+bus+")");
            Expect(TransitNetwork.Corridors.Count>=10,"median bus lanes ("+TransitNetwork.Corridors.Count+")");
            var timetableStop=new NetStation{id="check-bus-20",name="검사용 정류장",lon=127f,lat=37.5f};
            for(int i=0;i<20;i++)timetableStop.lines.Add(new NetLine{id="check-"+i,kind="bus",shortName="노선"+i});
            var busesNow=BusStops.Next(timetableStop,0,20);
            var busesLater=BusStops.Next(timetableStop,400,20);
            Expect(busesNow.Count==20&&busesLater.Count==20,"twenty bus routes are scheduled");
            for(int i=1;i<busesNow.Count;i++)Expect(System.Math.Abs(busesNow[i].at-busesNow[i-1].at-15)<.01,"first arrivals stagger fifteen seconds");
            var repeated=BusStops.Next(timetableStop,450,20);
            foreach(var prior in busesLater){var later=repeated.Find(x=>x.route==prior.route);Expect(System.Math.Abs(later.at-prior.at-50)<.01,"same bus route repeats at fifty seconds");}
            foreach(var line in TransitNetwork.Lines){var times=TransitSchedule.Departures(line,0);for(int i=1;i<times.Length;i++)Expect(System.Math.Abs(times[i]-times[i-1]-(line.kind=="bus"||line.kind=="brt"?50:30))<.01,"fixed route interval "+line.id);}

            // 2호선 is a loop; at 강남 the 교대 side is 내선순환 (clockwise) and the 역삼 side 외선순환.
            var line2=TransitNetwork.Lines.Find(l=>l.shortName=="2호선"&&l.loop&&l.region=="수도권");
            Expect(line2!=null,"2호선 loop");
            var gangnam=TransitNetwork.Named("강남").Find(s=>line2!=null&&s.lines.Contains(line2));
            Expect(gangnam!=null,"강남 on 2호선");
            if(line2!=null&&gangnam!=null)
            {
                int index=line2.IndexOf(gangnam);
                for(int direction=0;direction<2;direction++)
                {
                    var next=TransitSchedule.NextStop(line2,direction,index);string toward=TransitSchedule.Toward(line2,direction);
                    if(next.name=="교대")Expect(toward=="내선순환","강남→교대 is 내선순환 ("+toward+")");
                    if(next.name=="역삼")Expect(toward=="외선순환","강남→역삼 is 외선순환 ("+toward+")");
                    // Midday: thirty-second headways, counting down.
                    double noon=12*3600;var arrivals=TransitSchedule.Next(line2,direction,index,2,noon);
                    Expect(arrivals.Count==2&&arrivals[0].seconds>=-30&&arrivals[0].seconds<=line2.offPeakMinutes*60+60,"강남 2호선 arrivals at noon");
                    Expect(arrivals.Count==2&&arrivals[1].seconds>arrivals[0].seconds,"arrivals in order");
                    if(arrivals.Count>0)Debug.Log("NetworkCheck: 강남 "+toward+" "+TransitSchedule.Describe(arrivals[0]));
                }
            }
            // Seoul Station's departure screen lists KTX and 무궁화호 by time.
            var seoul=TransitSchedule.RailStation("서울");
            Expect(seoul!=null,"서울역 has trains");
            var trains=TransitSchedule.TrainsFrom(seoul,9*3600);
            Expect(trains.Exists(a=>a.line.kind=="ktx")&&trains.Exists(a=>a.line.kind=="mugunghwa"),"서울역 departures have KTX and 무궁화호");
            for(int i=1;i<trains.Count;i++)Expect(trains[i].seconds>=trains[i-1].seconds,"departures sorted");
            Debug.Log("NetworkCheck: "+TransitSchedule.DepartureText(seoul,3).Replace("\n"," / "));
            // Subway platforms under the districts follow the network (홍대입구: 2호선 and 공항철도).
            var hongdae=WorldBuilder.PlatformLines(2);
            Expect(hongdae.Count==4&&hongdae.TrueForAll(s=>s.net!=null),"홍대입구 platforms from the network ("+hongdae.Count+")");
            var seoulPlatforms=WorldBuilder.PlatformLines(1).FindAll(p=>p.line=="공항철도");
            Expect(seoulPlatforms.Count==2&&seoulPlatforms[0].parity==0&&seoulPlatforms[1].parity==1,"서울역 공항철도 is a terminus with alternate trains");
            Expect(WorldBuilder.PlatformLines(1).FindAll(p=>(p.line=="1호선"||p.line=="4호선")&&p.net!=null).Count==4,"서울역 1·4호선 platforms from the network");
            foreach(var side in hongdae)foreach(int d in side.ahead)Expect(WorldBuilder.RideSeconds(side,d)>30,"ride from 홍대입구 "+side.toward+" to "+WorldBuilder.StationNames[d]+" takes time");

            // Customisation: add a station and a line through it, remove a station and a line, and rebuild.
            var edits=new NetworkEdits();
            edits.stations.Add(new CustomStation{id="u1",name="반도시청",lon=127.0f,lat=37.5f});
            edits.lines.Add(new CustomLine{id="ul1",kind="metro",name="반도선",color="#FF8800",stops={gangnam!=null?gangnam.id:"",  "u1"}});
            var removedLine=TransitNetwork.Lines.Find(l=>l.kind=="mugunghwa");
            edits.removedLines.Add(removedLine.id);
            var removedStation=line2!=null?line2.stops[3]:null;
            if(removedStation!=null)edits.removedStations.Add(removedStation.id);
            int stopsBefore=line2!=null?line2.stops.Count:0;
            TransitNetwork.Build(edits);
            var custom=TransitNetwork.Line("ul1");
            Expect(custom!=null&&custom.stops.Count==2&&custom.custom,"custom line built");
            Expect(TransitNetwork.Station("u1")!=null&&TransitNetwork.Station("u1").lines.Contains(custom),"custom station served");
            Expect(TransitNetwork.Line(removedLine.id)==null,"line removed");
            if(removedStation!=null)
            {
                Expect(TransitNetwork.Station(removedStation.id)==null,"station removed");
                var again=TransitNetwork.Line(line2.id);
                Expect(again!=null&&again.stops.Count==stopsBefore-1,"removed station leaves the line");
            }
            TransitNetwork.Build(null);
            Debug.Log("NetworkCheck: "+(failed==0?"passed":failed+" failed"));
            EditorApplication.Exit(failed==0?0:1);
        }
    }
}
