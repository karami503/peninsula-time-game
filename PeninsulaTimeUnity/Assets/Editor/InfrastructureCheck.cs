using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Batch check of the mapped Seoul districts: entrances lead down to a working station (fare gates, trains),
    // KTX only on Seoul Station's surface tracks, bus stops, and Gimpo's terminal with aircraft at the gates.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.InfrastructureCheck.Run
    public static class InfrastructureCheck
    {
        static int failures;
        static void Expect(bool condition,int district,string message){if(!condition){failures++;Debug.LogError("InfrastructureCheck district "+district+": "+message);}}
        public static void Run()
        {
            var camera=new GameObject("Infrastructure camera").AddComponent<Camera>();
            var world=new GameObject("Infrastructure world").AddComponent<WorldBuilder>();world.worldCamera=camera;
            failures=0;
            int[] expectedEntrances={10,14,6,4}; // Gimpo: four surveyed exits (WorldBuilder.GimpoStation BuildGimpoEntrances)
            for(int district=0;district<4;district++)
            {
                world.BuildDistrict(district);Physics.SyncTransforms();
                world.SetDistrictView(district,false);Physics.SyncTransforms();
                var feet=camera.transform.position-Vector3.up*1.65f;
                bool insideBuilding=false;
                foreach(var hit in Physics.OverlapCapsule(feet+Vector3.up*.45f,feet+Vector3.up*1.55f,.3f))
                    if(hit.name.ToLowerInvariant().StartsWith("building")||hit.name.ToLowerInvariant().StartsWith("roof"))insideBuilding=true;
                Expect(!insideBuilding,district,"street camera starts inside a building at "+feet);
                RaycastHit front;
                if(Physics.Raycast(camera.transform.position,camera.transform.forward,out front,4f,~0,QueryTriggerInteraction.Ignore))
                    Expect(!front.collider.name.ToLowerInvariant().Contains("building"),district,"street camera faces a nearby building at "+front.distance.ToString("F1")+" m");
                Expect(world.MappedEntranceCount==expectedEntrances[district],district,"entrance count "+world.MappedEntranceCount);
                Expect(world.BusStopCount>0,district,"no bus stops");
                // BIS at every stop: each shelter has a clickable arrival screen whose live text lists routes with arrival times.
                int shelters=0,withBis=0,withTimes=0;
                foreach(Transform t in world.root.transform)
                {
                    if(!t.name.StartsWith("버스 정류장")&&!t.name.StartsWith("BRT 정류장"))continue;
                    shelters++;
                    bool bis=false;foreach(var f in t.GetComponentsInChildren<Fixture>())if(f.kind=="bis")bis=true;
                    if(bis)withBis++;
                    var board=t.GetComponentInChildren<TransitBoard>();
                    if(board!=null&&board.compose!=null&&(board.compose().Contains("분 ")||board.compose().Contains("곧 도착")||board.compose().Contains("정류장 도착")))withTimes++;
                }
                Expect(withBis==shelters,district,"bus shelters without BIS: "+(shelters-withBis)+" of "+shelters);
                Expect(withTimes==shelters,district,"BIS screens without arrival times: "+(shelters-withTimes)+" of "+shelters);
                Debug.Log("InfrastructureCheck district "+district+": BIS "+withBis+"/"+shelters+", with arrival times "+withTimes);
                foreach(var mesh in world.root.GetComponentsInChildren<MeshFilter>())
                    if(mesh.name.StartsWith("OSM 주차장")&&(mesh.sharedMesh==null||mesh.sharedMesh.triangles.Length<3))Expect(false,district,"empty parking surface "+mesh.name);
                // KTX runs only on Seoul Station's surface tracks; every station has two subway trains.
                Expect(district==1?world.MovingTrainCount>0:world.MovingTrainCount==0,district,"surface trains "+world.MovingTrainCount);
                foreach(var rail in world.root.GetComponentsInChildren<RailVehicle>())
                {
                    if(rail.name.StartsWith("유도로"))continue;
                    // Over one full service cycle the set must leave its stop; a set hidden on the far side of its phase does not move.
                    var before=rail.transform.position;float moved=0;
                    for(float t=0;t<RailVehicle.Cycle;t+=.5f){rail.Step(.5f);moved=Mathf.Max(moved,Vector3.Distance(before,rail.transform.position));}
                    Expect(moved>1f,district,rail.name+" did not move over a cycle (stopAt "+rail.stopAt+", length "+rail.Length+", furthest "+moved.ToString("F1")+" m)");
                }
                // One island per line: two sides each, tied to the network's timetable (홍대입구 has 2호선 and 공항철도).
                Expect(world.StationTrains.Count==world.PlatformSides.Count&&world.StationTrains.Count==(district==3?10:district==2?4:district==1?6:2),district,"subway trains "+world.StationTrains.Count);
                bool anyAhead=false;
                foreach(var side in world.PlatformSides){Expect(side.net!=null,district,side.line+" "+side.toward+" has no timetable");anyAhead|=side.ahead.Length>0;}
                Expect(anyAhead,district,"no train goes to another district");
                foreach(var train in world.StationTrains)
                {
                    train.Begin(0);bool boardable=false;
                    for(float t=0;t<SubwayTrain.Cycle;t+=.5f){train.Step(.5f);boardable|=train.Boardable;}
                    Expect(boardable,district,train.line.line+" train never stopped for boarding");
                }
                // Portals: street entrances go down, station exits come up to the street.
                int down=0,up=0;
                foreach(var portal in world.root.GetComponentsInChildren<StationPortal>())
                {
                    if(portal.downstairs){down++;Expect(portal.destination.y<-1f||portal.destination.x>WorldBuilder.AirportOrigin.x-100f,district,portal.name+" does not lead inside");}
                    else up++;
                    // Nothing underground may lead past the fare gates without a tap.
                    if(portal.transform.position.y<-1f)Expect(portal.transform.position.z<WorldBuilder.GateZ,district,portal.name+" opens inside the paid area");
                    if(portal.downstairs&&portal.destination.y<-1f)Expect(portal.destination.z<WorldBuilder.GateZ,district,portal.name+" lands inside the paid area");
                    Expect(portal.destination.sqrMagnitude>1f,district,portal.name+" has no destination");
                }
                Expect(down>=expectedEntrances[district]&&up>0,district,"entrance/exit portals "+down+"/"+up);
                CheckFareGates(world,district);
                if(district==3)CheckAirport(world);
                Debug.Log("InfrastructureCheck district "+district+": entrances "+world.MappedEntranceCount+", portals "+down+"/"+up+", bus stops "+world.BusStopCount+", trains "+world.MovingTrainCount+", aircraft "+world.AircraftCount);
            }
            Debug.Log("InfrastructureCheck: "+(failures==0?"passed":failures+" failed"));
            EditorApplication.Exit(failures==0?0:1);
        }
        // A closed fare gate stops a walker; a tapped one lets them through and closes again.
        static void CheckFareGates(WorldBuilder world,int district)
        {
            Barrier gate=null;
            foreach(var b in world.root.GetComponentsInChildren<Barrier>())if(b.kind=="fare"){gate=b;break;}
            Expect(gate!=null&&gate.blocker!=null,district,"no fare gate");
            if(gate==null||gate.blocker==null)return;
            var lane=gate.blocker.bounds.center;var origin=lane+Vector3.back*3f;
            RaycastHit hit;
            bool blocked=Physics.SphereCast(origin,.3f,Vector3.forward,out hit,6f,~0,QueryTriggerInteraction.Ignore)&&hit.collider==gate.blocker;
            Expect(blocked,district,"closed fare gate does not stop a walker (hit "+(hit.collider!=null?hit.collider.name:"none")+")");
            gate.Open();Physics.SyncTransforms();
            bool through=!Physics.SphereCast(origin,.3f,Vector3.forward,out hit,6f,~0,QueryTriggerInteraction.Ignore)||hit.collider!=gate.blocker;
            Expect(through,district,"open fare gate still blocks");
            gate.Step(Barrier.OpenSeconds+.5f);
            Expect(gate.blocker.enabled,district,"fare gate did not close again");
        }
        static void CheckAirport(WorldBuilder world)
        {
            Expect(world.AircraftCount>0,3,"no aircraft at the stands");
            Expect(world.TerminalEntrance.sqrMagnitude>1f,3,"no terminal entrance");
            for(int g=1;g<=8;g++)Expect(world.GateAircraft(g)!=null,3,"no aircraft at gate "+g);
            int checkin=0,gates=0,security=0;
            foreach(var f in world.root.GetComponentsInChildren<Fixture>()){if(f.kind=="checkin")checkin++;if(f.kind=="gate")gates++;}
            foreach(var b in world.root.GetComponentsInChildren<Barrier>())if(b.kind=="security"){security++;Expect(b.blocker!=null&&b.blocker.enabled,3,"security lane open without a pass");}
            Expect(checkin>=4&&gates==8&&security>0,3,"terminal fixtures checkin "+checkin+", gates "+gates+", security "+security);
        }
    }
}
