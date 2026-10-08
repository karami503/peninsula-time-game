using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Batch check for 3D street life: people walk along sidewalks, and doors, lamps, people and vehicles
    // can be hit by a click ray and react.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.InteractionCheck.Run
    public static class InteractionCheck
    {
        static int failed;
        static void Expect(bool condition,string message){if(!condition){failed++;Debug.LogError("InteractionCheck failed: "+message);}}
        static T ClickAt<T>(Vector3 target) where T:Interactable
        {
            Physics.SyncTransforms();
            for(int i=0;i<8;i++)
            {
                float angle=i*Mathf.PI*.25f;
                var from=target+new Vector3(Mathf.Sin(angle)*6f,1.5f,Mathf.Cos(angle)*6f);RaycastHit hit;
                if(Physics.Raycast(from,(target-from).normalized,out hit,12f))
                {
                    var clicked=hit.collider.GetComponentInParent<T>();if(clicked!=null)return clicked;
                }
            }
            return null;
        }
        public static void Run()
        {
            failed=0;
            var camera=new GameObject("Check Camera").AddComponent<Camera>();
            var world=new GameObject("Check World").AddComponent<WorldBuilder>();world.worldCamera=camera;
            var state=new GameState();
            state.buildings.Add(new PlacedBuilding{id="house",city=state.selectedCity});
            for(int column=0;column<6;column++)state.roads.Add(new RoadSegment{city=state.selectedCity,axis=0,row=2,column=column});
            for(int row=0;row<4;row++)state.roads.Add(new RoadSegment{city=state.selectedCity,axis=1,row=row,column=3});
            world.BuildCityPlot(state);
            var director=world.root.GetComponentInChildren<TrafficDirector>();
            Expect(director!=null&&director.Walkers.Count>=4,"plot has walking people ("+(director!=null?director.Walkers.Count:0)+")");

            // People keep to the sidewalk beside the road and move.
            var walker=director.Walkers[0];var last=walker.transform.position;float distanceWalked=0;
            for(int i=0;i<200;i++){director.Step(.05f);distanceWalked+=Vector3.Distance(last,walker.transform.position);last=walker.transform.position;}
            Expect(distanceWalked>3f,"person walked ("+distanceWalked+" m)");
            var p=walker.transform.position;float fromRoad=Mathf.Min(Mathf.Abs(p.z),Mathf.Abs(p.x));
            Expect(fromRoad>3.6f&&fromRoad<6f,"person walks on the sidewalk beside the 7.2 m road ("+fromRoad+" m)");

            var door=Object.FindAnyObjectByType<DoorInteract>();
            Expect(door!=null,"house has a door");
            if(door!=null)
            {
                Expect(ClickAt<DoorInteract>(door.transform.position+Vector3.up)==door,"door can be clicked");
                var leaf=door.transform.Find("DoorLeaf model");var before=leaf.rotation;
                door.Toggle();for(int i=0;i<30;i++)door.Swing(.05f);
                Expect(door.IsOpen&&Quaternion.Angle(before,leaf.rotation)>80f,"door swings open ("+Quaternion.Angle(before,leaf.rotation)+"°)");
                Expect(door.hasInterior&&Object.FindAnyObjectByType<InteriorExit>()!=null,"building has a walkable interior and an exit");
                int shopCount=0,leisureCount=0;foreach(var f in world.root.GetComponentsInChildren<Fixture>()){if(f.kind=="shop")shopCount++;if(f.kind=="leisure")leisureCount++;}
                Expect(shopCount>0&&leisureCount>0,"interior has shopping and leisure fixtures");
            }
            var lamp=Object.FindAnyObjectByType<StreetLamp>();
            Expect(lamp!=null&&lamp.head.y>lamp.transform.position.y,"street lamp has a light head");

            var person=director.Walkers[1];
            Expect(ClickAt<Pedestrian>(person.transform.position+Vector3.up)==person,"person can be clicked");
            string line=person.Talk(Vector3.zero,80,"서울",false);var stand=person.transform.position;director.Step(1f);
            Expect(line.Length>0&&person.Talking&&Vector3.Distance(stand,person.transform.position)<.01f,"person stops to talk");

            var car=director.Vehicles[0];
            Expect(ClickAt<VehicleInteract>(car.GetComponent<Collider>().bounds.center)!=null,"vehicle can be clicked to board");
            Expect(car.Heading.sqrMagnitude>.9f,"vehicle reports heading for the ride camera");
            car.TakeWheel();var parked=car.transform.position;director.Step(1f);
            Expect(car.PlayerControlled&&Vector3.Distance(parked,car.transform.position)<.01f,"player car no longer drives itself");
            car.transform.position=new Vector3(100,.06f,100);Physics.SyncTransforms();
            var drivingStart=car.transform.position;
            for(int i=0;i<20;i++)car.ManualDrive(1f,0f,.1f);
            Expect(Vector3.Distance(drivingStart,car.transform.position)>1f,"W accelerates the car under player control");

            world.BuildDistrict(0,9);
            int crowd=0;foreach(var d in world.root.GetComponentsInChildren<TrafficDirector>())crowd+=d.Walkers.Count;
            Expect(crowd>=50,"Seoul district has crowds ("+crowd+")");
            // Street walkers use sidewalks and zebra crossings, not the carriageway.
            Physics.SyncTransforms();int samples=0,onCarriageway=0;
            foreach(var d in world.root.GetComponentsInChildren<TrafficDirector>())
            {
                if(d.Walkers.Count==0||d.transform.position.y<-1f)continue;
                for(int k=0;k<10;k++)
                {
                    for(int i=0;i<40;i++)d.Step(.1f);
                    foreach(var w in d.Walkers)
                    {
                        RaycastHit ground;var at=w.transform.position;if(at.y<-1f||at.x>1000f)continue;
                        if(!Physics.Raycast(at+Vector3.up*3f,Vector3.down,out ground,6f,~0,QueryTriggerInteraction.Ignore))continue;
                        samples++;string n=ground.collider.name.ToLowerInvariant();if(n=="road"||n=="busway")onCarriageway++;
                    }
                }
            }
            Expect(samples>200&&onCarriageway<=samples*.12f,"street walkers off the carriageway ("+onCarriageway+"/"+samples+" on road, crossings included)");
            Debug.Log("InteractionCheck: walkers on carriageway "+onCarriageway+"/"+samples);
            CheckCollisions(world);
            foreach(var type in new[]{"brt","metro","ktx"})
            {
                int before=world.root.transform.childCount;
                world.BuildRideInfrastructure(type);
                Expect(world.root.transform.childCount>before,type+" has distinct 3D infrastructure");
                var transit=world.CreateVehicle(type,new Vector3(0,WorldBuilder.RideHeight(type),0));
                Expect(transit!=null,type+" has a vehicle");
                if(transit!=null){world.CreateRideCabin(transit,type);Expect(transit.transform.childCount>0,type+" has a cabin");}
            }
            world.BuildRailDestination("상주");
            Expect(world.ArrivalCabin!=null&&world.ArrivalCabin.Open,"KTX arrives with an open walkable cabin");
            Debug.Log("InteractionCheck: "+(failed==0?"passed":failed+" failed"));
            EditorApplication.Exit(failed==0?0:1);
        }
        // Cars stop for people and people keep apart: over a minute of district traffic, sample how often a
        // car body stands on a person and how often two people overlap (closer than shoulder to shoulder).
        static void CheckCollisions(WorldBuilder world)
        {
            var directors=world.root.GetComponentsInChildren<TrafficDirector>();
            int samples=0,runOver=0,pairs=0,overlapping=0;float driven=0,walked=0;
            var carsBefore=new System.Collections.Generic.Dictionary<TrafficVehicle,Vector3>();
            var peopleBefore=new System.Collections.Generic.Dictionary<Pedestrian,Vector3>();
            for(int step=0;step<600;step++)
            {
                foreach(var car in TrafficVehicle.Active)carsBefore[car]=car.transform.position;
                foreach(var person in Pedestrian.Walking)peopleBefore[person]=person.transform.position;
                foreach(var d in directors)d.Step(.1f);
                foreach(var car in TrafficVehicle.Active)driven+=Vector3.Distance(carsBefore[car],car.transform.position);
                foreach(var person in Pedestrian.Walking)walked+=Vector3.Distance(peopleBefore[person],person.transform.position);
                if(step%5!=0)continue;
                var people=Pedestrian.Walking;
                foreach(var person in people)
                {
                    var at=person.transform.position;if(at.y<-1f)continue;
                    samples++;
                    foreach(var car in TrafficVehicle.Active)if(car.Covers(at,-.15f)){runOver++;break;}
                }
                for(int i=0;i<people.Count;i++)for(int j=i+1;j<people.Count;j++)
                {
                    var a=people[i].transform.position;var b=people[j].transform.position;
                    if(Mathf.Abs(a.y-b.y)>1.5f)continue;
                    a.y=b.y=0;if(Vector3.Distance(a,b)>3f)continue;
                    pairs++;if(Vector3.Distance(a,b)<.35f)overlapping++;
                }
            }
            float carSpeed=driven/Mathf.Max(1,TrafficVehicle.Active.Count)/60f,walkSpeed=walked/Mathf.Max(1,Pedestrian.Walking.Count)/60f;
            Debug.Log("InteractionCheck: people under cars "+runOver+"/"+samples+", overlapping pairs "+overlapping+"/"+pairs+", mean car speed "+carSpeed.ToString("F2")+" m/s, walking "+walkSpeed.ToString("F2")+" m/s");
            Expect(carSpeed>1.5f&&walkSpeed>.6f,"traffic and people keep moving while giving way");
            Expect(samples>1000&&runOver<=samples*.002f,"cars stop for people ("+runOver+"/"+samples+" under a car)");
            Expect(pairs>100&&overlapping<=pairs*.03f,"people do not walk through each other ("+overlapping+"/"+pairs+" close pairs overlap)");
        }
    }
}
