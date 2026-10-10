using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Leaves a destination terminal through its landside doors, to that airport's city on the map.
    public class AirportExit : Interactable
    {
        public string city,airport;
        public override string Hint{get{return airport+" 밖으로 나가기 (도시 지도)";}}
    }

    // Every Korean airport other than Gimpo (which has its own surveyed district) as a walkable, blocky scene: the main
    // runway at its real heading and length, a parallel taxiway, an apron with one aircraft per gate, and a terminal
    // scaled by the airport's size with self check-in kiosks, gate doors, a baggage belt and a landside exit, plus a
    // control tower and the forecourt. Each part is its own Structure; nothing is curved except the parked aircraft.
    public partial class WorldBuilder
    {
        static readonly float[] TerminalWidth={96,150,240,360},TerminalDepth={34,44,56,70},TerminalHeight={9,11,14,18};
        const float StandDepth=40f,TaxiLane=95f,RunwayOffset=230f,RunwayWidth=45f,TaxiWidth=23f,LineupRun=150f;
        public KoreanAirport DestinationAirport{get;private set;}
        Transform destinationSite;
        readonly List<GameObject> destinationGates=new List<GameObject>();

        float DW{get{return TerminalWidth[DestinationAirport.size];}}
        float DD{get{return TerminalDepth[DestinationAirport.size];}}
        float GateLocalX(int gate){int n=DestinationAirport.Gates;return -DW*.5f+DW*(Mathf.Clamp(gate,1,n)-.5f)/n;}
        Vector3 SitePoint(float x,float y,float z){return destinationSite.TransformPoint(new Vector3(x,y,z));}
        // Inside, between the gate seating and the kiosks, facing the landside doors.
        public Vector3 DestinationArrivalSpawn{get{return SitePoint(0,1.65f,DD*.5f-14f);}}
        public Vector3 DestinationArrivalFacing{get{return -destinationSite.forward;}}

        public void BuildDestinationAirport(KoreanAirport airport)
        {
            Clear();destinationGates.Clear();DestinationAirport=airport;
            SetupLight(new Color(.52f,.58f,.63f),new Color(1f,.93f,.8f));
            worldCamera.orthographic=false;worldCamera.fieldOfView=72;
            RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.0009f;
            // Local +X runs along the runway in its real heading (degrees from north, world +Z north).
            destinationSite=new GameObject(airport.name).transform;destinationSite.SetParent(root.transform,false);
            destinationSite.rotation=Quaternion.Euler(0,airport.heading-90f,0);
            BuildDestinationAirfield(airport);
            BuildDestinationTerminal(airport);
            BuildDestinationTower(airport);
            BuildDestinationForecourt(airport);
            for(int g=1;g<=airport.Gates;g++)
            {
                var plane=CityModel("Airplane",DestinationGatePlane(g),1f,destinationSite.eulerAngles.y+180f); // nose in, toward the terminal
                if(plane==null)continue;
                plane.name="탑승구 "+g+" 항공기";plane.transform.SetParent(destinationSite,true);destinationGates.Add(plane);
            }
            Marker(DestinationArrivalSpawn,"도착 홀");
            worldCamera.transform.position=DestinationArrivalSpawn;worldCamera.transform.rotation=Quaternion.LookRotation(DestinationArrivalFacing);
        }
        Transform Part(string name,bool building)
        {
            var t=new GameObject(name).transform;t.SetParent(destinationSite,false);
            t.gameObject.AddComponent<Structure>().proxy=building;return t;
        }

        void BuildDestinationAirfield(KoreanAirport a)
        {
            float half=a.runway*.5f,rz=DD*.5f+RunwayOffset;
            var ground=Part("공항 부지",false);
            Block("잔디",ground,new Vector3(-half-600,-.4f,-DD*.5f-400),new Vector3(half+600,-.2f,rz+500),Mat("district-ground",new Color(.34f,.43f,.35f),0,"grass",160f));
            var runway=Part("활주로",false);
            var asphalt=Mat("runway",new Color(.21f,.23f,.24f),0,"asphalt",40f);var paint=Mat("runway-paint",new Color(.93f,.93f,.9f));
            Block("활주로 포장",runway,new Vector3(-half,-.2f,rz-RunwayWidth*.5f),new Vector3(half,0,rz+RunwayWidth*.5f),asphalt);
            for(float x=-half+120;x<half-120;x+=60)Block("활주로 중심선",runway,new Vector3(x,0,rz-.45f),new Vector3(x+30,.03f,rz+.45f),paint,false);
            foreach(float end in new[]{-half+8,half-38})for(int k=-4;k<=4;k++)if(k!=0)
                Block("활주로 시단 표지",runway,new Vector3(end,0,rz+k*4.6f-.9f),new Vector3(end+30,.03f,rz+k*4.6f+.9f),paint,false);
            var taxi=Part("유도로",false);var taxiMat=Mat("taxiway",new Color(.27f,.28f,.28f),0,"asphalt",30f);var line=Mat("taxi-line",new Color(.95f,.78f,.2f));
            float tz=DD*.5f+TaxiLane,entry=-half+60;
            Block("평행 유도로",taxi,new Vector3(entry-TaxiWidth*.5f,-.18f,tz-TaxiWidth*.5f),new Vector3(DW*.5f+60,0,tz+TaxiWidth*.5f),taxiMat);
            Block("연결 유도로",taxi,new Vector3(entry-TaxiWidth*.5f,-.18f,tz),new Vector3(entry+TaxiWidth*.5f,0,rz),taxiMat);
            Block("유도로 중심선",taxi,new Vector3(entry,0,tz-.2f),new Vector3(DW*.5f+60,.03f,tz+.2f),line,false);
            Block("유도로 중심선",taxi,new Vector3(entry-.2f,0,tz),new Vector3(entry+.2f,.03f,rz),line,false);
            var apron=Part("계류장",false);
            Block("계류장 포장",apron,new Vector3(-DW*.5f-60,-.16f,DD*.5f),new Vector3(DW*.5f+60,0,tz-TaxiWidth*.5f),Mat("apron",new Color(.66f,.66f,.63f),0,"concrete",30f));
            for(int g=1;g<=a.Gates;g++)
            {
                float x=GateLocalX(g);
                Block("주기장 유도선",apron,new Vector3(x-.2f,0,DD*.5f+6),new Vector3(x+.2f,.03f,tz-TaxiWidth*.5f),line,false);
            }
        }

        void BuildDestinationTerminal(KoreanAirport a)
        {
            float w=DW,d=DD,h=TerminalHeight[a.size],x0=-w*.5f,x1=w*.5f,z0=-d*.5f,z1=d*.5f;
            var t=Part(a.name+" 여객터미널",true);
            var floor=Mat("destination-floor",new Color(.52f,.51f,.49f),0,"paving",8f);var wall=Mat("destination-wall",new Color(.70f,.71f,.70f));
            var glass=Mat("terminal-glass",new Color(.55f,.72f,.82f),.6f);var steel=Mat("terminal-frame",new Color(.22f,.25f,.28f),.5f);
            Block("바닥",t,new Vector3(x0,-.2f,z0),new Vector3(x1,0,z1),floor);
            Block("지붕",t,new Vector3(x0-3,h,z0-3),new Vector3(x1+3,h+.8f,z1+3),wall);
            Block("측벽",t,new Vector3(x0-.4f,0,z0),new Vector3(x0,h,z1),wall);
            Block("측벽",t,new Vector3(x1,0,z0),new Vector3(x1+.4f,h,z1),wall);
            // Landside: glass curtain wall with the exit doors in the middle.
            const float door=4f;
            Block("전면 유리벽",t,new Vector3(x0,0,z0-.3f),new Vector3(-door,h,z0),glass);
            Block("전면 유리벽",t,new Vector3(door,0,z0-.3f),new Vector3(x1,h,z0),glass);
            Block("출입구 상부",t,new Vector3(-door,3.4f,z0-.3f),new Vector3(door,h,z0),wall);
            var exit=Block("도착 출구 자동문",t,new Vector3(-door+.2f,0,z0-.25f),new Vector3(door-.2f,3.2f,z0-.05f),steel);
            var leave=exit.AddComponent<AirportExit>();leave.city=a.City;leave.airport=a.name;
            // Airside: glass between gate doors; each door is the boarding fixture for its aircraft.
            float last=x0;
            for(int g=1;g<=a.Gates;g++)
            {
                float x=GateLocalX(g);
                Block("탑승동 유리벽",t,new Vector3(last,0,z1),new Vector3(x-1.6f,h,z1+.3f),glass);
                Block("탑승구 상부",t,new Vector3(x-1.6f,2.8f,z1),new Vector3(x+1.6f,h,z1+.3f),wall);
                var gate=Block("탑승구 "+g+" 문",t,new Vector3(x-1.5f,0,z1+.05f),new Vector3(x+1.5f,2.8f,z1+.25f),steel);
                AddFixture(gate,"gate","탑승구 "+g+" · 탑승하기","",g);
                Sign("탑승구 "+g,t,SitePoint(x,3.4f,z1-.1f),-destinationSite.forward,.5f,new Color(1f,.85f,.3f));
                // A boxy jet bridge from the door to the aircraft's front door.
                Block("탑승교",t,new Vector3(x-1.4f,3.2f,z1+.3f),new Vector3(x+1.4f,6f,z1+StandDepth-22f),Mat("jet-bridge",new Color(.80f,.82f,.84f),.3f),false);
                // Seating beside the gate lane, so the walk from the hall to the door stays straight.
                for(int s=0;s<3;s++)Block("대기 좌석",t,new Vector3(x+3,0,z1-6-s*2.2f),new Vector3(x+9,.5f,z1-5.3f-s*2.2f),Mat("seat",new Color(.2f,.33f,.52f)));
                last=x+1.6f;
            }
            Block("탑승동 유리벽",t,new Vector3(last,0,z1),new Vector3(x1,h,z1+.3f),glass);
            // Square columns on a 12 m grid, kept out of the centre line and every gate lane.
            for(float x=x0+12;x<x1-6;x+=12)for(float z=z0+12;z<z1-6;z+=12)
                if(Mathf.Abs(x)>6&&!InGateLane(x,a.Gates))Block("기둥",t,new Vector3(x-.5f,0,z-.5f),new Vector3(x+.5f,h,z+.5f),wall);
            // Self check-in kiosks just inside the landside doors.
            int kiosks=2+a.size*2;
            for(int i=0;i<kiosks;i++)
            {
                float x=(i-(kiosks-1)*.5f)*2.2f+(i<kiosks/2?-door:door);
                var kiosk=Block("셀프 체크인 키오스크",t,new Vector3(x-.4f,0,z0+8),new Vector3(x+.4f,1.6f,z0+8.6f),Mat("kiosk",new Color(.85f,.86f,.88f),.3f));
                Block("키오스크 화면",t,new Vector3(x-.3f,1f,z0+8.6f),new Vector3(x+.3f,1.45f,z0+8.65f),Glow("kiosk-screen",new Color(.2f,.55f,.85f),.9f),false);
                AddFixture(kiosk,"kiosk","셀프 체크인 키오스크 · 목적지 공항 선택","");
            }
            Marker(SitePoint(0,0,z0+8),"키오스크");
            // Arrivals: a baggage belt (a low box) along the right-hand wall, clear of the walk from the gates to the doors.
            float bx=x1-10.5f;var belt=Mat("baggage-belt",new Color(.25f,.27f,.29f),.4f);
            Block("수하물 수취대",t,new Vector3(bx-1.5f,0,z0+4),new Vector3(bx+1.5f,.7f,z1-14),belt);
            Sign("수하물 수취대\nBaggage Claim",t,SitePoint(bx,4f,z0+4),-destinationSite.forward,.4f,Color.white);
            Sign(a.name+"\n"+a.english+" ("+a.iata+")",t,SitePoint(0,h-2.2f,z0-.4f),-destinationSite.forward,1.3f,Color.white);
            Sign("도착 · Arrivals\n출구 Exit ↓",t,SitePoint(0,4.4f,z0+.4f),destinationSite.forward,.45f,new Color(1f,.85f,.3f));
            Sign(a.name+" "+a.iata,t,SitePoint(0,h-1.6f,z1-.4f),-destinationSite.forward,.8f,Color.white);
            var lamp=new GameObject("터미널 조명").AddComponent<Light>();lamp.transform.SetParent(t,false);lamp.transform.localPosition=new Vector3(0,h-1,0);
            lamp.type=LightType.Point;lamp.range=w*.7f;lamp.intensity=.6f;lamp.color=new Color(1f,.96f,.88f);
        }

        // Under the terminal roof: lit by the hall lamps, not the sun (Block pieces cast no shadows).
        public bool IsDestinationTerminalInterior(Vector3 eye)
        {
            if(DestinationAirport==null||destinationSite==null)return false;
            var p=destinationSite.InverseTransformPoint(eye);
            return Mathf.Abs(p.x)<DW*.5f&&Mathf.Abs(p.z)<DD*.5f&&p.y<TerminalHeight[DestinationAirport.size];
        }
        bool InGateLane(float x,int gates){for(int g=1;g<=gates;g++)if(Mathf.Abs(x-GateLocalX(g))<3.5f)return true;return false;}
        void BuildDestinationTower(KoreanAirport a)
        {
            float x=DW*.5f+70,z=DD*.5f+30,h=26+a.size*10;
            var t=Part("관제탑",true);var concrete=Mat("tower-concrete",new Color(.8f,.8f,.78f));
            Block("관제탑 기둥",t,new Vector3(x-4,0,z-4),new Vector3(x+4,h,z+4),concrete);
            Block("관제실",t,new Vector3(x-6.5f,h,z-6.5f),new Vector3(x+6.5f,h+5,z+6.5f),Mat("tower-glass",new Color(.3f,.45f,.5f),.6f));
            Block("관제탑 지붕",t,new Vector3(x-7,h+5,z-7),new Vector3(x+7,h+5.6f,z+7),concrete);
        }

        void BuildDestinationForecourt(KoreanAirport a)
        {
            float z0=-DD*.5f,reach=DW*.5f+90;
            var road=Part("공항 진입로",false);var asphalt=Mat("road",new Color(.17f,.21f,.24f),0,"osm_road",4f);
            Block("보도",road,new Vector3(-reach,-.1f,z0-6),new Vector3(reach,.15f,z0),Mat("sidewalk",new Color(.6f,.6f,.57f),0,"paving",3f));
            Block("진입 도로",road,new Vector3(-reach,-.15f,z0-20),new Vector3(reach,0,z0-6),asphalt);
            for(float x=-reach+6;x<reach-6;x+=12)Block("차선",road,new Vector3(x,0,z0-13.1f),new Vector3(x+6,.03f,z0-12.9f),Mat("lane",new Color(.91f,.82f,.59f)),false);
            var lot=Part("주차장",false);
            Block("주차장 포장",lot,new Vector3(-reach,-.15f,z0-80),new Vector3(reach,0,z0-24),asphalt);
            for(float x=-reach+4;x<reach-4;x+=2.6f)Block("주차 구획선",lot,new Vector3(x,0,z0-60),new Vector3(x+.12f,.03f,z0-54),Mat("parking-line",new Color(.92f,.92f,.88f)),false);
        }

        // Gate stand: nose about 18 m from the airside glass.
        Vector3 DestinationGatePlane(int gate){return SitePoint(GateLocalX(gate),0,DD*.5f+StandDepth);}
        Vector3[] DestinationDepartureTaxi(int gate)
        {
            float half=DestinationAirport.runway*.5f,rz=DD*.5f+RunwayOffset,tz=DD*.5f+TaxiLane,entry=-half+60,x=GateLocalX(gate);
            var local=new[]{new Vector3(x,0,DD*.5f+StandDepth+20),new Vector3(x,0,tz),new Vector3(entry,0,tz),new Vector3(entry,0,rz),new Vector3(entry+LineupRun,0,rz)};
            var path=AirfieldRounded(local,40);
            for(int i=0;i<path.Count;i++)path[i]=destinationSite.TransformPoint(path[i]);
            return path.ToArray();
        }
    }
}
