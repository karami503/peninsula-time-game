using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Minecraft-style chunk loading for a built district: each Structure (building or road segment) belongs to the
    // CellSize grid cell under its centre. Cells within the render distance (plus Margin) of the focus (the camera,
    // which rides with the player or the train) are active; cells beyond that plus Hysteresis are deactivated with
    // their colliders and render batches. Batching runs first, on the full district.
    public sealed class ChunkStreamer : MonoBehaviour
    {
        public const float CellSize=128f,Margin=96f,Hysteresis=128f,Interval=.25f;
        public Transform focus;public SceneRenderBudget budget;
        readonly Dictionary<Vector2Int,List<GameObject>> cells=new Dictionary<Vector2Int,List<GameObject>>();
        readonly HashSet<Vector2Int> loaded=new HashSet<Vector2Int>();
        public int CellCount{get{return cells.Count;}}
        public int LoadedCells{get{return loaded.Count;}}
        public bool Indexed{get;private set;}
        public static float LoadRadius{get{return PerformanceRuntime.RenderDistance+Margin;}}

        IEnumerator Start()
        {
            while(budget!=null&&!budget.Ready)yield return null;
            Index();
            var wait=new WaitForSeconds(Interval);
            while(true){if(focus!=null)Stream(focus.position);yield return wait;}
        }
        public void Index()
        {
            cells.Clear();loaded.Clear();
            foreach(var s in GetComponentsInChildren<Structure>(true))
            {
                Bounds b=default(Bounds);bool any=false;
                foreach(var r in s.GetComponentsInChildren<Renderer>(true)){if(any)b.Encapsulate(r.bounds);else{b=r.bounds;any=true;}}
                if(!any)continue;
                var cell=Cell(b.center);List<GameObject> list;
                if(!cells.TryGetValue(cell,out list))cells[cell]=list=new List<GameObject>();
                list.Add(s.gameObject);
                if(s.gameObject.activeSelf)loaded.Add(cell);
            }
            Indexed=true;
        }
        static Vector2Int Cell(Vector3 p){return new Vector2Int(Mathf.FloorToInt(p.x/CellSize),Mathf.FloorToInt(p.z/CellSize));}
        // Distance from p to the nearest point of a cell, on the ground plane.
        public static float Distance(Vector2Int cell,Vector3 p)
        {
            float dx=Mathf.Max(0,Mathf.Max(cell.x*CellSize-p.x,p.x-(cell.x+1)*CellSize));
            float dz=Mathf.Max(0,Mathf.Max(cell.y*CellSize-p.z,p.z-(cell.y+1)*CellSize));
            return Mathf.Sqrt(dx*dx+dz*dz);
        }
        public bool IsLoaded(Vector3 p){return loaded.Contains(Cell(p))||!cells.ContainsKey(Cell(p));}
        // Loads every cell near `at` now and unloads the far ones; returns the number of cells loaded.
        public int Stream(Vector3 at)
        {
            if(!Indexed)Index();
            float load=LoadRadius,unload=load+Hysteresis;
            foreach(var pair in cells)
            {
                float d=Distance(pair.Key,at);bool on=loaded.Contains(pair.Key);
                if(!on&&d<=load){foreach(var g in pair.Value)if(g!=null)g.SetActive(true);loaded.Add(pair.Key);}
                else if(on&&d>unload){foreach(var g in pair.Value)if(g!=null)g.SetActive(false);loaded.Remove(pair.Key);}
            }
            return loaded.Count;
        }
    }
}
