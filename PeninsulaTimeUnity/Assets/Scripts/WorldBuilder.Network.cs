using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Transit layer on the national map, as on Naver/Kakao maps: subway lines in their line colours along the
    // real track, KTX and 무궁화호 lines, BRT and median bus lanes, and the selected line drawn thick on top.
    public partial class WorldBuilder
    {
        // Line width relative to the map zoom (orthographic size), per kind.
        const float LineWidthPerZoom=.0042f,HighlightHeight=1.14f;
        readonly List<LineRenderer> networkLines=new List<LineRenderer>();
        readonly List<float> networkWeights=new List<float>();
        Transform networkLayer,highlightLayer;
        Material lineMaterial;
        public NetLine HighlightedLine{get;private set;}

        Material LineMaterial()
        {
            if(lineMaterial!=null)return lineMaterial;
            var shader=Shader.Find("Peninsula/LineColor");
            lineMaterial=new Material(shader!=null?shader:Shader.Find("Sprites/Default"));
            return lineMaterial;
        }
        static float KindHeight(string kind){return kind=="brt"?1.0f:kind=="mugunghwa"?1.03f:kind=="ktx"?1.06f:kind=="bus"?1.08f:1.1f;}
        static float KindWeight(string kind){return kind=="ktx"?1.5f:kind=="mugunghwa"?1.1f:kind=="brt"?.8f:kind=="bus"?.8f:1.15f;}

        // Draws every line except bus routes (those appear when picked). Called by BuildMap and after network edits.
        public void BuildNetworkLayer()
        {
            if(root==null)return;
            if(networkLayer!=null)DestroyImmediate(networkLayer.gameObject);
            networkLines.Clear();networkWeights.Clear();highlightLayer=null;
            networkLayer=new GameObject("교통망 · OpenStreetMap").transform;networkLayer.SetParent(root.transform,false);
            var brt=new Color(.86f,.18f,.20f);
            foreach(var corridor in TransitNetwork.Corridors)Polyline(networkLayer,corridor.Key,corridor.Value,brt,KindWeight("brt"),KindHeight("brt"));
            foreach(var line in TransitNetwork.Lines)
            {
                if(line.kind=="bus")continue;
                foreach(var shape in LineShapes(line))Polyline(networkLayer,line.name,shape,line.color,KindWeight(line.kind),KindHeight(line.kind));
            }
            if(HighlightedLine!=null)Highlight(TransitNetwork.Line(HighlightedLine.id));
            UpdateNetworkWidths();
        }
        // The track geometry when known, otherwise straight between stops.
        static List<List<Vector2>> LineShapes(NetLine line)
        {
            if(line.shapes.Count>0)return line.shapes;
            var straight=new List<Vector2>();foreach(var s in line.stops)straight.Add(new Vector2(s.lon,s.lat));
            return new List<List<Vector2>>{straight};
        }
        LineRenderer Polyline(Transform parent,string name,List<Vector2> lonLat,Color color,float weight,float height)
        {
            if(lonLat.Count<2)return null;
            var o=new GameObject(name);o.transform.SetParent(parent,false);
            var lr=o.AddComponent<LineRenderer>();lr.useWorldSpace=true;lr.positionCount=lonLat.Count;
            for(int i=0;i<lonLat.Count;i++)lr.SetPosition(i,GeoProjection.ToWorld(lonLat[i].x,lonLat[i].y,height));
            lr.sharedMaterial=LineMaterial();lr.startColor=lr.endColor=color;lr.numCornerVertices=2;lr.numCapVertices=2;
            lr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;lr.receiveShadows=false;
            networkLines.Add(lr);networkWeights.Add(weight);
            return lr;
        }
        // The picked line on top, wider with a white casing; bus routes are drawn only this way.
        public void Highlight(NetLine line)
        {
            HighlightedLine=line;
            if(highlightLayer!=null)
            {
                foreach(var lr in highlightLayer.GetComponentsInChildren<LineRenderer>()){int i=networkLines.IndexOf(lr);if(i>=0){networkLines.RemoveAt(i);networkWeights.RemoveAt(i);}}
                DestroyImmediate(highlightLayer.gameObject);highlightLayer=null;
            }
            if(line==null||networkLayer==null)return;
            highlightLayer=new GameObject("선택 노선").transform;highlightLayer.SetParent(networkLayer,false);
            foreach(var shape in LineShapes(line))
            {
                Polyline(highlightLayer,"테두리",shape,Color.white,KindWeight(line.kind)*3.2f,HighlightHeight);
                Polyline(highlightLayer,line.name,shape,line.color,KindWeight(line.kind)*2.2f,HighlightHeight+.02f);
            }
            UpdateNetworkWidths();
        }
        void UpdateNetworkWidths()
        {
            if(worldCamera==null||!worldCamera.orthographic)return;
            float unit=worldCamera.orthographicSize*LineWidthPerZoom;
            for(int i=0;i<networkLines.Count;i++)if(networkLines[i]!=null)networkLines[i].widthMultiplier=unit*networkWeights[i];
        }
    }
}
