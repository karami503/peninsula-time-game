using UnityEngine;

namespace PeninsulaTime
{
    // 진해구, carved from one lump (WorldBuilder.BuildJinhae): the walk starts in 진해역's hall and goes on out of the
    // front door into the streets, or out of the back door onto the platform and the shuttle to 경화역; the ground is
    // made and dropped around the walker as they go, and nothing moves them but their feet and the train they board.
    public partial class GameController
    {
        // A rail or metro station inside 진해구 (not a bus stop): the carved station nearest it ("진해" or "경화"), else null.
        static string JinhaeStopNear(NetStation station,NetLine line)
        {
            if(station==null||!WorldBuilder.JinhaeArea.Contains(new Vector2(station.lon,station.lat)))return null;
            if(line!=null?line.kind=="bus"||line.kind=="brt":!station.lines.Exists(l=>l.kind!="bus"&&l.kind!="brt"))return null;
            var here=new Vector2(station.lon,station.lat);
            float toJinhae=(here-new Vector2((float)WorldBuilder.JinhaeLon,(float)WorldBuilder.JinhaeLat)).sqrMagnitude;
            float toGyeonghwa=(here-new Vector2((float)WorldBuilder.GyeonghwaLon,(float)WorldBuilder.GyeonghwaLat)).sqrMagnitude;
            return toGyeonghwa<toJinhae?"경화":"진해";
        }
        // stop null: in 진해역's hall; "진해" or "경화": on that station's platform.
        void EnterJinhae(string stop=null)
        {
            ClearRides();cabin=null;ridingCar=null;flight=null;cityStreet=false;undergroundWalk=false;riding=false;
            mode="carved";tab="3D";streetBoard=false;selectedStation=null;
            world.BuildJinhae();world.dayNight=true;
            if(stop=="경화")Teleport(world.GyeonghwaSpawn+Vector3.up*EyeHeight,world.GyeonghwaFacing);
            else if(stop=="진해")Teleport(world.JinhaePlatformSpawn+Vector3.up*EyeHeight,world.JinhaePlatformFacing);
            else Teleport(world.JinhaeSpawn+Vector3.up*EyeHeight,world.JinhaeFacing);
            if(stop!=null)world.Carved.BuildAround(eye.position,140f);
            var line=world.JinhaeLine;
            if(line!=null)
            {
                System.Func<bool> aboard=()=>cabin!=null&&cabin.kind=="jinhae";
                line.leaving=next=>{if(aboard())Toast("문이 닫힙니다 · 다음 역 "+next);};
                line.arrived=here=>{if(aboard())Toast(here+"역 도착 · 문이 열리면 걸어서 내리세요");};
            }
            PlaceViewCamera();
            if(playtest)Debug.Log("QA-3D start 진해역 "+eye.position);
            if(stop!=null){Toast(stop+"역 승강장 · 진해선 셔틀로 "+(stop=="경화"?"진해":"경화")+"까지 · 걸어서 진해 시내로");return;}
            Toast("진해역 대합실에서 시작합니다 · 정문으로 나가면 진해 시내 · 뒷문으로 나가 승강장의 열차를 타면 경화역 · WASD 이동");
        }

        // A national train physically reaches the carved platform. Preserve the coach and the rider's position in it,
        // rebuild the destination around the detached coach, open its doors, and let the player walk out normally.
        void ArriveJinhaeByTrain(string stop)
        {
            var vehicle=netVehicle;var arrivedCabin=NetCabin;
            if(vehicle==null||arrivedCabin==null){EnterJinhae(stop);return;}
            Vector3 riderLocal=arrivedCabin.transform.InverseTransformPoint(eye.position);
            PlaceNet(1);vehicle.SetParent(null,true);
            if(netCorridor!=null)DestroyImmediate(netCorridor.gameObject);
            ResetNetRide();

            cityStreet=false;undergroundWalk=false;riding=false;ridingCar=null;flight=null;
            mode="carved";tab="3D";streetBoard=false;selectedStation=null;
            world.BuildJinhae();world.dayNight=true;
            var platform=stop=="경화"?world.GyeonghwaStation:world.JinhaeStation;
            Vector3 centre=stop=="경화"?world.GyeonghwaSpawn:world.JinhaePlatformSpawn;
            centre.y-=WorldBuilder.JinhaePlatformY;
            if(platform!=null)centre+=stop=="경화"?-platform.right*3f:platform.forward*2.6f;
            Vector3 along=platform!=null?(stop=="경화"?platform.forward:platform.right):Vector3.right;
            Quaternion wanted=Quaternion.LookRotation(along,Vector3.up);
            Quaternion turn=wanted*Quaternion.Inverse(arrivedCabin.transform.rotation);
            vehicle.rotation=turn*vehicle.rotation;
            vehicle.position+=centre-arrivedCabin.transform.position;
            vehicle.SetParent(world.root.transform,true);cabin=arrivedCabin;
            eye.SetPositionAndRotation(arrivedCabin.transform.TransformPoint(riderLocal),wanted);
            world.Carved.BuildAround(eye.position,140f);Physics.SyncTransforms();PlaceViewCamera();
            Toast(stop+"역 도착 · 문이 열렸습니다 · 열차에서 걸어 내리세요");
        }
    }
}
