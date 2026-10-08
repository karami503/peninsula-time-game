using System.Collections.Generic;
using UnityEngine;
namespace PeninsulaTime {
    // The official length is applied, but width/track arrangement remain the playable common model.
    public class NetworkPlatformGeometry : MonoBehaviour {
        public string stationId,lineId,officialType,officialFloor;
        public bool officialLength;
        public float length,width,nearZ,farZ;
        public Vector3 rampBottom,rampTop;
    }
    public partial class WorldBuilder {
        public readonly List<StationJourney> NetworkServices=new List<StationJourney>();
        public readonly List<NetworkTransferRoute> NetworkTransfers=new List<NetworkTransferRoute>();
        public NetStation NetworkHubStation {get;private set;}
        public float NetworkHubGrade {get;private set;}
        public float NetworkHallY {get;private set;}
        public float NetworkHallRight {get;private set;}
        readonly HashSet<StationJourney> networkPreparedJourneys=new HashSet<StationJourney>();
        partial void BuildNetworkTransferWayfinding();
        public Vector3 NetworkSpawn;
        public StationAreaBuildResult NetworkArea {get;private set;}
        public const float NetworkPlatformNear=-50f,NetworkPlatformWidth=12f;
        public static float NetworkPlatformLength(NetStation station,NetLine line){
            var dimensions=StationAreaData.Architecture(station,line);
            return dimensions!=null&&dimensions.platformLength>0?dimensions.platformLength:100f;
        }
        readonly List<Bounds> networkAreaExclusions=new List<Bounds>();
        public void RefreshNetworkArea(NetStation station,Vector3 center){
            if(NetworkArea!=null&&NetworkArea.root!=null)DestroyImmediate(NetworkArea.root);
            NetworkArea=BuildStationArea(station,new Vector3(center.x,0,center.z),Quaternion.Euler(0,180,0),networkAreaExclusions);
            // Already batched by feature type; its lifetime follows the current stop.
            if(NetworkArea.root!=null)NetworkArea.root.AddComponent<RenderMovingRoot>();
        }
        // Connected playable hub. Track grade is mapped; floor dimensions and shaft positions are schematic.
        public StationJourney BuildNetworkStation(NetStation station,NetLine line,int direction){
            NetworkServices.Clear();NetworkTransfers.Clear();networkPreparedJourneys.Clear();NetworkHubStation=station;
            NetworkArea=null;networkAreaExclusions.Clear();
            if(line.kind=="bus"||line.kind=="brt"){var bus=BuildNetworkBus(station,line,direction);NetworkServices.Add(bus);return bus;}
            Clear();SetupLight(new Color(.6f,.63f,.64f),new Color(1,.93f,.83f));
            worldCamera.orthographic=false;worldCamera.fieldOfView=72;dayNight=true;
            // The selected direction is first, followed by every usable direction of each exact service ID.
            var services=new List<KeyValuePair<NetStation,NetLine>>();var directions=new List<int>();
            if(!StationTransferCatalog.CanDepart(station,line,direction))direction=1-direction;
            services.Add(new KeyValuePair<NetStation,NetLine>(station,line));directions.Add(direction);
            foreach(var pair in StationTransferCatalog.Services(station))for(int d=0;d<2;d++){
                if(pair.Value.id==line.id&&d==direction)continue;
                if(!StationTransferCatalog.CanDepart(pair.Key,pair.Value,d))continue;
                services.Add(pair);directions.Add(d);
            }
            float grade=station.grade=="underground"?-12:station.grade=="elevated"?8:0;
            float right=services.Count*24+6,hall=grade+6.8f,end=StationJourney.Spacing*3;
            NetworkHubGrade=grade;NetworkHallY=hall;NetworkHallRight=right;
            if(grade>=0)networkAreaExclusions.Add(new Bounds(new Vector3(right*.5f,20,(end-100)*.5f),new Vector3(right+35,80,end+200)));
            else networkAreaExclusions.Add(new Bounds(new Vector3(right*.5f,20,-75),new Vector3(right+Mathf.Abs(hall)*6+32,80,34)));
            var concrete=Mat("network-platform",new Color(.62f,.63f,.61f),0,"pavement",2);
            var wall=Mat("network-wall",new Color(.72f,.74f,.73f));var steel=Mat("network-rail",new Color(.35f,.38f,.4f),.55f);
            var ground=Mat("network-grass",new Color(.28f,.37f,.23f));
            // Earth stops outside the underground excavation; it must never span an entrance ramp.
            Block("서쪽 지면",root.transform,new Vector3(-1200,-.5f,-1200),new Vector3(grade<0?-Mathf.Abs(hall)*3-19:-20,-.02f,end+1200),ground);
            Block("동쪽 지면",root.transform,new Vector3(right+(grade<0?Mathf.Abs(hall)*3+9:20),-.5f,-1200),new Vector3(right+1200,-.02f,end+1200),ground);
            Block("앞쪽 지면",root.transform,new Vector3(-20,-.5f,-1200),new Vector3(right+20,-.02f,-200),ground);
            if(grade>=0)Block("역 주변 지면",root.transform,new Vector3(-20,-.5f,-200),new Vector3(right+20,-.02f,end+1200),ground);
            else {
                Block("지하역 기초",root.transform,new Vector3(-16,grade-.5f,-85),new Vector3(right+12,grade-.2f,end+300),concrete);
                Block("지하역 서벽",root.transform,new Vector3(-16,grade-.5f,-67),new Vector3(-15,-.02f,end+300),wall);
                Block("지하역 동벽",root.transform,new Vector3(right+11,grade-.5f,-67),new Vector3(right+12,-.02f,end+300),wall);
                Block("지하역 천장",root.transform,new Vector3(-16,-.5f,-85),new Vector3(right+12,-.02f,end+300),concrete);
            }
            for(int n=0;n<services.Count;n++){
                var pair=services[n];float x=n*24;int travelDirection=directions[n];
                var obj=new GameObject(pair.Value.name+" 운행");obj.transform.SetParent(root.transform,false);
                var service=obj.AddComponent<StationJourney>();service.origin=new Vector3(x,grade,0);service.line=pair.Value;service.direction=travelDirection;
                service.stops.AddRange(StationJourney.Next(pair.Value,pair.Key,travelDirection));service.startDelay=n*15;NetworkServices.Add(service);
                float length=(service.stops.Count-1)*StationJourney.Spacing;
                Block("선로 바닥",root.transform,new Vector3(x-2,grade-.18f,-300),new Vector3(x+2,grade-.08f,length+300),Mat("network-ballast",new Color(.25f,.27f,.26f)));
                if(pair.Value.shortName.Contains("자기부상"))Block("자기부상 유도 궤도",root.transform,new Vector3(x-.9f,grade-.03f,-300),new Vector3(x+.9f,grade+.09f,length+300),steel);
                else foreach(float rail in new[]{-.7175f,.7175f})Block("레일",root.transform,new Vector3(x+rail-.04f,grade-.02f,-300),new Vector3(x+rail+.04f,grade+.08f,length+300),steel);
                // Only the current interchange is built initially. Further arrival platforms are
                // created for the service the player actually boards, then discarded on interchange change.
                var geometry=BuildNetworkPlatform(service,0,root.transform);
                var route=new NetworkTransferRoute {station=pair.Key,line=pair.Value,direction=travelDirection,service=service,
                    hallPoint=new Vector3(x+6,hall,-75),rampTop=geometry.rampTop,rampBottom=geometry.rampBottom,
                    platformPoint=new Vector3(x+6,grade+.8f,7.55f),platformLength=geometry.length,toward=StationTransferCatalog.Toward(pair.Key,pair.Value,travelDirection)};
                NetworkTransfers.Add(route);
                Board(pair.Value.name+" · "+route.toward,root.transform,new Vector3(x+6,hall+2.5f,-65.2f),Vector3.back,new Vector2(12,.65f),pair.Value.color,Color.white,.21f);
                string model=pair.Value.kind=="ktx"?"Ktx":"Metro";var train=CityModel(model,service.origin);
                if(train!=null){train.transform.SetParent(obj.transform,true);service.train=train.transform;FitBoxCollider(train);
                    service.doors=model=="Ktx"?AttachKtxCabin(train,610+n):AttachNetworkMetro(train);service.doors.cabin.kind="networkrail";service.doors.cabin.doorSide=1;service.doors.Set(1);service.Step(0);Sfx.Attach(train,"rumble",.35f,40);}
            }
            Block("공용 대합실",root.transform,new Vector3(-10,hall-.25f,-85),new Vector3(right,hall,-65),concrete);
            Block("대합실 천장",root.transform,new Vector3(-10,hall+3.4f,-85),new Vector3(right,hall+3.6f,-65),wall);
            // Enclose the concourse, leaving only the platform ramps and four street exits open.
            Block("대합실 뒤벽",root.transform,new Vector3(-10,hall,-85.2f),new Vector3(right,hall+3.4f,-85),wall);
            float wallFrom=-10;
            for(int n=0;n<services.Count;n++){
                float opening=n*24+6;
                Block("대합실 승강장 벽",root.transform,new Vector3(wallFrom,hall,-65),new Vector3(opening-2,hall+3.4f,-64.8f),wall);
                wallFrom=opening+2;
            }
            Block("대합실 승강장 벽",root.transform,new Vector3(wallFrom,hall,-65),new Vector3(right,hall+3.4f,-64.8f),wall);
            foreach(float x in new[]{-10f,right}){
                foreach(var limits in new[]{new Vector2(-85,-82.6f),new Vector2(-79.4f,-70.6f),new Vector2(-67.4f,-65)})
                    Block("대합실 출구 사이벽",root.transform,new Vector3(x-.1f,hall,limits.x),new Vector3(x+.1f,hall+3.4f,limits.y),wall);
            }
            for(float x=-5;x<right;x+=16){
                var light=new GameObject("대합실 조명").AddComponent<Light>();light.transform.SetParent(root.transform,false);light.transform.position=new Vector3(x,hall+2.9f,-76);light.range=20;light.intensity=1.3f;
            }
            // Four fallback walking exits, retained only when no mapped entrances are available.
            var fallbackExits=new GameObject("지도 출입구가 없는 역의 방향 출구");fallbackExits.transform.SetParent(root.transform,false);
            foreach(float x in new[]{-10f,right})foreach(float z in new[]{-69f,-81f}){
                float side=x<0?-1:1;var a=new Vector3(x,hall,z);var b=a+Vector3.right*side*(Mathf.Abs(hall)*3+8);b.y=0;
                NetworkRamp(a,b,3.2f,concrete,fallbackExits.transform);
                if(grade<0){
                    var delta=b-a;delta.y=0;var normal=Vector3.Cross(Vector3.up,delta.normalized)*1.6f;
                    var vertices=new List<Vector3>();var triangles=new List<int>();
                    foreach(float edge in new[]{-1f,1f})PassageQuad(vertices,triangles,a+normal*edge,b+normal*edge,b+normal*edge+Vector3.up*3.3f,a+normal*edge+Vector3.up*3.3f,true);
                    PassageQuad(vertices,triangles,a-normal+Vector3.up*3.3f,b-normal+Vector3.up*3.3f,b+normal+Vector3.up*3.3f,a+normal+Vector3.up*3.3f,true);
                    PassageMesh(fallbackExits.transform,"출구 경사 통로 벽과 천장",vertices,triangles,wall,true);
                }
                Block("출구 앞 보도",fallbackExits.transform,new Vector3(side<0?b.x-6:b.x,-.2f,z-2),new Vector3(side<0?b.x:b.x+6,0,z+2),concrete);
                Board(side<0?"서쪽 출구 ←":"동쪽 출구 →",fallbackExits.transform,a+Vector3.up*2.5f,Vector3.back,new Vector2(3,.5f),new Color(.95f,.72f,.08f),Color.black,.22f);
            }
            NetworkSpawn=new Vector3(4.5f,grade+.82f,7.55f);
            RefreshNetworkArea(station,Vector3.zero);
            bool mappedEntrances=NetworkArea!=null&&NetworkArea.entrances.Count>0;
            if(mappedEntrances){
                // Mapped access replaces these ramps. Seal their old openings too, otherwise
                // a player following the former side exit can step off the concourse floor.
                // Do this before cutting mapped passage volumes so their clearances remain valid.
                fallbackExits.SetActive(false);
                foreach(float x in new[]{-10f,right})foreach(float z in new[]{-69f,-81f})
                    Block("대합실 이전 출구 폐쇄벽",root.transform,new Vector3(x-.1f,hall,z-1.6f),new Vector3(x+.1f,hall+3.4f,z+1.6f),wall);
            }
            BuildNetworkMappedExits(new Vector3((NetworkHallRight-10)*.5f,NetworkHallY,-75),NetworkHallRight+10);
            for(float x=-5;x<right;x+=16)
                Board(station.name+(mappedEntrances?" · 환승 ↑   지도 출입구 ↓":" · 환승 ↑   출구 ← →"),root.transform,new Vector3(x,hall+2.65f,-74),Vector3.back,new Vector2(9,.55f),new Color(.09f,.17f,.24f),Color.white,.23f);
            BuildNetworkTransferWayfinding();
            // Minimap labels belong to this generated hub, just like its platforms and exits.
            // Clear() has discarded district labels; regenerate from the actual service graph.
            foreach(var route in NetworkTransfers){
                string next=route.service.stops.Count>1?TransitNetwork.Bare(route.service.stops[1].name)+" 방면":route.toward;
                Marker(route.platformPoint,route.line.shortName+" · "+next);
                Marker(route.hallPoint,route.line.shortName+" ↓");
            }
            if(NetworkMappedExits.Count>0){
                Marker(NetworkMappedExits[0].hallPoint,"출구");
                foreach(var exit in NetworkMappedExits)Marker(exit.surfacePoint,string.IsNullOrEmpty(exit.exitNumber)?"지하철 출입구":exit.exitNumber);
            }else foreach(float x in new[]{-10f,right})foreach(float z in new[]{-69f,-81f})
                Marker(new Vector3(x,hall,z),x<0?"서쪽 출구":"동쪽 출구");
            return NetworkServices[0];
        }
        // Future platforms must exist before the doors close, while the occupied cabin remains alive.
        public void PrepareNetworkJourney(StationJourney service){
            if(service==null||service.Bus||networkPreparedJourneys.Contains(service))return;
            networkPreparedJourneys.Add(service);
            var future=new GameObject("현재 탑승 노선의 도착 승강장");future.transform.SetParent(root.transform,false);
            // This is created after the initial static batch pass and is destroyed with the old interchange.
            future.AddComponent<RenderMovingRoot>();
            for(int i=1;i<service.stops.Count;i++)BuildNetworkPlatform(service,i,future.transform);
        }
        NetworkPlatformGeometry BuildNetworkPlatform(StationJourney service,int index,Transform parent){
            float x=service.origin.x,grade=service.origin.y,hall=grade+6.8f,z=index*StationJourney.Spacing;
            var stop=service.stops[index];var line=service.line;
            var concrete=Mat("network-platform",new Color(.62f,.63f,.61f),0,"pavement",2);
            var wall=Mat("network-wall",new Color(.72f,.74f,.73f));var steel=Mat("network-rail",new Color(.35f,.38f,.4f),.55f);
            var architecture=StationAreaData.Architecture(stop,line);float platformLength=NetworkPlatformLength(stop,line);
            float near=z+NetworkPlatformNear,far=near+platformLength,outer=x+2+NetworkPlatformWidth;
            var platform=Block("platform",parent,new Vector3(x+2,grade,near),new Vector3(outer,grade+.8f,far),concrete);
            var geometry=platform.AddComponent<NetworkPlatformGeometry>();geometry.stationId=stop.id;geometry.lineId=line.id;
            geometry.length=platformLength;geometry.width=NetworkPlatformWidth;geometry.nearZ=near;geometry.farZ=far;
            geometry.officialLength=architecture!=null;geometry.officialType=architecture!=null?architecture.platformType:"";geometry.officialFloor=architecture!=null?architecture.floorLabel:"";
            geometry.rampBottom=new Vector3(x+6,grade+.8f,near);geometry.rampTop=new Vector3(x+6,hall,near-15);
            Block("점자 안전선",parent,new Vector3(x+2.05f,grade+.81f,near+2),new Vector3(x+2.4f,grade+.83f,far-2),Mat("network-warning",new Color(.93f,.72f,.17f)));
            int columns=Mathf.Max(2,Mathf.CeilToInt(platformLength/45f));
            for(int k=0;k<columns;k++){
                float along=Mathf.Lerp(near+15,far-15,k/(float)(columns-1));
                Block("지붕 기둥",parent,new Vector3(outer-1,grade+.8f,along-.1f),new Vector3(outer-.8f,grade+4.5f,along+.1f),steel);
                Board(stop.name+" · "+line.shortName,parent,new Vector3(x+7,grade+3.2f,along),Vector3.back,new Vector2(7,.7f),line.color,Color.white,.23f);
            }
            Block("승강장 지붕",parent,new Vector3(x+2,grade+4.5f,near),new Vector3(outer+.5f,grade+4.68f,far),wall);
            NetworkRamp(geometry.rampBottom,geometry.rampTop,4,concrete,parent);
            if(index>0)Block("역 대합실",parent,new Vector3(x-3,hall-.2f,near-35),new Vector3(outer+3,hall,near-15),concrete);
            if(grade<0)for(float along=near+20;along<far;along+=55){var light=new GameObject("역 조명").AddComponent<Light>();light.transform.SetParent(parent,false);light.transform.position=new Vector3(x+7,grade+4,along);light.range=65;light.intensity=1.3f;}
            return geometry;
        }
        // Platform selection is stable while empty trains approach/leave. Moving train positions
        // must not switch Current to a different station or reset an arrival announcement.
        public StationJourney NetworkServiceAt(Vector3 feet,StationJourney fallback){
            if(fallback!=null&&fallback.started&&fallback.index>0)return fallback;
            float closest=float.MaxValue;StationJourney best=fallback;
            foreach(var route in NetworkTransfers){
                float x=route.platformPoint.x;
                bool platform=feet.z>=route.rampBottom.z-.5f&&feet.z<=route.rampBottom.z+route.platformLength+1;
                bool ramp=feet.z>=route.rampTop.z-.6f&&feet.z<route.rampBottom.z;
                if(!platform&&!ramp)continue;
                float distance=Mathf.Abs(feet.x-x);if(distance>8||distance>=closest)continue;
                closest=distance;best=route.service;
            }
            return best;
        }
        void NetworkRamp(Vector3 a,Vector3 b,float width,Material material,Transform parent=null){
            if(parent==null)parent=root.transform;
            WalkSlab(parent,"보행 연결 경사로",a,b,width,.2f,material,true);
            var forward=b-a;forward.y=0;var right=Vector3.Cross(Vector3.up,forward.normalized);
            foreach(float side in new[]{-1f,1f})SurveyBeam("보행 난간",parent,a+right*side*(width*.5f)+Vector3.up*.95f,b+right*side*(width*.5f)+Vector3.up*.95f,.07f,Mat("network-handrail",new Color(.27f,.3f,.32f),.6f));
        }
        VehicleDoors AttachNetworkMetro(GameObject train)
        {
            var frame=Frame(train);
            var c=frame.gameObject.AddComponent<Cabin>();c.kind="networkrail";c.floor=MetroFloor;c.halfWidth=MetroInner;
            c.back=-9.5f;c.front=9.5f;c.doors=new[]{-7.55f,-2.52f,2.52f,7.55f};c.doorHalf=.62f;c.aisle=.85f;c.exitDistance=.8f;
            var doors=frame.gameObject.AddComponent<VehicleDoors>();doors.cabin=c;
            var body=Mat("network-door",new Color(.8f,.82f,.83f),.3f);
            foreach(float z in c.doors)foreach(float side in new[]{-1f,1f})foreach(float half in new[]{-1f,1f})
            {
                var leaf=Leaf(frame,new Vector3(side*1.51f,MetroFloor+.95f,z+half*.325f),1.9f,.64f,body,VehicleGlass());
                if(side>0)doors.Add(leaf,new Vector3(0,0,half*.62f));
            }
            doors.Set(0);c.Register();return doors;
        }
    }
}
