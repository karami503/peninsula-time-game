using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeninsulaTime.Editor
{
    public static class ProjectBuilder
    {
        [MenuItem("반도의 시간/시작 장면 생성")]
        public static void Generate()
        {
            Directory.CreateDirectory("Assets/Resources");
            var baseMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/RuntimeBase.mat");
            if(baseMaterial==null)
            {
                baseMaterial=new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(baseMaterial,"Assets/Resources/RuntimeBase.mat");
            }
            if(AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/TerrainBase.mat")==null)
            {
                var terrainShader=Shader.Find("Peninsula/VertexTerrain");
                var terrainMaterial=new Material(terrainShader!=null?terrainShader:Shader.Find("Standard"));
                AssetDatabase.CreateAsset(terrainMaterial,"Assets/Resources/TerrainBase.mat");
            }
            if(AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/DistrictBase.mat")==null)
            {
                var cityShader=Shader.Find("Peninsula/DoubleSidedCity");
                var cityMaterial=new Material(cityShader!=null?cityShader:Shader.Find("Standard"));
                AssetDatabase.CreateAsset(cityMaterial,"Assets/Resources/DistrictBase.mat");
            }
            var sky=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Sky.mat");
            if(sky==null)
            {
                var shader=Shader.Find("Skybox/Procedural");
                if(shader!=null){sky=new Material(shader);AssetDatabase.CreateAsset(sky,"Assets/Resources/Sky.mat");}
            }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var cameraObject=new GameObject("Main Camera");cameraObject.tag="MainCamera";
            var camera=cameraObject.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.Skybox;camera.fieldOfView=48;
            cameraObject.AddComponent<AudioListener>();
            var game=new GameObject("Peninsula Time Game");game.AddComponent<PeninsulaTime.GameController>();
            RenderSettings.skybox=sky;RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            var path="Assets/Scenes/Main.unity";Directory.CreateDirectory("Assets/Scenes");EditorSceneManager.SaveScene(scene,path);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(path,true)};
            PlayerSettings.companyName="Kim Garam";PlayerSettings.productName="반도의 시간";
            PlayerSettings.bundleVersion="0.9.9-preview.1";
            // Online servers on a LAN or home PC speak plain HTTP; public servers should sit behind HTTPS.
            PlayerSettings.insecureHttpOption=InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone,"com.kimgaram.peninsulatime");
            PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.runInBackground=true;
            AssetDatabase.SaveAssets();
            Debug.Log("Peninsula Time scene generated: "+path);
        }
        [MenuItem("반도의 시간/macOS 앱 빌드")]
        public static void BuildMac()
        {
            Generate();
            Directory.CreateDirectory("Builds");
            var report=BuildPipeline.BuildPlayer(new[]{"Assets/Scenes/Main.unity"},"Builds/PeninsulaTime.app",BuildTarget.StandaloneOSX,BuildOptions.None);
            Debug.Log("Build result: "+report.summary.result+", errors: "+report.summary.totalErrors+", size: "+report.summary.totalSize);
            if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new System.Exception("macOS build failed");
        }
        [MenuItem("반도의 시간/Windows 앱 빌드")]
        public static void BuildWindows()
        {
            Generate();
            Directory.CreateDirectory("Builds/Windows");
            var report=BuildPipeline.BuildPlayer(new[]{"Assets/Scenes/Main.unity"},"Builds/Windows/PeninsulaTime.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
            Debug.Log("Windows build result: "+report.summary.result+", errors: "+report.summary.totalErrors);
            if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new System.Exception("Windows build failed");
        }
        [MenuItem("반도의 시간/Android APK 빌드")]
        public static void BuildAndroid()
        {
            Generate();
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android,"com.kimgaram.peninsulatime");
            EditorUserBuildSettings.buildAppBundle=false;
            Directory.CreateDirectory("Builds");
            var report=BuildPipeline.BuildPlayer(new[]{"Assets/Scenes/Main.unity"},"Builds/PeninsulaTime.apk",BuildTarget.Android,BuildOptions.None);
            Debug.Log("Android build result: "+report.summary.result+", errors: "+report.summary.totalErrors);
            if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new System.Exception("Android build failed");
        }
    }
}
