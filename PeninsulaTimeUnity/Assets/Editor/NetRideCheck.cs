using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // National-network rides are seamless: boarding a district line that leaves the detailed districts, Seoul
    // Station's KTX, or a train at a network station, the rider stays in the same car (never faded, never moved within
    // it) until it stops at the next station, which was built while the train ran; stepping off lands on that
    // station's platform. Staying aboard rides on to the following stop, and a ticket runs straight to its station.
    // A KTX toward Seoul runs into Seoul Station's own surface platform along its mapped track.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.NetRideCheck.Run
    public static class NetRideCheck
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        const float Step=1f/30f;
        static int failures;
        static void Check(bool ok,string message){if(!ok){failures++;Debug.LogError("NetRideCheck: "+message);}}
        static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,Flags).Invoke(o,args);}
        static T Get<T>(object o,string name){return (T)o.GetType().GetField(name,Flags).GetValue(o);}
        static void Set(object o,string name,object value){o.GetType().GetField(name,Flags).SetValue(o,value);}

        // Runs the ride until the train stands at the next station; returns that station (null on failure).
        static NetStation RideToStop(GameController game,WorldBuilder world,string label,bool rail=false)
        {
            var cabin=Get<Cabin>(game,"cabin");var feet=Get<Vector3>(game,"cabinFeet");
            int bound=Mathf.CeilToInt((RailCorridor.Ahead+RailCorridor.Behind)/RailCorridor.Chunk)+2,peak=0;bool loaded=false;float ground=float.NaN;
            for(int i=0;i<(int)(300f/Step);i++)
            {
                if(rail)Call(game,"UpdateRailRide",Step);
                Call(game,"UpdateNetRide",Step);
                Check(Get<float>(game,"fade")==0,label+": the screen faded");
                if(Get<Cabin>(game,"cabin")!=cabin){Check(false,label+": the rider left the train ("+game.NetRidePhase+")");return null;}
                Check((Get<Vector3>(game,"cabinFeet")-feet).sqrMagnitude<1e-6f,label+": the rider was moved inside the car");
                var corridor=Get<RailCorridor>(game,"netCorridor");if(corridor!=null)peak=Mathf.Max(peak,corridor.ChunkCount);
                // The ground beside the track (in the train's frame) must not jump when the next station is swapped in.
                float under=corridor!=null?corridor.GroundAt(Get<float>(game,"netAlong")):float.NaN;
                if(!loaded&&Get<bool>(game,"netLoaded")&&!float.IsNaN(ground)&&!float.IsNaN(under))Check(Mathf.Abs(under-ground)<.5f,label+": ground jumped "+ground+" -> "+under+" when the next station loaded");
                ground=under;
                loaded|=Get<bool>(game,"netLoaded");
                if(game.NetRidePhase=="arrived")break;
            }
            Check(game.NetRidePhase=="arrived",label+": never stopped at the next station ("+game.NetRidePhase+")");
            Check(loaded&&peak>0&&peak<=bound,label+": station not loaded on the way, or track chunks "+peak+" (bound "+bound+")");
            var at=Get<NetStation>(game,"netFrom");
            bool district=game.state.district==WorldBuilder.DistrictOf(at!=null?at.name:"")&&Get<string>(game,"mode")=="district";
            Check(world.NetworkHubStation==at||district,label+": the station built is not the one stopped at");
            Debug.Log("NetRideCheck "+label+": arrived at "+(at!=null?at.name:"?")+", peak "+peak+" chunks");
            return at;
        }
        // Doors open onto the platform: step out on the +x side and stand on it.
        static void StepOff(GameController game,WorldBuilder world,string label)
        {
            for(int i=0;i<60;i++)Call(game,"UpdateNetRide",Step);
            var cabin=Get<Cabin>(game,"cabin");Check(cabin!=null&&cabin.Open,label+": doors not open at the station");if(cabin==null)return;
            Physics.SyncTransforms();
            int side=Vector3.Dot(cabin.transform.right,Vector3.right)>0?1:-1;
            Call(game,"ExitCabin",side);
            var eye=Get<Transform>(game,"eye");float feet=eye.position.y-1.65f;
            float platform=world.NetworkHubGrade+.8f;
            Check(Get<Cabin>(game,"cabin")==null&&Mathf.Abs(feet-platform)<.35f,label+": stepping off did not land on the platform (feet "+feet+", platform "+platform+")");
        }

        public static void Run()
        {
            failures=0;
            try
            {
                TransitNetwork.Build(null);
                var camera=new GameObject("net ride camera").AddComponent<Camera>();
                var world=new GameObject("net ride world").AddComponent<WorldBuilder>();world.worldCamera=camera;
                var host=new GameObject("net ride player");host.SetActive(false);
                var game=host.AddComponent<GameController>();game.state=new GameState();game.viewCamera=camera;game.world=world;
                Set(game,"eye",new GameObject("net ride eye").transform);

                // 1. A Gimpo line that runs on past the detailed districts, then staying aboard to the next stop.
                Call(game,"EnterDistrict",3,false);
                var train=world.StationTrains.Find(t=>t.line.net!=null&&t.line.ahead.Length==0&&t.consists.Count>0);
                Check(train!=null,"no Gimpo line leaving the districts");
                if(train!=null)
                {
                    var c=train.consists[0];c.gameObject.SetActive(true);c.Place(0,1,train.direction);
                    Call(game,"EnterCabin",c.cabin,new Vector3(0,0,SubwayTrain.CarCentres[1]));
                    Check(game.NetRideActive,"district line ride did not start");
                    var first=RideToStop(game,world,"김포 "+train.line.line);
                    if(first!=null)
                    {
                        for(int i=0;i<(int)(15f/Step)&&game.NetRidePhase=="arrived";i++)Call(game,"UpdateNetRide",Step);
                        var second=RideToStop(game,world,"김포 "+train.line.line+" (계속 탑승)");
                        Check(second!=null&&second!=first,"staying aboard did not ride on to the next stop");
                        if(second!=null)StepOff(game,world,"계속 탑승 하차");
                    }
                }
                // 2. Seoul Station's KTX, out along its mapped track and on to its first stop.
                Call(game,"EnterDistrict",1,false);
                RailVehicle ktx=null;foreach(var r in world.PlatformTrains)if(r!=null&&r.doors!=null){ktx=r;break;}
                Check(ktx!=null,"no KTX at a Seoul Station platform");
                if(ktx!=null)
                {
                    ktx.doors.Set(1);
                    Call(game,"EnterCabin",ktx.doors.cabin,new Vector3(0,0,0));
                    Check(Get<RailVehicle>(game,"railRide")==ktx,"KTX boarding did not start (no departing trip)");
                    if(Get<RailVehicle>(game,"railRide")==ktx){var stop=RideToStop(game,world,"서울역 KTX",true);if(stop!=null)StepOff(game,world,"KTX 하차");}
                }
                // 3. A train boarded at a network station with a ticket two stops on: it runs straight there.
                var hubStation=TransitNetwork.Named("강남").Find(s=>s.lines.Exists(l=>l.shortName=="2호선"));
                Check(hubStation!=null,"강남 2호선 network station missing");
                if(hubStation!=null)
                {
                    Call(game,"VisitNetworkStation",hubStation,hubStation.lines.Find(l=>l.shortName=="2호선"),0);
                    var journey=Get<StationJourney>(game,"stationJourney");
                    var target=journey.stops[Mathf.Min(2,journey.stops.Count-1)];
                    Call(game,"BuyTicketTo",target,journey.direction);
                    journey.clock=0;journey.Step(0);journey.doors.Set(1);
                    Call(game,"EnterCabin",journey.doors.cabin,new Vector3(1,0,0));
                    Check(game.NetRideActive,"boarding at a network station did not start the ride");
                    var stop=RideToStop(game,world,"강남 2호선 표");
                    Check(stop==target,"ticket ride stopped at "+(stop!=null?stop.name:"?")+" instead of "+target.name);
                    if(stop!=null)StepOff(game,world,"표 하차");
                }
                // 4. KTX toward Seoul from a network station: it runs into Seoul Station's own surface platform.
                NetStation from=null;NetLine ktxLine=null;int toward=-1;
                foreach(var s in TransitNetwork.Named("광명"))foreach(var l in s.lines)for(int d=0;d<2&&toward<0;d++)
                {
                    if(l.kind!="ktx")continue;var next=StationJourney.Next(l,s,d,1);
                    if(next.Count>1&&WorldBuilder.DistrictOf(next[1].name)==GameController.SeoulStationDistrict){from=s;ktxLine=l;toward=d;}
                }
                Check(from!=null,"광명 KTX toward 서울 missing");
                if(from!=null)
                {
                    Call(game,"VisitNetworkStation",from,ktxLine,toward);
                    var journey=Get<StationJourney>(game,"stationJourney");
                    journey.clock=0;journey.Step(0);journey.doors.Set(1);
                    Call(game,"EnterCabin",journey.doors.cabin,new Vector3(1,0,0));
                    Check(game.NetRideActive,"boarding KTX toward Seoul did not start the ride");
                    var stop=RideToStop(game,world,"광명 KTX → 서울");
                    Check(game.state.district==GameController.SeoulStationDistrict&&Get<string>(game,"mode")=="district","KTX to Seoul did not arrive in the Seoul Station district");
                    if(stop!=null)
                    {
                        for(int i=0;i<60;i++)Call(game,"UpdateNetRide",Step);
                        var cabin=Get<Cabin>(game,"cabin");Check(cabin!=null&&cabin.Open,"KTX doors not open at Seoul Station");
                        float rail=Get<Transform>(game,"netVehicle").position.y;
                        Physics.SyncTransforms();int side=1;
                        foreach(int k in new[]{1,-1})
                        {
                            var p=cabin.transform.TransformPoint(new Vector3(k*(cabin.halfWidth+cabin.exitDistance),cabin.floor,0));RaycastHit h;
                            if(Physics.Raycast(p+Vector3.up*.5f,Vector3.down,out h,1.5f)&&h.point.y>rail+.4f){side=k;break;}
                        }
                        Call(game,"ExitCabin",side);
                        var feet=Get<Transform>(game,"eye").position-Vector3.up*1.65f;RaycastHit ground;
                        Check(Get<Cabin>(game,"cabin")==null&&feet.y>rail+.4f&&Physics.Raycast(feet+Vector3.up*.2f,Vector3.down,out ground,.5f),"stepping off at Seoul Station did not land on the platform (feet "+feet+", rail "+rail+")");
                        // The empty train backs out and the platform's own set comes back.
                        for(int i=0;i<(int)(60f/Step)&&game.NetRideActive;i++)Call(game,"UpdateNetRide",Step);
                        Check(!game.NetRideActive&&world.PlatformTrains.TrueForAll(t=>t==null||t.gameObject.activeSelf),"the KTX did not leave Seoul Station or the platform's own set stayed hidden");
                    }
                }

                // 5. A national-network train arriving at Jinhae keeps the rider inside the same open coach. The
                // rider then walks through its door onto the carved platform; arrival must not teleport to it.
                NetStation jinhae=TransitNetwork.Named("진해").Find(s=>s.lines.Exists(l=>l.kind!="bus"&&l.kind!="brt"));
                Check(jinhae!=null,"진해 national-network station missing");
                if(jinhae!=null)
                {
                    NetStation before=null;NetLine line=null;int direction=0;
                    foreach(var candidate in jinhae.lines)for(int d=0;d<2&&before==null;d++)foreach(var s in candidate.stops)
                    {
                        var next=StationJourney.Next(candidate,s,d,1);
                        if(!WorldBuilder.JinhaeArea.Contains(new Vector2(s.lon,s.lat))&&next.Count>1){before=s;line=candidate;direction=d;break;}
                    }
                    Check(before!=null,"no rail approach to 진해");
                    if(before!=null)
                    {
                        Call(game,"VisitNetworkStation",before,line,direction);
                        var journey=Get<StationJourney>(game,"stationJourney");
                        Check(journey!=null,"approach station opened the wrong world");if(journey==null)throw new System.InvalidOperationException("진해 approach station missing journey");
                        Call(game,"BuyTicketTo",jinhae,journey.direction);journey.clock=0;journey.Step(0);journey.doors.Set(1);
                        Call(game,"EnterCabin",journey.doors.cabin,new Vector3(0,0,0));var same=Get<Cabin>(game,"cabin");
                        for(int i=0;i<(int)(300f/Step)&&Get<string>(game,"mode")!="carved";i++)Call(game,"UpdateNetRide",Step);
                        Check(Get<string>(game,"mode")=="carved"&&Get<Cabin>(game,"cabin")==same,"arrival at 진해 did not preserve the rider in the same coach");
                        Check(same!=null&&same.Open,"진해 arrival coach doors are not open");
                        if(same!=null)
                        {
                            for(int k=0;k<180&&Get<Cabin>(game,"cabin")!=null;k++)
                            {
                                var f=Get<Vector3>(game,"cabinFeet");float door=same.doors[0];foreach(float z in same.doors)if(Mathf.Abs(z-f.z)<Mathf.Abs(door-f.z))door=z;
                                var local=Mathf.Abs(f.z-door)>.05f?new Vector3(0,0,Mathf.Clamp(door-f.z,-.075f,.075f)):new Vector3(same.doorSide*.075f,0,0);
                                Call(game,"MoveInCabin",same.transform.TransformDirection(local));if(Get<Cabin>(game,"cabin")!=null)Call(game,"CarryInCabin");
                            }
                            var feet=Get<Transform>(game,"eye").position-Vector3.up*1.65f;
                            Check(Get<Cabin>(game,"cabin")==null&&Mathf.Abs(feet.y-WorldBuilder.JinhaePlatformY)<.3f,"진해 coach could not be walked out onto the platform (feet "+feet+")");
                        }
                    }
                }
            }
            catch(System.Exception e){failures++;Debug.LogException(e);}
            Debug.Log("NetRideCheck: "+(failures==0?"passed":failures+" failed"));
            EditorApplication.Exit(failures==0?0:1);
        }
    }
}
