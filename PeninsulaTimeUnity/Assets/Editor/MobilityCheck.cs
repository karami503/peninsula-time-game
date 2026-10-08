using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PeninsulaTime
{
    public static class MobilityCheck
    {
        static int failed;
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Expect(bool ok,string message){if(!ok){failed++;Debug.LogError("MobilityCheck: "+message);}}
        static void Field(object target,string name,object value){target.GetType().GetField(name,Private).SetValue(target,value);}
        static object Call(object target,string name,params object[] args){return target.GetType().GetMethod(name,Private).Invoke(target,args);}
        public static void Run()
        {
            var camera=new GameObject("Check Camera").AddComponent<Camera>();
            var world=new GameObject("Check World").AddComponent<WorldBuilder>();world.worldCamera=camera;
            var player=new GameObject("Inactive Check Player");player.SetActive(false);
            var game=player.AddComponent<GameController>();game.state=new GameState();
            var eye=new GameObject("Check Eye").transform;Field(game,"eye",eye);game.cardBalance=0;
            for(int district=0;district<4;district++)
            {
                world.BuildDistrict(district,9);int buses=0;
                foreach(var bus in world.root.GetComponentsInChildren<TrafficVehicle>())
                {
                    if(!bus.Bus)continue;buses++;
                    Expect(bus.cabin!=null&&bus.doors!=null,"district "+district+" bus has cabin/doors");
                    if(bus.cabin==null)continue;
                    Expect(Vector3.Dot(bus.cabin.transform.forward,bus.Heading)>.99f,"cabin follows bus heading");
                    Expect(Mathf.Abs(bus.cabin.transform.lossyScale.x-1)<.02f,"cabin uses world metres");
                }
                Expect(buses>0,"district "+district+" has buses");
                var spawn=world.SafeStreetSpawn(world.FirstBusStop,world.FirstBusStopFacing);
                Expect(spawn.y<1.5f,"bus stop shortcut stays below shelter roof");
                Debug.Log("MobilityCheck district "+district+": "+buses+" boardable bus models");
            }
            // Actual controller walking transition: closed door refuses, open door boards, wall blocks.
            world.BuildRailDestination("검사역");
            var busObject=world.CityModel("BusBlue",new Vector3(60,0,0));
            var busDriver=busObject.AddComponent<TrafficVehicle>();busDriver.Bus=true;
            var doors=world.AttachBusCabin(busObject,4);busDriver.cabin=doors.cabin;busDriver.doors=doors;
            bool glass=false;foreach(var renderer in busObject.GetComponentsInChildren<Renderer>())foreach(var mat in renderer.sharedMaterials)if(mat.shader.name=="Peninsula/Glass"&&mat.color.a<.5f)glass=true;
            Expect(glass,"bus window material is transparent in player builds");
            var c=doors.cabin;var feet=c.transform.TransformPoint(new Vector3(c.halfWidth+.68f,c.floor,c.doors[0]));
            var step=c.transform.TransformDirection(Vector3.left*.15f);
            eye.position=feet+Vector3.up*1.65f;Physics.SyncTransforms();
            Expect(!(bool)Call(game,"TryEnterCabin",step),"closed door prevents entry");
            doors.Set(1);Physics.SyncTransforms();
            Expect((bool)Call(game,"TryEnterCabin",step),"walking through open bus door boards");
            Expect((Cabin)typeof(GameController).GetField("cabin",Private).GetValue(game)==c,"player attached to cabin");
            Expect(game.cardBalance==0,"zero-balance bus boarding does not charge");
            Call(game,"ExitCabin",1);eye.position=feet+Vector3.up*1.65f;
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=feet+Vector3.up*.9f+step;wall.transform.localScale=new Vector3(.12f,1.8f,2);
            Physics.SyncTransforms();Expect(!(bool)Call(game,"TryEnterCabin",step),"wall blocks remote boarding");Object.DestroyImmediate(wall);
            // All network rail stations have a route sequence, including terminal reverse directions.
            TransitNetwork.Build(null);int stations=0;
            foreach(var s in TransitNetwork.Stations)
                foreach(var line in s.lines)
                    if(line.kind=="metro"||line.kind=="ktx"||line.kind=="mugunghwa")
                    {var stops=StationJourney.Next(line,s,0);Expect(stops.Count>=1&&stops.Count<=4&&stops[0]==s,"valid station route");stations++;break;}
            Expect(stations>500,"nationwide station dataset loaded");
            var serviceLine=TransitNetwork.Lines.Find(l=>l.kind=="ktx"&&l.stops.Count>=4);
            var service=world.BuildNetworkStation(serviceLine.stops[0],serviceLine,0);
            Expect(service.doors.cabin.Open,"station train initially open");
            service.Board();int arrivals=0,last=0;
            for(int i=0;i<5000&&!service.Finished;i++)
            {
                float before=service.train.localPosition.z;service.Step(.05f);
                Expect(service.train.localPosition.z-before<=TransitSpeed.JourneyCruiseSpeed(service.line.kind)*.05f+.02f,"continuous journey without teleport");
                if(service.speed>.1f)Expect(service.doors.amount<.01f,"moving train doors closed");
                if(service.index!=last){arrivals++;last=service.index;}
            }
            service.Step(1);
            Expect(arrivals==3&&service.Stopped,"three intermediate stations reached with open final doors");
            var saved=DayCycle.Seconds;DayCycle.Seconds=599.9f;DayCycle.Advance(.1f);Expect(DayCycle.Night,"night starts at 600 seconds");
            DayCycle.Advance(600);Expect(!DayCycle.Night,"day restarts after 600 night seconds");DayCycle.Seconds=saved;
            Debug.Log("MobilityCheck: "+stations+" rail stations; "+(failed==0?"passed":failed+" failed"));
            Object.DestroyImmediate(player);Object.DestroyImmediate(world.gameObject);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(eye.gameObject);
            EditorApplication.Exit(failed==0?0:1);
        }
    }
}
