using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    public partial class WorldBuilder
    {
        // KOC eAIP RKSS AD 2.12 / chart 2-1, 8 Jan 2026. Terminal-local +X represents
        // runway 14 direction; this interior annex is rotated relative to the georeferenced city.
        public const float GimpoNearRunwayLength=3200f,GimpoNearRunwayWidth=60f;
        public const float GimpoFarRunwayLength=3600f,GimpoFarRunwayWidth=45f;
        public const float GimpoRunwaySeparation=360f;
        // The playable terminal is an annex east of the 650 m city tile. Keep all new
        // airfield pavement east of that tile, rather than laying runways through its roads.
        const float AirfieldEastOffset=2400f,AirfieldTaxiZ=220f;
        const float AirfieldNearStart=AirfieldEastOffset-1794f,AirfieldFarStart=AirfieldEastOffset-1800f;
        const float AirfieldEntryX=AirfieldEastOffset-1660f,AirfieldLineupX=AirfieldEastOffset-1575f;
        const float AirfieldApronWest=-190f;

        public void ConfigureAirportFlight(PlaneFlight flight,int gate)
        {
            flight.gate=GatePlane(gate);flight.runwayDirection=Vector3.right;
            flight.runwayZ=AirportOrigin.z+RunwayZ;
            flight.taxiPath=AirportDepartureTaxi(gate);
        }

        public Vector3[] AirportDepartureTaxi(int gate)
        {
            float x=GateX(gate);
            var path=AirfieldRounded(new[]{new Vector3(x,0,105),new Vector3(x,0,AirfieldTaxiZ),new Vector3(AirfieldEntryX,0,AirfieldTaxiZ),new Vector3(AirfieldEntryX,0,RunwayZ),new Vector3(AirfieldLineupX,0,RunwayZ)},40);
            for(int i=0;i<path.Count;i++)path[i]+=AirportOrigin;
            return path.ToArray();
        }

        void BuildGimpoAirfield()
        {
            Block("김포공항 활주로 주변 잔디",airport,new Vector3(AirfieldApronWest,-.4f,30),new Vector3(AirfieldEastOffset+2400,-.2f,1200),Mat("district-ground",new Color(.34f,.43f,.35f),0,"grass",160f),false);
            Block("중앙 계류장",airport,new Vector3(AirfieldApronWest,-.12f,TerminalHalfZ),new Vector3(440,0,185),Mat("apron",new Color(.66f,.66f,.63f),0,"concrete",30f),false);
            // The runway dimensions are preserved; apron extents and taxiway intersection stations are
            // a game reconstruction, not a surveyed or operational ground-movement chart.
            BuildAirfieldRunway("14R","32L",AirfieldNearStart,RunwayZ,GimpoNearRunwayLength,GimpoNearRunwayWidth);
            BuildAirfieldRunway("14L","32R",AirfieldFarStart,RunwayZ+GimpoRunwaySeparation,GimpoFarRunwayLength,GimpoFarRunwayWidth);
            AirfieldTaxiway("P",new[]{new Vector3(-170,0,AirfieldTaxiZ),new Vector3(AirfieldNearStart+GimpoNearRunwayLength-86,0,AirfieldTaxiZ)},30);
            AirfieldTaxiway("계류장 R",new[]{new Vector3(-170,0,150),new Vector3(410,0,150)},30);
            // Aircraft push back from each fixed stand, then follow the same painted turn used by the flight.
            for(int gate=1;gate<=8;gate++)
            {
                float x=GateX(gate);
                var exit=AirfieldRounded(new[]{new Vector3(x,0,105),new Vector3(x,0,AirfieldTaxiZ),new Vector3(x+100,0,AirfieldTaxiZ)},40);
                AirfieldStrip("계류장 진입 유도로",exit,30,Mat("taxiway",new Color(.32f,.33f,.33f),0,"asphalt",20),.018f);
                AirfieldStrip("주기장 유도선",AirfieldRounded(new[]{new Vector3(x,0,65),new Vector3(x,0,AirfieldTaxiZ),new Vector3(x+100,0,AirfieldTaxiZ)},40),.22f,Mat("taxiway-yellow",new Color(.98f,.77f,.12f)),.048f);
            }
            var nearEntry=AirfieldRounded(new[]{new Vector3(AirfieldEntryX-100,0,AirfieldTaxiZ),new Vector3(AirfieldEntryX,0,AirfieldTaxiZ),new Vector3(AirfieldEntryX,0,RunwayZ),new Vector3(AirfieldLineupX,0,RunwayZ)},40);
            AirfieldTaxiway("G2",nearEntry,40);
            // Cross-links between the two parallel runways. Their game spacing is schematic.
            foreach(var link in new[]{new Vector2(-1100,0),new Vector2(-450,1),new Vector2(650,2),new Vector2(1280,3)})
            {
                float x=AirfieldEastOffset+link.x;string label=new[]{"E2 / E1","D2 / D1","C2 / C1","B2 / B1"}[(int)link.y];
                var linkPath=AirfieldRounded(new[]{new Vector3(x-55,0,AirfieldTaxiZ),new Vector3(x,0,AirfieldTaxiZ),new Vector3(x,0,RunwayZ+GimpoRunwaySeparation),new Vector3(x+60,0,RunwayZ+GimpoRunwaySeparation)},32);
                AirfieldTaxiway(label,linkPath,35);
                AirfieldHoldLine(x,RunwayZ-70,35);
            }
            // Rounded end taxiways reflect the AD chart's connected, curved aerodrome outline.
            float nearEnd=AirfieldNearStart+GimpoNearRunwayLength,farEnd=AirfieldFarStart+GimpoFarRunwayLength;
            AirfieldTaxiway("A",AirfieldRounded(new[]{new Vector3(nearEnd-296,0,AirfieldTaxiZ),new Vector3(nearEnd-85,0,AirfieldTaxiZ),new Vector3(nearEnd+40,0,RunwayZ),new Vector3(nearEnd+40,0,RunwayZ+135),new Vector3(farEnd-140,0,RunwayZ+135),new Vector3(farEnd-60,0,RunwayZ+GimpoRunwaySeparation)},70),35);
            AirfieldHoldLine(AirfieldEntryX,RunwayZ-64,40);
            AirfieldGroundLabel("14R / 32L",new Vector3(AirfieldEastOffset-900,.065f,RunwayZ-47),Vector3.right,5,Color.white);
            AirfieldGroundLabel("14L / 32R",new Vector3(AirfieldEastOffset-900,.065f,RunwayZ+GimpoRunwaySeparation-38),Vector3.right,5,Color.white);
            AirfieldTaxiSign("G2  →  14R",new Vector3(AirfieldEntryX-29,1.0f,RunwayZ-62),Vector3.right);
            AirfieldTaxiSign("P",new Vector3(AirfieldEntryX,1.0f,AirfieldTaxiZ-23),Vector3.back);
        }

        void BuildAirfieldRunway(string first,string opposite,float start,float z,float length,float width)
        {
            var asphalt=Mat("runway",new Color(.25f,.26f,.27f),0,"asphalt",20f);
            var white=Mat("runway-paint",new Color(.94f,.94f,.91f));
            Block("활주로 "+first+"/"+opposite,airport,new Vector3(start,0,z-width*.5f),new Vector3(start+length,.032f,z+width*.5f),asphalt,false);
            foreach(float side in new[]{-1f,1f})
                Block("활주로 측선",airport,new Vector3(start,.039f,z+side*(width*.5f-1.5f)-.45f),new Vector3(start+length,.048f,z+side*(width*.5f-1.5f)+.45f),white,false);
            for(float along=120;along<length-120;along+=60)
                Block("활주로 중앙선",airport,new Vector3(start+along,.039f,z-.45f),new Vector3(start+along+30,.05f,z+.45f),white,false);
            for(int end=0;end<2;end++)
            {
                float direction=end==0?1:-1,threshold=end==0?start:start+length;
                for(int stripe=-5;stripe<=5;stripe++)
                {
                    if(stripe==0)continue;float centre=z+stripe*width*.074f;
                    float a=threshold+direction*7,b=threshold+direction*37;
                    Block("활주로 시단 표지",airport,new Vector3(Mathf.Min(a,b),.04f,centre-1.05f),new Vector3(Mathf.Max(a,b),.052f,centre+1.05f),white,false);
                }
                AirfieldGroundLabel(end==0?first:opposite,new Vector3(threshold+direction*77,.06f,z),Vector3.right*direction,12,Color.white);
                foreach(float side in new[]{-1f,1f})
                {
                    float a=threshold+direction*300,b=threshold+direction*345;
                    Block("활주로 조준점",airport,new Vector3(Mathf.Min(a,b),.04f,z+side*width*.28f-2.2f),new Vector3(Mathf.Max(a,b),.052f,z+side*width*.28f+2.2f),white,false);
                }
            }
            var lamp=Glow("runway-edge-light",new Color(1f,.96f,.79f),1.5f);
            for(float along=0;along<=length;along+=60)foreach(float side in new[]{-1f,1f})
                Block("활주로 가장자리 등",airport,new Vector3(start+along-.23f,.05f,z+side*(width*.5f+.5f)-.23f),new Vector3(start+along+.23f,.22f,z+side*(width*.5f+.5f)+.23f),lamp,false);
        }

        void AirfieldTaxiway(string label,IList<Vector3> line,float width)
        {
            AirfieldStrip("유도로 "+label,line,width,Mat("taxiway",new Color(.32f,.33f,.33f),0,"asphalt",20),.018f);
            AirfieldStrip("유도로 중앙선 "+label,line,.22f,Mat("taxiway-yellow",new Color(.98f,.77f,.12f)),.057f);
        }
        void AirfieldStrip(string name,IList<Vector3> line,float width,Material material,float y)
        {
            if(line.Count<2)return;
            // Subdivide long straights so the rendered/tested surface avoids giant PhysX triangles.
            var dense=new List<Vector3>{line[0]};
            for(int i=1;i<line.Count;i++){
                int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(line[i-1],line[i])/100f));
                for(int j=1;j<=count;j++)dense.Add(Vector3.Lerp(line[i-1],line[i],j/(float)count));
            }
            line=dense;
            var vertices=new Vector3[line.Count*2];var uv=new Vector2[vertices.Length];var triangles=new int[(line.Count-1)*6];float along=0;
            for(int i=0;i<line.Count;i++)
            {
                Vector3 forward=i==0?line[1]-line[0]:i==line.Count-1?line[i]-line[i-1]:line[i+1]-line[i-1];
                var right=Vector3.Cross(Vector3.up,forward.normalized);var p=line[i];p.y=y;
                vertices[i*2]=p-right*width*.5f;vertices[i*2+1]=p+right*width*.5f;
                if(i>0)along+=Vector3.Distance(line[i-1],line[i]);uv[i*2]=new Vector2(0,along/20);uv[i*2+1]=new Vector2(width/20,along/20);
                if(i<line.Count-1){int k=i*6,v=i*2;triangles[k]=v;triangles[k+1]=v+2;triangles[k+2]=v+1;triangles[k+3]=v+1;triangles[k+4]=v+2;triangles[k+5]=v+3;}
            }
            var mesh=new Mesh{name=name};mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();
            var go=new GameObject(name);go.transform.SetParent(airport,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;go.AddComponent<AccessMeshOwner>().mesh=mesh;
        }
        static List<Vector3> AirfieldRounded(IList<Vector3> corners,float radius)
        {
            var result=new List<Vector3>{corners[0]};
            for(int i=1;i<corners.Count-1;i++)
            {
                var point=corners[i];var incoming=point-corners[i-1];var outgoing=corners[i+1]-point;
                float cut=Mathf.Min(radius,Mathf.Min(incoming.magnitude,outgoing.magnitude)*.45f);
                var a=point-incoming.normalized*cut;var b=point+outgoing.normalized*cut;
                result.Add(a);
                for(int k=1;k<=12;k++){float t=k/12f;result.Add((1-t)*(1-t)*a+2*(1-t)*t*point+t*t*b);}
            }
            result.Add(corners[corners.Count-1]);return result;
        }
        void AirfieldHoldLine(float x,float z,float width)
        {
            var yellow=Mat("taxiway-yellow",new Color(.98f,.77f,.12f));
            foreach(float offset in new[]{-.7f,.7f})Block("활주로 진입 정지선",airport,new Vector3(x-width*.45f,.061f,z+offset-.13f),new Vector3(x+width*.45f,.068f,z+offset+.13f),yellow,false);
        }
        void AirfieldGroundLabel(string text,Vector3 local,Vector3 heading,float height,Color color)
        {
            var label=Sign(text,airport,AirportOrigin+local,Vector3.up,height,color);
            label.transform.rotation=Quaternion.LookRotation(Vector3.down,heading);
        }
        void AirfieldTaxiSign(string text,Vector3 local,Vector3 facing)
        {
            Board(text,airport,AirportOrigin+local,facing,new Vector2(7,1.2f),new Color(.04f,.05f,.04f),new Color(1f,.8f,.1f),.65f);
        }
