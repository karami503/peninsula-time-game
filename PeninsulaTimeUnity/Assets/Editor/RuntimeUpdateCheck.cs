using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PeninsulaTime
{
    // Timing/transform regressions for the CPU update changes; no generated city required.
    public static class RuntimeUpdateCheck
    {
        static int failures;
        static void Expect(bool ok,string message){if(!ok){failures++;Debug.LogError("RuntimeUpdateCheck: "+message);}}
        static Transform Child(Transform parent,string name)
        {
            var child=new GameObject(name).transform;child.SetParent(parent,false);return child;
        }
        static NetLine Line()
        {
            var line=new NetLine{id="runtime-update-check",kind="metro",firstMinute=0,lastMinute=1440,offsets=new[]{0f,90f,180f}};
            for(int i=0;i<3;i++)line.stops.Add(new NetStation{id="runtime-"+i,name="Check "+i});
            return line;
        }
        static SubwayTrain Train(NetLine line,int direction,int parity)
        {
            var train=new GameObject("Update timing fixture").AddComponent<SubwayTrain>();
            train.line=new StationLine("Check","Check",Color.white,0){net=line,direction=direction,index=1,parity=parity};
            train.direction=direction==0?1:-1;
            for(int i=0;i<2;i++)
            {
                var c=Child(train.transform,"Consist "+i).gameObject.AddComponent<TrainConsist>();
                c.train=train;c.cabin=c.gameObject.AddComponent<Cabin>();
                c.doorLeaves.Add(Child(c.transform,"Door"));c.doorClosed.Add(Vector3.zero);c.doorOpen.Add(Vector3.forward);
                train.consists.Add(c);
            }
            train.leaves.Add(Child(train.transform,"Screen door"));train.leafClosed.Add(Vector3.zero);train.leafOpen.Add(Vector3.forward);
            return train;
        }
        // Fresh schedule queries are the reference. Cached interpolation must agree
        // through arrival/departure, parity, reverse direction and abrupt clock skips.
        static void CompareFresh(SubwayTrain train,double now)
        {
            train.Follow(now);
            float[] expected={SubwayTrain.Hidden,SubwayTrain.Hidden};
            float window=SubwayTrain.Dwell+SubwayTrain.Depart;
            foreach(var a in TransitSchedule.Next(train.line.net,train.line.direction,train.line.index,6,now-window))
            {
                if(train.line.parity>=0&&a.trip%2!=train.line.parity)continue;
                float since=(float)(window-a.seconds);int k=(train.line.parity<0?a.trip:a.trip/2)%2;
                if(since>=-SubwayTrain.Approach&&since<=window&&expected[k]==SubwayTrain.Hidden)expected[k]=SubwayTrain.Approach+since;
            }
            for(int k=0;k<2;k++)
            {
                var c=train.consists[k];
                Expect(Mathf.Abs(c.clock-expected[k])<.001f,"cached clock diverged at "+now+" direction="+train.line.direction+" parity="+train.line.parity+" consist="+k);
                if(expected[k]<SubwayTrain.Shown)
                {
                    Expect(Mathf.Abs(c.doorAmount-SubwayTrain.DoorAmount(expected[k]))<.001f,"door phase diverged at "+now);
                    Expect(Mathf.Abs(c.transform.localPosition.z-train.Offset(expected[k])*train.direction)<.001f,"train motion diverged at "+now);
                }
            }
        }
        static void CheckTimetable()
        {
            var line=Line();
            foreach(int direction in new[]{0,1})foreach(int parity in new[]{-1,0,1})
            {
                var train=Train(line,direction,parity);
                try
                {
                    double start=43211.25;
                    for(int frame=0;frame<3600;frame++)CompareFresh(train,start+frame/60d);
                    Expect(train.ScheduleQueryCount<=61,"schedule was recomputed per frame instead of reusing absolute arrivals: "+train.ScheduleQueryCount);
                    foreach(double jump in new[]{43199.5,43800.25,0.0,86399.75,86400.1,86400.4})CompareFresh(train,jump);
                    int before=train.ScheduleQueryCount;
                    var changed=train.line;changed.direction=1-direction;train.line=changed;
                    CompareFresh(train,86400.45);
                    Expect(train.ScheduleQueryCount==before+1,"route change must invalidate the cached timetable immediately");
                }
                finally{Object.DestroyImmediate(train.gameObject);}
            }
            Expect(TransitSchedule.Headway(line,43200)==30,"metro cadence changed");
            line.kind="bus";Expect(TransitSchedule.Headway(line,43200)==50,"bus cadence changed");
        }
        static void CheckStationaryTransforms()
        {
            var train=Train(Line(),0,-1);
            var doorObject=new GameObject("Vehicle doors fixture");var barrierObject=new GameObject("Gate fixture");
            try
            {
                var c=train.consists[0];c.Place(0,0,1);var leaf=c.doorLeaves[0];
                leaf.hasChanged=false;c.transform.hasChanged=false;
                for(int i=0;i<600;i++)c.Place(0,0,1);
                Expect(!leaf.hasChanged&&!c.transform.hasChanged,"stationary consist repeatedly dirtied physics/render transforms");
                c.Place(1,.5f,1);
                Expect(leaf.hasChanged&&c.transform.hasChanged&&Mathf.Abs(c.cabin.open-.5f)<.001f,"moving consist/doors must continue to update");
                train.Begin(SubwayTrain.Approach+6);
                var screen=train.leaves[0];screen.hasChanged=false;
                for(int i=0;i<600;i++)train.Step(0);
                Expect(!screen.hasChanged,"open stationary screen door repeatedly dirtied its transform");
                train.Step(6);
                Expect(screen.hasChanged,"screen doors must still close before departure");

                var doors=doorObject.AddComponent<VehicleDoors>();var busLeaf=Child(doorObject.transform,"Bus door");
                doors.cabin=doorObject.AddComponent<Cabin>();doors.Add(busLeaf,Vector3.forward);doors.Set(0);busLeaf.hasChanged=false;
                for(int i=0;i<600;i++)doors.Set(0);
                Expect(!busLeaf.hasChanged,"closed bus door repeatedly dirtied its transform");
                doors.Move(true,.4f,.8f);
                Expect(busLeaf.hasChanged&&Mathf.Abs(doors.cabin.open-.5f)<.001f,"bus door opening lost its animation");

                var barrier=barrierObject.AddComponent<Barrier>();var flap=Child(barrier.transform,"Flap");barrier.flaps=new[]{flap};
                barrier.Step(0);flap.hasChanged=false;for(int i=0;i<600;i++)barrier.Step(1f/60f);
                Expect(!flap.hasChanged,"closed gate repeatedly dirtied its flap transform");
                barrier.Open();barrier.Step(.1f);
                Expect(flap.hasChanged&&barrier.IsOpen,"gate no longer opens");
            }
            finally{Object.DestroyImmediate(train.gameObject);Object.DestroyImmediate(doorObject);Object.DestroyImmediate(barrierObject);}
        }
        public static void Run()
        {
            failures=0;
            try{CheckTimetable();CheckStationaryTransforms();}
            catch(Exception e){failures++;Debug.LogException(e);}
            Debug.Log("RuntimeUpdateCheck: "+(failures==0?"passed":failures+" failed")+"; fresh timetable compared at 21,600 frame positions plus skips and route changes");
            EditorApplication.Exit(failures==0?0:1);
        }
    }
}
