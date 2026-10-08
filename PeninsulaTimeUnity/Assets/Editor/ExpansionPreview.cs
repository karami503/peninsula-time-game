using System.IO;
using UnityEngine;
using UnityEditor;
namespace PeninsulaTime {
public static class ExpansionPreview {
public static void Run(){
 var dir=System.Environment.GetEnvironmentVariable("PREVIEW_DIR");Directory.CreateDirectory(dir);
 var camera=new GameObject("camera").AddComponent<Camera>();camera.farClipPlane=3000;var world=new GameObject("world").AddComponent<WorldBuilder>();world.worldCamera=camera;
 TransitNetwork.Build(null);
 var at=TransitNetwork.Named("사당").Find(s=>s.grade=="underground")??TransitNetwork.Stations.Find(s=>s.grade=="underground"&&s.lines.Count>1);var line=at.lines.Find(l=>l.kind=="metro");
 var service=world.BuildNetworkStation(at,line,0);world.UpdateInteriorLighting(new Vector3(0,-5,0));camera.transform.position=new Vector3(8,service.origin.y+8.45f,-79);camera.transform.LookAt(new Vector3(40,service.origin.y+8.45f,-73));DistrictPreview.Capture(camera,Path.Combine(dir,"전국역-공용대합실.png"));
 var busLine=TransitNetwork.Lines.Find(l=>l.id.StartsWith("cw-bus-")&&l.shortName=="105")??TransitNetwork.Lines.Find(l=>l.id.StartsWith("cw-bus-"));
 var start=busLine.stops.Find(s=>s.lat>35.2f&&s.lon>128.65f&&s.lon<128.7f)??busLine.stops[2];
 service=world.BuildNetworkStation(start,busLine,0);var c=service.doors.cabin;
 camera.transform.position=c.transform.TransformPoint(new Vector3(10,3,12));camera.transform.LookAt(c.transform.position+Vector3.up*1.5f);DistrictPreview.Capture(camera,Path.Combine(dir,"창원-실제노선-버스.png"));
 camera.transform.position=c.transform.position+new Vector3(100,130,-120);camera.transform.LookAt(c.transform.position);DistrictPreview.Capture(camera,Path.Combine(dir,"창원-주변도로-건물.png"));
 world.BuildDistrict(0,9);
 foreach(var route in world.root.GetComponentsInChildren<StationWalkRoute>()){
  if(route.points.Length<4)continue;
  var a=route.points[2];var b=route.points[3];if(Vector3.Distance(a,b)<8)continue;
  camera.transform.position=Vector3.Lerp(a,b,.5f)+Vector3.up*1.65f;camera.transform.LookAt(b+Vector3.up*1.65f);world.UpdateInteriorLighting(camera.transform.position);
  DistrictPreview.Capture(camera,Path.Combine(dir,"강남-연결통로.png"));break;
 }
 Debug.Log("ExpansionPreview: passed");EditorApplication.Exit(0);
}}}
