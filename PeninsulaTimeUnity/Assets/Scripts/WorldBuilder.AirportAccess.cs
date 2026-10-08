using UnityEngine;

namespace PeninsulaTime
{
    // Recorded centre lines are also used by the physical walking regression check.
    public class AirportWalkRoute : MonoBehaviour { public Vector3[] points; }

    public partial class WorldBuilder
    {
        public Vector3 AirportSecuritySpawn { get { return AirportOrigin+new Vector3(-10.5f,Floor3+1.65f,-4); } }
        public Vector3 AirportBoardingDoor(int gate) { return AirportOrigin+new Vector3(GateX(gate)+2.2f,3,57.8f); }

        void AirportFloor(string name,float y,float x0,float x1,float z0,float z1,Rect hole,Material mat)
        {
            Block(name,airport,new Vector3(x0,y-.4f,z0),new Vector3(hole.xMin,y,z1),mat);
            Block(name,airport,new Vector3(hole.xMax,y-.4f,z0),new Vector3(x1,y,z1),mat);
            if(hole.yMin>z0)Block(name,airport,new Vector3(hole.xMin,y-.4f,z0),new Vector3(hole.xMax,y,hole.yMin),mat);
            if(hole.yMax<z1)Block(name,airport,new Vector3(hole.xMin,y-.4f,hole.yMax),new Vector3(hole.xMax,y,z1),mat);
        }

        void AirportStairPair(Vector3 start,float rise,string label)
        {
            const float run=14f;
            foreach(float side in new[]{-1f,1f})
            {
                var at=start+Vector3.right*side*1.1f;
                Escalator(at,1.9f,run,rise);
                var record=new GameObject(label+(side<0?" 계단":" 에스컬레이터")).AddComponent<AirportWalkRoute>();
                record.transform.SetParent(airport,false);
                record.points=new[]{AirportOrigin+at-Vector3.forward,AirportOrigin+at,AirportOrigin+at+new Vector3(0,rise,run),AirportOrigin+at+new Vector3(0,rise,run+2)};
            }
            var rail=Mat("terminal-steel",new Color(.70f,.72f,.75f),.6f);
            // Upper floor guard rails border the opening, never the arrival landing.
            foreach(float side in new[]{-1f,1f})
                Block("계단실 보호 난간",airport,start+new Vector3(side*3.7f-.04f,rise,-1.8f),start+new Vector3(side*3.7f+.04f,rise+1.1f,run-.4f),rail);
            Board(label+" ↑",airport,AirportOrigin+start+new Vector3(0,3.4f,-1.5f),Vector3.back,new Vector2(5,.5f),new Color(.1f,.24f,.45f),Color.white,.25f);
        }

