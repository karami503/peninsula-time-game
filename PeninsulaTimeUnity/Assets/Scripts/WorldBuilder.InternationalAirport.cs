using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeninsulaTime {
    // Separate records prevent the domestic airport's route contract from being changed.
    public sealed class InternationalWalkRoute : MonoBehaviour { public Vector3[] points; }

    public partial class WorldBuilder {
        // Public KAC plans supply floor uses and shape; OSM way 226573240 supplies ~613 x 131 m scale.
        // This annex location, stair runs, storey heights and interior dimensions are game assumptions.
        public static readonly Vector3 InternationalAirportOrigin=new Vector3(900,0,-800);
        public static readonly Vector3 InternationalAirportConnector=new Vector3(440,-5.8f,-496);
        public Vector3 InternationalArrivalSpawn=>InternationalAirportOrigin+new Vector3(0,1.65f,-42);
        public Vector3 InternationalCheckInSpawn=>InternationalAirportOrigin+new Vector3(0,7.65f,-20);
        public Vector3 InternationalDepartureSpawn=>InternationalAirportOrigin+new Vector3(0,13.65f,-28);
        public Transform InternationalAirportRoot=>internationalAirport;
        Transform internationalAirport;
        static readonly Vector3[] InternationalCorridor={new Vector3(440,-5.8f,-496),new Vector3(440,-5.8f,-700),new Vector3(760,-5.8f,-700),new Vector3(760,-5.8f,-730)};
        readonly List<Vector3> internationalCorridorPath=new List<Vector3>();
        static bool InRect(Vector3 p,float x0,float x1,float z0,float z1,float y0,float y1)=>p.x>=x0&&p.x<=x1&&p.z>=z0&&p.z<=z1&&p.y>=y0&&p.y<=y1;
        // Lighting checks eye positions. Never use x>350: apron, taxiway and sky are outdoors.
        public static bool IsInternationalAirportInterior(Vector3 eye) {
            var p=eye-InternationalAirportOrigin;
            if(InRect(p,-160,160,-65,65,0,24))return true;
            if(InRect(p,-294,320,32,56,0,17.8f))return true;
            if(InRect(p,-143.7f,-136.3f,56,73,-5.8f,5))return true;
            if(eye.y>=-5.9f&&eye.y<=-2.15f)for(int i=1;i<InternationalCorridor.Length;i++)
                if(InternationalSegmentDistance(eye,InternationalCorridor[i-1],InternationalCorridor[i])<4.5f)return true;
            return false;
        }
        public static bool IsDomesticAirportInterior(Vector3 eye) {
            var p=eye-AirportOrigin;
            return InRect(p,-60,60,-30,30,0,24)||InRect(p,-150,150,0,30,11.6f,24);
        }
        static float InternationalSegmentDistance(Vector3 p,Vector3 a,Vector3 b){p.y=a.y;b.y=a.y;var d=b-a;return Vector3.Distance(p,a+d*Mathf.Clamp01(Vector3.Dot(p-a,d)/d.sqrMagnitude));}

        public void BuildInternationalAirport() {
            if(root==null||internationalAirport!=null)return;
            internationalAirport=new GameObject("김포공항 국제선 청사").transform;
            internationalAirport.SetParent(root.transform,false);internationalAirport.localPosition=InternationalAirportOrigin;
            var stone=Mat("intl-floor",new Color(.80f,.82f,.82f),.05f,"granite",4);
            var white=Mat("intl-wall",new Color(.91f,.91f,.88f));
            var glass=Mat("intl-glass",new Color(.45f,.68f,.76f),.3f);
            var metal=Mat("intl-metal",new Color(.30f,.35f,.39f),.5f);
            var wood=Mat("intl-wood",new Color(.43f,.29f,.17f));
            var blue=Mat("intl-blue",new Color(.05f,.24f,.36f));
            var light=Glow("intl-lights",new Color(1,.97f,.85f),.8f);
            var yellow=Mat("intl-tactile",new Color(.92f,.76f,.14f));
            for(int f=0;f<4;f++) {
                float y=f*6;var batch=new InternationalMeshBatch(internationalAirport,"국제선 "+(f+1)+"F");
                var holes=new List<Rect>();
                if(f>0)foreach(float baseX in new[]{-105f,105f}){float x=baseX+((f-1)%2)*6;holes.Add(new Rect(x-2.8f,-48,5.6f,24));}
                if(f==0)holes.Add(new Rect(-143.7f,56,7.4f,9));
                // 4F has the forehall notch visible in the official evacuation drawing.
                if(f==3)holes.Add(new Rect(-36,-65,72,39));
                IntlFloor(batch,new Rect(-160,-65,320,130),holes,y,stone);
                if(f<3) {
                    IntlFloor(batch,new Rect(-294,32,134,24),null,y,stone);
                    IntlFloor(batch,new Rect(160,32,160,24),null,y,stone);
                    IntlWingEnvelope(batch,-294,-160,y,f==2,white,glass);
                    IntlWingEnvelope(batch,160,320,y,f==2,white,glass);
                }
                // Side gables open to the long piers only where there is an actual floor.
                foreach(float x in new[]{-160f,160f}) {
                    batch.Box(new Vector3(x-.16f,y,-65),new Vector3(x+.16f,y+5.7f,f<3?32:65),white);
                    if(f<3)batch.Box(new Vector3(x-.16f,y,56),new Vector3(x+.16f,y+5.7f,65),white);
                }
                if(f==0)IntlWallWithDoors(batch,-160,160,65,y,new[]{-140f},7.4f,glass);
                else batch.Box(new Vector3(-160,y,65),new Vector3(160,y+5.7f,65.25f),glass);
                if(f<2)IntlWallWithDoors(batch,-160,160,-65,y,f==0?new[]{-24f,0f,24f}:new[]{-36f,-12f,12f,36f},7,glass);
                else if(f==2)batch.Box(new Vector3(-160,y,-65.25f),new Vector3(160,y+5.7f,-65),glass);
                else {
                    batch.Box(new Vector3(-160,y,-65.25f),new Vector3(-36,y+5.7f,-65),glass);
                    batch.Box(new Vector3(36,y,-65.25f),new Vector3(160,y+5.7f,-65),glass);
                    // Guard the notch instead of letting a player walk off the floor.
                    batch.Box(new Vector3(-36.2f,y,-65),new Vector3(-36,y+1.2f,-26),glass);
                    batch.Box(new Vector3(36,y,-65),new Vector3(36.2f,y+1.2f,-26),glass);
                    batch.Box(new Vector3(-36,y,-26.2f),new Vector3(36,y+1.2f,-26),glass);
                }
                // Columns are outside the central pedestrian and stair approaches.
                for(float x=-140;x<=140;x+=40)foreach(float z in new[]{-60f,22f,61f})
                    if(!(f==0&&x==-140&&z==61)&&!(f==3&&Mathf.Abs(x)<36&&z<-26))
                        batch.Box(new Vector3(x-.35f,y,z-.35f),new Vector3(x+.35f,y+5.7f,z+.35f),white);
                for(float z=-56;z<=56;z+=28) {
                    if(f==3&&z<-26){batch.Box(new Vector3(-150,y+5.5f,z-.18f),new Vector3(-40,y+5.56f,z+.18f),light,false);batch.Box(new Vector3(40,y+5.5f,z-.18f),new Vector3(150,y+5.56f,z+.18f),light,false);}
                    else batch.Box(new Vector3(-150,y+5.5f,z-.18f),new Vector3(150,y+5.56f,z+.18f),light,false);
                }
                // Flight-side rails surround the slab openings; landings at each end stay clear.
                if(f>0)foreach(float baseX in new[]{-105f,105f})foreach(float s in new[]{-1f,1f}) {
                    float x=baseX+((f-1)%2)*6;batch.Box(new Vector3(x+s*2.75f-.08f,y,-48),new Vector3(x+s*2.75f+.08f,y+1.2f,-24),glass);
                }
                // Return flights use an adjacent lane, avoiding intersecting ramps/headroom above the top landing.
                if(f<3)foreach(float baseX in new[]{-105f,105f}) {
                    float x=baseX+(f%2)*6;
                    var a=new Vector3(x,y,f%2==0?-48:-24);var b=new Vector3(x,y+6,f%2==0?-24:-48);
                    IntlStairs(batch,a,b,4.8f,stone,metal,glass);
                    IntlRoute((f+1)+"F ↔ "+(f+2)+"F "+(x<0?"서측":"동측"),new[]{a+(a-b).normalized*1.6f,b+(b-a).normalized*1.6f},true,a,b);
                    IntlSign((f+2)+"F ↑  "+(f==0?"체크인":f==1?"출국장":"식당·휴게공간"),a+new Vector3(0,3.4f,f%2==0?-1:1),f%2==0?Vector3.back:Vector3.forward,8,.8f);
                }
                if(f==0)IntlArrivals(batch,white,metal,blue,wood);
                if(f==1)IntlCheckIn(batch,white,metal,blue,wood);
                if(f==2)IntlDepartures(batch,white,metal,blue,wood,glass);
                if(f==3)IntlRestaurants(batch,white,metal,wood);
                // Floors above are ceilings; only the top storey needs its own roof.
                if(f==3)IntlFloor(batch,new Rect(-160,-65,320,130),null,24.1f,white);
                batch.Flush();
            }
            BuildInternationalCorridor(stone,white,metal,glass,yellow,light);
            BuildInternationalForecourt(stone,white,glass,metal);
            IntlSign("김포공항 국제선  ·  International Terminal",new Vector3(0,4.5f,-64.8f),Vector3.forward,24,1.2f);
            IntlSign("1F 도착  ·  2F 체크인  ·  3F 출국·탑승  ·  4F 식당",new Vector3(-125,3.6f,43),Vector3.forward,16,1);
            IntlRoute("1F 지하철 → 체크인 계단",new[]{new Vector3(-140,0,56),new Vector3(-140,0,50),new Vector3(-130,0,50),new Vector3(-130,0,-56),new Vector3(-105,0,-56),new Vector3(-105,0,-48)});
            IntlRoute("2F 계단 → 체크인",new[]{new Vector3(-105,6,-24),new Vector3(-105,6,-18),new Vector3(0,6,-18),new Vector3(0,6,-8)});
            IntlRoute("3F 계단 → 출국 → 39번",new[]{new Vector3(-99,12,-48),new Vector3(-99,12,-56),new Vector3(0,12,-56),new Vector3(0,12,-22),new Vector3(0,12,12),new Vector3(0,12,44),new Vector3(307,12,44)});
            IntlRoute("2F 서측 계단 환승",new[]{new Vector3(-105,6,-24),new Vector3(-105,6,-18),new Vector3(-99,6,-18),new Vector3(-99,6,-24)});
            IntlRoute("3F 서측 계단 환승",new[]{new Vector3(-99,12,-48),new Vector3(-99,12,-56),new Vector3(-105,12,-56),new Vector3(-105,12,-48)});
            IntlRoute("4F 서측 계단 → 식당",new[]{new Vector3(-105,18,-24),new Vector3(-105,18,-18),new Vector3(-60,18,-18),new Vector3(-60,18,6),new Vector3(0,18,6),new Vector3(6,18,6),new Vector3(6,18,23)});
            IntlRoute("1F 입국 수하물 → 도착홀",new[]{new Vector3(95,0,44),new Vector3(0,0,44),new Vector3(0,0,-42),new Vector3(0,0,-73)});
            Marker(InternationalArrivalSpawn-Vector3.up*1.65f,"국제선 도착");
            Marker(InternationalCheckInSpawn-Vector3.up*1.65f,"국제선 체크인");
            Marker(InternationalDepartureSpawn-Vector3.up*1.65f,"국제선 출국");
            Physics.SyncTransforms();
        }
        void IntlRoute(string name,Vector3[] points,bool stairs=false,Vector3 a=default(Vector3),Vector3 b=default(Vector3)) {
            if(stairs){var d=b-a;d.y=0;d.Normalize();points=new[]{a-d*1.5f,a,b,b+d*1.5f};}
            var route=new GameObject("국제선 "+name).AddComponent<InternationalWalkRoute>();route.transform.SetParent(internationalAirport,false);
            route.points=new Vector3[points.Length];for(int i=0;i<points.Length;i++)route.points[i]=InternationalAirportOrigin+points[i];
        }
        void IntlSign(string text,Vector3 p,Vector3 facing,float width=10,float height=.85f) {
            Board(text,internationalAirport,InternationalAirportOrigin+p,facing,new Vector2(width,height),new Color(.04f,.17f,.24f),Color.white,.40f);
        }
        static void IntlFloor(InternationalMeshBatch b,Rect area,List<Rect> holes,float y,Material mat) {
            var xs=new List<float>{area.xMin,area.xMax};var zs=new List<float>{area.yMin,area.yMax};
            for(float x=area.xMin+72;x<area.xMax;x+=72)xs.Add(x);
            for(float z=area.yMin+60;z<area.yMax;z+=60)zs.Add(z);
            if(holes!=null)foreach(var h in holes){xs.Add(Mathf.Clamp(h.xMin,area.xMin,area.xMax));xs.Add(Mathf.Clamp(h.xMax,area.xMin,area.xMax));zs.Add(Mathf.Clamp(h.yMin,area.yMin,area.yMax));zs.Add(Mathf.Clamp(h.yMax,area.yMin,area.yMax));}
            xs.Sort();zs.Sort();
            for(int i=1;i<xs.Count;i++)for(int j=1;j<zs.Count;j++){
                if(xs[i]-xs[i-1]<.01f||zs[j]-zs[j-1]<.01f)continue;var p=new Vector2((xs[i]+xs[i-1])*.5f,(zs[j]+zs[j-1])*.5f);bool cut=false;
                if(holes!=null)foreach(var h in holes)if(h.Contains(p)){cut=true;break;}
                if(!cut)b.Box(new Vector3(xs[i-1],y-.24f,zs[j-1]),new Vector3(xs[i],y,zs[j]),mat);
            }
        }
        static void IntlWallWithDoors(InternationalMeshBatch b,float min,float max,float z,float y,float[] doors,float width,Material mat) {
            float start=min;foreach(float door in doors){
                b.Box(new Vector3(start,y,z-.14f),new Vector3(door-width*.5f,y+5.7f,z+.14f),mat);
                b.Box(new Vector3(door-width*.5f,y+3.6f,z-.14f),new Vector3(door+width*.5f,y+5.7f,z+.14f),mat);start=door+width*.5f;
            }b.Box(new Vector3(start,y,z-.14f),new Vector3(max,y+5.7f,z+.14f),mat);
        }
        static void IntlWingEnvelope(InternationalMeshBatch b,float x0,float x1,float y,bool top,Material white,Material glass) {
            foreach(float z in new[]{32f,56f})b.Box(new Vector3(x0,y,z-.15f),new Vector3(x1,y+5.7f,z+.15f),glass);
            float end=x0<0?x0:x1;b.Box(new Vector3(end-.15f,y,32),new Vector3(end+.15f,y+5.7f,56),white);
            if(top)IntlFloor(b,new Rect(x0,32,x1-x0,24),null,y+5.9f,white);
        }
        static void IntlStairs(InternationalMeshBatch b,Vector3 a,Vector3 c,float width,Material stone,Material metal,Material glass) {
            var forward=c-a;forward.y=0;forward.Normalize();var right=Vector3.Cross(Vector3.up,forward);var offset=right*width*.5f;
            b.Quad(a-offset,c-offset,c+offset,a+offset,stone,true); // continuous ascending collision surface
            int steps=Mathf.CeilToInt((c.y-a.y)/.17f);
            for(int n=0;n<steps;n++){
                var first=Vector3.Lerp(a,c,n/(float)steps);var last=Vector3.Lerp(a,c,(n+1f)/steps);var center=(first+last)*.5f;
                b.RotatedBox(center+Vector3.up*((last.y-center.y)*.5f),new Vector3(width,.10f,Vector3.Distance(new Vector3(first.x,0,first.z),new Vector3(last.x,0,last.z))+.012f),Quaternion.LookRotation(forward),stone,false);
            }
            foreach(float side in new[]{-1f,1f}){
                var s=right*side*(width*.5f+.12f);var d=c-a;
                b.Quad(a+s,c+s,c+s+Vector3.up*1.15f,a+s+Vector3.up*1.15f,glass,true,true);
                b.RotatedBox((a+c)*.5f+s+Vector3.up*1.17f,new Vector3(.13f,.12f,d.magnitude+.2f),Quaternion.LookRotation(d),metal,true);
            }
        }
        static void IntlSeat(InternationalMeshBatch b,float x,float y,float z,float width,Material mat,Material metal){
            b.Box(new Vector3(x-width*.5f,y+.42f,z-.5f),new Vector3(x+width*.5f,y+.6f,z+.5f),mat);
            b.Box(new Vector3(x-width*.5f,y+.6f,z+.36f),new Vector3(x+width*.5f,y+1.3f,z+.54f),mat);
            foreach(float s in new[]{-1f,1f})b.Box(new Vector3(x+s*(width*.5f-.35f)-.1f,y,z-.35f),new Vector3(x+s*(width*.5f-.35f)+.1f,y+.42f,z+.35f),metal);
        }
        void IntlArrivals(InternationalMeshBatch b,Material white,Material metal,Material blue,Material wood){
            IntlWallWithDoors(b,-120,160,16,0,new[]{0f},12,white);
            b.Box(new Vector3(-120.15f,0,16),new Vector3(-119.85f,5.7f,65),white);
            // Arrival screening/circulation remains clear along x=0 and z=44.
            foreach(float x in new[]{-88f,-42f,42f,88f}){
                b.Box(new Vector3(x-13,.35f,24),new Vector3(x+13,.9f,31),metal);
                b.Box(new Vector3(x-10,.9f,25.5f),new Vector3(x+10,.98f,29.5f),blue);
                IntlSign("수하물 찾는 곳",new Vector3(x,3.1f,24),Vector3.back,10,.8f);
            }
            foreach(float x in new[]{-50f,50f}){
                for(int n=-1;n<=1;n++)IntlSeat(b,x,0,-34+n*5,14,wood,metal);
                b.Box(new Vector3(x-12,0,-8),new Vector3(x+12,1.05f,-5),blue);
            }
            IntlSign("입국 · 수하물 수취",new Vector3(0,3.7f,18),Vector3.forward,15,1);
            IntlSign("도착홀  ↓  지하철 · 국내선",new Vector3(0,3.7f,14),Vector3.back,15,1);
            for(int i=0;i<3;i++)IntlSign((i+1)+"번 출입문",new Vector3(-24+i*24,3.1f,-65),Vector3.back,6,.7f);
        }
        void IntlCheckIn(InternationalMeshBatch b,Material white,Material metal,Material blue,Material wood){
            for(int group=0;group<4;group++){
                float x=-69+group*46;
                for(int desk=0;desk<6;desk++){
                    float dx=x-15+desk*6;b.Box(new Vector3(dx-2,6,1),new Vector3(dx+2,7.15f,4),white);
                    b.Box(new Vector3(dx-1.8f,7.15f,1.1f),new Vector3(dx+1.8f,7.25f,3.8f),blue);
                    b.Box(new Vector3(dx-.8f,7.25f,3),new Vector3(dx+.8f,7.85f,3.12f),metal,false);
                }
                IntlSign("체크인 "+(char)('A'+group),new Vector3(x,9.8f,.8f),Vector3.back,18,1.1f);
            }
            foreach(float x in new[]{-76f,-60f,60f,76f}){
                b.Box(new Vector3(x-1,6,-50),new Vector3(x+1,7.5f,-48.5f),metal);
                b.Box(new Vector3(x-.7f,7.2f,-50.05f),new Vector3(x+.7f,7.55f,-49.98f),blue,false);
            }
            IntlSign("셀프 체크인",new Vector3(-68,9.1f,-50),Vector3.back,15,.9f);
            IntlSign("3F 출국장  ←     →  3F 출국장",new Vector3(0,9.7f,-31),Vector3.back,24,1);
            foreach(float x in new[]{-140f,140f})for(int i=0;i<3;i++)IntlSeat(b,x,6,-22+i*6,11,wood,metal);
            for(int i=0;i<4;i++)IntlSign((i+1)+"번 출입문",new Vector3(-36+i*24,9.1f,-65),Vector3.back,6,.7f);
        }
        void IntlDepartures(InternationalMeshBatch b,Material white,Material metal,Material blue,Material wood,Material glass){
            IntlWallWithDoors(b,-160,160,-12,12,new[]{0f},16,white);
            foreach(float x in new[]{-6f,6f}){
                // Metal detector frames: 3.0 m clear height and 4.5 m lane width.
                foreach(float s in new[]{-1f,1f})b.Box(new Vector3(x+s*2.45f-.15f,12,-5),new Vector3(x+s*2.45f+.15f,15.3f,-4.6f),metal);
                b.Box(new Vector3(x-2.6f,15.1f,-5),new Vector3(x+2.6f,15.4f,-4.6f),metal);
                b.Box(new Vector3(x+(x<0?-5:3),12,-4),new Vector3(x+(x<0?-3:5),13.3f,3),blue);
            }
            IntlSign("보안검색 · 출국심사",new Vector3(0,15.7f,-12.3f),Vector3.back,15,1.1f);
            IntlSign("34–39 · R1  탑승구 →",new Vector3(0,15.7f,17),Vector3.back,18,1);
            foreach(float x in new[]{-70f,70f}){
                b.Box(new Vector3(x-18,12,1),new Vector3(x+18,13.1f,8),wood);
                IntlSign("면세점",new Vector3(x,15.7f,1),Vector3.back,12,1);
            }
            string[] gates={"34","35","R1","36","37","38","39"};float[] locations={-110,-48,40,130,190,248,308};
            for(int i=0;i<gates.Length;i++){
                float x=locations[i];b.Box(new Vector3(x-4,12,51),new Vector3(x+4,13.1f,53),blue);
                IntlSign(gates[i]+"  탑승구",new Vector3(x,15.3f,51),Vector3.back,11,1);
                // East-pier seats stay wholly south of the belt, including their backrests.
                // Rows 34.5/37.5 leave the z=32 pier wall and z=40.25 belt edge clear.
                for(int row=0;row<2;row++)IntlSeat(b,x-9,12,x>=170?34.5f+row*3:36+row*4,9,wood,metal);
                Marker(InternationalAirportOrigin+new Vector3(x,12,49),gates[i]+"번");
            }
            // East gate pier: opposing walkways plus a completely free centre lane.
            IntlBelt(b,new Vector3(170,12,41),new Vector3(295,12,41),1.5f,metal,glass);
            IntlBelt(b,new Vector3(295,12,47),new Vector3(170,12,47),1.5f,metal,glass);
            IntlRoute("3F 동측 무빙워크",new[]{new Vector3(168,12,41),new Vector3(297,12,41)});
        }
        void IntlRestaurants(InternationalMeshBatch b,Material white,Material metal,Material wood){
            IntlSign("4F 식당 · 카페 · 휴게공간",new Vector3(0,21.5f,-15),Vector3.back,24,1.1f);
            foreach(float x in new[]{-116f,-58f,0f,58f,116f}){
                b.Box(new Vector3(x-20,18,37),new Vector3(x+20,19.1f,42),wood);
                for(int n=0;n<3;n++){
                    float tx=x-12+n*12;b.Box(new Vector3(tx-1.5f,18.85f,16),new Vector3(tx+1.5f,19.05f,19),white);
                    b.Box(new Vector3(tx-.2f,18,17.2f),new Vector3(tx+.2f,18.85f,17.6f),metal);
                    IntlSeat(b,tx,18,13.8f,3,wood,metal);
                }
            }
            IntlSign("식당가",new Vector3(-60,21.5f,36),Vector3.back,20,1);
            IntlSign("카페 · 휴게공간",new Vector3(65,21.5f,36),Vector3.back,20,1);
        }
        void BuildInternationalCorridor(Material stone,Material white,Material metal,Material glass,Material yellow,Material light){
            var b=new InternationalMeshBatch(internationalAirport,"국제선 B1 연결");
            internationalCorridorPath.Clear();internationalCorridorPath.AddRange(RoundGimpoPath(InternationalCorridor,7));
            for(int i=1;i<internationalCorridorPath.Count;i++){
                var a=internationalCorridorPath[i-1]-InternationalAirportOrigin;var c=internationalCorridorPath[i]-InternationalAirportOrigin;
                var d=c-a;var q=Quaternion.LookRotation(d);var right=Vector3.Cross(Vector3.up,d.normalized);
                b.RotatedBox((a+c)*.5f-Vector3.up*.12f,new Vector3(7.2f,.24f,d.magnitude+.18f),q,stone);
                b.RotatedBox((a+c)*.5f+Vector3.up*3.6f,new Vector3(7.6f,.2f,d.magnitude+.18f),q,white);
                // The first end joins the existing B1 corridor sideways. Leave a six-metre
                // side mouth instead of putting a wall across the incoming pedestrian line.
                var wallStart=i==1?a+d.normalized*6:a;float wallLength=Vector3.Distance(wallStart,c);
                foreach(float side in new[]{-1f,1f})b.RotatedBox((wallStart+c)*.5f+right*side*3.65f+Vector3.up*1.7f,new Vector3(.2f,3.4f,wallLength+.16f),q,white);
                b.RotatedBox((a+c)*.5f+Vector3.up*.01f,new Vector3(.28f,.02f,d.magnitude+.1f),q,yellow,false);
                if(d.magnitude>45){
                    int sections=Mathf.CeilToInt(d.magnitude/90);
                    for(int k=0;k<sections;k++){
                        var start=Vector3.Lerp(a,c,k/(float)sections)+d.normalized*7;var end=Vector3.Lerp(a,c,(k+1f)/sections)-d.normalized*7;
                        if(Vector3.Distance(start,end)<10)continue;
                        IntlBelt(b,start+right*1.7f,end+right*1.7f,1.3f,metal,glass);
                        IntlBelt(b,end-right*1.7f,start-right*1.7f,1.3f,metal,glass);
                    }
                    for(float along=14;along<d.magnitude-5;along+=30)
                        b.RotatedBox(a+d.normalized*along+Vector3.up*3.43f,new Vector3(4.7f,.05f,.3f),q,light,false);
                }
            }
            var bottom=new Vector3(-140,-5.8f,70);var top=new Vector3(-140,0,56);
            IntlStairs(b,bottom,top,6.6f,stone,metal,glass);
            var shaftSide=Vector3.right*3.65f;
            foreach(float side in new[]{-1f,1f})b.Quad(bottom+shaftSide*side,top+shaftSide*side,top+shaftSide*side+Vector3.up*3.6f,bottom+shaftSide*side+Vector3.up*3.6f,white,true,true);
            b.Quad(bottom-shaftSide+Vector3.up*3.6f,top-shaftSide+Vector3.up*3.6f,top+shaftSide+Vector3.up*3.6f,bottom+shaftSide+Vector3.up*3.6f,white,true,true);
            var route=new GameObject("국제선 B1 통로 → 1F").AddComponent<InternationalWalkRoute>();route.transform.SetParent(internationalAirport,false);
            var points=new List<Vector3>(internationalCorridorPath);points.Add(InternationalAirportOrigin+top);points.Add(InternationalAirportOrigin+new Vector3(-140,0,50));route.points=points.ToArray();
            IntlSign("국제선 청사 →",InternationalAirportConnector-InternationalAirportOrigin+Vector3.back*8+Vector3.up*2.8f,Vector3.forward,5,.7f);
            IntlSign("지하철 · 국내선 ←",new Vector3(-140,3.5f,54),Vector3.back,10,.8f);
            IntlSign("1F 도착홀 ↑",bottom+Vector3.up*3,Vector3.forward,6,.8f);
            b.Flush();
        }
        void IntlBelt(InternationalMeshBatch b,Vector3 a,Vector3 c,float width,Material metal,Material glass){
            var d=c-a;var q=Quaternion.LookRotation(d);var right=Vector3.Cross(Vector3.up,d.normalized);
            // Top at y=0: the motor's belt contact is not on a raised disconnected object.
            b.RotatedBox((a+c)*.5f-Vector3.up*.02f,new Vector3(width,.04f,d.magnitude),q,metal,false);
            foreach(float side in new[]{-1f,1f}){
                b.RotatedBox((a+c)*.5f+right*side*(width*.5f+.09f)+Vector3.up*.51f,new Vector3(.12f,1.02f,d.magnitude),q,glass);
                b.RotatedBox((a+c)*.5f+right*side*(width*.5f+.09f)+Vector3.up*1.04f,new Vector3(.16f,.1f,d.magnitude),q,metal);
            }
            var go=new GameObject("국제선 무빙워크");go.transform.SetParent(internationalAirport,false);go.transform.localPosition=(a+c)*.5f;go.transform.localRotation=q;
            var moving=go.AddComponent<MovingWalkway>();moving.length=d.magnitude;moving.width=width;moving.speed=1.15f;moving.Register();
        }
        void BuildInternationalForecourt(Material stone,Material white,Material glass,Material metal){
            var b=new InternationalMeshBatch(internationalAirport,"국제선 정문");
            IntlFloor(b,new Rect(-180,-94,360,29),null,0,stone);
            IntlFloor(b,new Rect(-155,-78,310,13),null,6,stone);
            b.Box(new Vector3(-155,6,-78.2f),new Vector3(155,7.2f,-78),glass);
            foreach(float x in new[]{-155f,155f})b.Box(new Vector3(x-.1f,6,-78),new Vector3(x+.1f,7.2f,-65),glass);
            b.Box(new Vector3(-160,10,-79),new Vector3(160,10.25f,-65),white);
            for(float x=-145;x<=145;x+=29)b.Box(new Vector3(x-.18f,6,-76),new Vector3(x+.18f,10,-75.6f),metal);
            b.Flush();
            IntlRoute("1F 정문 1–3번 출입문",new[]{new Vector3(-24,0,-60),new Vector3(-24,0,-86),new Vector3(24,0,-86),new Vector3(24,0,-60)});
            IntlRoute("2F 정문 발코니",new[]{new Vector3(-36,6,-60),new Vector3(-36,6,-72),new Vector3(36,6,-72),new Vector3(36,6,-60)});
        }

        // Directly append vertices to material/spatial batches. Hundreds of desks, treads and rails
        // never become individual GameObjects/Renderers. Mesh colliders stay active when culled.
        sealed class InternationalMeshBatch {
            readonly Transform parent;readonly string name;
            sealed class Part {public Material material;public bool collide;public readonly List<Vector3> vertices=new List<Vector3>();public readonly List<int> triangles=new List<int>();public readonly List<Vector2> uv=new List<Vector2>();}
            readonly Dictionary<string,Part> parts=new Dictionary<string,Part>();
            public InternationalMeshBatch(Transform parent,string name){this.parent=parent;this.name=name;}
            Part Get(Material mat,bool collision,Vector3 at){string key=mat.GetInstanceID()+":"+collision+":"+Mathf.FloorToInt((at.x+64)/128)+":"+Mathf.FloorToInt((at.z+96)/192);Part p;if(!parts.TryGetValue(key,out p)){p=new Part{material=mat,collide=collision};parts.Add(key,p);}return p;}
            static void Face(Part p,Vector3 a,Vector3 b,Vector3 c,Vector3 d){int n=p.vertices.Count;p.vertices.Add(a);p.vertices.Add(b);p.vertices.Add(c);p.vertices.Add(d);p.triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});p.uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});}
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Material mat,bool collision,bool twoSided=false){var p=Get(mat,collision,(a+b+c+d)*.25f);Face(p,a,b,c,d);if(twoSided)Face(p,d,c,b,a);}
            public void Box(Vector3 min,Vector3 max,Material mat,bool collision=true){if(max.x-min.x<.001f||max.y-min.y<.001f||max.z-min.z<.001f)return;RotatedBox((min+max)*.5f,max-min,Quaternion.identity,mat,collision);}
            public void RotatedBox(Vector3 center,Vector3 size,Quaternion rotation,Material mat,bool collision=true){
                var p=Get(mat,collision,center);var v=new Vector3[8];
                for(int i=0;i<8;i++)v[i]=center+rotation*Vector3.Scale(size*.5f,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                Face(p,v[0],v[4],v[6],v[2]);Face(p,v[1],v[3],v[7],v[5]);
                Face(p,v[0],v[1],v[5],v[4]);Face(p,v[2],v[6],v[7],v[3]);
                Face(p,v[0],v[2],v[3],v[1]);Face(p,v[4],v[5],v[7],v[6]);
            }
            public void Flush(){
                foreach(var part in parts.Values){
                    if(part.vertices.Count==0)continue;var mesh=new Mesh{name=name+" 合成",indexFormat=IndexFormat.UInt32};mesh.SetVertices(part.vertices);mesh.SetTriangles(part.triangles,0);mesh.SetUVs(0,part.uv);mesh.RecalculateNormals();mesh.RecalculateBounds();
                    var go=new GameObject(name+(part.collide?" 보행 구조":" 장식"));go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=part.material;renderer.shadowCastingMode=ShadowCastingMode.Off;
                    if(part.collide)go.AddComponent<MeshCollider>().sharedMesh=mesh;go.AddComponent<AccessMeshOwner>().mesh=mesh;
                }parts.Clear();
            }
        }
    }
}