#if UNITY_EDITOR
        // Batch entry point: -executeMethod PeninsulaTime.WorldBuilder.RunAirfieldCheck
        public static void RunAirfieldCheck()
        {
            int failures=0;
            System.Action<bool,string> check=(ok,message)=>{if(!ok){failures++;Debug.LogError("AirfieldCheck: "+message);}};
            var world=new GameObject("airfield check").AddComponent<WorldBuilder>();world.worldCamera=new GameObject("camera").AddComponent<Camera>();world.BuildDistrict(3,9);
            // Transform.Find treats slashes in runway names as path separators.
            Transform near=null,far=null;
            foreach(var child in world.airport.GetComponentsInChildren<Transform>()){
                if(child.name=="활주로 14R/32L")near=child;
                if(child.name=="활주로 14L/32R")far=child;
            }
            check(near!=null&&far!=null,"two runways exist");
            if(near!=null&&far!=null){
                var n=near.GetComponent<Renderer>().bounds;var f=far.GetComponent<Renderer>().bounds;
                check(Mathf.Abs(n.size.x-3200)<.01f&&Mathf.Abs(n.size.z-60)<.01f,"14R/32L dimensions");
                check(Mathf.Abs(f.size.x-3600)<.01f&&Mathf.Abs(f.size.z-45)<.01f,"14L/32R dimensions");
                check(Mathf.Abs(f.center.z-n.center.z-360)<.01f,"parallel runway separation");
                var left14=Vector3.Cross(Vector3.right,Vector3.up);
                check(Vector3.Dot(f.center-n.center,left14)>0,"14L lies left of 14R when facing runway 14");
                var left32=Vector3.Cross(Vector3.left,Vector3.up);
                check(Vector3.Dot(n.center-f.center,left32)>0,"32L lies left of 32R when facing runway 32");
                check(Mathf.Abs(n.min.x-f.min.x-6)<.01f,"14R threshold starts six metres after 14L");
            }
            // Add test-only pavement colliders: require a hit on actual airfield pavement.
            // A city road, generic terrain or underlying grass must never make a gap pass.
            var pavement=new HashSet<Collider>();
            foreach(var filter in world.airport.GetComponentsInChildren<MeshFilter>()){
                string name=filter.name;
                if(!(name=="활주로 14L/32R"||name=="활주로 14R/32L"||name=="중앙 계류장"||name=="계류장 진입 유도로"||(name.StartsWith("유도로 ")&&!name.StartsWith("유도로 중앙선"))))continue;
                var collider=filter.GetComponent<Collider>();
                if(collider==null){var mesh=filter.gameObject.AddComponent<MeshCollider>();mesh.sharedMesh=filter.sharedMesh;collider=mesh;}
                pavement.Add(collider);
                check(filter.GetComponent<Renderer>().bounds.min.x>325f,"airfield pavement avoids the city tile: "+name);
            }
            Physics.SyncTransforms();
            for(int gate=1;gate<=8;gate++){
                var mover=new GameObject("flight check").AddComponent<PlaneFlight>();world.ConfigureAirportFlight(mover,gate);
                check(Vector3.Distance(mover.taxiPath[0],world.GatePlane(gate)+Vector3.forward*35)<.01f,"pushback joins taxi route at gate "+gate);
                bool onPavement=true;
                for(float t=0;t<=54;t+=.25f){
                    mover.clock=t;mover.Step(0);RaycastHit hit;
                    if(!Physics.Raycast(mover.transform.position+Vector3.up*.25f,Vector3.down,out hit,1)||!pavement.Contains(hit.collider)||Mathf.Abs(hit.point.y)>.1f){
                        onPavement=false;Debug.LogError("AirfieldCheck pavement gap/overlap gate "+gate+" at t="+t+" position="+mover.transform.position+" hit="+(hit.collider==null?"none":hit.collider.name)+" y="+hit.point.y);break;
                    }
                    // Also catch foreign buildings above the short wheel-to-ground probe.
                    foreach(var obstruction in Physics.RaycastAll(mover.transform.position+Vector3.up*16,Vector3.down,16f)){
                        if(obstruction.point.y>.1f&&!obstruction.collider.transform.IsChildOf(world.airport)){
                            onPavement=false;Debug.LogError("AirfieldCheck foreign obstruction gate "+gate+" at t="+t+": "+obstruction.collider.name);break;
                        }
                    }
                    if(!onPavement)break;
                    if(t>=36){
                        check(Vector3.Dot(mover.transform.forward,Vector3.right)>.97f,"aircraft lines up with runway");
                        check(hit.collider!=null&&hit.collider.name=="활주로 14R/32L","takeoff roll stays on the shorter near runway 14R");
                    }
                }
                check(onPavement,"gate "+gate+" taxi and takeoff stay on generated pavement");
                mover.clock=mover.Duration;mover.Step(0);check(mover.Done&&mover.transform.position.y>100,"flight completes while climbing");
                DestroyImmediate(mover.gameObject);
            }
            Debug.Log("AirfieldCheck: "+(failures==0?"passed":failures+" failed"));UnityEditor.EditorApplication.Exit(failures==0?0:1);
        }
#endif
    }
}
