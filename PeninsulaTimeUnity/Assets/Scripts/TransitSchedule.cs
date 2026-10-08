using System;
using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // One vehicle on its way to a stop, as a real-time arrival board shows it.
    public struct Arrival
    {
        public NetLine line;public int direction,trip,stopsAway,delay;
        public double seconds;      // until it reaches the stop (delay included); negative while it stands there
        public double scheduled;    // timetable time at the stop, seconds after midnight
        public NetStation now;      // the stop it is at or last left
        public bool waiting;        // not yet left its first stop
    }

    // Timetables made from each line's first/last service, peak and off-peak headways and running times, on the
    // device clock. Arrivals and vehicle positions follow from them, like a BIS or a station's arrival screen.
    // The times are a simulation, not the operators' published timetables.
    public static class TransitSchedule
    {
        public const double Day=86400;
        // Time the player has skipped by waiting for a train (keeps boards and trains in step).
        public static double Skip;
        public static readonly double ServiceEpoch=Application.isPlaying?DateTime.Now.TimeOfDay.TotalSeconds:0;
        public static double Now{get{return DateTime.Now.TimeOfDay.TotalSeconds+Skip;}}
        static readonly Dictionary<string,double[]> departures=new Dictionary<string,double[]>();
        static int builtFor=-1;

        static bool Peak(double t){double h=(t/3600)%24;return (h>=7&&h<9)||(h>=18&&h<20);}
        public static double Headway(NetLine line,double t){return line.kind=="bus"||line.kind=="brt"?50:30;}
        // Departure times from the line's first stop in this direction over one service day.
        public static double[] Departures(NetLine line,int direction)
        {
            if(builtFor!=TransitNetwork.Version){departures.Clear();builtFor=TransitNetwork.Version;}
            string key=line.id+"/"+direction;double[] result;
            if(departures.TryGetValue(key,out result))return result;
            var list=new List<double>();
            // Each direction starts at its own end; offset the reverse a little so the two do not mirror exactly,
            // and lines sharing a first-train time a few minutes apart.
            int ordinal=TransitNetwork.Lines.IndexOf(line);if(ordinal<0)ordinal=0;
            double t=line.firstMinute*60+ordinal*15+(direction==1?15:0);
            while(t<=line.lastMinute*60){list.Add(t);t+=Headway(line,t);}
            result=list.ToArray();departures[key]=result;return result;
        }
        // String hash that is the same on every platform and run (string.GetHashCode need not be).
        static int Stable(string text){int h=17;foreach(char c in text)h=unchecked(h*31+c);return h&0x7fffffff;}
        public static int Last(NetLine line){return line.stops.Count-1;}
        // Position of stop `index` along the direction of travel, and its running time from the start of the trip.
        public static int Order(NetLine line,int direction,int index){return direction==0?index:Last(line)-index;}
        public static float Offset(NetLine line,int direction,int index)
        {
            if(line.offsets.Length==0)return 0;
            return direction==0?line.offsets[index]:line.offsets[Last(line)]-line.offsets[index];
        }
        // A steady, repeatable delay for a trip: subway trains run close to time, buses and 무궁화호 drift more.
        public static int Delay(NetLine line,int direction,int trip)
        {
            return 0; // Fixed gameplay cadence: random delay would break the requested headway.
        }

        public static string TrainNumber(NetLine line,int direction,int trip)
        {
            int baseNumber=Stable(line.id)%40*20+(line.kind=="ktx"?1:1201);
            return (line.kind=="ktx"?"KTX ":line.kind=="mugunghwa"?"무궁화 ":"")+(baseNumber+trip*2+(direction==0?0:1)).ToString(line.kind=="ktx"?"000":"0000");
        }

        // The next `count` vehicles stopping at stop `index` (one standing there now is included).
        public static List<Arrival> Next(NetLine line,int direction,int index,int count,double now)
        {
            var result=new List<Arrival>();
            if(line==null||index<0||index>=line.stops.Count)return result;
            // Trips end at the last stop of their direction: nothing departs from there in that direction.
            if(!line.loop&&Order(line,direction,index)==Last(line))return result;
            var times=Departures(line,direction);float offset=Offset(line,direction,index);
            float dwell=TransitNetwork.DwellSeconds(line.kind);
            now%=Day;
            // Delays are bounded by nine minutes. Binary search to the first relevant trip instead of
            // walking a whole day's timetable for every station sign and map label.
            for(int day=-1;day<=1;day++)
            {
                double earliest=now-day*Day-offset-dwell;
                int trip=System.Array.BinarySearch(times,earliest);
                if(trip<0)trip=~trip;
                for(;trip<times.Length;trip++)
                {
                    int delay=Delay(line,direction,trip);
                    double start=times[trip]+day*Day,at=start+offset+delay;
                    if(at+dwell<now)continue;
                    if(result.Count>=count)break;
                    var a=new Arrival{line=line,direction=direction,trip=trip,delay=delay,seconds=at-now,scheduled=(start+offset+Day)%Day};
                    Locate(ref a,start+delay,now);
                    a.stopsAway=Order(line,direction,index)-a.stopsAway;
                    result.Add(a);
                    if(result.Count>count+8){result.Sort((x,y)=>x.seconds.CompareTo(y.seconds));result.RemoveRange(count+8,result.Count-count-8);}
                }
            }
            result.Sort((a,b)=>a.seconds.CompareTo(b.seconds));
            if(result.Count>count)result.RemoveRange(count,result.Count-count);
            return result;
        }
        // Trains (KTX, 무궁화호; or one kind) leaving a station, soonest first; one standing there is included.
        public static List<Arrival> TrainsFrom(NetStation s,double now,string kind=null)
        {
            var result=new List<Arrival>();if(s==null)return result;
            foreach(var line in s.lines)
            {
                if((line.kind!="ktx"&&line.kind!="mugunghwa")||(kind!=null&&line.kind!=kind))continue;
                int index=line.IndexOf(s);
                for(int direction=0;direction<2;direction++)result.AddRange(Next(line,direction,index,4,now));
            }
            result.Sort((a,b)=>a.seconds.CompareTo(b.seconds));
            return result;
        }
        public static NetStation RailStation(string name)
        {
            foreach(var s in TransitNetwork.Named(name))foreach(var l in s.lines)if(l.kind=="ktx"||l.kind=="mugunghwa")return s;
            return null;
        }
        // A station's departure screen: time, train, destination, status.
        public static string DepartureText(NetStation s,int rows)
        {
            if(s==null)return "";
            double now=Now;var trains=TrainsFrom(s,now);
            var text=TransitNetwork.Bare(s.name)+"역 열차 출발 안내   "+Clock(now);
            for(int i=0;i<Mathf.Min(rows,trains.Count);i++)
            {
                var a=trains[i];var end=a.line.Terminus(a.direction);
                text+="\n"+Clock(a.scheduled)+"  "+TrainNumber(a.line,a.direction,a.trip)+"  "+(end!=null?TransitNetwork.Bare(end.name):"")+"  "+(a.seconds<=0?"탑승 중":a.delay>=60?a.delay/60+"분 지연":"정시");
            }
            if(trains.Count==0)text+="\n오늘 운행이 끝났습니다";
            return text;
        }
        // A bus stop's BIS screen: each route's next bus, soonest first ("100  3분 12초 후 · 2번째 전").
        public static string StopText(NetStation s,int rows)
        {
            if(s==null)return "";
            double now=Now;var next=new List<Arrival>();
            foreach(var line in s.lines)
            {
                if(line.kind!="bus"&&line.kind!="brt")continue;
                int index=line.IndexOf(s);
                for(int direction=0;direction<2;direction++){var a=Next(line,direction,index,1,now);if(a.Count>0)next.Add(a[0]);}
            }
            next.Sort((a,b)=>a.seconds.CompareTo(b.seconds));
            var text=s.name+"   "+Clock(now);
            for(int i=0;i<Mathf.Min(rows,next.Count);i++)text+="\n"+next[i].line.shortName+"  "+Describe(next[i]);
            if(next.Count==0)text+="\n도착 정보 없음";
            return text;
        }
        // Where the vehicle of a trip that left its first stop at `left` is now; stores its order in a.stopsAway.
        static void Locate(ref Arrival a,double left,double now)
        {
            var line=a.line;double elapsed=now-left;int order=0;
            a.waiting=elapsed<0;
            for(int k=1;k<line.stops.Count;k++)
            {
                int index=a.direction==0?k:Last(line)-k;
                if(Offset(line,a.direction,index)<=elapsed)order=k;else break;
            }
            a.stopsAway=order;
            a.now=line.stops[a.direction==0?order:Last(line)-order];
        }
        // Vehicles of a line running right now, as (direction, fraction 0..1 along the trip) pairs for route maps.
        public static List<Vector2> Running(NetLine line,double now)
        {
            var result=new List<Vector2>();if(line.offsets.Length==0)return result;
            float total=line.offsets[Last(line)];now%=Day;
            for(int direction=0;direction<2;direction++)
            {
                var times=Departures(line,direction);
                for(int day=-1;day<=0;day++)
                {
                    int first=System.Array.BinarySearch(times,now-day*Day-total);if(first<0)first=~first;
                    for(int trip=first;trip<times.Length&&times[trip]+day*Day<=now;trip++){
                    double elapsed=now-(times[trip]+day*Day+Delay(line,direction,trip));
                    if(elapsed>=0&&elapsed<=total)result.Add(new Vector2(direction,(float)(elapsed/Math.Max(1,total))));
                    }
                }
            }
            return result;
        }

        // "3분 12초 후", "곧 도착", "전역 출발" ... in the style of Seoul's BIS and subway arrival screens.
        public static string Describe(Arrival a)
        {
            bool bus=a.line.kind=="bus"||a.line.kind=="brt";
            if(a.seconds<=0)return bus?"정류장 도착":"당역 도착";
            if(a.seconds<60)return bus?"곧 도착":a.stopsAway<=0?"당역 진입":"전역 출발";
            if(a.seconds>=3600)return Clock(a.scheduled+a.delay)+" 도착 예정";
            string eta=Mathf.FloorToInt((float)a.seconds/60)+"분"+(a.seconds<600?" "+Mathf.FloorToInt((float)a.seconds%60)+"초":"")+" 후";
            if(a.waiting)return eta+" · 출발 대기";
            if(bus)return eta+" · "+a.stopsAway+"번째 전";
            return eta+(a.stopsAway<=1?" · 전역 출발":" · "+a.stopsAway+"번째 전역 ("+a.now.name+")");
        }
        public static string Clock(double seconds){seconds=((seconds%Day)+Day)%Day;int m=(int)(seconds/60);return (m/60).ToString("00")+":"+(m%60).ToString("00");}
        public static string Toward(NetLine line,int direction)
        {
            if(line.loop)return (direction==0)==line.clockwise?"내선순환":"외선순환";
            var end=line.Terminus(direction);return end!=null?end.name+"행":"";
        }
        // The stop after `index` in this direction, for "○○ 방면" signs.
        public static NetStation NextStop(NetLine line,int direction,int index)
        {
            int next=index+(direction==0?1:-1);
            if(line.loop){int n=line.stops.Count-1;next=(next+n)%n;}
            return next>=0&&next<line.stops.Count?line.stops[next]:null;
        }
    }
}
