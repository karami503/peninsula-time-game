using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Batch check for transit operation rules and separately playable former cities.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.TransitCheck.Run
    public static class TransitCheck
    {
        static int failed;
        static void Expect(bool condition,string message){if(!condition){failed++;Debug.LogError("TransitCheck failed: "+message);}}
        static TrafficDirector Director(TrafficGraph graph)
        {
            var red=new Material(Shader.Find("Standard"));
            var director=new GameObject("Check traffic").AddComponent<TrafficDirector>();
            director.Init(graph,red,red,red);
            return director;
        }
        static TrafficVehicle Car(TrafficDirector director,int seed)
        {
            var car=new GameObject("Check car").AddComponent<TrafficVehicle>();
            car.transform.SetParent(director.transform);
            car.Begin(director.graph,0,10f,0f,4.2f,seed);director.Add(car);return car;
        }
        static void CheckTraffic()
        {
            // Loop road without junctions: cars keep driving, stay on the 8 m road (corners are cut on curves), never close up.
            var loop=TrafficGraph.FromRoadText("8 0,0 50,0 50,50 0,50 0,0");
            Expect(loop.nodes.Count==4,"road text builds 4 nodes ("+loop.nodes.Count+")");
            var director=Director(loop);
            var cars=new[]{Car(director,1),Car(director,2),Car(director,3)};
            float travelled=0,closest=float.MaxValue;
            for(int i=0;i<600;i++)
            {
                var before=cars[0].transform.position;director.Step(.05f);travelled+=Vector3.Distance(before,cars[0].transform.position);
                foreach(var car in cars)
                {
                    var p=car.transform.position;
                    float offRoad=Mathf.Min(Mathf.Min(Mathf.Abs(p.x),Mathf.Abs(p.x-50)),Mathf.Min(Mathf.Abs(p.z),Mathf.Abs(p.z-50)));
                    if(offRoad>4f){Expect(false,"vehicle left the 8 m road at "+p);i=600;break;}
                    foreach(var other in cars)if(other!=car&&other.From==car.From&&other.To==car.To)closest=Mathf.Min(closest,Mathf.Abs(other.Travelled-car.Travelled));
                }
            }
            Expect(travelled>60f,"lead vehicle drove "+travelled+" m in 30 s");
            Expect(closest>=4.2f+TrafficVehicle.Clearance-.6f||closest==float.MaxValue,"vehicles kept apart, closest "+closest+" m");
            Object.DestroyImmediate(director.gameObject);

            // Crossroads: a car never enters the junction while its light is not green.
            var cross=TrafficGraph.FromRoadText("8 -60,0 0,0 60,0\n8 0,-60 0,0 0,60");
            Expect(cross.IsSignal(cross.Node(Vector3.zero)),"crossroads has a signal");
            director=Director(cross);
            var driver=Car(director,4);
            int centre=cross.Node(Vector3.zero);bool ranRed=false,crossed=false;
            for(int i=0;i<2000;i++)
            {
                int to=driver.To,from=driver.From;
                director.Step(.05f);
                if(to==centre&&driver.From==centre)
                {
                    crossed=true;
                    if(cross.LampState(centre,cross.Group(centre,from))==TrafficGraph.Red)ranRed=true;
                }
            }
            Expect(crossed,"car crossed the junction on green");
            Expect(!ranRed,"car ran a red light");
            Object.DestroyImmediate(director.gameObject);

            // Busy crossroads: cars from different approaches never share the junction box closely.
            cross=TrafficGraph.FromRoadText("8 -80,0 0,0 80,0\n8 0,-80 0,0 0,80");
            centre=cross.Node(Vector3.zero);
            director=Director(cross);
            var busy=new System.Collections.Generic.List<TrafficVehicle>();
            for(int i=0;i<16;i++)busy.Add(Car(director,10+i));
            float nearest=float.MaxValue;int passes=0;string closestPair="";
            var lastTo=new int[busy.Count];for(int i=0;i<busy.Count;i++)lastTo[i]=busy[i].To;
            float box=cross.JunctionRadius(centre)+1f;
            for(int step=0;step<6000;step++)
            {
                director.Step(.05f);
                for(int i=0;i<busy.Count;i++)
                {
                    if(lastTo[i]==centre&&busy[i].From==centre)passes++;
                    lastTo[i]=busy[i].To;
                    var p=busy[i].transform.position;if(new Vector2(p.x,p.z).magnitude>box)continue;
                    for(int j=i+1;j<busy.Count;j++)
                    {
                        var q=busy[j].transform.position;if(new Vector2(q.x,q.z).magnitude>box)continue;
                        bool sameLane=busy[i].From==busy[j].From&&busy[i].To==busy[j].To;
                        if(!sameLane&&Vector3.Distance(p,q)<nearest){nearest=Vector3.Distance(p,q);closestPair=busy[i].From+">"+busy[i].To+" t"+busy[i].Travelled.ToString("F1")+" @"+p+" vs "+busy[j].From+">"+busy[j].To+" t"+busy[j].Travelled.ToString("F1")+" @"+q+" step "+step;}
                    }
                }
            }
            Expect(passes>=40,"junction kept flowing ("+passes+" passes in 300 s)");
            Expect(nearest>=3f,"turning cars kept apart in the junction, closest "+nearest+" m "+closestPair);
            Object.DestroyImmediate(director.gameObject);

            // Real district network: reservations must not gridlock 70 cars.
            var hongdae=TrafficGraph.FromRoadText(Resources.Load<TextAsset>("Geo/HongdaeRoads").text);
            director=Director(hongdae);
            var fleet=new System.Collections.Generic.List<TrafficVehicle>();
            var rng=new System.Random(3);
            for(int i=0;i<70;i++)
            {
                var car=new GameObject("Check car").AddComponent<TrafficVehicle>();car.transform.SetParent(director.transform);
                int start;do start=rng.Next(hongdae.nodes.Count);while(hongdae.links[start].Count==0);
                car.Begin(hongdae,start,9f,0f,4.2f,100+i);director.Add(car);fleet.Add(car);
            }
            for(int step=0;step<2400;step++)director.Step(.05f);
            var mark=new Vector3[fleet.Count];for(int i=0;i<fleet.Count;i++)mark[i]=fleet[i].transform.position;
            for(int step=0;step<1200;step++)director.Step(.05f);
            int stuck=0;for(int i=0;i<fleet.Count;i++)if(Vector3.Distance(mark[i],fleet[i].transform.position)<1f)stuck++;
            Expect(stuck<=3,"Hongdae traffic keeps moving ("+stuck+"/70 cars stood still for 60 s)");
            Debug.Log("TransitCheck: Hongdae cars still for 60 s: "+stuck+"/70");
            Object.DestroyImmediate(director.gameObject);
        }
        public static void Run()
        {
            failed=0;
            foreach(var type in new[]{"bus","brt","metro","ktx"})
            {
                Expect(TransitEconomy.Capacity(type)>0,"capacity for "+type);
                Expect(TransitEconomy.VehiclePurchase(type)>0,"vehicle purchase for "+type);
            }
            Expect(TransitEconomy.Capacity("brt")>TransitEconomy.Capacity("bus"),"BRT carries more than local bus");
            Expect(TransitEconomy.Capacity("ktx")>TransitEconomy.Capacity("metro"),"KTX carries more than metro");
            var footway=TrafficGraph.FromRoadText("8 0,0 50,0");
            var person=new GameObject("Check pedestrian").AddComponent<Pedestrian>();
            person.Begin(footway,0,2f,1f,0f,7,new Transform[0],new Transform[0]);
            for(int step=0;step<2000;step++)
            {
                person.Step(.05f);
                Expect(Mathf.Abs(person.transform.position.z)>=4.9f,"pedestrian stayed off the 8 m carriageway at "+person.transform.position);
                if(failed>0)break;
            }
            Object.DestroyImmediate(person.gameObject);
            int cheap=TransitEconomy.Demand("bus",1000,1000,60,60,TransitEconomy.BaseFare/2);
            int normal=TransitEconomy.Demand("bus",1000,1000,60,60,TransitEconomy.BaseFare);
            int dear=TransitEconomy.Demand("bus",1000,1000,60,60,TransitEconomy.BaseFare*2);
            Expect(cheap>normal&&normal>dear,"higher fare lowers demand ("+cheap+","+normal+","+dear+")");
            Expect(TransitEconomy.Demand("metro",1000,1000,60,60,TransitEconomy.BaseFare)>normal,"metro draws more riders than bus");
            Expect(TransitEconomy.Riders("bus",2,normal)==Mathf.Min(normal,80),"riders capped by vehicle capacity");
            Expect(TransitEconomy.Profit("bus",10,0,TransitEconomy.BaseFare)<0,"empty vehicles lose money");
            Expect(TransitEconomy.Profit("bus",1,40,TransitEconomy.BaseFare)>0,"full bus earns money");
            foreach(var id in new[]{"changwon","masan","jinhae","samcheonpo","chungmu","iri","yeocheon"})
                Expect(GameContent.City(id)!=null&&GameContent.City(id).id==id,"playable city "+id);
            CheckTraffic();
            Debug.Log("TransitCheck: "+(failed==0?"passed":failed+" failed"));
            EditorApplication.Exit(failed==0?0:1);
        }
    }
}
