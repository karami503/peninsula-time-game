using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Rides the subway from one district to the next district along its line and checks the ride is seamless: the
    // rider stays in the same car the whole way (never faded, never moved within it), the track is streamed in a
    // bounded number of chunks, every station on the way is a real stop, and the same train runs into the destination
    // district's own platform track with its doors open onto the platform floor. Stepping off at a station on the way
    // lands on that station's platform.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.MetroCorridorCheck.Run
    public static class MetroCorridorCheck
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        const float Step=1f/30f;
        const int MaxStops=45;
        static int failures,stepsOnTheWay;
        static void Check(bool ok,string message){if(!ok){failures++;Debug.LogError("MetroCorridorCheck: "+message);}}
        static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,Flags).Invoke(o,args);}
        static T Get<T>(object o,string name){return (T)o.GetType().GetField(name,Flags).GetValue(o);}
        static void Set(object o,string name,object value){o.GetType().GetField(name,Flags).SetValue(o,value);}

        // Runs the ride until the train stands at the next station; returns that station (null on failure).
        static NetStation RideToStop(GameController game,string label)
        {
            var cabin=Get<Cabin>(game,"cabin");var feet=Get<Vector3>(game,"cabinFeet");
            int bound=Mathf.CeilToInt((RailCorridor.Ahead+RailCorridor.Behind)/RailCorridor.Chunk)+2,peak=0;
            for(int i=0;i<(int)(300f/Step);i++)
            {
                Call(game,"UpdateNetRide",Step);
                Check(Get<float>(game,"fade")==0,label+": the screen faded");
                if(Get<Cabin>(game,"cabin")!=cabin){Check(false,label+": the rider left the train ("+game.NetRidePhase+")");return null;}
                Check((Get<Vector3>(game,"cabinFeet")-feet).sqrMagnitude<1e-6f,label+": the rider was moved inside the car");
                var corridor=Get<RailCorridor>(game,"netCorridor");if(corridor!=null)peak=Mathf.Max(peak,corridor.ChunkCount);
                if(game.NetRidePhase=="arrived")break;
            }
            Check(game.NetRidePhase=="arrived",label+": never stopped at the next station ("+game.NetRidePhase+")");
            Check(peak>0&&peak<=bound,label+": track chunks alive "+peak+" (bound "+bound+")");
            return game.NetRidePhase=="arrived"?Get<NetStation>(game,"netFrom"):null;
        }
        // Doors open onto the platform: step out on the open side and stand on the platform floor.
        static void StepOff(GameController game,string label,float platform)
        {
            for(int i=0;i<60;i++)Call(game,"UpdateNetRide",Step);
            var cabin=Get<Cabin>(game,"cabin");Check(cabin!=null&&cabin.Open,label+": doors not open at the station");if(cabin==null)return;
            Physics.SyncTransforms();
            Call(game,"ExitCabin",cabin.doorSide);
            float feet=Get<Transform>(game,"eye").position.y-1.65f;
            Check(Get<Cabin>(game,"cabin")==null&&Mathf.Abs(feet-platform)<.35f,label+": stepping off did not land on the platform (feet "+feet+", platform "+platform+")");
        }

        static void Ride(GameController game,WorldBuilder world,int from,bool leaveOnTheWay)
        {
            Call(game,"EnterDistrict",from,false);
            var start=world.StationTrains.Find(t=>t!=null&&t.line.net!=null&&t.line.ahead.Length>0&&t.consists.Count>0);
            Check(start!=null,WorldBuilder.StationTitle(from)+" has no track toward another district");if(start==null)return;
            int to=start.line.ahead[0];string label=WorldBuilder.StationTitle(from)+" → "+WorldBuilder.StationTitle(to)+" ("+start.line.line+")";
            var consist=start.consists[0];consist.gameObject.SetActive(true);consist.Place(0,1,start.direction);
            Call(game,"EnterCabin",consist.cabin,new Vector3(0,0,SubwayTrain.CarCentres[1]));
            Check(game.NetRideActive&&Get<TrainConsist>(game,"netConsist")==consist,label+": boarding did not start the ride");
            for(int n=0;n<MaxStops;n++)
            {
                var at=RideToStop(game,label);if(at==null)return;
                if(WorldBuilder.DistrictOf(at.name)==to)
                {
                    var track=Get<SubwayTrain>(game,"netTrack");
                    Check(game.state.district==to&&Get<string>(game,"mode")=="district",label+": "+at.name+" stop is not the district");
                    Check(track!=null&&world.StationTrains.Contains(track),label+": train not at one of the district's platform tracks");
                    if(track!=null)StepOff(game,label,track.transform.position.y-.16f+1.1f);
                    Debug.Log("MetroCorridorCheck "+label+": arrived after "+n+" stops on the way");
                    return;
                }
                Check(world.NetworkHubStation==at,label+": the station built is not the one stopped at ("+at.name+")");
                if(leaveOnTheWay)
                {
                    StepOff(game,label+" · "+at.name+" 하차",world.NetworkHubGrade+.8f);stepsOnTheWay++;
                    // The train ridden leaves; the next one of this line at the station runs on into the district.
                    for(int i=0;i<(int)(60f/Step)&&game.NetRideActive;i++)Call(game,"UpdateNetRide",Step);
                    Check(!game.NetRideActive,label+": the train left at "+at.name+" never went on");
                    var line=Get<NetLine>(game,"netLine");var service=world.NetworkServices.Find(s=>s.line==line&&s.direction==start.line.direction)??world.NetworkServices[0];
                    service.clock=0;service.Step(0);service.doors.Set(1);
                    Call(game,"EnterCabin",service.doors.cabin,new Vector3(1,0,0));
                    Check(game.NetRideActive,label+": boarding the next train at "+at.name+" did not start a ride");
                    var next=RideToStop(game,label+" (환승역 승차)");
                    var track=Get<SubwayTrain>(game,"netTrack");
                    Check(next!=null&&WorldBuilder.DistrictOf(next.name)==to&&game.state.district==to&&track!=null,label+": a train boarded at "+at.name+" did not run into the district platform");
                    if(track==null)return;
                    for(int i=0;i<60;i++)Call(game,"UpdateNetRide",Step);
                    Check(track.heldDoors>.9f,label+": screen doors shut while a network car stands at the district platform");
                    StepOff(game,label+" (환승역 승차)",track.transform.position.y-.16f+1.1f);
                    return;
                }
                for(int i=0;i<(int)(30f/Step)&&game.NetRidePhase=="arrived";i++)Call(game,"UpdateNetRide",Step);
            }
            Check(false,label+": did not reach the district within "+MaxStops+" stops");
        }

        public static void Run()
        {
            failures=0;stepsOnTheWay=0;
            try
            {
                TransitNetwork.Build(null);
                var camera=new GameObject("corridor check camera").AddComponent<Camera>();
                var world=new GameObject("corridor check world").AddComponent<WorldBuilder>();world.worldCamera=camera;
                var host=new GameObject("corridor check player");host.SetActive(false);
                var game=host.AddComponent<GameController>();game.state=new GameState();game.viewCamera=camera;game.world=world;
                Set(game,"eye",new GameObject("corridor check eye").transform);
                foreach(int from in new[]{1,0})Ride(game,world,from,false);
                Ride(game,world,1,true);
                Check(stepsOnTheWay==1,"never stepped off at a station on the way");
            }
            catch(System.Exception e){failures++;Debug.LogException(e);}
            Debug.Log("MetroCorridorCheck: "+(failures==0?"passed":failures+" failed"));
            EditorApplication.Exit(failures==0?0:1);
        }
    }
}
