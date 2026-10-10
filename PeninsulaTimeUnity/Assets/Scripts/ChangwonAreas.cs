using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PeninsulaTime
{
    // "성산구 상남동": the 구 outline containing a point plus the nearest 동·읍·면·리 label (Overture Maps divisions).
    public static class ChangwonAreas
    {
        class Gu {public string name;public Vector2[] ring;}
        class Spot {public string name,kind;public Vector2 pos;}
        static readonly List<Gu> gus=new List<Gu>();static readonly List<Spot> spots=new List<Spot>();
        static float F(string s){return float.Parse(s,CultureInfo.InvariantCulture);}
        public static void Parse(string text)
        {
            gus.Clear();spots.Clear();
            foreach(var line in text.Split('\n')){
                if(line.Length<2||line[0]=='#')continue;var f=line.Split('|');
                if(f[0]=="G"&&f.Length>=3){var pairs=f[2].Split(';');var ring=new Vector2[pairs.Length];for(int i=0;i<pairs.Length;i++){var xy=pairs[i].Split(',');ring[i]=new Vector2(F(xy[0]),F(xy[1]));}gus.Add(new Gu{name=f[1],ring=ring});}
                else if(f[0]=="P"&&f.Length>=5)spots.Add(new Spot{kind=f[1],name=f[2].Trim(),pos=new Vector2(F(f[3]),F(f[4]))});
            }
        }
        public static string GuAt(float x,float z){var p=new Vector2(x,z);foreach(var g in gus)if(Polygon.Contains(g.ring,p))return g.name;return "";}
        public static string Name(float x,float z)
        {
            string gu=GuAt(x,z);var p=new Vector2(x,z);Spot best=null;float bestD=2600f*2600f;
            foreach(var s in spots){float d=(s.pos-p).sqrMagnitude;if(d<bestD){bestD=d;best=s;}}
            if(gu.Length==0&&best==null)return "";
            if(gu.Length==0)return best.name;
            return best!=null?gu+" "+best.name:gu;
        }
    }

    public static class ChangwonLandmarks
    {
        public class Landmark {public string id,name,gu,kind,description,activity;public Vector2 pos;public float height;}
        public static readonly List<Landmark> All=new List<Landmark>();
        static float F(string s){float v;return float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:0;}
        public static void Parse(string text)
        {
            All.Clear();
            foreach(var line in text.Split('\n')){
                if(line.Length<2||line[0]=='#')continue;var f=line.Split('|');if(f.Length<9)continue;
                All.Add(new Landmark{id=f[0],name=f[1],gu=f[2],pos=new Vector2(F(f[3]),F(f[4])),kind=f[5],height=F(f[6]),description=f[7],activity=f[8].Trim()});
            }
        }
        public static Landmark Nearest(Vector2 p,float within){Landmark best=null;float d=within*within;foreach(var l in All){float e=(l.pos-p).sqrMagnitude;if(e<d){d=e;best=l;}}return best;}
    }
}
