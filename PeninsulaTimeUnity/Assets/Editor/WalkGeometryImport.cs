using UnityEditor;
namespace PeninsulaTime {
public class WalkGeometryImport : AssetPostprocessor {
 void OnPreprocessModel(){if(assetPath.EndsWith("OSM.fbx"))((ModelImporter)assetImporter).isReadable=true;}
 public static void Prepare(){foreach(var guid in AssetDatabase.FindAssets("t:Model",new[]{"Assets/Resources/Models"})){
  var path=AssetDatabase.GUIDToAssetPath(guid);if(!path.EndsWith("OSM.fbx"))continue;
  var importer=(ModelImporter)AssetImporter.GetAtPath(path);if(!importer.isReadable){importer.isReadable=true;importer.SaveAndReimport();}
 }WalkAccessCheck.Run();}
}}
