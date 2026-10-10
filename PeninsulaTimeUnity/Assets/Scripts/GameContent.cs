using System;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    [Serializable] public class EraInfo
    {
        public string name, date, category, detail, source;
        public int[] cost;
        public EraInfo(string n, string d, string c, string text, string link, params int[] needed)
        { name=n; date=d; category=c; detail=text; source=link; cost=needed; }
    }
    [Serializable] public class BuildingInfo
    {
        public string id, name, detail,category;
        public int unlock;
        public int[] cost, output;
        public BuildingInfo(string i,string n,int u,string d,int[] c,int[] o,string group)
        {id=i;name=n;unlock=u;detail=d;cost=c;output=o;category=group;}
    }
    [Serializable] public class CityInfo
    {
        public string id,name,region;
        public float longitude,latitude;
        public string kind;
        public bool featured;
        public CityInfo(string i,string n,string r,float lon,float lat,string place="city",bool highlight=false)
        {id=i;name=n;region=r;longitude=lon;latitude=lat;kind=place;featured=highlight;}
        public Vector3 position { get { return GeoProjection.ToWorld(longitude,latitude,1.4f); } }
    }
    public static class GameContent
    {
        public const string HistorySource="https://contents.history.go.kr/";
        public static readonly string[] ResourceNames={"목재","석재","식량","금속","제작품","연구","전력"};
        public static readonly string[] ResourceSymbols={"♣","◆","●","⬡","▣","✦","ϟ"};
        public static readonly string[] BuildCategories={"주거","식량·자원","산업","에너지","교통","서비스"};
        // Resource order: wood, stone, food, metal, goods, knowledge, energy.
        public static int[] R(int wood=0,int stone=0,int food=0,int metal=0,int goods=0,int knowledge=0,int energy=0)
        {return new[]{wood,stone,food,metal,goods,knowledge,energy};}
        // The game is set in modern Korea. GameState.era keeps this number for saves and online records written
        // by versions that had earlier eras.
        public const int ModernEra=9;
        public static readonly EraInfo Modern=new EraInfo("대한민국 현대","1953~2026","현대사","산업화·민주화·도시화는 서로 다른 역사입니다. 이 게임의 자원 수치는 역사 통계가 아닌 경영 규칙입니다.",HistorySource,R());
        public static readonly BuildingInfo[] Buildings={
            new BuildingInfo("camp","정착지",0,"첫 생산 거점",R(wood:5,stone:5),R(food:2),"주거"),
            new BuildingInfo("pit-house","움집",1,"선사 시대의 반지하형 주거",R(wood:8,stone:4),R(food:2),"주거"),
            new BuildingInfo("farm","농장",1,"시기마다 식량 생산",R(wood:8,stone:5),R(food:8),"식량·자원"),
            new BuildingInfo("forge","청동 작업장",2,"금속 생산을 추상화",R(wood:8,stone:10),R(metal:5),"산업"),
            new BuildingInfo("market","시장",3,"교역과 제작품",R(wood:12,stone:12,metal:4),R(goods:4),"산업"),
            new BuildingInfo("workshop","공방",4,"제작품 생산",R(wood:15,metal:10),R(goods:8),"산업"),
            new BuildingInfo("kiln","가마",5,"도자 생산을 추상화",R(wood:20,stone:20),R(goods:12),"산업"),
            new BuildingInfo("hanok","한옥",6,"조선 시대 목조 주거",R(wood:20,stone:8),R(food:3),"주거"),
            new BuildingInfo("school","학당",6,"연구와 교육",R(wood:20,goods:10),R(knowledge:8),"산업"),
            new BuildingInfo("park","공원",3,"휴식과 여가 공간",R(wood:6,stone:4),R(food:1),"서비스"),
            new BuildingInfo("hospital","병원",7,"시민의 건강을 돌봄",R(stone:15,goods:12,metal:8),R(knowledge:2),"서비스"),
            new BuildingInfo("railworks","차량 제작소",7,"근대 교통 산업",R(metal:30,goods:25),R(goods:12),"교통"),
            new BuildingInfo("house","주택",7,"근현대 주거",R(wood:20,stone:24,goods:8),R(food:4),"주거"),
            new BuildingInfo("coal-power","석탄 화력발전소",7,"근대 화력 발전",R(stone:25,metal:25),R(energy:9),"에너지"),
            new BuildingInfo("power","발전소",8,"전력 공급",R(metal:35,goods:30),R(energy:16),"에너지"),
            new BuildingInfo("oil-power","석유 화력발전소",9,"석유를 사용하는 발전",R(metal:45,goods:30),R(energy:20),"에너지"),
            new BuildingInfo("factory","현대 공장",9,"교통 차량과 부품",R(metal:45,goods:40,energy:10),R(goods:25),"산업"),
            new BuildingInfo("apartment","아파트",9,"현대 공동주택",R(stone:40,metal:30,goods:20),R(food:8),"주거"),
            new BuildingInfo("nuclear","원자력발전소",9,"높은 건설 비용의 현대 발전",R(stone:90,metal:90,goods:60,knowledge:40),R(energy:65),"에너지"),
            new BuildingInfo("wind","풍력발전기",9,"바람으로 전력 생산",R(metal:35,goods:25),R(energy:12),"에너지"),
            new BuildingInfo("bus-depot","버스 차고지",9,"버스 운영 시설",R(stone:20,metal:25,goods:30),R(goods:3),"교통"),
            new BuildingInfo("metro-depot","도시철도 차량기지",9,"철도 차량 정비 시설",R(stone:60,metal:55,goods:50,energy:10),R(goods:5),"교통")
        };
        static CityInfo[] cities;
        public static CityInfo[] Cities { get { if(cities==null)cities=LoadCities();return cities; } }
        static CityInfo[] LoadCities()
        {
            var asset=Resources.Load<TextAsset>("Geo/KoreanPlaces");
            var cities=new List<CityInfo>();
            if(asset!=null)
            {
                foreach(var line in asset.text.Split('\n'))
                {
                    if(string.IsNullOrWhiteSpace(line)||line[0]=='#')continue;
                    var fields=line.TrimEnd('\r').Split('\t');
                    float longitude,latitude;
                    if(fields.Length<7||!float.TryParse(fields[3],NumberStyles.Float,CultureInfo.InvariantCulture,out longitude)||
                       !float.TryParse(fields[4],NumberStyles.Float,CultureInfo.InvariantCulture,out latitude))continue;
                    cities.Add(new CityInfo(fields[0],fields[1],fields[2],longitude,latitude,fields[5],fields[6]=="1"));
                }
            }
            if(cities.Count==0)cities.Add(new CityInfo("seoul","서울","남한",126.978f,37.5665f,"city",true));
            return cities.ToArray();
        }
        public static readonly string[] DistrictNames={"강남","서울역","홍대","김포공항"};
        public static CityInfo City(string id){foreach(var c in Cities)if(c.id==id)return c;return Cities[0];}
        public static BuildingInfo Building(string id){foreach(var b in Buildings)if(b.id==id)return b;return null;}
    }
}
