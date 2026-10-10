using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Starts in 진해역's hall and walks: out of the back door, over the crossing onto the island platform, into the
    // 진해선 shuttle through an open door, rides it to 경화역, walks out, down off the platform and back, rides back to
    // 진해 and walks off; then back through the hall, out of the front door, across the square to the street and down
    // 중원로 for some 300 m. The walker is only ever moved by walking steps (no jump between steps) or by the train they
    // stand in, the ground is carved around them as they go and dropped behind, every chunk, station part and coach is
    // a single mesh, the people are square-built, and the land has its surveyed hills, sea and buildings.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.JinhaeCheck.Run
    public static class JinhaeCheck
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        const float Dt=1f/60f;
        static int failures,steps;
        static void Check(bool ok,string message){if(!ok){failures++;Debug.LogError("JinhaeCheck: "+message);}}
        static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,Flags).Invoke(o,args);}
        static T Get<T>(object o,string name){return (T)o.GetType().GetField(name,Flags).GetValue(o);}
        static void Set(object o,string name,object value){o.GetType().GetField(name,Flags).SetValue(o,value);}

        public static void Run()
        {
            failures=0;steps=0;
            try{Scenario();}
            catch(Exception e){failures++;Debug.LogException(e);}
            Debug.Log("JinhaeCheck: "+(failures==0?"passed":failures+" failed"));
            EditorApplication.Exit(failures==0?0:1);
        }
        static float Feet(Transform eye){return eye.position.y-1.65f;}

        // Walks on the flat toward `target`, streaming the ground as it goes; fails on a block, a fall or a jump.
        static bool Walk(GameController game,WorldBuilder world,Transform eye,Vector3 target,string label)
        {
            int stalled=0;
            for(int step=0;step<12000;step++)
            {
                var feet=eye.position-Vector3.up*1.65f;var delta=target-feet;delta.y=0;
                if(delta.magnitude<.12f)return true;
                Call(game,"MoveWalkerStep",delta.normalized*Mathf.Min(.075f,delta.magnitude),Dt);
                world.Carved.Stream(eye.position);world.Carved.MovePeople(Dt);steps++;
                if(steps%600==0)world.Carved.DropFar(eye.position);
                var after=eye.position-Vector3.up*1.65f;var moved=after-feet;
                if(new Vector2(moved.x,moved.z).magnitude>.2f||Mathf.Abs(moved.y)>.6f){Check(false,label+": the walker jumped from "+feet+" to "+after);return false;}
                float under;byte what;
                if(world.Carved.Sample(after,out under,out what)&&after.y<under-1f){Check(false,label+": fell through the ground at "+after);return false;}
                if(Get<string>(game,"mode")!="carved"){Check(false,label+": left the carved 진해 ("+Get<string>(game,"mode")+")");return false;}
                if(moved.sqrMagnitude<1e-7f)stalled++;else stalled=0;
                if(stalled>8)
                {
                    RaycastHit hit;Physics.SphereCast(feet+Vector3.up*.85f,.3f,delta.normalized,out hit,.5f);
                    Check(false,label+" blocked at "+feet+" by "+(hit.collider?hit.collider.name+" "+hit.collider.bounds:"floor/step"));return false;
                }
            }
            Check(false,label+" timeout");return false;
        }

        static Cabin Aboard(GameController game){return Get<Cabin>(game,"cabin");}
        // Waits for the train's doors to open, walks along the platform to just outside the nearest door and steps in.
        static bool Board(GameController game,WorldBuilder world,Transform eye,JinhaeTrain line,string label)
        {
            for(int i=0;i<60*240&&!line.Open;i++)line.Step(Dt);
            if(!line.Open){Check(false,label+": the train never stood with its doors open");return false;}
            Transform car=null;float door=0,best=float.MaxValue;
            foreach(var c in line.cars)foreach(float z in c.GetComponent<Cabin>().doors)
            {float gap=(c.TransformPoint(new Vector3(0,0,z))-eye.position).sqrMagnitude;if(gap<best){best=gap;car=c;door=z;}}
            var cabin=car.GetComponent<Cabin>();int side=cabin.doorSide;
            Check(cabin.Open,label+": "+car.name+" doors not open");
            if(!Walk(game,world,eye,car.TransformPoint(new Vector3(side*2.4f,0,door)),label+": to the door"))return false;
            for(int k=0;k<120&&Aboard(game)==null;k++)
            {
                var step=car.TransformDirection(new Vector3(-side,0,0))*.075f;
                if(!(bool)Call(game,"TryEnterCabin",step))Call(game,"MoveWalkerStep",step,Dt);
            }
            Check(Aboard(game)==cabin,label+": did not walk in through the open door of "+car.name);
            return Aboard(game)==cabin;
        }
        // Stands in the aisle while the train runs to `stop`, then walks out of the door onto its platform.
        static bool Ride(GameController game,WorldBuilder world,Transform eye,JinhaeTrain line,string stop,string label)
        {
            var cabin=Aboard(game);
            for(int k=0;k<40;k++){var f=Get<Vector3>(game,"cabinFeet");Call(game,"MoveInCabin",cabin.transform.TransformDirection(new Vector3(-Mathf.Clamp(f.x,-.05f,.05f),0,0)));Call(game,"CarryInCabin");}
            float from=line.s,top=0;int stepsRun=0;var start=eye.position;
            for(int i=0;i<60*400;i++)
            {
                line.Step(Dt);Call(game,"CarryInCabin");world.Carved.Stream(eye.position);world.Carved.MovePeople(Dt);stepsRun++;
                if(stepsRun%300==0)world.Carved.DropFar(eye.position);
                top=Mathf.Max(top,line.speed);
                if(Aboard(game)!=cabin){Check(false,label+": fell out of the train at "+eye.position);return false;}
                var localFeet=Get<Vector3>(game,"cabinFeet");float floor=cabin.transform.TransformPoint(new Vector3(localFeet.x,cabin.floor,localFeet.z)).y;
                if(line.phase=="run"&&Mathf.Abs(Feet(eye)-floor)>.08f){Check(false,label+": rider left the tilted coach floor by "+(Feet(eye)-floor)+" m at "+eye.position);return false;}
                if(line.Open&&line.Here==stop)break;
            }
            Check(line.Open&&line.Here==stop,label+": the train did not reach "+stop+" with its doors open ("+line.phase+" at "+line.Here+", s "+line.s+" of "+line.stops[0]+"/"+line.stops[1]+", doors "+line.doors[0].amount+"/"+line.doors[1].amount+")");
            float rode=Vector3.Distance(start,eye.position);
            Debug.Log("JinhaeCheck: "+label+" "+Mathf.Abs(line.s-from).ToString("F0")+" m of line ("+rode.ToString("F0")+" m straight) in "+(stepsRun*Dt).ToString("F0")+" s, top speed "+top.ToString("F1")+" m/s");
            Check(top>10f&&rode>500f,label+": hardly moved (top "+top+" m/s, "+rode+" m)");
            // Out of the door on the platform side.
            for(int k=0;k<80&&Aboard(game)!=null;k++)
            {
                var f=Get<Vector3>(game,"cabinFeet");float door=cabin.doors[0];foreach(float z in cabin.doors)if(Mathf.Abs(z-f.z)<Mathf.Abs(door-f.z))door=z;
                var local=Mathf.Abs(f.z-door)>.05f?new Vector3(0,0,Mathf.Clamp(door-f.z,-.075f,.075f)):new Vector3(cabin.doorSide*.075f,0,0);
                Call(game,"MoveInCabin",cabin.transform.TransformDirection(local));if(Aboard(game)!=null)Call(game,"CarryInCabin");
            }
            Check(Aboard(game)==null,label+": could not walk out of the door at "+stop);
            Physics.SyncTransforms();Call(game,"MoveWalkerStep",Vector3.zero,Dt);
            return Aboard(game)==null;
        }

        static void Scenario()
        {
            var camera=new GameObject("jinhae check camera").AddComponent<Camera>();
            var world=new GameObject("jinhae check world").AddComponent<WorldBuilder>();world.worldCamera=camera;
            var host=new GameObject("jinhae check player");host.SetActive(false);
            var game=host.AddComponent<GameController>();game.state=new GameState();game.viewCamera=camera;game.world=world;
            var eye=new GameObject("jinhae check eye").transform;Set(game,"eye",eye);world.viewer=eye;
            var clock=System.Diagnostics.Stopwatch.StartNew();
            Call(game,"EnterJinhae",new object[]{null});clock.Stop();
            var district=world.Carved;var station=world.JinhaeStation;float d=world.JinhaeTrackOffset;
            Check(district!=null&&station!=null,"진해 was not built");if(district==null||station==null)return;
            Check(Get<string>(game,"mode")=="carved","not in the carved 진해 after entering");
            Physics.SyncTransforms();
            Call(game,"MoveWalkerStep",Vector3.zero,Dt);
            Check(Mathf.Abs(Feet(eye)-.25f)<.15f,"start is not on the station hall floor (feet "+Feet(eye)+")");
            Debug.Log("JinhaeCheck: track "+d+" m north of the station point, "+district.ChunkCount+" chunks at the start, built in "+clock.ElapsedMilliseconds+" ms ("+(clock.ElapsedMilliseconds/Mathf.Max(1,district.ChunkCount))+" ms a chunk)");

            // One lump each: the station's parts, each chunk (people walk on it as lumps of their own).
            var line=world.JinhaeLine;var gyeonghwa=world.GyeonghwaStation;
            Check(line!=null&&line.cars.Count==2&&gyeonghwa!=null,"no 진해선 shuttle or 경화역");if(line==null||gyeonghwa==null)return;
            foreach(var part in new[]{station.Find("진해역 역사"),station.Find("진해역 승강장"),gyeonghwa.Find("경화역 승강장")})
                Check(part!=null&&part.GetComponents<MeshFilter>().Length==1&&part.childCount==0,(part!=null?part.name:"a station part")+" is not one carved mesh");
            foreach(var car in line.cars)Check(car.GetComponents<MeshFilter>().Length==1,car.name+" is not one carved mesh");
            Debug.Log("JinhaeCheck: 진해선 line "+line.Length.ToString("F0")+" m, 진해 at "+line.stops[0].ToString("F0")+", 경화 at "+line.stops[1].ToString("F0")+" ("+Mathf.Abs(line.stops[1]-line.stops[0]).ToString("F0")+" m apart), 경화역 "+(gyeonghwa.position.y+district.datum).ToString("F1")+" m above the sea");

            // The land: the surveyed hills, the sea, the mapped buildings, the far land to the horizon.
            Check(district.BuildingCount>10000,"only "+district.BuildingCount+" mapped buildings");
            var sea=district.Local(128.6601,35.125);var peak=district.Local(128.6755,35.1750);
            Check(district.WaterAt(new Vector2(sea.x,sea.z))&&district.Elevation(new Vector2(sea.x,sea.z))<=-district.datum+.01f,"no sea south of the station");
            Check(district.Raw(new Vector2(peak.x,peak.z))+district.datum>300f,"no hills north of the station ("+(district.Raw(new Vector2(peak.x,peak.z))+district.datum)+" m)");
            Check(district.transform.Find("진해 땅 (먼 곳)")!=null,"no far land");
            Check(district.FineCount>0&&district.FineCount<district.ChunkCount,"no near/far chunk detail ("+district.FineCount+" fine of "+district.ChunkCount+")");
            Debug.Log("JinhaeCheck: "+district.BuildingCount+" mapped buildings; station "+district.datum.ToString("F1")+" m above the sea; rail bridges "+district.BridgeMetres.ToString("F0")+" m, covered cuttings "+district.TunnelMetres.ToString("F0")+" m");
            Check(district.BridgeMetres>0&&district.TunnelMetres>0,"mapped OSM bridge/tunnel structures were not loaded");
            Check(district.MappedCrossingCount>0,"mapped OSM railway crossings were not loaded");
            Check(world.Indoors(eye.position),"the hall is not lit as indoors");
            Call(world,"ApplyDaylight",eye.position);
            var sun=Get<Light>(world,"sun");
            Check(sun!=null&&sun.enabled,"the outdoor sun was disabled while the player stood in the hall");
            Check(camera.clearFlags==CameraClearFlags.Skybox,"the hall hid the daylight visible through its entrances");
            foreach(var chunk in district.Chunks)
            {
                int pieces=0;foreach(var filter in chunk.GetComponentsInChildren<MeshFilter>())if(filter.gameObject==chunk||filter.name!="진해 사람")pieces++;
                Check(pieces==1,chunk.name+" is made of "+pieces+" meshes");
            }

            Vector3 At(float x,float z){var p=station.TransformPoint(new Vector3(x,0,z));p.y=0;return p;}
            // To the platform and back.
            bool platform=Walk(game,world,eye,At(0,8f),"out of the back door")
                &&Walk(game,world,eye,At(0,d-9.2f),"onto the crossing")
                &&Walk(game,world,eye,At(-1.2f,d-6.5f),"onto the platform")
                &&Walk(game,world,eye,At(-1.2f,d-2.6f),"along the platform to the main track side");
            if(platform)Check(Mathf.Abs(Feet(eye)-WorldBuilder.JinhaePlatformY)<.15f,"not standing on the platform (feet "+Feet(eye)+")");
            // By train to 경화역 and back.
            Vector3 G(float x,float z){var p=gyeonghwa.TransformPoint(new Vector3(x,0,z));return p;}
            bool rode=platform&&Board(game,world,eye,line,"boarding at 진해")&&Ride(game,world,eye,line,"경화","to 경화");
            if(rode)Check(Mathf.Abs(Feet(eye)-(gyeonghwa.position.y+WorldBuilder.JinhaePlatformY))<.15f,"not standing on 경화역's platform (feet "+Feet(eye)+", platform "+(gyeonghwa.position.y+WorldBuilder.JinhaePlatformY)+")");
            rode=rode&&Walk(game,world,eye,G(3f,1f),"along 경화역's platform")&&Walk(game,world,eye,G(8.5f,1f),"down the steps off 경화역's platform");
            if(rode){float under;byte what;district.Sample(eye.position,out under,out what);Check(Mathf.Abs(Feet(eye)-under)<.1f,"not on the ground beside 경화역 (feet "+Feet(eye)+", ground "+under+")");}
            rode=rode&&Walk(game,world,eye,G(3f,1f),"back up onto 경화역's platform")
                &&Board(game,world,eye,line,"boarding at 경화")&&Ride(game,world,eye,line,"진해","back to 진해");
            if(rode)Check(Mathf.Abs(Feet(eye)-WorldBuilder.JinhaePlatformY)<.15f,"not back on 진해역's platform (feet "+Feet(eye)+")");
            bool back=rode&&Walk(game,world,eye,At(-1.2f,d-2.6f),"along the platform from the train")
                &&Walk(game,world,eye,At(-1.2f,d-6.5f),"back across the platform")
                &&Walk(game,world,eye,At(0,d-9.2f),"back onto the crossing")
                &&Walk(game,world,eye,At(0,8f),"back to the back door")
                &&Walk(game,world,eye,At(0,1.5f),"back into the hall");
            // Out into the town.
            bool street=back&&Walk(game,world,eye,At(0,-8f),"out of the front door")
                &&Walk(game,world,eye,At(2f,-60f),"across the station square");
            if(!street)return;
            Vector3 road=Vector3.zero;bool found=false;float h;byte kind;
            for(float z=-60f;z>-160f&&!found;z-=.5f)for(float x=2f;x<=10f&&!found;x+=.5f)
                if(district.Sample(At(x,z),out h,out kind)&&kind==CarvedDistrict.Road&&district.Sample(At(x,z-1f),out h,out kind)&&kind==CarvedDistrict.Road){road=At(x,z-1f);found=true;} // well inside the carriageway
            Check(found,"no street south of the station square");if(!found)return;
            bool town=Walk(game,world,eye,road,"to the street in front of the station");
            {
                float under;byte what;district.Sample(eye.position,out under,out what);RaycastHit ground;Physics.Raycast(eye.position,Vector3.down,out ground,5f);
                Check(town&&Mathf.Abs(Feet(eye)-under)<.1f,"not standing on the street (feet "+Feet(eye)+", target "+road+", eye "+eye.position+", cell "+what+" at "+under+", ground "+(ground.collider?ground.collider.name+" "+ground.point.y:"none")+")");
            }
            Check(!world.Indoors(eye.position),"the street below the station's level is lit as indoors (eye "+eye.position+")");
            int most=0;
            if(town)
            {
                var from=eye.position;
                Walk(game,world,eye,At(14f,-380f),"down 중원로");
                district.DropFar(eye.position);most=district.ChunkCount;
                Debug.Log("JinhaeCheck: walked "+Vector3.Distance(from,eye.position).ToString("F0")+" m down 중원로; "+most+" chunks alive");
            }
            int reach=Mathf.CeilToInt((CarvedDistrict.Radius+96f)/CarvedDistrict.Chunk)*2+2;
            Check(most>0&&most<=reach*reach,"chunks alive "+most+" (bound "+reach*reach+")");
            float distance=Vector3.Distance(eye.position,station.position);
            Check(distance>250f,"did not get far from the station on foot ("+distance+" m)");

            // People: some about, all square-built (a block figure 1.8 m tall and 0.7 m across).
            Check(district.PeopleCount>0,"nobody about");
            foreach(var mesh in district.people)
            {
                var size=mesh.bounds.size;
                Check(Mathf.Abs(size.y-1.8f)<.05f&&size.x<=.71f&&mesh.vertexCount<2000,mesh.name+" is not a square-built person ("+size+", "+mesh.vertexCount+" vertices)");
            }
            Debug.Log("JinhaeCheck: "+steps+" walking steps, "+district.PeopleCount+" people about");

            // Find a short mapped road segment with a real height change and walk it without jumping. This catches
            // hillside regressions that flat station/platform routes cannot expose.
            Vector3 slopeA=Vector3.zero,slopeB=Vector3.zero;bool slopeFound=false;
            var centre=eye.position;
            for(float z=centre.z-120;z<=centre.z+120&&!slopeFound;z+=4f)for(float x=centre.x-120;x<=centre.x+120&&!slopeFound;x+=4f)
            {
                foreach(var delta in new[]{new Vector3(6,0,0),new Vector3(0,0,6)})
                {
                    float a,b;byte ka,kb;var p=new Vector3(x,0,z);var q=p+delta;
                    if(!district.Sample(p,out a,out ka)||!district.Sample(q,out b,out kb)||ka!=CarvedDistrict.Road||kb!=CarvedDistrict.Road)continue;
                    float rise=Mathf.Abs(b-a);if(rise<.35f||rise>2.2f)continue;
                    slopeA=p+Vector3.up*a;slopeB=q+Vector3.up*b;slopeFound=true;break;
                }
            }
            Check(slopeFound,"no mapped sloped road was available for the walking test");
            if(slopeFound)
            {
                eye.position=slopeA+Vector3.up*1.65f;Set(game,"grounded",true);Set(game,"verticalSpeed",0f);Physics.SyncTransforms();
                bool climbed=Walk(game,world,eye,slopeB,"up a mapped Jinhae slope");
                Check(climbed&&Mathf.Abs(Feet(eye)-slopeB.y)<.2f,"could not walk the mapped slope from "+slopeA+" to "+slopeB+" (feet "+Feet(eye)+")");
                Debug.Log("JinhaeCheck: walked mapped slope "+slopeA+" -> "+slopeB+" without jumping");
            }

            // A rail or metro station inside 진해구 opened from the map (here the planned 창원 2호선 경화) is the carved 경화역.
            if(TransitNetwork.Stations.Count==0)TransitNetwork.Build(null);
            var planned=TransitNetwork.Named("경화").Find(s=>s.lines.Exists(l=>l.kind!="bus"&&l.kind!="brt"));
            Check(planned!=null,"no 경화 station in the network");
            if(planned!=null)
            {
                Call(game,"VisitNetworkStation",planned,null,0);Call(game,"MoveWalkerStep",Vector3.zero,Dt);
                var stop=world.GyeonghwaStation;
                Check(Get<string>(game,"mode")=="carved"&&stop!=null&&Mathf.Abs(Feet(eye)-(stop.position.y+WorldBuilder.JinhaePlatformY))<.15f,"visiting 경화 from the map did not open the carved 경화역 (mode "+Get<string>(game,"mode")+", feet "+Feet(eye)+")");
            }
        }
    }
}
