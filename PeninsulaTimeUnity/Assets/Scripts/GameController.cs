using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace PeninsulaTime
{
    [Serializable] public class PlacedBuilding { public string id,city; }
    [Serializable] public class TransitRoute { public string name,type,from,to,fromStop,toStop,via; public int vehicles=1,fare=TransitEconomy.BaseFare,demand,riders,profit; }
    [Serializable] public class CityEconomy { public string city; public int population=12,happiness=65,taxRate=10,budget=300; }
    [Serializable] public class RoadSegment { public string city; public int axis,row,column; }
    [Serializable] public class ServerConfig { public string statusUrl,onlineUrl; }
    [Serializable] public class RemoteStatus { public bool maintenance; public string message,version,downloadUrl; }
    [Serializable] public class GameState
    {
        public int era=9,turn=1,district=0,selectedCountry=0;
        public string selectedCity="seoul";
        public int[] resources=GameContent.R(wood:250,stone:250,food:300,metal:250,goods:250,knowledge:100,energy:100);
        public List<PlacedBuilding> buildings=new List<PlacedBuilding>();
        public List<TransitRoute> routes=new List<TransitRoute>();
        public List<CityEconomy> economies=new List<CityEconomy>();
        public List<RoadSegment> roads=new List<RoadSegment>();
        public List<string> log=new List<string>{"대한민국의 도시를 설계하세요."};
        public int[] relations={50,50,50,50,50,50,50,50};
        [HideInInspector] public int wallet; // Legacy save compatibility only; never used for purchases.
        public List<string> purchases=new List<string>();
        [HideInInspector] public int transitCard; // Legacy save compatibility only; boarding has no balance.
        public NetworkEdits network=new NetworkEdits(); // stations and lines the player added, changed or deleted
    }
    public partial class GameController : MonoBehaviour
    {
        public GameState state;
        public WorldBuilder world;
        public Camera viewCamera;
        string tab="지도",mode="map",notice="";
        string routeName="",fromStop="",toStop="",via="",routeType="버스";
        TransitRoute currentRide;
        string citySearch="";
        string countrySearch="";
        string buildCategory="주거";
        int routeFrom=0,routeTo=1,selectedBuilding=0;
        Vector2 scroll;
        bool maintenance=false,showIntro=true,riding=false;
        bool returnToCityAfterBuild=false;
        bool roadTool=false,cityStreet=false;
        bool updateAvailable=false;
        string updateUrl="",maintenanceMessage="서버 점검 중입니다. 잠시 후 다시 접속하세요.";
        string gdpText="온라인 조회 전";
        float rideProgress=0,noticeUntil=0;
        float touchMove=0,touchStrafe=0,touchYaw=0;
        GUIStyle titleStyle,headingStyle,bodyStyle,smallStyle,buttonStyle,accentButtonStyle,boxStyle,cardStyle,textFieldStyle,kickerStyle,metricStyle,navStyle,navSelectedStyle;
        Texture2D panelTexture,cardTexture,goldTexture,buttonTexture,inkTexture,softTexture,lineTexture,chapterArt;
        Texture2D worldAtlas;
        byte[] worldCountryMask;
        int worldRegion=0;
        int atlasZoom=0;
        Font koreanFont;
        readonly string[] tabs={"지도","생산·건설","교통","역사","세계·외교","서울 3D","온라인"};
        readonly string[] nations={"대한민국","조선민주주의인민공화국","중국","일본","미국","러시아","독일","인도"};
        readonly string[] systems={"민주공화국","일당 지배 체제","중국공산당 일당 지배","입헌군주제·의회 민주주의","연방 대통령제 공화국","연방 공화국","연방 의원내각제 공화국","연방 의원내각제 공화국"};
        readonly string[] industries={"제조업·기술","광물 자원","석탄·희토류","제조업·기술","에너지·농업·기술","석유·천연가스","제조업·기술","농업·광물·서비스"};
        static string SaveDirectory {
            get {var args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++)if(args[i]=="--save-directory")return Path.GetFullPath(args[i+1]);return Application.persistentDataPath;}
        }
        string SavePath { get { return Path.Combine(SaveDirectory,"save-v1.json"); } }
        void Awake()
        {
            PerformanceRuntime.Apply();
            if(Application.isMobilePlatform)Screen.orientation=ScreenOrientation.LandscapeLeft;
            var existing=GetComponent<WorldBuilder>();world=existing!=null?existing:gameObject.AddComponent<WorldBuilder>();
            viewCamera=Camera.main;if(viewCamera==null){var o=new GameObject("Main Camera");o.tag="MainCamera";viewCamera=o.AddComponent<Camera>();o.AddComponent<AudioListener>();}
            world.worldCamera=viewCamera;Load();
            if(state.relations==null||state.relations.Length<WorldCatalog.Countries.Length)
            {
                var old=state.relations;state.relations=new int[WorldCatalog.Countries.Length];
                for(int i=0;i<state.relations.Length;i++)state.relations[i]=old!=null&&i<old.Length?old[i]:50;
            }
            state.selectedCountry=Mathf.Clamp(state.selectedCountry,0,WorldCatalog.Countries.Length-1);
            if(state.roads==null)state.roads=new List<RoadSegment>();
            foreach(var route in state.routes){route.vehicles=Mathf.Clamp(route.vehicles,1,TransitEconomy.MaxVehicles);if(route.fare<=0)route.fare=TransitEconomy.BaseFare;}
            if(state.network==null)state.network=new NetworkEdits();
            if(state.purchases==null)state.purchases=new List<string>();
            TransitNetwork.Build(state.network);
            showIntro=state.era==0&&state.turn==1;world.BuildMap(state);CreatePlayer();
        }
        void Start(){StartCoroutine(CheckServerLoop());StartPlaytest();}
        IEnumerator CheckServerLoop()
        {
            string path=Path.Combine(Application.streamingAssetsPath,"server.json");
            ServerConfig config=null;
            if(path.Contains("://"))
            {
                using(var localRequest=UnityWebRequest.Get(path))
                {
                    yield return localRequest.SendWebRequest();
                    if(localRequest.result==UnityWebRequest.Result.Success)
                    {
                        try{config=JsonUtility.FromJson<ServerConfig>(localRequest.downloadHandler.text);}
                        catch(Exception e){Debug.LogWarning("Server config: "+e.Message);}
                    }
                }
            }
            else
            {
                try{if(File.Exists(path))config=JsonUtility.FromJson<ServerConfig>(File.ReadAllText(path));}
                catch(Exception e){Debug.LogWarning("Server config: "+e.Message);}
            }
            if(config!=null&&!string.IsNullOrWhiteSpace(config.onlineUrl)&&!PlayerPrefs.HasKey(OnlineUrlKey))onlineUrl=config.onlineUrl;
            if(config==null||string.IsNullOrWhiteSpace(config.statusUrl))yield break;
            while(true)
            {
                using(var request=UnityWebRequest.Get(config.statusUrl))
                {
                    request.timeout=8;yield return request.SendWebRequest();
                    if(request.result==UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            var result=JsonUtility.FromJson<RemoteStatus>(request.downloadHandler.text);
                            maintenance=result.maintenance;
                            maintenanceMessage=string.IsNullOrWhiteSpace(result.message)?"서버 점검 중입니다. 잠시 후 다시 접속하세요.":result.message;
                            Version remoteVersion,localVersion;
                            Uri downloadUri;
                            updateAvailable=Version.TryParse(result.version,out remoteVersion)&&Version.TryParse(Application.version,out localVersion)
                                &&remoteVersion.CompareTo(localVersion)>0&&Uri.TryCreate(result.downloadUrl,UriKind.Absolute,out downloadUri)
                                &&downloadUri.Scheme==Uri.UriSchemeHttps;
                            updateUrl=result.downloadUrl;
                        }
                        catch(Exception e){Debug.LogWarning("Remote status: "+e.Message);}
                    }
                }
                yield return new WaitForSecondsRealtime(60);
            }
        }
        void Load()
        {
            try{if(File.Exists(SavePath)){var loaded=JsonUtility.FromJson<GameState>(File.ReadAllText(SavePath));if(loaded!=null&&loaded.era>=0&&loaded.era<GameContent.Eras.Length&&loaded.resources!=null&&loaded.resources.Length==7){state=loaded;return;}}}catch(Exception e){Debug.LogWarning("Save load failed: "+e.Message);}
            state=new GameState();
        }
        // Keep purchases and the last place when the app is closed or sent to the background.
        void OnApplicationQuit(){if(state!=null)Save();}
        void OnApplicationPause(bool paused){if(paused&&state!=null)Save();}
        void Save(){if(InOpenWorld)SaveChangwonProgress();try{Directory.CreateDirectory(SaveDirectory);File.WriteAllText(SavePath,JsonUtility.ToJson(state,true));}catch(Exception e){Toast("저장 실패: "+e.Message);}}
        void Toast(string message){notice=message;noticeUntil=Time.time+3.5f;}
        void Log(string message){state.log.Insert(0,message);if(state.log.Count>7)state.log.RemoveAt(state.log.Count-1);Save();Toast(message);}
        CityEconomy Economy(string city)
        {
            if(state.economies==null)state.economies=new List<CityEconomy>();
            var result=state.economies.Find(e=>e.city==city);
            if(result==null){result=new CityEconomy{city=city};state.economies.Add(result);}
            return result;
        }
        int MoneyCost(BuildingInfo info){int total=0;foreach(int resource in info.cost)total+=resource;return Mathf.Max(4,total/5);}
        void UpdateEconomy(CityEconomy economy)
        {
            var owned=state.buildings.FindAll(b=>b.city==economy.city);
            int housing=0,services=0,power=0;
            foreach(var placed in owned)
            {
                var info=GameContent.Building(placed.id);if(info==null)continue;
                if(info.category=="주거")housing+=placed.id=="apartment"?70:placed.id=="house"?12:6;
                if(placed.id=="market"||placed.id=="school"||placed.id=="farm")services+=5;
                if(info.category=="에너지")power+=15;
            }
            int capacity=Mathf.Max(12,housing);
            int roads=state.roads.FindAll(r=>r.city==economy.city).Count;
            int crowding=Mathf.Max(0,economy.population-capacity)*2;
            int target=Mathf.Clamp(70-economy.taxRate*2+services+Mathf.Min(power,10)+Mathf.Min(roads,15)-crowding,0,100);
            economy.happiness=Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(economy.happiness,target,.25f)),0,100);
            int change=economy.happiness>=60?Mathf.Max(1,economy.population/20):economy.happiness<35?-Mathf.Max(1,economy.population/15):0;
            economy.population=Mathf.Clamp(economy.population+change,0,capacity);
            int taxRevenue=Mathf.RoundToInt(economy.population*economy.taxRate*.09f);
            int upkeep=owned.Count*2+roads;
            economy.budget+=taxRevenue-upkeep;
        }
        void UpdateRoute(TransitRoute route)
        {
            var from=Economy(route.from);var to=Economy(route.to);
            route.demand=TransitEconomy.Demand(route.type,from.population,to.population,from.happiness,to.happiness,route.fare);
            route.riders=TransitEconomy.Riders(route.type,route.vehicles,route.demand);
            route.profit=TransitEconomy.Profit(route.type,route.vehicles,route.riders,route.fare);
            from.budget+=route.profit;
        }
        void BuyVehicle(TransitRoute route)
        {
            var owner=Economy(route.from);int price=TransitEconomy.VehiclePurchase(route.type);
            if(route.vehicles>=TransitEconomy.MaxVehicles){Toast("이 노선의 차량이 최대입니다.");return;}
            if(owner.budget<price){Toast("차량 구입에는 "+GameContent.City(route.from).name+" 예산 "+price+"이 필요합니다.");return;}
            owner.budget-=price;route.vehicles++;Log(route.name+" 차량 추가 ("+route.vehicles+"대)");
        }
        bool CanPay(int[] cost){for(int i=0;i<7;i++)if(state.resources[i]<cost[i])return false;return true;}
        void Pay(int[] cost){for(int i=0;i<7;i++)state.resources[i]-=cost[i];}
        void Tick()
        {
            state.turn++;
            foreach(var placed in state.buildings){var info=GameContent.Building(placed.id);if(info==null)continue;for(int i=0;i<7;i++)state.resources[i]+=info.output[i];}
            if(state.economies!=null)foreach(var economy in state.economies)UpdateEconomy(economy);
            foreach(var route in state.routes)UpdateRoute(route);
            Save();
            AutoPublishCity();
        }
        void Gather(int resource,int amount)
        {
            if(maintenance)return;state.resources[resource]+=amount;Tick();Log(GameContent.ResourceNames[resource]+" "+amount+"개 채집");
        }
        void Build(BuildingInfo info)
        {
            if(maintenance||info.unlock>state.era||!CanPay(info.cost))return;
            var city=GameContent.City(state.selectedCity);
            int used=state.buildings.FindAll(b=>b.city==city.id).Count;
            if(used>=CityCapacity(city)){Toast(city.name+"의 건설 부지가 가득 찼습니다.");return;}
            var economy=Economy(city.id);
            if(economy.budget<MoneyCost(info)){Toast("도시 예산이 부족합니다.");return;}
            Pay(info.cost);economy.budget-=MoneyCost(info);
            state.buildings.Add(new PlacedBuilding{id=info.id,city=state.selectedCity});Log(GameContent.City(state.selectedCity).name+"에 "+info.name+" 건설");
            if(returnToCityAfterBuild){EnterCityPlot();returnToCityAfterBuild=false;}
            else{world.BuildMap(state);mode="map";}
        }
        int CityCapacity(CityInfo city){return city.kind=="town"?12:city.region=="북한"?18:24;}
        void ToggleRoad(RoadMarker marker)
        {
            if(marker==null||maintenance)return;
            var existing=state.roads.Find(r=>r.city==state.selectedCity&&r.axis==marker.axis&&r.row==marker.row&&r.column==marker.column);
            if(existing!=null){state.roads.Remove(existing);Log("도로 구간 철거");}
            else
            {
                var economy=Economy(state.selectedCity);
                if(state.resources[1]<3||economy.budget<5){Toast("도로 건설에는 석재 3과 도시 예산 5가 필요합니다.");return;}
                state.resources[1]-=3;economy.budget-=5;
                state.roads.Add(new RoadSegment{city=state.selectedCity,axis=marker.axis,row=marker.row,column=marker.column});
                Log("도로 구간 건설");
            }
            world.BuildCityPlot(state);
            world.SetRoadCandidates(roadTool);
        }
        void Advance()
        {
            if(maintenance||state.era>=GameContent.Eras.Length-1)return;
            if(state.era==0&&!state.buildings.Exists(b=>b.id=="camp")){Toast("먼저 정착지를 건설하세요.");return;}
            var current=GameContent.Eras[state.era];if(!CanPay(current.cost)){Toast("다음 시대에 필요한 자원이 부족합니다.");return;}
            Pay(current.cost);state.era++;Tick();Log(GameContent.Eras[state.era].name+" 시대가 열렸습니다.");mode="map";world.BuildMap(state);showIntro=true;
        }
        void AddRoute()
        {
            if(maintenance||state.era<9){Toast("현대 시대에 개통할 수 있습니다.");return;}
            if(string.IsNullOrWhiteSpace(routeName)||string.IsNullOrWhiteSpace(fromStop)||string.IsNullOrWhiteSpace(toStop)||
                (routeFrom==routeTo&&fromStop.Trim()==toStop.Trim())){Toast("노선 이름과 서로 다른 역·정류장을 입력하세요.");return;}
            var cost=RouteCost(routeType);
            if(!CanPay(cost)){Toast("노선 건설 자원이 부족합니다.");return;}
            Pay(cost);state.routes.Add(new TransitRoute{name=routeName.Trim(),type=RouteId(routeType),from=GameContent.Cities[routeFrom].id,to=GameContent.Cities[routeTo].id,fromStop=fromStop.Trim(),toStop=toStop.Trim(),via=via.Trim()});
            Log(routeName.Trim()+" 개통");routeName=fromStop=toStop=via="";world.BuildMap(state);mode="map";
        }
        static string RouteId(string label){return label=="BRT"?"brt":label=="지하철"?"metro":label=="KTX"?"ktx":"bus";}
        static string RouteLabel(string id){return id=="brt"?"BRT":id=="metro"?"지하철":id=="ktx"?"KTX":"버스";}
        static int[] RouteCost(string label){return label=="KTX"?GameContent.R(stone:100,metal:100,goods:80,energy:20):label=="지하철"?GameContent.R(stone:55,metal:45,goods:45,energy:12):label=="BRT"?GameContent.R(stone:20,metal:12,goods:22):GameContent.R(wood:12,goods:15);}
        void EnterDistrict(int index,bool save=true)
        {
            state.district=index;if(save)Save();ClearRides();mode="district";riding=false;undergroundWalk=false;world.BuildDistrict(index,state.era);world.SetDistrictView(index,false);eye.SetPositionAndRotation(viewCamera.transform.position,viewCamera.transform.rotation);world.dayNight=true;tab="서울 3D";
            Toast((state.era<9?"시대별 지역 시범: ":GameContent.DistrictNames[index]+": ")+"WASD 이동 · 마우스 시선 · B 정류장 · G 지하철 · P 주차장");
        }
        void ReturnMap(bool selectMap=true){if(InOpenWorld)LeaveOpenWorldState();ClearRides();ridingCar=null;flight=null;fade=0;undergroundWalk=false;mode="map";riding=false;cityStreet=false;spectating=null;spectateSession++;world.BuildMap(state);world.dayNight=false;if(selectMap)tab="지도";}
        void EnterCityPlot(){ClearRides();mode="city";riding=false;cityStreet=false;tab="지도";world.BuildCityPlot(state);world.SetRoadCandidates(roadTool);Toast(GameContent.City(state.selectedCity).name+" 도시 설계 화면");}
        void ToggleCityStreet()
        {
            ridingCar=null;ClearRides();cityStreet=!cityStreet;roadTool=false;world.SetCityView(cityStreet);if(cityStreet){eye.SetPositionAndRotation(viewCamera.transform.position,viewCamera.transform.rotation);world.dayNight=true;}else world.dayNight=false;
            Toast(cityStreet?"3D 거리: WASD 이동 · Q/E 높이 · 문·사람·차 클릭 · V 돌아가기":"도시 설계 화면");
        }
        // First-person walk for districts; fly=true also allows changing height for city street views.

        void FocusDistrictFeature(Vector3 location)
        {
            world.SetDistrictView(state.district,false);
            // Stand on whatever is there (street, platform, bridge) a few metres short of the feature.
            var stand=world.SafeStreetSpawn(new Vector3(location.x,0,location.z-3.6f),Vector3.forward);
            viewCamera.transform.position=stand+Vector3.up*EyeHeight;
            viewCamera.transform.LookAt(new Vector3(location.x,stand.y+1.2f,location.z));
            eye.SetPositionAndRotation(viewCamera.transform.position,viewCamera.transform.rotation);
            undergroundWalk=false;
        }
        // Stand at `location` facing `facing`, at eye height, e.g. in reach of a door.
        void FocusDistrictFeature(Vector3 location,Vector3 facing)
        {
            world.SetDistrictView(state.district,false);
            var stand=world.SafeStreetSpawn(location,facing);
            Teleport(stand+Vector3.up*EyeHeight,facing);
            undergroundWalk=false;
        }
        // Left edge of the 3D view in GUI units (the side panel covers the rest).
        float WorldLeft(){return InOpenWorld?0f:548f;}
        bool PointerOverWorld()
        {
            // Same scale as the GUI matrix in OnGUI.
            float scale=Mathf.Max(.55f,Mathf.Min(Screen.width/1440f,Screen.height/860f));
            float x=Input.mousePosition.x/scale;
            float y=(Screen.height-Input.mousePosition.y)/scale;
            float w=Screen.width/scale;
            if(x>w-210f&&(y<305f||y>Screen.height/scale-235f))return false;
            if(boardRect.Contains(new Vector2(x,y)))return false; // the station's arrival board over the map
            return x>WorldLeft()&&x<w-24f&&y>108f&&y<Screen.height/scale-56f;
        }
        // Zooms by a factor so the whole country and a single subway station are both a few steps away.
        void ZoomMap(float wheel)
        {
            if(Mathf.Abs(wheel)<.01f)return;
            viewCamera.orthographicSize=Mathf.Clamp(viewCamera.orthographicSize*Mathf.Pow(.85f,wheel),.8f,90f);
        }
        void PanMap(float x,float z)
        {
            float speed=Mathf.Clamp(viewCamera.orthographicSize/30f,.04f,2f);x*=speed;z*=speed; // the same screen distance at any zoom
            var p=viewCamera.transform.position;
            p.x=Mathf.Clamp(p.x+x,-95f,95f);
            p.z=Mathf.Clamp(p.z+z,-110f,80f);
            viewCamera.transform.position=p;
        }
        void ResetMapView()
        {
            viewCamera.transform.position=new Vector3(0,155,-50);
            viewCamera.transform.LookAt(Vector3.zero);
            viewCamera.orthographic=true;viewCamera.orthographicSize=75f;
        }
        void SelectCity(CityInfo city)
        {
            state.selectedCity=city.id;Save();Toast(city.name+" 선택");
            if(mode=="city"){world.BuildCityPlot(state);if(cityStreet)world.SetCityView(true);return;}
            if(mode=="map"&&viewCamera!=null)
            {
                float zoom=viewCamera.orthographicSize;
                world.BuildMap(state);
                var target=city.position;target.y=0;
                viewCamera.transform.position=target+new Vector3(0,155,-50);
                viewCamera.transform.LookAt(target);
                viewCamera.orthographicSize=Mathf.Min(zoom,18f);
            }
        }
        void EnterWorld(){mode="world";riding=false;world.BuildWorldBackdrop();tab="세계·외교";StartCoroutine(LoadGDP(state.selectedCountry));}
        IEnumerator LoadGDP(int country)
        {
            string iso=WorldCatalog.Countries[Mathf.Clamp(country,0,WorldCatalog.Countries.Length-1)].iso2;
            if(iso=="-99"||iso.Length!=2){gdpText="해당 국가 코드 자료 없음";yield break;}
            gdpText="세계은행 조회 중…";
            using(var request=UnityWebRequest.Get("https://api.worldbank.org/v2/country/"+iso+"/indicator/NY.GDP.MKTP.CD?format=json&per_page=5"))
            {
                request.timeout=8;yield return request.SendWebRequest();
                if(country!=state.selectedCountry)yield break;
                if(request.result!=UnityWebRequest.Result.Success){gdpText="조회 불가 · 자료 링크 참고";yield break;}
                var match=Regex.Match(request.downloadHandler.text,"\\\"date\\\":\\\"(\\d{4})\\\",\\\"value\\\":([0-9.Ee+-]+)");
                double value;
                gdpText=match.Success&&double.TryParse(match.Groups[2].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out value)
                    ?(value/1e12).ToString("F2",CultureInfo.InvariantCulture)+"조 USD ("+match.Groups[1].Value+"년 공개값)":"공개값 없음 · 자료 링크 참고";
            }
        }
        void StartRide(TransitRoute route)
        {
            state.selectedCity=route.from;Save();world.BuildRideScene(route.type);mode="ride";tab="서울 3D";riding=true;rideProgress=0;currentRide=route;
            world.BuildRideInfrastructure(route.type);
            world.vehicle=world.CreateVehicle(route.type,new Vector3(0,WorldBuilder.RideHeight(route.type),-30));
            if(world.vehicle!=null){world.vehicle.SetActive(true);world.CreateRideCabin(world.vehicle,route.type);}
            Toast(route.name+" 탑승: "+route.fromStop+" → "+route.toStop);
        }
        void LateUpdate(){RenderMinimap();}
        void Update()
        {
            UpdatePerformanceMeter();
            if(maintenance)return;
            if(showIntro&&(Input.GetKeyDown(KeyCode.Return)||Input.GetKeyDown(KeyCode.Space)))showIntro=false;
            bool typing=GUIUtility.keyboardControl!=0;
            if(Input.GetKeyDown(KeyCode.Escape)){if(OpenWorldEscape()){}else if(ReleaseCursorOnEscape()){}else if(typing)GUIUtility.keyboardControl=0;else if(TransitRideActive()){}else if(InVehicle())LeaveVehicle();else if(cityStreet)ToggleCityStreet();else if(mode=="spectate")StopSpectating();else if(mode!="map")ReturnMap();}
            if(!showIntro&&!typing)
            {
                if(!InOpenWorld)for(int i=0;i<tabs.Length;i++)if(Input.GetKeyDown((KeyCode)((int)KeyCode.F1+i)))SelectTab(i);
                if((mode=="city"||mode=="spectate")&&Input.GetKeyDown(KeyCode.V))ToggleCityStreet();
                if(Input.GetKeyDown(KeyCode.F8)&&state.era>=9&&!InOpenWorld)
                {
                    var target=selectedStation??TransitSchedule.RailStation("서울");
                    if(target!=null)VisitNetworkStation(target,target.lines.Find(l=>l.kind=="ktx")??target.lines.Find(l=>l.kind=="metro"||l.kind=="mugunghwa"));
                }
                if(Input.GetKeyDown(KeyCode.F9)){Save();Toast("저장했습니다.");}
                if(mode=="district"&&Input.GetKeyDown(KeyCode.M))showMinimap=!showMinimap;
                if(tab=="지도"&&Input.GetKeyDown(KeyCode.C)){if(mode=="city")ReturnMap();else EnterCityPlot();}
                if(mode=="city"&&!cityStreet&&Input.GetKeyDown(KeyCode.R)){roadTool=!roadTool;world.SetRoadCandidates(roadTool);Toast(roadTool?"도로 도구 켜짐: 구간을 클릭하세요.":"도로 도구 꺼짐");}
                if(Input.GetKeyDown(KeyCode.N))Advance();
                if(tab=="생산·건설")
                {
                    if(Input.GetKeyDown(KeyCode.G))Gather(0,6);
                    if(Input.GetKeyDown(KeyCode.H))Gather(1,6);
                    if(Input.GetKeyDown(KeyCode.J))Gather(2,4);
                    if(Input.GetKeyDown(KeyCode.LeftBracket))selectedBuilding=Mathf.Max(0,selectedBuilding-1);
                    if(Input.GetKeyDown(KeyCode.RightBracket))selectedBuilding=Mathf.Min(GameContent.Buildings.Length-1,selectedBuilding+1);
                    if(Input.GetKeyDown(KeyCode.B))Build(GameContent.Buildings[selectedBuilding]);
                }
                if(tab=="교통"&&Input.GetKeyDown(KeyCode.F10))AddRoute();
                if(mode=="district")
                {
                    for(int i=0;i<4;i++)if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1+i)))EnterDistrict(i);
                    if(state.era>=9&&Input.GetKeyDown(KeyCode.V))world.SetDistrictView(state.district,!world.aerialDistrict);
                    if(state.era>=9&&cabin==null)
                    {
                        if(Input.GetKeyDown(KeyCode.B)&&world.FirstBusStop!=Vector3.zero)FocusDistrictFeature(world.FirstBusStop,world.FirstBusStopFacing);
                        if(Input.GetKeyDown(KeyCode.P)&&world.MappedParkingCount>0)FocusDistrictFeature(world.FirstParkingPosition);
                        if(Input.GetKeyDown(KeyCode.G)&&world.MappedEntranceCount>0)FocusDistrictFeature(world.FirstEntrancePosition+world.FirstEntranceFacing*7f,-world.FirstEntranceFacing);
                    }
                }
            }
            if(InOpenWorld){UpdateOpenWorld(typing);return;}
            if(playtest&&Input.GetKeyDown(KeyCode.F11))NextPlaytestPoint();
            if(playtest&&Input.GetKeyDown(KeyCode.F10))Debug.Log("Playtest position feet="+Feet.ToString("F3")+" yaw="+Yaw+" pitch="+Pitch+" cabin="+(cabin!=null));
            UpdateCursor();
            UpdateRides();
            if(!typing&&OnFoot()&&Input.GetKeyDown(KeyCode.T))thirdPerson=!thirdPerson;
            // Cars stop for the player on foot in a street view, as for any pedestrian.
            bool onFoot=!showIntro&&!InVehicle()&&!TransitRideActive()&&((mode=="district"&&!world.aerialDistrict)||mode=="rail"||mode=="interior"||cityStreet);
            TrafficVehicle.PlayerFeet=onFoot?Feet:(Vector3?)null;
            if(!showIntro&&!typing)
            {
                if(InVehicle()||TransitRideActive()){}
                else if(mode=="rail"||mode=="interior")WalkCamera(mode=="rail"?2000:55,false);
                else if(mode=="district"&&!world.aerialDistrict)WalkCamera(state.era>=9?315:55,false);
                else if(cityStreet)WalkCamera(50,true);
                UpdateDistrictTransfers();PlaceViewCamera();UpdateInteraction();
            }
            else hoverHint="";
            if(onFoot)PlaceViewCamera();
            if((mode=="map"||mode=="city")&&!showIntro&&!cityStreet&&!typing)
            {
                if(mode=="city"&&roadTool&&Input.GetMouseButtonDown(0)&&PointerOverWorld())
                {
                    // People, lamps and cars also have colliders: take the nearest road marker under the cursor.
                    RoadMarker nearest=null;float best=float.MaxValue;
                    foreach(var hit in Physics.RaycastAll(viewCamera.ScreenPointToRay(Input.mousePosition),250f))
                    {var marker=hit.collider.GetComponent<RoadMarker>();if(marker!=null&&hit.distance<best){best=hit.distance;nearest=marker;}}
                    if(nearest!=null)ToggleRoad(nearest);
                }
                if(mode=="map"&&Input.GetMouseButtonDown(0)&&PointerOverWorld()&&hoveredStation==null&&!HandleNetworkMapClick())
                {
                    var ray=viewCamera.ScreenPointToRay(Input.mousePosition);RaycastHit hit;
                    if(Physics.Raycast(ray,out hit,300)){var marker=hit.collider.GetComponentInParent<CityMarker>();if(marker!=null)SelectCity(GameContent.City(marker.cityId));}
                }
                if(tab=="지도"||tab=="교통")
                {
                    float horizontal=(Input.GetKey(KeyCode.RightArrow)||Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.LeftArrow)||Input.GetKey(KeyCode.A)?1:0);
                    float vertical=(Input.GetKey(KeyCode.UpArrow)||Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.DownArrow)||Input.GetKey(KeyCode.S)?1:0);
                    if(horizontal!=0||vertical!=0)PanMap(horizontal*32f*Time.deltaTime,vertical*32f*Time.deltaTime);
                    if(PointerOverWorld()&&Input.GetMouseButton(1))PanMap(-Input.GetAxis("Mouse X")*2.2f,-Input.GetAxis("Mouse Y")*2.2f);
                    if(Input.GetKeyDown(KeyCode.Equals)||Input.GetKeyDown(KeyCode.KeypadPlus))ZoomMap(2f);
                    if(Input.GetKeyDown(KeyCode.Minus)||Input.GetKeyDown(KeyCode.KeypadMinus))ZoomMap(-2f);
                    if(PointerOverWorld())ZoomMap(Input.GetAxis("Mouse ScrollWheel")*7f);
                }
            }
            // Country selection is handled by the atlas overlay in OnGUI.
            if(world.dayNight)DayCycle.Advance(Time.deltaTime);
            if(riding&&world.vehicle!=null)
            {
                rideProgress+=Time.deltaTime/12f;float t=Mathf.Clamp01(rideProgress);
                var p=new Vector3(currentRide.type=="bus"?Mathf.Sin(t*1.4f)*2:0,WorldBuilder.RideHeight(currentRide.type),Mathf.Lerp(-30,42,t));world.vehicle.transform.position=p;
                viewCamera.transform.position=p+new Vector3(0,1.65f,-1.6f);viewCamera.transform.LookAt(p+new Vector3(0,1.6f,8));
                if(t>=1){riding=false;Toast("목적지에 도착했습니다. 지도 버튼으로 돌아갈 수 있습니다.");}
            }
        }
        Texture2D Solid(Color color){var t=new Texture2D(1,1);t.SetPixel(0,0,color);t.Apply();return t;}
        // The IMGUI text engine only tries the first family name, so put a Korean font that is installed first
        // (macOS: Apple SD Gothic Neo, Windows: Malgun Gothic, Android/Linux: Noto Sans CJK KR).
        static string[] KoreanFontNames()
        {
            var wanted=new[]{"Apple SD Gothic Neo","Malgun Gothic","Noto Sans CJK KR","Noto Sans KR","NanumGothic","Arial Unicode MS"};
            var installed=new HashSet<string>(Font.GetOSInstalledFontNames());
            foreach(var name in wanted)if(installed.Contains(name))return new[]{name};
            return wanted;
        }
        void SetupStyles()
        {
            koreanFont=Font.CreateDynamicFontFromOSFont(KoreanFontNames(),18);
            panelTexture=Solid(new Color(.045f,.071f,.077f,.97f));
            cardTexture=Solid(new Color(.085f,.12f,.127f,.97f));
            goldTexture=Solid(new Color(.78f,.55f,.28f));
            buttonTexture=Solid(new Color(.12f,.17f,.18f));
            inkTexture=Solid(new Color(.025f,.041f,.046f));
            softTexture=Solid(new Color(.02f,.035f,.04f,.78f));
            lineTexture=Solid(new Color(.45f,.34f,.21f,.68f));
            chapterArt=Resources.Load<Texture2D>("EarlyEraArt");
            worldAtlas=Resources.Load<Texture2D>("Geo/WorldAtlas");
            var maskAsset=Resources.Load<TextAsset>("Geo/WorldCountryMask");
            worldCountryMask=maskAsset!=null?maskAsset.bytes:null;
            titleStyle=new GUIStyle(GUI.skin.label){font=koreanFont,fontSize=29,fontStyle=FontStyle.Bold,normal={textColor=new Color(.96f,.93f,.85f)},wordWrap=true};
            headingStyle=new GUIStyle(GUI.skin.label){font=koreanFont,fontSize=19,fontStyle=FontStyle.Bold,normal={textColor=new Color(.94f,.73f,.43f)},wordWrap=true};
            bodyStyle=new GUIStyle(GUI.skin.label){font=koreanFont,fontSize=15,normal={textColor=new Color(.91f,.93f,.89f)},wordWrap=true,padding=new RectOffset(0,0,2,3)};
            smallStyle=new GUIStyle(bodyStyle){fontSize=12,normal={textColor=new Color(.69f,.75f,.72f)}};
            kickerStyle=new GUIStyle(smallStyle){fontSize=11,fontStyle=FontStyle.Bold,normal={textColor=new Color(.79f,.61f,.37f)}};
            metricStyle=new GUIStyle(bodyStyle){fontSize=17,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};
            boxStyle=new GUIStyle(GUI.skin.box){normal={background=panelTexture,textColor=Color.white},padding=new RectOffset(16,16,15,15)};
            cardStyle=new GUIStyle(boxStyle){normal={background=cardTexture,textColor=Color.white},padding=new RectOffset(14,14,12,12)};
            buttonStyle=new GUIStyle(GUI.skin.button){font=koreanFont,fontSize=14,fontStyle=FontStyle.Bold,normal={background=buttonTexture,textColor=new Color(.94f,.94f,.89f)},hover={background=cardTexture,textColor=Color.white},active={background=cardTexture,textColor=Color.white},padding=new RectOffset(12,12,10,10)};
            accentButtonStyle=new GUIStyle(buttonStyle){normal={background=goldTexture,textColor=new Color(.09f,.13f,.13f)},hover={background=goldTexture,textColor=new Color(.09f,.13f,.13f)}};
            navStyle=new GUIStyle(buttonStyle){alignment=TextAnchor.MiddleLeft,fontSize=15,padding=new RectOffset(16,10,0,0)};
            navSelectedStyle=new GUIStyle(navStyle){normal={background=cardTexture,textColor=new Color(.95f,.77f,.5f)},hover={background=cardTexture,textColor=new Color(.95f,.77f,.5f)}};
            textFieldStyle=new GUIStyle(GUI.skin.textField){font=koreanFont,fontSize=15,normal={textColor=Color.white},padding=new RectOffset(11,11,9,9)};
            GUI.skin.font=koreanFont;
        }
        void Label(string text,GUIStyle style=null,params GUILayoutOption[] options){GUILayout.Label(text,style??bodyStyle,options);}
        bool Button(string text,bool accent=false,params GUILayoutOption[] options){return GUILayout.Button(text,accent?accentButtonStyle:buttonStyle,options);}
        void SelectTab(int i)
        {
            returnToCityAfterBuild=mode=="city"&&i==1;
            tab=tabs[i];scroll=Vector2.zero;
            if(tab=="지도")ReturnMap();
            else if(tab=="서울 3D"){if(mode!="district"&&mode!="openworld")EnterDistrict(state.district);}
            else if(tab=="세계·외교"){if(mode!="world")EnterWorld();}
            else if(tab=="온라인"){if(mode!="map"&&mode!="spectate")ReturnMap(false);}
            else if(mode!="map")ReturnMap(false);
        }
        void ResourceCost(int[] values)
        {string text="";for(int i=0;i<7;i++)if(values[i]>0)text+=GameContent.ResourceNames[i]+" "+values[i]+"  ";Label(text,smallStyle);}
        void OnGUI()
        {
            if(titleStyle==null)SetupStyles();
            GUI.enabled=!maintenance;
            float scale=Mathf.Min(Screen.width/1440f,Screen.height/860f);scale=Mathf.Max(.55f,scale);GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(scale,scale,1));
            float w=Screen.width/scale,h=Screen.height/scale;
            if(InOpenWorld)
            {
                if(viewCamera!=null)viewCamera.rect=new Rect(0,0,1,1);
                DrawOpenWorldGUI(w,h);if(cwReady&&!cwMapOpen&&!cwMenuOpen)DrawHoverHint();
                DrawFade(w,h);if(Time.time<noticeUntil)GUI.Box(new Rect(w*.5f-280,h-113,560,42),notice,cardStyle);
                GUI.enabled=true;return;
            }
            bool scenic=tab=="지도"||tab=="교통"||tab=="서울 3D"||tab=="세계·외교"||tab=="온라인";
            if(viewCamera!=null)viewCamera.rect=scenic?new Rect(548f/w,0f,1f-548f/w,1f):new Rect(0f,0f,1f,1f);
            if(!scenic)GUI.DrawTexture(new Rect(0,0,w,h),inkTexture);
            GUI.DrawTexture(new Rect(0,0,w,91),inkTexture);
            GUI.DrawTexture(new Rect(0,0,190,h),inkTexture);
            if(scenic)GUI.DrawTexture(new Rect(190,92,358,h-146),inkTexture);
            GUI.DrawTexture(new Rect(190,0,1,h),lineTexture);
            GUI.DrawTexture(new Rect(0,90,w*(state.era+1f)/GameContent.Eras.Length,2),goldTexture);
            GUI.Label(new Rect(21,12,170,23),"CHRONICLE / 2026",kickerStyle);
            GUI.Label(new Rect(20,31,177,47),"반도의 시간",titleStyle);
            GUI.Label(new Rect(215,12,420,21),"시대 "+(state.era+1).ToString("00")+" / "+GameContent.Eras.Length.ToString("00")+"    ·    "+state.turn+"시기",kickerStyle);
            GUI.Label(new Rect(215,35,435,43),GameContent.Eras[state.era].name,headingStyle);
            float resourceX=w-755f;
            for(int i=0;i<7;i++)
            {
                float x=resourceX+i*78f;
                GUI.DrawTexture(new Rect(x,16,73,58),buttonTexture);
                GUI.Label(new Rect(x+8,19,65,19),GameContent.ResourceNames[i],kickerStyle);
                GUI.Label(new Rect(x+8,39,65,28),state.resources[i].ToString("N0"),metricStyle);
            }
            if(GUI.Button(new Rect(w-164,20,145,54),state.era>=9?"다음 시기  →":"다음 시대로  →",accentButtonStyle))
            {if(state.era>=9){Tick();Toast("도시 운영이 한 시기 진행되었습니다.");}else Advance();}
            GUI.Label(new Rect(18,115,160,24),"NAVIGATION",kickerStyle);
            for(int i=0;i<tabs.Length;i++)
            {
                float y=151+i*56;
                if(tab==tabs[i])GUI.DrawTexture(new Rect(0,y,4,47),goldTexture);
                if(GUI.Button(new Rect(12,y,164,47),(i+1).ToString("00")+"   "+tabs[i],tab==tabs[i]?navSelectedStyle:navStyle))SelectTab(i);
            }
            GUI.Label(new Rect(19,h-166,165,25),"현재 목표",kickerStyle);
            GUI.Label(new Rect(19,h-140,160,76),state.era<9?"다음 시대\n"+GameContent.Eras[state.era+1].name:"도시와 교통망을\n확장하세요.",bodyStyle);
            GUI.DrawTexture(new Rect(19,h-61,155,1),lineTexture);
            GUI.Label(new Rect(19,h-54,160,50),"F1–F7 화면 전환\nF9 저장  ·  Esc 지도",smallStyle);
            bool split=!scenic&&w>=1220;
            Rect contentRect=scenic?new Rect(207,112,325,h-183):new Rect(207,112,split?Mathf.Min(765,w-590):w-229,h-183);
            GUI.Box(contentRect,"",boxStyle);
            GUILayout.BeginArea(new Rect(contentRect.x+17,contentRect.y+20,contentRect.width-34,contentRect.height-40));
            scroll=GUILayout.BeginScrollView(scroll);
            if(tab=="지도")DrawMapPanel();else if(tab=="생산·건설")DrawBuildPanel();else if(tab=="교통")DrawTransitPanel();else if(tab=="역사")DrawHistoryPanel();else if(tab=="세계·외교")DrawWorldPanel();else if(tab=="온라인")DrawOnlinePanel();else DrawWalkPanel();
            GUILayout.EndScrollView();GUILayout.EndArea();
            if(split)DrawInsightPanel(new Rect(contentRect.xMax+16,112,w-contentRect.xMax-36,h-183));
            if(tab=="세계·외교"&&!showIntro)DrawWorldAtlas(w,h);
            if((mode=="map"||mode=="city")&&(tab=="지도"||tab=="교통")&&!showIntro&&!cityStreet)DrawMapControls(w,h);
            DrawNetworkOverlay(w,h);
            if(tab=="온라인"&&!showIntro&&battle!=null&&onlineMode=="battle")DrawBattleMap(w,h);
            GUI.DrawTexture(new Rect(190,h-54,w-190,54),inkTexture);
            GUI.DrawTexture(new Rect(190,h-54,w-190,1),lineTexture);
            GUI.Label(new Rect(214,h-43,w-395,28),state.log.Count>0?state.log[0]:"한반도의 시간을 시작하세요.",smallStyle);
            if(GUI.Button(new Rect(w-159,h-45,139,35),"지금 저장",buttonStyle)){Save();Toast("저장했습니다.");}
            if(Application.isMobilePlatform&&((mode=="district"&&!world.aerialDistrict)||mode=="rail"||mode=="interior"||cityStreet)&&!showIntro)
            {
                float cx=w*.5f;
                touchMove=(GUI.RepeatButton(new Rect(cx-170,h-184,72,72),"↑",buttonStyle)?1:0)-(GUI.RepeatButton(new Rect(cx-170,h-104,72,72),"↓",buttonStyle)?1:0);
                touchStrafe=(GUI.RepeatButton(new Rect(cx-90,h-104,72,72),"→",buttonStyle)?1:0)-(GUI.RepeatButton(new Rect(cx-250,h-104,72,72),"←",buttonStyle)?1:0);
                touchYaw=(GUI.RepeatButton(new Rect(cx+160,h-104,72,72),"회전 →",buttonStyle)?1:0)-(GUI.RepeatButton(new Rect(cx+80,h-104,72,72),"← 회전",buttonStyle)?1:0);
            }
            else touchMove=touchStrafe=touchYaw=0;
            DrawMinimap(w,h);
            DrawHoverHint();
            DrawCrosshair(w,h);DrawFade(w,h);
            if(Time.time<noticeUntil)GUI.Box(new Rect(w*.5f-280,h-113,560,42),notice,cardStyle);
            if(showIntro)
            {
                GUI.DrawTexture(new Rect(0,0,w,h),softTexture);
                bool art=state.era==1&&chapterArt!=null;
                float top=h*.5f-(art?230:175);
                GUI.Box(new Rect(w*.5f-290,top,580,art?460:350),"",boxStyle);
                if(art)GUI.DrawTexture(new Rect(w*.5f-270,top+20,540,165),chapterArt,ScaleMode.ScaleAndCrop);
                GUI.Label(new Rect(w*.5f-262,top+(art?203:27),500,22),"시대 "+(state.era+1).ToString("00")+"   /   "+GameContent.Eras[state.era].category,kickerStyle);
                GUI.Label(new Rect(w*.5f-262,top+(art?228:62),520,48),GameContent.Eras[state.era].name,titleStyle);
                GUI.Label(new Rect(w*.5f-262,top+(art?287:123),520,110),GameContent.Eras[state.era].detail,bodyStyle);
                if(GUI.Button(new Rect(w*.5f+88,top+(art?387:280),174,47),"이 시대로 시작",accentButtonStyle))showIntro=false;
                GUI.Label(new Rect(w*.5f-262,top+(art?400:293),310,32),"Enter 키로도 시작할 수 있습니다.",smallStyle);
            }
            if(updateAvailable&&GUI.Button(new Rect(w-355,h-105,178,39),"업데이트 다운로드",accentButtonStyle))Application.OpenURL(updateUrl);
            GUI.enabled=true;
            if(maintenance)
            {
                GUI.DrawTexture(new Rect(0,0,w,h),softTexture);
                GUI.Box(new Rect(w*.5f-290,h*.5f-120,580,240),"",boxStyle);
                GUI.Label(new Rect(w*.5f-255,h*.5f-83,510,38),"운영 점검 중",titleStyle);
                GUI.Label(new Rect(w*.5f-255,h*.5f-22,510,115),maintenanceMessage,bodyStyle);
            }
        }
        void DrawInsightPanel(Rect rect)
        {
            GUI.Box(rect,"",boxStyle);
            float x=rect.x+20,y=rect.y+25,width=rect.width-40;
            GUI.Label(new Rect(x,y,width,24),"THE CHRONICLE",kickerStyle);y+=34;
            GUI.Label(new Rect(x,y,width,45),GameContent.Eras[state.era].name,headingStyle);y+=49;
            GUI.Label(new Rect(x,y,width,30),GameContent.Eras[state.era].date,kickerStyle);y+=46;
            if(state.era==1&&chapterArt!=null)
            {
                GUI.DrawTexture(new Rect(x,y,width,150),chapterArt,ScaleMode.ScaleAndCrop);y+=170;
            }
            GUI.DrawTexture(new Rect(x,y,width,1),lineTexture);y+=21;
            GUI.Label(new Rect(x,y,width,25),"이 시대의 기록",kickerStyle);y+=32;
            GUI.Label(new Rect(x,y,width,125),GameContent.Eras[state.era].detail,bodyStyle);y+=141;
            GUI.DrawTexture(new Rect(x,y,width,1),lineTexture);y+=21;
            GUI.Label(new Rect(x,y,width,25),"도시의 현재",kickerStyle);y+=35;
            GUI.Label(new Rect(x,y,width,29),GameContent.City(state.selectedCity).name+"  /  "+GameContent.City(state.selectedCity).region,bodyStyle);y+=40;
            GUI.Label(new Rect(x,y,width,78),"시설 "+state.buildings.Count+"개     개통 노선 "+state.routes.Count+"개\n\n지도에서 도시를 선택해 건설 위치를 바꿀 수 있습니다.",smallStyle);y+=102;
            if(y+112<rect.yMax)
            {
                GUI.DrawTexture(new Rect(x,y,width,1),lineTexture);y+=21;
                GUI.Label(new Rect(x,y,width,25),"최근 행동",kickerStyle);y+=32;
                int count=Mathf.Min(3,state.log.Count);
                for(int i=0;i<count;i++){GUI.Label(new Rect(x,y,width,42),"· "+state.log[i],smallStyle);y+=42;}
            }
        }
        void DrawMapControls(float w,float h)
        {
            float x=w-89f;
            GUI.Box(new Rect(x-8,116,79,179),"",boxStyle);
            GUI.Label(new Rect(x-1,123,68,22),"지도",kickerStyle);
            if(GUI.Button(new Rect(x,149,62,40),"+",buttonStyle))ZoomMap(2f);
            if(GUI.Button(new Rect(x,194,62,40),"−",buttonStyle))ZoomMap(-2f);
            if(GUI.Button(new Rect(x,239,62,39),"중앙",buttonStyle))ResetMapView();
            GUI.Box(new Rect(w-202,h-224,176,140),"",boxStyle);
            GUI.Label(new Rect(w-184,h-211,150,20),"지도 이동",kickerStyle);
            if(GUI.RepeatButton(new Rect(w-130,h-185,38,33),"↑",buttonStyle))PanMap(0,24f*Time.deltaTime);
            if(GUI.RepeatButton(new Rect(w-171,h-149,38,33),"←",buttonStyle))PanMap(-24f*Time.deltaTime,0);
            if(GUI.RepeatButton(new Rect(w-130,h-149,38,33),"↓",buttonStyle))PanMap(0,-24f*Time.deltaTime);
            if(GUI.RepeatButton(new Rect(w-89,h-149,38,33),"→",buttonStyle))PanMap(24f*Time.deltaTime,0);
        }
        void DrawMapPanel()
        {
            Label("01  /  TERRITORY",kickerStyle);Label("한반도 지도",titleStyle);
            Label("실제 해안선을 바탕으로 한 지도입니다. 중국·러시아·일본은 배경이고 한반도의 도시만 선택할 수 있습니다.",smallStyle);
            Label("지도 이동: WASD·방향키  /  지도 위 휠·오른쪽 +·− 확대  /  C: 도시 설계  /  R: 도로 도구",smallStyle);
            GUILayout.Space(18);GUILayout.BeginVertical(cardStyle);
            var city=GameContent.City(state.selectedCity);Label(city.name+" · "+city.region,headingStyle);
            Label("건설 부지 "+state.buildings.FindAll(b=>b.city==city.id).Count+" / "+CityCapacity(city)+" · 연결 노선 "+state.routes.FindAll(r=>r.from==city.id||r.to==city.id).Count+"개");
            var economy=Economy(city.id);
            Label("인구 "+economy.population+" · 행복도 "+economy.happiness+"/100 · 도시 예산 "+economy.budget,smallStyle);
            if(Button(mode=="city"?"한반도 지도로 돌아가기":"도시 설계 화면 열기",true)){if(mode=="city")ReturnMap();else EnterCityPlot();}
            if(mode=="city"&&Button(cityStreet?"설계 화면으로 (V)":"내 도시 3D 거리 보기 (V)",cityStreet))ToggleCityStreet();
            if(mode=="city"&&!cityStreet)
            {
                Label("건설한 도로 "+state.roads.FindAll(r=>r.city==city.id).Count+"구간",smallStyle);
                if(Button(roadTool?"도로 도구 끄기 (R)":"도로 도구 켜기 (R)",roadTool)){roadTool=!roadTool;world.SetRoadCandidates(roadTool);}
                Label("도로 도구를 켠 뒤 회색 구간을 클릭해 건설·철거합니다. 구간당 석재 3, 예산 5.",smallStyle);
            }
            if(Button("이 도시 부지 확대",true))SelectCity(city);
            if(Button("선택 도시에서 건설"))tab="생산·건설";GUILayout.EndVertical();GUILayout.Space(10);
            GUILayout.BeginVertical(cardStyle);Label("전국 도시·읍 찾기",headingStyle);
            Label("현대 지명으로 위치를 찾습니다. 고대의 도시·국경을 뜻하지 않습니다.",smallStyle);
            string query=GUILayout.TextField(citySearch,40,textFieldStyle);
            if(query!=citySearch)citySearch=query;
            int shown=0;
            if(string.IsNullOrWhiteSpace(citySearch))
            {
                Label("주요 도시 · 검색하면 전국 지명이 표시됩니다.",smallStyle);
                int featuredCount=Mathf.Min(15,GameContent.Cities.Length);
                for(int i=0;i<featuredCount;i+=3)
                {
                    GUILayout.BeginHorizontal();
                    for(int j=i;j<Mathf.Min(i+3,featuredCount);j++)
                    {
                        var option=GameContent.Cities[j];
                        if(Button(option.name,option.id==state.selectedCity))SelectCity(option);
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else foreach(var option in GameContent.Cities)
            {
                if(option.name.IndexOf(citySearch,StringComparison.OrdinalIgnoreCase)<0)continue;
                if(Button(option.name+"  ·  "+option.region,option.id==state.selectedCity))SelectCity(option);
                if(++shown>=35)break;
            }
            if(!string.IsNullOrWhiteSpace(citySearch)&&shown==0)Label("검색 결과가 없습니다.",smallStyle);
            if(shown>=35)Label("결과가 많습니다. 이름을 더 입력하세요.",smallStyle);
            GUILayout.EndVertical();GUILayout.Space(10);
            GUILayout.BeginVertical(cardStyle);Label(GameContent.Eras[state.era].category+" · "+GameContent.Eras[state.era].date,headingStyle);
            Label(GameContent.Eras[state.era].detail);GUILayout.EndVertical();
        }
        void DrawBuildPanel()
        {
            Label("생산 · 건설",titleStyle);Label("선택 도시: "+GameContent.City(state.selectedCity).name+" · 채집하면 한 시기가 흐르고 시설이 생산합니다.",smallStyle);
            var chosenCity=GameContent.City(state.selectedCity);
            int used=state.buildings.FindAll(b=>b.city==chosenCity.id).Count;
            Label("건설 부지 "+used+" / "+CityCapacity(chosenCity),headingStyle);
            var economy=Economy(chosenCity.id);
            GUILayout.BeginVertical(cardStyle);Label("도시 운영",headingStyle);
            Label("인구 "+economy.population+"명 · 행복도 "+economy.happiness+"/100 · 예산 "+economy.budget,smallStyle);
            GUILayout.BeginHorizontal();if(Button("세금 −")){economy.taxRate=Mathf.Max(0,economy.taxRate-1);Save();}Label("세율 "+economy.taxRate+"%",bodyStyle);if(Button("세금 +")){economy.taxRate=Mathf.Min(25,economy.taxRate+1);Save();}GUILayout.EndHorizontal();
            Label("주거·서비스·전력·혼잡과 세율에 따라 매 시기 인구·행복도·예산이 변합니다.",smallStyle);GUILayout.EndVertical();
            Label("G 목재 · H 석재 · J 식량 · [ ] 시설 선택 · B 건설",smallStyle);
            GUILayout.Space(12);GUILayout.BeginVertical(cardStyle);Label("기초 채집",headingStyle);
            GUILayout.BeginHorizontal();if(Button("목재 +6"))Gather(0,6);if(Button("석재 +6"))Gather(1,6);if(Button("식량 +4"))Gather(2,4);GUILayout.EndHorizontal();GUILayout.EndVertical();
            GUILayout.Space(12);
            foreach(var category in GameContent.BuildCategories)
                if(Button(category+(category==buildCategory?"  ●":""),category==buildCategory))buildCategory=category;
            GUILayout.Space(12);
            foreach(var b in GameContent.Buildings)
            {
                if(b.unlock>state.era||b.category!=buildCategory)continue;
                GUILayout.BeginVertical(cardStyle);Label((GameContent.Buildings[selectedBuilding]==b?"● ":"")+b.name,headingStyle);Label(b.detail,smallStyle);
                ResourceCost(b.cost);Label("시기별 생산",smallStyle);ResourceCost(b.output);
                Label("도시 예산 비용 "+MoneyCost(b),smallStyle);
                GUI.enabled=CanPay(b.cost)&&economy.budget>=MoneyCost(b)&&used<CityCapacity(chosenCity)&&!maintenance;if(Button("건설하기"))Build(b);GUI.enabled=!maintenance;
                GUILayout.EndVertical();GUILayout.Space(8);
            }
        }
        int SelectedCityIndex(){return Mathf.Max(0,Array.FindIndex(GameContent.Cities,c=>c.id==state.selectedCity));}
        void DrawTransitPanel()
        {
            DrawNetworkPanel();
            if(selectedLine!=null)return;
            GUILayout.Space(10);
            Label("도시 간 운영 노선",titleStyle);Label("경영 모드: 도시 사이에 버스·BRT·지하철·KTX 노선을 만들고 차량을 운행해 수익을 냅니다.",smallStyle);
            Label("출발·도착 도시는 한반도 지도(F1)에서 검색해 고른 뒤 ‘지도에서 고른 도시로’를 누르세요.",smallStyle);
            GUILayout.Space(11);GUILayout.BeginVertical(cardStyle);
            Label("노선 이름",headingStyle);routeName=GUILayout.TextField(routeName,32,textFieldStyle);
            GUILayout.BeginHorizontal();foreach(var choice in new[]{"버스","BRT","지하철","KTX"})if(Button(choice+(routeType==choice?" ✓":"")))routeType=choice;GUILayout.EndHorizontal();
            GUILayout.Space(8);Label("출발 도시: "+GameContent.Cities[routeFrom].name,bodyStyle);
            GUILayout.BeginHorizontal();if(Button("지도에서 고른 도시로"))routeFrom=SelectedCityIndex();if(Button("다음 도시"))routeFrom=(routeFrom+1)%GameContent.Cities.Length;GUILayout.EndHorizontal();
            fromStop=GUILayout.TextField(fromStop,32,textFieldStyle);if(string.IsNullOrEmpty(fromStop))Label("↑ 출발 역·정류장 이름 입력",smallStyle);
            GUILayout.Space(8);Label("도착 도시: "+GameContent.Cities[routeTo].name,bodyStyle);
            GUILayout.BeginHorizontal();if(Button("지도에서 고른 도시로"))routeTo=SelectedCityIndex();if(Button("다음 도시"))routeTo=(routeTo+1)%GameContent.Cities.Length;GUILayout.EndHorizontal();
            toStop=GUILayout.TextField(toStop,32,textFieldStyle);if(string.IsNullOrEmpty(toStop))Label("↑ 도착 역·정류장 이름 입력",smallStyle);
            Label("중간 정차역 (쉼표 구분, 선택)",smallStyle);via=GUILayout.TextField(via,160,textFieldStyle);
            var cost=RouteCost(routeType);ResourceCost(cost);
            GUI.enabled=state.era>=9&&CanPay(cost)&&!maintenance;if(Button("노선 건설",true))AddRoute();GUI.enabled=!maintenance;
            if(state.era<9)Label("대한민국 현대 시대에 해금됩니다.",smallStyle);
            GUILayout.EndVertical();GUILayout.Space(15);Label("운행 노선",headingStyle);
            if(state.routes.Count==0)Label("아직 개통한 노선이 없습니다.",smallStyle);
            foreach(var r in state.routes)
            {
                GUILayout.BeginVertical(cardStyle);Label(r.name+" · "+RouteLabel(r.type),headingStyle);
                Label(GameContent.City(r.from).name+" "+r.fromStop+" → "+GameContent.City(r.to).name+" "+r.toStop,smallStyle);
                if(!string.IsNullOrEmpty(r.via))Label("경유: "+r.via,smallStyle);
                Label("차량 "+r.vehicles+"대 · 요금 "+(r.fare*100).ToString("N0")+"원 · 승객 "+r.riders+"/"+r.demand+" · 수익 "+(r.profit>=0?"+":"")+r.profit,bodyStyle);
                if(r.demand>r.riders)Label("수요가 수송력보다 많습니다. 차량을 늘리세요.",smallStyle);
                GUILayout.BeginHorizontal();
                if(Button("차량 +"))BuyVehicle(r);
                if(Button("차량 −")&&r.vehicles>1){r.vehicles--;Save();}
                if(Button("요금 −")&&r.fare>1){r.fare--;Save();}
                if(Button("요금 +")&&r.fare<TransitEconomy.BaseFare*2){r.fare++;Save();}
                GUILayout.EndHorizontal();
                Label("차량 1대 구입 "+TransitEconomy.VehiclePurchase(r.type)+" · 운영비 "+TransitEconomy.VehicleCost(r.type)+"/시기. 수치는 ‘다음 시기’에 갱신됩니다.",smallStyle);
                if(Button("차량 탑승",true))StartRide(r);GUILayout.EndVertical();GUILayout.Space(7);
            }
        }
        void DrawHistoryPanel()
        {
            Label("한국사 연대기",titleStyle);Label("서사·전승·검증된 역사 설명을 구분합니다. 생산 비용은 게임 규칙입니다.",smallStyle);
            GUILayout.Space(12);
            for(int i=0;i<GameContent.Eras.Length;i++)
            {
                var e=GameContent.Eras[i];GUILayout.BeginVertical(cardStyle);
                Label((i==state.era?"● ":i<state.era?"✓ ":"○ ")+e.name+" · "+e.date,headingStyle);
                Label(e.category,smallStyle);Label(e.detail);
                GUILayout.EndVertical();GUILayout.Space(7);
            }
            GUILayout.BeginVertical(cardStyle);Label("세계 전쟁의 영향",headingStyle);
            Label("제1차 세계대전(1914~1918), 제2차 세계대전(1939~1945)은 한반도의 외교·독립운동·해방과 연결됩니다. 이 버전에서는 연대기 설명만 제공하며 전쟁 시뮬레이션은 아직 없습니다.");
            GUILayout.EndVertical();
        }
        void DrawWorldPanel()
        {
            Label("세계 · 외교",titleStyle);Label("지도에서 국가를 직접 클릭하거나 이름으로 검색하세요.",smallStyle);
            Label("외교 관계는 게임 수치입니다. GDP는 세계은행 공개값을 조회합니다.",smallStyle);
            GUILayout.BeginHorizontal();
            if(Button("← 이전"))SelectCountry((state.selectedCountry+WorldCatalog.Countries.Length-1)%WorldCatalog.Countries.Length);
            Label((state.selectedCountry+1)+" / "+WorldCatalog.Countries.Length,smallStyle);
            if(Button("다음 →"))SelectCountry((state.selectedCountry+1)%WorldCatalog.Countries.Length);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);countrySearch=GUILayout.TextField(countrySearch,40,textFieldStyle);
            if(!string.IsNullOrWhiteSpace(countrySearch))
            {
                int matches=0;
                for(int i=0;i<WorldCatalog.Countries.Length&&matches<12;i++)
                {
                    var c=WorldCatalog.Countries[i];
                    if(c.name.IndexOf(countrySearch,StringComparison.OrdinalIgnoreCase)<0&&c.sourceName.IndexOf(countrySearch,StringComparison.OrdinalIgnoreCase)<0)continue;
                    int id=i;if(Button(c.name)){SelectCountry(id);countrySearch="";}matches++;
                }
                if(matches==0)Label("검색 결과가 없습니다.",smallStyle);
            }
            int n=state.selectedCountry;var chosen=WorldCatalog.Countries[n];
            GUILayout.BeginVertical(cardStyle);Label(chosen.name,headingStyle);
            Label("정치 체제: "+(n<systems.Length?systems[n]:"검증 자료 미연결"));
            Label("주요 자원·산업: "+(n<industries.Length?industries[n]:"검증 자료 미연결"));
            Label("외교 관계: "+state.relations[n]+"/100 (게임값)");Label("명목 GDP: "+gdpText,smallStyle);Label("군사력 순위: 공인된 단일 기준이 없어 미표시",smallStyle);
            if(Button("외교 교류 +5",true)){state.relations[n]=Mathf.Min(100,state.relations[n]+5);Log(chosen.name+"과 교류했습니다.");}
            GUILayout.EndVertical();GUILayout.Space(10);GUILayout.BeginVertical(cardStyle);
            Label("기준과 출처",headingStyle);Label("국가 경계: Natural Earth. 현대 지표의 연도·출처가 확인된 데이터만 추가합니다.");
            if(Button("세계은행 GDP 자료"))Application.OpenURL("https://data.worldbank.org/indicator/NY.GDP.MKTP.CD");GUILayout.EndVertical();
        }
        void SelectCountry(int index)
        {
            state.selectedCountry=index;Save();
            Toast(WorldCatalog.Countries[index].name+" 선택");StartCoroutine(LoadGDP(index));
        }
        void DrawWorldAtlas(float w,float h)
        {
            if(worldAtlas==null||worldCountryMask==null)return;
            float left=560f,top=142f,availableW=w-left-22f,availableH=h-top-88f;
            Rect uv=worldRegion==1?new Rect(.69f,.44f,.24f,.41f):worldRegion==2?new Rect(.44f,.38f,.20f,.48f):worldRegion==3?new Rect(.06f,.16f,.36f,.68f):new Rect(0,0,1,1);
            if(atlasZoom>0)
            {
                float factor=Mathf.Pow(.7f,atlasZoom),width=uv.width*factor,height=uv.height*factor;
                uv=new Rect(uv.center.x-width*.5f,uv.center.y-height*.5f,width,height);
            }
            float aspect=2f*uv.width/uv.height;
            float mapW=Mathf.Min(availableW,availableH*aspect),mapH=mapW/aspect;
            Rect map=new Rect(left+(availableW-mapW)*.5f,top+(availableH-mapH)*.5f,mapW,mapH);
            GUI.DrawTextureWithTexCoords(map,worldAtlas,uv);
            string[] regions={"세계 전체","동아시아","유럽","아메리카"};
            for(int i=0;i<regions.Length;i++)if(GUI.Button(new Rect(left+i*105,104,98,30),regions[i],i==worldRegion?accentButtonStyle:buttonStyle)){worldRegion=i;atlasZoom=0;}
            if(GUI.Button(new Rect(w-132,104,46,30),"+",buttonStyle))atlasZoom=Mathf.Min(5,atlasZoom+1);
            if(GUI.Button(new Rect(w-79,104,46,30),"−",buttonStyle))atlasZoom=Mathf.Max(0,atlasZoom-1);
            GUI.Label(new Rect(left,top+availableH+4,availableW,28),"국가를 클릭해 선택 · 경계 데이터 Natural Earth (공개 영역)",smallStyle);
            var e=Event.current;
            if(e.type==EventType.ScrollWheel&&map.Contains(e.mousePosition))
            {atlasZoom=Mathf.Clamp(atlasZoom+(e.delta.y<0?1:-1),0,5);e.Use();}
            if(e.type==EventType.MouseUp&&e.button==0&&map.Contains(e.mousePosition))
            {
                float rx=(e.mousePosition.x-map.x)/map.width,ry=(e.mousePosition.y-map.y)/map.height;
                int px=Mathf.Clamp(Mathf.FloorToInt((uv.x+rx*uv.width)*2048),0,2047);
                int py=Mathf.Clamp(Mathf.FloorToInt((1f-uv.y-uv.height+ry*uv.height)*1024),0,1023);
                int address=(py*2048+px)*2;
                if(address+1<worldCountryMask.Length)
                {
                    int id=worldCountryMask[address]|(worldCountryMask[address+1]<<8);
                    if(id>0&&id<=WorldCatalog.Countries.Length){SelectCountry(id-1);e.Use();}
                }
            }
        }
        void DrawWalkPanel()
        {
            DrawPerformanceSettings();
            if(mode=="rail"&&stationJourney!=null){DrawNetworkStationPanel();return;}
            if(mode=="ride")
            {
                Label("차량 탑승",titleStyle);
                if(currentRide!=null){Label(currentRide.name+" · "+RouteLabel(currentRide.type),headingStyle);Label(currentRide.fromStop+" → "+currentRide.toStop);}
                Label("이동 진행 "+Mathf.RoundToInt(Mathf.Clamp01(rideProgress)*100)+"%",smallStyle);
                if(Button("하차하고 지도로 돌아가기",true))ReturnMap();
                return;
            }
            Label("시대별 3D 탐방",titleStyle);
            Label(state.era<9?"현재 지명을 기준으로 지역을 고릅니다. 해당 시대의 가상 풍경을 표시하며 현대 건물은 나오지 않습니다.":"현대 도로·건물 윤곽은 OpenStreetMap 자료를 Blender로 모델링했습니다.",smallStyle);
            Label(state.era<9?"고대 풍경은 위치별 고증 지도가 아닌 시대별 시범 장면입니다.":"건물 높이·외관 중 자료가 없는 부분은 추정치입니다. 지도 데이터 © OpenStreetMap 기여자 (ODbL)",smallStyle);GUILayout.Space(12);
            if(state.era>=9&&Button("지도 데이터 출처"))Application.OpenURL("https://www.openstreetmap.org/copyright");
            // Riding: no teleports or district changes until the train, plane or car stops.
            if(TransitRideActive()||metroConsist!=null){Label("이동 중입니다 · 도착하면 다시 걸을 수 있습니다.",bodyStyle);DrawRideChoices();}
            else if(InVehicle())Label("차량 탑승 중 · F로 하차",bodyStyle);
            else
            {
                if(state.era>=9&&Button(world.aerialDistrict?"거리로 내려가기 (V)":"항공 시점으로 보기 (V)",true))world.SetDistrictView(state.district,!world.aerialDistrict);
                for(int i=0;i<GameContent.DistrictNames.Length;i++)if(Button(GameContent.DistrictNames[i]+(state.district==i&&mode=="district"?"  ●":""),i==state.district&&mode=="district"))EnterDistrict(i);
                if(state.era>=9&&Button("창원 오픈월드 · 시 전체 자유 탐험",true)){EnterOpenWorld();return;}
                if(state.era>=9&&Button("창원 · 실제 도로와 버스")){
                    var changwon=TransitNetwork.Lines.Find(l=>l.id.StartsWith("cw-bus-")&&l.shortName=="105");
                    if(changwon!=null){var stop=changwon.stops.Find(s=>s.lat>35.2f&&s.lon>128.65f&&s.lon<128.7f)??changwon.stops[0];VisitNetworkStation(stop,changwon);return;}
                }
                if(mode=="district"&&state.era>=9)
                {
                    Label("지도에 기록된 시설: 주차장 "+world.MappedParkingCount+"곳 · 철길 "+world.MappedTrackCount+"구간 · 지하철 출입구 "+world.MappedEntranceCount+"곳 · 버스 정류장 "+world.BusStopCount+"곳",smallStyle);
                    Label(TransitStatus(),bodyStyle);
                    if(world.MappedParkingCount>0&&Button("지도 주차장으로 이동"))FocusDistrictFeature(world.FirstParkingPosition);
                    if(world.FirstBusStop!=Vector3.zero&&Button("버스 정류장으로 이동")){FocusDistrictFeature(world.FirstBusStop,world.FirstBusStopFacing);Toast("버스가 정차해 문을 열면 걸어 들어가 타세요");}
                    if(world.MappedEntranceCount>0&&Button("지하철 출입구로 이동"))FocusDistrictFeature(world.FirstEntrancePosition+world.FirstEntranceFacing*7f,-world.FirstEntranceFacing);
                    if(world.FirstRailPlatform!=Vector3.zero&&Button("KTX 승강장으로 이동")){FocusDistrictFeature(world.FirstRailPlatform);world.KtxArriveSoon();Toast("KTX가 정차해 문을 열면 걸어 들어가 타세요");}
                    if(state.district==3&&Button("국제선 청사 1층으로 이동")){world.SetDistrictView(3,false);Teleport(world.InternationalArrivalSpawn,Vector3.forward);}
                    if(world.TerminalEntrance!=Vector3.zero&&Button("공항 청사 입구로 이동"))FocusDistrictFeature(world.TerminalEntrance,world.TerminalFacing);
                    if(!world.aerialDistrict)DrawIndoorShortcuts();
                }
            }
            GUILayout.Space(10);GUILayout.BeginVertical(cardStyle);Label("조작",headingStyle);Label("W/A/S/D 이동 · 마우스로 시선 회전 · Space 점프 · T 1/3인칭 · M 미니맵 · B 정류장 · G 지하철 · P 주차장 · F8 전국 역 방문");Label("가까이서 F/클릭으로 대화·문·단말기·자동차를 사용합니다. 버스와 열차는 열린 문으로 걸어 들어갑니다.",smallStyle);Label("지하철: 출입구 → 자동 개찰구 → 승강장에서 열린 문으로 탑승. 상점은 선택 즉시 구매됩니다. 김포공항: 2층 체크인 → 보안검색 → 3층 탑승구.",smallStyle);
            if(Button("한반도 지도로 돌아가기"))ReturnMap();GUILayout.EndVertical();
        }
    }
}
