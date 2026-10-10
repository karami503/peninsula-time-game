using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Plan-view sweep of every mapped district. Reports walkable surfaces running through a building's footprint
    // and trees or street lights standing inside a building. Writes sweep-report.txt and sweep-<district>.png to $SWEEP_DIR.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.MapSweepCheck.Run
    public static class MapSweepCheck
    {
        const float CellMetres = 1f, EdgeStep = .25f, GroundTop = 2f, PropHeight = 1.5f, PropRadius = .3f;
        const int MinClusterCells = 3, ReportedClusters = 10, ReportedProps = 20, StreetReach = 6, EmptyLotCells = 400;
        // Park sites: empty lots this big get a park at their centre, if the centre is inside the playable square.
        const int ParkLotCells = 1000;
        const float PlayableHalf = 300f;
        static readonly string[] DistrictFileNames = { "Gangnam", "SeoulStation", "Hongdae", "GimpoAirport" };
        const byte Walk = 1, Boundary = 2, Overlap = 4, Outside = 8, Interior = 16, Empty = 32;
        static readonly string[] WalkNames = { "road", "busway", "sidewalk", "footway", "junction", "platform" };
        static readonly string[] PropNames = { "Tree model", "Pine model", "StreetLight model" };
        static int problems;

        public static void Run()
        {
            string dir = System.Environment.GetEnvironmentVariable("SWEEP_DIR");
            if (string.IsNullOrEmpty(dir)) { Debug.LogError("MapSweepCheck: set SWEEP_DIR"); EditorApplication.Exit(1); return; }
            Directory.CreateDirectory(dir);
            var camera = new GameObject("Sweep camera").AddComponent<Camera>();
            var world = new GameObject("Sweep world").AddComponent<WorldBuilder>();
            world.worldCamera = camera;
            var report = new StringBuilder();
            for (int district = 0; district < 4; district++)
            {
                world.BuildDistrict(district);
                Physics.SyncTransforms();
                SweepDistrict(world, district, dir, report);
            }
            File.WriteAllText(Path.Combine(dir, "sweep-report.txt"), report.ToString());
            Debug.Log("MapSweepCheck: " + (problems == 0 ? "passed" : problems + " problems") + " (report " + dir + "/sweep-report.txt)");
            EditorApplication.Exit(problems == 0 ? 0 : 1);
        }

        // Writes Resources/Geo/<District>Parks.txt (local x,z metres). Run once with WRITE_PARKS=1; WorldBuilder reads it.
        static void WriteParkSites(Plan plan, List<Cluster> lots, int district)
        {
            var rows = new StringBuilder("# Park sites: centres of empty lots of at least " + ParkLotCells
                + " m2 inside +-" + PlayableHalf + " m. Local x,z metres.\n");
            foreach (var lot in lots)
            {
                if (lot.Size < ParkLotCells) continue;
                float x = plan.Origin.x + lot.CentreX * CellMetres, z = plan.Origin.z + lot.CentreZ * CellMetres;
                if (Mathf.Abs(x) > PlayableHalf || Mathf.Abs(z) > PlayableHalf) continue;
                rows.Append(x.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                    .Append(z.ToString("0.0", CultureInfo.InvariantCulture)).Append('\n');
            }
            File.WriteAllText(Path.Combine(Application.dataPath, "Resources", "Geo", DistrictFileNames[district] + "Parks.txt"), rows.ToString());
        }

        static void SweepDistrict(WorldBuilder world, int district, string dir, StringBuilder report)
        {
            var walkFilters = new List<MeshFilter>();
            var buildingFilters = new List<MeshFilter>();
            foreach (var filter in world.root.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                string name = filter.name.ToLowerInvariant();
                if (System.Array.IndexOf(WalkNames, name) >= 0) walkFilters.Add(filter);
                else if (name.StartsWith("building") || name.StartsWith("roof")) buildingFilters.Add(filter);
            }
            var plan = new Plan(BoundsOf(walkFilters, buildingFilters));
            foreach (var filter in walkFilters) FillMesh(plan, filter, Walk);
            foreach (var filter in buildingFilters) MarkGroundEdges(plan, filter);
            MarkInterior(plan);
            int overlap = 0;
            // A walk cell counts only when it is at least one cell inside the wall: a 1 m sliver where a surface
            // touches the wall line is rasterization noise, not an intrusion.
            for (int i = 0; i < plan.Cells.Length; i++)
                if ((plan.Cells[i] & Walk) != 0 && (plan.Cells[i] & Interior) != 0 && !NearWall(plan, i)) { plan.Cells[i] |= Overlap; overlap++; }
            var clusters = FindClusters(plan, Overlap);
            MarkEmptyLots(plan);
            var lots = FindClusters(plan, Empty);
            lots.RemoveAll(c => c.Size < EmptyLotCells);
            lots.Sort((a, b) => b.Size.CompareTo(a.Size));
            if (System.Environment.GetEnvironmentVariable("WRITE_PARKS") == "1") WriteParkSites(plan, lots, district);
            report.AppendLine("  empty lots next to a street (>= " + EmptyLotCells + " m2): " + lots.Count);
            for (int i = 0; i < lots.Count && i < ReportedClusters; i++)
                report.AppendLine("  empty lot " + lots[i].Size + " m2 at " + plan.WorldOf(lots[i].CentreX, lots[i].CentreZ)
                    + " extent x" + lots[i].MinX + ".." + lots[i].MaxX + " z" + lots[i].MinZ + ".." + lots[i].MaxZ);
            int bigClusters = 0;
            foreach (var c in clusters) if (c.Size >= MinClusterCells) bigClusters++;
            var props = PropsInBuildings(world);
            problems += bigClusters + props.Count;
            report.AppendLine("district " + district + ": grid " + plan.Width + "x" + plan.Depth + " m, origin " + plan.Origin
                + ", walk meshes " + walkFilters.Count + ", building meshes " + buildingFilters.Count
                + ", overlap cells " + overlap + ", clusters >= " + MinClusterCells + " cells " + bigClusters
                + ", props inside buildings " + props.Count);
            clusters.Sort((a, b) => b.Size.CompareTo(a.Size));
            for (int i = 0; i < clusters.Count && i < ReportedClusters; i++)
                report.AppendLine("  overlap " + clusters[i].Size + " cells at " + plan.WorldOf(clusters[i].CentreX, clusters[i].CentreZ)
                    + " extent x" + clusters[i].MinX + ".." + clusters[i].MaxX + " z" + clusters[i].MinZ + ".." + clusters[i].MaxZ
                    + " walk " + string.Join("/", KindsAt(plan, walkFilters, clusters[i].CentreX, clusters[i].CentreZ)));
            for (int i = 0; i < props.Count && i < ReportedProps; i++) report.AppendLine("  prop in building " + props[i]);
            WritePng(plan, Path.Combine(dir, "sweep-" + district + ".png"));
        }

        static Bounds BoundsOf(List<MeshFilter> walk, List<MeshFilter> buildings)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var filter in walk)
                foreach (var v in filter.sharedMesh.vertices)
                {
                    var p = filter.transform.localToWorldMatrix.MultiplyPoint3x4(v);
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
            foreach (var filter in buildings)
                foreach (var v in filter.sharedMesh.vertices)
                    bounds.Encapsulate(filter.transform.localToWorldMatrix.MultiplyPoint3x4(v));
            return bounds;
        }

        static void FillMesh(Plan plan, MeshFilter filter, byte flag)
        {
            var vertices = filter.sharedMesh.vertices;
            var triangles = filter.sharedMesh.triangles;
            var toWorld = filter.transform.localToWorldMatrix;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                var a = toWorld.MultiplyPoint3x4(vertices[triangles[i]]);
                var b = toWorld.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                var c = toWorld.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                // Elevated decks (bridges, flyovers) pass over buildings by design; only ground-level surfaces count.
                if (Mathf.Min(a.y, Mathf.Min(b.y, c.y)) < GroundTop) FillTriangle(plan, plan.Local(a), plan.Local(b), plan.Local(c), flag);
            }
        }

        // Edges that touch the ground (both ends under GroundTop) trace the building footprint; roofs and upper floors are skipped.
        static void MarkGroundEdges(Plan plan, MeshFilter filter)
        {
            var vertices = filter.sharedMesh.vertices;
            var triangles = filter.sharedMesh.triangles;
            var toWorld = filter.transform.localToWorldMatrix;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    var a = toWorld.MultiplyPoint3x4(vertices[triangles[i + e]]);
                    var b = toWorld.MultiplyPoint3x4(vertices[triangles[i + (e + 1) % 3]]);
                    if (Mathf.Max(a.y, b.y) < GroundTop) MarkLine(plan, plan.Local(a), plan.Local(b), Boundary);
                }
        }

        // Flood from the grid border through open cells; cells it cannot reach sit inside a building's ground ring.
        // Walls only touching a sidewalk edge stay outside, so only walkable cells really under a building count.
        static void MarkInterior(Plan plan)
        {
            var queue = new Queue<int>();
            for (int i = 0; i < plan.Cells.Length; i++)
            {
                int x = i % plan.Width, z = i / plan.Width;
                if (x == 0 || z == 0 || x == plan.Width - 1 || z == plan.Depth - 1) Reach(plan, queue, x, z);
            }
            while (queue.Count > 0)
            {
                int k = queue.Dequeue();
                int x = k % plan.Width, z = k / plan.Width;
                Reach(plan, queue, x + 1, z);
                Reach(plan, queue, x - 1, z);
                Reach(plan, queue, x, z + 1);
                Reach(plan, queue, x, z - 1);
            }
            for (int i = 0; i < plan.Cells.Length; i++)
                if ((plan.Cells[i] & (Boundary | Outside)) == 0) plan.Cells[i] |= Interior;
        }

        // Names of the walk meshes that cover a cell, for the report.
        static List<string> KindsAt(Plan plan, List<MeshFilter> walk, float cellX, float cellZ)
        {
            var names = new List<string>();
            var p = new Vector2(cellX, cellZ);
            foreach (var filter in walk)
            {
                if (names.Contains(filter.name)) continue;
                var vertices = filter.sharedMesh.vertices;
                var triangles = filter.sharedMesh.triangles;
                var toWorld = filter.transform.localToWorldMatrix;
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    var a = plan.Local(toWorld.MultiplyPoint3x4(vertices[triangles[i]]));
                    var b = plan.Local(toWorld.MultiplyPoint3x4(vertices[triangles[i + 1]]));
                    var c = plan.Local(toWorld.MultiplyPoint3x4(vertices[triangles[i + 2]]));
                    float area = Cross(a, b, c);
                    if (Mathf.Abs(area) < 1e-6f) continue;
                    if (Cross(a, b, p) * area >= 0 && Cross(b, c, p) * area >= 0 && Cross(c, a, p) * area >= 0) { names.Add(filter.name); break; }
                }
            }
            return names;
        }

        static bool NearWall(Plan plan, int index)
        {
            int x = index % plan.Width, z = index / plan.Width;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (plan.Inside(x + dx, z + dz) && (plan.Cells[(z + dz) * plan.Width + x + dx] & Boundary) != 0) return true;
            return false;
        }

        static void Reach(Plan plan, Queue<int> queue, int x, int z)
        {
            if (!plan.Inside(x, z)) return;
            int k = z * plan.Width + x;
            if ((plan.Cells[k] & (Boundary | Outside)) != 0) return;
            plan.Cells[k] |= Outside;
            queue.Enqueue(k);
        }

        static void FillTriangle(Plan plan, Vector2 a, Vector2 b, Vector2 c, byte flag)
        {
            float area = Cross(a, b, c);
            if (Mathf.Abs(area) < 1e-6f) return;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int x1 = Mathf.Min(plan.Width - 1, Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int z1 = Mathf.Min(plan.Depth - 1, Mathf.FloorToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + .5f, z + .5f);
                    if (Cross(a, b, p) * area >= 0 && Cross(b, c, p) * area >= 0 && Cross(c, a, p) * area >= 0) plan.Mark(x, z, flag);
                }
        }

        static void MarkLine(Plan plan, Vector2 a, Vector2 b, byte flag)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / EdgeStep));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                plan.Mark(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), flag);
            }
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 p) { return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x); }

        static List<string> PropsInBuildings(WorldBuilder world)
        {
            var found = new List<string>();
            foreach (var t in world.root.GetComponentsInChildren<Transform>())
            {
                if (System.Array.IndexOf(PropNames, t.name) < 0 || !t.gameObject.activeInHierarchy) continue;
                var hits = Physics.OverlapSphere(t.position + Vector3.up * PropHeight, PropRadius, ~0, QueryTriggerInteraction.Ignore);
                foreach (var hit in hits)
                {
                    string name = hit.gameObject.name.ToLowerInvariant();
                    if (name.StartsWith("building") || name.StartsWith("roof")) { found.Add(t.name + " at " + t.position); break; }
                }
            }
            return found;
        }

        // Empty ground within StreetReach cells of a walkable surface, and not under any building.
        static void MarkEmptyLots(Plan plan)
        {
            var distance = new int[plan.Cells.Length];
            var queue = new Queue<int>();
            for (int i = 0; i < distance.Length; i++)
            {
                distance[i] = (plan.Cells[i] & Walk) != 0 ? 0 : -1;
                if (distance[i] == 0) queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int k = queue.Dequeue();
                if (distance[k] == StreetReach) continue;
                int x = k % plan.Width, z = k / plan.Width;
                Spread(plan, distance, queue, x + 1, z, distance[k] + 1);
                Spread(plan, distance, queue, x - 1, z, distance[k] + 1);
                Spread(plan, distance, queue, x, z + 1, distance[k] + 1);
                Spread(plan, distance, queue, x, z - 1, distance[k] + 1);
            }
            for (int i = 0; i < distance.Length; i++)
                if (distance[i] > 0 && (plan.Cells[i] & (Walk | Boundary | Interior)) == 0) plan.Cells[i] |= Empty;
        }

        static void Spread(Plan plan, int[] distance, Queue<int> queue, int x, int z, int next)
        {
            if (!plan.Inside(x, z)) return;
            int k = z * plan.Width + x;
            if (distance[k] >= 0) return;
            distance[k] = next;
            queue.Enqueue(k);
        }

        static List<Cluster> FindClusters(Plan plan, byte mask)
        {
            var found = new List<Cluster>();
            var seen = new bool[plan.Cells.Length];
            var queue = new Queue<int>();
            for (int start = 0; start < plan.Cells.Length; start++)
            {
                if (seen[start] || (plan.Cells[start] & mask) == 0) continue;
                var cluster = new Cluster();
                seen[start] = true;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int k = queue.Dequeue();
                    int x = k % plan.Width, z = k / plan.Width;
                    cluster.Add(x, z);
                    Enqueue(plan, seen, queue, mask, x + 1, z);
                    Enqueue(plan, seen, queue, mask, x - 1, z);
                    Enqueue(plan, seen, queue, mask, x, z + 1);
                    Enqueue(plan, seen, queue, mask, x, z - 1);
                }
                found.Add(cluster);
            }
            return found;
        }

        static void Enqueue(Plan plan, bool[] seen, Queue<int> queue, byte mask, int x, int z)
        {
            if (!plan.Inside(x, z)) return;
            int k = z * plan.Width + x;
            if (seen[k] || (plan.Cells[k] & mask) == 0) return;
            seen[k] = true;
            queue.Enqueue(k);
        }

        static void WritePng(Plan plan, string path)
        {
            var texture = new Texture2D(plan.Width, plan.Depth, TextureFormat.RGB24, false);
            var pixels = new Color32[plan.Cells.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte c = plan.Cells[i];
                pixels[i] = (c & Overlap) != 0 ? new Color32(255, 0, 255, 255)
                    : (c & Boundary) != 0 ? new Color32(220, 60, 40, 255)
                    : (c & Walk) != 0 ? new Color32(150, 150, 150, 255)
                    : (c & Interior) != 0 ? new Color32(45, 55, 90, 255)
                    : (c & Empty) != 0 ? new Color32(60, 120, 60, 255)
                    : new Color32(30, 30, 34, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(texture));
            Object.DestroyImmediate(texture);
        }

        sealed class Plan
        {
            public readonly int Width, Depth;
            public readonly Vector3 Origin;
            public readonly byte[] Cells;
            public Plan(Bounds bounds)
            {
                Origin = new Vector3(bounds.min.x, 0, bounds.min.z);
                Width = Mathf.CeilToInt(bounds.size.x / CellMetres) + 1;
                Depth = Mathf.CeilToInt(bounds.size.z / CellMetres) + 1;
                Cells = new byte[Width * Depth];
            }
            public bool Inside(int x, int z) { return x >= 0 && z >= 0 && x < Width && z < Depth; }
            public void Mark(int x, int z, byte flag) { if (Inside(x, z)) Cells[z * Width + x] |= flag; }
            public Vector2 Local(Vector3 world) { return new Vector2((world.x - Origin.x) / CellMetres, (world.z - Origin.z) / CellMetres); }
            public string WorldOf(float cellX, float cellZ) { return (Origin.x + cellX * CellMetres).ToString("F0") + ", " + (Origin.z + cellZ * CellMetres).ToString("F0"); }
        }

        sealed class Cluster
        {
            public int Size;
            public int MinX = int.MaxValue, MinZ = int.MaxValue, MaxX = -1, MaxZ = -1;
            public float CentreX { get { return (MinX + MaxX) * .5f + .5f; } }
            public float CentreZ { get { return (MinZ + MaxZ) * .5f + .5f; } }
            public void Add(int x, int z)
            {
                Size++;
                MinX = Mathf.Min(MinX, x); MaxX = Mathf.Max(MaxX, x);
                MinZ = Mathf.Min(MinZ, z); MaxZ = Mathf.Max(MaxZ, z);
            }
        }
    }
}
