using UnityEngine;

namespace PeninsulaTime
{
    public partial class WorldBuilder
    {
        public Cabin ArrivalCabin{get;private set;}
        // Compact destination scene: a real cabin, track, platform and station hall for disembarking.
        public void BuildRailDestination(string station)
        {
            Clear();ArrivalCabin=null;SetupLight(new Color(.53f,.57f,.61f),new Color(1f,.92f,.8f));
            worldCamera.orthographic=false;worldCamera.fieldOfView=72;
            var concrete=Mat("arrival-concrete",new Color(.57f,.58f,.56f));
            var track=Mat("arrival-track",new Color(.20f,.23f,.25f));
            Primitive(PrimitiveType.Cube,"station ground",root.transform,new Vector3(0,-.35f,0),new Vector3(200,.5f,230),concrete);
            Primitive(PrimitiveType.Cube,"rail bed",root.transform,new Vector3(0,-.06f,0),new Vector3(4,.16f,130),track);
            foreach(float x in new[]{-.7175f,.7175f})Primitive(PrimitiveType.Cube,"rail",root.transform,new Vector3(x,.05f,0),new Vector3(.13f,.12f,130),Mat("arrival-steel",new Color(.69f,.72f,.74f)));
            for(int z=-62;z<=62;z+=3)Primitive(PrimitiveType.Cube,"sleeper",root.transform,new Vector3(0,.01f,z),new Vector3(3.2f,.11f,.22f),concrete);
            Primitive(PrimitiveType.Cube,"platform",root.transform,new Vector3(5.7f,.39f,0),new Vector3(7,.78f,120),concrete);
            Primitive(PrimitiveType.Cube,"yellow safety line",root.transform,new Vector3(2.34f,.8f,0),new Vector3(.14f,.02f,118),Mat("arrival-yellow",new Color(.95f,.75f,.18f)));
            for(int z=-50;z<=50;z+=12)
            {
                Primitive(PrimitiveType.Cylinder,"canopy column",root.transform,new Vector3(8.5f,2.7f,z),new Vector3(.2f,2.7f,.2f),track);
                Primitive(PrimitiveType.Cube,"canopy",root.transform,new Vector3(5.8f,5.35f,z),new Vector3(7.5f,.22f,11),track);
            }
            Label(station+"역 · KTX",new Vector3(5.7f,3.4f,14),1.1f,Color.white,root.transform);
            var train=CityModel("Ktx",Vector3.zero);
            if(train!=null)
            {
                train.name="도착한 KTX";var doors=AttachKtxCabin(train,2026);doors.Set(1);
                ArrivalCabin=doors.cabin;
            }
            else ArrivalCabin=null;
            worldCamera.transform.position=new Vector3(5f,2.45f,-10);worldCamera.transform.LookAt(new Vector3(0,1.4f,0));
        }
        public static string NearestCity(string name)
        {
            foreach(var city in GameContent.Cities)if(city.name==name||city.name.Contains(name)||name.Contains(city.name))return city.id;
            return "seoul";
        }
    }
}
