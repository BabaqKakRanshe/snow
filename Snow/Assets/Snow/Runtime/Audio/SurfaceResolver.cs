using System.Collections.Generic;
using UnityEngine;

namespace Snow.Audio
{
    /// <summary>
    /// What is under the feet (RESEARCH_AUDIO §4.6): a Terrain gives its strongest layer at the point (layer name ->
    /// surface, library table); any other collider its SurfaceTag, else a keyword of its material / collider / object
    /// name; nothing matched: the indoor or outdoor default (a roof above or not).
    /// </summary>
    public static class SurfaceResolver
    {
        static readonly RaycastHit[] s_hits = new RaycastHit[8];
        static readonly Dictionary<int, string> s_byCollider = new Dictionary<int, string>();
        const string NoMatch = "";

        /// <summary>Surface under the feet; null when nothing is below (in the air, no library).</summary>
        public static string At(Vector3 feet, Transform self) => Resolve(feet, self, false, out _);

        /// <summary>The same with the reason, for the pult («слой TL_RoadGravel», «материал …», «под крышей»).</summary>
        public static string Explain(Vector3 feet, Transform self, out string why) => Resolve(feet, self, true, out why);

        /// <summary>A roof within 30 m above the point.</summary>
        public static bool Indoors(Vector3 p) =>
            Physics.Raycast(p + Vector3.up * 2f, Vector3.up, 30f, ~0, QueryTriggerInteraction.Ignore);

        static string Resolve(Vector3 feet, Transform self, bool explain, out string why)
        {
            why = null;
            SoundLibrary lib = SnowAudio.Library;
            if (lib == null) { why = "нет библиотеки звуков"; return null; }
            int n = Physics.RaycastNonAlloc(feet + Vector3.up * 0.3f, Vector3.down, s_hits, 1.2f, ~0, QueryTriggerInteraction.Ignore);
            int best = -1;
            for (int i = 0; i < n; i++)
            {
                if (self != null && s_hits[i].transform.IsChildOf(self)) continue;
                if (best < 0 || s_hits[i].distance < s_hits[best].distance) best = i;
            }
            if (best < 0) { why = "под ногами ничего"; return null; }
            RaycastHit hit = s_hits[best];
            Collider c = hit.collider;
            if (c is TerrainCollider tc)
            {
                string t = TerrainSurface(tc, hit.point, lib, out string layer);
                if (explain) why = "террейн, слой " + (layer ?? "?") + (t == null ? " (нет в таблице)" : "");
                if (t != null) return t;
            }
            else
            {
                string s = explain ? ColliderSurface(c, lib, out why) : Cached(c, lib);
                if (s.Length > 0) return s;
                if (explain) why = c.name + ": ни одного слова из таблицы";
            }
            bool roof = Indoors(feet);
            if (explain) why += roof ? ", под крышей" : ", под небом";
            return roof ? lib.indoorDefault : lib.outdoorDefault;
        }

        static string TerrainSurface(TerrainCollider tc, Vector3 point, SoundLibrary lib, out string layerName)
        {
            layerName = null;
            TerrainData td = tc.terrainData;
            if (td == null || td.alphamapLayers == 0) return null;
            Vector3 local = point - tc.transform.position;
            int x = Mathf.Clamp(Mathf.FloorToInt(local.x / td.size.x * td.alphamapWidth), 0, td.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(local.z / td.size.z * td.alphamapHeight), 0, td.alphamapHeight - 1);
            float[,,] a = td.GetAlphamaps(x, z, 1, 1);                     // [z, x, layer]
            int layer = 0;
            for (int l = 1; l < td.alphamapLayers; l++) if (a[0, 0, l] > a[0, 0, layer]) layer = l;
            TerrainLayer[] layers = td.terrainLayers;
            if (layer >= layers.Length || layers[layer] == null) return null;
            layerName = layers[layer].name;
            return lib.ForLayer(layerName);
        }

        static string Cached(Collider c, SoundLibrary lib)
        {
            int key = c.GetInstanceID();
            if (s_byCollider.TryGetValue(key, out string cached)) return cached;
            string s = ColliderSurface(c, lib, out _);
            s_byCollider[key] = s;
            return s;
        }

        static string ColliderSurface(Collider c, SoundLibrary lib, out string why)
        {
            why = null;
            SurfaceTag tag = c.GetComponentInParent<SurfaceTag>();
            if (tag != null && !string.IsNullOrEmpty(tag.surface)) { why = "метка SurfaceTag на " + tag.name; return tag.surface; }
            Renderer r = c.GetComponent<Renderer>();
            if (r == null) r = c.GetComponentInParent<Renderer>();
            string s;
            if (r != null && r.sharedMaterial != null && (s = lib.ForName(r.sharedMaterial.name)) != null) { why = "материал " + r.sharedMaterial.name; return s; }
            if (c.sharedMaterial != null && (s = lib.ForName(c.sharedMaterial.name)) != null) { why = "физ. материал " + c.sharedMaterial.name; return s; }
            if ((s = lib.ForName(c.name)) != null) { why = "имя " + c.name; return s; }
            if (c.transform.parent != null && (s = lib.ForName(c.transform.parent.name)) != null) { why = "имя " + c.transform.parent.name; return s; }
            return NoMatch;
        }
    }
}
