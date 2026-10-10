using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Seoul Station's main concourse (맞이방), a deck over the north end of the KTX/ITX platforms as at the real
    // station: ticket office and ticket machines, waiting seats, shops, a departure board, and a stair down to each
    // platform pair through a ticket check line; stairs at both ends lead to the east plaza (subway entrances for
    // AREX and lines 1 and 4) and to the west side. Platform positions come from OSM; the floor plan is a game
    // layout, not a surveyed one.
    public partial class WorldBuilder
    {
        public const float SeoulHallFloor=9f;
        const float SeoulHallHeight=11f,SeoulStairTop=78f,SeoulStairHalf=2.2f,SeoulDoorZ=60f,SeoulDoorHalf=4f;
        static readonly Rect SeoulHall=Rect.MinMaxRect(-118,30,52,96); // x (= -east), z (= -north)
        readonly List<KeyValuePair<string,List<Vector3>>> railPlatformShapes=new List<KeyValuePair<string,List<Vector3>>>();
        public readonly List<Vector3> SeoulPlatformStairs=new List<Vector3>(); // foot of each platform stair
        public Transform SeoulStationHall{get;private set;}
        // Where the 3D button starts: the middle of the 맞이방, facing the platform stairs.
        public Vector3 SeoulStationConcourse{get{return new Vector3(-33,SeoulHallFloor,56);}}
        public Vector3 SeoulStationConcourseFacing{get{return Vector3.forward;}}
        public Vector3 SeoulHallEastDoor{get{return new Vector3(SeoulHall.xMin+1.5f,SeoulHallFloor,SeoulDoorZ);}}
        public Vector3 SeoulHallWestDoor{get{return new Vector3(SeoulHall.xMax-1.5f,SeoulHallFloor,SeoulDoorZ);}}

        // Centre x of a platform polygon where it crosses z (NaN if it does not), and its width there.
        static float PlatformCentreAt(List<Vector3> shape,float z,out float width)
        {
            float min=float.MaxValue,max=float.MinValue;
            for(int i=0;i<shape.Count;i++)
            {
                var a=shape[i];var b=shape[(i+1)%shape.Count];
                if((a.z-z)*(b.z-z)>0||Mathf.Approximately(a.z,b.z))continue;
                float x=Mathf.Lerp(a.x,b.x,(z-a.z)/(b.z-a.z));min=Mathf.Min(min,x);max=Mathf.Max(max,x);
            }
            width=max-min;return min<=max?(min+max)*.5f:float.NaN;
        }
        // The walkable height at (x, z): the highest collider surface below 30 m, else 0.
        static float GroundAt(float x,float z){RaycastHit hit;return Physics.Raycast(new Vector3(x,30,z),Vector3.down,out hit,40f)?hit.point.y:0f;}

        void BuildSeoulStationHall()
        {
            SeoulPlatformStairs.Clear();
            float F=SeoulHallFloor,top=F+SeoulHallHeight;
            // OSM building blocks standing where the hall and its outside stairs are give way to it.
            var site=Rect.MinMaxRect(SeoulHall.xMin-2*F-4,SeoulHall.yMin-2,SeoulHall.xMax+2*F+4,SeoulHall.yMax+2);
            foreach(var s in root.GetComponentsInChildren<Structure>(true))
            {
                if(!s.proxy)continue;
                Bounds b=default(Bounds);bool any=false;
                foreach(var r in s.GetComponentsInChildren<Renderer>(true)){if(any)b.Encapsulate(r.bounds);else{b=r.bounds;any=true;}}
                if(any&&b.max.x>site.xMin&&b.min.x<site.xMax&&b.max.z>site.yMin&&b.min.z<site.yMax)DestroyImmediate(s.gameObject);
            }
            Physics.SyncTransforms();
            BridgeKtxPlatforms();
            var hall=new GameObject("서울역 맞이방").transform;hall.SetParent(root.transform,false);SeoulStationHall=hall;
            hall.gameObject.AddComponent<Structure>().proxy=true;
            var floor=Mat("seoul-hall-floor",new Color(.52f,.51f,.49f),0,"granite",3f);
            var glass=Mat("station-glass",new Color(.55f,.72f,.78f),.3f);
            var steel=Mat("seoul-hall-steel",new Color(.62f,.65f,.68f),.6f);
            var roof=Mat("seoul-hall-roof",new Color(.78f,.82f,.84f),.2f,"metal",6f);
            var stone=Mat("stair-stone",new Color(.70f,.70f,.68f),0,"granite",1);
            var yellow=Mat("tactile",new Color(.95f,.78f,.15f));
            var counterMat=Mat("station-counter",new Color(.30f,.36f,.42f));
            var blue=new Color(.07f,.22f,.45f);

            // Stairs down to every platform pair that reaches under the hall's south edge.
            var stairs=new List<KeyValuePair<string,Vector3>>();
            foreach(var pair in railPlatformShapes)
            {
                float width;float probe=SeoulStairTop+16,x=PlatformCentreAt(pair.Value,probe,out width);
                if(float.IsNaN(x)||width<SeoulStairHalf*2-.4f||x-SeoulStairHalf<SeoulHall.xMin+2||x+SeoulStairHalf>SeoulHall.xMax-2)continue;
                if(stairs.Exists(s=>Mathf.Abs(s.Value.x-x)<SeoulStairHalf*2+1))continue;
                RaycastHit hit;
                if(!Physics.Raycast(new Vector3(x,F-1,probe),Vector3.down,out hit,F+2)||hit.collider.name.ToLowerInvariant()!="platform")continue;
                stairs.Add(new KeyValuePair<string,Vector3>(pair.Key.Replace(';','·'),new Vector3(x,hit.point.y,0)));
            }
            stairs.Sort((a,b)=>a.Value.x.CompareTo(b.Value.x));

            // Floor: whole north of the stair line; south of it, cut round each stairwell.
            Block("맞이방 바닥",hall,new Vector3(SeoulHall.xMin,F-.5f,SeoulHall.yMin),new Vector3(SeoulHall.xMax,F,SeoulStairTop),floor);
            float from=SeoulHall.xMin;
            foreach(var s in stairs){Block("맞이방 바닥",hall,new Vector3(from,F-.5f,SeoulStairTop),new Vector3(s.Value.x-SeoulStairHalf,F,SeoulHall.yMax),floor);from=s.Value.x+SeoulStairHalf;}
            Block("맞이방 바닥",hall,new Vector3(from,F-.5f,SeoulStairTop),new Vector3(SeoulHall.xMax,F,SeoulHall.yMax),floor);
            // The roof shades the hall (Block turns shadows off); daylight comes in through the roof lights only.
            Block("맞이방 지붕",hall,new Vector3(SeoulHall.xMin-1,top,SeoulHall.yMin-1),new Vector3(SeoulHall.xMax+1,top+.4f,SeoulHall.yMax+1),roof,false).GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            for(float x=SeoulHall.xMin+6;x<SeoulHall.xMax-4;x+=12)Block("천창",hall,new Vector3(x-2,top+.41f,SeoulHall.yMin+6),new Vector3(x+2,top+.45f,SeoulHall.yMax-6),glass,false);
            // Glass walls, with a door in the middle of each end where the outside stairs arrive.
            foreach(float x in new[]{SeoulHall.xMin,SeoulHall.xMax})
            {
                Block("유리벽",hall,new Vector3(x-.1f,F,SeoulHall.yMin),new Vector3(x+.1f,top,SeoulDoorZ-SeoulDoorHalf),glass);
                Block("유리벽",hall,new Vector3(x-.1f,F,SeoulDoorZ+SeoulDoorHalf),new Vector3(x+.1f,top,SeoulHall.yMax),glass);
                Block("출입문 위 벽",hall,new Vector3(x-.1f,F+3.4f,SeoulDoorZ-SeoulDoorHalf),new Vector3(x+.1f,top,SeoulDoorZ+SeoulDoorHalf),glass,false);
            }
            Block("유리벽",hall,new Vector3(SeoulHall.xMin,F,SeoulHall.yMin-.1f),new Vector3(SeoulHall.xMax,top,SeoulHall.yMin+.1f),glass);
            Block("유리벽",hall,new Vector3(SeoulHall.xMin,F,SeoulHall.yMax-.1f),new Vector3(SeoulHall.xMax,top,SeoulHall.yMax+.1f),glass);
            // Columns from each platform up through the deck, clear of the seats, stairs and tracks.
            foreach(var s in stairs)foreach(float z in new[]{40f,70f})
            {
                float x=s.Value.x+(z<50?0:SeoulStairHalf+1.2f);
                Block("기둥",hall,new Vector3(x-.5f,s.Value.y,z-.5f),new Vector3(x+.5f,top,z+.5f),steel);
            }
            // Platform stairs, each behind a ticket check line with its departure sign.
            foreach(var s in stairs)
            {
                float x=s.Value.x,y=s.Value.y,bottom=SeoulStairTop+2*(F-y);
                StairFlight(hall,new Vector3(x,F,SeoulStairTop),new Vector3(x,y,bottom),SeoulStairHalf*2,stone,yellow);
                SeoulPlatformStairs.Add(new Vector3(x,y,bottom+1));
                foreach(float side in new[]{-1f,1f})
                    Block("계단 난간",hall,new Vector3(x+side*SeoulStairHalf-.06f,F,SeoulStairTop),new Vector3(x+side*SeoulStairHalf+.06f,F+1.1f,SeoulHall.yMax),glass);
                foreach(float gx in new[]{-1.4f,1.4f})Block("승차권 확인 게이트",hall,new Vector3(x+gx-.15f,F,SeoulStairTop-2.6f),new Vector3(x+gx+.15f,F+1f,SeoulStairTop-1.4f),steel);
                Board(s.Key+"번 타는 곳",hall,new Vector3(x,F+3.3f,SeoulStairTop-2f),Vector3.back,new Vector2(4.2f,.7f),blue,Color.white,.3f);
                Marker(new Vector3(x,F,SeoulStairTop-2),s.Key+" 타는 곳");
            }
            // East: ticket office, ticket machines and customer service.
            var counter=Block("매표소 창구",hall,new Vector3(-112,F,31),new Vector3(-90,F+1.1f,33.4f),counterMat);
            Block("매표소 유리",hall,new Vector3(-112,F+1.1f,33.2f),new Vector3(-90,F+2.6f,33.3f),glass,false);
            Board("승차권 판매 · Tickets",hall,new Vector3(-101,F+3.2f,33.5f),Vector3.forward,new Vector2(9,.7f),blue,Color.white,.3f);
            AddFixture(counter,"info","매표소에서 묻기","서울역 매표소: KTX·ITX·무궁화호 승차권을 팝니다. 타는 곳은 남쪽 계단 위 번호를 보세요.");
            for(int i=0;i<6;i++)
            {
                var machine=CityModel("TicketMachine",new Vector3(-88+i*2.2f,F,32.2f),1f,180f);
                if(machine==null)continue;machine.transform.SetParent(hall,true);FitBoxCollider(machine);
                AddFixture(machine,"info","승차권 자동발매기","목적지와 시간을 고르면 승차권이 나옵니다. 열차는 타는 곳 번호의 계단으로 내려가 타세요.");
            }
            Board("승차권 자동발매기",hall,new Vector3(-82.5f,F+2.6f,31),Vector3.forward,new Vector2(6,.5f),blue,Color.white,.24f);
            var service=Block("고객지원실",hall,new Vector3(-116,F,40),new Vector3(-112,F+1.1f,50),counterMat);
            AddFixture(service,"info","고객지원실","분실물·휠체어 지원·환승 안내: 공항철도와 지하철 1·4호선은 동쪽 계단을 내려가 지하철 출입구로 가세요.");
            Board("고객지원실",hall,new Vector3(-111.9f,F+2.4f,45),Vector3.right,new Vector2(4,.5f),blue,Color.white,.24f);
            // Centre: waiting seats either side of an aisle, and the departure board over them.
            var seat=Mat("bench",new Color(.55f,.40f,.26f),0,"timber",1f);
            foreach(float z in new[]{44f,48f,52f,62f,66f})foreach(var span in new[]{new Vector2(-62,-37),new Vector2(-29,-4)})
                Block("대합실 의자",hall,new Vector3(span.x,F+.42f,z),new Vector3(span.y,F+.5f,z+.55f),seat);
            Block("출발 안내 전광판",hall,new Vector3(-48,F+4.4f,30.2f),new Vector3(-18,F+7.6f,30.5f),Mat("arrival-screen",new Color(.03f,.03f,.04f)),false);
            Sign("출발  Departures\nKTX 부산 · 동대구 · 대전\nKTX 목포 · 광주송정\nITX-새마을 · 무궁화호 경부선",hall,new Vector3(-33,F+6,30.6f),Vector3.forward,.42f,new Color(1f,.75f,.2f));
            Board("서울역  Seoul Station",hall,new Vector3(-33,top-1.2f,SeoulHall.yMax-.3f),Vector3.back,new Vector2(16,1.2f),new Color(.12f,.13f,.15f),Color.white,.6f);
            // West: shops along the north wall, and a food court.
            string[,] shops={{"편의점","생수","삼각김밥"},{"카페","아메리카노","라떼"},{"베이커리","소보로빵","크루아상"},{"서점","여행 안내서","잡지"},{"약국","소화제","밴드"}};
            var tints=new[]{new Color(.12f,.45f,.62f),new Color(.45f,.30f,.20f),new Color(.72f,.45f,.20f),new Color(.30f,.45f,.30f),new Color(.20f,.55f,.40f)};
            for(int i=0;i<5;i++)
            {
                float x0=-6+i*10.5f,x1=x0+10;
                Block("점포 벽",hall,new Vector3(x1-.2f,F,SeoulHall.yMin),new Vector3(x1,F+3.4f,SeoulHall.yMin+7),Mat("shop-wall",new Color(.78f,.78f,.76f)));
                Block("점포 간판",hall,new Vector3(x0,F+3f,SeoulHall.yMin+6.8f),new Vector3(x1-.2f,F+3.6f,SeoulHall.yMin+7),Glow("seoul-shop-"+i,tints[i],.8f),false);
                Sign(shops[i,0],hall,new Vector3((x0+x1)*.5f,F+3.3f,SeoulHall.yMin+7.05f),Vector3.forward,.36f,Color.white);
                var till=Block("계산대",hall,new Vector3(x0+1,F,SeoulHall.yMin+4.4f),new Vector3(x1-1.2f,F+1f,SeoulHall.yMin+5f),counterMat);
                AddFixture(till,"shop",shops[i,0]+" 들르기",shops[i,0]+"에 들렀습니다.");
                for(int k=1;k<3;k++)
                {
                    var product=Block(shops[i,k],hall,new Vector3(x0+1+(k-1)*3,F+1f,SeoulHall.yMin+4.5f),new Vector3(x0+1.6f+(k-1)*3,F+1.4f,SeoulHall.yMin+4.9f),Mat("product-seoul-"+i+"-"+k,Color.Lerp(tints[i],Color.white,.35f)));
                    AddFixture(product,"shop",shops[i,k]+" 구매",shops[i,k]);
                }
            }
            for(int i=0;i<4;i++)foreach(float dz in new[]{0f,5f})Block("식탁",hall,new Vector3(4+i*10,F+.7f,46+dz),new Vector3(6+i*10,F+.75f,48+dz),seat);
            // Ways out: east plaza (subway entrances) and the west side.
            Board("← 공항철도 · 지하철 1·4호선 · 동쪽 광장",hall,new Vector3(SeoulHall.xMin+.3f,F+4f,SeoulDoorZ),Vector3.right,new Vector2(9,.6f),new Color(0f,.56f,.82f),Color.white,.26f);
            Board("서부역 · 서쪽 출구 →",hall,new Vector3(SeoulHall.xMax-.3f,F+4f,SeoulDoorZ),Vector3.left,new Vector2(6,.6f),blue,Color.white,.26f);
            float east=SeoulHall.xMin-2*F,west=SeoulHall.xMax+2*F;
            StairFlight(hall,new Vector3(SeoulHall.xMin,F,SeoulDoorZ),new Vector3(east,GroundAt(east-1,SeoulDoorZ),SeoulDoorZ),SeoulDoorHalf*2,stone,yellow);
            StairFlight(hall,new Vector3(SeoulHall.xMax,F,SeoulDoorZ),new Vector3(west,GroundAt(west+1,SeoulDoorZ),SeoulDoorZ),SeoulDoorHalf*2,stone,yellow);
            // Light, and people waiting.
            for(float x=SeoulHall.xMin+14;x<SeoulHall.xMax;x+=28)
            {
                var lamp=new GameObject("맞이방 조명").AddComponent<Light>();lamp.transform.SetParent(hall,false);
                lamp.transform.position=new Vector3(x,top-1.5f,62);lamp.type=LightType.Point;lamp.range=36;lamp.intensity=1.1f;lamp.color=new Color(1f,.96f,.88f);
            }
            Physics.SyncTransforms();
            var random=new System.Random(1925);
            for(int i=0;i<24;i++)
            {
                Transform[] legs;var at=new Vector3(Mathf.Lerp(SeoulHall.xMin+6,SeoulHall.xMax-6,(float)random.NextDouble()),F,Mathf.Lerp(38,74,(float)random.NextDouble()));
                if(Mathf.Abs(at.x+33)<3||Physics.CheckSphere(at+Vector3.up*.9f,.4f))continue;
                Bystander(at,new Vector3((float)random.NextDouble()-.5f,0,(float)random.NextDouble()-.5f),hall,random,out legs);
            }
            Marker(SeoulStationConcourse,"서울역 맞이방");
            Physics.SyncTransforms();
        }
        // The OSM platform outlines stand a few metres off the mapped KTX tracks: at each set's stopping point a
        // platform-height deck closes the gap, so riders step straight between the doors and the platform.
        void BridgeKtxPlatforms()
        {
            var deck=Mat("platform-concrete",new Color(.70f,.69f,.65f),0,"concrete",2f);
            foreach(var rail in PlatformTrains)
            {
                Vector3 at,heading;
                if(rail==null||!rail.ApproachPose(0,out at,out heading))continue;
                heading.y=0;if(heading.sqrMagnitude<.01f)continue;heading.Normalize();
                var right=Vector3.Cross(Vector3.up,heading);
                foreach(float side in new[]{-1f,1f})
                {
                    RaycastHit edge,top;
                    if(!Physics.Raycast(at+Vector3.up*.5f,right*side,out edge,12f)||edge.collider.name.ToLowerInvariant()!="platform")continue;
                    if(!Physics.Raycast(edge.point+right*side*.4f+Vector3.up*3f,Vector3.down,out top,4f))continue;
                    const float carSide=1.6f;float far=edge.distance+.4f;if(far-carSide<.3f)continue; // already alongside
                    var o=Primitive(PrimitiveType.Cube,"KTX 승강장 연결부",root.transform,
                        at+right*side*(carSide+far)*.5f+Vector3.up*(top.point.y-.55f-at.y),new Vector3(far-carSide,1.1f,60f),deck);
                    o.transform.rotation=Quaternion.LookRotation(right*side);o.transform.localScale=new Vector3(60f,1.1f,far-carSide);
                    o.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            Physics.SyncTransforms();
        }
    }
}
