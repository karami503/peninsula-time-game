using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PeninsulaTime
{
    // Wire format of OnlineServer/server.js. Field names must match the JSON keys.
    [Serializable] public class OnlineBattlePlayer { public int index,troops,maxTroops,gold,tiles,attackLevel,defenseLevel,economyLevel; public string name; public bool bot,alive; public int[] forts,barracks; }
    [Serializable] public class OnlineAttack { public int from,target,troops; }
    [Serializable] public class OnlineBattleState { public string id,phase,owners; public int tick,spawnSeconds,you,winner,width,height; public OnlineBattlePlayer[] players; public OnlineAttack[] attacks; public string[] log; }
    [Serializable] public class OnlineRoom { public string id,phase; public int humans,players,tick; }
    [Serializable] public class OnlineRoomList { public OnlineRoom[] rooms; }
    [Serializable] public class OnlineResult { public bool ok; public string error,id,token,room,username; public int index; }
    [Serializable] public class OnlineCredentials { public string username,password; }
    [Serializable] public class OnlineBattleAction { public string room,token,type,kind; public int tile; public float ratio; }
    [Serializable] public class HealingSummary { public string id,name,city; public int era,buildingCount,population,happiness; public long updated; }
    [Serializable] public class HealingList { public HealingSummary[] cities; }
    [Serializable] public class HealingRoad { public int axis,row,column; }
    [Serializable] public class HealingCity { public string id,name,city; public int era,population,happiness,budget; public long updated; public string[] buildings; public HealingRoad[] roads; }

    // Online modes: territory battle on the Korean peninsula, and the healing-mode city gallery
    // where other players watch a published city read-only (design view or 3D street view).
    public partial class GameController
    {
        const string OnlineUrlKey="onlineUrl",PlayerNameKey="onlineName",SessionKey="onlineSession",HealingIdKey="healingId",HealingAutoKey="healingAuto";
        const int TileScale=4;
        static readonly Color32[] Palette={
            new Color32(0,0,0,0),new Color32(226,84,72,255),new Color32(66,135,245,255),new Color32(245,190,50,255),new Color32(140,90,220,255),
            new Color32(40,180,150,255),new Color32(235,120,190,255),new Color32(120,200,70,255),new Color32(250,140,40,255),new Color32(90,200,230,255),
            new Color32(180,60,110,255),new Color32(150,150,60,255),new Color32(70,90,200,255),new Color32(200,110,80,255),new Color32(110,170,120,255),new Color32(230,230,230,255)};
        string onlineUrl="http://127.0.0.1:8787",onlineName="",onlineMode="",onlineStatus="";
        string onlineSession="",onlinePassword="",onlineAccount="";
        string battleRoom="",battleToken="",battleTool="attack";
        float battleRatio=.3f;
        int battleSession,spectateSession;
        OnlineBattleState battle;
        OnlineRoom[] battleRooms=new OnlineRoom[0];
        HealingSummary[] healingCities=new HealingSummary[0];
        HealingCity spectating;
        Texture2D battleTexture;
        GUIStyle mapLabelStyle;
        bool[] koreaLand;
        int gridW,gridH;
        float gridLon0,gridLat0,gridLon1,gridLat1;
        bool onlineLoaded;

        void LoadOnlinePrefs()
        {
            if(onlineLoaded)return;onlineLoaded=true;
            onlineUrl=PlayerPrefs.GetString(OnlineUrlKey,onlineUrl);
            onlineName=PlayerPrefs.GetString(PlayerNameKey,"");
            onlineSession=PlayerPrefs.GetString(SessionKey,"");
            if(onlineSession.Length>0)StartCoroutine(CheckSession());
        }
        bool LoggedIn(){return onlineAccount.Length>0&&onlineSession.Length>0;}
        IEnumerator CheckSession()
        {
            yield return OnlineRequest("GET","/auth/me",null,(code,text)=>
            {
                if(code==200)onlineAccount=JsonUtility.FromJson<OnlineResult>(text).username??"";
                else if(code==401)ForgetSession("로그인이 만료되었습니다. 다시 로그인하세요.");
            });
        }
        void ForgetSession(string reason)
        {
            onlineSession=onlineAccount="";PlayerPrefs.DeleteKey(SessionKey);PlayerPrefs.Save();
            if(reason!=null)onlineStatus=reason;
        }
        // Register or log in; only the session token is kept, never the password.
        void Authenticate(bool register)
        {
            SaveOnlineIdentity();onlineStatus=register?"가입 중…":"로그인 중…";
            var body=JsonUtility.ToJson(new OnlineCredentials{username=onlineName,password=onlinePassword});
            onlinePassword="";
            StartCoroutine(OnlineRequest("POST",register?"/auth/register":"/auth/login",body,(code,text)=>
            {
                if(code!=200){onlineStatus=ErrorText(code,text);return;}
                var result=JsonUtility.FromJson<OnlineResult>(text);
                onlineSession=result.token;onlineAccount=result.username;
                PlayerPrefs.SetString(SessionKey,onlineSession);PlayerPrefs.Save();
                onlineStatus="";Toast(onlineAccount+"님, 환영합니다.");
            }));
        }
        void Logout()
        {
            StartCoroutine(OnlineRequest("POST","/auth/logout","{}",(code,text)=>{}));
            if(onlineMode=="battle")LeaveBattle();
            ForgetSession(null);Toast("로그아웃했습니다.");
        }
        bool ValidOnlineUrl(){Uri uri;return Uri.TryCreate(onlineUrl,UriKind.Absolute,out uri)&&(uri.Scheme==Uri.UriSchemeHttp||uri.Scheme==Uri.UriSchemeHttps);}
        string ErrorText(long code,string text)
        {
            if(code==0)return "서버에 연결할 수 없습니다. 주소와 서버 실행 여부를 확인하세요.";
            try{var result=JsonUtility.FromJson<OnlineResult>(text);if(result!=null&&!string.IsNullOrEmpty(result.error))return result.error;}
            catch(Exception e){Debug.LogWarning("Online error body: "+e.Message);}
            return "서버 오류 ("+code+")";
        }
        IEnumerator OnlineRequest(string method,string path,string json,Action<long,string> done)
        {
            if(!ValidOnlineUrl()){done(0,null);yield break;}
            using(var request=new UnityWebRequest(onlineUrl.TrimEnd('/')+path,method))
            {
                request.downloadHandler=new DownloadHandlerBuffer();
                if(json!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));request.SetRequestHeader("Content-Type","application/json");}
                if(onlineSession.Length>0)request.SetRequestHeader("Authorization","Bearer "+onlineSession);
                request.timeout=8;
                yield return request.SendWebRequest();
                bool reached=request.result==UnityWebRequest.Result.Success||request.result==UnityWebRequest.Result.ProtocolError;
                done(reached?request.responseCode:0,reached?request.downloadHandler.text:null);
            }
        }
        void SaveOnlineIdentity(){onlineName=onlineName.Trim();PlayerPrefs.SetString(PlayerNameKey,onlineName);PlayerPrefs.SetString(OnlineUrlKey,onlineUrl);PlayerPrefs.Save();}

        // ---------- Battle ----------
        void LoadKoreaGrid()
        {
            if(koreaLand!=null)return;
            var asset=Resources.Load<TextAsset>("Geo/KoreaGrid");
            if(asset==null){Debug.LogError("Korea grid missing");return;}
            var lines=asset.text.Trim().Split('\n');var head=lines[0].Trim().Split(' ');
            gridW=int.Parse(head[0]);gridH=int.Parse(head[1]);
            gridLon0=float.Parse(head[2],CultureInfo.InvariantCulture);gridLat0=float.Parse(head[3],CultureInfo.InvariantCulture);
            gridLon1=float.Parse(head[4],CultureInfo.InvariantCulture);gridLat1=float.Parse(head[5],CultureInfo.InvariantCulture);
            koreaLand=new bool[gridW*gridH];
            for(int y=0;y<gridH;y++)for(int x=0;x<gridW;x++)koreaLand[y*gridW+x]=lines[1+y][x]=='1';
        }
        void JoinBattle()
        {
            SaveOnlineIdentity();onlineStatus="참가 중…";
            StartCoroutine(OnlineRequest("POST","/battle/join","{}",(code,text)=>
            {
                if(code==401){ForgetSession(ErrorText(code,text));return;}
                if(code!=200){onlineStatus=ErrorText(code,text);return;}
                var joined=JsonUtility.FromJson<OnlineResult>(text);
                OpenBattle(joined.room,joined.token);
                Toast(text.Contains("\"rejoined\":true")?"진행 중이던 대전에 다시 들어왔습니다.":"대전 참가: 20초 안에 지도를 눌러 시작 위치를 고르세요.");
            }));
        }
        void OpenBattle(string room,string token)
        {
            if(mode=="spectate")StopSpectating();
            battleRoom=room;battleToken=token??"";onlineMode="battle";battle=null;onlineStatus="";battleTool="attack";
            LoadKoreaGrid();StartCoroutine(BattleLoop(++battleSession));
        }
        void LeaveBattle(){onlineMode="";battle=null;battleSession++;battleRoom=battleToken="";}
        IEnumerator BattleLoop(int session)
        {
            while(session==battleSession&&onlineMode=="battle")
            {
                string path="/battle/state?room="+Uri.EscapeDataString(battleRoom)+(battleToken.Length>0?"&token="+Uri.EscapeDataString(battleToken):"");
                yield return OnlineRequest("GET",path,null,(code,text)=>
                {
                    if(session!=battleSession)return;
                    if(code==200){battle=JsonUtility.FromJson<OnlineBattleState>(text);PaintBattle();onlineStatus="";}
                    else if(code==404){LeaveBattle();onlineStatus="방이 닫혔습니다.";}
                    else onlineStatus=ErrorText(code,text);
                });
                yield return new WaitForSecondsRealtime(.5f);
            }
        }
        IEnumerator RefreshRooms()
        {
            SaveOnlineIdentity();
            yield return OnlineRequest("GET","/battle/rooms",null,(code,text)=>
            {
                if(code==200){battleRooms=JsonUtility.FromJson<OnlineRoomList>(text).rooms??new OnlineRoom[0];onlineStatus=battleRooms.Length==0?"진행 중인 대전이 없습니다.":"";}
                else onlineStatus=ErrorText(code,text);
            });
        }
        void SendBattle(string type,int tile,string kind="")
        {
            if(battleToken.Length==0)return;
            var action=new OnlineBattleAction{room=battleRoom,token=battleToken,type=type,tile=tile,kind=kind,ratio=battleRatio};
            StartCoroutine(OnlineRequest("POST","/battle/act",JsonUtility.ToJson(action),(code,text)=>{if(code!=200)Toast(ErrorText(code,text));}));
        }
        OnlineBattlePlayer BattlePlayer(int index){return battle!=null&&battle.players!=null&&index>0&&index<=battle.players.Length?battle.players[index-1]:null;}
        int OwnerAt(int tile){return battle!=null&&battle.owners!=null&&tile>=0&&tile<battle.owners.Length?battle.owners[tile]-'0':0;}
        void PaintBattle()
        {
            if(battle==null||koreaLand==null||battle.width!=gridW||battle.height!=gridH)return;
            int w=gridW*TileScale,h=gridH*TileScale;
            if(battleTexture==null)battleTexture=new Texture2D(w,h,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color32[w*h];
            var sea=new Color32(18,40,52,255);var empty=new Color32(74,104,82,255);
            for(int ty=0;ty<gridH;ty++)for(int tx=0;tx<gridW;tx++)
            {
                int tile=ty*gridW+tx,owner=OwnerAt(tile);
                Color32 fill=!koreaLand[tile]?sea:owner==0?empty:Palette[owner%Palette.Length];
                bool mine=owner!=0&&owner==battle.you;
                for(int py=0;py<TileScale;py++)for(int px=0;px<TileScale;px++)
                {
                    var c=fill;
                    if(koreaLand[tile])
                    {
                        // Darken (or for your own land, whiten) the pixels along a border with another owner.
                        int nx=tx+(px==0?-1:px==TileScale-1?1:0),ny=ty+(py==0?-1:py==TileScale-1?1:0);
                        bool edge=(nx!=tx||ny!=ty)&&nx>=0&&ny>=0&&nx<gridW&&ny<gridH&&koreaLand[ny*gridW+nx]&&OwnerAt(ny*gridW+nx)!=owner;
                        if(edge)c=mine?new Color32(255,255,255,255):new Color32((byte)(c.r*.45f),(byte)(c.g*.45f),(byte)(c.b*.45f),255);
                    }
                    pixels[((gridH-1-ty)*TileScale+(TileScale-1-py))*w+tx*TileScale+px]=c;
                }
            }
            battleTexture.SetPixels32(pixels);battleTexture.Apply();
        }
        Rect BattleRect(float w,float h)
        {
            float left=560f,top=130f,availableW=w-left-22f,availableH=h-top-70f;
            float aspect=(gridLon1-gridLon0)*Mathf.Cos(Mathf.Deg2Rad*(gridLat0+gridLat1)*.5f)/(gridLat1-gridLat0);
            float mapH=Mathf.Min(availableH,availableW/aspect),mapW=mapH*aspect;
            return new Rect(left+(availableW-mapW)*.5f,top+(availableH-mapH)*.5f,mapW,mapH);
        }
        Vector2 TileCentre(Rect map,int tile){return new Vector2(map.x+(tile%gridW+.5f)*map.width/gridW,map.y+(tile/gridW+.5f)*map.height/gridH);}
        void DrawBattleMap(float w,float h)
        {
            if(battleTexture==null)return;
            if(mapLabelStyle==null)mapLabelStyle=new GUIStyle(smallStyle){alignment=TextAnchor.MiddleCenter,normal={textColor=new Color(1,1,1,.8f)}};
            UiTexture(new Rect(548,92,w-548,h-146),inkTexture);
            var map=BattleRect(w,h);
            UiTexture(map,battleTexture,ScaleMode.StretchToFill);
            foreach(var city in GameContent.Cities)
            {
                if(!city.featured)continue;
                float x=map.x+(city.longitude-gridLon0)/(gridLon1-gridLon0)*map.width,y=map.y+(gridLat1-city.latitude)/(gridLat1-gridLat0)*map.height;
                UiLabel(new Rect(x-40,y-9,80,18),city.name,mapLabelStyle);
            }
            foreach(var p in battle.players)
            {
                if(!p.alive)continue;
                if(p.forts!=null)foreach(int f in p.forts){var c=TileCentre(map,f);UiLabel(new Rect(c.x-8,c.y-10,16,20),"▲",metricStyle);}
                if(p.barracks!=null)foreach(int b in p.barracks){var c=TileCentre(map,b);UiLabel(new Rect(c.x-8,c.y-10,16,20),"■",metricStyle);}
            }
            var winner=BattlePlayer(battle.winner);
            string phase=battle.phase=="spawn"?"배치 단계 · "+battle.spawnSeconds+"초 남음 · 시작 위치를 누르세요":battle.phase=="over"?"전쟁 종료 · 승자 "+(winner!=null?winner.name:"없음"):"전쟁 중";
            UiLabel(new Rect(map.x,map.y-24,map.width,22),(battle.you==0?"관전 중 · ":"")+phase,kickerStyle);
            var e=Event.current;
            if(!map.Contains(e.mousePosition))return;
            int tx=Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.x-map.x)/map.width*gridW),0,gridW-1);
            int ty=Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.y-map.y)/map.height*gridH),0,gridH-1);
            int tile=ty*gridW+tx;
            if(!koreaLand[tile])return;
            var owner=BattlePlayer(OwnerAt(tile));
            UiBox(new Rect(e.mousePosition.x+14,e.mousePosition.y+8,200,34),owner!=null?owner.name+" · 병력 "+owner.troops.ToString("N0"):"빈 땅",cardStyle);
            if(e.type!=EventType.MouseUp||e.button!=0||battle.you==0)return;
            if(battle.phase=="spawn")SendBattle("spawn",tile);
            else if(battleTool=="attack")SendBattle("attack",tile);
            else SendBattle("build",tile,battleTool);
            e.Use();
        }
        // Mirrors techCost/buildingCost in OnlineServer/battle.js (the server decides; this is for display).
        static int TechCost(int level){return 150*(1<<level);}
        static int BuildingCost(string kind,int count){return kind=="fort"?250+150*count:300+200*count;}
        void DrawBattlePanel()
        {
            Label(battle==null?"대전 불러오는 중…":"방 "+battle.id+(battle.you==0?" · 관전":""),headingStyle);
            if(battle==null){if(Button("나가기"))LeaveBattle();return;}
            var me=BattlePlayer(battle.you);
            if(me!=null)
            {
                BeginVerticalDump(cardStyle);
                Label(me.name+(me.alive?"":" · 멸망"),headingStyle);
                Label("영토 "+me.tiles+"칸 · 병력 "+me.troops.ToString("N0")+" / "+me.maxTroops.ToString("N0")+" · 금 "+me.gold.ToString("N0"));
                if(me.alive&&battle.phase!="over")
                {
                    Label("출병 비율 "+Mathf.RoundToInt(battleRatio*100)+"% (보유 병력 중 보낼 몫)",smallStyle);
                    battleRatio=GUILayout.HorizontalSlider(battleRatio,.05f,1f);GUILayout.Space(8);
                    int forts=me.forts!=null?me.forts.Length:0,barracks=me.barracks!=null?me.barracks.Length:0;
                    GUILayout.BeginHorizontal();
                    if(Button("공격",battleTool=="attack"))battleTool="attack";
                    if(Button("요새 "+BuildingCost("fort",forts),battleTool=="fort"))battleTool="fort";
                    if(Button("병영 "+BuildingCost("barracks",barracks),battleTool=="barracks"))battleTool="barracks";
                    GUILayout.EndHorizontal();
                    Label(battleTool=="attack"?"지도에서 맞닿은 빈 땅이나 적 영토를 누르면 출병합니다.":battleTool=="fort"?"요새: 반경 5칸 방어력 ×1.6. 내 영토를 누르세요.":"병영: 병력 증가 +25%, 최대 병력 +1500. 내 영토를 누르세요.",smallStyle);
                    Label("군사력 개발",headingStyle);
                    if(Button("공격 기술 "+me.attackLevel+"단계 → 금 "+TechCost(me.attackLevel)))SendBattle("tech",0,"attack");
                    if(Button("방어 기술 "+me.defenseLevel+"단계 → 금 "+TechCost(me.defenseLevel)))SendBattle("tech",0,"defense");
                    if(Button("경제 기술 "+me.economyLevel+"단계 → 금 "+TechCost(me.economyLevel)))SendBattle("tech",0,"economy");
                }
                EndVerticalDump();
            }
            GUILayout.Space(8);Label("세력",headingStyle);
            var ranked=new List<OnlineBattlePlayer>(battle.players);ranked.Sort((a,b)=>b.tiles.CompareTo(a.tiles));
            foreach(var p in ranked)
            {
                GUILayout.BeginHorizontal();
                var swatch=GUILayoutUtility.GetRect(14,14,GUILayout.Width(14));swatch.y+=4;
                GUI.color=Palette[p.index%Palette.Length];UiTexture(swatch,Texture2D.whiteTexture);GUI.color=Color.white;
                Label((p.index==battle.you?"★ ":"")+p.name+(p.bot?" (AI)":"")+(p.alive?"":" · 멸망")+"  "+p.tiles+"칸 · 병력 "+p.troops.ToString("N0"),smallStyle);
                GUILayout.EndHorizontal();
            }
            if(battle.log!=null&&battle.log.Length>0){GUILayout.Space(6);foreach(var line in battle.log)Label("· "+line,smallStyle);}
            GUILayout.Space(8);if(Button(battle.you==0?"관전 그만하기":"대전에서 나가기"))LeaveBattle();
        }

        // ---------- Healing ----------
        HealingCity MyCitySnapshot()
        {
            var city=GameContent.City(state.selectedCity);var economy=Economy(city.id);
            var roads=new List<HealingRoad>();
            foreach(var r in state.roads)if(r.city==city.id)roads.Add(new HealingRoad{axis=r.axis,row=r.row,column=r.column});
            var buildings=new List<string>();
            foreach(var b in state.buildings)if(b.city==city.id)buildings.Add(b.id);
            return new HealingCity{city=city.id,era=state.era,population=economy.population,happiness=economy.happiness,
                budget=economy.budget,buildings=buildings.ToArray(),roads=roads.ToArray()};
        }
        void PublishCity(bool quiet)
        {
            StartCoroutine(OnlineRequest("POST","/healing/publish",JsonUtility.ToJson(MyCitySnapshot()),(code,text)=>
            {
                if(code==401){ForgetSession(ErrorText(code,text));return;}
                if(code!=200){if(!quiet)Toast(ErrorText(code,text));return;}
                var result=JsonUtility.FromJson<OnlineResult>(text);
                PlayerPrefs.SetString(HealingIdKey,result.id);PlayerPrefs.Save();
                if(!quiet)Toast(GameContent.City(state.selectedCity).name+" 공개 완료 · 다른 사람이 구경할 수 있습니다.");
            }));
        }
        void AutoPublishCity(){if(PlayerPrefs.GetInt(HealingAutoKey,0)==1&&PlayerPrefs.HasKey(HealingIdKey)){LoadOnlinePrefs();if(onlineSession.Length>0)PublishCity(true);}}
        IEnumerator RefreshHealing()
        {
            SaveOnlineIdentity();
            yield return OnlineRequest("GET","/healing/list",null,(code,text)=>
            {
                if(code==200){healingCities=JsonUtility.FromJson<HealingList>(text).cities??new HealingSummary[0];onlineStatus=healingCities.Length==0?"아직 공개된 도시가 없습니다.":"";}
                else onlineStatus=ErrorText(code,text);
            });
        }
        void Spectate(string id){spectating=null;StartCoroutine(SpectateLoop(id,++spectateSession));}
        void StopSpectating(){spectateSession++;spectating=null;cityStreet=false;mode="map";world.BuildMap(state);}
        IEnumerator SpectateLoop(string id,int session)
        {
            while(session==spectateSession)
            {
                yield return OnlineRequest("GET","/healing/city?id="+Uri.EscapeDataString(id),null,(code,text)=>
                {
                    if(session!=spectateSession)return;
                    if(spectating!=null&&mode!="spectate"){spectateSession++;return;}
                    if(code!=200){onlineStatus=ErrorText(code,text);return;}
                    var city=JsonUtility.FromJson<HealingCity>(text);
                    if(spectating!=null&&spectating.updated==city.updated)return;
                    bool first=spectating==null;spectating=city;ShowSpectated(first);
                });
                yield return new WaitForSecondsRealtime(15f);
            }
        }
        // Builds the watched city from a throwaway GameState; the player's own save is never touched.
        void ShowSpectated(bool first)
        {
            if(onlineMode=="battle")LeaveBattle();
            var place=GameContent.City(spectating.city);
            var view=new GameState{era=GameContent.ModernEra,selectedCity=place.id};
            if(spectating.buildings!=null)foreach(var id in spectating.buildings)view.buildings.Add(new PlacedBuilding{id=id,city=place.id});
            if(spectating.roads!=null)foreach(var r in spectating.roads)view.roads.Add(new RoadSegment{city=place.id,axis=r.axis,row=r.row,column=r.column});
            var position=viewCamera.transform.position;var rotation=viewCamera.transform.rotation;
            mode="spectate";riding=false;world.BuildCityPlot(view);
            if(cityStreet&&!first){world.SetCityView(true);viewCamera.transform.SetPositionAndRotation(position,rotation);eye.SetPositionAndRotation(position,rotation);}
            else cityStreet=false;
            if(first)Toast((string.IsNullOrEmpty(spectating.name)?"이름 없는 시장":spectating.name)+"의 "+place.name+" 구경 중 · V: 3D 거리 보기");
        }
        void DrawHealingPanel()
        {
            BeginVerticalDump(cardStyle);Label("힐링 온라인",headingStyle);
            Label("지금처럼 혼자 건설하고 지켜보며, 내 도시를 공개해 다른 사람이 구경할 수 있게 합니다. 구경하는 사람은 수정할 수 없습니다.",smallStyle);
            string here=GameContent.City(state.selectedCity).name;
            GUI.enabled=LoggedIn()&&!maintenance;
            if(Button((PlayerPrefs.HasKey(HealingIdKey)?"내 도시 다시 공개 (":"내 도시 공개 (")+here+")",true))PublishCity(false);
            GUI.enabled=!maintenance;
            if(!LoggedIn())Label("공개는 로그인 후 할 수 있습니다. 구경은 로그인 없이 됩니다.",smallStyle);
            if(PlayerPrefs.HasKey(HealingIdKey))
            {
                bool auto=PlayerPrefs.GetInt(HealingAutoKey,0)==1;
                if(Button(auto?"시기마다 자동 갱신: 켜짐":"시기마다 자동 갱신: 꺼짐",auto)){PlayerPrefs.SetInt(HealingAutoKey,auto?0:1);PlayerPrefs.Save();}
            }
            if(Button("공개 도시 목록 새로고침"))StartCoroutine(RefreshHealing());
            foreach(var c in healingCities)
            {
                var place=GameContent.City(c.city);
                if(Button("구경: "+(string.IsNullOrEmpty(c.name)?"이름 없는 시장":c.name)+"의 "+place.name+" (건물 "+c.buildingCount+")"))Spectate(c.id);
            }
            EndVerticalDump();
        }
        void DrawSpectatePanel()
        {
            var place=GameContent.City(spectating.city);
            BeginVerticalDump(cardStyle);
            Label("구경 중 · 읽기 전용",kickerStyle);Label((string.IsNullOrEmpty(spectating.name)?"이름 없는 시장":spectating.name)+"의 "+place.name,headingStyle);
            Label(GameContent.Modern.name+" · 건물 "+(spectating.buildings!=null?spectating.buildings.Length:0)+" · 도로 "+(spectating.roads!=null?spectating.roads.Length:0)+"구간",smallStyle);
            Label("인구 "+spectating.population+" · 행복도 "+spectating.happiness+"/100",smallStyle);
            if(Button(cityStreet?"위에서 보기 (V)":"3D 거리 보기 (V)",true))ToggleCityStreet();
            Label("15초마다 주인의 변경 사항을 불러옵니다. 3D 거리 보기: WASD 이동 · ←/→ 회전 · Q/E·휠 높이.",smallStyle);
            if(Button("구경 그만하기"))StopSpectating();
            EndVerticalDump();
        }

        void DrawOnlinePanel()
        {
            LoadOnlinePrefs();
            Label("07  /  ONLINE",kickerStyle);Label("온라인",titleStyle);
            if(!string.IsNullOrEmpty(onlineStatus))Label(onlineStatus,smallStyle);
            if(onlineMode=="battle"){DrawBattlePanel();return;}
            if(mode=="spectate"&&spectating!=null){DrawSpectatePanel();return;}
            BeginVerticalDump(cardStyle);
            Label("서버 주소",smallStyle);onlineUrl=GUILayout.TextField(onlineUrl,120,textFieldStyle);
            if(!ValidOnlineUrl())Label("http:// 또는 https:// 로 시작하는 주소를 입력하세요.",smallStyle);
            if(LoggedIn())
            {
                Label(onlineAccount+"님 로그인됨",headingStyle);
                if(Button("로그아웃"))Logout();
            }
            else
            {
                Label("아이디 (한글·영문·숫자 2~16자)",smallStyle);onlineName=GUILayout.TextField(onlineName,16,textFieldStyle);
                Label("비밀번호 (8자 이상)",smallStyle);onlinePassword=GUILayout.PasswordField(onlinePassword,'●',72,textFieldStyle);
                GUILayout.BeginHorizontal();
                if(Button("로그인",true))Authenticate(false);
                if(Button("회원가입"))Authenticate(true);
                GUILayout.EndHorizontal();
                if(onlineUrl.StartsWith("http://")&&!onlineUrl.Contains("127.0.0.1")&&!onlineUrl.Contains("localhost"))Label("주의: http 주소는 비밀번호가 암호화되지 않고 전송됩니다. 인터넷 서버는 https를 쓰세요.",smallStyle);
            }
            EndVerticalDump();GUILayout.Space(8);
            BeginVerticalDump(cardStyle);Label("온라인 대전 · 한반도 땅따먹기",headingStyle);
            Label("시작 위치를 고르고 병력 일부를 보내 빈 땅과 이웃 세력을 점령합니다. 금으로 공격·방어·경제 기술과 요새·병영을 개발합니다. 땅의 80%를 차지하거나 마지막까지 남으면 승리.",smallStyle);
            GUI.enabled=LoggedIn()&&!maintenance;
            if(Button("대전 참가",true))JoinBattle();
            GUI.enabled=!maintenance;
            if(!LoggedIn())Label("대전 참가는 로그인 후 할 수 있습니다. 관전은 로그인 없이 됩니다.",smallStyle);
            if(Button("진행 중인 대전 보기 (관전)"))StartCoroutine(RefreshRooms());
            foreach(var room in battleRooms)
                if(Button("관전 · 방 "+room.id+" · "+(room.phase=="spawn"?"배치 중":room.phase=="play"?"전쟁 중":"종료")+" · 사람 "+room.humans+"명"))OpenBattle(room.id,"");
            EndVerticalDump();GUILayout.Space(8);
            DrawHealingPanel();
        }
    }
}
