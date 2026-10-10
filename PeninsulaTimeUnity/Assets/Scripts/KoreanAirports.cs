using UnityEngine;

namespace PeninsulaTime
{
    // South Korea's civil passenger airports (Incheon International plus the Korea Airports Corporation airports).
    // Position, main-runway heading and length are taken from published aerodrome data, rounded; size picks the
    // terminal scale of the generated scene (0 small regional, 1 regional international, 2 large, 3 hub).
    public sealed class KoreanAirport
    {
        public readonly string iata,name,english,cityId;
        public readonly float longitude,latitude,heading,runway;
        public readonly int size;
        public KoreanAirport(string iata,string name,string english,string cityId,float longitude,float latitude,float heading,float runway,int size)
        {this.iata=iata;this.name=name;this.english=english;this.cityId=cityId;this.longitude=longitude;this.latitude=latitude;this.heading=heading;this.runway=runway;this.size=size;}
        public int Gates{get{return size+2;}}
        // The city the arrivals exit leads to: the listed one, else the nearest city on the map.
        public string City
        {
            get
            {
                if(cityId.Length>0)return cityId;
                string best="seoul";float distance=float.MaxValue;
                foreach(var c in GameContent.Cities)
                {
                    if(c.kind!="city")continue;
                    float d=Sq(c.longitude-longitude)+Sq(c.latitude-latitude);
                    if(d<distance){distance=d;best=c.id;}
                }
                return best;
            }
        }
        static float Sq(float v){return v*v;}
    }

    public static class KoreanAirports
    {
        public const string Gimpo="GMP";
        public static readonly KoreanAirport[] All={
            new KoreanAirport("ICN","인천국제공항","Incheon International Airport","incheon",126.4407f,37.4602f,154f,3750f,3),
            new KoreanAirport("GMP","김포국제공항","Gimpo International Airport","seoul",126.7906f,37.5583f,143f,3600f,2),
            new KoreanAirport("PUS","김해국제공항","Gimhae International Airport","busan",128.9382f,35.1795f,178f,3200f,2),
            new KoreanAirport("CJU","제주국제공항","Jeju International Airport","jeju",126.4930f,33.5113f,70f,3180f,2),
            new KoreanAirport("TAE","대구국제공항","Daegu International Airport","daegu",128.6588f,35.8941f,133f,2755f,1),
            new KoreanAirport("CJJ","청주국제공항","Cheongju International Airport","cheongju",127.4991f,36.7166f,57f,2744f,1),
            new KoreanAirport("MWX","무안국제공항","Muan International Airport","",126.3828f,34.9914f,13f,2800f,1),
            new KoreanAirport("KWJ","광주공항","Gwangju Airport","gwangju",126.8089f,35.1264f,43f,2835f,1),
            new KoreanAirport("YNY","양양국제공항","Yangyang International Airport","",128.6692f,38.0613f,153f,2500f,1),
            new KoreanAirport("RSU","여수공항","Yeosu Airport","",127.6169f,34.8423f,170f,2100f,0),
            new KoreanAirport("USN","울산공항","Ulsan Airport","ulsan",129.3518f,35.5935f,177f,2000f,0),
            new KoreanAirport("KPO","포항경주공항","Pohang-Gyeongju Airport","pohang",129.4204f,35.9879f,103f,2133f,0),
            new KoreanAirport("HIN","사천공항","Sacheon Airport","",128.0704f,35.0886f,61f,2744f,0),
            new KoreanAirport("KUV","군산공항","Gunsan Airport","",126.6158f,35.9038f,179f,2745f,0),
            new KoreanAirport("WJU","원주공항","Wonju Airport","",127.9604f,37.4381f,32f,2743f,0)};

        public static KoreanAirport ByCode(string iata){foreach(var a in All)if(a.iata==iata)return a;return null;}
        // The airport serving a city: the one listed for it, else the nearest.
        public static KoreanAirport ForCity(string cityId)
        {
            foreach(var a in All)if(a.cityId==cityId)return a;
            var city=GameContent.City(cityId);KoreanAirport best=All[1];float distance=float.MaxValue;
            foreach(var a in All)
            {
                float d=(a.longitude-city.longitude)*(a.longitude-city.longitude)+(a.latitude-city.latitude)*(a.latitude-city.latitude);
                if(d<distance){distance=d;best=a;}
            }
            return best;
        }
        // Pure: a stable flight number for a route, e.g. "BS 1427"; the airline code comes from WorldBuilder.AirlineCodes.
        public static string FlightNumber(string from,string to)
        {
            int hash=17;foreach(char c in from+to)hash=unchecked(hash*31+c);hash&=0x7fffffff;
            return WorldBuilder.AirlineCodes[hash%WorldBuilder.AirlineCodes.Length]+" "+(1000+hash%9000).ToString("0000");
        }
    }
}
