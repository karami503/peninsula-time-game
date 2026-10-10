using System.Collections.Generic;
using UnityEngine;
namespace PeninsulaTime {
    public partial class GameController {
        // Read-only check of what the current district depends on: entrances, bus stops, trains, clock, save round-trip.
        // Nothing is saved or spawned; SelfTestCheck runs it on every district.
        public List<string> RunSelfTest() {
            var lines=new List<string>();
            void Check(bool ok,string name){lines.Add((ok?"OK   ":"FAIL ")+name);}
            try {
                int entrances=world==null?0:world.MappedEntranceCount, stops=world==null?0:world.BusStopCount, trains=world==null?0:world.StationTrains.Count;
                Check(entrances>0,"출입구 "+entrances+"개");
                Check(stops>0,"버스 정류장 "+stops+"개");
                Check(trains>0,"지하철 열차 "+trains+"편");
                // A shelter with no route (no bus line within 150 m) has no timetable; check the first stop that has one.
                var served=new List<NetStation>();
                if(world!=null)foreach(var info in world.BusStopList)if(info.station!=null&&BusStops.RoutesAt(info.station).Count>0)served.Add(info.station);
                Check(served.Count>0,"노선 있는 정류장 "+served.Count+"/"+(world==null?0:world.BusStopList.Count)+"개");
                var stop=served.Count>0?served[0]:null;
                Check(stop!=null&&BusStops.Next(stop,TransitSchedule.Now,3).Count>0,"버스 도착 예측");
                int crossings=world!=null&&world.DistrictRoads!=null?world.DistrictRoads.nodes.Count:0;
                Check(crossings>1,"배달 도로 교차점 "+crossings+"개");
                Check(DayCycle.Hours>=0f&&DayCycle.Hours<24f,"낮밤 시계 "+DayCycle.Clock);
                var copy=JsonUtility.FromJson<GameState>(JsonUtility.ToJson(state));
                Check(copy!=null&&copy.buildings.Count==state.buildings.Count&&copy.routes.Count==state.routes.Count,"저장 왕복");
            } catch(System.Exception e) {
                lines.Add("FAIL 예외 "+e.Message);
            }
            return lines;
        }
    }
}
