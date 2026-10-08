using UnityEngine;

namespace PeninsulaTime {
    public partial class GameController {
        void UpdateDistrictTransfers(){
            if(mode!="district"||world.aerialDistrict||cabin!=null||ridingCar!=null)return;
            foreach(var portal in world.DistrictTransfers){
                if(portal==null||!portal.Contains(Feet))continue;
                var at=TransitNetwork.Station(portal.stationId);var line=TransitNetwork.Line(portal.lineId);
                if(at==null||line==null)continue;
                VisitNetworkStation(at,line);fade=1;
                Toast(line.shortName+" 환승 승강장입니다 · 열린 문으로 걸어서 승차하세요");return;
            }
        }
        void StartNetworkMetroRide(TrainConsist source){
            var side=source.train.line;var line=side.net;
            if(line==null||side.index<0||side.index>=line.stops.Count)return;
            var feet=cabinFeet;float yaw=cabinYaw;
            VisitNetworkStation(line.stops[side.index],line,side.direction);
            var next=stationJourney!=null&&stationJourney.doors!=null?stationJourney.doors.cabin:null;
            if(next==null)return;
            cabin=next;cabinFeet=next.Clamp(feet,feet);cabinYaw=yaw;
            BoardNetworkRail(next,true);CarryInCabin();fade=1;
        }
        bool TryLeaveIntermediateMetro(){
            if(metroConsist==null||cabin!=metroConsist.cabin||metroPhase!="stop"||metroShown<=0||metroShown>metroStops.Count)return false;
            var line=metroLine.net;string name=metroStops[metroShown-1];
            var at=line!=null?line.stops.Find(s=>TransitNetwork.Bare(s.name)==TransitNetwork.Bare(name)):null;
            if(at==null)return false;
            int direction=metroLine.direction;VisitNetworkStation(at,line,direction);fade=1;
            Toast(at.name+" 도착 · 바닥의 노선색 안내를 따라 환승하세요");return true;
        }
    }
}
