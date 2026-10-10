using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Renders shots of the carved 진해 around 진해역 to PNG files in $PREVIEW_DIR (camera positions in the station frame:
    // x along the track, z toward it).
    // Unity -batchmode -projectPath . -executeMethod PeninsulaTime.JinhaePreview.Run
    public static class JinhaePreview
    {
        public static void Run()
        {
            string dir=Environment.GetEnvironmentVariable("PREVIEW_DIR");
            if(string.IsNullOrEmpty(dir)){Debug.LogError("JinhaePreview: set PREVIEW_DIR");EditorApplication.Exit(1);return;}
            Directory.CreateDirectory(dir);
            var camera=new GameObject("Preview Camera").AddComponent<Camera>();camera.farClipPlane=24000f;camera.fieldOfView=60f;
            var world=new GameObject("Preview World").AddComponent<WorldBuilder>();world.worldCamera=camera;world.viewer=camera.transform;
            world.BuildJinhae();
            var station=world.JinhaeStation;float d=world.JinhaeTrackOffset;
            for(int i=0;i<600;i++)world.Carved.MovePeople(1f/30f);
            var shots=new[]{
                new object[]{"front",new Vector3(-14f,2f,-30f),new Vector3(0,3f,0)},
                new object[]{"hall",new Vector3(8f,1.7f,4.5f),new Vector3(-6f,1.5f,-3f)},
                new object[]{"platform",new Vector3(-30f,2.4f,d-4.5f),new Vector3(10f,1.5f,d-3f)},
                new object[]{"square",new Vector3(3f,1.7f,-8f),new Vector3(5f,1.5f,-80f)},
                new object[]{"aerial",new Vector3(-90f,70f,-140f),new Vector3(0,0,-20f)},
                new object[]{"street",new Vector3(9f,1.7f,-150f),new Vector3(14f,1.5f,-300f)},
                new object[]{"train",new Vector3(-12f,2.2f,d-5.5f),new Vector3(6f,1.8f,d)}};
            foreach(var shot in shots)
            {
                var at=station.TransformPoint((Vector3)shot[1]);var look=station.TransformPoint((Vector3)shot[2]);
                camera.transform.position=at;camera.transform.LookAt(look);
                world.Carved.BuildAround(at,200f);Physics.SyncTransforms();
                DistrictPreview.Capture(camera,Path.Combine(dir,"jinhae-"+shot[0]+".png"));
            }
            // 경화역 with the train standing there, and the land from the air: the town, the sea, the hills.
            var line=world.JinhaeLine;line.Begin(1);var g=world.GyeonghwaStation;
            var far=new[]{
                new object[]{"gyeonghwa",g.TransformPoint(new Vector3(16f,3f,-28f)),g.TransformPoint(new Vector3(2f,1.5f,4f))},
                new object[]{"land",new Vector3(1800f,420f,-3200f),new Vector3(200f,60f,600f)},
                new object[]{"hills",station.TransformPoint(new Vector3(-20f,40f,-260f)),station.TransformPoint(new Vector3(200f,120f,1500f))}};
            foreach(var shot in far)
            {
                var at=(Vector3)shot[1];camera.transform.position=at;camera.transform.LookAt((Vector3)shot[2]);
                world.Carved.BuildAround(at,200f);Physics.SyncTransforms();
                DistrictPreview.Capture(camera,Path.Combine(dir,"jinhae-"+shot[0]+".png"));
            }
            Debug.Log("JinhaePreview: wrote "+dir);
            EditorApplication.Exit(0);
        }
    }
}
