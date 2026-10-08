using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Renders camera shots of the modern Seoul districts to PNG files in $PREVIEW_DIR.
    // $PREVIEW_SHOTS: "district:x,y,z:lookX,lookY,lookZ:name;..." (Unity metres). Traffic runs $PREVIEW_SECONDS first.
    // Unity -batchmode -projectPath . -executeMethod PeninsulaTime.DistrictPreview.Run
    public static class DistrictPreview
    {
        static Vector3 Vec(string text)
        {
            var v=text.Split(',');
            return new Vector3(float.Parse(v[0],CultureInfo.InvariantCulture),float.Parse(v[1],CultureInfo.InvariantCulture),float.Parse(v[2],CultureInfo.InvariantCulture));
        }
        public static void Run()
        {
            string dir=Environment.GetEnvironmentVariable("PREVIEW_DIR");
            string shots=Environment.GetEnvironmentVariable("PREVIEW_SHOTS");
            if(string.IsNullOrEmpty(dir)||string.IsNullOrEmpty(shots)){Debug.LogError("DistrictPreview: set PREVIEW_DIR and PREVIEW_SHOTS");EditorApplication.Exit(1);return;}
            float seconds;if(!float.TryParse(Environment.GetEnvironmentVariable("PREVIEW_SECONDS")??"",NumberStyles.Float,CultureInfo.InvariantCulture,out seconds))seconds=20f;
            Directory.CreateDirectory(dir);
            var camera=new GameObject("Preview Camera").AddComponent<Camera>();
            camera.farClipPlane=2500f;camera.fieldOfView=60f;
            var world=new GameObject("Preview World").AddComponent<WorldBuilder>();
            world.worldCamera=camera;
            int built=-1;
            foreach(var shot in shots.Split(';'))
            {
                var parts=shot.Split(':');if(parts.Length<4)continue;
                int district=int.Parse(parts[0]);
                if(district!=built)
                {
                    world.BuildDistrict(district,9);built=district;
                    var director=world.root.GetComponentInChildren<TrafficDirector>();
                    var trains=world.root.GetComponentsInChildren<RailVehicle>();
                    for(float t=0;t<seconds;t+=.05f)
                    {
                        if(director!=null)director.Step(.05f);
                        foreach(var train in trains)train.Step(.05f);
                    }
                }
                if(parts[1]=="ride")
                {
                    // "ride:side" sits in the lead car of that platform's train, as GameController does after boarding.
                    var train=world.StationTrains[int.Parse(parts[2])];train.ArriveNow();
                    world.CreateRideCabin(train.leadCar.gameObject,"metro");
                    foreach(Transform child in train.leadCar)if(child.name.StartsWith("MetroCabin"))child.position+=Vector3.up*.9f; // world space: the car FBX is scaled and rotated
                    world.AddCabinPassengers(train.leadCar,Vector3.forward*train.direction,train.leadCar.position.y+1.02f,7);
                    var ahead=Vector3.forward*train.direction;
                    camera.transform.position=train.leadCar.position+Vector3.up*2.65f-ahead*4f;camera.transform.rotation=Quaternion.LookRotation(ahead);
                }
                else if(parts[1].StartsWith("@"))
                {
                    // "@Name:offset" frames the first generated object whose name starts with Name.
                    Transform target=null;
                    foreach(var t in world.root.GetComponentsInChildren<Transform>())if(t.name.StartsWith(parts[1].Substring(1))){target=t;break;}
                    if(target==null){Debug.LogWarning("DistrictPreview: no "+parts[1]);continue;}
                    camera.transform.position=target.position+Quaternion.Euler(0,target.eulerAngles.y,0)*Vec(parts[2]);camera.transform.LookAt(target.position+Vector3.up*1.5f); // offset in the target's heading frame
                }
                else{camera.transform.position=Vec(parts[1]);camera.transform.LookAt(Vec(parts[2]));}world.UpdateInteriorLighting(camera.transform.position);
                Capture(camera,Path.Combine(dir,parts[3]+".png"));
            }
            Debug.Log("DistrictPreview: wrote "+dir);
            EditorApplication.Exit(0);
        }
        public static void Capture(Camera camera,string path)
        {
            var target=new RenderTexture(1600,1000,24);camera.targetTexture=target;camera.Render();
            RenderTexture.active=target;var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();
            File.WriteAllBytes(path,image.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
