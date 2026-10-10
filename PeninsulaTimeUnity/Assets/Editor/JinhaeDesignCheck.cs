using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    public static class JinhaeDesignCheck
    {
        [MenuItem("Peninsula/Checks/Jinhae design system")]
        public static void Run()
        {
            CheckNear("station wall", JinhaeDesign.Harmonize("station-wall", Color.white), JinhaeDesign.Cream, .28f);
            CheckNear("terminal glass", JinhaeDesign.Harmonize("terminal-glass", Color.white), JinhaeDesign.Pane, .43f);
            CheckNear("platform concrete", JinhaeDesign.Harmonize("platform-concrete", Color.white), JinhaeDesign.Concrete, .35f);
            CheckNear("wood bench", JinhaeDesign.Harmonize("wood-bench", Color.white), JinhaeDesign.Timber, .50f);
            CheckNear("roof", JinhaeDesign.Harmonize("station-roof", Color.white), JinhaeDesign.Shingle, .63f);

            var line = new Color(.12f, .7f, .92f, 1f);
            if (JinhaeDesign.Harmonize("metro-line-4", line) != line) throw new System.Exception("Transit line identity colour was changed");
            var signal = Color.red;
            if (JinhaeDesign.Harmonize("traffic-signal", signal) != signal) throw new System.Exception("Signal safety colour was changed");
            Debug.Log("JINHAE DESIGN CHECK PASS: architecture, glass, concrete, timber, roof and functional colours");
        }

        static void CheckNear(string label, Color actual, Color target, float maxDistance)
        {
            var d = new Vector3(actual.r-target.r, actual.g-target.g, actual.b-target.b).magnitude;
            if (d > maxDistance) throw new System.Exception(label+" is outside Jinhae palette: "+d);
        }
    }
}
