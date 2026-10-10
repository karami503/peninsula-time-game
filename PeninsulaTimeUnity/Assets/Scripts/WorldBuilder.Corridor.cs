using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // The track a ride runs along between stations, in the frame of the platform the ride left from:
    // local z = sign * along, along measured from that platform's centre. It lives beside the district root, so
    // the station behind can be unloaded and the one ahead loaded while the train is between them; the corridor
    // (train and rider inside) is then moved rigidly onto the new station's arrival track. Nothing teleports or
    // fades: the rider stays in the same moving car. Track is built in Chunk-metre pieces around the train and
    // removed behind it, like terrain chunks around a player.
    public sealed class RailCorridor : MonoBehaviour
    {
        public const float Chunk=60f,Ahead=480f,Behind=360f;
        public float sign=1f;
        // A single track, in tunnel or in the open (with the ground at local groundY, and a viaduct when that is
        // well below the rails); the style changes at `portal` metres along.
        public bool startTunnel,endTunnel;public float portal=float.MaxValue,groundY;
        public float groundEnd=float.NaN,groundSpan=1f; // the ground eases from groundY to groundEnd over groundSpan metres
        public float GroundAt(float along){return float.IsNaN(groundEnd)?groundY:Mathf.Lerp(groundY,groundEnd,Mathf.Clamp01(along/groundSpan));}
        public readonly List<Vector2> covered=new List<Vector2>(); // along ranges already tunnelled by a station
        public readonly Dictionary<int,GameObject> chunks=new Dictionary<int,GameObject>();
        public Vector3 Point(float along){return new Vector3(0,0,sign*along);}
        public bool Covered(float along){foreach(var r in covered)if(along>r.x&&along<r.y)return true;return false;}
        public int ChunkCount{get{return chunks.Count;}}
    }

    public partial class WorldBuilder
    {
        public static float StationTunnel{get{return SubwayTrain.Run+80f;}} // how far each station's own tunnel reaches

        // Starts a corridor at `from`'s track, its trains travelling `sign` along it.
        public RailCorridor BeginCorridor(SubwayTrain from,float sign)
        {
            var go=new GameObject("지하철 터널 구간");go.transform.SetParent(transform,false);
            go.transform.SetPositionAndRotation(from.transform.position,from.transform.rotation);
            var c=go.AddComponent<RailCorridor>();c.sign=sign;
            c.covered.Add(new Vector2(-1e6f,StationTunnel));
            return c;
        }

        // Builds and destroys tunnel chunks so that [along-Behind, along+Ahead] is tunnel; returns chunks alive.
        public int StreamCorridor(RailCorridor c,float along)
        {
            int first=Mathf.FloorToInt((along-RailCorridor.Behind)/RailCorridor.Chunk),last=Mathf.FloorToInt((along+RailCorridor.Ahead)/RailCorridor.Chunk);
            var gone=new List<int>();
            foreach(var pair in c.chunks)if(pair.Key<first||pair.Key>last){if(pair.Value!=null)DestroyImmediate(pair.Value);gone.Add(pair.Key);}
            foreach(int i in gone)c.chunks.Remove(i);
            for(int i=first;i<=last;i++)if(!c.chunks.ContainsKey(i))c.chunks[i]=CorridorChunk(c,i);
            return c.chunks.Count;
        }
        // Drops every chunk so the next stream rebuilds them against new station coverage.
        public void RefreshCorridor(RailCorridor c){foreach(var pair in c.chunks)if(pair.Value!=null)DestroyImmediate(pair.Value);c.chunks.Clear();}

        GameObject CorridorChunk(RailCorridor c,int index)
        {
            var chunk=new GameObject("터널 청크 "+index);chunk.transform.SetParent(c.transform,false);
            float a0=index*RailCorridor.Chunk,a1=a0+RailCorridor.Chunk;
            // Tunnel only where no station's own tunnel is (checked per metre).
            float from=a0;
            for(float a=a0;a<=a1;a+=1f)
            {
                if(a<a1&&!c.Covered(a+.5f))continue;
                if(a>from)NetworkSpan(chunk.transform,c,from,a);
                from=a+1f;
            }
            return chunk;
        }
        // One national-network track, split where the tunnel ends.
        void NetworkSpan(Transform parent,RailCorridor c,float a0,float a1)
        {
            if(a0<c.portal&&a1>c.portal){NetworkSpan(parent,c,a0,c.portal);NetworkSpan(parent,c,c.portal,a1);return;}
            bool tunnel=(a0+a1)*.5f<c.portal?c.startTunnel:c.endTunnel;
            float z0=Mathf.Min(c.sign*a0,c.sign*a1),z1=Mathf.Max(c.sign*a0,c.sign*a1);
            var steel=Mat("network-rail",new Color(.35f,.38f,.4f),.55f);
            Block("선로 바닥",parent,new Vector3(-2,-.18f,z0),new Vector3(2,-.08f,z1),Mat("network-ballast",new Color(.25f,.27f,.26f)),false);
            foreach(float rail in new[]{-.7175f,.7175f})Block("레일",parent,new Vector3(rail-.04f,-.02f,z0),new Vector3(rail+.04f,.08f,z1),steel,false);
            if(tunnel)
            {
                var wall=Mat("tunnel-wall",new Color(.42f,.43f,.44f),0,"concrete",4f);
                Block("터널 바닥",parent,new Vector3(-3.4f,-.6f,z0),new Vector3(3.4f,-.18f,z1),wall,false);
                foreach(float x in new[]{-3.4f,3.4f})Block("터널 벽",parent,new Vector3(x-.3f,-.6f,z0),new Vector3(x+.3f,6.2f,z1),wall,false);
                Block("터널 천장",parent,new Vector3(-3.7f,6.2f,z0),new Vector3(3.7f,6.5f,z1),wall,false);
                var lamp=Glow("tunnel-light",new Color(1f,.9f,.7f),1.5f);
                for(float z=Mathf.Ceil(z0/20f)*20f;z<z1-1.2f;z+=20f)Block("터널 조명",parent,new Vector3(-3.06f,3f,z),new Vector3(-2.94f,3.2f,z+1.2f),lamp,false);
                return;
            }
            // Open line: ground to the horizon on both sides (below any station's own ground), on a viaduct if high;
            // the ground eases from the station behind to the one ahead, in 15 m steps.
            var grass=Mat("network-grass",new Color(.28f,.37f,.23f));var concrete=Mat("network-platform",new Color(.62f,.63f,.61f),0,"pavement",2);
            for(float s0=a0;s0<a1;s0+=15f)
            {
                float s1=Mathf.Min(a1,s0+15f),g=c.GroundAt((s0+s1)*.5f),y0=Mathf.Min(c.sign*s0,c.sign*s1),y1=Mathf.Max(c.sign*s0,c.sign*s1);
                Block("선로 주변 지면",parent,new Vector3(-1000,g-.6f,y0),new Vector3(1000,g-.4f,y1),grass,false);
                if(g<-2f)
                {
                    Block("고가 상판",parent,new Vector3(-3,-.9f,y0),new Vector3(3,-.18f,y1),concrete,false);
                    for(float z=Mathf.Ceil(y0/30f)*30f;z<y1;z+=30f)Block("고가 교각",parent,new Vector3(-1,g-.4f,z-1),new Vector3(1,-.9f,z+1),concrete,false);
                }
                else for(float z=Mathf.Ceil(y0/50f)*50f;z<y1;z+=50f)Block("전차선 기둥",parent,new Vector3(2.6f,-.1f,z-.15f),new Vector3(2.9f,6.5f,z+.15f),steel,false);
            }
        }
        // Starts a national-network leg at a pose (forward = direction of travel); `cover` metres ahead already have
        // track (the station just left), so the corridor's own track begins after them.
        public RailCorridor BeginNetworkCorridor(Vector3 position,Quaternion rotation,float cover,bool tunnel,float groundY)
        {
            var go=new GameObject("철도 선로 구간");go.transform.SetParent(transform,false);
            go.transform.SetPositionAndRotation(position,rotation);
            var c=go.AddComponent<RailCorridor>();c.startTunnel=c.endTunnel=tunnel;c.groundY=groundY;
            c.covered.Add(new Vector2(-1e6f,cover));
            return c;
        }

        // The district behind has gone: its tunnel no longer covers the corridor's start.
        public void ReleaseCorridorStart(RailCorridor c){c.covered.RemoveAll(r=>r.x<-1e5f);RefreshCorridor(c);}

        // Rigidly moves the corridor so its point `along` (with the train travelling `sign` there) coincides with
        // `arrival`'s platform centre, arriving the way that track's trains arrive. Returns whether the corridor
        // was turned round (the train then reaches the platform with its doors on the other side).
        public bool AlignCorridor(RailCorridor c,SubwayTrain arrival,float along)
        {return AlignCorridorTo(c,arrival.transform.position,arrival.transform.rotation,arrival.direction,along,StationTunnel);}
        // The same onto any track: its platform centre `at`, frame `frame`, trains arriving along its local z * `direction`;
        // the station's own track reaches `cover` metres back from the platform centre.
        public bool AlignCorridorTo(RailCorridor c,Vector3 at,Quaternion frame,float direction,float along,float cover)
        {
            bool turned=Mathf.Sign(direction)!=Mathf.Sign(c.sign);
            var rotation=frame*(turned?Quaternion.Euler(0,180,0):Quaternion.identity);
            c.transform.rotation=rotation;
            c.transform.position=at-rotation*c.Point(along);
            c.covered.Add(new Vector2(along-cover,1e6f));
            RefreshCorridor(c);
            return turned;
        }
    }
}
