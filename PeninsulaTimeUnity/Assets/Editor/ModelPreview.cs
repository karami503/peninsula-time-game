using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Renders the city plot with every building, and both ride cabins, to PNG files in $PREVIEW_DIR.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.ModelPreview.Run
    public static class ModelPreview
    {
        public static void Run()
        {
            string dir=Environment.GetEnvironmentVariable("PREVIEW_DIR");
            if(string.IsNullOrEmpty(dir)){Debug.LogError("ModelPreview: set PREVIEW_DIR");EditorApplication.Exit(1);return;}
            Directory.CreateDirectory(dir);
            var camera=new GameObject("Preview Camera").AddComponent<Camera>();
            var world=new GameObject("Preview World").AddComponent<WorldBuilder>();
            world.worldCamera=camera;
            var state=new GameState();
            foreach(var info in GameContent.Buildings)state.buildings.Add(new PlacedBuilding{id=info.id,city=state.selectedCity});
            for(int column=0;column<6;column++)state.roads.Add(new RoadSegment{city=state.selectedCity,axis=0,row=2,column=column});
            for(int row=0;row<4;row++)state.roads.Add(new RoadSegment{city=state.selectedCity,axis=1,row=row,column=3});
            world.BuildCityPlot(state);
            Capture(camera,Path.Combine(dir,"city-plot.png"));
            camera.orthographic=false;camera.fieldOfView=50;
            camera.transform.position=new Vector3(-30,22,-48);camera.transform.LookAt(new Vector3(-8,4,-5));
            Capture(camera,Path.Combine(dir,"city-street.png"));
            world.BuildDistrict(0,9);
            camera.fieldOfView=60;
            camera.transform.position=new Vector3(-20,9,-40);camera.transform.LookAt(new Vector3(10,2,20));
            Capture(camera,Path.Combine(dir,"seoul-street.png"));
            camera.transform.position=new Vector3(-160,140,-200);camera.transform.LookAt(new Vector3(0,0,0));
            Capture(camera,Path.Combine(dir,"seoul-aerial.png"));
            Debug.Log("ModelPreview: Seoul traffic vehicles "+world.root.GetComponentsInChildren<TrafficVehicle>().Length);
            foreach(var type in new[]{"bus","metro"})
            {
                world.BuildDistrict(0,9);
                var vehicle=world.CreateVehicle(type,new Vector3(0,.8f,0));
                world.CreateRideCabin(vehicle,type);
                camera.fieldOfView=72;
                camera.transform.position=new Vector3(0,.8f+1.65f,-1.6f);camera.transform.LookAt(new Vector3(0,2.4f,8));
                Capture(camera,Path.Combine(dir,type+"-cabin.png"));
            }
            Debug.Log("ModelPreview: wrote "+dir);
            EditorApplication.Exit(0);
        }
        static void Capture(Camera camera,string path)
        {
            var target=new RenderTexture(1600,1000,24);camera.targetTexture=target;camera.Render();
            RenderTexture.active=target;var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();
            File.WriteAllBytes(path,image.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
