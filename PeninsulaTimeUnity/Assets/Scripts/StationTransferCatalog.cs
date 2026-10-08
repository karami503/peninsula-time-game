using System;
using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime {
    // Catalogued stop identity + proximity, never every geographically-near station.
    // Separate service IDs retain branches and KTX / conventional stopping patterns.
    public static class StationTransferCatalog {
        public const float MaximumStationSeparationKm=.8f;
        public static bool Rail(NetLine line){return line!=null&&(line.kind=="metro"||line.kind=="ktx"||line.kind=="mugunghwa");}
        public static string StationKey(string name){
            string key=(name??"").Trim();
            if(key.StartsWith("총신대입구",StringComparison.Ordinal))return "이수";
            int parenthesis=key.IndexOf('(');if(parenthesis>=0)key=key.Substring(0,parenthesis);
            parenthesis=key.IndexOf('（');if(parenthesis>=0)key=key.Substring(0,parenthesis);
            return TransitNetwork.Bare(key.Trim()).Replace(" ","");
        }
        public static bool SameStation(NetStation a,NetStation b){
            return a!=null&&b!=null&&(a==b||(StationKey(a.name)==StationKey(b.name)&&TransitNetwork.Kilometres(a,b)<=MaximumStationSeparationKm));
        }
        public static List<KeyValuePair<NetStation,NetLine>> Services(NetStation station){
            var result=new List<KeyValuePair<NetStation,NetLine>>();var lines=new HashSet<string>();
            if(station==null)return result;
            Add(station,result,lines);
            foreach(var other in TransitNetwork.Stations)if(other!=station&&SameStation(station,other))Add(other,result,lines);
            return result;
        }
        static void Add(NetStation station,List<KeyValuePair<NetStation,NetLine>> result,HashSet<string> seen){
            foreach(var line in station.lines)if(Rail(line)&&line.stops.Contains(station)&&line.stops.Count>1&&seen.Add(line.id))
                result.Add(new KeyValuePair<NetStation,NetLine>(station,line));
        }
        public static bool CanDepart(NetStation station,NetLine line,int direction){
            if(station==null||line==null||line.stops.Count<2)return false;
            int at=line.stops.IndexOf(station);if(at<0)return false;
            return line.loop||(direction==0?at<line.stops.Count-1:at>0);
        }
        public static string Toward(NetStation station,NetLine line,int direction){
            if(!CanDepart(station,line,direction))return "종착";
            var next=StationJourney.Next(line,station,direction,1);
            string terminus=TransitSchedule.Toward(line,direction);
            return (next.Count>1?TransitNetwork.Bare(next[1].name)+" 방면 · ":"")+terminus;
        }
    }
    // World-space foot positions shared by floor guides, walk tests and platform selection.
    public sealed class NetworkTransferRoute {
        public NetStation station;public NetLine line;public int direction;public StationJourney service;
        public Vector3 hallPoint,rampTop,rampBottom,platformPoint;
        public float platformLength;
        public string toward;
        public Vector3[] Points {get{return new[]{hallPoint,rampTop,rampBottom,platformPoint};}}
    }
}
