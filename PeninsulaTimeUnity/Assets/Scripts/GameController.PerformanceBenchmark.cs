using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Unity.Profiling;
namespace PeninsulaTime {
    public partial class GameController {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartPerformanceBenchmark() {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"--performance-benchmark")<0||Array.IndexOf(args,"--save-directory")<0)return;
            var game=UnityEngine.Object.FindFirstObjectByType<GameController>();
            if(game!=null)game.StartCoroutine(game.RunPerformanceBenchmark());
        }
        IEnumerator RunPerformanceBenchmark() {
            yield return null;
            var args=Environment.GetCommandLineArgs();
            string output=Path.Combine(SaveDirectory,"performance.json");
            int at=Array.IndexOf(args,"--benchmark-output");if(at>=0&&at+1<args.Length)output=args[at+1];
            string[] scenes=Array.IndexOf(args,"--benchmark-expanded")>=0?new[]{"surface","concourse","platform","terminal","international-checkin","international-gates","national-gangnam","national-seomyeon","national-seoul-hall","gangnam-side-platform"}:new[]{"surface","concourse","platform","terminal"};
            int profile=Array.IndexOf(args,"--benchmark-quality");if(profile>=0&&profile+1<args.Length){int value;if(int.TryParse(args[profile+1],out value))PerformanceRuntime.Apply(value);}
            state.era=9;showIntro=false;EnterDistrict(3,false);
            Application.targetFrameRate=60;
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            var result=new PerformanceReport{version=Application.version,device=SystemInfo.deviceModel,gpu=SystemInfo.graphicsDeviceName,width=Screen.width,height=Screen.height};
            using(var draws=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",1))
            using(var tris=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Triangles Count",1))
            using(var alloc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1)) {
                for(int index=0;index<scenes.Length;index++) {
                    switch(index) {
                        case 0:world.SetDistrictView(3,false);eye.SetPositionAndRotation(viewCamera.transform.position,viewCamera.transform.rotation);break;
                        case 1:Teleport(world.ConcourseSpawn,Vector3.forward);break;
                        case 2:Vector3 face;Teleport(world.PlatformSpawn(2,out face),face);break;
                        case 3:Teleport(WorldBuilder.AirportOrigin+new Vector3(-23.5f,WorldBuilder.Floor2+EyeHeight,-18),Vector3.left);break;
                        case 4:Teleport(world.InternationalCheckInSpawn,Vector3.forward);break;
                        case 5:Teleport(WorldBuilder.InternationalAirportOrigin+new Vector3(175,13.65f,44),Vector3.right);break;
                        case 6:var gangnam=TransitNetwork.Named("강남").Find(s=>s.lines.Exists(l=>l.shortName=="2호선"));VisitNetworkStation(gangnam,gangnam.lines.Find(l=>l.shortName=="2호선"));Teleport(new Vector3(-65,EyeHeight,-81),Vector3.left);break;
                        case 7:var seomyeon=TransitNetwork.Named("서면").Find(s=>s.lines.Exists(l=>l.region=="부산"));VisitNetworkStation(seomyeon);Teleport(new Vector3(-65,EyeHeight,-81),Vector3.left);break;
                        case 8:var seoul=TransitNetwork.Named("서울").Find(s=>s.lines.Exists(l=>l.kind=="ktx"));VisitNetworkStation(seoul,seoul.lines.Find(l=>l.kind=="ktx"));Teleport(world.NetworkTransfers[0].hallPoint+Vector3.up*EyeHeight,Vector3.right);break;
                        case 9:EnterDistrict(0,false);Vector3 gangnamFacing;Teleport(world.PlatformSpawn(0,out gangnamFacing),gangnamFacing);break;
                    }
                    lookLocked=false;PlaceViewCamera();yield return new WaitForSecondsRealtime(4f);
                    var times=new List<float>();long drawSum=0,triSum=0,allocSum=0;
                    float until=Time.realtimeSinceStartup+6f;
                    while(Time.realtimeSinceStartup<until) {
                        yield return null;times.Add(Time.unscaledDeltaTime*1000f);
                        drawSum+=draws.Valid?draws.LastValue:0;triSum+=tris.Valid?tris.LastValue:0;allocSum+=alloc.Valid?alloc.LastValue:0;
                    }
                    times.Sort();float sum=0;foreach(float ms in times)sum+=ms;
                    int count=Mathf.Max(1,times.Count),active=0;
                    var renderers=world.root.GetComponentsInChildren<Renderer>(true);
                    foreach(var r in renderers)if(r.enabled&&!r.forceRenderingOff&&r.gameObject.activeInHierarchy)active++;
                    var sample=new PerformanceSample{name=scenes[index],frames=times.Count,meanMs=sum/count,p95Ms=times[Mathf.Min(times.Count-1,(int)(times.Count*.95f))],drawCalls=drawSum/count,triangles=triSum/count,gcBytes=alloc.Valid?allocSum/count:-1,renderers=renderers.Length,activeRenderers=active,memoryMB=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()/1048576f};
                    result.samples.Add(sample);Debug.Log("PERF "+JsonUtility.ToJson(sample));
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(result,true));
            Debug.Log("PERFORMANCE COMPLETE "+output);Application.Quit();
        }
    }
    [Serializable] public class PerformanceReport {public string version,device,gpu;public int width,height;public List<PerformanceSample> samples=new List<PerformanceSample>();}
    [Serializable] public class PerformanceSample {public string name;public int frames,renderers,activeRenderers;public float meanMs,p95Ms,memoryMB;public long drawCalls,triangles,gcBytes;}
}
