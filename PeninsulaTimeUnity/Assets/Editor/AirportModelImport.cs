using UnityEditor;
namespace PeninsulaTime {
public class AirportModelImport : AssetPostprocessor {
    const string ModelPath="Assets/Resources/Models/City/Airplane.fbx";
    void OnPreprocessModel(){if(assetPath==ModelPath)((ModelImporter)assetImporter).isReadable=true;}
    [InitializeOnLoadMethod]
    static void SchedulePreparation(){EditorApplication.delayCall+=Prepare;}
    public static void Prepare(){
        var importer=AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if(importer!=null&&!importer.isReadable){importer.isReadable=true;importer.SaveAndReimport();}
    }
}}
