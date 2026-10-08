using System;
using UnityEngine;
namespace PeninsulaTime {
    public partial class GameController {
        // Explicit QA opt-in, always isolated from the player's normal save directory.
        bool playtest;int playtestPoint;
        void StartPlaytest(){
            var args=Environment.GetCommandLineArgs();
            playtest=Array.IndexOf(args,"--playtest")>=0&&Array.IndexOf(args,"--save-directory")>=0;
            if(Array.IndexOf(args,"--changwon")>=0&&Array.IndexOf(args,"--save-directory")>=0){showIntro=false;EnterOpenWorld();return;}
            if(playtest)NextPlaytestPoint();
        }
        void NextPlaytestPoint(){
            state.era=9;showIntro=false;
            if(playtestPoint==0||world==null||state.district!=3)EnterDistrict(3,false);
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
            }
            lookLocked=false;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            Debug.Log("Playtest checkpoint "+playtestPoint+" feet="+Feet);playtestPoint=(playtestPoint+1)%23;
        }
    }
}
