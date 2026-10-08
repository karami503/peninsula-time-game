using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    public class CountryInfo
    {
        public string name,iso2,sourceName;
        public CountryInfo(string n,string code,string source){name=n;iso2=code;sourceName=source;}
    }
    public static class WorldCatalog
    {
        static CountryInfo[] countries;
        public static CountryInfo[] Countries
        {
            get
            {
                if(countries==null)countries=Load();
                return countries;
            }
        }
        static CountryInfo[] Load()
        {
            var result=new List<CountryInfo>();
            var asset=Resources.Load<TextAsset>("Geo/WorldCountries");
            if(asset!=null)foreach(var line in asset.text.Split('\n'))
            {
                if(string.IsNullOrWhiteSpace(line)||line[0]=='#')continue;
                var fields=line.TrimEnd('\r').Split('\t');
                if(fields.Length>=4)result.Add(new CountryInfo(fields[1],fields[2],fields[3]));
            }
            if(result.Count==0)result.Add(new CountryInfo("대한민국","KR","South Korea"));
            return result.ToArray();
        }
    }
}
