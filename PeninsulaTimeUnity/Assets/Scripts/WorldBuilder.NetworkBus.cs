using System.Collections.Generic;
using UnityEngine;
namespace PeninsulaTime {
public partial class WorldBuilder {
    // Changwon BIS gives ordered shape points as well as stop coordinates. Retain the measured curves.
    StationJourney BuildNetworkBus(NetStation start,NetLine line,int direction){
        Clear();SetupLight(new Color(.53f,.57f,.61f),new Color(1f,.92f,.8f));worldCamera.orthographic=false;worldCamera.fieldOfView=72;dayNight=true;
        var service=root.AddComponent<StationJourney>();service.line=line;service.direction=direction;service.stops.AddRange(StationJourney.Next(line,start,direction));
        float scale=111320f*Mathf.Cos(start.lat*Mathf.Deg2Rad);
        System.Func<Vector2,Vector3> project=v=>new Vector3((v.x-start.lon)*scale,.06f,(v.y-start.lat)*111320f);
        var full=new List<Vector3>();foreach(var shape in line.shapes)foreach(var xy in shape){var p=project(xy);if(line.stopShapeIndices.Length>0||full.Count==0||Vector3.Distance(full[full.Count-1],p)>.1f)full.Add(p);}
        if(full.Count<2){full.Clear();foreach(var stop in line.stops)full.Add(project(new Vector2(stop.lon,stop.lat)));}
        // Match the whole ordered stop list first, avoiding the return carriageway at the same coordinates.
        var indices=new int[line.stops.Count];int cursor=0;
        for(int si=0;si<line.stops.Count;si++){var stop=line.stops[si];var p=project(new Vector2(stop.lon,stop.lat));int best=cursor;float distance=float.MaxValue;
            for(int k=cursor;k<full.Count;k++){float d=(full[k]-p).sqrMagnitude;if(d<distance){distance=d;best=k;}}
            // Choose the first matching carriageway near the minimum, not a later return pass.
            for(int k=cursor;k<best;k++)if((full[k]-p).sqrMagnitude<=distance+16f){best=k;break;}
            indices[si]=best;cursor=best;
        }
        if(line.stopShapeIndices.Length==line.stops.Count)indices=line.stopShapeIndices;
        int startIndex=line.stops.IndexOf(start),stopStep=direction==0?1:-1;
        int from=indices[startIndex],to=indices[Mathf.Clamp(startIndex+stopStep*(service.stops.Count-1),0,indices.Length-1)],step=to>=from?1:-1;
        var path=new List<Vector3>();for(int k=from;;k+=step){path.Add(full[k]);if(k==to)break;}
        if(path.Count<2)path.Add(path[0]+Vector3.forward*10);
        var lengths=new float[path.Count];for(int k=1;k<path.Count;k++)lengths[k]=lengths[k-1]+Vector3.Distance(path[k-1],path[k]);
        var stopsAt=new float[service.stops.Count];for(int k=0;k<stopsAt.Length;k++)stopsAt[k]=lengths[Mathf.Clamp(Mathf.Abs(indices[Mathf.Clamp(startIndex+stopStep*k,0,indices.Length-1)]-from),0,lengths.Length-1)];
        service.SetPath(path.ToArray(),stopsAt);
        var bounds=new Bounds(path[0],Vector3.zero);foreach(var p in path)bounds.Encapsulate(p);
        Primitive(PrimitiveType.Cube,"주변 지면",root.transform,new Vector3(bounds.center.x,-.28f,bounds.center.z),new Vector3(bounds.size.x+2400,.5f,bounds.size.z+2400),Mat("cw-land",new Color(.43f,.46f,.37f)));
        var road=new List<Vector3>(path);road.Insert(0,path[0]+(path[0]-path[1]).normalized*120);road.Add(path[path.Count-1]+(path[path.Count-1]-path[path.Count-2]).normalized*120);
        for(int k=0;k<road.Count;k++)road[k]-=Vector3.up*.06f;
        RoadRibbon(root.transform,road,7,"창원 BIS 실제 도로 경로",Mat("cw-asphalt",new Color(.22f,.24f,.25f),0,"asphalt",4),true);
        AddCityContext(start,Vector3.zero);
        var concrete=Mat("cw-stop-pavement",new Color(.67f,.68f,.66f),0,"pavement",2);
        for(int k=0;k<service.stops.Count;k++){
            int ix=Mathf.Clamp(Mathf.Abs(indices[Mathf.Clamp(startIndex+stopStep*k,0,indices.Length-1)]-from),0,path.Count-1);var p=path[ix];var forward=path[Mathf.Min(ix+1,path.Count-1)]-path[Mathf.Max(0,ix-1)];forward.Normalize();var right=Vector3.Cross(Vector3.up,forward);
            var walk=new GameObject(service.stops[k].name+" 정류장 보도").transform;walk.SetParent(root.transform,false);walk.position=p+right*4.3f-Vector3.up*.06f;walk.rotation=Quaternion.LookRotation(forward);
            Block("보도",walk,new Vector3(-1.2f,-.2f,-12),new Vector3(3,.02f,12),concrete);
            // Changwon reference: long flat dark roof, glass waiting space and striped guardrails.
            var steel=Mat("cw-shelter-frame",new Color(.13f,.16f,.16f),.4f);var glass=VehicleGlass();
            Block("창원 정류장 직선 지붕",walk,new Vector3(-.6f,2.8f,-7),new Vector3(2.6f,3,7),steel);
            foreach(float z in new[]{-6.8f,-3.4f,0,3.4f,6.8f})Block("유리 대기실 기둥",walk,new Vector3(2.1f,0,z-.04f),new Vector3(2.2f,2.8f,z+.04f),steel);
            Block("대기실 뒷유리",walk,new Vector3(2.2f,.35f,-6.7f),new Vector3(2.25f,2.65f,6.7f),glass);
            foreach(float z in new[]{-6.8f,6.8f})Block("대기실 옆유리",walk,new Vector3(.2f,.35f,z-.04f),new Vector3(2.2f,2.65f,z+.04f),glass);
            Board(service.stops[k].name+" · "+line.shortName,walk,walk.TransformPoint(new Vector3(.5f,2.55f,0)),-right,new Vector2(6,.4f),new Color(.1f,.15f,.14f),Color.white,.21f);
            BusPanel(service.stops[k],walk,0,7f);
            Marker(walk.position,service.stops[k].name);
        }
        var bus=CityModel("Bus",path[0]);if(bus!=null){service.train=bus.transform;FitBoxCollider(bus);service.doors=AttachBusCabin(bus,379);service.doors.cabin.kind="networkrail";service.doors.Set(1);RouteSign(bus,line.shortName);Sfx.Attach(bus,"bus-engine",.4f,35);service.Step(0);}
        return service;
    }
}}
