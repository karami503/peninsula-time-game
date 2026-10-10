using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // A walking passage from a district's transfer portal to a line that is not built in the district. It turns twice,
    // so from its middle stretch neither end is in sight: the station behind is unloaded and the one ahead loaded while
    // the walker is there, and the passage (walker inside) is moved rigidly onto the new station. Nothing fades or jumps.
    // Its own frame: origin on the floor at the portal, +z out of the concourse; out (A), across (B, where the stations
    // swap) and on (C), which climbs to the far station's level only after the second turn, so the climb (which may
    // rise through the far station's ground) is never seen from the near station. It ends at End, heading +z.
    public sealed class TransferPassage : MonoBehaviour
    {
        public const float Width=3.2f,Height=3f,Out=20f,Across=40f;
        public string stationId,lineId;public float rise,length; // length: of the last stretch, long enough for its stairs
        public bool atHub;
        public Vector3 End{get{return new Vector3(Across,rise,Out+length);}}
        // How far along the middle stretch the walker is, 0..1, or NaN outside it.
        public float Middle(Vector3 feet)
        {
            var p=transform.InverseTransformPoint(feet);
            return Mathf.Abs(p.z-Out)<Width*.5f&&p.x>-Width*.5f&&p.x<Across+Width*.5f&&p.y<Height?Mathf.Clamp01(p.x/Across):float.NaN;
        }
    }

    public partial class WorldBuilder
    {
        // Builds the passage at the portal; `rise` is the height of the far end above the portal floor.
        public TransferPassage BuildTransferPassage(DistrictTransferPortal portal,float rise,string toward)
        {
            const float W=TransferPassage.Width,H=TransferPassage.Height,O=TransferPassage.Out,A=TransferPassage.Across,w=W*.5f,t=.2f;
            float C=Mathf.Max(O,2*Mathf.Abs(rise)+10f),end=O+C,top=Mathf.Max(0,rise)+H,foot=O+w+2f,head=foot+2*Mathf.Abs(rise); // the stairs run from z=foot to z=head
            var go=new GameObject(portal.label+" 환승 통로");go.transform.SetParent(transform,false);
            var passage=go.AddComponent<TransferPassage>();passage.stationId=portal.stationId;passage.lineId=portal.lineId;passage.rise=rise;passage.length=C;
            var outward=portal.entry-portal.approach;outward.y=0;
            go.transform.SetPositionAndRotation(portal.entry,Quaternion.LookRotation(outward.normalized));
            var p=go.transform;
            var floor=Mat("passage-floor",new Color(.66f,.66f,.64f),0,"granite",2f);
            var wall=Mat("passage-wall",new Color(.86f,.86f,.83f),0,"white",2f);
            var stone=Mat("stair-stone",new Color(.70f,.70f,.68f),0,"granite",1);
            var yellow=Mat("tactile",new Color(.95f,.78f,.15f));
            // A: out from the portal.
            Block("환승 통로 바닥",p,new Vector3(-w,-.3f,0),new Vector3(w,0,O+w),floor);
            Block("환승 통로 벽",p,new Vector3(-w-t,0,0),new Vector3(-w,H,O-w),wall);
            Block("환승 통로 벽",p,new Vector3(w,0,0),new Vector3(w+t,H,O-w),wall);
            Block("환승 통로 천장",p,new Vector3(-w,H,0),new Vector3(w,H+t,O-w),wall,false);
            // B: across, level.
            Block("환승 통로 바닥",p,new Vector3(w,-.3f,O-w),new Vector3(A+w,0,O+w),floor);
            Block("환승 통로 벽",p,new Vector3(-w-t,0,O-w),new Vector3(-w,H,O+w),wall);
            Block("환승 통로 벽",p,new Vector3(A+w,0,O-w),new Vector3(A+w+t,H,O+w),wall);
            Block("환승 통로 벽",p,new Vector3(w,0,O-w-t),new Vector3(A+w,H,O-w),wall);
            Block("환승 통로 벽",p,new Vector3(-w,0,O+w),new Vector3(A-w,H,O+w+t),wall);
            Block("환승 통로 천장",p,new Vector3(-w,H,O-w),new Vector3(A+w,H+t,O+w),wall,false);
            // C: on, climbing (or descending) to the far station's level.
            if(rise>0)Block("환승 통로 벽",p,new Vector3(A-w,H,O+w),new Vector3(A+w,top,O+w+t),wall); // over C's mouth
            Block("환승 통로 바닥",p,new Vector3(A-w,-.3f,O+w),new Vector3(A+w,0,foot),floor);
            if(Mathf.Abs(rise)>.05f)StairFlight(p,p.TransformPoint(rise>0?new Vector3(A,rise,head):new Vector3(A,0,foot)),p.TransformPoint(rise>0?new Vector3(A,0,foot):new Vector3(A,rise,head)),W-.4f,stone,yellow);
            Block("환승 통로 바닥",p,new Vector3(A-w,rise-.3f,head),new Vector3(A+w,rise,end+.4f),floor); // overlaps the far floor across its wall
            Block("환승 통로 벽",p,new Vector3(A-w-t,Mathf.Min(0,rise),O+w),new Vector3(A-w,top,end),wall);
            Block("환승 통로 벽",p,new Vector3(A+w,Mathf.Min(0,rise),O+w),new Vector3(A+w+t,top,end),wall);
            Block("환승 통로 천장",p,new Vector3(A-w,top,O+w),new Vector3(A+w,top+t,end),wall,false);
            var glow=Glow("passage-light",new Color(1f,.96f,.88f),1.3f);
            foreach(var a in new[]{new Vector3(0,H,2),new Vector3(0,H,O),new Vector3(A,H,O),new Vector3(A,top,end-2)})
            {
                Block("환승 통로 조명",p,a+new Vector3(-.5f,-.06f,-.5f),a+new Vector3(.5f,-.01f,.5f),glow,false);
                var lamp=new GameObject("환승 통로 조명").AddComponent<Light>();lamp.transform.SetParent(p,false);
                lamp.transform.localPosition=a-Vector3.up*.6f;lamp.range=18;lamp.intensity=1.1f;lamp.color=new Color(1f,.96f,.88f);
            }
            var line=TransitNetwork.Line(portal.lineId);var colour=line!=null?line.color:Color.gray;
            Board(toward+" →",p,p.TransformPoint(new Vector3(w-.05f,2.2f,O*.5f)),-p.right,new Vector2(3.6f,.5f),colour,Color.white,.22f);
            Board("← "+toward,p,p.TransformPoint(new Vector3(A*.5f,H-.6f,O+w-.05f)),-p.forward,new Vector2(4.6f,.55f),colour,Color.white,.24f);
            OpenConcourseWall(passage);
            return passage;
        }
        // The concourse's outer wall gives way where the passage leaves it (again whenever the district is rebuilt).
        public void OpenConcourseWall(TransferPassage passage)
        {
            Physics.SyncTransforms();
            var p=passage.transform;float half=TransferPassage.Width*.5f+.2f;
            foreach(var wall in Physics.OverlapBox(p.TransformPoint(new Vector3(0,1.5f,.8f)),new Vector3(half-.2f,1f,1f),p.rotation))
            {
                if(wall.name!="대합실 외곽 벽")continue;
                // A Block: centre and size in its parent's frame.
                var parent=wall.transform.parent;var centre=wall.transform.localPosition;var size=wall.transform.localScale;
                var material=wall.GetComponent<Renderer>().sharedMaterial;
                int axis=size.x>size.z?0:2;float gap=parent.InverseTransformPoint(p.position)[axis];
                DestroyImmediate(wall.gameObject);
                foreach(var piece in new[]{new Vector2(centre[axis]-size[axis]*.5f,gap-half),new Vector2(gap+half,centre[axis]+size[axis]*.5f)})
                {
                    if(piece.y-piece.x<.05f)continue;
                    Vector3 min=centre-size*.5f,max=centre+size*.5f;min[axis]=piece.x;max[axis]=piece.y;
                    Block("대합실 외곽 벽",parent,min,max,material);
                }
            }
            Physics.SyncTransforms();
        }

        // The hub's own geometry (its ground, mapped-exit corridors, street) gives way inside the passage, which may
        // run through it on the way up to the concourse.
        public void CarveNetworkGround(TransferPassage passage)
        {
            const float H=TransferPassage.Height,O=TransferPassage.Out,A=TransferPassage.Across,w=TransferPassage.Width*.5f;
            var p=passage.transform;float rise=passage.rise,end=passage.End.z;
            var volumes=new List<NetworkExitVolume>{
                new NetworkExitVolume(p.TransformPoint(Vector3.zero),p.TransformPoint(new Vector3(0,0,O+w)),w,.02f,H-.02f),
                new NetworkExitVolume(p.TransformPoint(new Vector3(-w,0,O)),p.TransformPoint(new Vector3(A+w,0,O)),w,.02f,H-.02f),
                new NetworkExitVolume(p.TransformPoint(new Vector3(A,0,O+w)),p.TransformPoint(new Vector3(A,0,end)),w,Mathf.Min(0,rise)+.02f,Mathf.Max(0,rise)+H-.02f)};
            CutNetworkExitVolumes(volumes,false,filter=>!filter.transform.IsChildOf(p));
            Physics.SyncTransforms();
        }
        // Opens a doorway the passage's width in the network hub's concourse back wall; returns its floor centre
        // (the passage arrives heading +z).
        public Vector3 OpenNetworkHallBack(float width)
        {
            const float x=-6f; // west of the mapped-exit gate at the hall centre
            foreach(Transform child in root.transform)
            {
                if(child.name!="대합실 뒤벽")continue;
                var b=child.GetComponent<Renderer>().bounds;if(x-width*.5f<b.min.x||x+width*.5f>b.max.x)continue;
                var wall=child.GetComponent<Renderer>().sharedMaterial;
                DestroyImmediate(child.gameObject);
                Block("대합실 뒤벽",root.transform,b.min,new Vector3(x-width*.5f,b.max.y,b.max.z),wall);
                Block("대합실 뒤벽",root.transform,new Vector3(x+width*.5f,b.min.y,b.min.z),b.max,wall);
                break;
            }
            Marker(new Vector3(x,NetworkHallY,-84),"환승 통로");
            Physics.SyncTransforms();
            return new Vector3(x,NetworkHallY,-85.2f);
        }
    }
}
