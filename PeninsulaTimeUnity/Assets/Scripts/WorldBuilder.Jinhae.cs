using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // 진해구 (창원시) as one carved lump (CarvedDistrict), entered at 진해역 and walked out of on foot, or ridden out of
    // on the 진해선 shuttle to 경화역 (JinhaeTrain). 진해역: the OSM station point, south of the 진해선 with one island
    // platform between two tracks. Its building follows the national heritage record (국가유산청, registered 2005): 1926,
    // one storey, a building area of 248.4 m² (here 24.5 × 10.25 m), timber frame with cement-sprayed walls, a 맞배
    // (gabled) roof with a 박공 gable over the front and the back, asphalt shingles, aluminium windows (since 2002); window
    // spacing and heights are game estimates. 경화역: the OSM disused station point, given a side platform for the game.
    // Every object here is carved from a lump of its own (Sculpt).
    public partial class WorldBuilder
    {
        public const double JinhaeLon=128.6601222,JinhaeLat=35.1534167;
        public static readonly Rect JinhaeArea=Rect.MinMaxRect(128.60f,35.04f,128.83f,35.20f); // lon/lat box of 진해구
        public CarvedDistrict Carved{get{return root!=null?root.GetComponent<CarvedDistrict>():null;}}
        public Transform JinhaeStation{get;private set;}
        public Vector3 JinhaeSpawn{get;private set;}
        public Vector3 JinhaeFacing{get;private set;}
        public float JinhaeTrackOffset{get;private set;} // station frame: the main track runs along +x at this z
        public const float JinhaePlatformY=.75f;
        public JinhaeTrain JinhaeLine{get;private set;}
        public Transform GyeonghwaStation{get;private set;}
        // Where a visit to 진해역's or 경화역's platform starts (map station panel, a network train arriving), and the way it faces.
        public Vector3 JinhaePlatformSpawn{get;private set;}
        public Vector3 JinhaePlatformFacing{get;private set;}
        public Vector3 GyeonghwaSpawn{get;private set;}
        public Vector3 GyeonghwaFacing{get;private set;}
        public const double GyeonghwaLon=128.686598,GyeonghwaLat=35.1592214;
        Material carvedMaterial;
        Material CarvedMaterial()
        {
            if(carvedMaterial!=null)return carvedMaterial;
            var shader=Shader.Find("Peninsula/VertexTerrain");if(shader==null)shader=Shader.Find("Standard");
            carvedMaterial=new Material(shader){name="carved lump",enableInstancing=true};
            return carvedMaterial;
        }

        // Under a roof, so lit by lamps, not the sun. The carved 진해 lies partly below its station (down to the sea), so
        // there "below ground level" means nothing: only 진해역's hall is indoors.
        public bool Indoors(Vector3 eye)
        {
            if(Carved!=null)return IsJinhaeHallInterior(eye);
            return eye.y<-2f||IsDomesticAirportInterior(eye)||IsInternationalAirportInterior(eye)||IsDestinationTerminalInterior(eye);
        }
        public bool IsJinhaeHallInterior(Vector3 eye)
        {
            if(JinhaeStation==null)return false;
            var p=JinhaeStation.InverseTransformPoint(eye);return p.x>-11.75f&&p.x<11.75f&&p.z>-6.5f&&p.z<5.5f&&p.y>0&&p.y<4.5f;
        }

        public void BuildJinhae()
        {
            Clear();SetupLight(new Color(.6f,.62f,.64f),new Color(1f,.95f,.86f));
            worldCamera.orthographic=false;worldCamera.fieldOfView=72;dayNight=true;aerialDistrict=false;
            if(normalFar<=0){normalFar=worldCamera.farClipPlane;normalFog=RenderSettings.fogMode;}
            worldCamera.farClipPlane=24000f;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.00012f; // the land to the horizon, hazy
            var district=root.AddComponent<CarvedDistrict>();
            district.material=CarvedMaterial();district.focus=Viewer;district.tree=CherryTree();district.pine=Pine();district.people=BoxPeople();
            district.Origin(JinhaeLon,JinhaeLat);

            // The station's frame: x along the 진해선 past the station point, z toward the track.
            Vector2 along=Vector2.right,toward=Vector2.up;float d=28f,tA=-70f,tB=100f,best=float.MaxValue;
            foreach(var rail in district.MappedRails)
            {
                if(rail.Key!="진해선")continue;var p=rail.Value;
                for(int k=1;k<p.Length;k++)
                {
                    var seg=p[k]-p[k-1];float length=seg.magnitude;if(length<1f)continue;
                    float t=Mathf.Clamp01(Vector2.Dot(-p[k-1],seg)/(length*length));float gap=(p[k-1]+seg*t).magnitude;
                    if(gap>=best)continue;best=gap;
                    along=seg/length;if(along.x<0)along=-along;
                    toward=new Vector2(-along.y,along.x);if(Vector2.Dot(p[k-1],toward)<0)toward=-toward;
                    d=Vector2.Dot(p[k-1],toward);
                    float a=Vector2.Dot(p[k-1],along),b=Vector2.Dot(p[k],along);tA=Mathf.Min(a,b);tB=Mathf.Max(a,b);
                }
            }
            JinhaeTrackOffset=d;
            var station=new GameObject("진해역").transform;station.SetParent(root.transform,false);
            station.rotation=Quaternion.LookRotation(new Vector3(toward.x,0,toward.y));JinhaeStation=station;
            float px0=Mathf.Max(tA+32f,-60f),px1=Mathf.Min(tB-32f,60f);if(px1-px0<40f){px0=-40f;px1=40f;}
            px0=Mathf.Floor(px0);px1=Mathf.Ceil(px1);
            // The second track: off the main line, along the platform's other face, and back.
            var siding=new List<Vector2[]>{new[]{along*(px0-30f)+toward*d,along*(px0-2f)+toward*(d-9.2f),along*(px1+2f)+toward*(d-9.2f),along*(px1+30f)+toward*d}};
            // The line the shuttle runs on, and 경화역 on it: a frame with x across the line (+x: the platform side,
            // south) and z along it.
            var train=new GameObject("진해선 열차").AddComponent<JinhaeTrain>();train.transform.SetParent(root.transform,false);
            train.district=district;train.Lay(JinhaeTrain.Chain(district.MappedRails,"진해선",toward*d));
            float mid=(px0+px1)*.5f,home=train.Project(along*mid+toward*d);
            var g=district.Local(GyeonghwaLon,GyeonghwaLat);float there=train.Project(new Vector2(g.x,g.z));
            var gAt=train.Point(there);var gAlong=train.Tangent(there);if(gAlong.x<0)gAlong=-gAlong;var gAcross=new Vector2(gAlong.y,-gAlong.x);
            float gY=Mathf.Round(district.Raw(gAt)/CarvedDistrict.Step)*CarvedDistrict.Step;
            // The station's grounds are levelled to its floor, 경화역's to its own height, blending out over 40 m.
            System.Func<Vector2,float> stationPad=p=>
            {
                float x=Vector2.Dot(p,along),z=Vector2.Dot(p,toward);
                float dx=Mathf.Max(px0-35f-x,0,x-px1-35f),dz=Mathf.Max(-75f-z,0,z-d-12f);
                return 1f-Mathf.Sqrt(dx*dx+dz*dz)/40f;
            };
            System.Func<Vector2,float> gyeonghwaPad=p=>
            {
                var q=p-gAt;float x=Vector2.Dot(q,gAcross),z=Vector2.Dot(q,gAlong);
                float dx=Mathf.Max(-8f-x,0,x-16f),dz=Mathf.Max(Mathf.Abs(z)-34f,0);
                return 1f-Mathf.Sqrt(dx*dx+dz*dz)/40f;
            };
            district.flats.Add(new KeyValuePair<System.Func<Vector2,float>,float>(stationPad,0));
            district.flats.Add(new KeyValuePair<System.Func<Vector2,float>,float>(gyeonghwaPad,gY));
            district.site=p=>
            {
                float x=Vector2.Dot(p,along),z=Vector2.Dot(p,toward);
                if(x>-40f&&x<40f&&z>-75f&&z<-3f||x>-20f&&x<20f&&z>=-3f&&z<d-11f)return 1; // the square in front, the court behind
                if(x>px0-35f&&x<px1+35f&&z>6f&&z<d+4f)return 2;
                var q=p-gAt;float gx=Vector2.Dot(q,gAcross),gz=Vector2.Dot(q,gAlong);
                if(gx>-6f&&gx<14f&&Mathf.Abs(gz)<30f)return 3; // open ground behind 경화역's platform
                return 0;
            };
            district.Setup(JinhaeArea,siding);

            Place(StationHall(),station,"진해역 역사");
            Place(Platform(px0,px1,d),station,"진해역 승강장");
            Place(Crossing(d),station,"진해역 건널목");
            var stop=new GameObject("경화역").transform;stop.SetParent(root.transform,false);
            stop.SetPositionAndRotation(new Vector3(gAt.x,gY,gAt.y),Quaternion.LookRotation(new Vector3(gAlong.x,0,gAlong.y)));GyeonghwaStation=stop;
            Place(GyeonghwaPlatform(),stop,"경화역 승강장");
            var blossom=CherryTree();
            for(float z=-20f;z<=20f;z+=8f)Place(blossom,stop,"경화역 벚나무").transform.localPosition=new Vector3(10f,.15f,z);
            train.names=new[]{"진해","경화"};train.stops=new[]{home,there};
            train.platforms=new[]{along*mid+toward*(d-4.6f),gAt+gAcross*3.6f};
            train.Make(Coach(),DoorLeaf(),CarvedMaterial(),2);train.Begin(0);JinhaeLine=train;
            var navy=new Color(.12f,.2f,.32f);
            Board("진 해 역",station,station.TransformPoint(new Vector3(0,3.9f,-7.05f)),-station.forward,new Vector2(3.4f,.7f),navy,Color.white,.42f);
            foreach(float side in new[]{-1f,1f})
                Board("진해 Jinhae  · 경화 →",station,station.TransformPoint(new Vector3(0,2.9f,d-4.6f+side*.06f)),station.forward*side,new Vector2(3.6f,.55f),navy,Color.white,.3f);
            foreach(float side in new[]{-1f,1f})
                Board("경 화 역  Gyeonghwa",stop,stop.TransformPoint(new Vector3(3.6f,2.5f,side*.06f)),stop.forward*side,new Vector2(3.4f,.55f),navy,Color.white,.3f);
            var lamp=new GameObject("진해역 대합실 조명").AddComponent<Light>();lamp.transform.SetParent(station,false);
            lamp.transform.localPosition=new Vector3(0,4f,1f);lamp.range=18f;lamp.intensity=1.2f;lamp.color=new Color(1f,.93f,.8f);

            JinhaeSpawn=station.TransformPoint(new Vector3(0,.25f,1.5f));JinhaeFacing=-station.forward;
            JinhaePlatformSpawn=station.TransformPoint(new Vector3(0,JinhaePlatformY,d-2.6f));JinhaePlatformFacing=station.forward;
            GyeonghwaSpawn=stop.TransformPoint(new Vector3(3f,JinhaePlatformY,1f));GyeonghwaFacing=-stop.right;
            district.BuildAround(JinhaeSpawn,140f);
            Physics.SyncTransforms();
        }
        GameObject Place(Mesh mesh,Transform parent,string name)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=CarvedMaterial();go.AddComponent<AccessMeshOwner>().mesh=mesh;
            go.AddComponent<MeshCollider>().sharedMesh=mesh;
            return go;
        }

        static readonly Color Cream=new Color(.93f,.89f,.78f),Granite=new Color(.62f,.6f,.57f),Timber=new Color(.45f,.32f,.22f),Slate=new Color(.27f,.3f,.35f),
            Tile=new Color(.76f,.72f,.65f),Pane=new Color(.33f,.43f,.52f),Sill=new Color(.95f,.95f,.93f),Concrete=new Color(.7f,.69f,.66f),Tactile=new Color(.95f,.78f,.15f);

        static readonly Color Shingle=new Color(.29f,.27f,.28f),Aluminium=new Color(.78f,.8f,.82f);

        // The station building, in the station frame: walls x -12.25..12.25, front (south) at z=-4.25, back at z=6; a
        // gabled porch over the front entrance out to z=-6.75. Floor at .25, eaves at 4.5, ridge along x.
        static Mesh StationHall()
        {
            const float W=12.25f,Front=-4.25f,Back=6f,Eaves=4.5f,Mid=(Front+Back)*.5f,Half=(Back-Front)*.5f,Pitch=.6f,Cross=.88f;
            var s=new Sculpt(new Vector3(-13.5f,0,-8f),new Vector3(13.5f,9.5f,8f),.25f);
            System.Func<Vector3,float> main=p=>Eaves+(Half-Mathf.Abs(p.z-Mid))*Pitch; // the main roof's top over a point
            System.Func<Vector3,float> gable=p=>Eaves+(3.5f-Mathf.Abs(p.x))*Cross;  // the cross gables' top
            s.Box(new Vector3(-W,0,Front),new Vector3(W,Eaves,Back),Cream);
            s.Box(new Vector3(-3.5f,0,-6.75f),new Vector3(3.5f,Eaves,Front),Cream);                                   // porch
            s.Box(new Vector3(-W-.25f,0,Front-.25f),new Vector3(W+.25f,.75f,Back+.25f),Granite);                       // plinth
            s.Box(new Vector3(-3.75f,0,-7f),new Vector3(3.75f,.75f,Front),Granite);
            s.Box(new Vector3(-W-.25f,Eaves-.25f,Front-.25f),new Vector3(W+.25f,Eaves,Back+.25f),Timber);           // eaves band
            // Gable ends: the walls carried up under the roof at both ends, and under the front and back gables.
            var attic=new Bounds(new Vector3(0,7f,Mid),new Vector3(2*W,5f,2*Half));
            s.Shape(attic,p=>p.y>=Eaves&&p.y<main(p)-.4f,Cream);
            s.Shape(new Bounds(new Vector3(0,7f,-5.5f),new Vector3(7f,5f,2.5f)),p=>p.y>=Eaves&&p.y<gable(p)-.4f,Cream);
            s.Shape(new Bounds(new Vector3(0,6f,Back-.25f),new Vector3(7f,4f,.5f)),p=>p.y>=Eaves&&p.y<gable(p)-.4f,Cream);
            // The 맞배 roof: two slopes meeting at a ridge along the hall, out past the walls; shingled.
            s.Shape(new Bounds(new Vector3(0,6.5f,Mid),new Vector3(2*W+1.5f,5f,2*Half+1f)),p=>{float top=main(p);return p.y<top&&p.y>=top-.4f;},Shingle);
            // The front and back gables (박공) across it, their ridges running out over the porch and the back door.
            s.Shape(new Bounds(new Vector3(0,6.5f,Mid-1.5f),new Vector3(8f,5f,2*Half+6f)),p=>
            {
                if(p.z<-7.25f||p.z>Back+.5f||Mathf.Abs(p.x)>4f)return false;
                float top=gable(p);return p.y<top&&p.y>=top-.4f&&top-.4f>main(p)-.6f;
            },Shingle);
            // The hall and the porch carved out, tiled; doors front (through the porch) and back.
            s.Box(new Vector3(-W+.5f,.25f,Front+.5f),new Vector3(W-.5f,Eaves-.25f,Back-.5f),null);
            s.Box(new Vector3(-W+.5f,0,Front+.5f),new Vector3(W-.5f,.25f,Back-.5f),Tile);
            s.Box(new Vector3(-3f,.25f,-6.5f),new Vector3(3f,Eaves-.25f,Front+.5f),null).Box(new Vector3(-3f,0,-6.5f),new Vector3(3f,.25f,Front+.5f),Tile);
            s.Box(new Vector3(-2f,.25f,Front-.25f),new Vector3(2f,3.25f,Front+.75f),null);
            s.Box(new Vector3(-1.75f,.25f,-7.25f),new Vector3(1.75f,3.25f,-6.25f),null);
            s.Box(new Vector3(-2f,.25f,Back-.75f),new Vector3(2f,3.25f,Back+.5f),null);
            // Aluminium windows: a pane in a recess, a silver mullion and transom, a silver sill.
            foreach(float x in new[]{-10f,-7.5f,-5f,5f,7.5f,10f})foreach(float face in new[]{Front,Back})
            {
                float o=face<Mid?-1f:1f,recess=face-o*.25f,inside=face-o*.5f; // o: outward; the wall is .5 thick
                s.Box(new Vector3(x-.75f,1.25f,face),new Vector3(x+.75f,3.5f,recess),null).Box(new Vector3(x-.75f,1.25f,recess),new Vector3(x+.75f,3.5f,inside),Pane);
                s.Box(new Vector3(x-.2f,1.25f,face),new Vector3(x+.05f,3.5f,recess),Aluminium).Box(new Vector3(x-.75f,2.55f,face),new Vector3(x+.75f,2.7f,recess),Aluminium);
                s.Box(new Vector3(x-1f,1f,face),new Vector3(x+1f,1.25f,face+o*.25f),Aluminium);
            }
            foreach(float z in new[]{-1.5f,1f,3.5f})foreach(float side in new[]{-1f,1f})
            {
                float recess=side*(W-.25f),inside=side*(W-.5f);
                s.Box(new Vector3(side*W,1.25f,z-.5f),new Vector3(recess,3.5f,z+.5f),null).Box(new Vector3(recess,1.25f,z-.5f),new Vector3(inside,3.5f,z+.5f),Pane);
                s.Box(new Vector3(side*W,2.55f,z-.5f),new Vector3(recess,2.7f,z+.5f),Aluminium);
            }
            // Ticket window, benches.
            s.Box(new Vector3(-W+.5f,.25f,2.5f),new Vector3(-6f,1.25f,3.25f),Timber).Box(new Vector3(-W+.5f,1.25f,3f),new Vector3(-6f,3f,3.25f),Pane);
            foreach(float x in new[]{2.5f,6.5f})s.Box(new Vector3(x,.25f,-1.5f),new Vector3(x+3,.75f,-1f),Timber).Box(new Vector3(x,.75f,-1.25f),new Vector3(x+3,1.5f,-1f),Timber);
            return s.Mesh("진해역 역사");
        }
        // The island platform between the main track (z=d) and the second track (z=d-9.2).
        static Mesh Platform(float x0,float x1,float d)
        {
            var s=new Sculpt(new Vector3(x0,0,d-7.6f),new Vector3(x1,4f,d-1.6f),.25f);
            s.Box(new Vector3(x0,0,d-7.6f),new Vector3(x1,JinhaePlatformY,d-1.6f),Concrete);
            s.Box(new Vector3(x0,.5f,d-7.6f),new Vector3(x1,JinhaePlatformY,d-7.1f),Tactile).Box(new Vector3(x0,.5f,d-2.1f),new Vector3(x1,JinhaePlatformY,d-1.6f),Tactile);
            var steel=new Color(.38f,.44f,.5f);
            for(float x=-16f;x<=16f;x+=8f)
            {
                s.Box(new Vector3(x-.25f,.75f,d-4.85f),new Vector3(x+.25f,3.5f,d-4.35f),steel);
                if(x<16f)s.Box(new Vector3(x+2f,.75f,d-5.1f),new Vector3(x+5f,1.25f,d-4.6f),Timber);
            }
            s.Box(new Vector3(-20f,3.5f,d-7.1f),new Vector3(20f,3.75f,d-2.1f),new Color(.55f,.6f,.64f));
            return s.Mesh("진해역 승강장");
        }
        // From the court behind the building over the second track onto the platform, at grade.
        static Mesh Crossing(float d)
        {
            var s=new Sculpt(new Vector3(-1.75f,0,d-11f),new Vector3(1.75f,.75f,d-7.6f),.25f);
            s.Box(new Vector3(-1.5f,0,d-10.85f),new Vector3(1.5f,.5f,d-7.6f),new Color(.42f,.36f,.3f));
            s.Box(new Vector3(-1.5f,.25f,d-10.85f),new Vector3(-1.25f,.5f,d-7.6f),Tactile).Box(new Vector3(1.25f,.25f,d-10.85f),new Vector3(1.5f,.5f,d-7.6f),Tactile);
            return s.Mesh("진해역 건널목");
        }
        // A 진해선 coach, x across and z along, floor at .75 over the rail's ground: hollow, with longitudinal benches,
        // window bands, door openings at z=±6.5 each side (Coach doors carry the leaves) and a rounded roof.
        static Mesh Coach()
        {
            var s=new Sculpt(new Vector3(-1.5f,0,-10f),new Vector3(1.5f,4f,10f),.25f);
            var body=new Color(.92f,.9f,.84f);var band=new Color(.78f,.22f,.2f);var roof=new Color(.7f,.72f,.74f);var dark=new Color(.18f,.19f,.21f);
            var floor=new Color(.55f,.53f,.5f);var seat=new Color(.25f,.42f,.6f);
            s.Box(new Vector3(-1.5f,.5f,-10f),new Vector3(1.5f,3.25f,10f),body);
            s.Box(new Vector3(-1.5f,1.25f,-10f),new Vector3(1.5f,1.5f,10f),band);
            for(float w=-5.25f;w<5f;w+=2.25f)s.Box(new Vector3(-1.5f,1.75f,w),new Vector3(1.5f,2.75f,w+1.5f),Pane);
            foreach(float w in new[]{-9.5f,8f})s.Box(new Vector3(-1.5f,1.75f,w),new Vector3(1.5f,2.75f,w+1.5f),Pane);
            s.Box(new Vector3(-.75f,1.75f,-10f),new Vector3(.75f,2.75f,10f),Pane);                                   // end windows
            s.Shape(new Bounds(new Vector3(0,3.6f,0),new Vector3(3f,.9f,20f)),p=>Sq(p.x/1.5f)+Sq((p.y-3f)/.75f)<1f,roof);
            s.Box(new Vector3(-1.25f,.75f,-9.75f),new Vector3(1.25f,3f,9.75f),null).Box(new Vector3(-1.25f,.5f,-9.75f),new Vector3(1.25f,.75f,9.75f),floor);
            foreach(float side in new[]{-1f,1f})
            {
                float wall=side*1.25f,edge=side*.75f,back=side*1f;
                foreach(var run in new[]{new Vector2(-5.5f,5.5f),new Vector2(-9.5f,-7.5f),new Vector2(7.5f,9.5f)})
                    s.Box(new Vector3(edge,.75f,run.x),new Vector3(wall,1.25f,run.y),seat).Box(new Vector3(back,1.25f,run.x),new Vector3(wall,1.75f,run.y),seat);
                foreach(float door in new[]{-6.5f,6.5f})s.Box(new Vector3(wall,.75f,door-.75f),new Vector3(side*1.5f,3f,door+.75f),null);
            }
            foreach(float bogie in new[]{-7f,7f})
            {
                s.Box(new Vector3(-1.1f,.2f,bogie-1.5f),new Vector3(1.1f,.5f,bogie+1.5f),dark);
                foreach(float axle in new[]{bogie-1f,bogie+1f})s.Rod(new Vector3(-1f,.4f,axle),new Vector3(1f,.4f,axle),.35f,dark);
            }
            return s.Mesh("진해선 객차");
        }
        // One sliding door leaf, z -.75..0 from its hinge line, in the coach wall.
        static Mesh DoorLeaf()
        {
            var s=new Sculpt(new Vector3(-.125f,.75f,-.75f),new Vector3(.125f,3f,0),.25f);
            s.Box(new Vector3(-.125f,.75f,-.75f),new Vector3(.125f,3f,0),new Color(.6f,.62f,.64f)).Box(new Vector3(-.125f,1.75f,-.5f),new Vector3(.125f,2.5f,0),Pane);
            return s.Mesh("진해선 객차 문짝");
        }
        // 경화역's side platform, in its frame (x across the line, platform side +x, z along): edge 1.6 m off the track
        // centre, 4 m wide, 44 m long, floor at the coach floor (.75); steps down at the back to the ground, a name post.
        static Mesh GyeonghwaPlatform()
        {
            var s=new Sculpt(new Vector3(1.5f,-3f,-22f),new Vector3(7f,3f,22f),.25f);
            s.Box(new Vector3(1.6f,-3f,-22f),new Vector3(5.6f,JinhaePlatformY,22f),Concrete);
            s.Box(new Vector3(1.6f,.5f,-22f),new Vector3(2.1f,JinhaePlatformY,22f),Tactile);
            s.Box(new Vector3(5.6f,-3f,-3f),new Vector3(6.1f,.5f,3f),Concrete).Box(new Vector3(6.1f,-3f,-3f),new Vector3(6.6f,.25f,3f),Concrete);
            s.Box(new Vector3(3.5f,.75f,-.125f),new Vector3(3.75f,2.25f,.125f),new Color(.38f,.44f,.5f));
            for(float z=-15f;z<=15f;z+=10f)s.Box(new Vector3(4.5f,.75f,z),new Vector3(5f,1.25f,z+2.5f),Timber);
            return s.Mesh("경화역 승강장");
        }
        static float Sq(float v){return v*v;}

        // A pine of the hills: a bare trunk, a cone of stepped tiers of needles.
        static Mesh Pine()
        {
            var s=new Sculpt(new Vector3(-2f,0,-2f),new Vector3(2f,8f,2f),.25f);
            s.Box(new Vector3(-.25f,0,-.25f),new Vector3(.25f,3f,.25f),new Color(.36f,.26f,.2f));
            System.Func<Vector3,float> reach=p=>{float t=(p.y-2.25f)/5.5f;return t<0||t>1?-1f:1.9f*(1f-t)*(.7f+.3f*Mathf.Repeat(t*4f,1f));};
            var region=new Bounds(new Vector3(0,5f,0),new Vector3(4f,6f,4f));
            s.Shape(region,p=>reach(p)>0&&Sq(p.x)+Sq(p.z)<Sq(reach(p)),new Color(.2f,.36f,.23f));
            s.Shape(region,p=>reach(p)>0&&Sq(p.x)+Sq(p.z)<Sq(reach(p))&&Mathf.Repeat(Mathf.Sin(Vector3.Dot(p,new Vector3(12.9898f,78.233f,37.719f)))*43758.5453f,1f)<.25f,new Color(.27f,.45f,.28f));
            return s.Mesh("소나무");
        }

        // A cherry tree (진해 is known for them): trunk and branches, a round pink crown flecked with white.
        static Mesh CherryTree()
        {
            var s=new Sculpt(new Vector3(-2.5f,0,-2.5f),new Vector3(2.5f,6f,2.5f),.25f);
            var bark=new Color(.42f,.3f,.22f);
            s.Box(new Vector3(-.25f,0,-.25f),new Vector3(.25f,2.75f,.25f),bark);
            foreach(var tip in new[]{new Vector3(1.1f,3.3f,.6f),new Vector3(-1f,3.4f,-.7f),new Vector3(.2f,3.6f,-1.1f)})s.Rod(new Vector3(0,2.2f,0),tip,.18f,bark);
            var crown=new[]{new KeyValuePair<Vector3,Vector3>(new Vector3(0,4f,0),new Vector3(2.1f,1.5f,2.1f)),new KeyValuePair<Vector3,Vector3>(new Vector3(.8f,4.6f,.5f),new Vector3(1.2f,1f,1.2f))};
            System.Func<Vector3,bool> inCrown=p=>{foreach(var b in crown){var q=p-b.Key;if(Sq(q.x/b.Value.x)+Sq(q.y/b.Value.y)+Sq(q.z/b.Value.z)<1f)return true;}return false;};
            var region=new Bounds(new Vector3(0,4.2f,0),new Vector3(5f,3.6f,5f));
            s.Shape(region,inCrown,new Color(.98f,.76f,.84f));
            s.Shape(region,p=>inCrown(p)&&Mathf.Repeat(Mathf.Sin(Vector3.Dot(p,new Vector3(12.9898f,78.233f,37.719f)))*43758.5453f,1f)<.3f,new Color(1f,.92f,.94f));
            return s.Mesh("벚나무");
        }
        // Square-built people: block legs, body, arms and a big block head with a face; a few outfits.
        static Mesh[] BoxPeople()
        {
            var skin=new Color(.96f,.8f,.66f);var shoe=new Color(.2f,.18f,.16f);var eye=new Color(.1f,.1f,.12f);
            var outfits=new[]{
                new[]{new Color(.85f,.3f,.3f),new Color(.2f,.24f,.35f),new Color(.15f,.12f,.1f)},
                new[]{new Color(.3f,.55f,.85f),new Color(.3f,.3f,.32f),new Color(.3f,.2f,.12f)},
                new[]{new Color(.95f,.85f,.4f),new Color(.4f,.35f,.3f),new Color(.1f,.1f,.1f)},
                new[]{new Color(.4f,.7f,.45f),new Color(.85f,.85f,.8f),new Color(.45f,.3f,.2f)},
                new[]{new Color(.98f,.7f,.8f),new Color(.25f,.3f,.45f),new Color(.12f,.1f,.1f)},
                new[]{new Color(.9f,.9f,.9f),new Color(.15f,.15f,.18f),new Color(.6f,.6f,.62f)}};
            var meshes=new Mesh[outfits.Length];
            for(int n=0;n<outfits.Length;n++)
            {
                Color shirt=outfits[n][0],pants=outfits[n][1],hair=outfits[n][2];
                var s=new Sculpt(new Vector3(-.35f,0,-.2f),new Vector3(.35f,1.8f,.2f),.05f);
                s.Box(new Vector3(-.2f,0,-.1f),new Vector3(-.05f,.8f,.1f),pants).Box(new Vector3(.05f,0,-.1f),new Vector3(.2f,.8f,.1f),pants);
                s.Box(new Vector3(-.2f,0,-.1f),new Vector3(-.05f,.1f,.15f),shoe).Box(new Vector3(.05f,0,-.1f),new Vector3(.2f,.1f,.15f),shoe);
                s.Box(new Vector3(-.25f,.8f,-.15f),new Vector3(.25f,1.35f,.15f),shirt);
                s.Box(new Vector3(-.35f,.75f,-.1f),new Vector3(-.25f,.85f,.1f),skin).Box(new Vector3(.25f,.75f,-.1f),new Vector3(.35f,.85f,.1f),skin);
                s.Box(new Vector3(-.35f,.85f,-.1f),new Vector3(-.25f,1.3f,.1f),shirt).Box(new Vector3(.25f,.85f,-.1f),new Vector3(.35f,1.3f,.1f),shirt);
                s.Box(new Vector3(-.2f,1.35f,-.2f),new Vector3(.2f,1.75f,.2f),skin);
                s.Box(new Vector3(-.2f,1.65f,-.2f),new Vector3(.2f,1.8f,.2f),hair).Box(new Vector3(-.2f,1.4f,-.2f),new Vector3(.2f,1.65f,-.15f),hair);
                s.Box(new Vector3(-.12f,1.55f,.15f),new Vector3(-.07f,1.6f,.2f),eye).Box(new Vector3(.07f,1.55f,.15f),new Vector3(.12f,1.6f,.2f),eye);
                s.Box(new Vector3(-.05f,1.45f,.15f),new Vector3(.05f,1.5f,.2f),new Color(.85f,.45f,.45f));
                meshes[n]=s.Mesh("네모난 사람 "+n);
            }
            return meshes;
        }
    }
}
