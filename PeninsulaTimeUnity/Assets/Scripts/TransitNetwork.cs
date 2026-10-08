using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PeninsulaTime
{
    // A stop on the network: subway or rail station, or bus stop.
    public class NetStation
    {
        public string id,name,grade,gradeSource;public float lon,lat;public bool custom,approximate;
        public readonly List<NetLine> lines=new List<NetLine>();
        public Vector3 MapPosition(float height){return GeoProjection.ToWorld(lon,lat,height);}
    }

    // One line or service pattern: a subway line, a KTX or 무궁화호 stopping pattern, a bus route or BRT corridor.
    public class NetLine
    {
        public string id,kind,name,shortName,region,source;public bool planned;public Color color;
        public float peakMinutes,offPeakMinutes,speedKmh;public int firstMinute,lastMinute;public bool loop,custom,edited;
        public bool clockwise; // loop lines: stops listed clockwise on a north-up map (2호선 내선순환 runs clockwise)
        public readonly List<NetStation> stops=new List<NetStation>();
        public readonly List<List<Vector2>> shapes=new List<List<Vector2>>(); // lon,lat polylines; empty = straight between stops
        public int[] stopShapeIndices=new int[0];
        public float[] offsets=new float[0]; // seconds from the first stop to each stop
        public int IndexOf(NetStation s){return stops.IndexOf(s);}
        public NetStation Terminus(int direction){return stops.Count==0?null:direction==0?stops[stops.Count-1]:stops[0];}
        public string KindLabel{get{return TransitNetwork.KindLabel(kind);}}
    }

    // Player changes to the network, kept in the save: new stations and lines, base lines re-ordered or trimmed, removals.
    [Serializable] public class CustomStation{public string id,name,grade,gradeSource;public float lon,lat;}
    [Serializable] public class CustomLine{public string id,kind,name,color;public float peak=5,offPeak=8;public List<string> stops=new List<string>();}
    [Serializable] public class NetworkEdits
    {
        public List<CustomStation> stations=new List<CustomStation>();
        public List<CustomLine> lines=new List<CustomLine>();
        public List<string> removedStations=new List<string>(),removedLines=new List<string>();
    }

    // The country's rail, subway, bus and BRT network from Resources/Geo/TransitNetwork.txt
    // (AssetSources/build_transit_network.py: OpenStreetMap stations, lines and stop orders; KTX and 무궁화호
    // stopping patterns), with the player's edits applied on top.
    public static class TransitNetwork
    {
        public static readonly string[] Kinds={"metro","ktx","mugunghwa","brt","bus"};
        public static readonly List<NetStation> Stations=new List<NetStation>();
        public static readonly List<NetLine> Lines=new List<NetLine>();
        // BRT busways and Seoul's median bus lanes (중앙버스전용차로): name and lon,lat polyline.
        public static readonly List<KeyValuePair<string,List<Vector2>>> Corridors=new List<KeyValuePair<string,List<Vector2>>>();
        static readonly Dictionary<string,NetStation> byId=new Dictionary<string,NetStation>();
        static string baseText;
        public static int Version{get;private set;} // bumps on every rebuild so views know to redraw

        public static string KindLabel(string kind)
        {
            switch(kind){case "ktx":return "KTX";case "mugunghwa":return "무궁화호";case "brt":return "BRT";case "bus":return "버스";default:return "지하철";}
        }
        public static NetStation Station(string id){NetStation s;return id!=null&&byId.TryGetValue(id,out s)?s:null;}
        public static NetLine Line(string id){return Lines.Find(l=>l.id==id);}

        // Rebuilds the network from the base data and the edits. Safe to call after every edit.
        public static void Build(NetworkEdits edits)
        {
            if(baseText==null){var asset=Resources.Load<TextAsset>("Geo/TransitNetwork");baseText=asset!=null?asset.text:"";
                foreach(var extra in new[]{"TransitExpansion","ChangwonBuses","StationGrades"}){var addition=Resources.Load<TextAsset>("Geo/"+extra);if(addition!=null)baseText+="\n"+addition.text;}}
            Stations.Clear();Lines.Clear();byId.Clear();Corridors.Clear();
            if(edits==null)edits=new NetworkEdits();
            var removedStations=new HashSet<string>(edits.removedStations);
            var removedLines=new HashSet<string>(edits.removedLines);
            var overrides=new Dictionary<string,CustomLine>();foreach(var l in edits.lines)overrides[l.id]=l;
            var stopIds=new Dictionary<NetLine,List<string>>();
            NetLine shapeOwner=null;
            foreach(var raw in baseText.Split('\n'))
            {
                if(raw.Length<3||raw[0]=='#')continue;
                var f=raw.TrimEnd('\r').Split('|');
                switch(f[0])
                {
                    case "S":
                        if(f.Length<5||removedStations.Contains(f[1]))break;
                        AddStation(new NetStation{id=f[1],name=f[2],lon=Float(f[3]),lat=Float(f[4])});break;
                    case "H":
                        if(f.Length>3&&Station(f[1])!=null){Station(f[1]).grade=f[2];Station(f[1]).gradeSource=f[3];}break;
                    case "E":
                        if(f.Length>2&&Station(f[1])!=null)Station(f[1]).approximate=f[2]=="approximate";break;
                    case "Q":
                        if(f.Length>3&&shapeOwner!=null&&shapeOwner.id==f[1]){shapeOwner.planned=f[2]=="planned";shapeOwner.source=f[3];}break;
                    case "L":
                        shapeOwner=null;
                        if(f.Length<13||removedLines.Contains(f[1]))break;
                        var line=new NetLine{id=f[1],kind=f[2],name=f[3],shortName=f[4],color=Colour(f[5]),peakMinutes=Float(f[6]),offPeakMinutes=Float(f[7]),
                            firstMinute=Minutes(f[8]),lastMinute=Minutes(f[9]),speedKmh=Float(f[10]),loop=f[11]=="1",region=f[12]};
                        Lines.Add(line);shapeOwner=line;break;
                    case "P":
                        if(shapeOwner!=null&&f.Length>2&&f[1]==shapeOwner.id)stopIds[shapeOwner]=new List<string>(f[2].Split(' '));break;
                    case "J":
                        if(shapeOwner!=null&&f.Length>2&&f[1]==shapeOwner.id)shapeOwner.stopShapeIndices=System.Array.ConvertAll(f[2].Split(' '),int.Parse);break;
                    case "G":
                        if(shapeOwner!=null&&f.Length>2&&f[1]==shapeOwner.id)shapeOwner.shapes.Add(Shape(f[2]));break;
                    case "B":
                        if(f.Length>2)Corridors.Add(new KeyValuePair<string,List<Vector2>>(f[1],Shape(f[2])));break;
                }
            }
            foreach(var c in edits.stations)if(!removedStations.Contains(c.id))AddStation(new NetStation{id=c.id,name=c.name,lon=c.lon,lat=c.lat,custom=true});
            foreach(var line in Lines)
            {
                CustomLine o;
                if(overrides.TryGetValue(line.id,out o))
                {
                    line.name=string.IsNullOrEmpty(o.name)?line.name:o.name;line.color=Colour(o.color,line.color);
                    stopIds[line]=o.stops;line.edited=true;line.shapes.Clear();line.stopShapeIndices=new int[0]; // re-drawn through its new stops
                }
            }
            foreach(var o in edits.lines)
            {
                if(Lines.Exists(l=>l.id==o.id)||removedLines.Contains(o.id))continue;
                var line=new NetLine{id=o.id,kind=o.kind,name=o.name,shortName=o.name,color=Colour(o.color,Color.white),peakMinutes=o.peak,offPeakMinutes=o.offPeak,
                    firstMinute=o.kind=="bus"||o.kind=="brt"?270:330,lastMinute=o.kind=="ktx"||o.kind=="mugunghwa"?1380:1440,speedKmh=DefaultSpeed(o.kind),region="사용자",custom=true};
                Lines.Add(line);stopIds[line]=o.stops;
            }
            foreach(var pair in stopIds)
            {
                foreach(var id in pair.Value){var s=Station(id);if(s!=null&&(pair.Key.stops.Count==0||pair.Key.stops[pair.Key.stops.Count-1]!=s))pair.Key.stops.Add(s);}
                Timing(pair.Key);
                if(pair.Key.loop)pair.Key.clockwise=SignedArea(pair.Key.stops)<0;
                foreach(var s in pair.Key.stops)if(!s.lines.Contains(pair.Key))s.lines.Add(pair.Key);
            }
            Lines.RemoveAll(l=>l.stops.Count<2);
            foreach(var s in Stations)s.lines.RemoveAll(l=>l.stops.Count<2); // a line cut down to one stop is gone
            Version++;
        }
        // Shoelace area in lon/lat: positive when the stops run counter-clockwise.
        static float SignedArea(List<NetStation> stops)
        {
            float area=0;for(int i=0;i<stops.Count;i++){var a=stops[i];var b=stops[(i+1)%stops.Count];area+=a.lon*b.lat-b.lon*a.lat;}
            return area;
        }
        static void AddStation(NetStation s){if(byId.ContainsKey(s.id))return;byId[s.id]=s;Stations.Add(s);}
        public static float DefaultSpeed(string kind){return kind=="ktx"?170f:kind=="mugunghwa"?75f:kind=="bus"?18f:kind=="brt"?24f:34f;}
        public static float DwellSeconds(string kind){return 10f;}
        // Seconds from the first stop to each stop: running time at the line's schedule speed plus dwell.
        static void Timing(NetLine line)
        {
            line.offsets=new float[line.stops.Count];
            for(int i=1;i<line.stops.Count;i++)
            {
                float km=Kilometres(line.stops[i-1],line.stops[i])*(line.kind=="bus"||line.kind=="brt"?1.25f:1.12f); // roads and tracks are not straight
                line.offsets[i]=line.offsets[i-1]+TransitSpeed.RunningSeconds(line.kind,km,line.speedKmh)+(i<line.stops.Count-1?DwellSeconds(line.kind):0);
            }
        }
        public static float Kilometres(NetStation a,NetStation b)
        {
            float dLat=(b.lat-a.lat)*111.32f,dLon=(b.lon-a.lon)*111.32f*Mathf.Cos((a.lat+b.lat)*.5f*Mathf.Deg2Rad);
            return Mathf.Sqrt(dLat*dLat+dLon*dLon);
        }
        static float Float(string s){float v;float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out v);return v;}
        static int Minutes(string hhmm){int v;int.TryParse(hhmm,out v);return v/100*60+v%100;}
        static Color Colour(string hex,Color fallback=default(Color))
        {
            Color c;if(!string.IsNullOrEmpty(hex)&&ColorUtility.TryParseHtmlString(hex.StartsWith("#")?hex:"#"+hex,out c))return c;
            return fallback==default(Color)?new Color(.6f,.6f,.6f):fallback;
        }
        static List<Vector2> Shape(string text)
        {
            var points=new List<Vector2>();
            foreach(var pair in text.Split(';')){var xy=pair.Split(',');if(xy.Length==2)points.Add(new Vector2(Float(xy[0]),Float(xy[1])));}
            return points;
        }

        // Nearest station (optionally only those accepted) to a map position, within maxKm.
        public static NetStation Nearest(float lon,float lat,float maxKm,Predicate<NetStation> accept=null)
        {
            NetStation best=null;float bestKm=maxKm;var probe=new NetStation{lon=lon,lat=lat};
            foreach(var s in Stations){if(accept!=null&&!accept(s))continue;float km=Kilometres(probe,s);if(km<bestKm){bestKm=km;best=s;}}
            return best;
        }
        // Stations with this name (a transfer station has one entry per line in the base data).
        public static List<NetStation> Named(string name)
        {
            var result=new List<NetStation>();string bare=Bare(name);
            foreach(var s in Stations)if(Bare(s.name)==bare)result.Add(s);
            return result;
        }
        public static string Bare(string name){return name!=null&&name.Length>1&&name.EndsWith("역")?name.Substring(0,name.Length-1):name;}
        public static string NewId(string prefix){return prefix+DateTime.Now.Ticks.ToString("x");}
    }
}
