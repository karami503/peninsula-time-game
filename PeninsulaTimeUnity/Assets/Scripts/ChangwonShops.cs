using System;
using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Shop fronts (striped awning + the real place name) for the convenience stores, cafes, restaurants, markets, fuel
    // stations… within ~180 m, each opening a free shop card (F), and 누비자 public-bike terminals by bus stops that lend
    // a rideable bicycle. Lives on a child of world.root (added by ChangwonMissions), so its things find it as their parent.
    public class ChangwonShops : MonoBehaviour, IChangwonUse
    {
        static readonly string[] Kinds={"store","mart","cafe","food","bar","fuel","pharmacy","market","bank","karaoke","pcbang","cinema","gym"};
        static readonly string[] Labels={"편의점","마트","카페","식당","주점","주유소","약국","시장","은행","노래방","PC방","영화관","헬스장"};
        static readonly Color32[] Colors={new Color32(60,170,95,255),new Color32(240,130,40,255),new Color32(125,82,52,255),new Color32(205,60,50,255),
            new Color32(160,80,30,255),new Color32(220,40,40,255),new Color32(40,160,95,255),new Color32(40,120,175,255),new Color32(40,70,145,255),
            new Color32(150,60,175,255),new Color32(40,170,205,255),new Color32(175,30,60,255),new Color32(75,75,85,255)};
        static readonly string[][] Items={
            new[]{"삼각김밥","바나나우유","컵라면","핫바"},new[]{"창원 단감 한 봉지","생수 2L","장바구니"},
            new[]{"아메리카노","바닐라라떼","단감 스무디","마카롱"},new[]{"돼지국밥","김치찌개","비빔밥"},
            new[]{"무알코올 맥주","노가리","감자튀김"},new[]{"주유하기","자동 세차","생수"},new[]{"감기약","파스","피로회복제"},
            new[]{"전어회","아구찜","미더덕","오만둥이"},new[]{"통장 정리","동전 교환"},new[]{"1시간 이용","음료수"},
            new[]{"1시간 이용","컵라면","음료수"},new[]{"영화 관람권","팝콘 콤보"},new[]{"1일 이용권","단백질 셰이크"}};
        static readonly string[] MasanFood={"아구찜","복국","돼지국밥"};
        const int Market=7,Bank=8;const float ShopRadius=180f,DockSpacing=600f;const string FuelLine="연료 가득 · 주유 완료";
        static readonly int[] Caps={12,22,34};
        class Dock {public Vector2 pos;public GameObject go;}

        readonly Dictionary<ChangwonData.Place,GameObject> shops=new Dictionary<ChangwonData.Place,GameObject>();
        readonly List<ChangwonData.Place> near=new List<ChangwonData.Place>(),pending=new List<ChangwonData.Place>(),drop=new List<ChangwonData.Place>();
        readonly List<string> hud=new List<string>(),lastHud=new List<string>();
        List<Dock> docks;Material mat;readonly Mesh[] awnings=new Mesh[Kinds.Length];Mesh stand,bikeMesh;
        float nextScan,fuelUntil;ChangwonCar bike;
        ChangwonData.Place open;int openKind;string openWhere="";readonly List<string> openItems=new List<string>();

        void Start(){mat=ChangwonMissions.VertexMaterial();ChangwonSession.Overlay+=DrawGUI;}
        void OnDestroy()
        {
            ChangwonSession.Overlay-=DrawGUI;if(open!=null)ChangwonSession.UiCapture=false;
            hud.Clear();ChangwonMissions.Publish(ChangwonSession.HudLines,lastHud,hud);
            if(mat!=null)Destroy(mat);if(stand!=null)Destroy(stand);if(bikeMesh!=null)Destroy(bikeMesh);foreach(var m in awnings)if(m!=null)Destroy(m);
        }

        void Update()
        {
            if(!ChangwonData.Loaded||!ChangwonSession.Active||ChangwonSession.Builder==null)return;
            if(docks==null)FindDocks();
            if(open!=null&&Input.GetKeyDown(KeyCode.Escape))Close(); // never left open under the pause menu
            if(bike!=null&&ChangwonSession.PlayerCar==bike)bike.SetEngine(false); // GameController starts an engine sound on boarding; a bicycle has none
            if(Time.time>=nextScan){nextScan=Time.time+2f;Scan(ChangwonSession.PlayerFeet);}
            for(int i=0;i<2&&pending.Count>0;i++){var pl=pending[pending.Count-1];pending.RemoveAt(pending.Count-1);if(!shops.ContainsKey(pl))shops[pl]=BuildShop(pl);}
        }
        void LateUpdate()
        {
            if(!ChangwonSession.Active)return;
            hud.Clear();if(Time.time<fuelUntil)hud.Add(FuelLine);
            ChangwonMissions.Publish(ChangwonSession.HudLines,lastHud,hud);
        }

        void Scan(Vector3 p)
        {
            var p2=new Vector2(p.x,p.z);near.Clear();
            foreach(var pl in ChangwonData.Places)if((pl.pos-p2).sqrMagnitude<ShopRadius*ShopRadius&&Array.IndexOf(Kinds,pl.kind)>=0)near.Add(pl);
            near.Sort((a,b)=>(a.pos-p2).sqrMagnitude.CompareTo((b.pos-p2).sqrMagnitude));
            pending.Clear();for(int i=Mathf.Min(near.Count,Caps[PerformanceRuntime.Level])-1;i>=0;i--)if(!shops.ContainsKey(near[i]))pending.Add(near[i]); // nearest last: built first
            drop.Clear();foreach(var kv in shops)if((kv.Key.pos-p2).sqrMagnitude>220f*220f)drop.Add(kv.Key);
            foreach(var pl in drop){if(shops[pl]!=null)Destroy(shops[pl]);shops.Remove(pl);}
            foreach(var d in docks){
                float e=(d.pos-p2).sqrMagnitude;
                if(d.go==null&&e<300f*300f)d.go=BuildDock(d.pos);
                else if(d.go!=null&&e>380f*380f){Destroy(d.go);d.go=null;}
            }
        }

        // ------------------------------------------------------------------ shops
        GameObject BuildShop(ChangwonData.Place pl)
        {
            int k=Array.IndexOf(Kinds,pl.kind);var p2=pl.pos;var probe=new Vector3(p2.x,ChangwonData.Height(p2.x,p2.y),p2.y);
            ChangwonData.Road road;float along;Vector3 rp;bool hasRoad=ChangwonData.NearestRoad(probe,120f,out road,out along,out rp);
            Vector3 pos=probe,face=Vector3.forward;Vector2 at,n;
            var b=ChangwonMissions.BuildingAt(p2);
            if(b!=null&&Wall(b.ring,hasRoad?new Vector2(rp.x,rp.z):p2,out at,out n)){face=new Vector3(n.x,0,n.y);pos=new Vector3(at.x,0,at.y)+face*.15f;}
            else if(hasRoad){
                // Free-standing: face the road, and keep off the carriageway and its pavement.
                Vector3 f;road.At(along,out f);f.y=0;f.Normalize();var right=new Vector3(f.z,0,-f.x);float off=Vector3.Dot(probe-rp,right),side=off>=0?1:-1;
                if(Mathf.Abs(off)<road.width*.5f+3f)pos=rp+right*side*(road.width*.5f+3.2f);
                face=-right*side;
            }
            pos.y=ChangwonData.Height(pos.x+face.x,pos.z+face.z);
            float w=Mathf.Clamp(pl.name.Length*.5f+1.2f,2.6f,8f);
            // Several places in one building land on the same shopfront: keep the first (nearest) awning, skip overlapping ones.
            foreach(var o in shops.Values){if(o==null)continue;float gap=(w+o.GetComponent<BoxCollider>().size.x)*.5f;if(ChangwonMissions.Flat(o.transform.position-pos)<gap*gap&&Mathf.Abs(o.transform.position.y-pos.y)<4f)return null;}
            var go=new GameObject("가게 "+pl.name);go.transform.SetParent(transform,false);go.transform.SetPositionAndRotation(pos,Quaternion.LookRotation(face));
            var front=ChangwonMissions.MeshObject("차양·간판",go.transform,Awning(k),mat,pos,go.transform.rotation);front.transform.localScale=new Vector3(w/3f,1,1);
            // Collider from 1.5 m up: above the walking check (which reaches ~1.15 m), still hit by the aim sphere from eye height.
            var col=go.AddComponent<BoxCollider>();col.center=new Vector3(0,2.75f,.3f);col.size=new Vector3(w,2.5f,.6f);
            var thing=go.AddComponent<ChangwonThing>();thing.kind="shop";thing.hint=Labels[k]+" "+pl.name+" 이용";thing.title=pl.name;thing.detail=Labels[k];thing.payload=pl;
            ChangwonSession.Builder.ChangwonSign(pl.name,go.transform,pos+Vector3.up*3.45f+face*.14f,face,.42f,Color.white);
            return go;
        }
        // The point on the footprint's wall facing 'target' (the nearest road) closest to it, and the wall's outward normal.
        static bool Wall(Vector2[] ring,Vector2 target,out Vector2 at,out Vector2 normal)
        {
            at=normal=Vector2.zero;float best=float.MaxValue;bool ccw=Polygon.SignedArea(ring)>0;
            for(int pass=0;pass<2&&best==float.MaxValue;pass++)for(int i=0;i<ring.Length;i++){
                Vector2 a=ring[i],b=ring[(i+1)%ring.Length],d=b-a;float len=d.magnitude;if(len<1.5f)continue;
                var nrm=(ccw?new Vector2(d.y,-d.x):new Vector2(-d.y,d.x))/len;float t;var q=ChangwonMissions.ClosestOnSegment(a,b,target,out t);
                if(pass==0&&Vector2.Dot(target-q,nrm)<=0)continue; // first try only walls that face the target
                float margin=Mathf.Min(.5f,1.6f/len);q=a+d*Mathf.Clamp(t,margin,1-margin);
                float e=(target-q).sqrMagnitude;if(e<best){best=e;at=q;normal=nrm;}
            }
            return best<float.MaxValue;
        }
        // Striped awning over a dark signboard, 3 m wide (scaled to the name), against a wall at z=0, facing +z.
        Mesh Awning(int k)
        {
            if(awnings[k]!=null)return awnings[k];
            var mb=new MeshBuild(1);var c=Colors[k];var white=new Color32(245,245,240,255);
            mb.Box(0,new Vector3(0,3.45f,.06f),new Vector3(3f,.8f,.12f),Quaternion.identity,new Color32(35,35,42,255));
            for(int i=0;i<6;i++)mb.Box(0,new Vector3(-1.25f+i*.5f,2.85f,.6f),new Vector3(.5f,.2f,1.2f),Quaternion.Euler(12f,0,0),(i&1)==0?c:white); // sloping down from the wall
            mb.Box(0,new Vector3(0,2.66f,1.2f),new Vector3(3f,.28f,.04f),Quaternion.identity,c);
            return awnings[k]=mb.ToMesh("차양 "+Labels[k]);
        }

        // ------------------------------------------------------------------ 누비자 public bikes
        // One terminal per 600 m cell: the town bus stop with the smallest position hash (deterministic).
        void FindDocks()
        {
            docks=new List<Dock>();var cells=new Dictionary<long,int>();var hashes=new List<uint>();
            foreach(var s in ChangwonData.Infrastructure){
                if(s.kind!="bus_stop")continue;byte land=ChangwonData.LandAt(s.pos.x,s.pos.y);
                if(land!=ChangwonData.Residential&&land!=ChangwonData.Commercial&&land!=ChangwonData.Urban)continue; // most stops sit on Urban cells
                long key=((long)Mathf.FloorToInt(s.pos.x/DockSpacing)<<32)^(uint)Mathf.FloorToInt(s.pos.y/DockSpacing);uint h=Hash(s.pos);int i;
                if(cells.TryGetValue(key,out i)){if(h<hashes[i]){hashes[i]=h;docks[i].pos=s.pos;}}
                else{cells[key]=docks.Count;docks.Add(new Dock{pos=s.pos});hashes.Add(h);}
            }
        }
        static uint Hash(Vector2 p){unchecked{uint h=(uint)Mathf.RoundToInt(p.x*10)*73856093u^(uint)Mathf.RoundToInt(p.y*10)*19349663u;h^=h>>13;h*=0x5bd1e995;h^=h>>15;return h;}}

        // A terminal on the pavement 7 m along the road from the bus stop, facing the road.
        GameObject BuildDock(Vector2 stop)
        {
            var probe=new Vector3(stop.x,ChangwonData.Height(stop.x,stop.y),stop.y);Vector3 pos=probe,face=Vector3.forward;
            ChangwonData.Road road;float along;Vector3 q;
            if(ChangwonData.NearestRoad(probe,40f,out road,out along,out q)){
                Vector3 f;q=road.At(Mathf.Clamp(along+7f,0,road.length),out f);f.y=0;f.Normalize();var right=new Vector3(f.z,0,-f.x);
                float side=Vector3.Dot(probe-q,right)>=0?1:-1;pos=q+right*side*(road.width*.5f+1.9f);face=-right*side;
                pos.y=Mathf.Max(q.y+.2f,ChangwonData.Height(pos.x,pos.z));
            }
            var go=ChangwonMissions.MeshObject("누비자 터미널",transform,Stand(),mat,pos,Quaternion.LookRotation(face));
            var col=go.AddComponent<BoxCollider>();col.center=new Vector3(0,.8f,.2f);col.size=new Vector3(2.8f,1.6f,1.6f);
            var thing=go.AddComponent<ChangwonThing>();thing.kind="nubija";thing.hint="누비자 자전거 빌리기 (무료)";thing.title="누비자";thing.detail="창원시 공영자전거";
            ChangwonSession.Builder.ChangwonSign("누비자",go.transform,pos+Vector3.up*2.3f,face,.5f,new Color(.45f,.95f,.5f));
            return go;
        }
        Mesh Stand()
        {
            if(stand!=null)return stand;
            var mb=new MeshBuild(1);var green=new Color32(40,165,75,255);
            mb.Box(0,new Vector3(0,.05f,0),new Vector3(2.8f,.1f,1.5f),Quaternion.identity,new Color32(150,150,150,255));
            mb.Box(0,new Vector3(-1.15f,.8f,-.45f),new Vector3(.45f,1.6f,.35f),Quaternion.identity,green);
            mb.Box(0,new Vector3(-1.15f,1.35f,-.26f),new Vector3(.36f,.3f,.04f),Quaternion.identity,new Color32(235,240,235,255));
            mb.Box(0,new Vector3(.25f,.45f,-.55f),new Vector3(2.1f,.25f,.12f),Quaternion.identity,green);
            for(int i=0;i<3;i++)Bike(mb,new Vector3(-.35f+i*.6f,.1f,.1f),Quaternion.Euler(0,180,0)); // docked, front wheel in the rail
            return stand=mb.ToMesh("누비자 터미널");
        }
        // A 누비자 bicycle along +z with its wheels on y=0: two tyres, green frame, white fork and basket.
        static void Bike(MeshBuild mb,Vector3 o,Quaternion rot)
        {
            Color32 tyre=new Color32(35,35,38,255),frame=new Color32(40,165,75,255),white=new Color32(235,240,235,255),dark=new Color32(30,30,30,255);
            const float R=.34f;
            foreach(float wz in new[]{.55f,-.55f}){
                for(int k=0;k<12;k++){float a=k*30f,rad=a*Mathf.Deg2Rad;mb.Box(0,o+rot*new Vector3(0,R+Mathf.Cos(rad)*R,wz+Mathf.Sin(rad)*R),new Vector3(.05f,.06f,.19f),rot*Quaternion.Euler(a,0,0),tyre);}
                mb.Box(0,o+rot*new Vector3(0,R,wz),new Vector3(.08f,.06f,.06f),rot,white);
            }
            Bar(mb,o,rot,new Vector3(0,.34f,0),new Vector3(0,.86f,.4f),.05f,frame);
            Bar(mb,o,rot,new Vector3(0,.34f,0),new Vector3(0,.92f,-.16f),.05f,frame);
            Bar(mb,o,rot,new Vector3(0,.34f,0),new Vector3(0,.34f,-.55f),.04f,frame);
            Bar(mb,o,rot,new Vector3(0,.92f,-.16f),new Vector3(0,.34f,-.55f),.04f,frame);
            Bar(mb,o,rot,new Vector3(0,.86f,.4f),new Vector3(0,.34f,.55f),.04f,white);
            Bar(mb,o,rot,new Vector3(0,.86f,.4f),new Vector3(0,1.05f,.36f),.04f,frame);
            mb.Box(0,o+rot*new Vector3(0,1.05f,.36f),new Vector3(.56f,.035f,.035f),rot,dark);
            mb.Box(0,o+rot*new Vector3(0,.96f,-.18f),new Vector3(.15f,.06f,.26f),rot,dark);
            mb.Box(0,o+rot*new Vector3(0,.88f,.62f),new Vector3(.34f,.22f,.26f),rot,white);
        }
        static void Bar(MeshBuild mb,Vector3 o,Quaternion rot,Vector3 a,Vector3 b,float t,Color32 c){var d=b-a;mb.Box(0,o+rot*((a+b)*.5f),new Vector3(t,t,d.magnitude),rot*Quaternion.LookRotation(d),c);}

        void RentBike(ChangwonThing thing)
        {
            if(ChangwonSession.EnterCar==null||ChangwonSession.Root==null)return;
            var t=thing.transform;var pos=t.position+t.forward*1.7f;
            if(bike!=null&&bike!=ChangwonSession.PlayerCar)Destroy(bike.gameObject); // one lent bike at a time
            if(bikeMesh==null){var mb=new MeshBuild(1);Bike(mb,Vector3.zero,Quaternion.identity);bikeMesh=mb.ToMesh("누비자 자전거");}
            var go=ChangwonMissions.MeshObject("누비자 자전거",ChangwonSession.Root,bikeMesh,mat,Vector3.zero,Quaternion.identity);
            var box=go.AddComponent<BoxCollider>();box.center=new Vector3(0,.6f,0);box.size=new Vector3(.6f,1.2f,1.8f);
            var th=go.AddComponent<ChangwonThing>();th.kind="car";th.hint="누비자 타기";
            var car=go.AddComponent<ChangwonCar>();car.model="누비자";car.bike=true;car.maxSpeed=8f;car.acceleration=3f;car.length=1.8f;car.width=.7f;car.wheelBase=1.1f;
            car.Init(pos,t.right);car.Move(Vector3.zero,.02f);bike=car;
            ChangwonSession.EnterCar(car);car.SetEngine(false);
            Sfx.Play("bell",.6f,1.4f);
            ChangwonSession.Toast("누비자 대여 완료 (무료) · W/S 페달·브레이크 · A/D 조향 · F 내리기");
        }

        // ------------------------------------------------------------------ use and the shop card
        public bool Handles(ChangwonThing thing){return thing!=null&&(thing.kind=="shop"||thing.kind=="nubija");}
        public void Use(ChangwonThing thing)
        {
            if(!Handles(thing)){ChangwonMissions.Forward(this,thing);return;}
            if(thing.kind=="nubija"){RentBike(thing);return;}
            var pl=thing.payload as ChangwonData.Place;if(pl==null)return;
            open=pl;openKind=Mathf.Max(0,Array.IndexOf(Kinds,pl.kind));if(pl.name.Contains("시장"))openKind=Market;
            string gu=ChangwonAreas.GuAt(pl.pos.x,pl.pos.y);openWhere=ChangwonAreas.Name(pl.pos.x,pl.pos.y);
            openItems.Clear();openItems.AddRange(openKind==3&&gu.StartsWith("마산")?MasanFood:Items[openKind]);
            if(gu=="진해구"&&(openKind<=3||openKind==Market))openItems.Add("벚꽃빵");
            if(openItems.Count>5)openItems.RemoveRange(5,openItems.Count-5);
            ChangwonSession.UiCapture=true;Sfx.Play("shop-door",.6f);
        }
        void Close(){open=null;ChangwonSession.UiCapture=false;}
        void DrawGUI(float w,float h)
        {
            if(open==null||ChangwonSession.Box==null)return;
            float bw=Mathf.Min(520,w-40),bh=168+openItems.Count*52;var box=new Rect(w*.5f-bw*.5f,h*.5f-bh*.5f,bw,bh);GUI.Box(box,"",ChangwonSession.Box);
            float x=box.x+28,iw=bw-56,y=box.y+100;
            GUI.Label(new Rect(x,box.y+18,iw,44),open.name,ChangwonSession.Title);
            GUI.Label(new Rect(x,box.y+62,iw,26),Labels[openKind]+(openWhere.Length>0?" · "+openWhere:"")+" · 모든 상품 무료",ChangwonSession.Small);
            foreach(var item in openItems){if(GUI.Button(new Rect(x,y,iw,44),item,ChangwonSession.Button))Buy(item);y+=52;}
            if(GUI.Button(new Rect(x,y+4,iw,44),"닫기",ChangwonSession.Button))Close();
        }
        void Buy(string item)
        {
            var progress=ChangwonSession.Progress;progress.score+=5;
            int buys=0,first=-1;
            for(int i=0;i<progress.done.Count;i++)if(progress.done[i].StartsWith("buy:",StringComparison.Ordinal)){if(first<0)first=i;buys++;}
            if(buys>=200)progress.done.RemoveAt(first); // keep at most 200 purchases
            progress.done.Add("buy:"+item);Sfx.Play("coin",.7f);
            if(item=="주유하기"){fuelUntil=Time.time+60f;ChangwonSession.Toast("주유 완료! 연료가 가득 찼습니다 (+5점)");}
            else ChangwonSession.Toast(open.name+": "+item+(openKind==Bank?" 완료":" 구매 완료")+" · 무료 (+5점)");
        }
    }
}
