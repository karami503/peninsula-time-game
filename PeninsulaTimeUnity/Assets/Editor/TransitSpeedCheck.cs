using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PeninsulaTime
{
    // Small geometry-free checks: faster travel, full dwell windows and deterministic long-frame handling.
    public static class TransitSpeedCheck
    {
        static int failures;
        static void Expect(bool ok,string message){if(!ok){failures++;Debug.LogError("TransitSpeedCheck: "+message);}}
        static StationJourney Journey(string kind,bool board=true)
        {
            var go=new GameObject("Speed check "+kind);var journey=go.AddComponent<StationJourney>();
            journey.line=new NetLine{id="speed-check-"+kind,kind=kind,firstMinute=0,lastMinute=1440};
            for(int i=0;i<4;i++){var stop=new NetStation{id=kind+i,name="Stop "+i};journey.stops.Add(stop);journey.line.stops.Add(stop);}
            var body=new GameObject("Vehicle");body.transform.SetParent(go.transform,false);journey.train=body.transform;
            journey.doors=body.AddComponent<VehicleDoors>();journey.doors.cabin=body.AddComponent<Cabin>();journey.doors.Set(1);
            if(board)journey.Board();return journey;
        }
        static void CheckJourney(string kind)
        {
            var fine=Journey(kind);var jump=Journey(kind);float time=0;
            try
            {
                // Unscaled boarding hold; travelling begins only after all leaves are shut.
                fine.Step(6);Expect(fine.travelled==0&&fine.doors.amount>.99f,kind+" boarding time shortened");
                fine.Step(2);Expect(fine.travelled==0&&fine.doors.amount<.001f,kind+" did not close before moving");time=8;
                foreach(float chunk in new[]{.125f,1.375f,3.25f,5.5f,11f,17.75f,30f,120f})
                {
                    float left=chunk;
                    while(left>0){float step=Mathf.Min(left,1f/60f);fine.Step(step);left-=step;
                        Expect(fine.speed<=.01f||fine.doors.amount<.001f,kind+" moved with doors open");}
                    time+=chunk;
                    Object.DestroyImmediate(jump.gameObject);jump=Journey(kind);jump.Step(time);
                    Expect(fine.index==jump.index,kind+" time jump changed stop index at "+time);
                    Expect(Mathf.Abs(fine.travelled-jump.travelled)<.03f,kind+" time jump changed distance at "+time);
                    Expect(Mathf.Abs(fine.wait-jump.wait)<.004f,kind+" time jump changed dwell at "+time);
                    Expect(Mathf.Abs(fine.doors.amount-jump.doors.amount)<.005f,kind+" time jump changed door state at "+time);
                }
                Expect(fine.Finished&&fine.Stopped&&Mathf.Abs(fine.travelled-1350)<.01f,kind+" final station inaccessible");
            }
            finally{Object.DestroyImmediate(fine.gameObject);Object.DestroyImmediate(jump.gameObject);}
            if(kind!="bus")Expect(TransitSpeed.JourneyDuration(kind,450)<9f,kind+" 450m running section takes too long");
        }
        static void CheckIdleAndCadence()
        {
            foreach(string kind in new[]{"metro","ktx","mugunghwa","bus"})
            {
                var fine=Journey(kind,false);var jump=Journey(kind,false);
                try
                {
                    fine.startDelay=jump.startDelay=15;
                    fine.Step(14);Expect(fine.startDelay==1&&fine.clock==0,"line start phase changed");
                    for(int i=0;i<480;i++)fine.Step(1f/60f);
                    jump.Step(22);
                    Expect(Mathf.Abs(fine.clock-jump.clock)<.001f&&Vector3.Distance(fine.train.position,jump.train.position)<.02f,kind+" delay overshoot was discarded");
                    Expect(TransitSchedule.Headway(fine.line,43200)==(kind=="bus"?50:30),kind+" headway changed");
                    var times=TransitSchedule.Departures(fine.line,0);Expect(times.Length>3&&times[1]-times[0]==fine.Period,kind+" departure cadence changed");
                    var reverse=TransitSchedule.Departures(fine.line,1);Expect(reverse[0]-times[0]==15,"direction phase changed");
                    fine.clock=0;fine.Step(1.5f);Expect(fine.Stopped,"idle train did not open for boarding");
                    fine.Step(5);Expect(fine.Stopped,"idle boarding window shortened");
                }
                finally{Object.DestroyImmediate(fine.gameObject);Object.DestroyImmediate(jump.gameObject);}
            }
            Expect(SubwayTrain.Dwell==12&&RailVehicle.DwellSeconds==10,"platform dwell shortened");
            var train=new GameObject("Metro speed check").AddComponent<SubwayTrain>();
            try
            {
                float position=train.Offset(0);Expect(Mathf.Abs(position+168.75f)<.001f,"approach distance changed instead of travel speed");
                Expect(SubwayTrain.Approach==3.75f&&SubwayTrain.CruiseSpeed==90,"metro 4x profile missing");
                for(float t=SubwayTrain.Approach+1.8f;t<SubwayTrain.Approach+SubwayTrain.Dwell-2.3f;t+=.25f)
                    Expect(SubwayTrain.DoorsOpenAt(t)&&train.Offset(t)==0,"scheduled metro doors shortened or moving during dwell");
            }
            finally{Object.DestroyImmediate(train.gameObject);}
            // Trackside rail uses absolute door phase, so jumps cannot leave an open moving train.
            var rail=new GameObject("Rail speed check").AddComponent<RailVehicle>();
            try
            {
                rail.doors=rail.gameObject.AddComponent<VehicleDoors>();rail.doors.cabin=rail.gameObject.AddComponent<Cabin>();
                rail.Begin(new List<Vector3>{Vector3.zero,Vector3.forward*500},12);rail.Serve(100,RailVehicle.Approach+3);
                Expect(rail.doors.cabin.Open&&rail.Dwelling,"rail platform doors do not open at stop");
                rail.Step(8);Expect(!rail.doors.cabin.Open,"time jump left rail doors open while moving");
            }
            finally{Object.DestroyImmediate(rail.gameObject);}
        }
        static void CheckBelt()
        {
            var belt=new GameObject("Belt speed check").AddComponent<MovingWalkway>();
            try
            {
                belt.length=20;belt.width=1.2f;belt.speed=1.15f;belt.transform.rotation=Quaternion.Euler(0,90,0);belt.Register();
                var v=MovingWalkway.VelocityAt(Vector3.up*.05f);
                Expect(Mathf.Abs(v.magnitude-3.45f)<.001f&&Vector3.Dot(v.normalized,Vector3.right)>.999f,"belt did not carry at 3x in its own direction");
                Expect(MovingWalkway.VelocityAt(Vector3.up*.5f)==Vector3.zero,"belt carries airborne player");
                Expect(MovingWalkway.VelocityAt(Vector3.forward*2)==Vector3.zero,"belt carries player outside belt width");
            }
            finally{Object.DestroyImmediate(belt.gameObject);}
        }
        public static void Run()
        {
            failures=0;
            try
            {
                foreach(string kind in new[]{"metro","ktx","mugunghwa","bus"})CheckJourney(kind);
                CheckIdleAndCadence();CheckBelt();
                Expect(Mathf.Abs(TransitSpeed.RunningSeconds("metro",10,40)-225)<.001f,"schedule running duration not accelerated");
                Expect(Mathf.Abs(TransitSpeed.RunningSeconds("bus",10,40)-900)<.001f,"bus running duration unexpectedly changed");
            }
            catch(Exception e){failures++;Debug.LogException(e);}
            Debug.Log("TransitSpeedCheck: "+(failures==0?"passed":failures+" failed")+"; 4 vehicle kinds, complete dwell, 15-second phases, long frames and belt boundaries");
            EditorApplication.Exit(failures==0?0:1);
        }
    }
}
