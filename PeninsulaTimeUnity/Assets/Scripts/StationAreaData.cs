using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace PeninsulaTime {
    // Offline OSM geometry only. Missing interior plans are not replaced with surveyed claims.
    public static class StationAreaData {
        [Serializable] public class Manifest {
            public int version; public float radius; public string source,sourceTimestamp,license;
            public Entry[] stations;
        }
        [Serializable] public class Entry {
            public string id,name,grade; public double lon,lat;
            public bool approximate,plannedOnly;
            public string[] lines,tiles;
            public int roads,buildings,entrances,platforms,rails;
        }
        [Serializable] public class Tile {
            public string key; public double lon,lat; public Feature[] features;
        }
        [Serializable] public class Feature {
            public string id,kind,name,@ref,subtype,heightSource,widthSource;
            public float h,@base,w; public int layer; public bool underground,bridge;
            public float[] points; public int[] rings,triangles;
        }
        [Serializable] public class ArchitectureRow {
            public int line;public string name,platformType,floorLabel,year;
            public float platformLength,totalStationArea;
        }
        [Serializable] public class ArchitectureFile {
            public string source,sourceDate;public ArchitectureRow[] rows;
        }
        static ArchitectureFile architecture;
        static bool architectureLoaded;
        public static ArchitectureRow Architecture(NetStation station,NetLine line){
            if(station==null||line==null||line.region!="수도권")return null;
            int number=0;
            for(int i=1;i<=8;i++)if(line.shortName==i+"호선"){number=i;break;}
            if(number==0)return null;
            if(!architectureLoaded){
                architectureLoaded=true;var asset=Resources.Load<TextAsset>("Geo/StationAreas/architecture");
                if(asset!=null){try{architecture=JsonUtility.FromJson<ArchitectureFile>(asset.text);}finally{Resources.UnloadAsset(asset);}}
            }
            if(architecture==null||architecture.rows==null)return null;
            string name=station.name.EndsWith("역")?station.name.Substring(0,station.name.Length-1):station.name;
            foreach(var row in architecture.rows)if(row.line==number&&row.name==name)return row;
            return null;
        }
        static Manifest manifest;
        static readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
        static readonly Dictionary<string,Tile> tiles=new Dictionary<string,Tile>();
        static readonly LinkedList<string> lru=new LinkedList<string>();
        public const int MaximumCachedTiles=12;
        static bool loaded;
        public static Manifest Index {
            get { LoadIndex(); return manifest; }
        }
        static void LoadIndex(){
            if(loaded)return; loaded=true;
            var asset=Resources.Load<TextAsset>("Geo/StationAreas/index");
            if(asset==null)return;
            try{
                manifest=JsonUtility.FromJson<Manifest>(asset.text);
                if(manifest!=null&&manifest.version==1&&manifest.stations!=null)
                    foreach(var e in manifest.stations)if(e!=null&&!string.IsNullOrEmpty(e.id))entries[e.id]=e;
            }catch(Exception e){Debug.LogWarning("역 주변 자료 목록을 읽지 못했습니다: "+e.Message);}
            finally{Resources.UnloadAsset(asset);}
        }
        public static Entry ForStation(NetStation station){
            if(station==null)return null;LoadIndex();Entry e;
            if(!entries.TryGetValue(station.id,out e))return null;
            // A player may move an existing station; do not apply the old site's geometry there.
            double dx=(station.lon-e.lon)*111320*Math.Cos(e.lat*Math.PI/180), dz=(station.lat-e.lat)*111320;
            return dx*dx+dz*dz<25?e:null;
        }
        public static Tile LoadTile(string key){
            if(string.IsNullOrEmpty(key))return null;
            Tile tile;
            if(tiles.TryGetValue(key,out tile)){lru.Remove(key);lru.AddLast(key);return tile;}
            var asset=Resources.Load<TextAsset>("Geo/StationAreas/tile_"+key);
            if(asset==null)return null;
            try{
                using(var input=new MemoryStream(asset.bytes,false))
                using(var unzip=new GZipStream(input,CompressionMode.Decompress))
                using(var reader=new StreamReader(unzip,Encoding.UTF8))
                    tile=JsonUtility.FromJson<Tile>(reader.ReadToEnd());
                if(tile==null||tile.features==null)return null;
                while(tiles.Count>=MaximumCachedTiles&&lru.First!=null){tiles.Remove(lru.First.Value);lru.RemoveFirst();}
                tiles[key]=tile;lru.AddLast(key);return tile;
            }catch(Exception e){Debug.LogWarning("역 주변 자료 "+key+"를 읽지 못했습니다: "+e.Message);return null;}
            finally{Resources.UnloadAsset(asset);}
        }
        public static void ClearTileCache(){tiles.Clear();lru.Clear();}
        public static Vector3 Project(Tile tile,Feature feature,int point,Entry station,Vector3 origin,Quaternion rotation){
            double x=(tile.lon-station.lon)*111320*Math.Cos(station.lat*Math.PI/180)
                +feature.points[point*2]*Math.Cos(station.lat*Math.PI/180)/Math.Cos(tile.lat*Math.PI/180);
            double z=(tile.lat-station.lat)*111320+feature.points[point*2+1];
            return origin+rotation*new Vector3((float)x,0,(float)z);
        }
    }
    public class StationAreaEntrance {
        public string id,name,number;public Vector3 position;
    }
    public class StationAreaBuildResult {
        public StationAreaData.Entry entry;
        public GameObject root;
        public int buildings,authoredBuildings,roads,platforms,rails,meshes,skippedUnderground,excludedBuildings,missingTiles;
        public readonly List<StationAreaEntrance> entrances=new List<StationAreaEntrance>();
        public bool HasData {get{return entry!=null;}}
        public string SourceNotice {get{return entry==null?"이 역 주변의 수집된 지도 자료가 없습니다.":"지도: OpenStreetMap · 추가 건물: Overture Maps / Qian Shi 외 East Asian Buildings (CC BY 4.0). 미기재 높이·도로 폭·실내 치수는 추정입니다."+(authoredBuildings>0?" 역사 외관은 사진 참고 추정이며 실측 배치가 아닙니다. 참고 사진: 오수 G43 (CC BY 3.0), 반성 안우석 (public domain).":"");}}
    }
}
