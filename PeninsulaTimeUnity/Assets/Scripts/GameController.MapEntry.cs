using UnityEngine;
namespace PeninsulaTime
{
    // Map 3D button: starts 3D at the spot last clicked on the national map; a click inside 진해구 starts at 진해역.
    // The 3D exists in the four Seoul districts (OSM geometry) and in each city's plot, so the click picks the nearest one:
    // within SeoulReachKm of a Seoul district, that district; otherwise the nearest city's 3D street.
    public partial class GameController
    {
        // Approximate district centres, lon/lat. The OSM district files use local metres, so these anchor them on the map.
        static readonly Vector2[] DistrictCentres={new Vector2(127.0276f,37.4979f),new Vector2(126.9707f,37.5547f),new Vector2(126.9237f,37.5563f),new Vector2(126.8012f,37.5583f)};
        const float SeoulReachKm=25f;
        bool mapClickSet;
        Vector2 mapClickGeo;

        // Pure: distance in km between two lon/lat points (equirectangular; accurate enough at city scale).
        public static float GeoDistanceKm(Vector2 a,Vector2 b)
        {
            float dx=(a.x-b.x)*Mathf.Cos(a.y*Mathf.Deg2Rad)*111.32f,dy=(a.y-b.y)*110.57f;
            return Mathf.Sqrt(dx*dx+dy*dy);
        }
        // Pure: index of the district centre nearest to a point.
        public static int NearestDistrict(Vector2 geo)
        {
            int best=0;float bestKm=float.MaxValue;
            for(int i=0;i<DistrictCentres.Length;i++)
            {
                float km=GeoDistanceKm(geo,DistrictCentres[i]);
                if(km<bestKm){bestKm=km;best=i;}
            }
            return best;
        }
        // Pure: whether a point is close enough to a Seoul district to open its 3D.
        public static bool WithinSeoulReach(Vector2 geo){return GeoDistanceKm(geo,DistrictCentres[NearestDistrict(geo)])<=SeoulReachKm;}
        // The city whose centre is nearest the point.
        static CityInfo NearestCityTo(Vector2 geo)
        {
            CityInfo best=GameContent.Cities[0];float bestKm=float.MaxValue;
            foreach(var city in GameContent.Cities)
            {
                float km=GeoDistanceKm(geo,new Vector2(city.longitude,city.latitude));
                if(km<bestKm){bestKm=km;best=city;}
            }
            return best;
        }
        // Map click on the ground plane (y=0): the lon/lat under the cursor.
        void RecordMapClick(){RecordMapClickAt(Input.mousePosition);}
        // screen: a window pixel in Input.mousePosition space; QA passes the projected 강남/부산 pixel through this same path.
        void RecordMapClickAt(Vector2 screen)
        {
            var ray=viewCamera.ScreenPointToRay(screen);float distance;
            if(!new Plane(Vector3.up,Vector3.zero).Raycast(ray,out distance))return;
            mapClickGeo=GeoProjection.ToGeo(ray.GetPoint(distance));mapClickSet=true;
            if(playtest)Debug.Log("QA-CLICK screen="+Mathf.RoundToInt(screen.x)+","+Mathf.RoundToInt(Screen.height-screen.y)+" geo="+mapClickGeo.x.ToString("F4")+","+mapClickGeo.y.ToString("F4")+" nearestDistrict="+NearestDistrict(mapClickGeo));
        }
        // The 3D button: starts in 진해역 for a click inside 진해구, otherwise inside Seoul Station, in the middle of its main concourse, with every chunk
        // around it loaded at once (the district is built whole; the streamer only unloads far cells later).
        public const int SeoulStationDistrict=1;
        void Start3DAtMapClick()
        {
            if(maintenance)return;
            if(mapClickSet&&WorldBuilder.JinhaeArea.Contains(mapClickGeo)){EnterJinhae();return;}
            if(mapClickSet&&!WithinSeoulReach(mapClickGeo))
            {
                var city=NearestCityTo(mapClickGeo);state.selectedCity=city.id;
                if(city.name.Contains("창원")){EnterOpenWorld();return;}
                EnterCityPlot();ToggleCityStreet();
                Toast(city.name+" 3D 거리 · WASD 이동 · 마우스 시선");return;
            }
            int district=mapClickSet?NearestDistrict(mapClickGeo):SeoulStationDistrict;
            EnterDistrict(district);
            if(district!=SeoulStationDistrict)return;
            Teleport(world.SeoulStationConcourse+Vector3.up*EyeHeight,world.SeoulStationConcourseFacing);
            undergroundWalk=world.SeoulStationConcourse.y<-1f;
            var chunks=world.root!=null?world.root.GetComponent<ChunkStreamer>():null;if(chunks!=null)chunks.Stream(eye.position);
            PlaceViewCamera();
            if(playtest)Debug.Log("QA-3D start Seoul Station concourse "+eye.position);
            Toast(WorldBuilder.StationTitle(SeoulStationDistrict)+" 맞이방에서 시작합니다 · WASD 이동 · 마우스 시선");
        }
    }
}
