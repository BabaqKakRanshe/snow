using System;
using UnityEngine;

namespace Snow.World
{
    /// <summary>
    /// The root of a snow_kit prefab (SNOW_KIT_CONTRACT.md; built by Tools / Snow / Kit / Build Kit). Marks a kit instance
    /// (the map steps replace their instances by it) and carries the item's "on / off": the lens and screen materials of
    /// its power parts (<see cref="swaps"/>), its real lights and halos, and the beacon's turning dome. Presentation only:
    /// who has power is decided by the building's script, which calls <see cref="SetPower"/> on every peer.
    /// </summary>
    [DisallowMultipleComponent]
    public class KitItem : MonoBehaviour
    {
        [Serializable]
        public class Swap
        {
            public Renderer renderer;
            public int slot;
            public Material on, off;
        }

        [Tooltip("Kit model name (Kit_*)")] public string item;
        [Tooltip("Power parts: material slots lit with the power (prefab starts with the off material)")] public Swap[] swaps = new Swap[0];
        [Tooltip("Real lights of the item (off in the prefab)")] public Light[] lights = new Light[0];
        [Tooltip("Halo cards shown with the power")] public GameObject[] glows = new GameObject[0];
        [Tooltip("Beacon: the dome that turns with its light while spinning")] public Transform spin;
        [Tooltip("Local axis of the turn")] public Vector3 spinAxis = Vector3.up;
        public float rpm;

        /// <summary>Lit (as last set).</summary>
        public bool Powered { get; private set; }
        /// <summary>Beacon: turns while powered and spinning.</summary>
        public bool spinning;

        bool _applied;

        public void SetPower(bool on)
        {
            if (_applied && on == Powered) return;
            _applied = true;
            Powered = on;
            foreach (Swap s in swaps)
            {
                if (s?.renderer == null) continue;
                Material[] ms = s.renderer.sharedMaterials;
                if (s.slot < 0 || s.slot >= ms.Length) continue;
                Material m = on ? s.on : s.off;
                if (m == null || ms[s.slot] == m) continue;
                ms[s.slot] = m;
                s.renderer.sharedMaterials = ms;
            }
            foreach (Light l in lights) if (l != null) l.enabled = on;
            foreach (GameObject g in glows) if (g != null) g.SetActive(on);
        }

        /// <summary>A lit material of the swaps replaced (a monitor's screen by its camera picture). Re-applies the state.</summary>
        public void ReplaceOn(string materialPrefix, Material replacement)
        {
            foreach (Swap s in swaps)
                if (s?.on != null && s.on.name.StartsWith(materialPrefix, StringComparison.Ordinal)) s.on = replacement;
            _applied = false;
            SetPower(Powered);
        }

        void Update()
        {
            if (spin == null || rpm == 0f || !spinning || !Powered) return;
            spin.localRotation = Quaternion.AngleAxis(rpm * 6f * Time.deltaTime, spinAxis) * spin.localRotation;
        }
    }
}