        void BuildAirportSecurity()
        {
            float y=Floor3;
            var partition=Mat("security-wall",new Color(.82f,.84f,.86f));
            // Two entrances, matching the two landside departure entries in the KAC 3F plan.
            foreach(var limits in new[]{new Vector2(-TerminalHalfX,-15.2f),new Vector2(-8.8f,8.8f),new Vector2(15.2f,TerminalHalfX)})
                Block("3층 보안검색 칸막이",airport,new Vector3(limits.x,y,-.15f),new Vector3(limits.y,Floor4,.15f),partition);
            var metal=Mat("terminal-steel",new Color(.7f,.72f,.75f),.6f);
            foreach(float entrance in new[]{-12f,12f})
            {
                Board("국내선 출발 · 신분증/탑승권 확인",airport,AirportOrigin+new Vector3(entrance,y+3.1f,-.3f),Vector3.back,new Vector2(6,.65f),new Color(.10f,.24f,.45f),Color.white,.23f);
                Block("출발장 상부",airport,new Vector3(entrance-3.2f,y+3.5f,-.15f),new Vector3(entrance+3.2f,Floor4,.15f),partition);
                foreach(float offset in new[]{-1.5f,1.5f})
                {
                    float cx=entrance+offset;
                    var lane=new GameObject("3층 보안검색 통로");lane.transform.SetParent(airport,false);lane.transform.localPosition=new Vector3(cx,y,0);
                    var blocker=lane.AddComponent<BoxCollider>();blocker.center=new Vector3(0,1.2f,0);blocker.size=new Vector3(2.9f,2.4f,.3f);
                    var pivot=new GameObject("차단봉 축").transform;pivot.SetParent(lane.transform,false);pivot.localPosition=new Vector3(-1.4f,1,0);
                    var bar=Primitive(PrimitiveType.Cube,"차단봉",pivot,new Vector3(1.4f,0,0),new Vector3(2.8f,.08f,.08f),Mat("gate-flap",new Color(.85f,.25f,.2f),.1f));
                    DestroyImmediate(bar.GetComponent<Collider>());
                    var barrier=lane.AddComponent<Barrier>();barrier.kind="security";barrier.blocker=blocker;barrier.flaps=new[]{pivot};
                    Block("검색대 기둥",airport,new Vector3(cx-1.55f,y,-.4f),new Vector3(cx-1.45f,y+2.6f,.4f),metal);
                    Block("엑스레이 검색기",airport,new Vector3(cx-1.4f,y,1.8f),new Vector3(cx-.65f,y+1.3f,5.2f),Mat("xray",new Color(.55f,.58f,.62f),.3f));
                    Block("문형 금속탐지기",airport,new Vector3(cx+.25f,y,3),new Vector3(cx+.4f,y+2.5f,3.25f),metal,false);
                    Block("문형 금속탐지기",airport,new Vector3(cx+1.3f,y,3),new Vector3(cx+1.45f,y+2.5f,3.25f),metal,false);
                    Block("문형 금속탐지기",airport,new Vector3(cx+.25f,y+2.4f,3),new Vector3(cx+1.45f,y+2.55f,3.25f),metal,false);
                }
            }
        }

        void BuildAirportUpperFloor()
        {
            AirportStairPair(new Vector3(48,Floor3,-24),Floor4-Floor3,"3층 → 4층 식당가");
            var desk=Mat("gate-desk",new Color(.30f,.36f,.42f));
            string[] shops={"한식 식당","면 요리","카페","푸드코트"};
            for(int i=0;i<shops.Length;i++)
            {
                float x=-40+i*20;
                var fixture=Block(shops[i],airport,new Vector3(x-5,Floor4,-29),new Vector3(x+5,Floor4+1.1f,-27),desk);
                AddFixture(fixture,"shop",shops[i]+" 들르기",shops[i]+"에서 식사합니다.");
                Board(shops[i],airport,AirportOrigin+new Vector3(x,Floor4+2.8f,-29),Vector3.forward,new Vector2(7,.55f),new Color(.4f,.27f,.16f),Color.white,.3f);
            }
            for(int i=0;i<6;i++)
                Block("식당 테이블",airport,new Vector3(-36+i*12,Floor4,-17),new Vector3(-33+i*12,Floor4+.75f,-15),desk);
            Block("4층 전망 난간",airport,new Vector3(-TerminalHalfX,Floor4,-.2f),new Vector3(TerminalHalfX,Floor4+1.2f,0),Mat("terminal-glass",new Color(.62f,.8f,.9f),.35f));
            Board("4F 식당가 · 3F 출발장 ↓",airport,AirportOrigin+new Vector3(48,Floor4+2.8f,-8),Vector3.back,new Vector2(6,.6f),new Color(.1f,.24f,.45f),Color.white,.25f);
        }

        void BuildAirportMovingWalkways()
        {
            // Short belts between gate approaches leave each gate's transverse path open.
            foreach(float x in new[]{-120f,-80f,-40f,40f,80f,120f})foreach(float side in new[]{-1f,1f})
            {
                var belt=Primitive(PrimitiveType.Cube,"공항 탑승동 무빙워크",airport,new Vector3(x,Floor3+.025f,22+side*.72f),new Vector3(1.1f,.05f,24),Mat("walkway-belt",new Color(.18f,.21f,.23f),.45f,"metal",8));
                belt.transform.localRotation=Quaternion.LookRotation(Vector3.right*side);
                var carrier=belt.AddComponent<MovingWalkway>();carrier.length=24;carrier.width=1.1f;carrier.Register();
                for(float dx=-10;dx<=10;dx+=5)
                {
                    var p=AirportOrigin+new Vector3(x+dx,Floor3+.06f,22+side*.72f);var tip=p+Vector3.right*side*.45f;
                    foreach(float wing in new[]{-1f,1f})WalkSlab(airport,"무빙워크 진행 화살표",p+Vector3.forward*wing*.25f,tip,.05f,.015f,Glow("walkway-arrow",new Color(.3f,.9f,.55f)),false);
                }
            }
        }

