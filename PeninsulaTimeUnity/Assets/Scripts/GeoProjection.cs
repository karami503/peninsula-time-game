using UnityEngine;

namespace PeninsulaTime
{
    public static class GeoProjection
    {
        public static Vector3 ToWorld(float longitude,float latitude,float height=0f)
        {
            return new Vector3((longitude-127.7f)*12f,height,(latitude-38f)*10f);
        }
        // Inverse of ToWorld: (longitude, latitude) of a national map position.
        public static Vector2 ToGeo(Vector3 world){return new Vector2(world.x/12f+127.7f,world.z/10f+38f);}
    }
}
