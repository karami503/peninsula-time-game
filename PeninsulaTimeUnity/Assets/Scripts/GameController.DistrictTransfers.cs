using UnityEngine;

namespace PeninsulaTime {
    // A transfer portal opens a walking passage (TransferPassage) to that line's own station, a network hub, which is
    // loaded while the walker is in the passage's middle stretch; walking back the same way loads the district again.
    public partial class GameController {
        TransferPassage passage;
        void UpdateDistrictTransfers(){
            UpdateTransferPassage();
            if(mode!="district"||world.aerialDistrict||cabin!=null||ridingCar!=null)return;
            foreach(var portal in world.DistrictTransfers){
                if(portal==null||!portal.Contains(Feet))continue;
                var at=TransitNetwork.Station(portal.stationId);var line=TransitNetwork.Line(portal.lineId);
                if(at==null||line==null)continue;
                if(passage!=null&&passage.stationId==portal.stationId&&passage.lineId==portal.lineId)return; // already open
                if(passage!=null)DestroyImmediate(passage.gameObject);
                passage=world.BuildTransferPassage(portal,GradeY(at)+6.8f-portal.entry.y,at.name+" "+line.shortName);
                passage.transform.SetParent(world.root.transform,true); // goes with the district
                Toast(line.shortName+" 갈아타는 통로 · 따라 걸어가면 "+at.name+" 대합실입니다");return;
            }
        }
        void UpdateTransferPassage(){
            if(passage==null||cabin!=null)return;
            float along=passage.Middle(Feet);if(float.IsNaN(along))return;
            if(!passage.atHub&&along>.6f&&mode=="district")PassageToHub();
            else if(passage.atHub&&along<.4f&&mode=="rail")PassageToDistrict();
        }
        void PassageToHub(){
            var at=TransitNetwork.Station(passage.stationId);var line=TransitNetwork.Line(passage.lineId);
            if(at==null||line==null)return;
            passage.transform.SetParent(world.transform,true); // kept while the world is rebuilt
            ClearRides();networkPassengerJourney=null;ridingCar=null;flight=null;cityStreet=false;undergroundWalk=false;
            stationJourney=world.BuildNetworkStation(at,line,StationJourney.Next(line,at,0).Count<2?1:0);stationAnnounced=0;
            state.selectedCity=WorldBuilder.NearestCity(TransitNetwork.Bare(at.name));
            mode="rail";tab="3D";streetBoard=false;selectedStation=null;world.dayNight=true;
            MovePassage(world.OpenNetworkHallBack(TransferPassage.Width),Vector3.forward,passage.End,Vector3.forward);
            world.CarveNetworkGround(passage);
            passage.atHub=true;passage.transform.SetParent(world.root.transform,true);
        }
        void PassageToDistrict(){
            passage.transform.SetParent(world.transform,true);
            ClearRides();
            int district=state.district;world.BuildDistrict(district);world.SetDistrictView(district,false);
            mode="district";riding=false;undergroundWalk=false;world.dayNight=true;tab="3D";
            var portal=world.DistrictTransfers.Find(p=>p!=null&&p.stationId==passage.stationId&&p.lineId==passage.lineId);
            if(portal==null){DestroyImmediate(passage.gameObject);passage=null;Vector3 facing;Teleport(world.PlatformSpawn(0,out facing),facing);return;} // the network was edited meanwhile
            var outward=portal.entry-portal.approach;outward.y=0;
            MovePassage(portal.entry,outward.normalized,Vector3.zero,Vector3.forward);
            world.OpenConcourseWall(passage);
            passage.atHub=false;passage.transform.SetParent(world.root.transform,true);
        }
        // Moves the passage, and the walker in it, rigidly so that its point `local` (heading `localHeading`) lands on
        // `target` heading `heading`.
        void MovePassage(Vector3 target,Vector3 heading,Vector3 local,Vector3 localHeading){
            var t=passage.transform;var from=t.TransformPoint(local);var direction=t.TransformDirection(localHeading);direction.y=0;
            var turn=Quaternion.Euler(0,Vector3.SignedAngle(direction,heading,Vector3.up),0);
            var offset=eye.position-from;
            t.SetPositionAndRotation(target+turn*(t.position-from),turn*t.rotation);
            eye.SetPositionAndRotation(target+turn*offset,turn*eye.rotation);
            Physics.SyncTransforms();PlaceViewCamera();
        }
    }
}
