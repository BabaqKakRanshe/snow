using System.Collections.Generic;
using UnityEngine;

namespace SkyIsland
{
    /// <summary>
    /// Scrolling textures (water, conveyor belts, smoke stains): each entry swaps a shared material for a runtime copy
    /// on the renderers under the root and moves its texture offsets every frame.
    /// speed: _BaseMap / _MainTex / _BaseColorMap. normalSpeed: _BumpMap / _DetailNormalMap / _DetailAlbedoMap / _NormalMap / _DetailMap.
    /// Note: URP Lit and the Standard shader sample the main normal map with the base map's offset, so there normalSpeed
    /// moves the detail layer (the detail albedo and normal share one offset). Assets are never modified.
    /// </summary>
    [AddComponentMenu("Sky Island/UV Scroll")]
    public class SkyIslandUVScroll : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            [Tooltip("Shared material asset (renderers get a runtime copy in play mode)")] public Material material;
            [Tooltip("Base map scroll in UV per second")] public Vector2 speed;
            [Tooltip("Normal / detail map scroll in UV per second")] public Vector2 normalSpeed;
        }

        [Tooltip("Root to search for renderers (defaults to this object)")] public Transform root;
        public List<Entry> entries = new List<Entry>();

        static readonly int[] MainIds =
        {
            Shader.PropertyToID("_BaseMap"), Shader.PropertyToID("_MainTex"), Shader.PropertyToID("_BaseColorMap"),
        };
        static readonly int[] NormalIds =
        {
            Shader.PropertyToID("_BumpMap"), Shader.PropertyToID("_DetailNormalMap"), Shader.PropertyToID("_DetailAlbedoMap"),
            Shader.PropertyToID("_NormalMap"), Shader.PropertyToID("_DetailMap"),
        };

        sealed class Live
        {
            public Entry entry;
            public SkyIslandMaterialSwap swap;
            public readonly List<int> mainIds = new List<int>(), normalIds = new List<int>();
            public readonly List<Vector2> mainBase = new List<Vector2>(), normalBase = new List<Vector2>();
        }

        readonly List<Live> _live = new List<Live>();

        void Start()
        {
            var r0 = root != null ? root : transform;
            foreach (var e in entries)
            {
                if (e == null || e.material == null) continue;
                var l = new Live { entry = e, swap = SkyIslandMaterialSwap.Create(r0, e.material) };
                var m = l.swap.Runtime;
                foreach (var id in MainIds)
                    if (m.HasProperty(id)) { l.mainIds.Add(id); l.mainBase.Add(m.GetTextureOffset(id)); }
                foreach (var id in NormalIds)
                    if (m.HasProperty(id)) { l.normalIds.Add(id); l.normalBase.Add(m.GetTextureOffset(id)); }
                _live.Add(l);
            }
        }

        void Update()
        {
            float t = Time.time;
            foreach (var l in _live)
            {
                var m = l.swap.Runtime;
                if (m == null) continue;
                Apply(m, l.mainIds, l.mainBase, l.entry.speed * t);
                Apply(m, l.normalIds, l.normalBase, l.entry.normalSpeed * t);
            }
        }

        static void Apply(Material m, List<int> ids, List<Vector2> baseOffsets, Vector2 delta)
        {
            if (delta == Vector2.zero) return;
            for (int i = 0; i < ids.Count; i++)
            {
                var o = baseOffsets[i] + delta;
                m.SetTextureOffset(ids[i], new Vector2(Mathf.Repeat(o.x, 1f), Mathf.Repeat(o.y, 1f)));   // keeps float precision
            }
        }

        void OnDestroy()
        {
            foreach (var l in _live) l.swap.Restore();
            _live.Clear();
        }
    }
}
