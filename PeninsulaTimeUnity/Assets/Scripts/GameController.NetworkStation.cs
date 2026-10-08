using UnityEngine;

namespace PeninsulaTime
{
    public partial class GameController
    {
        StationJourney stationJourney;
        StationJourney networkPassengerJourney;
        int stationAnnounced;
        void VisitNetworkStation(NetStation station,NetLine preferred=null,int direction=0)
        {
            if(state.era<9){Toast("현대 시대에 역을 방문할 수 있습니다.");return;}
            var line=preferred??station.lines.Find(l=>l.kind=="metro"||l.kind=="ktx"||l.kind=="mugunghwa"||l.kind=="bus"||l.kind=="brt");
            if(line==null){Toast("이 역에 연결된 철도 노선이 없습니다.");return;}
            if(StationJourney.Next(line,station,direction).Count<2)direction=1-direction;
            ClearRides();networkPassengerJourney=null;ridingCar=null;flight=null;cityStreet=false;undergroundWalk=false;
            stationJourney=world.BuildNetworkStation(station,line,direction);stationAnnounced=0;
            state.selectedCity=WorldBuilder.NearestCity(TransitNetwork.Bare(station.name));
            mode="rail";tab="서울 3D";streetBoard=false;selectedStation=null;world.dayNight=true;
            float doorway=stationJourney.doors!=null?stationJourney.doors.cabin.doors[stationJourney.doors.cabin.doors.Length-1]:7.55f;
            if(stationJourney.doors!=null){var c=stationJourney.doors.cabin;var p=c.transform.TransformPoint(new Vector3(c.halfWidth+1.1f,0,doorway));if(!stationJourney.Bus)p.y=world.NetworkSpawn.y;Teleport(p+Vector3.up*EyeHeight,-c.transform.right);}
            else Teleport(world.NetworkSpawn+Vector3.up*EyeHeight,Vector3.left);
            Toast(station.name+" · 열린 문으로 걸어서 승차"+(world.NetworkArea!=null&&world.NetworkArea.HasData?" · 실제 지도 도로와 건물 윤곽 로드":""));
        }
        bool MayBoardNetworkRail(Cabin c)
        {
            var s=c.GetComponentInParent<StationJourney>();
            if(s==null||s.startDelay>.001f||!s.Stopped||s.Finished)return Refuse("정차 중인 열차의 열린 문으로 들어가세요",false);
            return true;
        }
        void BoardNetworkRail(Cabin c,bool paid=false)
        {
            stationJourney=c.GetComponentInParent<StationJourney>();
            if(stationJourney==null)return;
            networkPassengerJourney=stationJourney;
            world.PrepareNetworkJourney(stationJourney);stationAnnounced=stationJourney.index;
            stationJourney.Board();Beep.Play("tap");
            Toast(stationJourney.line.name+" 승차 · 다음 정차역까지 객실에서 이동합니다");
        }
        void UpdateNetworkJourney()
        {
            if(stationJourney==null)return;
            // Alight first, then replace the old journey with the actual arrival station's
            // complete interchange. Never clear a train while the player is still inside it.
            if(cabin!=null&&cabin.kind=="networkrail")networkPassengerJourney=stationJourney;
            if(cabin==null&&networkPassengerJourney!=null){
                stationJourney=networkPassengerJourney;networkPassengerJourney=null;
                if(!stationJourney.Bus&&stationJourney.index>0)RebuildNetworkInterchangeAfterAlighting();
                else if(!stationJourney.Bus){
                    // A passenger who steps back out before departure remains at the origin.
                    // Resume this stop's unoccupied timetable instead of following an empty train.
                    stationJourney.started=false;stationJourney.occupied=false;stationJourney.clock=0;
                    stationJourney.travelled=0;stationJourney.wait=8;stationJourney.Step(0);
                }
            }
            if(cabin==null&&world.NetworkServices.Count>1){
                var nearest=world.NetworkServiceAt(Feet,stationJourney);
                if(nearest!=null&&nearest!=stationJourney){stationJourney=nearest;stationAnnounced=nearest.index;}
            }
            if(stationJourney.index!=stationAnnounced)
            {
                stationAnnounced=stationJourney.index;
                if(!stationJourney.Bus)world.RefreshNetworkArea(stationJourney.Current,stationJourney.origin+Vector3.forward*(stationJourney.index*StationJourney.Spacing));
                Toast(stationJourney.Current.name+" 도착 · 문이 열리면 걸어서 하차");
                state.selectedCity=WorldBuilder.NearestCity(TransitNetwork.Bare(stationJourney.Current.name));
            }
            if(cabin!=null&&cabin.kind=="networkrail")CarryInCabin();
        }
        void RebuildNetworkInterchangeAfterAlighting(){
            if(cabin!=null||stationJourney==null||stationJourney.Bus||stationJourney.index<=0)return;
            var arrived=stationJourney.Current;var line=stationJourney.line;int direction=stationJourney.direction;
            // Each journey uses schematic station spacing, so preserve the exact platform-relative
            // foot position and view while recentering its freshly-loaded interchange.
            var oldCentre=stationJourney.origin+Vector3.forward*(stationJourney.index*StationJourney.Spacing);
            var localFeet=Feet-oldCentre;float yaw=Yaw,pitch=Pitch;
            stationJourney=world.BuildNetworkStation(arrived,line,direction);stationAnnounced=0;
            eye.position=stationJourney.origin+localFeet+Vector3.up*EyeHeight;
            SetLook(yaw,pitch);verticalSpeed=0;grounded=true;jumpQueued=false;
            state.selectedCity=WorldBuilder.NearestCity(TransitNetwork.Bare(arrived.name));
            Toast(arrived.name+" 하차 · 바닥 환승 안내를 따라 다른 노선의 열린 문으로 이동하세요");
        }
        void DrawNetworkStationPanel()
        {
            Label(stationJourney.Current.name,titleStyle);
            Label(stationJourney.line.name,headingStyle);
            if(stationJourney.line.planned)Label("계획 시나리오 · 미확정 정거장 위치는 추정",smallStyle);
            Label("전국 역 방문 · 반경 450m 실제 지도 도로·건물 윤곽",smallStyle);
            if(world.NetworkArea!=null){Label(world.NetworkArea.SourceNotice,smallStyle);Label("도로 "+world.NetworkArea.roads+" · 건물 "+world.NetworkArea.buildings+" · 지도 출입구 "+world.NetworkArea.entrances.Count,smallStyle);}
            if(!stationJourney.Bus){
                var architecture=StationAreaData.Architecture(stationJourney.Current,stationJourney.line);
                if(architecture!=null){
                    Label("승강장 길이 "+architecture.platformLength.ToString("0.#")+"m · 서울교통공사 공식값 반영",smallStyle);
                    Label("공식 자료의 형식 "+architecture.platformType+" · 층수 "+architecture.floorLabel,smallStyle);
                }else Label("승강장 길이 100m · 미측정 구간의 게임용 추정",smallStyle);
                Label("승강장 폭 12m는 추정 · 선로 배치·출구·층간 깊이는 공통 보행 모델",smallStyle);
                Label("지상·지하·고가: OSM 선로에서 추정 · 실측 실내 평면도는 미반영",smallStyle);
                var plan=StationPlanData.ForStation(stationJourney.Current,stationJourney.line);
                if(plan!=null)Label(plan.FloorReferenceLabel,smallStyle);
                if(plan!=null&&plan.HasDiagram&&Button("공식 역사 안내도 자료 보기"))Application.OpenURL(plan.planURL);
                if(Button("추가 건물 자료 출처"))Application.OpenURL("https://doi.org/10.5281/zenodo.8174931");
                if(Button("Overture 지도 출처"))Application.OpenURL("https://docs.overturemaps.org/attribution/");
            }
            Label("역간 이동 구간은 게임용 공통 경로입니다.",smallStyle);
            for(int i=0;i<stationJourney.stops.Count;i++)
                Label((i==stationJourney.index?"● ":"○ ")+stationJourney.stops[i].name,bodyStyle);
            if(stationJourney.Bus)Label("창원 BIS 정류장·주행 경로 · 50초 배차",smallStyle);
            Label(cabin!=null?"객실 안에서 이동 가능 · 정차 후 열린 문으로 하차":(stationJourney.Bus?"50초마다 버스 도착":"30초마다 열차 도착")+" · 문이 열리면 걸어서 승차",smallStyle);
            if(cabin==null&&stationJourney.Bus){
                if(Button("이 정류장에서 다음 구간 탐방",true))VisitNetworkStation(stationJourney.Current,stationJourney.line,stationJourney.direction);
                if(Button("반대 방향"))VisitNetworkStation(stationJourney.Current,stationJourney.line,1-stationJourney.direction);
            }
            if(cabin==null&&!stationJourney.Bus)
            {
                Label("연결 경사로 → 공용 대합실 → 바닥 안내 색을 따라 다른 노선의 열린 문으로 환승",smallStyle);
                Label("이 역에서 갈아탈 수 있는 열차",headingStyle);
                foreach(var route in world.NetworkTransfers)
                    Label(route.line.name+" · "+route.toward+(route.line.planned?" (계획 시나리오)":""),smallStyle);
            }
            if(Button("전국 교통 지도로 돌아가기")){ReturnMap();tab="교통";}
        }
    }
}
