using System;
using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime {
    // Published station floor labels and diagram references. These are not surveyed CAD dimensions.
    public static class StationPlanData {
        [Serializable] public class Entry {
            public string stationId,lineId,name,lineName;
            public bool hasPlatformFloor;
            public int platformFloor,planCount;
            public int[] platformFloors;
            public string platformFloorLabel,platformType,architectureFloorLabel;
            public float platformLength;
            public string[] sourceIds;
            public string sourceDate,planURL,sourceURL;
            // Raw CSV fields above remain unchanged, including published mistakes.
            public string floorQuality,floorQualityNote,floorEvidenceURL;
            public int[] verifiedPlatformFloors;
            public string verifiedPlatformFloorLabel,reviewedPlatformType;
            public bool floorGeometryAllowed;
            public bool HasDiagram { get { return planCount>0&&!string.IsNullOrEmpty(planURL); } }
            // A floor number is a label, not an absolute depth or a complete station layout.
            public bool HasSingleFloor { get { return hasPlatformFloor&&platformFloors!=null&&platformFloors.Length==1; } }
            public bool HasVerifiedFloor { get { return verifiedPlatformFloors!=null&&verifiedPlatformFloors.Length>0&&!string.IsNullOrEmpty(floorEvidenceURL); } }
            // Use this in reference panels instead of calling raw CSV values verified facts.
            public string FloorReferenceLabel { get {
                if(HasVerifiedFloor)return "안내도에서 확인한 승강장 층: "+verifiedPlatformFloorLabel+(floorQuality=="source-conflict"?" (CSV와 불일치)":"");
                if(platformFloors!=null&&platformFloors.Length>0)return "공개 CSV 층 표기: "+platformFloorLabel+" (안내도 대조 전)";
                return "승강장 층 미확인";
            } }
            // Even reviewed floor labels cannot supply measured depths or an interior shape.
            public bool CanUseFloorForGeometry { get { return false; } }
        }
        [Serializable] public class Coverage {
            public int gameStationIds,withOfficialPlatformData,withOfficialPlan,matchedStationLinePairs;
            public int multiLevelPairs,publicPlatformDatasets,publicStructureDatasets,exactInterior;
            public int diagramReviewedPairs,sourceConflictPairs,unreviewedFloorPairs;
        }
        [Serializable] public class Manifest {
            public int version;public string retrieved,notice;public Coverage stats;public Entry[] rows;
        }
        static readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
        static bool loaded;
        static Manifest manifest;
        static string Key(string stationId,string lineId){return stationId+"|"+lineId;}
        public static Manifest Index { get { Load();return manifest; } }
        static void Load(){
            if(loaded)return;loaded=true;
            var asset=Resources.Load<TextAsset>("Geo/StationPlans/index");
            if(asset==null)return;
            try{
                manifest=JsonUtility.FromJson<Manifest>(asset.text);
                if(manifest==null||manifest.version!=1||manifest.rows==null)return;
                foreach(var row in manifest.rows)
                    if(row!=null&&!string.IsNullOrEmpty(row.stationId)&&!string.IsNullOrEmpty(row.lineId))
                        entries[Key(row.stationId,row.lineId)]=row;
            }catch(Exception e){Debug.LogWarning("역 구조 참고 자료를 읽지 못했습니다: "+e.Message);}
            finally{Resources.UnloadAsset(asset);}
        }
        public static Entry ForStation(NetStation station,NetLine line){
            if(station==null||line==null||station.custom||line.custom||line.planned)return null;
            // Player-relocated stations must not inherit a real station's floor labels and diagram.
            if(StationAreaData.ForStation(station)==null)return null;
            Load();Entry row;
            return entries.TryGetValue(Key(station.id,line.id),out row)?row:null;
        }
    }
}
