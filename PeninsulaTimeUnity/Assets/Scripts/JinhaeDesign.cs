using UnityEngine;

namespace PeninsulaTime
{
    /// <summary>Shared visual language derived from the carved Jinhae district and its 1926 station.</summary>
    public static class JinhaeDesign
    {
        public static readonly Color Cream = new Color(.93f, .89f, .78f);
        public static readonly Color Granite = new Color(.62f, .60f, .57f);
        public static readonly Color Timber = new Color(.45f, .32f, .22f);
        public static readonly Color Slate = new Color(.27f, .30f, .35f);
        public static readonly Color Tile = new Color(.76f, .72f, .65f);
        public static readonly Color Pane = new Color(.33f, .43f, .52f);
        public static readonly Color Sill = new Color(.95f, .95f, .93f);
        public static readonly Color Concrete = new Color(.70f, .69f, .66f);
        public static readonly Color Tactile = new Color(.95f, .78f, .15f);
        public static readonly Color Shingle = new Color(.29f, .27f, .28f);
        public static readonly Color Aluminium = new Color(.78f, .80f, .82f);
        public static readonly Color Water = new Color(.12f, .25f, .30f);
        public static readonly Color Foliage = new Color(.29f, .42f, .25f);
        public static readonly Color Ink = new Color(.045f, .071f, .077f);
        public static readonly Color Card = new Color(.085f, .12f, .127f);

        static bool Has(string value, params string[] words)
        {
            value = (value ?? string.Empty).ToLowerInvariant();
            foreach (var word in words) if (value.Contains(word)) return true;
            return false;
        }

        /// <summary>Maps architectural surfaces to Jinhae's palette while preserving operational colours.</summary>
        public static Color Harmonize(string key, Color source)
        {
            string k = (key ?? string.Empty).ToLowerInvariant();
            // Route identities, signals, warnings and emissive lights carry gameplay information.
            if (Has(k, "line", "route", "signal", "traffic", "warning", "tactile", "guide", "glow", "light", "marker", "amber", "map-", "network", "transit", "portal", "authored-red", "station-red")) return source;
            if (Has(k, "glass", "window", "pane", "vglass")) return Blend(source, Pane, .58f);
            if (Has(k, "wood", "timber", "bench", "ticket", "brown")) return Blend(source, Timber, .58f);
            if (Has(k, "metal", "steel", "aluminium", "aluminum", "chrome", "rail")) return Blend(source, Aluminium, .48f);
            if (Has(k, "roof", "shingle", "slate", "asphalt", "road", "dark")) return Blend(source, Slate, .52f);
            if (Has(k, "concrete", "platform", "paving", "pavement", "curb", "stone", "granite", "floor", "bridge", "tunnel")) return Blend(source, Concrete, .50f);
            if (Has(k, "water", "sea", "river")) return Blend(source, Water, .55f);
            if (Has(k, "grass", "tree", "leaf", "leaves", "forest", "terrain", "land")) return Blend(source, Foliage, .32f);
            if (Has(k, "wall", "facade", "station", "terminal", "hall", "white", "beige", "cream", "building")) return Blend(source, Cream, .42f);
            return Blend(source, Tile, .14f);
        }

        static Color Blend(Color source, Color target, float amount)
        {
            float alpha = source.a;
            var result = Color.Lerp(source, target, amount);
            result.a = alpha;
            return result;
        }
    }
}
