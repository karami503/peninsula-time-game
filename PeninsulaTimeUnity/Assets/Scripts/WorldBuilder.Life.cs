using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Street life for the 3D views: walking people, doors that open, lamps that switch on, vehicles to board.
    public partial class WorldBuilder
    {
        public static readonly string[] DoorBuildings={"house","hanok","apartment","school","workshop","factory"};
        const float HipHeight=.9f,ShoulderHeight=1.44f,HipSpread=.09f,ShoulderSpread=.27f,WalkSpeed=1.3f;
        static readonly Color[] Shirts={JinhaeDesign.Slate,JinhaeDesign.Timber,JinhaeDesign.Cream,JinhaeDesign.Foliage,JinhaeDesign.Tactile,JinhaeDesign.Tile,JinhaeDesign.Ink};
        static readonly Color[] Trousers={JinhaeDesign.Ink,JinhaeDesign.Timber,JinhaeDesign.Slate,JinhaeDesign.Granite};
        public static int ShirtCount{get{return Shirts.Length;}} public static int TrouserCount{get{return Trousers.Length;}}
        public static Color ShirtColor(int index){return Shirts[Mathf.Abs(index)%Shirts.Length];}
        public static Color TrouserColor(int index){return Trousers[Mathf.Abs(index)%Trousers.Length];}

        static Bounds RendererBounds(GameObject o)
        {
            var renderers=o.GetComponentsInChildren<Renderer>();
            var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);return b;
        }
        static void FitBoxCollider(GameObject o)
        {
            var b=RendererBounds(o);var box=o.AddComponent<BoxCollider>();
            box.center=o.transform.InverseTransformPoint(b.center);
            var size=o.transform.InverseTransformVector(b.size);box.size=new Vector3(Mathf.Abs(size.x),Mathf.Abs(size.y),Mathf.Abs(size.z));
        }
        void Tint(GameObject part,Color shirt,Color trousers)
        {
            Material shirtMat,trouserMat;materials.TryGetValue("city-shirt",out shirtMat);materials.TryGetValue("city-pants",out trouserMat);
            foreach(var renderer in part.GetComponentsInChildren<Renderer>())
            {
                var shared=renderer.sharedMaterials;
                for(int i=0;i<shared.Length;i++)
                {
                    if(shared[i]!=shirtMat&&shared[i]!=trouserMat)continue;
                    var block=new MaterialPropertyBlock();block.SetColor("_Color",shared[i]==shirtMat?shirt:trousers);
                    renderer.SetPropertyBlock(block,i);
                }
            }
        }
        public void TintPerson(GameObject person,int shirt,int trousers){if(person!=null)Tint(person,ShirtColor(shirt),TrouserColor(trousers));}
        // A person assembled from the Blender parts, facing +Z, feet at the root.
        public Pedestrian CreatePerson(Vector3 position,float scale,System.Random random,out Transform[] legs,out Transform[] arms)
        {
            legs=arms=null;
            var person=new GameObject("Person");person.transform.SetParent(root.transform,false);
            var body=CityModel("PersonBody",new Vector3(0,HipHeight,0));
            if(body==null){DestroyImmediate(person);return null;}
            body.transform.SetParent(person.transform,false);
            legs=new Transform[2];arms=new Transform[2];
            for(int i=0;i<2;i++)
            {
                float s=i==0?-1:1;
                var leg=CityModel("PersonLeg",new Vector3(s*HipSpread,HipHeight,0));leg.transform.SetParent(person.transform,false);legs[i]=leg.transform;
                var arm=CityModel("PersonArm",new Vector3(s*ShoulderSpread,ShoulderHeight,0));arm.transform.SetParent(person.transform,false);arms[i]=arm.transform;
            }
            Tint(person,Shirts[random.Next(Shirts.Length)],Trousers[random.Next(Trousers.Length)]);
            var capsule=person.AddComponent<CapsuleCollider>();capsule.center=new Vector3(0,.9f,0);capsule.height=1.8f;capsule.radius=.3f;
            person.transform.localScale=Vector3.one*scale;person.transform.position=position;
            return person.AddComponent<Pedestrian>();
        }
        // Riders in a subway car: some seated on the long benches facing the aisle, some standing.
        // `ahead` is the car's direction of travel; the floor is at floorY.
        public void AddCabinPassengers(Transform car,Vector3 ahead,float floorY,int seed)
        {
            var random=new System.Random(seed);var across=Vector3.Cross(Vector3.up,ahead);
            System.Action<Vector3,Vector3,bool> rider=(at,facing,seated)=>
            {
                Transform[] legs;var go=Bystander(at,facing,car,random,out legs);
                if(go==null||!seated)return;
                go.transform.position=new Vector3(at.x,floorY+.5f-HipHeight,at.z)+facing*.15f; // hips on the bench
                // Thighs forward along the bench, shins down from the knees: each half a standing leg.
                foreach(var leg in legs)
                {
                    var hip=leg.localPosition;var shin=Instantiate(leg.gameObject,leg.parent).transform;
                    HalfLeg(leg,hip,Quaternion.AngleAxis(-90,Vector3.right));
                    HalfLeg(shin,hip+Vector3.forward*HipHeight*.5f,Quaternion.identity);
                }
            };
            var centre=new Vector3(car.position.x,floorY,car.position.z);
            foreach(float along in new[]{-7f,-4.5f,1.5f,6.5f})foreach(float side in new[]{-1f,1f})
                if(random.Next(3)>0)rider(centre+ahead*along+across*side*.95f,-across*side,true);
            // Standing riders by the doors, clear of the view down the aisle.
            foreach(float along in new[]{-2.5f,4f})rider(centre+ahead*along+across*(random.Next(2)==0?-.62f:.62f),ahead*(random.Next(2)==0?1:-1),false);
        }
        // A person who stays put (no walking, no collider): riders, queues at counters.
        public GameObject Bystander(Vector3 at,Vector3 facing,Transform parent,System.Random random,out Transform[] legs)
        {
            Transform[] arms;var person=CreatePerson(at,1f,random,out legs,out arms);if(person==null)return null;
            var go=person.gameObject;DestroyImmediate(person);DestroyImmediate(go.GetComponent<Collider>());
            go.transform.rotation=Quaternion.LookRotation(facing);go.transform.SetParent(parent,true);return go;
        }
        // Hangs a leg model from a joint at `at` (person space), squashed to half its length.
        static void HalfLeg(Transform leg,Vector3 at,Quaternion bend)
        {
            var joint=new GameObject("Leg joint").transform;joint.SetParent(leg.parent,false);
            joint.localPosition=at;joint.localRotation=bend;joint.localScale=new Vector3(1,.5f,1);
            leg.SetParent(joint,false);leg.localPosition=Vector3.zero;
        }
        public void SpawnPeople(TrafficDirector director,int count,float height,float sideOffset,float scale)
        {
            if(director==null||count<=0)return;
            var graph=director.graph;var starts=new List<int>();
            for(int i=0;i<graph.nodes.Count;i++)if(graph.links[i].Count>0)starts.Add(i);
            if(starts.Count==0)return;
            var random=new System.Random(count*17+starts.Count);
            for(int k=0;k<count;k++)
            {
                Transform[] legs,arms;
                var walker=CreatePerson(Vector3.zero,scale,random,out legs,out arms);
                if(walker==null)return;
                walker.Begin(graph,starts[random.Next(starts.Count)],WalkSpeed*scale*(.8f+(float)random.NextDouble()*.4f),sideOffset,height,random.Next(),legs,arms);
                director.AddWalker(walker);
            }
        }
        // Finds the street-facing wall (-Z) by ray casting against the model and hangs a door there.
        public DoorInteract AddDoor(GameObject building)
        {
            if(building==null)return null;
            var colliders=new List<MeshCollider>();
            var temporary=new List<MeshCollider>();
            foreach(var filter in building.GetComponentsInChildren<MeshFilter>())
                if(filter.sharedMesh!=null&&filter.sharedMesh.isReadable)
                {var c=filter.GetComponent<MeshCollider>();if(c==null){c=filter.gameObject.AddComponent<MeshCollider>();c.sharedMesh=filter.sharedMesh;temporary.Add(c);}colliders.Add(c);}
            var b=RendererBounds(building);
            var ray=new Ray(new Vector3(b.center.x,building.transform.position.y+1.3f,b.min.z-1f),Vector3.forward);
            RaycastHit best=new RaycastHit();bool found=false;
            foreach(var c in colliders){RaycastHit hit;if(c.Raycast(ray,out hit,b.size.z+2f)&&(!found||hit.distance<best.distance)){best=hit;found=true;}}
            foreach(var c in temporary)DestroyImmediate(c);
            if(!found)return null;
            var spot=new Vector3(best.point.x,building.transform.position.y,best.point.z-.36f);
            var door=new GameObject("Door");door.transform.SetParent(root.transform,false);door.transform.position=spot;
            var frame=CityModel("DoorFrame",spot);var leaf=CityModel("DoorLeaf",spot);
            if(frame==null||leaf==null){DestroyImmediate(door);return null;}
            frame.transform.SetParent(door.transform,true);leaf.transform.SetParent(door.transform,true);
            // The leaf's origin is its hinge: slide it so the leaf centres in the frame, then swing it inward (+Z).
            float offset=RendererBounds(leaf).center.x-spot.x;
            leaf.transform.position-=new Vector3(offset,0,0);
            var box=door.AddComponent<BoxCollider>();box.center=new Vector3(0,1.1f,0);box.size=new Vector3(1.2f,2.2f,.5f);
            var interact=door.AddComponent<DoorInteract>();interact.Init(leaf.transform,offset>0?-1f:1f);
            AddInterior(interact,spot);
            return interact;
        }
        void MakeLampInteractive(GameObject lamp)
        {
            if(lamp==null)return;
            var b=RendererBounds(lamp);var foot=lamp.transform.position;
            var reach=new Vector3(b.center.x-foot.x,0,b.center.z-foot.z);
            // Only the pole is solid and clickable, so the arm overhead does not catch clicks meant for things past it.
            float height=b.max.y-foot.y;
            var pole=new GameObject("가로등 기둥").transform;pole.position=foot+Vector3.up*height*.5f;pole.SetParent(lamp.transform,true);
            pole.gameObject.AddComponent<BoxCollider>().size=new Vector3(.4f,height,.4f);
            lamp.AddComponent<StreetLamp>().head=foot+reach*2f+Vector3.up*(b.max.y-foot.y-.4f);
        }
        void MakeVehicleInteractive(GameObject vehicle,bool bus)
        {
            FitBoxCollider(vehicle);
            vehicle.AddComponent<VehicleInteract>().bus=bus;
        }
    }
}
