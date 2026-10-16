using System.Collections.Generic;
using UnityEngine;

namespace SkyIsland
{
    /// <summary>
    /// Location power grid. Lights whose name starts with <see cref="lightPrefix"/> and halo objects whose name starts with
    /// <see cref="glowPrefix"/> are off while there is no power; the lamp materials are swapped (off -> on) on every renderer
    /// under the root while powered. Power is on when every switch is on: for each prefix in <see cref="switches"/> at least one
    /// SkyIslandInteractable with that name prefix exists and all of them are open (generator start lever, main switch).
    /// Gameplay code can also call SetPower(bool) directly (then set switches to an empty array) and listen to onPowerChanged.
    /// Nothing is changed in edit mode; play mode never modifies material assets.
    /// </summary>
    [AddComponentMenu("Sky Island/Power Grid")]
    public class SkyIslandPowerGrid : MonoBehaviour
    {
        [System.Serializable]
        public class MaterialPair
        {
            [Tooltip("Lamp material while there is no power")] public Material off;
            [Tooltip("Lamp material while powered")] public Material on;
        }

        [Tooltip("Root to search (defaults to this object)")] public Transform root;
        [Tooltip("Lights whose name starts with this are powered (Light_power_...)")] public string lightPrefix = "Light_power";
        [Tooltip("Halo objects whose name starts with this are powered (Glow_power_...)")] public string glowPrefix = "Glow_power";
        [Tooltip("Interactable name prefixes that must all be open for power (empty = SetPower only)")] public string[] switches = new string[0];
        public List<MaterialPair> materials = new List<MaterialPair>();
        [Tooltip("Power at start (before any switch changes)")] public bool startPowered;

        public bool Powered { get; private set; }
        public System.Action<bool> onPowerChanged;

        struct Slot { public Renderer r; public int index; public int pair; }

        readonly List<Light> _lights = new List<Light>();
        readonly List<GameObject> _glows = new List<GameObject>();
        readonly List<Slot> _slots = new List<Slot>();
        List<SkyIslandInteractable>[] _switches;

        // v2.7: the state is known before Start, so powered parts asked from another script's Awake / Start get the
        // start state (lights, halos and materials are still applied in Start)
        void Awake() { Powered = startPowered; }

        void Start()
        {
            var r0 = root != null ? root : transform;
            foreach (var l in r0.GetComponentsInChildren<Light>(true))
                if (!string.IsNullOrEmpty(lightPrefix) && l.name.StartsWith(lightPrefix)) _lights.Add(l);
            foreach (var t in r0.GetComponentsInChildren<Transform>(true))
                if (!string.IsNullOrEmpty(glowPrefix) && t.name.StartsWith(glowPrefix)) _glows.Add(t.gameObject);
            foreach (var r in r0.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    for (int p = 0; p < materials.Count; p++)
                        if (materials[p] != null && mats[i] != null && (mats[i] == materials[p].off || mats[i] == materials[p].on))
                        {
                            _slots.Add(new Slot { r = r, index = i, pair = p });
                            break;
                        }
            }
            _switches = new List<SkyIslandInteractable>[switches != null ? switches.Length : 0];
            var all = r0.GetComponentsInChildren<SkyIslandInteractable>(true);
            for (int s = 0; s < _switches.Length; s++)
            {
                _switches[s] = new List<SkyIslandInteractable>();
                foreach (var ia in all)
                    if (!string.IsNullOrEmpty(switches[s]) && ia.name.StartsWith(switches[s]))
                    {
                        _switches[s].Add(ia);
                        ia.onToggled += OnSwitch;
                    }
            }
            Apply(startPowered, true);
        }

        void OnDestroy()
        {
            if (_switches == null) return;
            foreach (var list in _switches)
                foreach (var ia in list)
                    if (ia != null) ia.onToggled -= OnSwitch;
        }

        void OnSwitch(SkyIslandInteractable ia, bool open)
        {
            if (_switches.Length == 0) return;
            bool on = true;
            foreach (var list in _switches)
            {
                if (list.Count == 0) { on = false; break; }
                foreach (var s in list)
                {
                    bool isOpen = s == ia ? open : s.IsOpen;
                    if (!isOpen) { on = false; break; }
                }
                if (!on) break;
            }
            SetPower(on);
        }

        /// <summary>Switch the grid on or off (lights, halos, lamp materials).</summary>
        public void SetPower(bool on) => Apply(on, false);

        void Apply(bool on, bool force)
        {
            if (!force && on == Powered) return;
            Powered = on;
            foreach (var l in _lights) if (l != null) l.enabled = on;
            foreach (var g in _glows) if (g != null) g.SetActive(on);
            foreach (var s in _slots)
            {
                if (s.r == null) continue;
                var mats = s.r.sharedMaterials;
                var pair = materials[s.pair];
                var want = on ? pair.on : pair.off;
                if (s.index < mats.Length && want != null && mats[s.index] != want)
                {
                    mats[s.index] = want;
                    s.r.sharedMaterials = mats;
                }
            }
            if (!force) onPowerChanged?.Invoke(on);
        }
    }
}
