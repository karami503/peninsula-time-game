using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeninsulaTime
{
    // Walkable insides for buses, subway trains and KTX sets: a level frame on each vehicle (x across, z ahead,
    // unit scale) carries the Cabin, the sliding door leaves and a few riders. Sizes match make_city_models.py.
    public partial class WorldBuilder
    {
        // City bus (11 m, low floor): doors on the right (kerb) side, by the driver and behind the middle.
        public const float BusFloor=.38f,BusInner=1.12f;
        static readonly float[] BusDoors={4.55f,-1.0f};
        static readonly Vector2[] BusSeats={new Vector2(-.78f,3.2f),new Vector2(-.78f,2.35f),new Vector2(-.78f,1.5f),new Vector2(-.78f,.65f),new Vector2(-.78f,-.2f),
            new Vector2(-.78f,-1.05f),new Vector2(-.78f,-1.9f),new Vector2(-.78f,-2.75f),new Vector2(-.78f,-3.6f),new Vector2(.78f,2.9f),new Vector2(.78f,2.05f),
            new Vector2(.78f,-2.4f),new Vector2(.78f,-3.25f),new Vector2(.78f,-4.1f),new Vector2(0,-4.75f)};
        // Subway car 19.5 m long and 3.1 m wide; four make a TrainConsist; door openings 1.3 m.
        public const float MetroFloor=.96f,MetroInner=1.47f;
        // KTX saloon car between the two power cars: doors at both ends, on both sides.
        public const float KtxFloor=1.0f,KtxInner=1.38f;
        static readonly float[] KtxDoors={-8.5f,8.5f};

        // A child of the vehicle that is level, faces the vehicle's front (+Z at yaw 0) and has unit scale.
        static Transform Frame(GameObject vehicle,float yaw=0)
        {
            var frame=new GameObject("객실").transform;frame.SetParent(vehicle.transform,false);
            var driver=vehicle.GetComponent<TrafficVehicle>();
            var rail=vehicle.GetComponent<RailVehicle>();
            var heading=driver!=null?driver.Heading:rail!=null?rail.Direction:Quaternion.Euler(0,yaw,0)*Vector3.forward;
            heading.y=0;
            frame.rotation=Quaternion.LookRotation(heading.sqrMagnitude>.01f?heading:Vector3.forward);frame.position=vehicle.transform.position;
            float s=vehicle.transform.lossyScale.x;if(s>1e-4f)frame.localScale=Vector3.one/s;
            return frame;
        }
        // A sliding door leaf in a frame: a panel with a window, optionally a line-colour band.
        Transform Leaf(Transform frame,Vector3 centre,float height,float width,Material body,Material glass,Material band=null)
        {
            var leaf=new GameObject("출입문").transform;leaf.SetParent(frame,false);leaf.localPosition=centre;
            System.Action<Vector3,Vector3,Material> part=(at,size,m)=>
            {var p=Primitive(PrimitiveType.Cube,"문짝",leaf,at,size,m);DestroyImmediate(p.GetComponent<Collider>());p.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;};
            part(new Vector3(0,-height*.32f,0),new Vector3(.04f,height*.36f,width),body);
            part(new Vector3(0,height*.4f,0),new Vector3(.04f,height*.2f,width),body);
            foreach(float e in new[]{-1f,1f})part(new Vector3(0,height*.08f,e*width*.42f),new Vector3(.04f,height*.44f,width*.16f),body);
            part(new Vector3(0,height*.08f,0),new Vector3(.03f,height*.44f,width*.7f),glass);
            if(band!=null)part(new Vector3(.005f,-height*.23f,0),new Vector3(.045f,.16f,width),band);
            return leaf;
        }
        Material VehicleGlass()
        {
            var m=Mat("vehicle-window-transparent",new Color(.30f,.42f,.48f,.23f),.15f);
            var shader=Shader.Find("Peninsula/Glass");if(shader!=null)m.shader=shader;
            m.color=new Color(.30f,.42f,.48f,.23f);m.renderQueue=3000;
            return m;
        }
        // A rider at a local spot in a vehicle frame: seated (hips on a seat `seat` metres above the floor) or standing.
        void Rider(Transform frame,Vector3 local,Vector3 facing,bool seated,System.Random random,float floor,float seat=.45f)
        {
            Transform[] legs;
            var face=frame.TransformDirection(facing);
            var go=Bystander(frame.TransformPoint(new Vector3(local.x,floor,local.z)),face,frame,random,out legs);
            if(go==null||!seated)return;
            go.transform.position=frame.TransformPoint(new Vector3(local.x,floor+seat-HipHeight,local.z))+face*.15f;
            // Thighs forward along the seat, shins down from the knees: each half a standing leg.
            foreach(var leg in legs)
            {
                var hip=leg.localPosition;var shin=Instantiate(leg.gameObject,leg.parent).transform;
                HalfLeg(leg,hip,Quaternion.AngleAxis(-90,Vector3.right));
                HalfLeg(shin,hip+Vector3.forward*HipHeight*.5f,Quaternion.identity);
            }
        }

        // ---------- bus ----------
        // Seoul bus colours: blue trunk (3 digits), green branch (4 digits, 마을버스), red express (9xxx, M, G), yellow circular (2 digits).
        public static string BusModel(string route)
        {
            var digits=new string(System.Array.FindAll(route.ToCharArray(),char.IsDigit));
            if(route.StartsWith("M")||route.StartsWith("G")||(digits.Length==4&&digits[0]=='9'))return "BusRed";
            if(digits.Length==3&&digits==route)return "BusBlue";
            if(digits.Length==2&&digits==route)return "BusYellow";
            return "Bus";
        }
        public VehicleDoors AttachBusCabin(GameObject bus,int seed)
        {
            var frame=Frame(bus);
            // Renderer bounds included mirrors and stopped walkers outside the open-door threshold.
            foreach(var old in bus.GetComponents<Collider>())DestroyImmediate(old);
            var bodyCollider=frame.gameObject.AddComponent<BoxCollider>();
            bodyCollider.center=new Vector3(0,1.6f,0);bodyCollider.size=new Vector3(2.5f,3.2f,11f);
            var cabin=frame.gameObject.AddComponent<Cabin>();
            cabin.kind="bus";cabin.halfWidth=BusInner;cabin.back=-5.2f;cabin.front=5.05f;cabin.floor=BusFloor;
            cabin.doors=BusDoors;cabin.doorHalf=.5f;cabin.doorSide=1;cabin.aisle=.4f;cabin.exitDistance=.8f;
            var doors=frame.gameObject.AddComponent<VehicleDoors>();doors.cabin=cabin;
            var body=Mat("bus-door",new Color(.22f,.24f,.26f),.3f);var glass=VehicleGlass();
            foreach(float d in BusDoors)foreach(float half in new[]{-1f,1f})
                doors.Add(Leaf(frame,new Vector3(1.21f,BusFloor+1.0f,d+half*.27f),2.0f,.53f,body,glass),new Vector3(.12f,0,half*.5f));
            doors.Set(0);cabin.Register();
            var random=new System.Random(seed);
            foreach(var seatAt in BusSeats)if(random.Next(4)==0)Rider(frame,new Vector3(seatAt.x,0,seatAt.y),Vector3.forward,true,random,BusFloor);
            return doors;
        }
        // LED route number on the front, back and kerb side of a bus.
        public void RouteSign(GameObject bus,string route)
        {
            var frame=bus.transform.Find("객실");if(frame==null)return;
            var amber=new Color(1f,.62f,.12f);
            foreach(float end in new[]{1f,-1f})
                Sign(route,frame,frame.TransformPoint(new Vector3(0,2.78f,end*5.54f)),frame.TransformDirection(Vector3.forward*end),.28f,amber).name="노선 번호";
            Sign(route,frame,frame.TransformPoint(new Vector3(1.27f,2.62f,2.0f)),frame.TransformDirection(Vector3.right),.2f,amber).name="노선 번호";
        }

        // ---------- subway ----------
        // Four cars with their door leaves (opening toward the island), the walkable inside and a few riders.
        TrainConsist BuildConsist(SubwayTrain train,string name,float sx,Material band,int seed)
        {
            var consistRoot=new GameObject(name).transform;consistRoot.SetParent(train.transform,false);
            var consist=consistRoot.gameObject.AddComponent<TrainConsist>();consist.train=train;
            var body=Mat("metro-door",new Color(.82f,.84f,.85f),.25f);var glass=VehicleGlass();
            var doors=new List<float>();float open=-sx;
            for(int k=0;k<4;k++)
            {
                float z=SubwayTrain.CarCentres[k];
                var car=CityModel("Metro",Vector3.zero,1f,sx>0?0:180);
                if(car!=null){car.transform.SetParent(consistRoot,false);car.transform.localPosition=new Vector3(0,0,z);}
                if((sx>0&&k==3)||(sx<0&&k==0))consist.lead=car!=null?car.transform:consistRoot;
                // Line-colour band below the windows, broken by the door openings.
                float from=z-9.75f;
                foreach(float d in SubwayTrain.DoorOffsets){Band(consistRoot,from,z+d-.66f,band);from=z+d+.66f;}
                Band(consistRoot,from,z+9.75f,band);
                foreach(float d in SubwayTrain.DoorOffsets)
                {
                    doors.Add(z+d);
                    foreach(float s in new[]{-1f,1f})foreach(float half in new[]{-1f,1f})
                    {
                        var leaf=Leaf(consistRoot,new Vector3(s*1.51f,MetroFloor+.95f,z+d+half*.325f),1.9f,.64f,body,glass,band);
                        consist.sideLeaves.Add(leaf);consist.leafSides.Add(s);consist.leafRest.Add(leaf.localPosition);consist.leafSlides.Add(new Vector3(0,0,half*.62f));
                        if(s!=open)continue;
                        consist.doorLeaves.Add(leaf);consist.doorClosed.Add(leaf.localPosition);consist.doorOpen.Add(leaf.localPosition+new Vector3(0,0,half*.62f));
                    }
                }
            }
            var cabinObject=new GameObject("객실");cabinObject.transform.SetParent(consistRoot,false);
            var cabin=cabinObject.AddComponent<Cabin>();
            cabin.kind="metro";cabin.halfWidth=MetroInner;cabin.back=-39.6f;cabin.front=39.6f;cabin.floor=MetroFloor;
            cabin.doors=doors.ToArray();cabin.doorHalf=.62f;cabin.doorSide=(int)open;cabin.aisle=.85f;cabin.exitDistance=.55f;cabin.Register();
            consist.cabin=cabin;
            var box=consistRoot.gameObject.AddComponent<BoxCollider>();box.center=new Vector3(0,1.95f,0);box.size=new Vector3(3.1f,2.9f,79.5f);
            var random=new System.Random(seed);
            for(int k=0;k<4;k++)
            {
                float z=SubwayTrain.CarCentres[k];
                foreach(float bay in new[]{-5.04f,0f,5.04f})foreach(float s in new[]{-1f,1f})
                    if(random.Next(4)==0)Rider(consistRoot,new Vector3(s*1.12f,0,z+bay+((float)random.NextDouble()-.5f)*2.4f),new Vector3(-s,0,0),true,random,MetroFloor);
                if(random.Next(3)==0)Rider(consistRoot,new Vector3(random.Next(2)*1f-.5f,0,z+SubwayTrain.DoorOffsets[random.Next(4)]+.95f),new Vector3(0,0,random.Next(2)*2-1),false,random,MetroFloor);
            }
            consist.Place(SubwayTrain.Run+60f,0,train.direction);
            return consist;
        }
        void Band(Transform parent,float z0,float z1,Material band)
        {
            if(z1-z0<.05f)return;
            foreach(float edge in new[]{-1.565f,1.565f})
            {
                var stripe=Primitive(PrimitiveType.Cube,"노선색 띠",parent,new Vector3(edge,1.35f,(z0+z1)*.5f),new Vector3(.03f,.32f,z1-z0),band);
                DestroyImmediate(stripe.GetComponent<Collider>());stripe.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            }
        }

        // ---------- KTX ----------
        public VehicleDoors AttachKtxCabin(GameObject train,int seed)
        {
            var frame=Frame(train);
            var cabin=frame.gameObject.AddComponent<Cabin>();
            cabin.kind="ktx";cabin.halfWidth=KtxInner;cabin.back=-9.2f;cabin.front=9.2f;cabin.floor=KtxFloor;
            cabin.doors=KtxDoors;cabin.doorHalf=.5f;cabin.doorSide=0;cabin.aisle=.38f;cabin.exitDistance=.75f;
            var doors=frame.gameObject.AddComponent<VehicleDoors>();doors.cabin=cabin;
            var body=Mat("ktx-door",new Color(.92f,.93f,.94f),.2f);var glass=VehicleGlass();
            foreach(float d in KtxDoors)foreach(float s in new[]{-1f,1f})
                doors.Add(Leaf(frame,new Vector3(s*1.44f,KtxFloor+.98f,d),1.95f,.98f,body,glass,Mat("ktx-band",new Color(.10f,.30f,.64f))),new Vector3(s*.06f,0,(d>0?-1:1)*1.0f));
            doors.Set(0);cabin.Register();
            var random=new System.Random(seed);
            for(float z=-7.2f;z<=7.2f;z+=1.0f)foreach(float x in new[]{-1.0f,-.55f,.55f,1.0f})
                if(random.Next(5)==0)Rider(frame,new Vector3(x,0,z),Vector3.forward,true,random,KtxFloor);
            return doors;
        }
    }
}
