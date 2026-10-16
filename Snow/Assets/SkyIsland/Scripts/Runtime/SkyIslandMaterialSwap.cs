using System.Collections.Generic;
using UnityEngine;

namespace SkyIsland
{
    /// <summary>
    /// Play-mode helper: replaces a shared material asset by a runtime copy on every renderer under a root, so a
    /// script can animate the copy without touching the asset, and puts the original back afterwards.
    /// </summary>
    internal sealed class SkyIslandMaterialSwap
    {
        struct Slot { public Renderer renderer; public int index; }

        readonly List<Slot> _slots = new List<Slot>();

        public Material Original { get; private set; }
        public Material Runtime { get; private set; }
        public int Count => _slots.Count;

        public static SkyIslandMaterialSwap Create(Transform root, Material shared)
        {
            var swap = new SkyIslandMaterialSwap { Original = shared, Runtime = new Material(shared) { name = shared.name + " (Runtime)" } };
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != shared) continue;
                    mats[i] = swap.Runtime;
                    swap._slots.Add(new Slot { renderer = r, index = i });
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
            return swap;
        }

        /// <summary>Puts the shared material back and destroys the runtime copy.</summary>
        public void Restore()
        {
            foreach (var s in _slots)
            {
                if (s.renderer == null) continue;
                var mats = s.renderer.sharedMaterials;
                if (s.index < mats.Length && mats[s.index] == Runtime)
                {
                    mats[s.index] = Original;
                    s.renderer.sharedMaterials = mats;
                }
            }
            _slots.Clear();
            if (Runtime != null)
            {
                if (Application.isPlaying) Object.Destroy(Runtime);
                else Object.DestroyImmediate(Runtime);
            }
            Runtime = null;
        }
    }
}
