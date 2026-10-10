using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Cars parked along local streets in town, near the player only (the chunks that have colliders). Any of them can be driven.
    public class ChangwonParked : MonoBehaviour
    {
        static readonly string[] Models={"Car","CarWhite","CarBlack","CarBlue","CarRed","CarWhite","CarBlack"};
        readonly Dictionary<long,List<GameObject>> byChunk=new Dictionary<long,List<GameObject>>();
        Vector2Int center=new Vector2Int(int.MinValue,0);float next;

        static long Key(int x,int z){return ((long)x<<32)^(uint)z;}
        void Update()
        {
            if(!ChangwonSession.Active||!ChangwonData.Loaded||Time.time<next)return;next=Time.time+.5f;
            var p=ChangwonSession.PlayerFeet;var c=ChangwonData.ChunkOf(p.x,p.z);if(c==center)return;center=c;
            int keep=PerformanceRuntime.Level==0?0:1;
            var wanted=new HashSet<long>();
            for(int dz=-keep;dz<=keep;dz++)for(int dx=-keep;dx<=keep;dx++){int x=c.x+dx,z=c.y+dz;if(x>=0&&z>=0&&x<ChangwonData.CX&&z<ChangwonData.CZ)wanted.Add(Key(x,z));}
            var drop=new List<long>();foreach(var k in byChunk.Keys)if(!wanted.Contains(k))drop.Add(k);
            foreach(var k in drop){foreach(var go in byChunk[k])if(go!=null&&(ChangwonSession.PlayerCar==null||go!=ChangwonSession.PlayerCar.gameObject))Destroy(go);byChunk.Remove(k);}
            foreach(var k in wanted)if(!byChunk.ContainsKey(k))byChunk[k]=Park((int)(k>>32),(int)(uint)(k&0xffffffff));
        }
        List<GameObject> Park(int cx,int cz)
        {
            var list=new List<GameObject>();var builder=ChangwonSession.Builder;if(builder==null)return list;
            var rnd=new System.Random(cx*92821+cz*68903);int budget=PerformanceRuntime.Level>=2?16:10;
            var min=ChangwonData.ChunkMin(cx,cz);float size=ChangwonData.ChunkSize;
            foreach(int id in ChangwonData.RoadsInChunk[cz*ChangwonData.CX+cx])
            {
                if(list.Count>=budget)break;
                var road=ChangwonData.Roads[id];if(road.cls!=ChangwonData.Local&&road.cls!=ChangwonData.Service||road.length<30||road.OneWay)continue;
                for(float s=12;s<road.length-12&&list.Count<budget;s+=26f+(float)rnd.NextDouble()*40f)
                {
                    Vector3 f;var at=road.At(s,out f);if(at.x<min.x||at.z<min.z||at.x>=min.x+size||at.z>=min.z+size)continue;
                    if(road.kind[0]!=ChangwonData.Ground)break;
                    byte land=ChangwonData.LandAt(at.x,at.z);if(land!=ChangwonData.Residential&&land!=ChangwonData.Urban&&land!=ChangwonData.Commercial)continue;
                    if(rnd.NextDouble()<.45)continue;
                    f.y=0;if(f.sqrMagnitude<.01f)continue;f.Normalize();var right=new Vector3(f.z,0,-f.x);
                    float side=rnd.NextDouble()<.5?1:-1;var pos=at+right*side*(road.width*.5f-1.1f);
                    if(Physics.CheckSphere(pos+Vector3.up*1f,1.4f,~0,QueryTriggerInteraction.Ignore)&&ChangwonWorld.Active!=null&&!NearOnlyGround(pos))continue;
                    var model=Models[rnd.Next(Models.Length)];
                    var go=builder.ChangwonVehicle(model,pos,"차 타기 (F)");if(go==null)continue;
                    go.name="주차 차량 "+model;go.transform.SetParent(ChangwonSession.Root,true);
                    var car=go.AddComponent<ChangwonCar>();car.model=model;car.Init(pos,side>0?f:-f);car.Move(Vector3.zero,.02f);
                    list.Add(go);
                }
            }
            return list;
        }
        // True when the only colliders around the spot are the ground meshes (roads/terrain), i.e. the spot is free.
        static readonly Collider[] overlap=new Collider[16];
        static bool NearOnlyGround(Vector3 pos)
        {
            int n=Physics.OverlapSphereNonAlloc(pos+Vector3.up*1f,1.4f,overlap,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<n;i++)if(!ChangwonCar.IsGround(overlap[i]))return false;return true;
        }
        void OnDestroy(){foreach(var l in byChunk.Values)foreach(var go in l)if(go!=null)Destroy(go);byChunk.Clear();}
    }
}
