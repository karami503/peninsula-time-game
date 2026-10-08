using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    public partial class WorldBuilder
    {
        // Gimpo's Line 5, Gimpo Goldline and Seohae use side platforms: tracks in the
        // middle, passengers outside. Widths and lengths are game-scale estimates,
        // not survey dimensions. Build at the origin before moving/turning `station`.
        const float GimpoSideTrackX=2.6f,GimpoSideWalkX=9.3f;

        // `number` is the first 1-based platform number. Adds two StationTrains in
        // lines order: lines[0] at +X, lines[1] at -X. Both entrances are at -Z,
        // centred on X +/-9.3, clear width 3 m and clear height 3.6 m.
        void BuildGimpoSidePlatforms(List<StationLine> lines,int number,float floorY,float halfLength,string title="김포공항",string english="Gimpo Int'l Airport",bool internalStairs=false)
        {
            float y=floorY,top=y+5f,track=y-1.1f,far=halfLength+220f;
            var floor=Mat("platform-floor",new Color(.78f,.77f,.74f),0,"pavement",3f);
            var bed=Mat("track-bed",new Color(.22f,.22f,.23f),0,"asphalt",4f);
            var wall=Mat("platform-wall",new Color(.88f,.88f,.85f),0,"white",2f);
            var tunnel=Mat("tunnel-wall",new Color(.42f,.43f,.44f),0,"concrete",4f);
            var ceiling=Mat("station-ceiling",new Color(.70f,.72f,.73f),.2f,"metal",6f);
            var steel=Mat("rail-steel",new Color(.70f,.76f,.80f),.55f);
            var tactile=Mat("tactile",new Color(.95f,.78f,.15f));
            var light=Glow("ceiling-light",new Color(1f,.97f,.9f),1.3f);
            var column=Mat("station-column",new Color(.82f,.82f,.80f),.1f,"white",1f);
            var seat=Mat("bench",new Color(.55f,.40f,.26f),0,"timber",1f);

            Block("상대식 승강장 선로 바닥",station,new Vector3(-5.1f,track-.4f,-far),new Vector3(5.1f,track,far),bed);
            float stairEnd=2+Mathf.Max(14f,(ConcourseY-floorY)*2f);
            if(internalStairs)BuildGangnamPlatformCeiling(y,halfLength,ceiling);
            else Block("상대식 승강장 천장",station,new Vector3(-13f,top,-halfLength),new Vector3(13f,top+.3f,halfLength),ceiling,false);
            for(int s=0;s<2;s++)
            {
                float sx=s==0?1f:-1f;var line=lines[s];
                var band=Mat("line-band-"+line.line,line.color);
                GimpoSideBlock("상대식 승강장 바닥",sx,5.1f,13f,y-1.1f,y,-halfLength,halfLength,floor);
                GimpoSideBlock("상대식 승강장 외벽",sx,13f,13.25f,y,top,-halfLength,halfLength,wall);
                GimpoSideBlock("승강장 안전선",sx,5.2f,5.7f,y,y+.012f,-halfLength+.3f,halfLength-.3f,tactile,false);
                GimpoSideBlock("승강장 보행 유도선",sx,7.1f,7.4f,y,y+.015f,-halfLength,halfLength-3,tactile,false);
                GimpoSideBlock("노선색 벽 띠",sx,12.93f,12.96f,y+2.8f,y+3.03f,-halfLength,halfLength,band,false);

                // Gimpo stairs join outside the end. Gangnam enters through its ceiling.
                if(internalStairs)GimpoSideBlock("승강장 끝 벽",sx,5.1f,13f,y,top,-halfLength-.25f,-halfLength,wall);
                else {
                GimpoSideBlock("승강장 출입구 옆벽",sx,5.1f,7.8f,y,top,-halfLength-.25f,-halfLength,wall);
                GimpoSideBlock("승강장 출입구 옆벽",sx,10.8f,13f,y,top,-halfLength-.25f,-halfLength,wall);
                GimpoSideBlock("승강장 출입구 상인방",sx,7.8f,10.8f,y+3.6f,top,-halfLength-.25f,-halfLength,wall);
                }
                GimpoSideBlock("승강장 끝 벽",sx,5.1f,13f,y,top,halfLength,halfLength+.25f,wall);

                foreach(float rx in new[]{-.72f,.72f})
                    Block("레일",station,new Vector3(sx*GimpoSideTrackX+rx-.04f,track,-far),new Vector3(sx*GimpoSideTrackX+rx+.04f,track+.16f,far),steel,false);
                // Each approach tunnel only spans its own track; no ceiling slab
                // stretches across the walking space outside the platform ends.
                foreach(var span in new[]{new Vector2(-far,-halfLength),new Vector2(halfLength,far)})
                {
                    GimpoSideBlock("상대식 선로 터널 벽",sx,5.1f,5.35f,track,top,span.x,span.y,tunnel);
                    GimpoSideBlock("상대식 선로 터널 천장",sx,0f,5.35f,top,top+.3f,span.x,span.y,tunnel,false);
                }
                for(float z=-far+8;z<far;z+=24)
                    if(Mathf.Abs(z)>halfLength)
                        GimpoSideBlock("터널 조명",sx,5.02f,5.08f,y+1.9f,y+2.15f,z,z+1.2f,light,false);

                var train=BuildGimpoSideDoors(number+s-1,sx,line,y,halfLength,floor,band);
                StationTrains.Add(train);
                // The straight route X +/-9.3 remains free of columns and benches.
                for(float z=-halfLength+20;z<halfLength-10;z+=25)
                {
                    if(internalStairs&&z>-2f&&z<stairEnd+4f)continue;
                    GimpoSideBlock("승강장 기둥",sx,11.6f,12.2f,y,top,z-.3f,z+.3f,column);
                    GimpoSideBlock("승강장 의자",sx,11.9f,12.65f,y+.4f,y+.5f,z+3,z+5.8f,seat);
                    Board(title+"  "+english,station,new Vector3(sx*12.88f,y+2.02f,z+8),new Vector3(-sx,0,0),new Vector2(7f,1.05f),new Color(.96f,.96f,.93f),new Color(.1f,.12f,.13f),.38f);
                    Board((number+s)+"  "+line.line+"  "+line.toward,station,new Vector3(sx*GimpoSideWalkX,y+3.6f,z),Vector3.back,new Vector2(6.8f,.72f),line.color,Color.white,.27f);
                    Board("나가는 곳 · 갈아타는 곳 ↓",station,new Vector3(sx*GimpoSideWalkX,y+3.6f,z+.11f),Vector3.forward,new Vector2(6.8f,.72f),new Color(.12f,.13f,.15f),new Color(1f,.82f,.1f),.3f);
                }
                foreach(var span in internalStairs?new[]{new Vector2(-halfLength+1,1),new Vector2(stairEnd+2,halfLength-1)}:new[]{new Vector2(-halfLength+1,halfLength-1)}){
                    GimpoSideBlock("승강장 조명",sx,8.05f,8.3f,top-.07f,top,span.x,span.y,light,false);
                    GimpoSideBlock("승강장 조명",sx,11f,11.25f,top-.07f,top,span.x,span.y,light,false);
                }
                for(float z=-halfLength+15;z<halfLength;z+=28)
                {
                    if(internalStairs&&z>0&&z<stairEnd+3)continue;
                    var lamp=new GameObject("상대식 승강장 조명").AddComponent<Light>();lamp.transform.SetParent(station,false);
                    lamp.transform.localPosition=new Vector3(sx*9.3f,top-.6f,z);lamp.type=LightType.Point;lamp.range=21f;lamp.intensity=1f;lamp.color=new Color(1f,.96f,.88f);
                }
                // Compact live text fits the board even when a destination name is
                // long; the full destination is on the fixed direction sign above.
                foreach(float z in new[]{-halfLength+8f,internalStairs?Mathf.Max(35f,stairEnd+8f):15f})
                {
                    var centre=new Vector3(sx*GimpoSideWalkX,y+3.2f,z);
                    Block("도착 안내 화면",station,centre-new Vector3(2.9f,.5f,.06f),centre+new Vector3(2.9f,.5f,.06f),Mat("arrival-screen",new Color(.03f,.03f,.04f)),false);
                    var text=Sign("",station,centre+Vector3.back*.08f,Vector3.back,.18f,new Color(1f,.62f,.12f));
                    var screen=text.gameObject.AddComponent<TransitBoard>();screen.text=text;
                    screen.compose=()=>train.line.line+"\n"+(train.Boardable?"열차 도착 · 열린 문으로 탑승":"다음 열차 "+ScreenDoor.Wait(train.SecondsToBoarding)+" 후");screen.Refresh();
                }
            }
        }

        GameObject GimpoSideBlock(string name,float sx,float inner,float outer,float bottom,float top,float from,float to,Material material,bool collide=true)
        {
            return Block(name,station,new Vector3(Mathf.Min(sx*inner,sx*outer),bottom,from),new Vector3(Mathf.Max(sx*inner,sx*outer),top,to),material,collide);
        }

        SubwayTrain BuildGimpoSideDoors(int side,float sx,StationLine line,float y,float halfLength,Material floor,Material band)
        {
            float x=sx*5.05f;
            var glass=Mat("psd-glass",new Color(.62f,.78f,.82f),.35f);
            var frame=Mat("psd-frame",new Color(.62f,.64f,.66f),.6f);
            var trainRoot=new GameObject((side+1)+"번 상대식 승강장 열차");trainRoot.transform.SetParent(station,false);
            var train=trainRoot.AddComponent<SubwayTrain>();train.line=line;train.trackX=sx*GimpoSideTrackX;
            train.floorY=y-1.1f+.16f;train.direction=-sx;
            trainRoot.transform.localPosition=new Vector3(train.trackX,train.floorY,0);
            // Fill the step to the car threshold. The platform screen doors are
            // farther out than the car body, so this lip must remain walkable.
            GimpoSideBlock("상대식 승강장 연단",sx,4.15f,5.1f,y-.5f,y,-40f,40f,floor);
            // Keep fixed screens along the entire side platform, not merely the
            // 80 m train: the extension has no boarding lip beside the tracks.
            float start=-halfLength;
            foreach(float car in SubwayTrain.CarCentres)foreach(float door in SubwayTrain.DoorOffsets)
            {
                float centre=car+door;
                if(centre-.9f>start)GimpoSidePanel("스크린도어 고정벽",train,x-.05f,x+.05f,y,y+2.2f,start,centre-.9f,glass);
                Block("스크린도어 문틀",station,new Vector3(x-.08f,y,centre-.95f),new Vector3(x+.08f,y+2.25f,centre-.9f),frame,false);
                Block("스크린도어 문틀",station,new Vector3(x-.08f,y,centre+.9f),new Vector3(x+.08f,y+2.25f,centre+.95f),frame,false);
                foreach(float half in new[]{-1f,1f})
                {
                    var leaf=GimpoSidePanel("스크린도어",train,x-.03f,x+.03f,y,y+2.15f,centre+(half<0?-.9f:0),centre+(half<0?0:.9f),glass);
                    train.leaves.Add(leaf.transform);train.leafClosed.Add(leaf.transform.localPosition);
                    train.leafOpen.Add(leaf.transform.localPosition+Vector3.forward*(half*.86f));
                }
                start=centre+.9f;
            }
            GimpoSidePanel("스크린도어 고정벽",train,x-.05f,x+.05f,y,y+2.2f,start,halfLength,glass);
            Block("스크린도어 상부",station,new Vector3(x-.15f,y+2.2f,-halfLength),new Vector3(x+.15f,y+2.75f,halfLength),Mat("psd-header",new Color(.20f,.22f,.25f)),false);
            Block("스크린도어 노선띠",station,new Vector3(x+sx*.16f-.01f,y+2.3f,-halfLength),new Vector3(x+sx*.16f+.01f,y+2.42f,halfLength),band,false);
            // BuildConsist's sx denotes which side of an island it serves. Reverse
            // it here so doors open OUT toward this side platform, not the tracks.
            for(int k=0;k<2;k++)train.consists.Add(BuildConsist(train,(side+1)+"번 승강장 열차 "+(k+1),-sx,band,side*7+k*131+StationIndex*977));
            train.Begin(side%2==0?SubwayTrain.Approach+4f:SubwayTrain.Cycle*.25f);
            return train;
        }

        GameObject GimpoSidePanel(string name,SubwayTrain train,float x0,float x1,float y0,float y1,float z0,float z1,Material material)
        {
            var panel=Block(name,station,new Vector3(x0,y0,z0),new Vector3(x1,y1,z1),material);
            panel.AddComponent<ScreenDoor>().train=train;return panel;
        }
    }
}
