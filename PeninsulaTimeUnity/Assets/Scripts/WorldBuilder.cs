using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PeninsulaTime
{
    public class CityMarker : MonoBehaviour { public string cityId; }
    public class RoadMarker : MonoBehaviour { public int axis,row,column; }
    public class CountryMarker : MonoBehaviour { public int countryIndex; }
    public class StreetFurnitureCollider : MonoBehaviour { public GameObject visual; }
    public partial class WorldBuilder : MonoBehaviour
    {
        public GameObject root;
        public Camera worldCamera;
        public Transform viewer; // the player's head in street views (GameController); the camera follows it
        Transform Viewer{get{return viewer!=null?viewer:worldCamera.transform;}}
        public GameObject vehicle;
        public Transform globePivot;
        public bool aerialDistrict;
        readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
        Font mapLabelFont;
        readonly List<TextMesh> labels=new List<TextMesh>();
        readonly List<GameObject> cityMarkers=new List<GameObject>();
        readonly List<GameObject> mapBuildings=new List<GameObject>();
        readonly List<CityInfo> markerCities=new List<CityInfo>();
        string selectedMarkerId="";
        Material Mat(string key,Color color,float metallic=0,string texture=null,float tiling=1f)
        {
            if(materials.ContainsKey(key))return materials[key];
            var baseMaterial=Resources.Load<Material>(key.StartsWith("osm-")?"DistrictBase":"RuntimeBase");
            Material m;
            if(baseMaterial!=null)m=new Material(baseMaterial);
            else
            {
                var shader=Shader.Find("Standard");if(shader==null)shader=Shader.Find("Sprites/Default");
                m=new Material(shader);
            }
            m.color=color;if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",metallic);if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.28f);
            // Tiled detail textures from AssetSources/Textures/make_textures.py, tinted by the colour.
            var image=texture!=null?Resources.Load<Texture2D>("Textures/City/"+texture):null;
            if(image!=null){m.mainTexture=image;m.mainTextureScale=Vector2.one*tiling;}
            // Station and terminal glass is see-through so platforms, halls and the apron show behind it.
            if(key.EndsWith("-glass")&&(key.StartsWith("terminal")||key.StartsWith("station")||key.StartsWith("escalator")||key.StartsWith("psd")))
            {
                var glass=Shader.Find("Peninsula/Glass");
                if(glass!=null){m.shader=glass;m.color=new Color(color.r,color.g,color.b,.24f);}
            }
            m.enableInstancing=true;
            materials[key]=m;return m;
        }
        GameObject Primitive(PrimitiveType type,string name,Transform parent,Vector3 position,Vector3 scale,Material material)
        {
            var o=GameObject.CreatePrimitive(type);o.name=name;o.transform.SetParent(parent,false);o.transform.localPosition=position;o.transform.localScale=scale;o.GetComponent<Renderer>().sharedMaterial=material;return o;
        }
        void Clear(){walkGraph=null;MapMarkers.Clear();if(root!=null)DestroyImmediate(root);root=new GameObject("Generated World");root.transform.SetParent(transform,false);vehicle=null;globePivot=null;labels.Clear();cityMarkers.Clear();markerCities.Clear();mapBuildings.Clear();networkLines.Clear();networkWeights.Clear();networkLayer=highlightLayer=null;}
        Light sun;Color outdoorAmbient;
        static readonly Color IndoorAmbient=new Color(.46f,.46f,.47f);
        // Underground stations and the terminal (far east of the map) are lit by their own lamps, not the sun,
        // since their ceilings are too far from the camera for shadows to keep sunlight out.
        public void UpdateInteriorLighting(Vector3 eye)
        {
            if(sun==null)return;
            bool inside=eye.y<-2f||IsDomesticAirportInterior(eye)||IsInternationalAirportInterior(eye);
            if(sun.enabled==!inside)return;
            sun.enabled=!inside;RenderSettings.ambientLight=inside?IndoorAmbient:outdoorAmbient;
        }
        void LateUpdate()
        {
            if(worldCamera==null)return;
            if(root!=null&&!worldCamera.orthographic&&root.GetComponent<SceneRenderBudget>()==null){var budget=root.AddComponent<SceneRenderBudget>();budget.world=this;}
            ApplyDaylight(Viewer.position);
            foreach(var label in labels)if(label!=null){var renderer=label.GetComponent<Renderer>();if(renderer.enabled&&!renderer.forceRenderingOff)label.transform.rotation=Quaternion.LookRotation(worldCamera.transform.position-label.transform.position);}
            if(worldCamera.orthographic&&cityMarkers.Count>0)UpdateMapMarkers();
        }
        void SetupLight(Color ambient,Color sunColor)
        {
            RenderSettings.ambientLight=ambient;RenderSettings.fog=true;RenderSettings.fogColor=new Color(.54f,.67f,.72f);RenderSettings.fogDensity=.004f;
            outdoorAmbient=ambient;
            var light=new GameObject("Sun").AddComponent<Light>();sun=light;light.transform.SetParent(root.transform,false);light.type=LightType.Directional;light.color=sunColor;light.intensity=1.25f;light.transform.rotation=Quaternion.Euler(48,-28,0);light.shadows=LightShadows.Soft;
        }
        Vector3 MapCityPosition(CityInfo city,int era)
        {
            var position=city.position;
            position.y=.9f;
            return position;
        }
        void GeographicTerrain()
        {
            var asset=Resources.Load<TextAsset>("Geo/NortheastAsia");
            if(asset==null){Debug.LogError("Northeast Asia map asset missing");return;}
            using(var input=new BinaryReader(new MemoryStream(asset.bytes)))
            {
                if(new string(input.ReadChars(4))!="PTGM")throw new InvalidDataException("Map header");
                int groups=input.ReadInt32();
                for(int group=0;group<groups;group++)
                {
                    int id=input.ReadByte(),vertexCount=input.ReadInt32(),indexCount=input.ReadInt32();
                    var vertices=new Vector3[vertexCount];
                    float elevation=id==0?.55f:.28f;
                    for(int i=0;i<vertexCount;i++)vertices[i]=new Vector3(input.ReadSingle(),elevation,input.ReadSingle());
                    var triangles=new int[indexCount];for(int i=0;i<indexCount;i++)triangles[i]=(int)input.ReadUInt32();
                    var mesh=new Mesh();mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();
                    var land=new GameObject(new[]{"한반도 · 실제 해안선","중국 · 배경","러시아 · 배경","일본 · 배경"}[id]);land.transform.SetParent(root.transform,false);
                    land.AddComponent<MeshFilter>().sharedMesh=mesh;
                    Color shade=id==0?new Color(.32f,.49f,.39f):id==1?new Color(.24f,.33f,.32f):id==2?new Color(.22f,.32f,.34f):new Color(.27f,.36f,.36f);
                    land.AddComponent<MeshRenderer>().sharedMaterial=Mat("geo-land-"+id,shade);
                }
            }
        }
        void UpdateMapMarkers()
        {
            for(int i=0;i<cityMarkers.Count;i++)
            {
                var city=markerCities[i];
                bool show=city.featured||city.id==selectedMarkerId||worldCamera.orthographicSize<46f;
                if(show)
                {
                    var viewport=worldCamera.WorldToViewportPoint(cityMarkers[i].transform.position);
                    show=viewport.z>0&&viewport.x>-.03f&&viewport.x<1.03f&&viewport.y>-.03f&&viewport.y<1.03f;
                }
                if(cityMarkers[i].activeSelf!=show)cityMarkers[i].SetActive(show);
            }
            // Close in, the city plots give way to the subway network underneath; markers shrink with the zoom.
            float zoom=worldCamera.orthographicSize;
            bool close=zoom<=24f&&zoom>=MetroZoom;
            foreach(var building in mapBuildings)if(building!=null&&building.activeSelf!=close)building.SetActive(close);
            var markerScale=Vector3.one*Mathf.Clamp(zoom/30f,.07f,1f);
            foreach(var marker in cityMarkers)marker.transform.localScale=markerScale;
            UpdateNetworkWidths();
        }
        public const float MetroZoom=7f;
        void Line(Vector3 a,Vector3 b,float width,Material material,string name)
        {var mid=(a+b)*.5f;var o=Primitive(PrimitiveType.Cylinder,name,root.transform,mid,new Vector3(width,Vector3.Distance(a,b)*.5f,width),material);o.transform.up=(b-a).normalized;DestroyImmediate(o.GetComponent<Collider>());}
        void Label(string text,Vector3 position,float size,Color color,Transform parent)
        {
            var go=new GameObject(text+" label");go.transform.SetParent(parent,false);go.transform.localPosition=position;
            var tm=go.AddComponent<TextMesh>();tm.text=text;tm.fontSize=50;tm.characterSize=size/50f;tm.anchor=TextAnchor.MiddleCenter;tm.alignment=TextAlignment.Center;tm.color=color;
            if(mapLabelFont==null)mapLabelFont=Font.CreateDynamicFontFromOSFont(new[]{"Apple SD Gothic Neo","Malgun Gothic","Noto Sans CJK KR","Arial Unicode MS"},50);tm.font=mapLabelFont;
            if(tm.font!=null)go.GetComponent<MeshRenderer>().sharedMaterial=tm.font.material;
            labels.Add(tm);
        }
        public void BuildMap(GameState state)
        {
            Clear();SetupLight(new Color(.48f,.56f,.56f),new Color(1f,.89f,.73f));
            RenderSettings.fog=false;
            Primitive(PrimitiveType.Cube,"Sea",root.transform,new Vector3(0,-.5f,0),new Vector3(900,.4f,900),Mat("sea",new Color(.07f,.19f,.23f),.08f));
            GeographicTerrain();selectedMarkerId=state.selectedCity;
            foreach(var city in GameContent.Cities)
            {
                bool selected=city.id==state.selectedCity;
                if(city.kind!="city"&&!selected)continue;
                var marker=new GameObject("City: "+city.name);marker.transform.SetParent(root.transform,false);marker.transform.position=MapCityPosition(city,state.era);
                marker.AddComponent<CityMarker>().cityId=city.id;
                Primitive(PrimitiveType.Cylinder,"Marker base",marker.transform,Vector3.zero,new Vector3(selected?1.45f:city.featured?1f:.65f,.12f,selected?1.45f:city.featured?1f:.65f),Mat("citybase",new Color(.08f,.17f,.17f),.2f));
                Primitive(PrimitiveType.Sphere,"Amber marker",marker.transform,new Vector3(0,.46f,0),Vector3.one*(selected?1.05f:city.featured?.7f:.42f),Mat("amber",new Color(.95f,.7f,.35f),.2f));
                cityMarkers.Add(marker);markerCities.Add(city);
            }
            foreach(var island in new object[][]{new object[]{"울릉도",130.90f,37.50f},new object[]{"독도",131.862f,37.242f}})
            {
                var position=GeoProjection.ToWorld((float)island[1],(float)island[2],.85f);
                Primitive(PrimitiveType.Cylinder,(string)island[0]+" 위치 표식",root.transform,position,new Vector3(.65f,.08f,.65f),Mat("island-marker",new Color(.95f,.7f,.35f)));
            }
            foreach(var built in state.buildings)
            {
                var city=GameContent.City(built.city);var info=GameContent.Building(built.id);if(info==null)continue;
                int index=state.buildings.FindAll(b=>b.city==built.city).IndexOf(built);
                Vector3 p=MapCityPosition(city,state.era)+new Vector3((index%6-2.5f)*2.1f,0,(index/6-1.5f)*2.1f);
                int before=root.transform.childCount;CreateHistoricBuilding(info.unlock,p,info.id,MapModelScale);
                for(int child=before;child<root.transform.childCount;child++)mapBuildings.Add(root.transform.GetChild(child).gameObject);
            }
            var selectedCity=GameContent.City(state.selectedCity);
            var centre=MapCityPosition(selectedCity,state.era);
            for(int slot=0;slot<24;slot++)
            {
                Vector3 p=centre+new Vector3((slot%6-2.5f)*2.1f,-.25f,(slot/6-1.5f)*2.1f);
                mapBuildings.Add(Primitive(PrimitiveType.Cube,"건설 부지 "+(slot+1),root.transform,p,new Vector3(1.95f,.07f,1.95f),Mat("plot",new Color(.18f,.29f,.29f))));
            }
            foreach(var route in state.routes)
            {
                var a=MapCityPosition(GameContent.City(route.from),state.era)+Vector3.up*.8f;var b=MapCityPosition(GameContent.City(route.to),state.era)+Vector3.up*.8f;
                if(route.from==route.to)b=a+new Vector3(3,0,3);
                var color=route.type=="ktx"?new Color(.37f,.69f,.97f):route.type=="metro"?new Color(.93f,.73f,.46f):route.type=="brt"?new Color(.94f,.32f,.31f):new Color(.25f,.8f,.46f);
                Line(a,b,route.type=="ktx"?.22f:route.type=="metro"?.15f:.09f,Mat(route.type,color),route.name);
            }
            worldCamera.orthographic=true;worldCamera.orthographicSize=75f;
            worldCamera.transform.position=new Vector3(0,155,-50);worldCamera.transform.LookAt(Vector3.zero);worldCamera.clearFlags=CameraClearFlags.SolidColor;worldCamera.backgroundColor=new Color(.07f,.19f,.23f);
            BuildNetworkLayer();
            UpdateMapMarkers();
        }
        // Blender-made models from AssetSources/Blender/make_city_models.py, keeping their material colours.
        public GameObject CityModel(string id,Vector3 position,float scale=1f,float yaw=0f)
        {
            var prefab=Resources.Load<GameObject>("Models/City/"+id);
            if(prefab==null)return null;
            var instance=Instantiate(prefab,root.transform);instance.name=id+" model";
            if(id=="Airplane"||id=="Metro"||id=="Ktx"||id.StartsWith("Bus")||id.StartsWith("Car"))instance.AddComponent<RenderMovingRoot>();
            instance.transform.position=position;
            // At yaw 0 every model faces +Z, as RailVehicle and PlaneFlight expect (LookRotation of the travel direction).
            instance.transform.rotation=Quaternion.Euler(0,yaw+ModelFrontYaw,0)*prefab.transform.localRotation;
            instance.transform.localScale=prefab.transform.localScale*scale;
            foreach(var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var source=renderer.sharedMaterials;var result=new Material[source.Length];
                for(int i=0;i<source.Length;i++)
                {
                    string key=source[i]!=null?source[i].name:"default";
                    Color c=source[i]!=null&&source[i].HasProperty("_Color")?source[i].color:Color.gray;
                    bool vehicleGlass=(id.StartsWith("Bus")||id=="Metro"||id=="Ktx"||id.StartsWith("Car"))&&(key=="glass"||key=="vglass");
                    result[i]=vehicleGlass?VehicleGlass():Mat("city-"+key,c,key=="metal"||key=="chrome"||key=="darkmetal"?.45f:0,key);
                }
                renderer.sharedMaterials=result;
            }
            if(id=="Tree"||id=="Pine")
            {
                // FBX roots carry an axis conversion and scale; a collider on that root
                // becomes much wider than the visible trunk. Keep its physics in world metres.
                var trunkObject=new GameObject("Tree trunk collider");
                trunkObject.transform.SetParent(root.transform,false);
                trunkObject.transform.position=position+Vector3.up*1.08f;
                var trunk=trunkObject.AddComponent<CapsuleCollider>();
                trunk.height=2.16f;trunk.radius=.16f;
                trunkObject.AddComponent<StreetFurnitureCollider>().visual=instance;
            }
            return instance;
        }
        void DressRoad(GameObject segment,int axis)
        {
            var p=segment.transform.position;
            // Build each entire grid link at its actual length. The old fixed FBX road
            // covered only part of a link and hid the clickable asphalt underneath.
            float length=axis==0?PlotNodeX-.2f:PlotNodeZ-.2f;
            var paving=Mat("plot-sidewalk",new Color(.63f,.63f,.58f),0,"paving",3f);
            var paint=Mat("plot-lane-paint",new Color(.88f,.79f,.52f));
            for(int sign=-1;sign<=1;sign+=2)
            {
                var pavementOffset=axis==0?new Vector3(0,0,4.05f*sign):new Vector3(4.05f*sign,0,0);
                var sidewalk=Primitive(PrimitiveType.Cube,"인도",root.transform,p+pavementOffset+Vector3.up*.04f,axis==0?new Vector3(length,.12f,.85f):new Vector3(.85f,.12f,length),paving);
                DestroyImmediate(sidewalk.GetComponent<Collider>());
            }
            for(float offset=-length*.5f+1.5f;offset<length*.5f-1f;offset+=3.5f)
            {
                var along=axis==0?new Vector3(offset,0,0):new Vector3(0,0,offset);
                var stripe=Primitive(PrimitiveType.Cube,"중앙 차선",root.transform,p+along+Vector3.up*.052f,axis==0?new Vector3(1.65f,.01f,.08f):new Vector3(.08f,.01f,1.65f),paint);
                DestroyImmediate(stripe.GetComponent<Collider>());
            }
            var side=axis==0?new Vector3(0,0,4.2f):new Vector3(4.2f,0,0);
            MakeLampInteractive(CityModel("StreetLight",new Vector3(p.x,.06f,p.z)+side,.8f,axis==0?180:270));
        }
        const float ModelFrontYaw=180f; // FBX export maps Blender -Y facades to Unity +Z; turn them toward -Z
        const int PlotMaxCars=24,PlotMaxPeople=24,DistrictCars=70,DistrictPeople=80,DistrictBuses=6,MaxStreetTrees=450,MaxStreetLights=220;
        const float MinTreeSpacing=18f,PlotNodeX=18f,PlotNodeZ=18f;
        static readonly string[] CarModels={"Car","CarRed","CarWhite","CarBlack","CarBlue","Bus"};
        // Plot road nodes sit on the grid between parcels: node (i,j) at ((i-3)*14, (j-2)*13.5).
        static TrafficGraph CityRoadGraph(GameState state,string city)
        {
            var graph=new TrafficGraph();
            foreach(var road in state.roads)
            {
                if(road.city!=city)continue;
                var a=new Vector3((road.column-3)*PlotNodeX,0,(road.row-2)*PlotNodeZ);
                var b=road.axis==0?a+new Vector3(PlotNodeX,0,0):a+new Vector3(0,0,PlotNodeZ);
                graph.Link(a,b,7.2f);
            }
            return graph;
        }
        // Vehicles plus signal poles at every junction of three or more roads; returns the director.
        public TrafficDirector SpawnTraffic(TrafficGraph graph,int count,float height,float speed,float scale)
        {
            var starts=new List<int>();
            for(int i=0;i<graph.nodes.Count;i++)foreach(int j in graph.links[i])if(!graph.BusOnly(i,j)){starts.Add(i);break;}
            if(starts.Count==0)return null;
            var director=new GameObject("Traffic").AddComponent<TrafficDirector>();director.transform.SetParent(root.transform,false);
            director.Init(graph,SignalMat("red",new Color(.95f,.12f,.08f)),SignalMat("yellow",new Color(1f,.75f,.1f)),SignalMat("green",new Color(.15f,.95f,.35f)));
            for(int node=0;node<graph.nodes.Count;node++)
            {
                if(!graph.IsSignal(node))continue;
                foreach(int from in graph.links[node])
                {
                    var travel=(graph.nodes[node]-graph.nodes[from]).normalized;
                    var right=Vector3.Cross(Vector3.up,travel);
                    var pole=graph.nodes[node]-travel*(graph.JunctionRadius(node)+.8f)+right*(graph.Width(node,from)*.5f+.8f);
                    var pole3=new Vector3(pole.x,graph.nodes[node].y+height,pole.z);
                    if(CityModel("TrafficLight",pole3,scale,Mathf.Atan2(-right.x,-right.z)*Mathf.Rad2Deg)==null)continue;
                    var lamp=GameObject.CreatePrimitive(PrimitiveType.Sphere);lamp.name="Signal lamp";
                    DestroyImmediate(lamp.GetComponent<Collider>());
                    lamp.transform.SetParent(root.transform,false);
                    lamp.transform.position=pole3-right*2.5f*scale+Vector3.up*4.45f*scale;lamp.transform.localScale=Vector3.one*.42f*scale;
                    director.AddLamp(lamp.GetComponent<Renderer>(),node,graph.Group(node,from));
                }
            }
            var random=new System.Random(starts.Count*31+count);
            for(int k=0;k<count;k++)
            {
                string id=CarModels[k%CarModels.Length];
                var vehicle=CityModel(id,Vector3.zero,id=="Bus"?1f:scale);
                if(vehicle==null)break;
                MakeVehicleInteractive(vehicle,id=="Bus");
                var driver=vehicle.AddComponent<TrafficVehicle>();driver.Bus=id=="Bus";
                driver.Begin(graph,starts[random.Next(starts.Count)],speed*(.8f+(float)random.NextDouble()*.4f),height,id=="Bus"?11f:4.2f*scale,random.Next());
                if(driver.Bus){driver.Route="순환";driver.doors=AttachBusCabin(vehicle,k+71);driver.cabin=driver.doors.cabin;RouteSign(vehicle,driver.Route);Sfx.Attach(vehicle,"bus-engine",.35f,30f);}
                director.Add(driver);
            }
            return director;
        }
        Material SignalMat(string name,Color color)
        {
            var m=Mat("signal-"+name,color);
            m.EnableKeyword("_EMISSION");if(m.HasProperty("_EmissionColor"))m.SetColor("_EmissionColor",color*.9f);
            return m;
        }
        // Plain asphalt over crossing lane paint where plot roads meet.
        void PaveJunctions(TrafficGraph graph,float height)
        {
            var asphalt=Mat("plot-junction",new Color(.55f,.57f,.59f),0,"asphalt_generated",3f);
            for(int node=0;node<graph.nodes.Count;node++)
            {
                var links=graph.links[node];
                if(links.Count<2)continue;
                bool straight=links.Count==2&&Vector3.Dot((graph.nodes[links[0]]-graph.nodes[node]).normalized,(graph.nodes[links[1]]-graph.nodes[node]).normalized)<-.99f;
                if(straight)continue;
                var patch=GameObject.CreatePrimitive(PrimitiveType.Cube);patch.name="Junction";
                DestroyImmediate(patch.GetComponent<Collider>());
                patch.transform.SetParent(root.transform,false);
                float diameter=graph.JunctionRadius(node)*2f;
                patch.transform.position=graph.nodes[node]+Vector3.up*height;patch.transform.localScale=new Vector3(diameter,.02f,diameter);
                patch.GetComponent<Renderer>().sharedMaterial=asphalt;
            }
        }
        // Street trees, lamps and moving traffic along the OSM road centre lines of a Seoul district.
        void DressDistrict(string district)
        {
            var text=Resources.Load<TextAsset>("Geo/"+district+"Roads");
            if(text==null)return;
            var graph=TrafficGraph.FromRoadText(text.text);
            districtGraph=graph;
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())
                if(renderer.gameObject.name.ToLowerInvariant().Contains("roof"))renderer.gameObject.AddComponent<MeshCollider>();
            Physics.SyncTransforms();
            float total=0;
            for(int i=0;i<graph.nodes.Count;i++)foreach(int j in graph.links[i])if(j>i)total+=Vector3.Distance(graph.nodes[i],graph.nodes[j]);
            float spacing=Mathf.Max(MinTreeSpacing,total*2f/MaxStreetTrees);
            int trees=0,lamps=0;
            for(int i=0;i<graph.nodes.Count;i++)foreach(int j in graph.links[i])
            {
                if(j<i)continue;
                var a=graph.nodes[i];var b=graph.nodes[j];float length=Vector3.Distance(a,b);
                // Bridges and bus lanes get no street trees; trees stand behind the sidewalk, lamps at the kerb.
                if(a.y>.5f||b.y>.5f||graph.BusOnly(i,j))continue;
                var direction=(b-a)/Mathf.Max(length,.01f);var normal=Vector3.Cross(Vector3.up,direction);float half=graph.Width(i,j)*.5f;
                float sidewalk=half*2>=13?3.5f:half*2>=8?2.5f:1.6f;
                for(float along=spacing*.5f;along<length-2f;along+=spacing)
                {
                    foreach(float side in new[]{1f,-1f})
                    {
                        var p=a+direction*along+normal*side*(half+sidewalk+.9f);
                        if(trees<MaxStreetTrees&&!UnderRoof(p)&&!OnRoad(graph,p)){CityModel((i+(int)along)%3==0?"Pine":"Tree",p,.9f);trees++;}
                    }
                    var lamp=a+direction*along+normal*(half+.35f);
                    if(lamps<MaxStreetLights&&!UnderRoof(lamp)){MakeLampInteractive(CityModel("StreetLight",lamp,1f,Mathf.Atan2(-normal.x,-normal.z)*Mathf.Rad2Deg));lamps++;}
                }
            }
            SpawnTraffic(graph,DistrictCars,.06f,9f,1f);
            if(graph.HasBusLanes)SpawnBuses(graph,DistrictBuses);
            // People walk the sidewalk network (kerbside pavements, zebra crossings, footways).
            var walks=Resources.Load<TextAsset>("Geo/"+district+"Walks");
            if(walks!=null)
            {
                var director=new GameObject("Pedestrians").AddComponent<TrafficDirector>();director.transform.SetParent(root.transform,false);
                walkGraph=TrafficGraph.FromRoadText(walks.text);
                director.Init(walkGraph,null,null,null);
                SpawnPeople(director,DistrictPeople,.14f,0f,1f);
            }
        }
        TrafficGraph walkGraph;
        // The nearest point on the district's pavement network, for stepping off a bus or car; p if there is none.
        public Vector3 NearestPavement(Vector3 p)
        {
            if(walkGraph==null)return p;
            var best=p;float nearest=float.MaxValue;
            for(int i=0;i<walkGraph.nodes.Count;i++)foreach(int j in walkGraph.links[i])
            {
                if(j<i)continue;
                Vector3 a=walkGraph.nodes[i],ab=walkGraph.nodes[j]-a;
                var q=a+ab*(ab.sqrMagnitude>1e-4f?Mathf.Clamp01(Vector3.Dot(p-a,ab)/ab.sqrMagnitude):0);
                if((q-p).sqrMagnitude<nearest){nearest=(q-p).sqrMagnitude;best=q;}
            }
            return best;
        }
        static bool OnRoad(TrafficGraph graph,Vector3 p)
        {
            for(int i=0;i<graph.nodes.Count;i++)foreach(int j in graph.links[i])
            {
                if(j<i)continue;var a=graph.nodes[i];var ab=graph.nodes[j]-a;
                float t=Mathf.Clamp01(Vector3.Dot(p-a,ab)/Mathf.Max(.01f,ab.sqrMagnitude));
                if(Vector3.Distance(new Vector3(p.x,0,p.z),new Vector3(a.x+ab.x*t,0,a.z+ab.z*t))<graph.Width(i,j)*.5f+.5f)return true;
            }
            return false;
        }
        // Blue trunk buses that prefer the median bus lanes and stop at the mapped stops.
        void SpawnBuses(TrafficGraph graph,int count)
        {
            var director=root.GetComponentInChildren<TrafficDirector>();if(director==null)return;
            var starts=new List<int>();
            for(int i=0;i<graph.nodes.Count;i++)foreach(int j in graph.links[i])if(graph.BusOnly(i,j)&&graph.Allowed(i,j)){starts.Add(i);break;}
            var random=new System.Random(77);
            for(int k=0;k<count&&starts.Count>0;k++)
            {
                var bus=CityModel("BusBlue",Vector3.zero);if(bus==null)bus=CityModel("Bus",Vector3.zero);if(bus==null)return;
                bus.name="BRT 간선버스";MakeVehicleInteractive(bus,true);
                var driver=bus.AddComponent<TrafficVehicle>();driver.Bus=true;
                driver.Begin(graph,starts[random.Next(starts.Count)],8f,.06f,11f,random.Next());
                driver.Route="BRT";driver.doors=AttachBusCabin(bus,77+k);driver.cabin=driver.doors.cabin;RouteSign(bus,driver.Route);Sfx.Attach(bus,"bus-engine",.4f,35f);
                director.Add(driver);
            }
        }
        // OSM surface meshes made by build_seoul_osm.py other than roads, buildings and roofs.
        Material DistrictSurface(string n)
        {
            switch(n)
            {
                case "facadeframe":return Mat("osm-facade-frame",new Color(.30f,.33f,.35f),.35f);
                case "facadetrim":return Mat("osm-facade-trim",new Color(.68f,.69f,.66f));
                case "facadeglazing":return Mat("osm-facade-glazing",new Color(.18f,.31f,.35f),.65f);
                case "facadeglazinglight":return Mat("osm-facade-glazing-light",new Color(.34f,.47f,.48f),.5f);
                case "facadeshop":return Mat("osm-facade-shop",new Color(.13f,.20f,.22f),.3f);
                case "facadesign":return Mat("osm-facade-sign",new Color(.18f,.23f,.25f));
                case "facadeawningred":return Mat("osm-facade-awning-red",new Color(.48f,.15f,.12f));
                case "facadeawningcream":return Mat("osm-facade-awning-cream",new Color(.75f,.68f,.53f));
                case "facadesoffit":return Mat("osm-facade-soffit",new Color(.72f,.74f,.72f),.2f);
                case "facadesteel":return Mat("osm-facade-steel",new Color(.54f,.59f,.60f),.65f);
                case "buildinggrey":return Mat("osm-wall-grey",new Color(.65f,.64f,.60f),0,"concrete",1f);
                case "buildingwhite":return Mat("osm-wall-white",new Color(.83f,.83f,.80f),0,"concrete",1f);
                case "buildingbeige":return Mat("osm-wall-beige",new Color(.73f,.66f,.53f),0,"concrete",1f);
                case "buildingbrick":return Mat("osm-wall-brick",new Color(.52f,.27f,.20f));
                case "buildingglass":return Mat("osm-wall-glass",new Color(.23f,.33f,.36f),.4f);
                case "busway":return Mat("osm-busway",new Color(.62f,.24f,.20f),0,"asphalt",1f);
                case "sidewalk":return Mat("osm-sidewalk",Color.white,0,"paving",1f);
                case "curb":return Mat("osm-curb",new Color(.72f,.72f,.69f));
                case "bridge":return Mat("osm-bridge",new Color(.64f,.64f,.61f),0,"concrete",1f);
                case "railing":return Mat("osm-railing",new Color(.38f,.42f,.45f),.5f);
                case "platform":return Mat("osm-platform",new Color(.74f,.73f,.69f),0,"concrete",1f);
                case "platformedge":return Mat("osm-platformedge",new Color(.95f,.78f,.15f));
                case "canopy":return Mat("osm-canopy",new Color(.86f,.88f,.89f),.2f);
                case "ballast":return Mat("osm-ballast",new Color(.40f,.38f,.35f),0,"asphalt",1f);
                case "sleeper":return Mat("osm-sleeper",new Color(.52f,.51f,.48f));
                case "rail":return Mat("osm-rail",new Color(.72f,.74f,.76f),.6f);
                case "apron":return Mat("osm-apron",new Color(.68f,.68f,.65f),0,"concrete",1f);
                case "taxiway":return Mat("osm-taxiway",new Color(.56f,.56f,.54f),0,"concrete",1f);
                case "taxiline":return Mat("osm-taxiline",new Color(.95f,.78f,.12f));
            }
            return null;
        }
        static bool UnderRoof(Vector3 p){return Physics.Raycast(p+Vector3.up*300f,Vector3.down,299.9f);}
        static string EraModelId(int era){return era<1?"camp":era<3?"pit-house":era<7?"hanok":era<9?"house":"apartment";}
        GameObject CreateHistoricBuilding(int era,Vector3 p,string id,float scale=1f)
        {
            var made=CityModel(Resources.Load<GameObject>("Models/City/"+id)!=null?id:EraModelId(era),p,scale);
            if(made!=null)
            {
                foreach(var filter in made.GetComponentsInChildren<MeshFilter>())
                    if(filter.sharedMesh!=null&&filter.sharedMesh.isReadable&&filter.GetComponent<MeshCollider>()==null)
                        filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
                return made;
            }
            if(id=="apartment"||id=="house"||id=="wind"||id=="nuclear"||id=="coal-power"||id=="oil-power")
            {
                var model=new GameObject(id);model.transform.SetParent(root.transform,false);model.transform.position=p;
                if(id=="apartment")
                {
                    Primitive(PrimitiveType.Cube,"아파트 동",model.transform,new Vector3(0,5,0),new Vector3(3.3f,10,2.5f),Mat("apartment-wall",new Color(.74f,.75f,.70f)));
                    for(int floor=1;floor<=9;floor++)for(int column=-1;column<=1;column++)
                        Primitive(PrimitiveType.Cube,"창",model.transform,new Vector3(column*.9f,floor+.15f,-1.26f),new Vector3(.5f,.42f,.06f),Mat("apartment-glass",new Color(.19f,.30f,.35f),.2f));
                    Primitive(PrimitiveType.Cube,"옥상",model.transform,new Vector3(0,10.15f,0),new Vector3(3.5f,.3f,2.7f),Mat("apartment-roof",new Color(.29f,.34f,.35f)));
                }
                else if(id=="house")
                {
                    Primitive(PrimitiveType.Cube,"주택 본채",model.transform,new Vector3(0,1.45f,0),new Vector3(3,2.9f,2.5f),Mat("house-wall",new Color(.75f,.68f,.59f)));
                    var roof=Primitive(PrimitiveType.Cube,"경사지붕",model.transform,new Vector3(0,3.08f,0),new Vector3(3.5f,.4f,2.9f),Mat("house-roof",new Color(.29f,.31f,.33f)));roof.transform.localRotation=Quaternion.Euler(0,0,8);
                    Primitive(PrimitiveType.Cube,"창",model.transform,new Vector3(-.7f,1.55f,-1.27f),new Vector3(.8f,.8f,.05f),Mat("house-glass",new Color(.21f,.38f,.45f),.1f));
                }
                else if(id=="wind")
                {
                    Primitive(PrimitiveType.Cylinder,"풍력 타워",model.transform,new Vector3(0,3.5f,0),new Vector3(.32f,3.5f,.32f),Mat("wind-tower",new Color(.86f,.88f,.85f)));
                    Primitive(PrimitiveType.Sphere,"허브",model.transform,new Vector3(0,7,0),new Vector3(.65f,.65f,.65f),Mat("wind-hub",new Color(.92f,.93f,.90f)));
                    for(int blade=0;blade<3;blade++){var part=Primitive(PrimitiveType.Cube,"날개",model.transform,new Vector3(0,7,0),new Vector3(.2f,3.8f,.12f),Mat("wind-blade",new Color(.87f,.89f,.86f)));part.transform.localRotation=Quaternion.Euler(0,0,blade*120f);part.transform.localPosition+=part.transform.up*1.8f;}
                }
                else
                {
                    Primitive(PrimitiveType.Cube,"발전 시설",model.transform,new Vector3(0,1.5f,0),new Vector3(3.5f,3,2.8f),Mat("power-wall",new Color(.56f,.61f,.62f)));
                    if(id=="nuclear")Primitive(PrimitiveType.Sphere,"원자로 돔",model.transform,new Vector3(0,3.15f,0),new Vector3(2.7f,1.5f,2.7f),Mat("reactor-dome",new Color(.84f,.85f,.82f)));
                    else for(int stack=0;stack<2;stack++)Primitive(PrimitiveType.Cylinder,"배기 굴뚝",model.transform,new Vector3(-.8f+stack*1.6f,4.15f,.65f),new Vector3(.35f,2f,.35f),Mat("power-stack",new Color(.44f,.37f,.34f)));
                }
                return null;
            }
            if(era<7)
            {
                var model=Resources.Load<GameObject>(era<3?"Models/Dwelling":"Models/Hanok");
                if(model!=null){var instance=Instantiate(model,p,Quaternion.identity,root.transform);instance.name=id+" model";TintModel(instance);return null;}
            }
            var parent=new GameObject(id);parent.transform.SetParent(root.transform,false);parent.transform.position=p;
            if(era<3)
            {
                Primitive(PrimitiveType.Cylinder,"Stone foundation",parent.transform,new Vector3(0,.3f,0),new Vector3(1.6f,.3f,1.6f),Mat("stone",new Color(.55f,.55f,.49f)));
                var roof=Primitive(PrimitiveType.Cylinder,"Thatch roof",parent.transform,new Vector3(0,1.8f,0),new Vector3(1.9f,.7f,1.9f),Mat("thatch",new Color(.59f,.45f,.27f)));roof.transform.localRotation=Quaternion.Euler(0,0,180);
            }
            else if(era<7)
            {
                Primitive(PrimitiveType.Cube,"Timber body",parent.transform,new Vector3(0,1,0),new Vector3(2.7f,1.8f,2.4f),Mat("timber",new Color(.53f,.32f,.19f)));
                var roof=Primitive(PrimitiveType.Cube,"Tiled roof",parent.transform,new Vector3(0,2.1f,0),new Vector3(3.2f,.35f,2.8f),Mat("tile",new Color(.18f,.24f,.26f)));
                roof.transform.rotation=Quaternion.Euler(0,0,8);
            }
            else
            {
                var h=era>=9?4.3f:3.2f;Primitive(PrimitiveType.Cube,"Industrial body",parent.transform,new Vector3(0,h*.5f,0),new Vector3(3,h,2.5f),Mat("concrete",new Color(.56f,.62f,.64f)));
                Primitive(PrimitiveType.Cylinder,"Chimney",parent.transform,new Vector3(1,4.1f,.7f),new Vector3(.28f,1.8f,.28f),Mat("chimney",new Color(.49f,.25f,.22f)));
            }
            return null;
        }
        public void BuildDistrict(int index,int era)
        {
            Clear();SetupLight(new Color(.49f,.57f,.64f),new Color(1f,.91f,.77f));
            worldCamera.orthographic=false;
            RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.0018f;
            if(era<9)
            {
                BuildHistoricalDistrict(index,era);
                return;
            }
            string[] models={"GangnamOSM","SeoulStationOSM","HongdaeOSM","GimpoAirportOSM"};
            var prefab=Resources.Load<GameObject>("Models/"+models[Mathf.Clamp(index,0,3)]);
            if(prefab!=null)
            {
                // The Blender XY map plane imports as a vertical plane in this FBX.
                var district=Instantiate(prefab,Vector3.zero,Quaternion.Euler(-90,0,0),root.transform);
                district.name=models[index]+" - OpenStreetMap geometry";
                foreach(var renderer in district.GetComponentsInChildren<Renderer>())
                {
                    string n=renderer.gameObject.name.ToLowerInvariant();
                    renderer.shadowCastingMode=n.StartsWith("building")||n.StartsWith("facade")||n.Contains("roof")?UnityEngine.Rendering.ShadowCastingMode.On:UnityEngine.Rendering.ShadowCastingMode.Off;
                    if(n.Contains("ground")){renderer.enabled=false;continue;}
                    Material special=DistrictSurface(n);
                    if(special!=null){renderer.sharedMaterial=special;continue;}
                    Color c=n.Contains("footway")?new Color(.56f,.57f,.53f):
                        n.Contains("road")?new Color(.17f,.20f,.23f):
                        n.Contains("roof")?new Color(.29f,.34f,.37f):
                        n.Contains("building")?new Color(.62f,.65f,.64f):
                        new Color(.37f,.46f,.37f);
                    string texture=n.Contains("footway")?"paving":n.Contains("junction")?"junction":n.Contains("crosswalk")?null:n.Contains("road")?"osm_road":
                        n.Contains("roof")?"roof_flat":n.StartsWith("building")&&n.Length>"building".Length?"facade_"+n.Substring("building".Length):n.Contains("building")?"facade":null;
                    if(n.Contains("crosswalk"))c=new Color(.92f,.92f,.88f);
                    renderer.sharedMaterial=Mat("osm-"+n,texture!=null?Color.white:c,0,texture);
                }
                Primitive(PrimitiveType.Cube,"District ground",root.transform,new Vector3(0,-.16f,0),new Vector3(650,.25f,650),Mat("district-ground",new Color(.58f,.57f,.54f),0,"concrete",160f));
                // Surrounding land so the horizon is not the bare sky colour.
                var outer=Primitive(PrimitiveType.Cube,"Surrounding land",root.transform,new Vector3(0,-.3f,0),new Vector3(2400,.2f,2400),Mat("outer-ground",new Color(.36f,.42f,.38f),0,"grass",400f));
                DestroyImmediate(outer.GetComponent<Collider>());
                DressDistrict(models[index].Replace("OSM",""));
                // Walkable surfaces: bridges, ramps and platforms carry the walking camera (after tree placement,
                // which ray casts for roofs).
                foreach(var filter in district.GetComponentsInChildren<MeshFilter>())
                {
                    string n=filter.gameObject.name.ToLowerInvariant();
                    if(n=="road"||n=="busway"||n=="sidewalk"||n=="footway"||n=="bridge"||n=="platform"||n=="junction"||n.StartsWith("building")||n.StartsWith("roof")||n=="facadesteel"||n=="facadesoffit"||n.StartsWith("facadeawning"))
                        filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
                }
                BuildMappedInfrastructure(models[index].Replace("OSM",""));
                SetDistrictView(index,true);
                vehicle=CreateVehicle(index==3?"bus":"metro",new Vector3(0,.8f,8));vehicle.SetActive(false);
                return;
            }
            var palettes=new[]{new Color(.75f,.67f,.54f),new Color(.57f,.68f,.74f),new Color(.73f,.52f,.61f),new Color(.62f,.71f,.65f)};
            var baseColor=palettes[Mathf.Clamp(index,0,3)];
            Primitive(PrimitiveType.Cube,"District ground",root.transform,new Vector3(0,-.15f,0),new Vector3(140,.3f,140),Mat("ground",new Color(.35f,.43f,.39f)));
            for(int r=-2;r<=2;r++)
            {
                Primitive(PrimitiveType.Cube,"Road north-south",root.transform,new Vector3(r*18,.015f,0),new Vector3(5,.04f,140),Mat("road",new Color(.17f,.21f,.24f)));
                Primitive(PrimitiveType.Cube,"Road east-west",root.transform,new Vector3(0,.02f,r*18),new Vector3(140,.04f,5),Mat("road",new Color(.17f,.21f,.24f)));
                for(int k=-3;k<=3;k++)
                {Primitive(PrimitiveType.Cube,"Lane marking",root.transform,new Vector3(r*18,.05f,k*18+6),new Vector3(.1f,.015f,3),Mat("lane",new Color(.91f,.82f,.59f)));}
            }
            var rand=new System.Random(91+index*100);
            for(int x=-3;x<=3;x++)for(int z=-3;z<=3;z++)
            {
                if(x==0&&z==0)continue;
                float px=x*18+9,pz=z*18+9;
                if(Mathf.Abs(px)<4||Mathf.Abs(pz)<4)continue;
                float height=index==0?11+rand.Next(0,23):index==3?5+rand.Next(0,6):6+rand.Next(0,13);
                var body=Primitive(PrimitiveType.Cube,"Building "+x+" "+z,root.transform,new Vector3(px,height*.5f,pz),new Vector3(8,height,8),Mat("block"+((x+z+20)%4),Color.Lerp(baseColor,new Color(.31f,.39f,.47f),((x+z+20)%4)*.17f),.08f));
                for(float y=2;y<height-1;y+=2.4f)for(int col=0;col<3;col++)
                {
                    float wx=px-2.5f+col*2.5f;
                    Primitive(PrimitiveType.Cube,"Window",root.transform,new Vector3(wx,y,pz-4.02f),new Vector3(1.1f,1.1f,.04f),Mat((col+y)%3<1?"windowlit":"window",(col+y)%3<1?new Color(.95f,.76f,.44f):new Color(.18f,.32f,.39f),.2f));
                }
                Primitive(PrimitiveType.Cube,"Roof",root.transform,new Vector3(px,height+.15f,pz),new Vector3(8.4f,.3f,8.4f),Mat("roof",new Color(.24f,.28f,.31f)));
            }
            for(int n=-3;n<=3;n++)
            {
                Primitive(PrimitiveType.Cylinder,"Street tree",root.transform,new Vector3(3.8f,1.5f,n*11),new Vector3(.22f,1.5f,.22f),Mat("treebark",new Color(.35f,.27f,.19f)));
                Primitive(PrimitiveType.Sphere,"Tree crown",root.transform,new Vector3(3.8f,3.3f,n*11),new Vector3(2.3f,2.5f,2.3f),Mat("leaves",new Color(.27f,.43f,.32f)));
            }
            worldCamera.transform.position=new Vector3(0,2.1f,-10);worldCamera.transform.rotation=Quaternion.Euler(0,0,0);worldCamera.fieldOfView=72;
            vehicle=CreateVehicle(index==3?"bus":"metro",new Vector3(0,.8f,8));vehicle.SetActive(false);
        }
        const float RoadHitWidth=7.4f; // clickable width fills the 4-unit gap between parcels
        const float MapModelScale=.2f; // 9.6 m lot model fits the 1.95-unit map plot
        public void SetRoadCandidates(bool visible)
        {
            if(root==null)return;
            foreach(var marker in root.GetComponentsInChildren<RoadMarker>())
            {
                var renderer=marker.GetComponent<Renderer>();
                if(renderer!=null&&marker.gameObject.name.Contains("후보"))renderer.enabled=visible;
            }
        }
        public void BuildCityPlot(GameState state)
        {
            Clear();SetupLight(new Color(.55f,.59f,.58f),new Color(1f,.91f,.76f));
            SetCityView(false);
            var grass=Mat("city-grass",new Color(.28f,.42f,.32f),0,"grass",25f);
            var road=Mat("city-road",new Color(.55f,.57f,.59f),0,"asphalt_generated",3f);
            var parcel=Mat("city-parcel",new Color(.36f,.49f,.39f));
            Primitive(PrimitiveType.Cube,"도시 터",root.transform,new Vector3(0,-.45f,0),new Vector3(130,.8f,110),grass);
            var selected=GameContent.City(state.selectedCity);
            var plannedRoad=Mat("road-planned",new Color(.30f,.36f,.36f));
            for(int row=0;row<=4;row++)for(int column=0;column<6;column++)
            {
                bool placed=state.roads!=null&&state.roads.Exists(r=>r.city==selected.id&&r.axis==0&&r.row==row&&r.column==column);
                var segment=Primitive(PrimitiveType.Cube,placed?"동서 도로":"동서 도로 후보",root.transform,new Vector3((column-2.5f)*PlotNodeX,.09f,(row-2)*PlotNodeZ),new Vector3(PlotNodeX-.2f,.07f,placed?7.2f:1.5f),placed?road:plannedRoad);
                var marker=segment.AddComponent<RoadMarker>();marker.axis=0;marker.row=row;marker.column=column;
                if(placed)DressRoad(segment,0);
                var hitArea=segment.GetComponent<BoxCollider>();hitArea.size=new Vector3(1,1,RoadHitWidth/segment.transform.localScale.z);
            }
            for(int column=0;column<=6;column++)for(int row=0;row<4;row++)
            {
                bool placed=state.roads!=null&&state.roads.Exists(r=>r.city==selected.id&&r.axis==1&&r.row==row&&r.column==column);
                var segment=Primitive(PrimitiveType.Cube,placed?"남북 도로":"남북 도로 후보",root.transform,new Vector3((column-3)*PlotNodeX,.10f,(row-1.5f)*PlotNodeZ),new Vector3(placed?7.2f:1.5f,.07f,PlotNodeZ-.2f),placed?road:plannedRoad);
                var marker=segment.AddComponent<RoadMarker>();marker.axis=1;marker.row=row;marker.column=column;
                if(placed)DressRoad(segment,1);
                var hitArea=segment.GetComponent<BoxCollider>();hitArea.size=new Vector3(RoadHitWidth/segment.transform.localScale.x,1,1);
            }
            var buildings=state.buildings.FindAll(b=>b.city==selected.id);
            int capacity=selected.kind=="town"?12:selected.region=="북한"?18:24;
            for(int slot=0;slot<capacity;slot++)
            {
                float x=(slot%6-2.5f)*PlotNodeX,z=(slot/6-1.5f)*PlotNodeZ;
                Primitive(PrimitiveType.Cube,"부지 "+(slot+1),root.transform,new Vector3(x,.04f,z),new Vector3(10,.08f,10f),parcel);
                if(slot<buildings.Count)
                {
                    var info=GameContent.Building(buildings[slot].id);
                    if(info!=null)
                    {
                        var model=CreateHistoricBuilding(info.unlock,new Vector3(x,.1f,z),info.id);
                        if(System.Array.IndexOf(DoorBuildings,info.id)>=0)AddDoor(model);
                    }
                }
            }
            var plotRoads=CityRoadGraph(state,selected.id);
            PaveJunctions(plotRoads,.115f);
            int plotRoadCount=state.roads.FindAll(r=>r.city==selected.id).Count;
            SpawnPeople(SpawnTraffic(plotRoads,Mathf.Min(2+plotRoadCount/2,PlotMaxCars),.11f,5f,.75f),Mathf.Min(4+plotRoadCount,PlotMaxPeople),.1f,1.05f,.9f);
            for(float x=-46;x<=46;x+=6.5f)foreach(float z in new[]{-33f,33f})CityModel(((int)x)%2==0?"Pine":"Tree",new Vector3(x,0,z+((int)x%3)),.9f+((int)x%4+4)%4*.08f);
            for(float z=-26;z<=26;z+=6.5f)foreach(float x in new[]{-47f,47f})CityModel("Tree",new Vector3(x,0,z),1f);
            Label(selected.name+" · "+buildings.Count+"/"+capacity,new Vector3(0,1.1f,35),2.8f,new Color(.98f,.81f,.52f),root.transform);
            SetRoadCandidates(false);
        }
        // Design view (orthographic, from above) or street view (perspective, eye height, sky behind).
        public void SetCityView(bool street)
        {
            RenderSettings.fog=street;
            worldCamera.orthographic=!street;
            if(street)
            {
                RenderSettings.fogColor=new Color(.62f,.74f,.80f);RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=60;RenderSettings.fogEndDistance=180;
                worldCamera.fieldOfView=60f;worldCamera.backgroundColor=new Color(.55f,.70f,.80f);
                worldCamera.transform.position=new Vector3(-7f,1.7f,-31f);worldCamera.transform.rotation=Quaternion.Euler(4,20,0);
                return;
            }
            worldCamera.orthographicSize=48f;
            worldCamera.transform.position=new Vector3(0,75,-36);
            worldCamera.transform.LookAt(Vector3.zero);
            worldCamera.backgroundColor=new Color(.08f,.20f,.23f);
        }
        public void BuildWorldBackdrop()
        {
            Clear();worldCamera.orthographic=true;worldCamera.orthographicSize=20f;
            worldCamera.transform.position=new Vector3(0,20,-12);worldCamera.transform.LookAt(Vector3.zero);
            worldCamera.clearFlags=CameraClearFlags.SolidColor;
            worldCamera.backgroundColor=new Color(.08f,.15f,.19f);
        }
        public void SetDistrictView(int index,bool aerial)
        {
            aerialDistrict=aerial;
            if(aerial)
            {
                worldCamera.transform.position=new Vector3(0,420,0);
                // North up, east right, as on Naver/Kakao maps: the district world has x = -east, z = -north.
                worldCamera.transform.rotation=Quaternion.LookRotation(Vector3.down,Vector3.back);
                worldCamera.fieldOfView=64;
            }
            else
            {
                Vector3[] spawn={new Vector3(.5f,2.1f,1.5f),new Vector3(-67.5f,2.1f,1.2f),new Vector3(-13.8f,2.1f,-14.8f),new Vector3(-36.6f,2.1f,2.3f)};
                Vector3[] facing={new Vector3(1,0,-.35f),new Vector3(0,0,-1),new Vector3(1,0,-.93f),new Vector3(0,0,1)};
                int tile=Mathf.Clamp(index,0,3);
                var pavement=SafeStreetSpawn(spawn[tile],facing[tile]);
                worldCamera.transform.position=pavement+Vector3.up*1.65f;
                worldCamera.transform.rotation=Quaternion.LookRotation(OpenStreetFacing(pavement,facing[tile]));
                worldCamera.fieldOfView=72;
            }
        }
        Vector3 OpenStreetFacing(Vector3 feet,Vector3 preferred)
        {
            float best=-1;Vector3 chosen=preferred.normalized;
            for(int i=0;i<16;i++)
            {
                var direction=Quaternion.Euler(0,i*22.5f,0)*Vector3.forward;
                float clear=26f;
                foreach(var hit in Physics.RaycastAll(feet+Vector3.up*1.55f,direction,26f,~0,QueryTriggerInteraction.Ignore))
                {
                    var name=hit.collider.name.ToLowerInvariant();
                    if(name.Contains("building")||name.Contains("roof")||name=="벽")clear=Mathf.Min(clear,hit.distance);
                }
                float score=clear+Vector3.Dot(direction,preferred.normalized)*2f;
                if(score>best){best=score;chosen=direction;}
            }
            return chosen;
        }
        public Vector3 SafeStreetSpawn(Vector3 target,Vector3 facing)
        {
            int noGround=0,badSurface=0,blockedSpace=0,blockedView=0;string blocker="";
            // Some mapped footways pass through building footprints. Try the target and nearby open
            // pavement, then validate a human-sized space against the actual scene colliders.
            for(int ring=0;ring<=16;ring++)for(int sample=0;sample<16;sample++)
            {
                float angle=sample*Mathf.PI/8f;
                var nearby=target+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*ring*3f;
                for(int option=0;option<2;option++)
                {
                    var candidate=option==0?nearby:NearestPavement(nearby);
                    RaycastHit ground=default(RaycastHit);bool found=false;
                    var surfaces=Physics.RaycastAll(candidate+Vector3.up*5f,Vector3.down,12f,~0,QueryTriggerInteraction.Ignore);
                    System.Array.Sort(surfaces,(a,b)=>a.distance.CompareTo(b.distance));
                    foreach(var hit in surfaces)
                    {
                        string surface=hit.collider.gameObject.name.ToLowerInvariant();
                        if(surface.StartsWith("building")||surface.Contains("roof")||surface.Contains("지붕")||surface.Contains("canopy")||surface.Contains("trunk")||surface=="road"||surface=="busway"||surface=="junction")continue;
                        if(hit.collider.GetComponentInParent<TrafficVehicle>()!=null||hit.collider.GetComponentInParent<RailVehicle>()!=null||hit.collider.GetComponentInParent<Pedestrian>()!=null||hit.collider.GetComponentInParent<StreetLamp>()!=null)continue;
                        ground=hit;found=true;break;
                    }
                    if(!found){noGround++;continue;}
                    candidate.y=ground.point.y;
                    bool blocked=false;
                    foreach(var obstacle in Physics.OverlapCapsule(candidate+Vector3.up*.45f,candidate+Vector3.up*1.55f,.31f,~0,QueryTriggerInteraction.Ignore))
                        if(obstacle.bounds.max.y>candidate.y+.38f){blocked=true;blocker=obstacle.name;break;}
                    if(blocked){blockedSpace++;continue;}
                    RaycastHit wall;
                    if(Physics.Raycast(candidate+Vector3.up*1.4f,facing.normalized,out wall,1.25f,~0,QueryTriggerInteraction.Ignore)){blockedView++;continue;}
                    return candidate;
                }
            }
            Debug.LogWarning("No unobstructed street spawn near "+target+" ground "+noGround+" surface "+badSurface+" space "+blockedSpace+" view "+blockedView+" last "+blocker);
            return new Vector3(0,.05f,0);
        }
        void BuildHistoricalDistrict(int index,int era)
        {
            aerialDistrict=false;
            var ground=Mat("historic-ground",era<3?new Color(.36f,.46f,.30f):new Color(.42f,.48f,.34f));
            Primitive(PrimitiveType.Cube,"Historic landscape",root.transform,new Vector3(0,-.2f,0),new Vector3(140,.35f,140),ground);
            var path=Mat("earth-path",new Color(.48f,.39f,.27f));
            Primitive(PrimitiveType.Cube,"Unpaved path",root.transform,new Vector3(0,.02f,0),new Vector3(3,.05f,110),path);
            var random=new System.Random(197+index*7+era*29);
            int count=era==0?0:era<3?5:era<7?10:15;
            for(int i=0;i<count;i++)
            {
                float side=i%2==0?-1:1;
                float x=side*(9+random.Next(0,29));
                float z=-42+i*85f/Mathf.Max(1,count);
                CreateHistoricBuilding(era,new Vector3(x,0,z),"시대 주거·생산 시설");
            }
            for(int i=0;i<55;i++)
            {
                float x=random.Next(-62,63),z=random.Next(-62,63);
                if(Mathf.Abs(x)<5)continue;
                Primitive(PrimitiveType.Cylinder,"Tree trunk",root.transform,new Vector3(x,1.7f,z),new Vector3(.22f,1.7f,.22f),Mat("historic-bark",new Color(.32f,.24f,.16f)));
                Primitive(PrimitiveType.Sphere,"Tree crown",root.transform,new Vector3(x,4.1f,z),new Vector3(2.9f,3.8f,2.9f),Mat("historic-leaves",new Color(.20f,.36f,.23f)));
            }
            worldCamera.transform.position=new Vector3(0,2.1f,-37);
            worldCamera.transform.rotation=Quaternion.Euler(0,0,0);
            worldCamera.fieldOfView=72;
        }
        public void BuildGlobe()
        {
            Clear();SetupLight(new Color(.24f,.28f,.37f),Color.white);RenderSettings.fog=false;
            worldCamera.orthographic=false;
            var stars=Mat("space",new Color(.018f,.035f,.065f));
            Primitive(PrimitiveType.Cube,"Space background",root.transform,new Vector3(0,0,75),new Vector3(200,160,1),stars);
            globePivot=new GameObject("Globe pivot").transform;globePivot.SetParent(root.transform,false);globePivot.rotation=Quaternion.Euler(0,127,0);
            var earth=Primitive(PrimitiveType.Sphere,"Earth - NASA Blue Marble",globePivot,Vector3.zero,Vector3.one*40,Mat("earth",Color.white));
            earth.transform.localRotation=Quaternion.Euler(0,77,0);
            var texture=Resources.Load<Texture2D>("BlueMarble");if(texture!=null)earth.GetComponent<Renderer>().sharedMaterial.mainTexture=texture;
            float[] lat={37.5f,40f,35f,36f,38f,55f,51f,22f};
            float[] lon={127f,127f,104f,138f,-98f,38f,10f,78f};
            string[] names={"대한민국","북한","중국","일본","미국","러시아","독일","인도"};
            for(int i=0;i<names.Length;i++)
            {
                float la=lat[i]*Mathf.Deg2Rad,lo=lon[i]*Mathf.Deg2Rad;
                var pos=new Vector3(Mathf.Cos(la)*Mathf.Sin(lo),Mathf.Sin(la),-Mathf.Cos(la)*Mathf.Cos(lo))*20.45f;
                var marker=Primitive(PrimitiveType.Sphere,names[i]+" marker",globePivot,pos,Vector3.one*.75f,Mat("globeMarker",new Color(.97f,.73f,.32f)));
                marker.AddComponent<CountryMarker>().countryIndex=i;
            }
            worldCamera.transform.position=new Vector3(0,7,-58);worldCamera.transform.LookAt(Vector3.zero);worldCamera.fieldOfView=55;
        }
        public GameObject CreateVehicle(string type,Vector3 position)
        {
            bool bus=type=="bus"||type=="brt";
            var cityVehicle=CityModel(bus?"Bus":"Metro",position);
            if(cityVehicle!=null){cityVehicle.name=type.ToUpperInvariant()+" vehicle";TintTransit(cityVehicle,type);return cityVehicle;}
            var model=Resources.Load<GameObject>(bus?"Models/Bus":"Models/Metro");
            if(model!=null){var instance=Instantiate(model,position,Quaternion.identity,root.transform);instance.name=type.ToUpperInvariant()+" vehicle";TintModel(instance);TintTransit(instance,type);return instance;}
            var o=new GameObject(type.ToUpperInvariant()+" vehicle");o.transform.SetParent(root.transform,false);o.transform.position=position;
            var body=Mat(bus?"busbody":"trainbody",bus?new Color(.22f,.68f,.64f):new Color(.77f,.8f,.84f),.15f);
            Primitive(PrimitiveType.Cube,"Body",o.transform,new Vector3(0,1.2f,0),new Vector3(bus?2.2f:2.7f,bus?2.1f:2.5f,bus?5.6f:9),body);
            Primitive(PrimitiveType.Cube,"Windshield",o.transform,new Vector3(0,1.6f,bus?2.82f:4.52f),new Vector3(bus?1.7f:2.1f,1.1f,.08f),Mat("glass",new Color(.13f,.29f,.39f),.3f));
            for(int side=-1;side<=1;side+=2)
            {
                for(int i=0;i<(bus?4:7);i++)Primitive(PrimitiveType.Cube,"Window",o.transform,new Vector3(side*(bus?1.12f:1.37f),1.7f,(i-(bus?1.5f:3f))*1.1f),new Vector3(.07f,.9f,.8f),Mat("glass",new Color(.13f,.29f,.39f),.3f));
                for(int i=-1;i<=1;i+=2){var wheel=Primitive(PrimitiveType.Cylinder,"Wheel",o.transform,new Vector3(side*(bus?1.15f:1.4f),.42f,i*(bus?1.75f:3.15f)),new Vector3(.42f,.13f,.42f),Mat("rubber",new Color(.07f,.09f,.1f)));wheel.transform.localRotation=Quaternion.Euler(0,0,90);}
            }
            return o;
        }
        public void BuildRideScene(string type)
        {
            Clear();SetupLight(new Color(.25f,.31f,.36f),new Color(.87f,.88f,.82f));
            worldCamera.orthographic=false;worldCamera.fieldOfView=72f;
            worldCamera.clearFlags=CameraClearFlags.SolidColor;
            worldCamera.backgroundColor=type=="metro"?new Color(.14f,.18f,.22f):new Color(.50f,.68f,.79f);
            if(type!="metro")Primitive(PrimitiveType.Cube,"주행 지형",root.transform,new Vector3(0,-.6f,6),new Vector3(120,1f,95),Mat("ride-ground",new Color(.31f,.43f,.34f)));
            if(type=="bus"||type=="brt")Primitive(PrimitiveType.Cube,"주행 도로",root.transform,new Vector3(0,.02f,6),new Vector3(8,.12f,80),Mat("ride-road",new Color(.19f,.22f,.25f)));
        }
        public static float RideHeight(string type){return type=="metro"?-3.2f:type=="ktx"?8.5f:.8f;}
        public void BuildRideInfrastructure(string type)
        {
            if(type=="bus")return;
            float y=RideHeight(type);
            if(type=="brt")
            {
                var lane=Primitive(PrimitiveType.Cube,"BRT 전용차로",root.transform,new Vector3(0,.08f,6),new Vector3(3,.035f,75),Mat("brt-lane",new Color(.60f,.13f,.13f)));
                DestroyImmediate(lane.GetComponent<Collider>());
                return;
            }
            var concrete=Mat("rail-concrete",new Color(.39f,.43f,.45f));
            var steel=Mat("rail-steel",new Color(.70f,.76f,.80f),.55f);
            Primitive(PrimitiveType.Cube,type=="metro"?"지하 터널 바닥":"KTX 고가 선로",root.transform,new Vector3(0,y-.55f,6),new Vector3(5,.4f,78),concrete);
            foreach(float x in new[]{-1.05f,1.05f})
            {
                var rail=Primitive(PrimitiveType.Cube,"레일",root.transform,new Vector3(x,y-.28f,6),new Vector3(.12f,.14f,78),steel);
                DestroyImmediate(rail.GetComponent<Collider>());
            }
            for(float z=-31;z<=43;z+=2.3f)
            {
                var sleeper=Primitive(PrimitiveType.Cube,"침목",root.transform,new Vector3(0,y-.4f,z),new Vector3(3,.13f,.28f),concrete);
                DestroyImmediate(sleeper.GetComponent<Collider>());
            }
            if(type=="metro")
            {
                var wall=Mat("metro-wall",new Color(.26f,.32f,.37f));
                var light=Mat("metro-light",new Color(.96f,.88f,.62f),.2f);
                foreach(float x in new[]{-3.1f,3.1f})
                    Primitive(PrimitiveType.Cube,"지하 터널 벽",root.transform,new Vector3(x,y+1.6f,6),new Vector3(.24f,4.4f,78),wall);
                Primitive(PrimitiveType.Cube,"터널 천장",root.transform,new Vector3(0,y+3.75f,6),new Vector3(6.5f,.25f,78),wall);
                foreach(float z in new[]{-27f,39f})
                {
                    foreach(float x in new[]{-4.6f,4.6f})Primitive(PrimitiveType.Cube,"승강장",root.transform,new Vector3(x,y-.24f,z),new Vector3(2.9f,.4f,12f),concrete);
                    Label(z<0?"출발역":"도착역",new Vector3(0,y+2.9f,z),.75f,Color.white,root.transform);
                }
                for(float z=-28;z<=40;z+=8f)
                {
                    var fixture=Primitive(PrimitiveType.Cube,"터널 조명",root.transform,new Vector3(0,y+3.57f,z),new Vector3(1.7f,.06f,.75f),light);
                    var lamp=fixture.AddComponent<Light>();lamp.type=LightType.Point;lamp.range=11f;lamp.intensity=1.5f;
                }
            }
            else for(float z=-25;z<=40;z+=13)
                Primitive(PrimitiveType.Cube,"고가 교각",root.transform,new Vector3(0,3.8f,z),new Vector3(1.1f,7.2f,1.1f),concrete);
        }
        void TintTransit(GameObject vehicle,string type)
        {
            if(type!="brt"&&type!="ktx")return;
            var stripe=Mat("transit-"+type,type=="brt"?new Color(.87f,.16f,.14f):new Color(.18f,.39f,.76f));
            var accent=GameObject.CreatePrimitive(PrimitiveType.Cube);accent.name=type.ToUpperInvariant()+" identification stripe";
            DestroyImmediate(accent.GetComponent<Collider>());accent.transform.SetParent(vehicle.transform,false);
            accent.transform.localPosition=new Vector3(0,1.15f,0);
            accent.transform.localScale=new Vector3(type=="brt"?2.3f:2.85f,.22f,type=="brt"?5.7f:9.1f);
            accent.GetComponent<Renderer>().sharedMaterial=stripe;
        }
        public void CreateRideCabin(GameObject carriage,string type)
        {
            foreach(var renderer in carriage.GetComponentsInChildren<Renderer>())renderer.enabled=false;
            bool bus=type=="bus"||type=="brt";
            var cabin=CityModel(bus?"BusCabin":"MetroCabin",carriage.transform.position,1f,carriage.transform.eulerAngles.y);
            if(cabin!=null){cabin.transform.SetParent(carriage.transform,true);return;}
            float side=bus?1.15f:1.43f,front=bus?2.65f:4.2f;
            var frame=Mat("cabin-frame",bus?new Color(.13f,.50f,.46f):new Color(.66f,.70f,.73f));
            var floor=Mat("cabin-floor",new Color(.25f,.31f,.32f));
            var seat=Mat("cabin-seat",bus?new Color(.18f,.31f,.38f):new Color(.30f,.37f,.52f));
            Primitive(PrimitiveType.Cube,"객실 바닥",carriage.transform,new Vector3(0,.38f,0),new Vector3(side*2,.12f,front*2),floor);
            for(int sign=-1;sign<=1;sign+=2)
            {
                Primitive(PrimitiveType.Cube,"창틀 하단",carriage.transform,new Vector3(sign*side,1.15f,0),new Vector3(.1f,.16f,front*2),frame);
                Primitive(PrimitiveType.Cube,"창틀 상단",carriage.transform,new Vector3(sign*side,2.75f,0),new Vector3(.1f,.15f,front*2),frame);
                for(int post=-1;post<=2;post++)Primitive(PrimitiveType.Cube,"창 기둥",carriage.transform,new Vector3(sign*side,1.95f,post*1.6f),new Vector3(.11f,1.7f,.11f),frame);
                for(int row=-1;row<=1;row++)
                {
                    Primitive(PrimitiveType.Cube,"승객 좌석",carriage.transform,new Vector3(sign*.62f,.76f,row*1.5f),new Vector3(.62f,.25f,.66f),seat);
                    Primitive(PrimitiveType.Cube,"좌석 등받이",carriage.transform,new Vector3(sign*.62f,1.2f,row*1.5f-.3f),new Vector3(.62f,.8f,.13f),seat);
                }
            }
            Primitive(PrimitiveType.Cube,"전면 창틀",carriage.transform,new Vector3(0,2.7f,front),new Vector3(side*2,.13f,.13f),frame);
        }
        void TintModel(GameObject model)
        {
            foreach(var renderer in model.GetComponentsInChildren<Renderer>())
            {
                string n=renderer.gameObject.name.ToLowerInvariant();
                Color c=n.Contains("window")||n.Contains("windshield")?new Color(.12f,.28f,.34f):
                    n.Contains("tile")||n.Contains("roof")?new Color(.18f,.23f,.25f):
                    n.Contains("wood")||n.Contains("pillar")||n.Contains("beam")||n.Contains("lattice")?new Color(.42f,.24f,.14f):
                    n.Contains("body")&&model.name.Contains("bus")?new Color(.11f,.60f,.54f):
                    n.Contains("body")&&model.name.Contains("train")?new Color(.75f,.79f,.81f):
                    n.Contains("wall")?new Color(.78f,.70f,.56f):
                    n.Contains("stone")||n.Contains("foundation")?new Color(.48f,.50f,.48f):
                    n.Contains("stripe")?new Color(.1f,.30f,.55f):
                    n.Contains("light")?new Color(.98f,.82f,.48f):
                    n.Contains("wheel")?new Color(.08f,.09f,.10f):
                    n.Contains("reed")||n.Contains("thatch")?new Color(.57f,.44f,.24f):new Color(.56f,.58f,.57f);
                renderer.sharedMaterial=Mat("model-"+n,c);
            }
        }
    }
}
