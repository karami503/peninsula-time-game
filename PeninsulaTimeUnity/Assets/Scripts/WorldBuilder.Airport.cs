using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Bags riding round a baggage claim carousel.
    public class BaggageBelt : MonoBehaviour
    {
        public float halfLength=6f,radius=1.6f,speed=.6f;
        readonly List<Transform> bags=new List<Transform>();float clock;
        public void Add(Transform bag){bags.Add(bag);}
        void Update(){Step(Time.deltaTime);}
        public void Step(float seconds)
        {
            clock+=seconds*speed;float loop=4*halfLength+2*Mathf.PI*radius;
            for(int i=0;i<bags.Count;i++)
            {
                float s=Mathf.Repeat(clock+i*loop/bags.Count,loop);Vector3 p;
                if(s<2*halfLength)p=new Vector3(-halfLength+s,0,radius);
                else if(s<2*halfLength+Mathf.PI*radius){float a=(s-2*halfLength)/radius;p=new Vector3(halfLength+Mathf.Sin(a)*radius,0,Mathf.Cos(a)*radius);}
                else if(s<4*halfLength+Mathf.PI*radius)p=new Vector3(halfLength-(s-2*halfLength-Mathf.PI*radius),0,-radius);
                else{float a=(s-4*halfLength-Mathf.PI*radius)/radius;p=new Vector3(-halfLength-Mathf.Sin(a)*radius,0,-Mathf.Cos(a)*radius);}
                bags[i].localPosition=p+Vector3.up*.75f;
            }
        }
    }

    // Departure from a gate: push back, turn, taxi along the apron and take off. Time drives the path.
    public class PlaneFlight : MonoBehaviour
    {
        public const float Seconds=24f;
        public Vector3 gate,runwayDirection=Vector3.right;
        public float runwayZ=float.NaN; // world z of the runway centre line; NaN: take off from the apron
        public Quaternion baseRotation=Quaternion.identity; // model rotation when facing +Z
        public float clock;
        public Vector3[] taxiPath;
        public float Duration{get{return taxiPath!=null&&taxiPath.Length>1?66f:Seconds;}}
        public bool Done{get{return clock>=Duration;}}
        void Update(){Step(Time.deltaTime);}
        // Pushback, turn, taxi out to the runway, line up, take-off roll and climb.
        public void Step(float seconds)
        {
            clock+=seconds;
            if(taxiPath!=null&&taxiPath.Length>1){StepAirportRoute();return;}
            Vector3 out1=gate+Vector3.forward*35f,hold=float.IsNaN(runwayZ)?out1:new Vector3(out1.x,out1.y,runwayZ),p,facing;
            if(clock<4f){float u=clock/4f;p=Vector3.Lerp(gate,out1,u*u*(3-2*u));facing=Vector3.back;}
            else if(clock<6.5f){p=out1;facing=Vector3.Slerp(Vector3.back+Vector3.right*.01f,Vector3.forward,(clock-4f)/2.5f);}
            else if(clock<11f){float u=(clock-6.5f)/4.5f;p=Vector3.Lerp(out1,hold,u*u*(3-2*u));facing=Vector3.forward;}
            else if(clock<13f){p=hold;facing=Vector3.Slerp(Vector3.forward,runwayDirection,(clock-11f)/2f);}
            else
            {
                float u=clock-13f,lift=Mathf.Max(0,u-4f);
                p=hold+runwayDirection*(5f*u*u)+Vector3.up*lift*lift*3f;facing=runwayDirection+Vector3.up*Mathf.Clamp01(lift*.15f);
            }
            transform.position=p;
            if(facing.sqrMagnitude>.001f)transform.rotation=Quaternion.LookRotation(facing)*baseRotation;
        }
        // The game compresses ground travel time, but positions follow the rendered taxi centre line.
        void StepAirportRoute()
        {
            const float pushEnd=4f,turnEnd=6.5f,taxiEnd=34.5f,holdEnd=36f,rollEnd=54f;
            Vector3 p,facing,first=taxiPath[0],last=taxiPath[taxiPath.Length-1];
            if(clock<pushEnd){float t=Mathf.Clamp01(clock/pushEnd);p=Vector3.Lerp(gate,first,t*t*(3-2*t));facing=Vector3.back;}
            else if(clock<turnEnd){p=first;facing=Vector3.Slerp(Vector3.back+Vector3.right*.01f,(taxiPath[1]-first).normalized,(clock-pushEnd)/(turnEnd-pushEnd));}
            else if(clock<taxiEnd)TaxiPose((clock-turnEnd)/(taxiEnd-turnEnd),out p,out facing);
            else if(clock<holdEnd){p=last;facing=runwayDirection;}
            else if(clock<rollEnd){float t=(clock-holdEnd)/(rollEnd-holdEnd);p=last+runwayDirection*(1350f*t*t);facing=runwayDirection;}
            else {float t=Mathf.Max(0,clock-rollEnd);p=last+runwayDirection*(1350f+150f*t)+Vector3.up*(150f*t*Mathf.Tan(12f*Mathf.Deg2Rad));facing=runwayDirection+Vector3.up*Mathf.Tan(12f*Mathf.Deg2Rad);}
            transform.position=p;transform.rotation=Quaternion.LookRotation(facing)*baseRotation;
        }
        void TaxiPose(float progress,out Vector3 position,out Vector3 facing)
        {
            float length=0;for(int i=1;i<taxiPath.Length;i++)length+=Vector3.Distance(taxiPath[i-1],taxiPath[i]);
            float distance=Mathf.Clamp01(progress)*length;
            for(int i=1;i<taxiPath.Length;i++){
                var delta=taxiPath[i]-taxiPath[i-1];float segment=delta.magnitude;
                if(distance<=segment||i==taxiPath.Length-1){position=Vector3.Lerp(taxiPath[i-1],taxiPath[i],segment>.001f?distance/segment:0);facing=segment>.001f?delta/segment:runwayDirection;return;}
                distance-=segment;
            }
            position=taxiPath[taxiPath.Length-1];facing=runwayDirection;
        }
    }

    // Gimpo domestic terminal. Surface: aircraft at the OSM stands, jet bridges and a taxiing aircraft.
    // Interior (built beside the district at AirportOrigin): 1F arrivals and baggage claim, 2F check-in and
    // 3F security and gate lounge with windows onto the apron (KAC floor guide: 1층 도착, 2층 출발 수속,
    // 3층 탑승구). Airline names and flight numbers are fictional.
    public partial class WorldBuilder
    {
        public static readonly Vector3 AirportOrigin=new Vector3(550,0,0);
        public const float Floor2=6f,Floor3=12f,Floor4=18f,RunwayZ=322.5f; // runway centre line, terminal-local
        const float TerminalHalfX=60f,TerminalHalfZ=30f,RoofY=24f,PierHalfX=150f; // 3F gate concourse runs out beyond the main hall
        public static readonly string[,] Flights={{"HB 1203","제주","jeju"},{"BS 8821","부산(김해)","busan"},{"JW 0442","제주","jeju"},{"SS 1507","광주","gwangju"},{"BS 8835","울산","ulsan"},{"HB 1219","대구","daegu"},{"JW 0450","포항경주","pohang"},{"SS 1511","제주","jeju"}};
        public static readonly string[] Airlines={"한빛항공","푸른하늘항공","제주바람항공","새솔항공"};
        public static readonly string[] AirlineCodes={"HB","BS","JW","SS"};
        static readonly string[] Departures={"09:10","09:25","09:40","09:55","10:05","10:20","10:35","10:50"};
        Vector3[] terminalOutline=new Vector3[0];
        Transform airport;
        public Vector3 AirportArrivalSpawn{get{return AirportOrigin+new Vector3(0,1.65f,-22);}}
        public Vector3 BaggageSpawn{get{return AirportOrigin+new Vector3(40,1.65f,10);}}
        public Vector3 GateSpawn(int gate){return AirportOrigin+new Vector3(GateX(gate),Floor3+1.65f,20);} // desk, jet bridge and aircraft in view
        static float GateX(int gate){return -140f+(Mathf.Clamp(gate,1,8)-1)*40f;} // stands 40 m apart: wider than an airliner's wingspan
        public Vector3 GatePlane(int gate){return AirportOrigin+new Vector3(GateX(gate),0,TerminalHalfZ+40);} // nose ~18 m from the glass
        public GameObject GateAircraft(int gate){var t=airport!=null?airport.Find("탑승구 "+gate+" 항공기"):null;return t!=null?t.gameObject:null;}

        Vector3 TerminalDoor(List<Vector3> points){terminalOutline=points.ToArray();return Centre(points);}

        void BuildAirport(List<Vector3> stands,List<List<Vector3>> bridges)
        {
            var centre=Centre(new List<Vector3>(terminalOutline));
            var airside=stands.Count>0?Centre(stands)-centre:Vector3.forward;airside.y=0;airside=airside.sqrMagnitude>1?airside.normalized:Vector3.forward;
            float yaw=Mathf.Atan2(-airside.x,-airside.z)*Mathf.Rad2Deg;
            foreach(var stand in stands)
            {
                if(Mathf.Abs(stand.x)>285||Mathf.Abs(stand.z)>285)continue;
                if(CityModel("Airplane",stand+airside*17f,1f,yaw)!=null)AircraftCount++;
            }
            var bridgeMat=Mat("jet-bridge",new Color(.80f,.82f,.84f),.3f);var steel=Mat("shelter-steel",new Color(.35f,.40f,.44f),.5f);
            foreach(var line in bridges)for(int i=1;i<line.Count;i++)
            {
                var a=line[i-1]+Vector3.up*4.6f;var b=line[i]+Vector3.up*4.6f;if((b-a).sqrMagnitude<.1f)continue;
                var tube=Primitive(PrimitiveType.Cube,"탑승교",root.transform,(a+b)*.5f,new Vector3(2.8f,2.8f,Vector3.Distance(a,b)+.3f),bridgeMat);
                tube.transform.rotation=Quaternion.LookRotation(b-a);
                Primitive(PrimitiveType.Cylinder,"탑승교 지지대",root.transform,new Vector3(b.x,1.6f,b.z),new Vector3(.4f,1.6f,.4f),steel);
            }
            // An aircraft taxiing along the apron, parallel to the terminal.
            var tangent=Vector3.Cross(Vector3.up,airside);
            var lane=new List<Vector3>();
            for(int i=-1;i<=1;i++){var p=centre+airside*150f+tangent*(i*240f);lane.Add(new Vector3(Mathf.Clamp(p.x,-290,290),0,Mathf.Clamp(p.z,-290,290)));}
            var taxi=CityModel("Airplane",lane[0]);
            if(taxi!=null){taxi.name="유도로 이동 항공기";taxi.AddComponent<RailVehicle>().Begin(lane,8f);AircraftCount++;}
            // Terminal door on the landside face, nearest the centre.
            Vector3 door=centre;float best=float.MaxValue;
            foreach(var p in terminalOutline)
            {
                float lateral=Mathf.Abs(Vector3.Dot(p-centre,tangent)),depth=Vector3.Dot(p-centre,airside);
                if(lateral<70f&&depth<best){best=depth;door=p;}
            }
            door.y=0;
            // Glass automatic doors in a steel frame under a short canopy; the frame box is what you click.
            var front=new GameObject("김포공항 국내선 청사 정문").transform;front.SetParent(root.transform,false);
            front.position=door;front.rotation=Quaternion.LookRotation(airside); // local -z faces the kerb
            var frame=Mat("terminal-frame",new Color(.22f,.25f,.28f),.5f);var leaf=Mat("terminal-door",new Color(.62f,.78f,.86f),.8f);
            var entrance=Primitive(PrimitiveType.Cube,"김포공항 국내선 청사 출입문",front,new Vector3(0,1.6f,-1.2f),new Vector3(6f,3.2f,1.2f),frame);
            for(int s=-1;s<=1;s+=2)DestroyImmediate(Primitive(PrimitiveType.Cube,"자동문 유리",front,new Vector3(s*1.4f,1.45f,-1.82f),new Vector3(2.6f,2.7f,.04f),leaf).GetComponent<Collider>());
            DestroyImmediate(Primitive(PrimitiveType.Cube,"출입구 캐노피",front,new Vector3(0,3.5f,-1.8f),new Vector3(10f,.22f,2f),frame).GetComponent<Collider>());
            Sign("김포국제공항 국내선\nGimpo Domestic Terminal",root.transform,door-airside*1.3f+Vector3.up*4.6f,-airside,.34f,Color.white);
            var portal=entrance.AddComponent<StationPortal>();portal.label="공항 청사 들어가기 (1층 도착 · 2층 출발)";portal.arrival="김포공항 국내선 청사 1층입니다 · 2층에서 체크인하세요";
            portal.destination=AirportArrivalSpawn;portal.facing=Vector3.forward;
            // Seen from across the forecourt; leaving the terminal puts you just outside the doors.
            TerminalEntrance=door-airside*8f;TerminalFacing=airside;Marker(door,"공항 청사");
            BuildTerminal(door-airside*4f,-airside);
            BuildInternationalAirport();
        }

        void BuildTerminal(Vector3 streetDoor,Vector3 streetFacing)
        {
            airport=new GameObject("김포공항 국내선 청사 내부").transform;airport.SetParent(root.transform,false);airport.localPosition=AirportOrigin;
            var floor=Mat("terminal-floor",new Color(.88f,.87f,.84f),0,"granite",3f);
            var wall=Mat("terminal-wall",new Color(.92f,.92f,.90f),0,"white",2f);
            var glass=Mat("terminal-glass",new Color(.62f,.80f,.90f),.35f);
            var steel=Mat("terminal-steel",new Color(.70f,.72f,.75f),.6f);
            var ceiling=Mat("terminal-ceiling",new Color(.93f,.93f,.92f),0,"white",4f);
            float hx=TerminalHalfX,hz=TerminalHalfZ;
            // Slabs: 1F ground, 2F departures (landside and security), 3F airside gate lounge, roof.
            Block("1층 바닥",airport,new Vector3(-hx,-.4f,-hz),new Vector3(hx,0,hz),floor);
            // Stair openings remain empty for the complete run, including pedestrian headroom.
            AirportFloor("2층 바닥",Floor2,-hx,hx,-hz,12,new Rect(-52,-30,8,16),floor);
            AirportFloor("3층 바닥",Floor3,-hx,hx,-hz,hz,new Rect(-4,-26,8,16),floor);
            Block("3층 서측 탑승동 바닥",airport,new Vector3(-PierHalfX,Floor3-.4f,0),new Vector3(-hx,Floor3,hz),floor);
            Block("3층 동측 탑승동 바닥",airport,new Vector3(hx,Floor3-.4f,0),new Vector3(PierHalfX,Floor3,hz),floor);
            AirportFloor("4층 식당가 바닥",Floor4,-hx,hx,-hz,0,new Rect(44,-26,8,16),floor);
            Block("지붕",airport,new Vector3(-hx-1,RoofY,-hz-1),new Vector3(hx+1,RoofY+.6f,hz+1),ceiling);
            // Envelope: glass curtain walls on both long sides, solid gables; on 3F the gables open into the
            // gate concourse wings, which stand on columns over the apron.
            foreach(float side in new[]{-1f,1f})
            {
                float gable=side*(hx+.15f),end=side*(PierHalfX+.15f),wing=side*(hx+PierHalfX)*.5f,length=PierHalfX-hx;
                if(side>0)Primitive(PrimitiveType.Cube,"청사 벽",airport,new Vector3(gable,RoofY*.5f,-hz*.5f),new Vector3(.3f,RoofY,hz),wall);
                else {
                    Block("청사 연결통로 옆벽",airport,new Vector3(-hx-.3f,0,-hz),new Vector3(-hx,RoofY,-17.25f),wall);
                    Block("청사 연결통로 옆벽",airport,new Vector3(-hx-.3f,0,-14.75f),new Vector3(-hx,RoofY,0),wall);
                    Block("청사 연결통로 상부",airport,new Vector3(-hx-.3f,3.1f,-17.25f),new Vector3(-hx,RoofY,-14.75f),wall);
                }
                Primitive(PrimitiveType.Cube,"청사 벽",airport,new Vector3(gable,(Floor3-.4f)*.5f,hz*.5f),new Vector3(.3f,Floor3-.4f,hz),wall);
                Primitive(PrimitiveType.Cube,"탑승동 끝벽",airport,new Vector3(end,(Floor3-.4f+RoofY)*.5f,hz*.5f),new Vector3(.3f,RoofY-Floor3+.4f,hz),wall);
                Primitive(PrimitiveType.Cube,"탑승동 지붕",airport,new Vector3(wing,RoofY+.3f,hz*.5f),new Vector3(length+2,.6f,hz+2),ceiling);
                Primitive(PrimitiveType.Cube,"탑승동 뒷벽",airport,new Vector3(wing,(Floor3-.4f+RoofY)*.5f,-.15f),new Vector3(length,RoofY-Floor3+.4f,.3f),wall);

                for(float x=hx+10;x<PierHalfX;x+=20)foreach(float z in new[]{2f,hz-2})DestroyImmediate(Primitive(PrimitiveType.Cube,"탑승동 기둥",airport,new Vector3(side*x,(Floor3-.4f)*.5f,z),new Vector3(.9f,Floor3-.4f,.9f),steel).GetComponent<Collider>());
            }
            Block("청사 유리벽",airport,new Vector3(-hx,0,-hz-.15f),new Vector3(hx,RoofY,-hz),glass);
            Block("청사 하부 유리벽",airport,new Vector3(-hx,0,hz),new Vector3(hx,Floor3-.4f,hz+.15f),glass);
            BuildAirportGateFacade(glass);
            for(float x=-PierHalfX;x<=PierHalfX;x+=7.5f)foreach(float z in new[]{-hz-.1f,hz+.1f})
            {
                bool hall=Mathf.Abs(x)<=hx;if(!hall&&z<0)continue;
                bool opening=false;if(z>0)for(int g=1;g<=8;g++)if(Mathf.Abs(x-GateX(g)-5)<1.8f)opening=true;
                if(opening)continue;
                Block("멀리언",airport,new Vector3(x-.1f,hall?0:Floor3-.4f,z-.12f),new Vector3(x+.1f,RoofY,z+.12f),steel,false);
            }
            for(float x=-PierHalfX+6;x<PierHalfX;x+=12)Block("천장 보",airport,new Vector3(x-.2f,RoofY-.8f,Mathf.Abs(x)<hx?-hz:0),new Vector3(x+.2f,RoofY,hz),steel,false);
            var light=Glow("terminal-light",new Color(1f,.98f,.92f),1.2f);
            for(float z=-26;z<=26;z+=8){float reach=z>0?PierHalfX-2:hx-2;Block("천장 조명",airport,new Vector3(-reach,RoofY-.85f,z-.2f),new Vector3(reach,RoofY-.8f,z+.2f),light,false);}
            for(int i=0;i<6;i++)
            {
                var lamp=new GameObject("청사 조명").AddComponent<Light>();lamp.transform.SetParent(airport,false);
                lamp.transform.localPosition=new Vector3(-50+i*20,i%2==0?Floor2+5:Floor3+4,i%2==0?-14:16);lamp.type=LightType.Point;lamp.range=34;lamp.intensity=.9f;lamp.color=new Color(1f,.97f,.9f);
            }
            BuildArrivals(streetDoor,streetFacing);
            BuildDepartures(streetDoor,streetFacing);
            BuildGates();
            BuildAirportMovingWalkways();
            BuildAirportUpperFloor();
            BuildApronView();
            BuildTerminalPeople();
            var o=AirportOrigin;
            Marker(o+new Vector3(-TerminalHalfX+.5f,0,-16),"지하철");Marker(o+new Vector3(0,0,-29),"출구");
            Marker(o+new Vector3(-48,0,-24),"2층 ↑");Marker(o+new Vector3(0,0,16),"수하물");Marker(o+new Vector3(-21,0,-11),"안내");
            for(int i=0;i<4;i++)Marker(o+new Vector3(-30+i*20,Floor2,-16),"체크인 "+(char)('A'+i));
            Marker(o+new Vector3(0,Floor2,-28),"키오스크");Marker(o+new Vector3(-12,Floor3,0),"보안검색");Marker(o+new Vector3(12,Floor3,0),"보안검색");Marker(o+new Vector3(0,Floor2,-24),"3층 ↑");
            Marker(o+new Vector3(48,Floor3,-24),"4층 식당 ↑");
            for(int g=1;g<=8;g++)Marker(o+new Vector3(GateX(g),Floor3,26),g+"번");
            Marker(o+new Vector3(PierHalfX-1,Floor3,12),"도착 ↓");
        }

        // Passengers circulating in the arrivals hall, the check-in hall and the gate lounge.
        void BuildTerminalPeople()
        {
            var graph=new TrafficGraph();var o=AirportOrigin;
            System.Action<float,float,float,float,float> loop=(y,x0,z0,x1,z1)=>
            {
                var p=new[]{o+new Vector3(x0,y,z0),o+new Vector3(x1,y,z0),o+new Vector3(x1,y,z1),o+new Vector3(x0,y,z1)};
                for(int i=0;i<4;i++)graph.Link(p[i],p[(i+1)%4],0);
            };
            loop(0,-40,-22,40,-5);
            loop(Floor2,-40,-27,40,-2);
            loop(Floor3,-PierHalfX+1.2f,4,PierHalfX-1.2f,22);
            var director=new GameObject("청사 승객").AddComponent<TrafficDirector>();director.transform.SetParent(airport,false);
            director.Init(graph,null,null,null);
            SpawnPeople(director,36,0,0,1f);
        }

        // Escalator as a hidden walkable ramp with visible steps, rising along +z from `start` (local).
        void Escalator(Vector3 start,float width,float run,float rise)
        {
            var steel=Mat("escalator-step",new Color(.35f,.37f,.39f),.5f,"metal",1f);
            float angle=Mathf.Atan2(rise,run)*Mathf.Rad2Deg,length=Mathf.Sqrt(rise*rise+run*run);
            var ramp=Primitive(PrimitiveType.Cube,"에스컬레이터 경사면",airport,start+new Vector3(0,rise*.5f-.15f,run*.5f),new Vector3(width,.3f,length),steel);
            ramp.transform.localRotation=Quaternion.Euler(-angle,0,0);ramp.GetComponent<Renderer>().enabled=false;
            int steps=Mathf.RoundToInt(rise/.2f);
            for(int i=0;i<steps;i++)
                Block("에스컬레이터 디딤판",airport,start+new Vector3(-width*.5f,rise*i/steps,run*i/steps),start+new Vector3(width*.5f,rise*(i+1)/steps,run*(i+1)/steps),steel,false);
            foreach(float side in new[]{-1f,1f})
            {
                var balustrade=Primitive(PrimitiveType.Cube,"에스컬레이터 난간",airport,start+new Vector3(side*width*.5f,rise*.5f+.55f,run*.5f),new Vector3(.08f,1f,length),Mat("escalator-glass",new Color(.70f,.82f,.86f),.3f));
                balustrade.transform.localRotation=Quaternion.Euler(-angle,0,0);DestroyImmediate(balustrade.GetComponent<Collider>());
            }
        }

        void BuildArrivals(Vector3 streetDoor,Vector3 streetFacing)
        {
            var glass=Mat("terminal-glass",new Color(.62f,.80f,.90f),.35f);var counter=Mat("station-counter",new Color(.30f,.36f,.42f));
            // Baggage claim (airside, z > 3) behind a glass wall with an exit opening.
            Block("수하물 수취 구역 유리벽",airport,new Vector3(-TerminalHalfX,0,2.9f),new Vector3(-3,Floor2-.4f,3.1f),glass);
            Block("수하물 수취 구역 유리벽",airport,new Vector3(3,0,2.9f),new Vector3(TerminalHalfX,Floor2-.4f,3.1f),glass);
            Board("도착 출구  Arrivals Exit",airport,AirportOrigin+new Vector3(0,4.3f,2.8f),Vector3.back,new Vector2(7,.8f),new Color(.95f,.75f,.10f),new Color(.1f,.1f,.1f),.4f);
            var belt=Mat("carousel",new Color(.25f,.27f,.30f),.3f);
            var colours=new[]{new Color(.75f,.18f,.15f),new Color(.15f,.25f,.55f),new Color(.12f,.12f,.13f),new Color(.85f,.70f,.20f),new Color(.30f,.55f,.35f)};
            for(int c=0;c<3;c++)
            {
                float cx=-30+c*30;
                var carousel=new GameObject("수하물 수취대 "+(c+1));carousel.transform.SetParent(airport,false);carousel.transform.localPosition=new Vector3(cx,0,16);
                Block("수취대",carousel.transform,new Vector3(-7.6f,0,-2.4f),new Vector3(7.6f,.7f,2.4f),belt);
                var mover=carousel.AddComponent<BaggageBelt>();
                for(int b=0;b<10;b++)
                {
                    var bag=Primitive(PrimitiveType.Cube,"여행 가방",carousel.transform,Vector3.zero,new Vector3(.45f,.3f,.7f),Mat("bag-"+(b%5),colours[b%5]));
                    DestroyImmediate(bag.GetComponent<Collider>());mover.Add(bag.transform);
                }
                mover.Step(0);
                Board((c+1)+"   "+Flights[c,1]+"  "+Flights[c,0],airport,AirportOrigin+new Vector3(cx,3.6f,12.8f),Vector3.back,new Vector2(5,.7f),new Color(.08f,.10f,.14f),new Color(1f,.82f,.25f),.3f);
            }
            // Arrivals hall: meeting rail, information desk, transport counters, seating, exits.
            Block("마중 난간",airport,new Vector3(-14,0,-1),new Vector3(-3,1.0f,-.9f),Mat("terminal-steel",new Color(.70f,.72f,.75f),.6f));
            Block("마중 난간",airport,new Vector3(3,0,-1),new Vector3(14,1.0f,-.9f),Mat("terminal-steel",new Color(.70f,.72f,.75f),.6f));
            var info=Block("종합안내",airport,new Vector3(-24,0,-12),new Vector3(-18,1.1f,-10),counter);
            AddFixture(info,"info","종합안내 문의","김포공항 종합안내: 출발은 2층에서 체크인한 뒤 3층에서 보안검색을 받고 탑승구로 갑니다. 지하철은 1층 서쪽 연결통로로 가세요.");
            Board("종합안내  Information",airport,AirportOrigin+new Vector3(-21,3.2f,-11),Vector3.back,new Vector2(5,.6f),new Color(.10f,.24f,.45f),Color.white,.3f);
            string[] counters={"리무진 버스","렌터카","택시 안내","환전"};
            for(int i=0;i<counters.Length;i++)
            {
                float x=10+i*10;
                var desk=Block(counters[i],airport,new Vector3(x-3,0,-27),new Vector3(x+3,1.1f,-25.5f),counter);
                Board(counters[i],airport,AirportOrigin+new Vector3(x,3f,-27.6f),Vector3.forward,new Vector2(5,.6f),new Color(.10f,.24f,.45f),Color.white,.3f);
                AddFixture(desk,"shop",counters[i]+" 문의",counters[i]+" 안내를 받았습니다.");
            }
            var seat=Mat("lounge-seat",new Color(.22f,.30f,.45f));
            for(int r=0;r<3;r++)Block("대기 의자",airport,new Vector3(-12,0,-20+r*3),new Vector3(-2,.45f,-19.4f+r*3),seat);
            Board("도착  Arrivals  1F",airport,AirportOrigin+new Vector3(0,4.6f,-29.6f),Vector3.forward,new Vector2(9,.9f),new Color(.95f,.75f,.10f),new Color(.1f,.1f,.1f),.45f);
            var outside=Primitive(PrimitiveType.Cube,"1층 출입문",airport,new Vector3(0,1.6f,-TerminalHalfZ+.2f),new Vector3(5,3.2f,.5f),Mat("terminal-door",new Color(.45f,.65f,.75f),.4f));
            var exit=outside.AddComponent<StationPortal>();exit.label="밖으로 나가기 (버스·택시)";exit.arrival="김포공항 국내선 청사 앞입니다";exit.destination=streetDoor+Vector3.up*1.65f;exit.facing=streetFacing;
            Board("지하철 ↓  5 · 9 · 공항철도 · 김포골드 · 서해",airport,AirportOrigin+new Vector3(-TerminalHalfX+.5f,4.2f,-16),Vector3.right,new Vector2(10,.7f),new Color(.10f,.24f,.45f),Color.white,.3f);
            AirportStairPair(new Vector3(-48,0,-28),Floor2,"1층 → 2층 출발");
            Board("2층 출발  Departures ↑",airport,AirportOrigin+new Vector3(-48,4.4f,-27),Vector3.back,new Vector2(5,.6f),new Color(.10f,.24f,.45f),Color.white,.3f);
        }

        void BuildDepartures(Vector3 streetDoor,Vector3 streetFacing)
        {
            float y=Floor2;var counter=Mat("checkin-counter",new Color(.78f,.80f,.82f),.2f);var back=Mat("checkin-back",new Color(.25f,.30f,.38f));
            var tints=new[]{new Color(.10f,.35f,.70f),new Color(.15f,.55f,.70f),new Color(.95f,.55f,.15f),new Color(.30f,.60f,.35f)};
            var queue=new System.Random(11);
            for(int i=0;i<4;i++)
            {
                float x=-30+i*20;char letter=(char)('A'+i);
                Block("체크인 아일랜드 "+letter,airport,new Vector3(x-1.4f,y,-24),new Vector3(x+1.4f,y+2.2f,-8),back);
                Block("체크인 간판",airport,new Vector3(x-1.5f,y+2.2f,-24),new Vector3(x+1.5f,y+3f,-8),Glow("airline-"+i,tints[i],.7f),false);
                foreach(float side in new[]{-1f,1f})
                {
                    Sign(letter+"  "+Airlines[i],airport,AirportOrigin+new Vector3(x+side*1.55f,y+2.6f,-16),new Vector3(side,0,0),.36f,Color.white);
                    for(int k=0;k<4;k++)
                    {
                        float z=-22+k*4,inner=x+side*1.4f,outer=x+side*2.4f;
                        var desk=Block("체크인 카운터",airport,new Vector3(Mathf.Min(inner,outer),y,z-1.2f),new Vector3(Mathf.Max(inner,outer),y+1.05f,z+1.2f),counter);
                        AddFixture(desk,"checkin",Airlines[i]+" 체크인 · 탑승권 받기","",i);
                        Block("수하물 저울",airport,new Vector3(x+side*2.9f-.4f,y,z-.4f),new Vector3(x+side*2.9f+.4f,y+.25f,z+.4f),Mat("scale",new Color(.35f,.36f,.38f),.5f),false);
                        Transform[] legs;if(k%2==0)Bystander(AirportOrigin+new Vector3(x+side*3.5f,y,z),new Vector3(-side,0,0),airport,queue,out legs); // checking in
                    }
                }
            }
            // Self check-in kiosks and the flight information display.
            for(int i=0;i<6;i++)
            {
                float x=-12+i*4;
                var kiosk=Block("셀프 체크인 키오스크",airport,new Vector3(x-.4f,y,-28.6f),new Vector3(x+.4f,y+1.6f,-28),Mat("kiosk",new Color(.85f,.86f,.88f),.3f));
                Block("키오스크 화면",airport,new Vector3(x-.3f,y+1.0f,-28.0f),new Vector3(x+.3f,y+1.45f,-27.95f),Glow("kiosk-screen",new Color(.2f,.55f,.85f),.9f),false);
                AddFixture(kiosk,"checkin","셀프 체크인 키오스크","",i%4);
            }
            Board("셀프 체크인  Self Check-in",airport,AirportOrigin+new Vector3(-2,y+3.2f,-28.5f),Vector3.forward,new Vector2(8,.6f),new Color(.10f,.24f,.45f),Color.white,.3f);
            var fids=new System.Text.StringBuilder("출발  DEPARTURES\n");
            for(int i=0;i<8;i++)fids.Append(Departures[i]+"   "+Flights[i,0]+"   "+Flights[i,1]+"   탑승구 "+(i+1)+"\n");
            Block("운항 정보 표시판",airport,new Vector3(-6,y+3.4f,4.6f),new Vector3(6,y+5.5f,4.9f),Mat("fids",new Color(.04f,.05f,.08f)),false);
            Sign(fids.ToString(),airport,AirportOrigin+new Vector3(0,y+4.45f,4.55f),Vector3.back,.17f,new Color(1f,.85f,.3f));
            // The KAC domestic guide places identity/security screening on 3F.
            AirportStairPair(new Vector3(0,y,-24),Floor3-Floor2,"2층 → 3층 출발장");
            BuildAirportSecurity();
            Board("3층 출발장 · 보안검색 ↑",airport,AirportOrigin+new Vector3(0,y+3.5f,-25.5f),Vector3.back,new Vector2(6,.6f),new Color(.10f,.24f,.45f),Color.white,.3f);
            Board("출발  Departures  2F",airport,AirportOrigin+new Vector3(0,y+8f,-29.6f),Vector3.forward,new Vector2(9,.9f),new Color(.10f,.24f,.45f),Color.white,.45f);
            var door=Primitive(PrimitiveType.Cube,"2층 출발 출입문",airport,new Vector3(20,y+1.6f,-TerminalHalfZ+.2f),new Vector3(5,3.2f,.5f),Mat("terminal-door",new Color(.45f,.65f,.75f),.4f));
            var exit=door.AddComponent<StationPortal>();exit.label="밖으로 나가기";exit.arrival="김포공항 국내선 청사 앞입니다";exit.destination=streetDoor+Vector3.up*1.65f;exit.facing=streetFacing;
        }

        void BuildGates()
        {
            float y=Floor3;var seat=Mat("lounge-seat",new Color(.22f,.30f,.45f));var desk=Mat("gate-desk",new Color(.30f,.36f,.42f));
            var glass=Mat("terminal-glass",new Color(.62f,.80f,.90f),.35f);

            for(int g=1;g<=8;g++)
            {
                float x=GateX(g);
                var counter=Block("탑승구 데스크 "+g,airport,new Vector3(x-1.6f,y,26),new Vector3(x+1.6f,y+1.1f,27),desk);
                AddFixture(counter,"info","탑승구 "+g+" 안내","오른쪽 탑승교를 따라 항공기 문 앞에서 탑승하세요.");
                Board(g+"  "+Flights[g-1,1]+"  "+Flights[g-1,0]+"  "+Departures[g-1],airport,AirportOrigin+new Vector3(x,y+3.4f,28.6f),Vector3.back,new Vector2(6.5f,.7f),new Color(.08f,.10f,.14f),new Color(1f,.85f,.3f),.3f);
                Board(g.ToString(),airport,AirportOrigin+new Vector3(x,y+5.2f,28.6f),Vector3.back,new Vector2(1.4f,1.2f),new Color(.95f,.75f,.10f),new Color(.1f,.1f,.1f),.8f);
                for(int r=0;r<3;r++)Block("대기 의자",airport,new Vector3(x-5,y,8+r*4),new Vector3(x+5,y+.45f,8.6f+r*4),seat);
            }
            string[] shops={"카페","편의점","서점","기념품"};
            float[] shopX={-50,-25,25,50};
            for(int i=0;i<4;i++)
            {
                var front=Block(shops[i],airport,new Vector3(shopX[i]-4,y,.1f),new Vector3(shopX[i]+4,y+1.1f,1.6f),desk);
                Board(shops[i],airport,AirportOrigin+new Vector3(shopX[i],y+3f,.1f),Vector3.forward,new Vector2(5,.6f),new Color(.45f,.30f,.20f),Color.white,.34f);
                AddFixture(front,"shop",shops[i]+" 들르기",shops[i]+"에서 잠시 쉬었다 갑니다.");
            }
            var arrivals=Primitive(PrimitiveType.Cube,"도착 승객 통로",airport,new Vector3(PierHalfX-.4f,y+1.6f,12),new Vector3(.5f,3.2f,4),Mat("entrance-dark",new Color(.12f,.13f,.14f)));
            var down=arrivals.AddComponent<StationPortal>();down.downstairs=true;down.label="도착 승객 통로 · 1층 수하물 찾는 곳";down.arrival="1층 수하물 찾는 곳입니다";down.destination=BaggageSpawn;down.facing=Vector3.back;
            Board("수하물 찾는 곳 · 도착 ↓",airport,AirportOrigin+new Vector3(PierHalfX-.5f,y+4.2f,12),Vector3.left,new Vector2(6,.6f),new Color(.95f,.75f,.10f),new Color(.1f,.1f,.1f),.3f);
        }

        // What the windows look onto: apron, aircraft at jet bridges and a distant runway.
        void BuildApronView()
        {
            BuildGimpoAirfield();
            var bridgeMat=Mat("jet-bridge",new Color(.80f,.82f,.84f),.3f);
            for(int g=1;g<=8;g++)
            {
                float x=GateX(g);
                var plane=CityModel("Airplane",GatePlane(g),1f,180f); // nose in (-Z), toward the jet bridge
                if(plane!=null){plane.name="탑승구 "+g+" 항공기";plane.transform.SetParent(airport,true);BuildAirportCabin(g,plane);}
                BuildWalkableJetBridge(g,x,bridgeMat);
            }
            var lane=new List<Vector3>{AirportOrigin+new Vector3(-170,0,120),AirportOrigin+new Vector3(350,0,120)};
            var taxi=CityModel("Airplane",lane[0]);
            if(taxi!=null){taxi.name="유도로 이동 항공기";taxi.AddComponent<RailVehicle>().Begin(lane,9f);}
        }
    }
}
