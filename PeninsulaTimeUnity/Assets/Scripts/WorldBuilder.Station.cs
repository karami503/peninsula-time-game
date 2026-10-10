using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeninsulaTime
{
    // Underground subway station below each district: a concourse (대합실) with exits and route kiosks,
    // shops and a gate line, and stairs to side or island platforms, with screen doors, arrival
    // screens and trains that run to the national network's timetable.
    // Layout follows Seoul Metro stations: unpaid concourse -> fare gates -> paid concourse -> platform.
    public partial class WorldBuilder
    {
        public const float ConcourseY=-11.8f,PlatformY=-19.6f,GateZ=-3f;
        const float ConcourseHeight=4.4f,HalfZ=18f,TrackX=7.6f,PlatformHalf=70f,IslandGap=28f;
        float HalfX=>30f;
        static readonly Color Line2=new Color(0f,.66f,.30f),Line9=new Color(.74f,.69f,.57f),Arex=new Color(0f,.56f,.82f);
        public static readonly string[] StationNames={"강남","서울역","홍대입구","김포공항"};
        static readonly string[] StationEnglish={"Gangnam","Seoul Station","Hongik Univ.","Gimpo Int'l Airport"};
        // The line modules under each detailed district. Gangnam L2 and three Gimpo
        // lines use side platforms; other modules keep the existing island layouts.
        // Seoul Station: Lines 1 and 4 either side of the airport railroad (KTX/무궁화 run on the surface tracks).
        static readonly string[][] IslandLines={new[]{"2호선"},new[]{"1호선","공항철도","4호선"},new[]{"2호선","공항철도"},new[]{"5호선","9호선","공항철도","김포골드라인","서해선"}};
        // Used when the network data is missing or the player removed the line or the station.
        static readonly StationLine[][] FallbackLines={
            new[]{new StationLine("2호선","내선순환 · 교대 방면",Line2,2),new StationLine("2호선","외선순환 · 역삼 방면",Line2,2)},
            new[]{new StationLine("공항철도","인천공항2터미널행 · 공덕 방면",Arex,2)},
            new[]{new StationLine("2호선","내선순환 · 신촌 방면",Line2,0),new StationLine("2호선","외선순환 · 합정 방면",Line2,0),
                  new StationLine("공항철도","서울행 · 공덕 방면",Arex,1),new StationLine("공항철도","인천공항2터미널행 · 디지털미디어시티 방면",Arex,3)},
            new[]{new StationLine("공항철도","서울행 · 마곡나루 방면",Arex,2),new StationLine("공항철도","인천공항2터미널행 · 계양 방면",Arex,-1)}};
        // "서울역" already ends in 역; the others take it as a suffix.
        public static string StationTitle(int district){var name=StationNames[Mathf.Clamp(district,0,StationNames.Length-1)];return name.EndsWith("역")?name:name+"역";}
        static readonly string[,] Shops={{"편의점","삼각김밥과 생수를 샀습니다."},{"베이커리","갓 구운 소보로빵 냄새가 납니다."},{"꽃집","작은 꽃다발을 샀습니다."},{"화장품","핸드크림 샘플을 받았습니다."}};
        static readonly string[,] ShopProducts={{"삼각김밥","생수","커피"},{"소보로빵","크루아상","우유"},{"꽃 한 송이","꽃다발","엽서"},{"핸드크림","립밤","마스크팩"}};

        public readonly List<Barrier> FareGates=new List<Barrier>();
        public readonly List<SubwayTrain> StationTrains=new List<SubwayTrain>();
        public readonly List<StationLine> PlatformSides=new List<StationLine>(); // one per train, in StationTrains order
        readonly List<float> islandX=new List<float>();
        readonly Dictionary<Transform,float> platformHalfLengths=new Dictionary<Transform,float>();
        readonly Dictionary<SubwayTrain,List<Vector3>> platformEdgeSpans=new Dictionary<SubwayTrain,List<Vector3>>();
        public float IslandFloor(int island){
            if(StationIndex==3)return GimpoFloors[Mathf.Clamp(island,0,4)];
            return StationIndex==2&&island==1?-30.4f:PlatformY;
        }
        public float IslandCentre(int island)=>islandX[Mathf.Clamp(island,0,islandX.Count-1)];
        public int StationIndex{get;private set;}
        Transform station;
        Font signFont;

        Font SignFont()
        {
            if(signFont==null)signFont=Font.CreateDynamicFontFromOSFont(new[]{"Apple SD Gothic Neo","Malgun Gothic","Noto Sans CJK KR","Noto Sans KR","NanumGothic","Arial Unicode MS"},64);
            return signFont;
        }
        // Axis-aligned box from min/max corners; interiors do not cast shadows.
        GameObject Block(string name,Transform parent,Vector3 min,Vector3 max,Material material,bool collide=true)
        {
            var o=Primitive(PrimitiveType.Cube,name,parent,(min+max)*.5f,max-min,material);
            if(!collide)DestroyImmediate(o.GetComponent<Collider>());
            o.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            return o;
        }
        Material Glow(string key,Color color,float strength=1f)
        {
            var m=Mat("glow-"+key,color);
            m.EnableKeyword("_EMISSION");if(m.HasProperty("_EmissionColor"))m.SetColor("_EmissionColor",color*strength);
            return m;
        }
        // Fixed sign text about `height` metres tall, readable from the `toViewer` side.
        public TextMesh Sign(string text,Transform parent,Vector3 position,Vector3 toViewer,float height,Color color)
        {
            var go=new GameObject(text.Replace('\n',' ')+" 표지");go.transform.SetParent(parent,false);go.transform.position=position;
            go.transform.rotation=Quaternion.LookRotation(-toViewer);
            var tm=go.AddComponent<TextMesh>();tm.text=text;tm.fontSize=64;tm.characterSize=height*10f/64f*1.4f;
            tm.anchor=TextAnchor.MiddleCenter;tm.alignment=TextAlignment.Center;tm.color=color;tm.font=SignFont();
            if(tm.font!=null)go.GetComponent<MeshRenderer>().sharedMaterial=SignMaterial(tm.font);
            return tm;
        }
        Material signMaterial;
        // Single-sided text material: a sign seen from behind disappears instead of reading mirrored.
        Material SignMaterial(Font font)
        {
            if(signMaterial!=null&&signMaterial.mainTexture==font.material.mainTexture)return signMaterial;
            var shader=Shader.Find("Peninsula/SignText");
            if(shader==null)return font.material;
            if(signMaterial==null)
            {
                signMaterial=new Material(shader);
                Font.textureRebuilt+=rebuilt=>{if(signMaterial!=null&&rebuilt==font)signMaterial.mainTexture=rebuilt.material.mainTexture;};
            }
            signMaterial.mainTexture=font.material.mainTexture;return signMaterial;
        }
        // Lit sign board with text at a world position (Seoul style: yellow exits, blue boarding).
        void Board(string text,Transform parent,Vector3 centre,Vector3 toViewer,Vector2 size,Color background,Color textColor,float textHeight)
        {
            var o=Primitive(PrimitiveType.Cube,"안내판",parent,Vector3.zero,new Vector3(size.x,size.y,.08f),Glow("board-"+background,background,.25f));
            o.transform.position=centre;o.transform.rotation=Quaternion.LookRotation(toViewer);DestroyImmediate(o.GetComponent<Collider>());
            var label=Sign(text,parent,centre+toViewer.normalized*.05f,toViewer,textHeight,textColor);
            var bounds=label.GetComponent<MeshRenderer>().localBounds.size;
            float fit=Mathf.Min(1f,Mathf.Min((size.x-.22f)/Mathf.Max(.01f,bounds.x),(size.y-.12f)/Mathf.Max(.01f,bounds.y)));
            label.transform.localScale=Vector3.one*Mathf.Max(.01f,fit);
        }
        Fixture AddFixture(GameObject o,string kind,string hint,string detail,int number=0)
        {
            var f=o.AddComponent<Fixture>();f.kind=kind;f.hint=hint;f.detail=detail;f.number=number;return f;
        }

        // Labelled points for the walking minimap; y picks the floor they belong to.
        public struct MapMarker{public Vector3 position;public string label;public MapMarker(Vector3 p,string l){position=p;label=l;}}
        public readonly List<MapMarker> MapMarkers=new List<MapMarker>();
        void Marker(Vector3 position,string label){MapMarkers.Add(new MapMarker(position,label));}

        // Platform sides for a district: each island line's directions at this station from the network
        // (where its trains go, the districts further along, the timetable to follow).
        public static List<StationLine> PlatformLines(int district)
        {
            district=Mathf.Clamp(district,0,StationNames.Length-1);
            if(TransitNetwork.Lines.Count==0)TransitNetwork.Build(null); // batch checks run without a GameController
            var result=new List<StationLine>();var names=IslandLines[district];
            for(int island=0;island<names.Length;island++)
            {
                var sides=NetworkSides(district,names[island],island);
                if(sides.Count==0)foreach(var fallback in FallbackLines[district])if(fallback.line==names[island]){var side=fallback;side.island=island;sides.Add(side);}
                if(sides.Count==0){var side=new StationLine(names[island],"노선 자료 없음",Color.gray,-1);side.island=island;sides.Add(side);}
                // A terminus: both sides of the island serve the one direction, taking alternate trains.
                if(sides.Count==1){var first=sides[0];first.parity=0;var second=first;second.parity=1;sides[0]=first;sides.Add(second);}
                result.AddRange(sides);
            }
            return district==3?GimpoPlatformLines(result):result;
        }
        static List<StationLine> NetworkSides(int district,string lineName,int island)
        {
            var sides=new List<StationLine>();
            foreach(var here in TransitNetwork.Named(StationNames[district]))foreach(var line in here.lines)
            {
                if(sides.Count>0||line.kind!="metro"||line.shortName!=lineName||line.offsets.Length!=line.stops.Count)continue;
                int index=line.IndexOf(here);
                for(int direction=0;direction<2;direction++)
                {
                    if(!line.loop&&TransitSchedule.Order(line,direction,index)==TransitSchedule.Last(line))continue;
                    var next=TransitSchedule.NextStop(line,direction,index);
                    var ahead=new List<int>();
                    foreach(int i in StopsAhead(line,direction,index)){int d=DistrictOf(line.stops[i].name);if(d>=0&&d!=district&&!ahead.Contains(d))ahead.Add(d);}
                    var side=new StationLine(line.shortName,TransitSchedule.Toward(line,direction)+(next!=null?" · "+next.name+" 방면":""),line.color,ahead.Count>0?ahead[0]:-1);
                    side.net=line;side.direction=direction;side.index=index;side.island=island;side.ahead=ahead.ToArray();
                    sides.Add(side);
                }
            }
            return sides;
        }
        // Stop indices after `index` in this direction, once round a loop line.
        static IEnumerable<int> StopsAhead(NetLine line,int direction,int index)
        {
            int n=line.loop?line.stops.Count-1:line.stops.Count;
            for(int k=1;k<n;k++)
            {
                int i=index+(direction==0?k:-k);
                if(line.loop)i=((i%n)+n)%n;else if(i<0||i>=n)yield break;
                yield return i;
            }
        }
        public static string Bare(string name){return TransitNetwork.Bare(name);}
        public static int DistrictOf(string stationName)
        {
            string bare=TransitNetwork.Bare(stationName);
            for(int d=0;d<StationNames.Length;d++)if(TransitNetwork.Bare(StationNames[d])==bare)return d;
            return -1;
        }
        // Timetable running time from this platform to a district further along the line (0 without a timetable).
        public static float RideSeconds(StationLine side,int district)
        {
            var line=side.net;if(line==null||line.offsets.Length!=line.stops.Count)return 0;
            foreach(int i in StopsAhead(line,side.direction,side.index))
            {
                if(DistrictOf(line.stops[i].name)!=district)continue;
                float t=TransitSchedule.Offset(line,side.direction,i)-TransitSchedule.Offset(line,side.direction,side.index);
                if(t<=0&&line.loop)t+=line.offsets[line.offsets.Length-1];
                return t;
            }
            return 0;
        }

        void BuildStation(int district)
        {
            StationTrains.Clear();FareGates.Clear();platformHalfLengths.Clear();platformEdgeSpans.Clear();StationIndex=district;
            PlatformSides.Clear();PlatformSides.AddRange(PlatformLines(district));
            gimpoIslands.Clear();GangnamPlatformRoutes.Clear();
            if(district==0){BuildGangnamStation();return;}
            if(district==3){BuildGimpoStation();return;}
            int islands=IslandLines[district].Length;islandX.Clear();
            for(int i=0;i<islands;i++)islandX.Add((i-(islands-1)*.5f)*(district==3?36f:district==1?24f:IslandGap)); // Seoul: three islands inside the 60 m concourse
            var hall=new GameObject(StationTitle(district)+" 지하").transform;hall.SetParent(root.transform,false);
            station=hall;
            BuildConcourse(district);
            BuildFareGates();
            int number=1;
            for(int i=0;i<islands;i++)
            {
                var sides=PlatformSides.FindAll(side=>side.island==i);
                // Built at the origin, then moved into place, so world-positioned signs move with it.
                var island=new GameObject(sides[0].line+" 승강장").transform;island.SetParent(hall,false);station=island;
                float floorY=IslandFloor(i);
                BuildStairs(sides[0].line+(district==3?(i<2?" · B3":i<4?" · B4":" · B5"):""),floorY);
                BuildPlatform(StationNames[district],StationEnglish[district],sides,number,StationTrains,true,null,floorY);
                island.localPosition=new Vector3(islandX[i],0,0);station=hall;
                Marker(new Vector3(islandX[i],ConcourseY,9),sides[0].line+" ↓");
                Marker(new Vector3(islandX[i],floorY,9),"대합실 ↑");
                for(int s=0;s<sides.Count;s++)Marker(new Vector3(islandX[i]+(s==0?TrackX:-TrackX),floorY,-40),(number+s)+" "+sides[s].line);
                number+=sides.Count;
            }
            BuildStationPeople();
            Marker(new Vector3(0,ConcourseY,GateZ),"개찰구");
            Marker(new Vector3(-HalfX+1.5f,ConcourseY,-13.6f),"교통 안내");
            Marker(new Vector3(-9.5f,ConcourseY,GateZ-3f),"안내");
        }

        void BuildConcourse(int district)
        {
            var lines=PlatformSides;
            float y=ConcourseY,top=y+ConcourseHeight;
            var floor=Mat("station-floor",new Color(.86f,.85f,.82f),0,"pavement",2f);
            var wall=Mat("station-wall",new Color(.90f,.90f,.87f),0,"white",2f);
            var ceiling=Mat("station-ceiling",new Color(.70f,.72f,.73f),.2f,"metal",6f);
            // Floor with a stairwell opening down to each platform.
            Block("대합실 바닥",station,new Vector3(-HalfX,y-.4f,-HalfZ),new Vector3(HalfX,y,2),floor);
            float from=-HalfX;
            var openings=district==0?new List<float>{-GimpoSideWalkX,GimpoSideWalkX}:islandX;
            float openingHalf=district==0?GangnamStairHalf:4f;
            foreach(float ox in openings){Block("대합실 바닥",station,new Vector3(from,y-.4f,2),new Vector3(ox-openingHalf,y,16),floor);from=ox+openingHalf;}
            Block("대합실 바닥",station,new Vector3(from,y-.4f,2),new Vector3(HalfX,y,16),floor);
            Block("대합실 바닥",station,new Vector3(-HalfX,y-.4f,16),new Vector3(HalfX,y,HalfZ),floor);
            Block("대합실 천장",station,new Vector3(-HalfX,top,-HalfZ),new Vector3(HalfX,top+.3f,HalfZ),ceiling,false);
            // South wall is partitioned around the walk-through exits in LinkStationExits.
            // Line-colour band round the walls.
            var band=Glow("line-"+district,lines[0].color,.35f);
            foreach(float sx in new[]{-1f,1f})Block("노선색 띠",station,new Vector3(sx*(HalfX-.05f)-.03f,y+3.3f,-HalfZ),new Vector3(sx*(HalfX-.05f)+.03f,y+3.5f,HalfZ),band,false);
            foreach(float sz in new[]{-1f,1f})Block("노선색 띠",station,new Vector3(-HalfX,y+2.5f,sz*(HalfZ-.05f)-.03f),new Vector3(HalfX,y+2.8f,sz*(HalfZ-.05f)+.03f),band,false);
            // Ceiling light strips and columns.
            var light=Glow("ceiling-light",new Color(1f,.97f,.9f),1.3f);
            for(float z=-16;z<=16;z+=4)Block("천장 조명",station,new Vector3(-HalfX+4,top-.05f,z-.15f),new Vector3(HalfX-4,top,z+.15f),light,false);
            var column=Mat("station-column",new Color(.82f,.82f,.80f),.1f,"white",1f);
            foreach(float cz in new[]{-11f,9f})foreach(float cx in cz<0?new[]{-20f,-10f,10f,20f}:new[]{-24f,-6.5f,6.5f,24f}) // clear of the stairwells
            {
                if(cz>0&&openings.Exists(ox=>Mathf.Abs(cx-ox)<openingHalf+.8f))continue;
                Block("기둥",station,new Vector3(cx-.45f,y,cz-.45f),new Vector3(cx+.45f,top,cz+.45f),column);
                Block("기둥 노선띠",station,new Vector3(cx-.47f,y+2.1f,cz-.47f),new Vector3(cx+.47f,y+2.35f,cz+.47f),band,false);
                Sign(StationNames[district],station,new Vector3(cx,y+1.7f,cz-.48f),Vector3.back,.28f,new Color(.12f,.14f,.16f));
            }
            for(int i=0;i<(district==3?12:4);i++)
            {
                var lamp=new GameObject("대합실 조명").AddComponent<Light>();lamp.transform.SetParent(station,false);
                lamp.transform.localPosition=new Vector3(-HalfX+12+i*12,top-.6f,-6);lamp.type=LightType.Point;lamp.range=22;lamp.intensity=1.1f;lamp.color=new Color(1f,.96f,.88f);
            }
            // Hanging station name and direction signs above the gates.
            Board(StationNames[district]+"  "+StationEnglish[district],station,new Vector3(0,top-.7f,GateZ-4f),Vector3.back,new Vector2(10,.9f),new Color(.12f,.13f,.15f),Color.white,.42f);
            Board("타는 곳 ↑  ·  노선별 안내는 개찰구 안쪽",station,new Vector3(0,top-.7f,GateZ-.4f),Vector3.back,new Vector2(10,.65f),new Color(.10f,.24f,.45f),Color.white,.26f);
            Board("나가는 곳",station,new Vector3(0,top-.85f,GateZ+.4f),Vector3.forward,new Vector2(5,.8f),new Color(.12f,.12f,.12f),new Color(1f,.82f,.1f),.32f);
            // Tactile paving from the exits to the gates.
            var tactile=Mat("tactile",new Color(.95f,.78f,.15f));
            Block("점자블록",station,new Vector3(.3f,y,-HalfZ+1),new Vector3(.9f,y+.02f,GateZ-1.2f),tactile,false);
            Block("점자블록",station,new Vector3(-HalfX+2,y,-10.3f),new Vector3(HalfX-6,y+.02f,-9.7f),tactile,false);
            BuildConcourseFixtures(district,y);
        }

        void BuildConcourseFixtures(int district,float y)
        {
            // Route information kiosks on the west wall of the public concourse.
            for(int i=0;i<4;i++)
            {
                var machine=CityModel("TicketMachine",new Vector3(-HalfX+.6f,y,-15.5f+i*1.3f),1f,90f);
                if(machine==null)continue;machine.transform.SetParent(station,true);
                FitBoxCollider(machine);AddFixture(machine,"info","노선·환승 안내 보기","바닥 안내선을 따라 원하는 노선의 승강장으로 이동하고 열린 문으로 탑승하세요.");
            }
            Board("노선·환승 안내",station,new Vector3(-HalfX+.08f,y+3.1f,-13.6f),Vector3.right,new Vector2(5.2f,.6f),new Color(.10f,.24f,.45f),Color.white,.28f);
            // Customer service centre beside the gates.
            var glass=Mat("station-glass",new Color(.55f,.72f,.78f),.3f);
            var counter=Mat("station-counter",new Color(.30f,.36f,.42f));
            var booth=Block("고객안내센터",station,new Vector3(-12,y,GateZ-3.2f),new Vector3(-7,y+1.1f,GateZ-2.4f),counter);
            Block("고객안내센터 유리",station,new Vector3(-12,y+1.1f,GateZ-3.0f),new Vector3(-7,y+2.6f,GateZ-2.9f),glass,false);
            Block("고객안내센터 벽",station,new Vector3(-12,y,GateZ-2.4f),new Vector3(-7,y+3f,GateZ-.4f),Mat("booth-wall",new Color(.82f,.84f,.86f)));
            Board("고객안내센터",station,new Vector3(-9.5f,y+3.25f,GateZ-3.05f),Vector3.back,new Vector2(4,.5f),new Color(.10f,.24f,.45f),Color.white,.26f);
            AddFixture(booth,"info","역무원에게 묻기",StationTitle(district)+" 고객안내센터: 개찰구는 가까이 걸어가면 자동으로 열려요. 바닥 안내선을 따라 승강장과 출구를 찾으세요.");
            // Shops along the east wall.
            var tints=new[]{new Color(.12f,.45f,.62f),new Color(.72f,.45f,.20f),new Color(.45f,.62f,.30f),new Color(.78f,.32f,.45f)};
            for(int i=0;i<4;i++)
            {
                float z0=-17+i*3.6f;var tint=tints[i];
                Block("점포 벽",station,new Vector3(HalfX-4.6f,y,z0+3.4f),new Vector3(HalfX,y+3f,z0+3.6f),Mat("shop-wall",new Color(.78f,.78f,.76f)));
                Block("점포 진열창",station,new Vector3(HalfX-4.6f,y+.4f,z0+.2f),new Vector3(HalfX-4.5f,y+2.6f,z0+2.2f),glass);
                Block("점포 하단",station,new Vector3(HalfX-4.6f,y,z0+.2f),new Vector3(HalfX-4.5f,y+.4f,z0+2.2f),Mat("shop-base",new Color(.25f,.26f,.27f)));
                Block("점포 간판",station,new Vector3(HalfX-4.7f,y+2.6f,z0),new Vector3(HalfX-4.5f,y+3.2f,z0+3.6f),Glow("shop-"+i,tint,.8f),false);
                Sign(Shops[i,0],station,new Vector3(HalfX-4.75f,y+2.9f,z0+1.8f),Vector3.left,.34f,Color.white);
                var counterTop=Block("점포 계산대",station,new Vector3(HalfX-2.4f,y,z0+.5f),new Vector3(HalfX-1.8f,y+1f,z0+2.6f),counter);
                for(int s=0;s<3;s++)
                {
                    Block("진열대",station,new Vector3(HalfX-.6f,y,z0+.4f+s*1f),new Vector3(HalfX-.1f,y+1.8f,z0+1.2f+s*1f),Mat("shelf-"+i,Color.Lerp(tint,Color.white,.5f)));
                    // Products face the concourse so each one can be selected through the storefront.
                    var product=Block(ShopProducts[i,s],station,new Vector3(HalfX-5.05f,y+1.05f,z0+.48f+s*.85f),new Vector3(HalfX-4.78f,y+1.50f,z0+.94f+s*.85f),Mat("product-"+i+"-"+s,Color.Lerp(tint,Color.white,.35f)));
                    AddFixture(product,"shop",ShopProducts[i,s]+" 구매",ShopProducts[i,s]);
                }
                AddFixture(counterTop,"shop",Shops[i,0]+" 들르기",Shops[i,1]);
            }
            // Lockers, route map, advertising light boxes and benches.
            var locker=Mat("locker",new Color(.62f,.66f,.70f),.4f);
            for(int r=0;r<3;r++)for(int c=0;c<6;c++)Block("물품보관함",station,new Vector3(-HalfX+.05f,y+.2f+r*.65f,-7f+c*.62f),new Vector3(-HalfX+.6f,y+.8f+r*.65f,-6.45f+c*.62f),locker);
            Board("물품보관함",station,new Vector3(-HalfX+.08f,y+2.5f,-5.2f),Vector3.right,new Vector2(3,.45f),new Color(.2f,.2f,.22f),Color.white,.24f);
            BuildRouteMap(new Vector3(-4,y+.9f,-HalfZ+.06f));
            var adColours=new[]{new Color(.95f,.55f,.25f),new Color(.30f,.62f,.85f),new Color(.85f,.30f,.45f),new Color(.40f,.75f,.45f)};
            for(int i=0;i<4;i++)Block("광고판",station,new Vector3(-24+i*4.5f+(i>1?30:0),y+.9f,HalfZ-.12f),new Vector3(-20.5f+i*4.5f+(i>1?30:0),y+2.3f,HalfZ-.02f),Glow("ad-"+i,adColours[i],.9f),false);
            var seat=Mat("bench",new Color(.55f,.40f,.26f),0,"timber",1f);
            foreach(float bx in new[]{-16f,-4f,8f})Block("의자",station,new Vector3(bx,y+.42f,-12.6f),new Vector3(bx+2.4f,y+.5f,-12.1f),seat);
        }

        void BuildRouteMap(Vector3 corner)
        {
            // On the south wall, read from the north (+z): lines and title sit in front of the board.
            Block("노선도 판",station,corner,corner+new Vector3(8,1.6f,.05f),Glow("routemap",new Color(.96f,.96f,.94f),.2f),false);
            var colours=new[]{new Color(0f,.2f,.6f),Line2,new Color(.94f,.47f,.1f),new Color(0f,.65f,.87f),new Color(.6f,.35f,.78f),Line9,Arex};
            for(int i=0;i<colours.Length;i++)
            {
                float h=.2f+i*.19f;
                Block("노선",station,corner+new Vector3(.3f,h,.06f),corner+new Vector3(7.7f-(i%3)*.8f,h+.06f,.07f),Mat("route-"+i,colours[i]),false);
            }
            Block("2호선 순환",station,corner+new Vector3(5.2f,.25f,.07f),corner+new Vector3(5.28f,1.35f,.08f),Mat("route-1",Line2),false);
            Sign("수도권 전철 노선도",station,corner+new Vector3(4,1.5f,.08f),Vector3.forward,.13f,new Color(.15f,.15f,.18f));
        }

        void BuildFareGates()
        {
            float y=ConcourseY;
            var fence=Mat("gate-fence",new Color(.66f,.80f,.85f),.3f);var post=Mat("gate-post",new Color(.72f,.74f,.76f),.6f);
            Block("안전 펜스",station,new Vector3(-HalfX,y,GateZ-.05f),new Vector3(-5.0f,y+1.2f,GateZ+.05f),fence);
            Block("안전 펜스",station,new Vector3(5.0f,y,GateZ-.05f),new Vector3(HalfX,y+1.2f,GateZ+.05f),fence);
            for(float x=-HalfX+2;x<HalfX;x+=3)if(Mathf.Abs(x)>5)Block("펜스 기둥",station,new Vector3(x-.04f,y,GateZ-.08f),new Vector3(x+.04f,y+1.25f,GateZ+.08f),post,false);
            var flapMat=Mat("gate-flap",new Color(.85f,.25f,.2f),.1f);
            GameObject previous=null;
            for(int i=0;i<=8;i++)
            {
                float x=-4.8f+1.2f*i;
                var cabinet=CityModel("FareGate",new Vector3(x,y,GateZ),1f,0f);
                if(cabinet==null)cabinet=Block("개찰구",station,new Vector3(x-.14f,y,GateZ-.8f),new Vector3(x+.14f,y+1f,GateZ+.8f),post);
                else FitBoxCollider(cabinet);
                if(i==8){cabinet.transform.SetParent(previous!=null?previous.transform:station,true);break;}
                var lane=new GameObject("개찰구 통로 "+(i+1));lane.transform.SetParent(station,false);lane.transform.localPosition=new Vector3(x+.6f,y,GateZ);
                var blocker=lane.AddComponent<BoxCollider>();blocker.center=new Vector3(0,.55f,0);blocker.size=new Vector3(.92f,1.1f,.25f);
                var flaps=new Transform[2];
                for(int s=0;s<2;s++)
                {
                    var pivot=new GameObject("플랩 축").transform;pivot.SetParent(lane.transform,false);pivot.localPosition=new Vector3(s==0?-.46f:.46f,.75f,0);
                    var flap=Primitive(PrimitiveType.Cube,"개찰구 플랩",pivot,new Vector3(s==0?.22f:-.22f,0,0),new Vector3(.42f,.45f,.03f),flapMat);
                    DestroyImmediate(flap.GetComponent<Collider>());flaps[s]=pivot;
                }
                var barrier=lane.AddComponent<Barrier>();barrier.kind="fare";barrier.blocker=blocker;barrier.flaps=flaps;FareGates.Add(barrier);
                cabinet.transform.SetParent(lane.transform,true);
                previous=lane;
            }
        }

        void BuildStairs(string line,float platformY)
        {
            float drop=ConcourseY-platformY;
            float runLength=Mathf.Max(14f,drop*2f),endZ=2+runLength,midZ=2+runLength*.5f;
            var stone=Mat("stair-stone",new Color(.70f,.70f,.68f),0,"granite",1f);
            var nosing=Mat("stair-nosing",new Color(.95f,.78f,.15f));
            var tread=Mat("escalator-step",new Color(.35f,.37f,.39f),.5f,"metal",1f);
            float angle=Mathf.Atan2(drop,runLength)*Mathf.Rad2Deg,length=Mathf.Sqrt(drop*drop+runLength*runLength);
            var ramp=Primitive(PrimitiveType.Cube,"계단 경사면",station,new Vector3(0,(ConcourseY+platformY)*.5f-.15f,midZ),new Vector3(8,.3f,length),stone);
            ramp.transform.localRotation=Quaternion.Euler(angle,0,0);ramp.GetComponent<Renderer>().enabled=false;
            int steps=Mathf.RoundToInt(drop/.17f);
            for(int i=0;i<steps;i++)
            {
                float z=2+runLength*i/steps,top=ConcourseY-drop*i/steps,rise=drop/steps,run=runLength/steps;
                Block("계단",station,new Vector3(-4,top-rise,z),new Vector3(0,top,z+run),stone,false);
                Block("계단 미끄럼방지",station,new Vector3(-4,top-.01f,z),new Vector3(0,top+.005f,z+.06f),nosing,false);
                Block("에스컬레이터 디딤판",station,new Vector3(.6f,top-rise,z),new Vector3(3.4f,top,z+run),tread,false);
            }
            var balustrade=Mat("escalator-glass",new Color(.70f,.82f,.86f),.3f);var rail=Mat("handrail",new Color(.08f,.08f,.09f));
            var stairWall=Mat("stair-wall",new Color(.85f,.85f,.83f));
            foreach(float x in new[]{-4f,0f,.6f,3.4f,4f})
            {
                var side=Primitive(PrimitiveType.Cube,"난간",station,new Vector3(x,(ConcourseY+platformY)*.5f+.55f,midZ),new Vector3(.06f,1f,length),x>0&&x<4?balustrade:stairWall);
                side.transform.localRotation=Quaternion.Euler(angle,0,0);DestroyImmediate(side.GetComponent<Collider>());
                var hand=Primitive(PrimitiveType.Cube,"손잡이",station,new Vector3(x,(ConcourseY+platformY)*.5f+1.05f,midZ),new Vector3(.1f,.08f,length),rail);
                hand.transform.localRotation=Quaternion.Euler(angle,0,0);DestroyImmediate(hand.GetComponent<Collider>());
            }
            // Railing round the stairwell on the concourse; the space under the stairs is closed off.
            var railing=Mat("station-glass",new Color(.55f,.72f,.78f),.3f);
            Block("계단실 난간",station,new Vector3(-4.1f,ConcourseY,2),new Vector3(-4f,ConcourseY+1.1f,16),railing);
            Block("계단실 난간",station,new Vector3(4f,ConcourseY,2),new Vector3(4.1f,ConcourseY+1.1f,16),railing);
            Block("계단실 난간",station,new Vector3(-4,ConcourseY,15.9f),new Vector3(4,ConcourseY+1.1f,16f),railing);
            Block("계단 아래 벽",station,new Vector3(-4.05f,platformY,2),new Vector3(4.05f,platformY+1.9f,endZ-3.6f),stairWall);
            Board("타는 곳 ↓ "+line,station,new Vector3(0,ConcourseY+3.5f,1.6f),Vector3.back,new Vector2(5,.6f),new Color(.10f,.24f,.45f),Color.white,.32f);
            Board("나가는 곳 ↑",station,new Vector3(0,platformY+3.2f,endZ+.4f),Vector3.forward,new Vector2(4,.6f),new Color(.12f,.12f,.12f),new Color(1f,.82f,.1f),.32f);
        }

        // One island platform (two sides) under `station`: name boards, screen doors and a SubwayTrain per side
        // (added to `made`, with trains of their own when `consists`); name board texts go to `names`.
        void BuildPlatform(string title,string english,List<StationLine> lines,int number,List<SubwayTrain> made,bool consists,List<TextMesh> names,float platformY=PlatformY,bool externalAccess=false,float platformHalf=PlatformHalf)
        {
            float stairEnd=4+Mathf.Max(14f,(ConcourseY-platformY)*2f);
            float y=platformY,track=y-1.1f,top=y+5f,far=SubwayTrain.Run+80;
            var floor=Mat("platform-floor",new Color(.78f,.77f,.74f),0,"pavement",3f);
            var bed=Mat("track-bed",new Color(.22f,.22f,.23f),0,"asphalt",4f);
            var tunnel=Mat("tunnel-wall",new Color(.42f,.43f,.44f),0,"concrete",4f);
            var wall=Mat("platform-wall",new Color(.88f,.88f,.85f),0,"white",2f);
            var ceiling=Mat("station-ceiling",new Color(.70f,.72f,.73f),.2f,"metal",6f);
            var steel=Mat("rail-steel",new Color(.70f,.76f,.80f),.55f);
            Block("승강장",station,new Vector3(-5,y-1.1f,-platformHalf),new Vector3(5,y,platformHalf),floor);
            for(int s=0;s<2;s++)
            {
                float sx=s==0?1:-1,inner=sx*5f,outer=sx*11.2f;var line=lines[s];
                Block("선로 바닥",station,new Vector3(Mathf.Min(inner,outer),track-.4f,-far),new Vector3(Mathf.Max(inner,outer),track,far),bed);
                Block("선로 벽",station,new Vector3(Mathf.Min(outer,outer+sx*.3f),track,-far),new Vector3(Mathf.Max(outer,outer+sx*.3f),top,far),tunnel);
                Block("승강장 벽 마감",station,new Vector3(outer-sx*.06f-.02f,y-.5f,-platformHalf),new Vector3(outer-sx*.06f+.02f,top,platformHalf),wall,false);
                foreach(float rx in new[]{-.72f,.72f})Block("레일",station,new Vector3(sx*TrackX+rx-.04f,track,-far),new Vector3(sx*TrackX+rx+.04f,track+.16f,far),steel,false);
                Block("승강장 안전선",station,new Vector3(Mathf.Min(inner,inner-sx*.5f),y,-platformHalf),new Vector3(Mathf.Max(inner,inner-sx*.5f),y+.01f,platformHalf),Mat("tactile",new Color(.95f,.78f,.15f)),false);
                for(float z=-SubwayTrain.Run;z<=SubwayTrain.Run;z+=15)
                    if(Mathf.Abs(z)>platformHalf)Block("터널 조명",station,new Vector3(outer-sx*.12f-.06f,track+3f,z),new Vector3(outer-sx*.12f+.06f,track+3.2f,z+1.2f),Glow("tunnel-light",new Color(1f,.9f,.7f),1.5f),false);
                // Station name boards and advertising on the track wall.
                for(float z=-50;z<=50;z+=50)
                {
                    Block("역명판",station,new Vector3(outer-sx*.1f-.03f,y+1.2f,z-3),new Vector3(outer-sx*.1f+.03f,y+2.6f,z+3),Glow("namesign",new Color(.97f,.97f,.95f),.35f),false);
                    Block("역명판 노선띠",station,new Vector3(outer-sx*.14f-.02f,y+1.2f,z-3),new Vector3(outer-sx*.14f+.02f,y+1.45f,z+3),Mat("line-band-"+line.line,line.color),false);
                    var nameSign=Sign(english.Length>0?title+"\n"+english:title,station,new Vector3(outer-sx*.18f,y+2.05f,z),new Vector3(-sx,0,0),.38f,new Color(.1f,.1f,.12f));
                    if(names!=null)names.Add(nameSign);
                    Block("광고판",station,new Vector3(outer-sx*.1f-.03f,y+1.0f,z+8),new Vector3(outer-sx*.1f+.03f,y+2.8f,z+14),Glow("ad-"+((int)z/50+1),new Color(.95f,.55f,.25f),.9f),false);
                }
                // Direction signs hanging over this side of the platform.
                for(float z=-40;z<=40;z+=40)
                {
                    if(z>=-2&&z<=stairEnd+2)continue;
                    Block("방면 안내",station,new Vector3(sx*3.4f-.04f,top-1.05f,z-3.2f),new Vector3(sx*3.4f+.04f,top-.35f,z+3.2f),Glow("dir-"+line.line,line.color,.5f),false);
                    Sign((number+s)+"  "+line.line+"  "+line.toward,station,new Vector3(sx*3.46f,top-.7f,z),new Vector3(sx,0,0),.22f,Color.white);
                }
                var train=BuildScreenDoors(number+s-1,sx,line,consists,y,platformHalf);made.Add(train);
                // Arrival screens over this side, read by people walking from the stairs.
                foreach(float z in new[]{-15f,Mathf.Max(35f,stairEnd+5f)})
                {
                    Block("도착 안내 화면",station,new Vector3(sx*2.6f-2.4f,top-1.45f,z-.06f),new Vector3(sx*2.6f+2.4f,top-.55f,z+.06f),Mat("arrival-screen",new Color(.03f,.03f,.04f)),false);
                    var text=Sign(line.line,station,new Vector3(sx*2.6f,top-1f,z+.08f),Vector3.forward,.15f,new Color(1f,.62f,.12f));
                    var screen=text.gameObject.AddComponent<TransitBoard>();screen.text=text;screen.compose=train.BoardText;screen.Refresh();
                }
            }
            if(!externalAccess){
            Block("승강장 천장",station,new Vector3(-11.2f,top,-platformHalf),new Vector3(11.2f,top+.3f,0),ceiling,false);
            Block("승강장 천장",station,new Vector3(-11.2f,top,stairEnd),new Vector3(11.2f,top+.3f,platformHalf),ceiling,false);
            Block("승강장 천장",station,new Vector3(-11.2f,top,0),new Vector3(-4.2f,top+.3f,stairEnd),ceiling,false);
            Block("승강장 천장",station,new Vector3(4.2f,top,0),new Vector3(11.2f,top+.3f,stairEnd),ceiling,false);
            }else Block("승강장 천장",station,new Vector3(-11.2f,top,-platformHalf),new Vector3(11.2f,top+.3f,platformHalf),ceiling,false);
            Block("터널 천장",station,new Vector3(-11.2f,top,-far),new Vector3(11.2f,top+.3f,-platformHalf),tunnel,false);
            Block("터널 천장",station,new Vector3(-11.2f,top,platformHalf),new Vector3(11.2f,top+.3f,far),tunnel,false);
            if(!externalAccess)Block("승강장 끝 벽",station,new Vector3(-5,y,-platformHalf-.3f),new Vector3(5,top,-platformHalf),wall);
            Block("승강장 끝 벽",station,new Vector3(-5,y,platformHalf),new Vector3(5,top,platformHalf+.3f),wall);
            var light=Glow("ceiling-light",new Color(1f,.97f,.9f),1.3f);
            foreach(float x in new[]{-2.5f,2.5f})foreach(var span in new[]{new Vector2(-platformHalf+2,-1),new Vector2(stairEnd+1,platformHalf-2)})
                Block("승강장 조명",station,new Vector3(x-.15f,top-.05f,span.x),new Vector3(x+.15f,top,span.y),light,false);
            if(!externalAccess&&ConcourseY>top)
            {
                foreach(float x in new[]{-4.16f,4.16f})
                    Block("계단실 상부 측벽",station,new Vector3(x-.04f,top,0),new Vector3(x+.04f,ConcourseY,stairEnd),wall,false);
                Block("계단실 상부 끝벽",station,new Vector3(-4.2f,top,stairEnd),new Vector3(4.2f,ConcourseY,stairEnd+.15f),wall,false);
                if(stairEnd>HalfZ)
                    Block("계단실 상부 덮개",station,new Vector3(-4.2f,ConcourseY,HalfZ),new Vector3(4.2f,ConcourseY+.15f,stairEnd),ceiling,false);
            }
            for(int i=0;i<4;i++)
            {
                var lamp=new GameObject("승강장 조명").AddComponent<Light>();lamp.transform.SetParent(station,false);
                lamp.transform.localPosition=new Vector3(0,top-.8f,-45+i*30);lamp.type=LightType.Point;lamp.range=24;lamp.intensity=1f;lamp.color=new Color(1f,.96f,.88f);
            }
            var column=Mat("station-column",new Color(.82f,.82f,.80f),.1f,"white",1f);var seat=Mat("bench",new Color(.55f,.40f,.26f),0,"timber",1f);
            for(float z=-60;z<=60;z+=20)
            {
                if(z>-2&&z<stairEnd+2)continue;
                foreach(float x in new[]{-2.6f,2.6f})Block("승강장 기둥",station,new Vector3(x-.3f,y,z-.3f),new Vector3(x+.3f,top,z+.3f),column);
                Block("승강장 의자",station,new Vector3(-.9f,y+.42f,z+3),new Vector3(.9f,y+.5f,z+3.5f),seat);
            }
        }

        // Platform screen doors with sliding leaves at every car door, and the trains that serve this side: two
        // consists taking alternate trips, so one arrives every 30 seconds.
        SubwayTrain BuildScreenDoors(int side,float sx,StationLine line,bool consists,float y,float platformHalf)
        {
            float x=sx*5.05f;
            var glass=Mat("psd-glass",new Color(.62f,.78f,.82f),.35f);var frame=Mat("psd-frame",new Color(.62f,.64f,.66f),.6f);
            var band=Mat("line-band-"+line.line,line.color);
            var trainRoot=new GameObject((side+1)+"번 승강장 열차");trainRoot.transform.SetParent(station,false);
            var train=trainRoot.AddComponent<SubwayTrain>();train.line=line;train.trackX=sx*TrackX;train.floorY=y-1.1f+.16f;train.direction=sx>0?1:-1;
            trainRoot.transform.localPosition=new Vector3(train.trackX,train.floorY,0);
            // The edge between the screen doors and the cars, so riders step straight across.
            Block("승강장 연단",station,new Vector3(Mathf.Min(sx*4.9f,sx*6.1f),y-.5f,-40),new Vector3(Mathf.Max(sx*4.9f,sx*6.1f),y,40),Mat("platform-floor",new Color(.78f,.77f,.74f),0,"pavement",3f));
            var openings=new List<float>();
            foreach(float car in new[]{-30f,-10f,10f,30f})foreach(float door in new[]{-7.55f,-2.52f,2.52f,7.55f})openings.Add(car+door);
            // Fixed panels enclose the full platform, including the extension
            // beyond this four-car train's door zone.
            float start=-platformHalf;
            foreach(float centre in openings)
            {
                if(centre-.9f>start)Block("스크린도어 고정벽",station,new Vector3(x-.05f,y,start),new Vector3(x+.05f,y+2.2f,centre-.9f),glass);
                Block("스크린도어 문틀",station,new Vector3(x-.08f,y,centre-.95f),new Vector3(x+.08f,y+2.25f,centre-.9f),frame,false);
                Block("스크린도어 문틀",station,new Vector3(x-.08f,y,centre+.9f),new Vector3(x+.08f,y+2.25f,centre+.95f),frame,false);
                foreach(float half in new[]{-1f,1f})
                {
                    var leaf=Block("스크린도어",station,new Vector3(x-.03f,y,centre+(half<0?-.9f:0)),new Vector3(x+.03f,y+2.15f,centre+(half<0?0:.9f)),glass);
                    train.leaves.Add(leaf.transform);train.leafClosed.Add(leaf.transform.localPosition);
                    train.leafOpen.Add(leaf.transform.localPosition+new Vector3(0,0,half*.86f));
                }
                start=centre+.9f;
            }
            Block("스크린도어 고정벽",station,new Vector3(x-.05f,y,start),new Vector3(x+.05f,y+2.2f,platformHalf),glass);
            Block("스크린도어 상부",station,new Vector3(x-.15f,y+2.2f,-platformHalf),new Vector3(x+.15f,y+2.75f,platformHalf),Mat("psd-header",new Color(.20f,.22f,.25f)),false);
            Block("스크린도어 노선띠",station,new Vector3(x-sx*.16f-.01f,y+2.3f,-platformHalf),new Vector3(x-sx*.16f+.01f,y+2.42f,platformHalf),band,false);
            if(consists)for(int k=0;k<2;k++)train.consists.Add(BuildConsist(train,(side+1)+"번 승강장 열차 "+(k+1),sx,band,side*7+k*131+StationIndex*977));
            foreach(Transform child in station)if(child.name.StartsWith("스크린도어")&&Mathf.Abs(child.localPosition.x-x)<.2f&&child.GetComponent<Collider>()!=null)
                child.gameObject.AddComponent<ScreenDoor>().train=train;
            train.Begin(side%2==0?SubwayTrain.Approach+4f:SubwayTrain.Cycle*.25f);
            return train;
        }

        public static readonly Vector3 RideOrigin=new Vector3(3000,0,0); // x beyond which nothing district-made stands
        // The platform side a train of `rode` arrives at in this district (same line and direction, else same line).
        void BuildStationPeople()
        {
            var graph=new TrafficGraph();
            System.Action<Vector3[]> loop=points=>{for(int i=0;i<points.Length;i++)graph.Link(points[i],points[(i+1)%points.Length],0);};
            loop(new[]{new Vector3(-20,ConcourseY,-14),new Vector3(20,ConcourseY,-14),new Vector3(20,ConcourseY,-7),new Vector3(-20,ConcourseY,-7)});
            for(int i=0;i<islandX.Count;i++)
            {
                float ox=islandX[i],floorY=IslandFloor(i),end=6+Mathf.Max(14,(ConcourseY-floorY)*2);
                loop(new[]{new Vector3(ox-3.6f,floorY,-60),new Vector3(ox-3.6f,floorY,-4),new Vector3(ox+3.6f,floorY,-4),new Vector3(ox+3.6f,floorY,-60)});
                loop(new[]{new Vector3(ox-3.6f,floorY,end),new Vector3(ox-3.6f,floorY,60),new Vector3(ox+3.6f,floorY,60),new Vector3(ox+3.6f,floorY,end)});
            }
            var director=new GameObject("역 보행자").AddComponent<TrafficDirector>();director.transform.SetParent(station,false);
            director.Init(graph,null,null,null);
            SpawnPeople(director,10+12*islandX.Count,0,0,1f);
        }

        // Where a passenger arriving by train stands: on the platform of side `side` (StationTrains order).
        public Vector3 PlatformSpawn(int side,out Vector3 facing)
        {
            side=Mathf.Clamp(side,0,Mathf.Max(0,PlatformSides.Count-1));
            int island=PlatformSides.Count>0?PlatformSides[side].island:0,within=0;
            for(int k=0;k<side;k++)if(PlatformSides[k].island==island)within++;
            // Back from the screen doors, looking along them so the platform and the arriving train are in view.
            float sx=within==0?1:-1,ox=island<islandX.Count?islandX[island]:0;facing=new Vector3(sx,0,-1.2f).normalized;
            if(StationIndex==3&&gimpoIslands.Count>island){
                var t=gimpoIslands[island];
                if(island!=1&&island!=2)facing=new Vector3(-sx,0,-1.2f).normalized;
                facing=t.TransformDirection(facing);
                return t.TransformPoint(new Vector3((island==1||island==2)?sx*2f:sx*9.3f,IslandFloor(island)+1.65f,-24));
            }
            if(StationIndex==0){facing=new Vector3(-sx,0,-1.2f).normalized;return new Vector3(sx*GimpoSideWalkX,PlatformY+1.65f,-24);}
            return new Vector3(ox+sx*2f,IslandFloor(island)+1.65f,-24);
        }
        public float PlatformHalfLength(SubwayTrain train)
        {
            var module=train.transform.parent;float half;
            if(!platformHalfLengths.TryGetValue(module,out half))
            {
                half=0;
                foreach(Transform child in module)
                    if(child.name=="승강장"||child.name=="상대식 승강장 바닥")half=Mathf.Max(half,child.localScale.z*.5f);
                if(half<=0)half=PlatformHalf;
                platformHalfLengths[module]=half;
            }
            return half;
        }
        // Distance from the track centre toward its platform to the nearest
        // supporting floor. The boarding lip only exists along the four cars;
        // longer platform extensions have a more distant concrete edge.
        public float PlatformEdgeDistance(SubwayTrain train,float along)
        {
            List<Vector3> spans;
            if(!platformEdgeSpans.TryGetValue(train,out spans))
            {
                spans=new List<Vector3>();
                float side=train.consists.Count>0&&train.consists[0].cabin!=null?train.consists[0].cabin.doorSide:-Mathf.Sign(train.trackX);
                foreach(Transform child in train.transform.parent)
                {
                    if(child.name!="승강장"&&child.name!="상대식 승강장 바닥"&&child.name!="승강장 연단"&&child.name!="상대식 승강장 연단")continue;
                    float edge=child.localPosition.x-side*child.localScale.x*.5f;
                    float distance=(edge-train.trackX)*side;
                    if(distance>0)spans.Add(new Vector3(distance,child.localPosition.z-child.localScale.z*.5f,child.localPosition.z+child.localScale.z*.5f));
                }
                platformEdgeSpans[train]=spans;
            }
            float nearest=float.MaxValue;
            foreach(var span in spans)if(along>=span.y&&along<=span.z)nearest=Mathf.Min(nearest,span.x);
            return nearest<float.MaxValue?nearest:2.6f;
        }
        // The platform side nearest a point (for "wait for the next train").
        public int NearestSide(Vector3 p)
        {
            int best=0;float nearest=float.MaxValue;
            for(int i=0;i<StationTrains.Count;i++)
            {
                var train=StationTrains[i];float half=PlatformHalfLength(train);
                var local=train.transform.InverseTransformPoint(p);
                float along=Mathf.Max(0,Mathf.Abs(local.z)-half);
                float vertical=Mathf.Abs(local.y-(MetroFloor+1.65f));
                // Long platforms are segments, not a single point at their centre.
                // A different storey must not win because it is closer in plan.
                float d=local.x*local.x+along*along+vertical*vertical;
                if(vertical>2f)d+=10000f+vertical*100f;
                if(d<nearest){nearest=d;best=i;}
            }
            return best;
        }
        public Vector3 ConcourseSpawn{get{return new Vector3(.6f,ConcourseY+1.65f,-12)+(StationIndex==3?GimpoHall:Vector3.zero);}}
    }
}
