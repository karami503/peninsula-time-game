using System.Collections.Generic;
using UnityEngine;
namespace PeninsulaTime {
    // The belt only carries grounded pedestrians; ordinary collision handling still applies.
    public class MovingWalkway : MonoBehaviour {
        static readonly List<MovingWalkway> active=new List<MovingWalkway>();
        public float length,width=.95f,speed=.9f;
        public void Register(){active.RemoveAll(b=>b==null);if(!active.Contains(this))active.Add(this);}
        void OnEnable(){Register();}
        void OnDisable(){active.Remove(this);}
        public static Vector3 VelocityAt(Vector3 feet){
            foreach(var belt in active){
                if(belt==null)continue;var p=Quaternion.Inverse(belt.transform.rotation)*(feet-belt.transform.position);
                if(Mathf.Abs(p.x)<belt.width*.5f&&Mathf.Abs(p.z)<belt.length*.5f&&p.y>=-.04f&&p.y<.18f)
                    return belt.transform.forward*belt.speed*TransitSpeed.WalkwayMultiplier;
            }return Vector3.zero;
        }
    }
}
