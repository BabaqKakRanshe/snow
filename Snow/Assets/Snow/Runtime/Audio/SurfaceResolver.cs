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
        public static string At(Vector3 feet, Transform self)
        {
            SoundLibrary lib = SnowAudio.Library;
            if (lib == null) return null;
            int n = Physics.RaycastNonAlloc(feet + Vector3.up * 0.3f, Vector3.down, s_hits, 1.2f, ~0, QueryTriggerInteraction.Ignore);
            int best = -1;
            for (int i = 0; i < n; i++)
            {
                if (self != null && s_hits[i].transform.IsChildOf(self)) continue;
                if (best < 0 || s_hits[i].distance < s_hits[best].distance) best = i;
            }
            if (best < 0) return null;
            RaycastHit hit = s_hits[best];
            Collider c = hit.collider;
            if (c is TerrainCollider tc)
            {
                string t = TerrainSurface(tc, hit.point, lib);
                if (t != null) return t;
            }
            else
            {
                string s = ColliderSurface(c, lib);
                if (s.Length > 0) return s;
            }
            return Indoors(feet) ? lib.indoorDefault : lib.outdoorDefault;
        }

        /// <summary>A roof within 30 m above the point.</summary>
        public static bool Indoors(Vector3 p) =>
            Physics.Raycast(p + Vector3.up * 2f, Vector3.up, 30f, ~0, QueryTriggerInteraction.Ignore);

        static string TerrainSurface(TerrainCollider tc, Vector3 point, SoundLibrary lib)
        {
            TerrainData td = tc.terrainData;
            if (td == null || td.alphamapLayers == 0) return null;
            Vector3 local = point - tc.transform.position;
            int x = Mathf.Clamp(Mathf.FloorToInt(local.x / td.size.x * td.alphamapWidth), 0, td.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(local.z / td.size.z * td.alphamapHeight), 0, td.alphamapHeight - 1);
            float[,,] a = td.GetAlphamaps(x, z, 1, 1);                     // [z, x, layer]
            int layer = 0;
            for (int l = 1; l < td.alphamapLayers; l++) if (a[0, 0, l] > a[0, 0, layer]) layer = l;
            TerrainLayer[] layers = td.terrainLayers;
            return layer < layers.Length && layers[layer] != null ? lib.ForLayer(layers[layer].name) : null;
        }

        static string ColliderSurface(Collider c, SoundLibrary lib)
        {
            int key = c.GetInstanceID();
            if (s_byCollider.TryGetValue(key, out string cached)) return cached;
            string s = null;
            SurfaceTag tag = c.GetComponentInParent<SurfaceTag>();
            if (tag != null && !string.IsNullOrEmpty(tag.surface)) s = tag.surface;
            if (s == null)
            {
                Renderer r = c.GetComponent<Renderer>();
                if (r == null) r = c.GetComponentInParent<Renderer>();
                if (r != null && r.sharedMaterial != null) s = lib.ForName(r.sharedMaterial.name);
            }
            if (s == null && c.sharedMaterial != null) s = lib.ForName(c.sharedMaterial.name);
            if (s == null) s = lib.ForName(c.name);
            if (s == null && c.transform.parent != null) s = lib.ForName(c.transform.parent.name);
            s ??= NoMatch;
            s_byCollider[key] = s;
            return s;
        }
    }
}
