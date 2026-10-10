using System;
using UnityEngine;
namespace PeninsulaTime {
    public partial class GameController {
        // Explicit QA opt-in, always isolated from the player's normal save directory.
        bool playtest;int playtestPoint;float playtestInterval,playtestNext;
        // QA: the frame after each checkpoint is captured a second time with the HUD hidden (playtest-N-bare.png), so Figma frames can sit on the real map and 3D scene.
        // A checkpoint is captured CaptureDelaySeconds after its state change, so camera smoothing has settled; the bare capture follows one frame later.
        const float CaptureDelaySeconds=1f;
        bool playtestBare;int bareStep,bareFor,capturePoint;float captureAt;
        // QA: a map point is projected only after the camera has settled, so the OS-mouse driver aims at where the point really is.
        string qaMapName;float qaMapLongitude,qaMapLatitude,qaMapAt;
        // --playtest-interval <seconds> walks the checkpoints unattended; F11 still steps by hand.
        void StartPlaytest(){
            var args=Environment.GetCommandLineArgs();
            playtest=Array.IndexOf(args,"--playtest")>=0&&Array.IndexOf(args,"--save-directory")>=0;
            int at=Array.IndexOf(args,"--playtest-interval");
            if(playtest&&at>=0&&at+1<args.Length)float.TryParse(args[at+1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out playtestInterval);
            // QA runs use a small window and 30 fps: a full-size Retina surface plus the district rebuilds overloaded the GPU and froze the Mac.
            // --playtest-start <N> begins at checkpoint N (QA re-runs of one stretch).
            int from=Array.IndexOf(args,"--playtest-start");
            if(playtest&&from>=0&&from+1<args.Length)int.TryParse(args[from+1],out playtestPoint);
            if(playtest){var size=PlaytestWindow(args);Screen.SetResolution(size.x,size.y,false);Application.targetFrameRate=30;NextPlaytestPoint();}
        }
        // --playtest-size <W>x<H>: default 1280x800, the size approved for QA runs.
        static Vector2Int PlaytestWindow(string[] args){
            int at=Array.IndexOf(args,"--playtest-size");
            if(at<0||at+1>=args.Length)return new Vector2Int(1280,800);
            var parts=args[at+1].Split('x');int width,height;
            if(parts.Length==2&&int.TryParse(parts[0],out width)&&int.TryParse(parts[1],out height)&&width>0&&height>0)return new Vector2Int(width,height);
            return new Vector2Int(1280,800);
        }
        void PlaytestTick(){
            if(!playtest)return;
            if(qaMapName!=null&&Time.time>=qaMapAt){QaLogMapTarget(qaMapName,qaMapLongitude,qaMapLatitude);qaMapName=null;}
            if(captureAt>0f&&Time.time>=captureAt){captureAt=0f;Time.timeScale=0f;BeginUiDump(capturePoint);ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(SaveDirectory,"playtest-"+capturePoint+".png"));bareFor=capturePoint;bareStep=1;}
            else if(bareStep==1){playtestBare=true;ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(SaveDirectory,"playtest-"+bareFor+"-bare.png"));bareStep=2;}
            else if(bareStep==2){playtestBare=false;bareStep=0;Time.timeScale=1f;}
            bool due=playtestInterval>0&&Time.time>=playtestNext;
            if(Input.GetKeyDown(KeyCode.F11)||due){playtestNext=Time.time+playtestInterval;NextPlaytestPoint();}
        }
        void NextPlaytestPoint(){

            // 27 boards a car in the world 26 entered; 80 and 82 capture what the real mouse started (the 3D button). None may reset the world.
            // 93-98 stay in the carved 진해 that the real mouse entered at 92.
            bool keepWorld=playtestPoint==27||playtestPoint==80||playtestPoint==82||playtestPoint>=93;
            if(!keepWorld&&(playtestPoint==0||world==null||state.district!=3))EnterDistrict(3,false);
            switch(playtestPoint){
                case 0:Teleport(WorldBuilder.GimpoExits[0]+new Vector3(0,EyeHeight+.02f,6),Vector3.back);break;
                case 1:Teleport(world.ConcourseSpawn,Vector3.forward);break;
                case 2:Vector3 face;Teleport(world.PlatformSpawn(2,out face),face);break;
                case 3:var route=Array.Find(world.root.GetComponentsInChildren<StationWalkRoute>(),r=>r.name.StartsWith("서해선")&&r.points.Length>2);if(route!=null)Teleport(route.points[0]+Vector3.up*EyeHeight,route.points[1]-route.points[0]);break;
                case 4:Teleport(WorldBuilder.AirportOrigin+new Vector3(-49.1f,EyeHeight,-30),Vector3.forward);break;
                case 5:Teleport(WorldBuilder.AirportOrigin+new Vector3(-23.5f,WorldBuilder.Floor2+EyeHeight,-18),Vector3.left);break;
                case 6:Teleport(world.AirportSecuritySpawn,Vector3.forward);break;
                case 7:Teleport(world.GateSpawn(boardingGate>0?boardingGate:1),Vector3.forward);break;
                case 8:Teleport(world.AirportBoardingDoor(boardingGate>0?boardingGate:1)+Vector3.up*EyeHeight,Vector3.left);break;
                case 9:Teleport(world.InternationalArrivalSpawn,Vector3.left);break;
                case 10:Teleport(WorldBuilder.InternationalAirportOrigin+new Vector3(-105,1.65f,-50),Vector3.forward);break;
                case 11:Teleport(world.InternationalCheckInSpawn,Vector3.forward);break;
                case 12:Teleport(world.InternationalDepartureSpawn,Vector3.forward);break;
                case 13:Teleport(WorldBuilder.InternationalAirportOrigin+new Vector3(170,13.65f,44),Vector3.right);break;
                case 14:var gn=TransitNetwork.Named("강남").Find(s=>s.lines.Exists(l=>l.shortName=="2호선"));VisitNetworkStation(gn,gn.lines.Find(l=>l.shortName=="2호선"));break;
                case 15:Teleport(new Vector3(-65,EyeHeight,-81),Vector3.left);break;
                case 16:var sm=TransitNetwork.Named("서면").Find(s=>s.lines.Exists(l=>l.region=="부산"));VisitNetworkStation(sm);Teleport(new Vector3(-65,EyeHeight,-81),Vector3.left);break;
                case 17:EnterDistrict(0,false);Teleport(new Vector3(-19,WorldBuilder.ConcourseY+EyeHeight,-.8f),Vector3.left);break;
                case 18:var seoul=TransitNetwork.Named("서울").Find(s=>s.lines.Exists(l=>l.kind=="ktx"));VisitNetworkStation(seoul,seoul.lines.Find(l=>l.kind=="ktx"));Teleport(world.NetworkTransfers[0].hallPoint+Vector3.up*EyeHeight,Vector3.right);break;
                case 19:EnterDistrict(3,false);Teleport(world.ConcourseSpawn+Vector3.forward*7,Vector3.forward);break;
                case 20:var osu=TransitNetwork.Named("오수").Find(s=>s.lines.Exists(l=>l.kind!="bus"));VisitNetworkStation(osu);Teleport(new Vector3(-55,EyeHeight,65),Vector3.back);break;
                case 21:var ban=TransitNetwork.Named("반성").Find(s=>s.lines.Exists(l=>l.kind!="bus"));VisitNetworkStation(ban);Teleport(new Vector3(-62,EyeHeight,50),Vector3.back);break;
                case 22:EnterDistrict(0,false);Vector3 gangnamFacing;Teleport(world.PlatformSpawn(0,out gangnamFacing),gangnamFacing);break;
                case 23:ReturnMap();break;
                case 24:EnterCityPlot();break;
                // 25–26 and 31 leave the H menu to the real key: QA presses H between captures, so the menu opens and closes by input.
                case 25:EnterDistrict(3,false);Teleport(world.ConcourseSpawn,Vector3.forward);break;
                // 26 and 27 use the Gangnam street: a walking view with the delivery objective, then the same street from a car.
                case 26:EnterDistrict(0,false);StartDeliveryMission();break;
                case 27:BoardVehicle(world.root.GetComponentInChildren<TrafficVehicle>(),false);break;
                case 28:ReturnMap();gameSpeed=3;break;
                case 29:break;
                case 30:break;
                case 31:gameSpeed=0;break;
                case 32:gameSpeed=1;break;
                case 33:break;
                case 34:break;
                case 35:break;
                case 36:gameSpeed=2;break;
                case 37:break;
                case 38:break;
                case 39:break;
                case 40:gameSpeed=0;break;
                // Ticket: at 강남 2호선, buy a ticket three stops ahead and step aboard; the same train runs there by 48.
                case 41:var ticketStation=TransitNetwork.Named("강남").Find(s=>s.lines.Exists(l=>l.shortName=="2호선"));VisitNetworkStation(ticketStation,ticketStation.lines.Find(l=>l.shortName=="2호선"));streetMenu=true;break;
                case 42:var ticketStops=stationJourney.stops;BuyTicketTo(ticketStops[Mathf.Min(ticketStops.Count-1,stationJourney.index+3)],stationJourney.direction);QaBoardStationTrain();break;
                case 43:break;
                case 44:break;
                case 45:break;
                case 46:break;
                case 47:break;
                case 48:break;
                // The map's 3D button: wherever the map was clicked, 3D starts in Seoul Station's concourse.
                case 49:ReturnMap();ClickMapAt(127.0276f,37.4979f);Start3DAtMapClick();break;
                case 50:ReturnMap();ClickMapAt(129.0756f,35.1796f);Start3DAtMapClick();break;
                // Real keys, as in Cities: Skylines: the run presses 3 after 51 (very fast, 4배), 2 after 52 (fast) and Space after 53 (pause).
                case 51:ReturnMap();gameSpeed=1;break;
                case 52:break;
                case 53:break;
                case 54:break;
                // Tickets for every non-bus mode: buy, step aboard, and the same train runs to the stop (log: "Ticket bought", "Ticket arrived ... after=").
                // KTX: buy at 56, wait 57-61, arrival at 62.
                case 55:StationWithKind("ktx");streetMenu=true;break;
                case 56:BuyFarthestTicket();QaBoardStationTrain();break;
                case 57:case 58:case 59:case 60:case 61:break;
                case 62:break;
                // 무궁화호: buy at 64, arrival at 70.
                case 63:StationWithKind("mugunghwa");streetMenu=true;break;
                case 64:BuyFarthestTicket();QaBoardStationTrain();break;
                case 65:case 66:case 67:case 68:case 69:break;
                case 70:break;
                // Flight: destination list at 71, ticket + security + boarding at 72, arrival at 78 (30 s after boarding).
                case 71:EnterDistrict(3,false);Teleport(WorldBuilder.AirportOrigin+new Vector3(-49.1f,EyeHeight,-30),Vector3.forward);flightDeskAirline=0;break;
                case 72:flightDeskAirline=-1;BuyFlightTicket(AirlineFlights(0)[0]);securityPassed=true;BoardAtGate(boardingGate);break;
                case 73:case 74:case 75:case 76:case 77:break;
                case 78:break;
                // Real mouse: the run logs where 강남 and the 3D button are, clicks them with the OS mouse, and 80 / 82 capture the result.
                case 79:ReturnMap();QaScheduleMapTarget("강남",127.0276f,37.4979f);break;
                case 80:break;
                case 81:ReturnMap();QaScheduleMapTarget("부산",129.0756f,35.1796f);break;
                case 82:break;
                // Airports: 78 lands in 제주국제공항. 83 shows its kiosk list, 84 buys 김해 and boards, 90 arrives at 김해, 91 leaves to the city.
                // 79-82 went back to the map, so return to the 제주 arrivals hall first.
                case 83:ArriveAirport("CJU","QA: 제주국제공항 도착 홀");kioskDesk=true;break;
                case 84:kioskDesk=false;BuyAirportTicket(KoreanAirports.ByCode("PUS"));BoardAtGate(boardingGate);break;
                case 85:case 86:case 87:case 88:case 89:break;
                case 90:break;
                case 91:if(world.DestinationAirport!=null)ArriveCity(world.DestinationAirport.City,world.DestinationAirport.name+"에서 나왔습니다.");break;
                // 진해: 92 logs where 진해역 and the 3D button are and the real mouse clicks them (93 captures the hall); 94 stands
                // in the shuttle at 진해역, 95 sends it off, 96 is under way, 97 has reached 경화역, 98 walks out onto its platform.
                case 92:ReturnMap();QaScheduleMapTarget("진해",(float)WorldBuilder.JinhaeLon,(float)WorldBuilder.JinhaeLat);break;
                case 93:break;
                case 94:QaBoardJinhae();break;
                case 95:if(world.JinhaeLine!=null)world.JinhaeLine.clock=JinhaeTrain.Dwell;break;
                case 96:break;
                case 97:QaRideJinhaeTo("경화");break;
                case 98:if(cabin!=null&&cabin.kind=="jinhae")ExitCabin(cabin.doorSide);break;
                // Visual-style regression: every legacy Seoul 3D entry must rebuild through the current Jinhae-style generator.
                case 99:EnterDistrict(0,false);break;
                case 100:EnterDistrict(1,false);break;
                case 101:EnterDistrict(2,false);break;
                case 102:EnterDistrict(3,false);break;
            }
            lookLocked=false;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            capturePoint=playtestPoint;captureAt=Time.time+CaptureDelaySeconds;
            Debug.Log("Playtest checkpoint "+playtestPoint+" feet="+Feet+" mode="+mode+" cabin="+(cabin!=null?cabin.kind:"none")+" fps="+(1f/Mathf.Max(Time.smoothDeltaTime,1e-4f)).ToString("F0"));
            playtestPoint=(playtestPoint+1)%103;
        }
        // QA: a station that has a line of this kind, visited on that line.
        void StationWithKind(string kind)
        {
            var station=TransitNetwork.Stations.Find(s=>s.lines.Exists(l=>l.kind==kind));
            VisitNetworkStation(station,station.lines.Find(l=>l.kind==kind));
        }
        // QA: step into the current station's train through its open door.
        void QaBoardStationTrain()
        {
            var j=stationJourney;if(j==null||j.doors==null)return;
            j.clock=0;j.Step(0);j.doors.Set(1);EnterCabin(j.doors.cabin,new Vector3(1,0,0));
        }
        // QA: stand in the 진해선 shuttle's first coach at 진해역, doors open.
        void QaBoardJinhae()
        {
            var line=world.JinhaeLine;if(mode!="carved"||line==null){Debug.Log("QA-JINHAE no shuttle (mode "+mode+")");return;}
            line.Begin(0);var c=line.cars[0].GetComponent<Cabin>();
            EnterCabin(c,new Vector3(c.doorSide,0,c.doors[0]));
            Debug.Log("QA-JINHAE aboard at "+line.Here+" "+eye.position);
        }
        // QA: run the shuttle on (in fixed steps, the rider carried) until it stands at `stop` with its doors open.
        void QaRideJinhaeTo(string stop)
        {
            var line=world.JinhaeLine;if(line==null)return;
            var start=eye.position;
            for(int i=0;i<20000&&!(line.Open&&line.Here==stop);i++){line.Step(.05f);if(cabin!=null)CarryInCabin();}
            Debug.Log("QA-JINHAE at "+line.Here+" open="+line.Open+" rode "+Vector3.Distance(start,eye.position).ToString("F0")+" m, aboard="+(cabin!=null));
        }
        // QA: buy a ticket to whichever end of the line is farther from the current stop.
        void BuyFarthestTicket()
        {
            var stops=stationJourney.stops;
            BuyTicketTo(stationJourney.index<stops.Count/2?stops[stops.Count-1]:stops[0],stationJourney.direction);
        }
        void QaScheduleMapTarget(string name,float longitude,float latitude){qaMapName=name;qaMapLongitude=longitude;qaMapLatitude=latitude;qaMapAt=Time.time+1.5f;}
        // QA: print the window pixel of a map point and of the map's 3D button, for the OS-mouse driver (top-left origin, window pixels).
        void QaLogMapTarget(string name,float longitude,float latitude)
        {
            var point=viewCamera.WorldToScreenPoint(GeoProjection.ToWorld(longitude,latitude));
            float scale=Mathf.Max(.55f,Mathf.Min(Screen.width/1440f,Screen.height/860f));
            var box=MapControlsRect(Screen.width/scale);
            Debug.Log("QA-MAP name="+name+" point="+Mathf.RoundToInt(point.x)+","+Mathf.RoundToInt(Screen.height-point.y)
                +" button3d="+Mathf.RoundToInt((box.x+8f+31f)*scale)+","+Mathf.RoundToInt((box.y+168f+19f)*scale)+" screen="+Screen.width+"x"+Screen.height);
        }
        // QA only: projects a lon/lat onto the map camera and clicks that pixel, so RecordMapClickAt runs the real screen-to-geo path.
        void ClickMapAt(float longitude,float latitude){RecordMapClickAt(viewCamera.WorldToScreenPoint(GeoProjection.ToWorld(longitude,latitude)));}
    }
}