        void BuildAirportGateFacade(Material glass)
        {
            float from=-PierHalfX;
            for(int g=1;g<=8;g++)
            {
                float x=GateX(g)+5;
                Block("탑승동 유리벽",airport,new Vector3(from,Floor3,TerminalHalfZ),new Vector3(x-1.6f,RoofY,TerminalHalfZ+.15f),glass);
                Block("탑승교 출입구 상부",airport,new Vector3(x-1.6f,Floor3+3.2f,TerminalHalfZ),new Vector3(x+1.6f,RoofY,TerminalHalfZ+.15f),glass);
                from=x+1.6f;
            }
            Block("탑승동 유리벽",airport,new Vector3(from,Floor3,TerminalHalfZ),new Vector3(PierHalfX,RoofY,TerminalHalfZ+.15f),glass);
        }

        void BuildWalkableJetBridge(int gate,float x,Material bridge)
        {
            var a=AirportOrigin+new Vector3(x+5,Floor3,TerminalHalfZ-.2f);
            var b=AirportBoardingDoor(gate);var direction=(b-a).normalized;var right=Vector3.Cross(Vector3.up,direction).normalized;
            WalkSlab(airport,"탑승교 보행 바닥",a,b,2.8f,.2f,bridge,true);
            WalkSlab(airport,"탑승교 지붕",a+Vector3.up*3,b+Vector3.up*3,3f,.15f,bridge,false);
            foreach(float side in new[]{-1f,1f})
            {
                var wallEnd=side<0?b-direction*2.0f:b;
                var wall=Primitive(PrimitiveType.Cube,"탑승교 측벽",airport,Vector3.zero,new Vector3(.12f,2.6f,Vector3.Distance(a,wallEnd)),bridge);
                wall.transform.position=(a+wallEnd)*.5f+right*side*1.45f+Vector3.up*1.3f;wall.transform.rotation=Quaternion.LookRotation(direction);
                WalkSlab(airport,"탑승교 조명",a+right*side*.65f+Vector3.up*2.8f,b+right*side*.65f+Vector3.up*2.8f,.1f,.025f,Glow("terminal-light",Color.white,1.2f),false);
            }
            var record=new GameObject("탑승교 보행 경로 "+gate).AddComponent<AirportWalkRoute>();record.transform.SetParent(airport,false);
            record.points=new[]{a-Vector3.forward*2,a,b-direction*1.2f};
            // The last flat landing turns into the front left aircraft door.
            Block("항공기 연결 발판",airport,new Vector3(x-1,2.8f,56.4f),new Vector3(x+3.7f,3,59.2f),bridge);
            // A real end wall stops a player holding forward before the flat landing ends.
            // Keep the aircraft-side opening clear: the last two metres of the left wall are absent.
            var horizontal=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
            var cap=Primitive(PrimitiveType.Cube,"탑승교 끝 막음벽",airport,Vector3.zero,new Vector3(3f,2.8f,.16f),bridge);
            cap.transform.position=b+horizontal*.65f+Vector3.up*1.4f;
            cap.transform.rotation=Quaternion.LookRotation(horizontal);
            var returnWall=Primitive(PrimitiveType.Cube,"탑승교 끝 오른쪽 난간",airport,Vector3.zero,new Vector3(.12f,2.8f,.95f),bridge);
            returnWall.transform.position=b+horizontal*.25f+right*1.45f+Vector3.up*1.4f;
            returnWall.transform.rotation=Quaternion.LookRotation(horizontal);
            Board("기내로 ←",airport,b+horizontal*.55f+Vector3.up*2.45f,-horizontal,new Vector2(2.5f,.5f),new Color(.1f,.24f,.45f),Color.white,.3f);
        }
    }
}
