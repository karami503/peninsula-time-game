using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeninsulaTime
{
    // GTA-style, non-violent activities in the Changwon open world: gold beacons at the 52 명소 that stamp the player's
    // passport, and missions started at green kiosks (F) — taxi fares, 마산 아구찜 delivery, checkpoint races and the
    // 진해 cherry-blossom photo tour. Also adds ChangwonShops on a child object, which holds the beacons and kiosks too
    // (their uses reach ChangwonShops first as the nearest IChangwonUse parent; it forwards them here).
    public class ChangwonMissions : MonoBehaviour, IChangwonUse
    {
        class Marker {public ChangwonLandmarks.Landmark lm;public GameObject go;public MeshFilter mf;public TextMesh sign;public float radius,minY;}
        class Spot {public string id,title,where,text,reward;public int type;public Vector3 pos,facing;public GameObject go;public Transform sign;}
        const int Taxi=0,Delivery=1,Race=2,Blossom=3,Fares=5;
        static readonly string[] RaceNames={"창원광장 3바퀴","창원대로 질주","안민고개 와인딩","마창대교 횡단"};
        static readonly string[] BlossomIds={"lm14","lm13","lm15"}; // 여좌천, 경화역 벚꽃길, 제황산공원
        static readonly string[] TaxiKinds={"food","cafe","mall","station","hospital","hotel"};
        static readonly string[] Thanks={
            "감사합니다, 기사님! 창원 길을 정말 잘 아시네요.","덕분에 약속 시간에 딱 맞췄어요. 좋은 하루 보내세요!",
            "승차감 최고예요. 다음에도 기사님 택시 탈게요!","창원은 길이 넓어서 좋죠? 안전 운전 하세요~",
            "마산 가시면 아구찜 꼭 드셔 보세요. 감사합니다!","벚꽃 필 때 진해에도 한번 가 보세요. 수고하셨어요!"};
        static readonly Color GoldText=new Color(1f,.85f,.4f),GreyText=new Color(.7f,.7f,.7f),GreenText=new Color(.55f,1f,.6f);
        static readonly Color GreenBlip=new Color(.25f,.9f,.35f),GoalBlip=new Color(1f,.9f,.2f),NextBlip=new Color(.6f,.85f,1f),PinkBlip=new Color(1f,.6f,.8f);

        readonly Dictionary<string,Marker> markers=new Dictionary<string,Marker>();
        readonly List<Spot> spots=new List<Spot>();
        readonly List<KeyValuePair<Vector3,Color>> blips=new List<KeyValuePair<Vector3,Color>>(),lastBlips=new List<KeyValuePair<Vector3,Color>>();
        readonly List<string> hud=new List<string>(),lastHud=new List<string>();
        readonly List<ChangwonData.Place> candidates=new List<ChangwonData.Place>();
        readonly System.Random rnd=new System.Random();
        Transform holder;Material mat;Mesh gold,grey,kiosk,gateNow,gateNext;GameObject gateA,gateB;ChangwonCar missionCar;
        float nextScan,nextStamp;Spot card;bool objectiveMine;

        // The running mission (-1: none).
        int mission=-1,stage,count,race,next,placed=-1,shownKey=int.MinValue;Spot from;float deadline,started,limit;bool racing;
        Vector3 goal;string goalName="";Vector2[] goalRing;Pedestrian customer;Transform waveArm;Quaternion waveRest;
        List<Vector3> course;bool[] visited;

        void Start()
        {
            var child=new GameObject("가게·누비자·명소·임무");child.transform.SetParent(transform,false);child.AddComponent<ChangwonShops>();holder=child.transform;
            mat=VertexMaterial();
            gold=Pillar("명소 표지",new Color32(255,190,50,255),new Color32(255,248,220,255),42f,false);
            grey=Pillar("명소 표지 (도장 완료)",new Color32(130,130,130,255),new Color32(200,200,200,255),42f,false);
            kiosk=Pillar("임무 키오스크",new Color32(40,200,80,255),new Color32(200,255,210,255),26f,true);
            gateNow=Gate(new Color32(255,200,30,255),6f);gateNext=Gate(new Color32(120,200,255,255),6f);
            gateA=MeshObject("레이스 게이트",holder,gateNow,mat,Vector3.zero,Quaternion.identity);gateB=MeshObject("다음 게이트",holder,gateNext,mat,Vector3.zero,Quaternion.identity);
            gateA.SetActive(false);gateB.SetActive(false);
            nextStamp=Time.time+4f; // let the welcome toast (controls) show before a 시청 stamp toast replaces it
            ChangwonSession.Overlay+=DrawGUI;
        }
        void OnDestroy()
        {
            ChangwonSession.Overlay-=DrawGUI;
            if(card!=null)ChangwonSession.UiCapture=false;
            if(objectiveMine)ChangwonSession.Objective="";
            blips.Clear();hud.Clear();Publish(ChangwonSession.Blips,lastBlips,blips);Publish(ChangwonSession.HudLines,lastHud,hud);
            foreach(var m in new UnityEngine.Object[]{mat,gold,grey,kiosk,gateNow,gateNext})if(m!=null)Destroy(m);
        }

        // ------------------------------------------------------------------ shared helpers (also used by ChangwonShops)
        internal static Material VertexMaterial()
        {
            // Always a new material: callers Destroy it in OnDestroy, so it must never be the builder's cached one.
            var sh=Shader.Find("Peninsula/VertexTerrain");if(sh==null)sh=Shader.Find("Standard");
            return new Material(sh){name="창원 활동 (정점 색)"};
        }
        internal static GameObject MeshObject(string name,Transform parent,Mesh mesh,Material material,Vector3 position,Quaternion rotation)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.position=position;go.transform.rotation=rotation;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;
            return go;
        }
        // Shared HUD lists: take back what this system added last frame, add this frame's entries.
        internal static void Publish(List<KeyValuePair<Vector3,Color>> shared,List<KeyValuePair<Vector3,Color>> last,List<KeyValuePair<Vector3,Color>> now)
        {
            foreach(var b in last)for(int i=shared.Count-1;i>=0;i--)if(shared[i].Key==b.Key&&shared[i].Value==b.Value){shared.RemoveAt(i);break;}
            shared.AddRange(now);last.Clear();last.AddRange(now);
        }
        internal static void Publish(List<string> shared,List<string> last,List<string> now)
        {
            foreach(var s in last)for(int i=shared.Count-1;i>=0;i--)if(ReferenceEquals(shared[i],s)){shared.RemoveAt(i);break;}
            shared.AddRange(now);last.Clear();last.AddRange(now);
        }
        // A thing this system does not handle: hand it to the system on world.root that does.
        internal static void Forward(IChangwonUse self,ChangwonThing thing)
        {
            if(ChangwonSession.Root!=null)foreach(var h in ChangwonSession.Root.GetComponents<IChangwonUse>())if(!ReferenceEquals(h,self)&&h.Handles(thing)){h.Use(thing);return;}
            if(!string.IsNullOrEmpty(thing.detail))ChangwonSession.Toast(thing.detail);
        }
        // The building whose footprint contains p (xz), searching the chunks around it.
        internal static ChangwonData.Building BuildingAt(Vector2 p)
        {
            var c=ChangwonData.ChunkOf(p.x,p.y);float size=ChangwonData.ChunkSize;
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++){
                int cx=c.x+dx,cz=c.y+dz;if(cx<0||cz<0||cx>=ChangwonData.CX||cz>=ChangwonData.CZ)continue;
                var min=ChangwonData.ChunkMin(cx,cz);
                if(p.x<min.x-150||p.x>min.x+size+150||p.y<min.z-150||p.y>min.z+size+150)continue;
                foreach(var b in ChangwonData.BuildingsIn(cx,cz))if((b.center-p).sqrMagnitude<150f*150f&&Polygon.Contains(b.ring,p))return b;
            }
            return null;
        }
        internal static Vector2 ClosestOnSegment(Vector2 a,Vector2 b,Vector2 p,out float t)
        {
            var d=b-a;float l2=d.sqrMagnitude;t=l2<1e-6f?0:Mathf.Clamp01(Vector2.Dot(p-a,d)/l2);return a+d*t;
        }
        internal static float EdgeDistance(Vector2[] ring,Vector2 p)
        {
            float best=float.MaxValue;float t;
            for(int i=0;i<ring.Length;i++)best=Mathf.Min(best,(ClosestOnSegment(ring[i],ring[(i+1)%ring.Length],p,out t)-p).sqrMagnitude);
            return Mathf.Sqrt(best);
        }
        internal static float Flat(Vector3 v){return v.x*v.x+v.z*v.z;}
        static string Clock(float seconds){int s=Mathf.Max(0,Mathf.CeilToInt(seconds));return (s/60).ToString("00")+":"+(s%60).ToString("00");}
        static string Lap(float seconds){return ((int)(seconds/60)).ToString("00")+":"+(seconds%60f).ToString("00.0",CultureInfo.InvariantCulture);}

        // ------------------------------------------------------------------ meshes
        // A slim beacon fading upward, with a ring on the ground (and a kiosk under it for mission points). Faces +z.
        static Mesh Pillar(string name,Color32 low,Color32 high,float height,bool withKiosk)
        {
            var mb=new MeshBuild(1);float y0=0;
            if(withKiosk){
                mb.Box(0,new Vector3(0,1.1f,0),new Vector3(1.1f,2.2f,1.1f),Quaternion.identity,low);
                mb.Box(0,new Vector3(0,1.55f,.56f),new Vector3(.8f,.6f,.04f),Quaternion.identity,new Color32(245,250,245,255));
                y0=2.2f;
            }
            const int Steps=8;float seg=(height-y0)/Steps;
            for(int i=0;i<Steps;i++){float t=i/(float)(Steps-1),w=Mathf.Lerp(.7f,.22f,t);mb.Box(0,new Vector3(0,y0+seg*(i+.5f),0),new Vector3(w,seg,w),Quaternion.identity,Color32.Lerp(low,high,t));}
            for(int k=0;k<16;k++){float a=k*Mathf.PI/8;mb.Box(0,new Vector3(Mathf.Sin(a)*2.4f,.08f,Mathf.Cos(a)*2.4f),new Vector3(.95f,.16f,.22f),Quaternion.Euler(0,k*22.5f,0),low);}
            return mb.ToMesh(name);
        }
        // A vertical ring standing on the ground, facing +z (no collider).
        static Mesh Gate(Color32 color,float r)
        {
            var mb=new MeshBuild(1);const int N=28;float seg=2*Mathf.PI*r/N*1.08f;
            for(int k=0;k<N;k++){float a=k*360f/N,rad=a*Mathf.Deg2Rad;mb.Box(0,new Vector3(Mathf.Sin(rad)*r,r+Mathf.Cos(rad)*r,0),new Vector3(seg,.7f,.7f),Quaternion.Euler(0,0,-a),color);}
            return mb.ToMesh("레이스 게이트");
        }

        // ------------------------------------------------------------------ setup
        void SetupSpots()
        {
            AddSpot("taxi-hall",Taxi,"택시 운행","창원시청 앞 택시 승강장",ChangwonData.ToXZ(128.68185,35.22765),18f,
                "창원 시내 곳곳의 손님을 태워 목적지까지 모셔다 드립니다. 손님 옆(8 m 이내)에 차를 세우면 탑승하고, 제한 시간 안에 목적지 앞(15 m)에 정차하면 요금을 받습니다. 손님 5명을 모시면 완료 · J 취소.",
                "손님당 100점 + 남은 시간 보너스");
            AddSpot("taxi-station",Taxi,"택시 운행","창원중앙역 택시 승강장",new Vector2(7446.7f,4705.2f),0,
                "KTX에서 내린 손님을 태워 창원 곳곳으로 모십니다. 손님 옆에 차를 세우면 탑승하고, 제한 시간 안에 목적지 앞에 정차하세요. 손님 5명을 모시면 완료 · J 취소.",
                "손님당 100점 + 남은 시간 보너스");
            AddSpot("agujjim",Delivery,"아구찜 배달","마산 오동동 아구찜 거리",new Vector2(-3790f,690f),0,
                "오동동 할매집에서 갓 만든 마산 아구찜을 아파트 단지까지 배달합니다. 걸어서, 누비자 자전거로, 차로 — 식기 전에만 도착하세요! · J 취소.",
                "150점 + 남은 시간 보너스");
            AddSpot("race",Race,"레이스","창원광장",new Vector2(5657f,2790f),0,
                "창원광장 회전교차로, 창원대로, 안민고개, 마창대교에서 체크포인트 레이스를 달립니다. 노란 출발 게이트를 지나면 타이머가 시작됩니다. 차가 없으면 한 대 내드립니다 · J 취소.",
                "완주 300점 · 최고 기록 갱신 +200점");
            AddSpot("blossom",Blossom,"벚꽃 사진 투어","진해 중원로터리",new Vector2(3608f,-5560f),0,
                "10분 안에 진해군항제의 명소 여좌천(로망스다리), 경화역 벚꽃길, 제황산공원(진해탑)을 돌며 벚꽃 사진을 찍으세요. 걸어서는 빠듯하니 차나 누비자를 이용하세요! · J 취소.",
                "400점 + 남은 시간 보너스");
        }
        // A kiosk on the pavement of the road nearest 'at' (moved 'shift' metres along it), facing the road.
        void AddSpot(string id,int type,string title,string where,Vector2 at,float shift,string text,string reward)
        {
            var probe=new Vector3(at.x,ChangwonData.Height(at.x,at.y),at.y);Vector3 pos=probe,facing=Vector3.forward;
            ChangwonData.Road road;float along;Vector3 q;
            if(ChangwonData.NearestRoad(probe,250f,out road,out along,out q)){
                Vector3 f;q=road.At(Mathf.Clamp(along+shift,0,road.length),out f);f.y=0;f.Normalize();var right=new Vector3(f.z,0,-f.x);
                float side=Vector3.Dot(probe-q,right)>=0?1:-1;
                pos=q+right*side*(road.width*.5f+2.4f);pos.y=Mathf.Max(q.y+.2f,ChangwonData.Height(pos.x,pos.z));facing=-right*side;
            }
            spots.Add(new Spot{id=id,type=type,title=title,where=where,text=text,reward=reward,pos=pos,facing=facing});
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if(!ChangwonData.Loaded||!ChangwonSession.Active||ChangwonSession.Builder==null||holder==null)return;
            if(spots.Count==0)SetupSpots();
            // Esc also closes the card, so it can never stay open under the pause menu (whose clicks it would steal).
            if(card!=null&&Input.GetKeyDown(KeyCode.Escape))Close();
            var p=ChangwonSession.PlayerFeet;
            if(Time.time>=nextScan){nextScan=Time.time+1f;Scan(p);}
            if(Time.time>=nextStamp){nextStamp=Time.time+.25f;Stamps(p);}
            foreach(var m in markers.Values)Face(m.sign.transform,p);
            foreach(var s in spots)if(s.sign!=null)Face(s.sign,p);
            if(mission<0)return;
            if(Input.GetKeyDown(KeyCode.J)&&!ChangwonSession.UiCapture){End(false,"임무를 취소했습니다.",0);return;}
            switch(mission){case Taxi:RunTaxi();break;case Delivery:RunDelivery(p);break;case Race:RunRace(p);break;case Blossom:RunBlossom(p);break;}
        }
        static void Face(Transform sign,Vector3 viewer){var v=sign.position-viewer;v.y=0;if(v.sqrMagnitude>1f)sign.rotation=Quaternion.LookRotation(v);}

        void LateUpdate()
        {
            if(!ChangwonSession.Active)return;
            blips.Clear();
            foreach(var s in spots)blips.Add(new KeyValuePair<Vector3,Color>(s.pos,GreenBlip));
            if(mission==Taxi||mission==Delivery)blips.Add(new KeyValuePair<Vector3,Color>(goal,GoalBlip));
            if(mission==Race&&course!=null){
                if(next<course.Count)blips.Add(new KeyValuePair<Vector3,Color>(course[next],GoalBlip));
                if(next+1<course.Count)blips.Add(new KeyValuePair<Vector3,Color>(course[next+1],NextBlip));
            }
            if(mission==Blossom)for(int i=0;i<BlossomIds.Length;i++){var lm=Landmark(BlossomIds[i]);if(lm!=null&&!visited[i])blips.Add(new KeyValuePair<Vector3,Color>(new Vector3(lm.pos.x,0,lm.pos.y),PinkBlip));}
            if(mission<0)hud.Clear();
            Publish(ChangwonSession.Blips,lastBlips,blips);Publish(ChangwonSession.HudLines,lastHud,hud);
        }

        // Beacons and kiosks exist within ~600 m of the player.
        void Scan(Vector3 p)
        {
            var p2=new Vector2(p.x,p.z);
            foreach(var lm in ChangwonLandmarks.All){
                float d=(lm.pos-p2).sqrMagnitude;Marker m;bool has=markers.TryGetValue(lm.id,out m);
                if(!has&&d<600f*600f)markers[lm.id]=SpawnMarker(lm);
                else if(has&&d>700f*700f){Destroy(m.go);markers.Remove(lm.id);}
            }
            foreach(var s in spots){
                float d=Flat(s.pos-p);
                if(s.go==null&&d<600f*600f)SpawnKiosk(s);
                else if(s.go!=null&&d>700f*700f){Destroy(s.go);s.go=null;s.sign=null;}
            }
        }
        Marker SpawnMarker(ChangwonLandmarks.Landmark lm)
        {
            float x=lm.pos.x,z=lm.pos.y,y=ChangwonData.Height(x,z);
            // Stamp radius: spec values (35 m, 60 m islands/bridges, 80 m summits) plus room for places one cannot stand on.
            float radius=lm.kind switch{"mountain"=>80f,"island"=>60f,"bridge"=>60f,"lake"=>150f,"plaza"=>120f,"port"=>120f,"industrial"=>150f,"campus"=>80f,"stadium"=>80f,_=>35f};
            float minY=float.MinValue,deck;
            if(lm.kind=="bridge"&&Deck(lm.pos,out deck))y=deck;
            if(lm.kind=="mountain")minY=Mathf.Min(y,lm.height>0?lm.height:y)-60f; // on the summit, not in a tunnel under it
            var inside=BuildingAt(lm.pos);if(inside!=null)radius=Mathf.Max(radius,EdgeDistance(inside.ring,lm.pos)+30f);
            bool got=ChangwonSession.Progress.stamps.Contains(lm.id);var at=new Vector3(x,y,z);
            var go=MeshObject("명소 "+lm.name,holder,got?grey:gold,mat,at,Quaternion.identity);
            var sign=ChangwonSession.Builder.ChangwonSign(lm.name,go.transform,at+Vector3.up*5f,Vector3.forward,1.3f,got?GreyText:GoldText);
            return new Marker{lm=lm,go=go,mf=go.GetComponent<MeshFilter>(),sign=sign,radius=radius,minY=minY};
        }
        // Height of the bridge deck nearest p (within 80 m).
        static bool Deck(Vector2 p,out float y)
        {
            y=0;float best=80f*80f;bool found=false;var c=ChangwonData.ChunkOf(p.x,p.y);
            foreach(int ri in ChangwonData.RoadsInChunk[c.y*ChangwonData.CX+c.x]){
                var r=ChangwonData.Roads[ri];if(!r.Drivable)continue;
                for(int i=0;i<r.pts.Length;i++){if(r.kind[i]!=ChangwonData.Bridge)continue;float d=(new Vector2(r.pts[i].x,r.pts[i].z)-p).sqrMagnitude;if(d<best){best=d;y=r.pts[i].y;found=true;}}
            }
            return found;
        }
        void SpawnKiosk(Spot s)
        {
            var go=MeshObject("임무 "+s.title+" · "+s.where,holder,kiosk,mat,s.pos,Quaternion.LookRotation(s.facing));
            var col=go.AddComponent<BoxCollider>();col.center=new Vector3(0,1.2f,0);col.size=new Vector3(1.3f,2.4f,1.3f);
            var thing=go.AddComponent<ChangwonThing>();thing.kind="mission";thing.hint=s.title+" 임무 보기";thing.title=s.title;thing.detail=s.where;thing.payload=s;
            s.sign=ChangwonSession.Builder.ChangwonSign(s.title+"\n"+s.where,go.transform,s.pos+Vector3.up*4f,s.facing,.7f,GreenText).transform;
            s.go=go;
        }
        void Stamps(Vector3 p)
        {
            var progress=ChangwonSession.Progress;
            foreach(var m in markers.Values){
                if(p.y<m.minY||Flat(new Vector3(m.lm.pos.x,0,m.lm.pos.y)-p)>m.radius*m.radius||progress.stamps.Contains(m.lm.id))continue;
                progress.stamps.Add(m.lm.id);progress.score+=100;Sfx.Play("arrival",.8f);
                ChangwonSession.Toast("명소 도장 획득: "+m.lm.name+" ("+progress.stamps.Count+"/"+ChangwonLandmarks.All.Count+") — "+m.lm.activity);
                m.mf.sharedMesh=grey;m.sign.color=GreyText;
            }
        }
        static ChangwonLandmarks.Landmark Landmark(string id){foreach(var l in ChangwonLandmarks.All)if(l.id==id)return l;return null;}

        // ------------------------------------------------------------------ mission card
        public bool Handles(ChangwonThing thing){return thing!=null&&thing.kind=="mission";}
        public void Use(ChangwonThing thing)
        {
            if(!Handles(thing)){Forward(this,thing);return;}
            card=thing.payload as Spot;if(card==null)return;
            ChangwonSession.UiCapture=true;Sfx.Play("tap");
        }
        void Close(){card=null;ChangwonSession.UiCapture=false;}
        void DrawGUI(float w,float h)
        {
            if(card==null||ChangwonSession.Box==null)return;
            bool racePick=card.type==Race;float bw=Mathf.Min(580,w-40),bh=racePick?500:340;
            var box=new Rect(w*.5f-bw*.5f,h*.5f-bh*.5f,bw,bh);GUI.Box(box,"",ChangwonSession.Box);
            float x=box.x+28,iw=bw-56;
            GUI.Label(new Rect(x,box.y+18,iw,44),card.title,ChangwonSession.Title);
            GUI.Label(new Rect(x,box.y+62,iw,24),card.where+(mission>=0?"  ·  새 임무를 시작하면 진행 중인 임무는 취소됩니다":""),ChangwonSession.Small);
            GUI.Label(new Rect(x,box.y+90,iw,130),card.text,ChangwonSession.Body);
            GUI.Label(new Rect(x,box.y+222,iw,24),"보상: "+card.reward,ChangwonSession.Small);
            float y=box.y+256;
            if(racePick){
                for(int i=0;i<RaceNames.Length;i++){float best=Best(i);if(GUI.Button(new Rect(x,y,iw,40),RaceNames[i]+"   ·   최고 기록 "+(best>0?Lap(best):"없음"),ChangwonSession.Button)){Begin(card,i);return;}y+=46;}
                y+=8;
            }else if(GUI.Button(new Rect(x,y,iw*.5f-6,48),"시작",ChangwonSession.Accent)){Begin(card,0);return;}
            if(GUI.Button(new Rect(racePick?x:x+iw*.5f+6,y,racePick?iw:iw*.5f-6,48),"닫기",ChangwonSession.Button))Close();
        }

        // ------------------------------------------------------------------ running missions
        void Begin(Spot s,int option)
        {
            Close();if(mission>=0)End(false,null,0);
            mission=s.type;from=s;stage=0;count=0;started=Time.time;shownKey=int.MinValue;hud.Clear();
            switch(s.type){
                case Taxi:EnsureCar(s,"CarWhite",true);NextCustomer();break;
                case Delivery:
                    if(!PickApartment(new Vector2(s.pos.x,s.pos.z))){End(false,"배달할 아파트를 찾지 못했습니다.",0);return;}
                    limit=Vector2.Distance(new Vector2(s.pos.x,s.pos.z),new Vector2(goal.x,goal.z))*1.3f/6.5f+60f;deadline=Time.time+limit;
                    Guide(new Vector2(goal.x,goal.z),goalName);ChangwonSession.Toast("아구찜 포장 완료! "+goalName+"까지 "+Clock(limit)+" 안에 배달하세요");
                    break;
                case Race:
                    race=option;course=Course(option);
                    if(course.Count<2){End(false,"레이스 경로를 찾지 못했습니다.",0);return;}
                    EnsureCar(s,"CarRed",false);next=0;racing=false;
                    if(Flat(course[0]-ChangwonSession.PlayerFeet)>80f*80f)Guide(new Vector2(course[0].x,course[0].z),goalName="레이스 출발선");
                    ChangwonSession.Toast(RaceNames[race]+" · 노란 게이트(출발선)를 지나면 타이머가 시작됩니다");
                    break;
                case Blossom:
                    visited=new bool[BlossomIds.Length];limit=600f;deadline=Time.time+limit;GuideBlossom();
                    ChangwonSession.Toast("벚꽃 사진 투어 시작! 10분 안에 세 곳을 모두 찍으세요");
                    break;
            }
            Sfx.Play("bell",.7f);
        }
        void End(bool success,string message,int pay)
        {
            if(mission<0)return;
            var progress=ChangwonSession.Progress;
            if(success){
                progress.score+=pay;progress.missionsDone++;
                string id=mission==Race?"race:"+RaceNames[race]:from.id;if(!progress.done.Contains(id))progress.done.Add(id);
                Sfx.Play("arrival");
            }
            if(message!=null)ChangwonSession.Toast(message);
            if(customer!=null)Destroy(customer.gameObject);customer=null;waveArm=null;
            gateA.SetActive(false);gateB.SetActive(false);placed=-1;
            if(ChangwonSession.Waypoint.HasValue&&ChangwonSession.WaypointName==goalName)Guide(null,"");
            mission=-1;course=null;goalRing=null;goalName="";hud.Clear();
            if(objectiveMine){ChangwonSession.Objective="";objectiveMine=false;}
        }
        void Objective(string text){ChangwonSession.Objective=text;objectiveMine=true;}
        // Rebuild the HUD texts only when what they show changes (once a second, or a tenth for race timers).
        bool Changed(int key){if(key==shownKey)return false;shownKey=key;hud.Clear();return true;}

        // GPS: a waypoint with GameController's purple route (null clears it).
        static void Guide(Vector2? at,string name)
        {
            if(ChangwonSession.SetWaypoint!=null){ChangwonSession.SetWaypoint(at,name??"");return;}
            ChangwonSession.Waypoint=at;ChangwonSession.WaypointName=name??"";
        }

        // A car (a taxi with a roof sign, or a race car) on the road by the kiosk when the player is on foot.
        void EnsureCar(Spot s,string model,bool taxi)
        {
            if(ChangwonSession.PlayerCar!=null||ChangwonSession.Builder==null)return;
            ChangwonData.Road road;float along;Vector3 q;
            if(!ChangwonData.NearestRoad(s.pos,120f,out road,out along,out q))return;
            Vector3 f;road.At(along,out f);f.y=0;f.Normalize();var pos=q+new Vector3(f.z,0,-f.x)*Mathf.Min(road.width*.25f,3f);
            var go=ChangwonSession.Builder.ChangwonVehicle(model,pos,"차 타기 (F)");if(go==null)return;
            go.transform.SetParent(ChangwonSession.Root,true);
            if(missionCar!=null&&missionCar!=ChangwonSession.PlayerCar)Destroy(missionCar.gameObject);
            var car=go.AddComponent<ChangwonCar>();car.model=model;car.Init(pos,f);car.Move(Vector3.zero,.02f);missionCar=car;
            if(taxi)foreach(var side in new[]{1f,-1f})ChangwonSession.Builder.ChangwonSign("택시",go.transform,go.transform.position+Vector3.up*1.75f,f*side,.32f,new Color(1f,.85f,.2f));
            ChangwonSession.EnterCar(car);
        }

        // --- 택시
        void NextCustomer()
        {
            Vector3 at,face;
            if(!CustomerSpot(ChangwonSession.PlayerFeet,out at,out face)){End(false,"근처에 택시를 기다리는 손님이 없습니다.",0);return;}
            Transform[] legs,arms;customer=ChangwonSession.Builder.ChangwonPerson(at,rnd,out legs,out arms);
            if(customer==null){End(false,null,0);return;}
            customer.name="택시 손님";customer.transform.rotation=Quaternion.LookRotation(face);
            waveArm=arms!=null&&arms.Length>1?arms[1]:null;if(waveArm!=null)waveRest=waveArm.localRotation;
            string area=ChangwonAreas.Name(at.x,at.z);goal=at;stage=0;shownKey=int.MinValue;goalName="택시 손님"+(area.Length>0?" ("+area+")":"");
            Guide(new Vector2(at.x,at.z),goalName);ChangwonSession.Toast("택시 호출! "+(area.Length>0?area+"에서 ":"")+"손님이 기다립니다 · 손님 옆에 차를 세우세요 ("+(count+1)+"/"+Fares+")");
        }
        void RunTaxi()
        {
            var car=ChangwonSession.PlayerCar;
            if(stage==0){
                if(customer==null){NextCustomer();return;}
                if(waveArm!=null)waveArm.localRotation=Quaternion.AngleAxis(-150f+20f*Mathf.Sin(Time.time*7f),Vector3.right)*waveRest;
                if(Changed(car==null?-1:-2)){Objective(car==null?"택시: 차를 타고 손님에게 가세요 (노란 점)":"택시: 손님 옆에 차를 세우세요 (노란 점)");hud.Add("택시 손님 "+(count+1)+"/"+Fares+" · J 취소");}
                if(car==null||Mathf.Abs(car.speed)>1.5f||Flat(car.transform.position-goal)>8f*8f)return;
                Destroy(customer.gameObject);customer=null;waveArm=null;
                ChangwonData.Place dest;Vector3 at;
                if(!Destination(goal,out dest,out at)){End(false,"손님이 갈 곳을 정하지 못해 내렸습니다.",0);return;}
                goal=at;goalName=dest.name;stage=1;shownKey=int.MinValue;
                limit=Vector2.Distance(new Vector2(car.transform.position.x,car.transform.position.z),dest.pos)*1.3f/12f+60f;deadline=Time.time+limit;
                Guide(dest.pos,goalName);Sfx.Play("door-chime",.4f);
                ChangwonSession.Toast("손님: \""+goalName+"까지 가 주세요!\" · 제한 시간 "+Clock(limit));
                return;
            }
            float left=deadline-Time.time;
            if(left<=0){End(false,"시간이 초과되어 손님이 중간에 내렸습니다. 다음엔 더 빨리!",0);return;}
            if(Changed(Mathf.CeilToInt(left))){Objective("택시 목적지: "+goalName);hud.Add("택시 손님 "+(count+1)+"/"+Fares+" · 남은 시간 "+Clock(left)+" · J 취소");}
            if(car==null||Mathf.Abs(car.speed)>1.5f||Flat(car.transform.position-goal)>15f*15f)return;
            int pay=100+Mathf.RoundToInt(left*2f);count++;
            if(count>=Fares){End(true,Thanks[rnd.Next(Thanks.Length)]+" · 택시 운행 완료! 손님 "+Fares+"명 (+"+pay+"점)",pay);return;}
            ChangwonSession.Progress.score+=pay;Sfx.Play("coin");
            ChangwonSession.Toast(Thanks[rnd.Next(Thanks.Length)]+" (+"+pay+"점 · "+count+"/"+Fares+")");
            NextCustomer();
        }
        // A pavement spot by a town street 200-900 m away, near some place.
        bool CustomerSpot(Vector3 from,out Vector3 at,out Vector3 face)
        {
            at=face=Vector3.zero;candidates.Clear();var f2=new Vector2(from.x,from.z);
            foreach(var pl in ChangwonData.Places){float d=(pl.pos-f2).sqrMagnitude;if(d>200f*200f&&d<900f*900f)candidates.Add(pl);}
            for(int tries=0;tries<16&&candidates.Count>0;tries++){
                var pl=candidates[rnd.Next(candidates.Count)];var probe=new Vector3(pl.pos.x,ChangwonData.Height(pl.pos.x,pl.pos.y),pl.pos.y);
                ChangwonData.Road road;float along;Vector3 q;
                if(!ChangwonData.NearestRoad(probe,120f,out road,out along,out q)||road.cls<ChangwonData.Primary||road.cls>ChangwonData.Local)continue;
                if(Mathf.Abs(q.y-ChangwonData.Height(q.x,q.z))>3f)continue; // not on a bridge or in a tunnel
                Vector3 f;road.At(along,out f);f.y=0;f.Normalize();var right=new Vector3(f.z,0,-f.x);float side=Vector3.Dot(probe-q,right)>=0?1:-1;
                at=q+right*side*(road.width*.5f+1.4f);at.y=Mathf.Max(q.y+.22f,ChangwonData.Height(at.x,at.z));face=-right*side;
                return true;
            }
            return false;
        }
        // A named restaurant, cafe, mall, station, hospital or hotel 1-4 km away, and the road point in front of it.
        bool Destination(Vector3 from,out ChangwonData.Place place,out Vector3 at)
        {
            place=null;at=Vector3.zero;candidates.Clear();var f2=new Vector2(from.x,from.z);
            foreach(var pl in ChangwonData.Places){
                if(pl.name.Length==0||Array.IndexOf(TaxiKinds,pl.kind)<0)continue;
                float d=(pl.pos-f2).sqrMagnitude;if(d>1000f*1000f&&d<4000f*4000f)candidates.Add(pl);
            }
            for(int tries=0;tries<16&&candidates.Count>0;tries++){
                var pl=candidates[rnd.Next(candidates.Count)];var probe=new Vector3(pl.pos.x,ChangwonData.Height(pl.pos.x,pl.pos.y),pl.pos.y);
                ChangwonData.Road road;float along;Vector3 q;
                if(!ChangwonData.NearestRoad(probe,150f,out road,out along,out q)||road.cls>ChangwonData.Service)continue;
                place=pl;at=q;return true;
            }
            return false;
        }

        // --- 아구찜 배달
        bool PickApartment(Vector2 from)
        {
            for(int tries=0;tries<30;tries++){
                float a=(float)rnd.NextDouble()*Mathf.PI*2,d=1000f+(float)rnd.NextDouble()*2000f;var c=from+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*d;
                if(!ChangwonData.Inside(c.x,c.y))continue;
                var ch=ChangwonData.ChunkOf(c.x,c.y);ChangwonData.Building best=null;float bestD=float.MaxValue;
                foreach(var b in ChangwonData.BuildingsIn(ch.x,ch.y)){
                    if(b.kind!=1)continue;float e=(b.center-from).magnitude;if(e<1000f||e>3000f)continue;
                    float n=(b.center-c).sqrMagnitude-(b.name.Length>0?250f*250f:0);if(n<bestD){bestD=n;best=b;}
                }
                if(best==null)continue;
                goal=new Vector3(best.center.x,ChangwonData.Height(best.center.x,best.center.y),best.center.y);goalRing=best.ring;
                goalName=best.name.Length>0?best.name:ChangwonAreas.Name(best.center.x,best.center.y)+" 아파트";
                return true;
            }
            return false;
        }
        void RunDelivery(Vector3 p)
        {
            float left=deadline-Time.time;
            if(left<=0){End(false,"아구찜이 식어 버렸어요… 다음엔 더 빨리 배달해 주세요!",0);return;}
            if(Changed(Mathf.CeilToInt(left))){
                Objective("배달 목적지: "+goalName);
                hud.Add("아구찜 온도 "+Mathf.RoundToInt(Mathf.Lerp(45f,85f,left/limit))+"°C · 남은 시간 "+Clock(left)+" · J 취소");
            }
            var car=ChangwonSession.PlayerCar;if(car!=null&&Mathf.Abs(car.speed)>3f)return;
            var p2=new Vector2(p.x,p.z);
            if(goalRing==null||!Polygon.Contains(goalRing,p2)&&EdgeDistance(goalRing,p2)>20f)return;
            int pay=150+Mathf.RoundToInt(left*1.5f);
            End(true,"배달 완료! \"아구찜이 아직 뜨끈하네요. 잘 먹겠습니다!\" (+"+pay+"점)",pay);
        }

        // --- 레이스
        List<Vector3> Course(int which)
        {
            if(which==0){
                // Counter-clockwise (right-hand traffic) round the 창원광장 roundabout, three laps, finishing at the start.
                var c=new Vector2(5657f,2933f);var ring=new List<Vector3>();
                for(int k=0;k<8;k++){
                    float a=(-90f+k*45f)*Mathf.Deg2Rad;var q2=c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*110f;var probe=new Vector3(q2.x,ChangwonData.Height(q2.x,q2.y),q2.y);
                    ChangwonData.Road r;float al;Vector3 at;ring.Add(ChangwonData.NearestRoad(probe,45f,out r,out al,out at)?at:probe);
                }
                var laps=new List<Vector3>();for(int lap=0;lap<3;lap++)laps.AddRange(ring);laps.Add(ring[0]);return laps;
            }
            if(which==1)return Resample(Trace(new Vector2(5657f,2933f),r=>r.name=="창원대로",new Vector2(1f,-.6f),4200f),400f,4000f);
            if(which==2)return Resample(Trace(new Vector2(5600f,-1300f),r=>r.name.StartsWith("안민고개")||r.name=="안민로",new Vector2(0,-1f),6500f),400f,6500f);
            // Only the 284 m main span is named "Machang Bridge" in the data; the rest of the deck and its approach are 남해안대로.
            return Resample(Trace(new Vector2(-3700f,-4470f),r=>r.name=="Machang Bridge"||r.name=="마창대교"||r.name=="남해안대로",new Vector2(1f,.35f),4000f),350f,4000f);
        }
        // Follows roads matching 'match' from the one starting nearest 'near' heading along 'dir', through junctions
        // (straightest matching road; short unnamed connectors allowed), respecting one-way directions.
        static List<Vector3> Trace(Vector2 near,Func<ChangwonData.Road,bool> match,Vector2 dir,float length)
        {
            var path=new List<Vector3>();ChangwonData.Road road=null;bool fwd=true;float best=float.MaxValue;
            foreach(var r in ChangwonData.Roads){
                if(!r.Drivable||!match(r))continue;var a=r.pts[0];var b=r.pts[r.pts.Length-1];
                for(int o=0;o<2;o++){
                    bool f=o==0;if(!f&&r.OneWay)continue;var s=f?a:b;var e=f?b:a;
                    if((e.x-s.x)*dir.x+(e.z-s.z)*dir.y<=0)continue;
                    float d=(new Vector2(s.x,s.z)-near).sqrMagnitude;if(d<best){best=d;road=r;fwd=f;}
                }
            }
            var used=new HashSet<int>();float total=0,loose=0;
            while(road!=null&&total<length&&used.Add(road.index)){
                loose=match(road)?0:loose+road.length;int n=road.pts.Length;
                for(int i=0;i<n;i++){var q=road.pts[fwd?i:n-1-i];if(path.Count>0){float d=Flat(q-path[path.Count-1]);if(d<1e-4f)continue;total+=Mathf.Sqrt(d);}path.Add(q);}
                if(path.Count<2)break;
                int node=fwd?road.b:road.a;var h=path[path.Count-1]-path[path.Count-2];h.y=0;h.Normalize();
                var list=ChangwonData.NodeRoads[node];road=null;float score=.2f;
                if(list!=null)foreach(int ri in list){
                    var r=ChangwonData.Roads[ri];if(used.Contains(ri)||r.cls>ChangwonData.Service)continue;
                    bool ok=match(r);if(!ok&&loose+r.length>300f)continue;
                    bool f=r.a==node;if(!f&&r.OneWay)continue;int m=r.pts.Length;
                    var v=f?r.pts[1]-r.pts[0]:r.pts[m-2]-r.pts[m-1];v.y=0;float s=Vector3.Dot(v.normalized,h)+(ok?.5f:0);
                    if(s>score&&(ok||s>.75f)){score=s;road=r;fwd=f;}
                }
            }
            return path;
        }
        // A checkpoint every 'spacing' metres along the path (up to maxLength), always ending at its end.
        static List<Vector3> Resample(List<Vector3> path,float spacing,float maxLength)
        {
            var cps=new List<Vector3>();if(path.Count<2)return cps;cps.Add(path[0]);
            float run=0,total=0;int i=1;
            for(;i<path.Count;i++){float d=Mathf.Sqrt(Flat(path[i]-path[i-1]));total+=d;run+=d;if(total>=maxLength)break;if(run>=spacing){cps.Add(path[i]);run=0;}}
            var end=path[Mathf.Min(i,path.Count-1)];
            if(run<spacing*.4f&&cps.Count>1)cps[cps.Count-1]=end;else cps.Add(end);
            return cps;
        }
        void RunRace(Vector3 p)
        {
            float t=racing?Time.time-started:0;
            if(Changed(racing?(int)(t*10)+next*100000:-next-1)){
                Objective("레이스: "+RaceNames[race]+(racing?" · 노란 게이트 통과":" · 노란 출발 게이트로"));
                hud.Add(racing?"기록 "+Lap(t)+" · 체크포인트 "+next+"/"+(course.Count-1)+" · J 취소":"출발 대기 · 체크포인트 "+(course.Count-1)+"개 · J 취소");
            }
            var cp=course[next];
            if(placed!=next){
                placed=next;PlaceGate(gateA,next);gateB.SetActive(next+1<course.Count);if(next+1<course.Count)PlaceGate(gateB,next+1);
            }
            if(Flat(cp-p)>16f*16f||Mathf.Abs(cp.y-p.y)>15f)return;
            if(next==0){racing=true;started=Time.time;ChangwonSession.Toast("출발! "+RaceNames[race]);Sfx.Play("bell");}
            else Sfx.Play("tap",.8f,1.3f);
            next++;
            if(next<course.Count)return;
            t=Time.time-started;float best=Best(race);bool record=best<=0||t<best;
            if(record)SaveBest(race,t);
            int pay=300+(record?200:0);
            End(true,"레이스 완주! "+RaceNames[race]+" "+Lap(t)+(record?" · 신기록!":" · 최고 기록 "+Lap(best))+" (+"+pay+"점)",pay);
        }
        void PlaceGate(GameObject gate,int i)
        {
            var at=course[i];var d=i+1<course.Count?course[i+1]-at:at-course[Mathf.Max(0,i-1)];d.y=0;
            if(d.sqrMagnitude<.01f)d=Vector3.forward;
            gate.transform.SetPositionAndRotation(at-Vector3.up*.3f,Quaternion.LookRotation(d));gate.SetActive(true);
        }
        // Best times: Progress.bestRace for the first race, "race:<name>:<seconds>" entries in Progress.done for the others.
        static float Best(int which)
        {
            var progress=ChangwonSession.Progress;if(which==0)return progress.bestRace;
            string prefix="race:"+RaceNames[which]+":";float v;
            foreach(var d in progress.done)if(d.StartsWith(prefix,StringComparison.Ordinal)&&float.TryParse(d.Substring(prefix.Length),NumberStyles.Float,CultureInfo.InvariantCulture,out v))return v;
            return 0;
        }
        static void SaveBest(int which,float seconds)
        {
            var progress=ChangwonSession.Progress;if(which==0){progress.bestRace=seconds;return;}
            string prefix="race:"+RaceNames[which]+":";
            progress.done.RemoveAll(d=>d.StartsWith(prefix,StringComparison.Ordinal));
            progress.done.Add(prefix+seconds.ToString("F1",CultureInfo.InvariantCulture));
        }

        // --- 벚꽃 사진 투어
        void GuideBlossom()
        {
            var p=ChangwonSession.PlayerFeet;ChangwonLandmarks.Landmark best=null;float bestD=float.MaxValue;
            for(int i=0;i<BlossomIds.Length;i++){var lm=Landmark(BlossomIds[i]);if(lm==null||visited[i])continue;float d=Flat(new Vector3(lm.pos.x,0,lm.pos.y)-p);if(d<bestD){bestD=d;best=lm;}}
            if(best!=null){goalName=best.name;Guide(best.pos,goalName);}
        }
        void RunBlossom(Vector3 p)
        {
            float left=deadline-Time.time;
            if(left<=0){End(false,"시간 초과! 벚꽃 사진 투어를 다시 도전해 보세요.",0);return;}
            int got=0;foreach(var v in visited)if(v)got++;
            if(Changed(Mathf.CeilToInt(left)+got*10000)){Objective("벚꽃 사진 투어: 분홍 점 "+(BlossomIds.Length-got)+"곳 남음");hud.Add("벚꽃 사진 "+got+"/"+BlossomIds.Length+" · 남은 시간 "+Clock(left)+" · J 취소");}
            for(int i=0;i<BlossomIds.Length;i++){
                var lm=Landmark(BlossomIds[i]);
                if(lm==null){visited[i]=true;continue;}
                if(visited[i]||Flat(new Vector3(lm.pos.x,0,lm.pos.y)-p)>70f*70f)continue;
                visited[i]=true;got++;Sfx.Play("chime");
                if(got>=BlossomIds.Length){int pay=400+Mathf.RoundToInt(left);End(true,"찰칵! "+lm.name+" · 벚꽃 사진 투어 완료! (+"+pay+"점)",pay);return;}
                ChangwonSession.Toast("찰칵! "+lm.name+" 벚꽃 사진 ("+got+"/"+BlossomIds.Length+")");GuideBlossom();
            }
        }
    }
}
