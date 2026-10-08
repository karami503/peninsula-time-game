using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Batch check: clicking anywhere in the gap beside a road candidate must hit that candidate.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.RoadClickCheck.Run
    public static class RoadClickCheck
    {
        const float GapEdgeOffset=1.6f;
        public static void Run()
        {
            var cameraObject=new GameObject("Check Camera");
            var camera=cameraObject.AddComponent<Camera>();
            camera.pixelRect=new Rect(0,0,1920,1080);
            var world=new GameObject("Check World").AddComponent<WorldBuilder>();
            world.worldCamera=camera;
            world.BuildCityPlot(new GameState());
            Physics.SyncTransforms();
            int checkedCount=0,failed=0;
            foreach(var marker in world.root.GetComponentsInChildren<RoadMarker>())
            {
                if(marker.GetComponent<Renderer>().enabled){failed++;Debug.LogError("Unbuilt road is visible without road tool");}
                var side=marker.axis==0?Vector3.forward:Vector3.right;
                foreach(var sign in new[]{1f,-1f})
                {
                    var target=marker.transform.position+side*GapEdgeOffset*sign;
                    RaycastHit hit;checkedCount++;
                    bool ok=Physics.Raycast(camera.ScreenPointToRay(camera.WorldToScreenPoint(target)),out hit,250f)&&hit.collider.GetComponent<RoadMarker>()==marker;
                    if(!ok){failed++;Debug.LogError("Road click miss: axis "+marker.axis+" row "+marker.row+" column "+marker.column+" hit "+(hit.collider!=null?hit.collider.name:"nothing"));}
                }
            }
            world.SetRoadCandidates(true);
            foreach(var marker in world.root.GetComponentsInChildren<RoadMarker>())
                if(!marker.GetComponent<Renderer>().enabled){failed++;Debug.LogError("Road candidate is hidden with road tool");}
            Debug.Log("RoadClickCheck: "+(checkedCount-failed)+"/"+checkedCount+" passed");
            Object.DestroyImmediate(world.gameObject);Object.DestroyImmediate(cameraObject);
            EditorApplication.Exit(failed==0&&checkedCount>0?0:1);
        }
    }
}
