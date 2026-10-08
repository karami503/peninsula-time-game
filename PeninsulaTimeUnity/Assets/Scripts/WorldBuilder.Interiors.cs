using UnityEngine;

namespace PeninsulaTime
{
    // Small walkable rooms for buildings on a city plot. Rooms are placed outside the plot
    // and connected by doors so their geometry never overlaps the exterior model.
    public class InteriorExit : Interactable
    {
        public Vector3 street;
        public override string Hint{get{return "거리로 나가기";}}
    }

    public partial class WorldBuilder
    {
        int interiorCount;
        void AddInterior(DoorInteract door,Vector3 street)
        {
            var centre=new Vector3(-1000f-interiorCount++*20f,0,0);
            door.hasInterior=true;door.entry=centre+new Vector3(0,1.65f,-2.4f);
            var room=new GameObject("건물 내부").transform;room.SetParent(root.transform,false);room.position=centre;
            var wall=Mat("interior-wall",new Color(.80f,.78f,.72f));
            var floor=Mat("interior-floor",new Color(.45f,.37f,.29f),0,"paving",2);
            var wood=Mat("interior-wood",new Color(.40f,.25f,.15f));
            var fabric=Mat("interior-fabric",new Color(.24f,.42f,.60f));
            Block("바닥",room,new Vector3(-4,-.22f,-4),new Vector3(4,0,4),floor);
            Block("천장",room,new Vector3(-4,3.3f,-4),new Vector3(4,3.5f,4),wall);
            Block("벽",room,new Vector3(-4,0,-4),new Vector3(-3.8f,3.3f,4),wall);
            Block("벽",room,new Vector3(3.8f,0,-4),new Vector3(4,3.3f,4),wall);
            Block("벽",room,new Vector3(-4,0,3.8f),new Vector3(4,3.3f,4),wall);
            Block("벽",room,new Vector3(-4,0,-4),new Vector3(-.8f,3.3f,-3.8f),wall);
            Block("벽",room,new Vector3(.8f,0,-4),new Vector3(4,3.3f,-3.8f),wall);
            Block("문 위",room,new Vector3(-.8f,2.3f,-4),new Vector3(.8f,3.3f,-3.8f),wall);
            var exit=Block("거리로 나가는 문",room,new Vector3(-.62f,0,-3.85f),new Vector3(.62f,2.2f,-3.8f),wood);
            exit.AddComponent<InteriorExit>().street=street+Vector3.back*2.3f+Vector3.up*1.65f;
            Block("테이블",room,new Vector3(-2.8f,.7f,.2f),new Vector3(-.7f,.82f,1.5f),wood);
            Block("소파",room,new Vector3(1.7f,.2f,.4f),new Vector3(3.2f,.7f,2.7f),fabric);
            Block("책장",room,new Vector3(-3.65f,0,2.6f),new Vector3(-3.35f,2.4f,3.6f),wood);
            var shop=Block("매대",room,new Vector3(1.9f,0,-.6f),new Vector3(3.5f,1.2f,.2f),wood);
            AddFixture(shop,"shop","편의점 간식 구매","간식을 구매했습니다.");
            string[] snacks={"생수","삼각김밥","과자"};
            for(int i=0;i<snacks.Length;i++)
            {
                var item=Block(snacks[i],room,new Vector3(2.0f+i*.47f,1.2f,-.45f),new Vector3(2.3f+i*.47f,1.55f,-.1f),Mat("snack-"+i,new Color(.55f+i*.12f,.52f,.29f+i*.1f)));
                AddFixture(item,"shop",snacks[i]+" 구매",snacks[i]);
            }
            var arcade=Block("오락기",room,new Vector3(2.4f,0,3.1f),new Vector3(3.2f,1.8f,3.6f),Mat("arcade",new Color(.20f,.29f,.39f)));
            AddFixture(arcade,"leisure","오락기 이용","잠시 즐겁게 놀았습니다.");
            Sign("편의점 · 휴게 공간",room,centre+new Vector3(0,2.8f,3.72f),Vector3.back,.22f,new Color(.98f,.88f,.65f));
        }
    }
}
