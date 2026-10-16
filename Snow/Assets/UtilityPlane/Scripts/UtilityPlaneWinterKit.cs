using System;
using System.Collections.Generic;
using UnityEngine;

namespace UtilityPlane
{
    /// <summary>Winter preflight kit items that can be on the parked aircraft (Data/winter_kit.json, same order).</summary>
    public enum KitItem
    {
        IntakePlug, ExhaustCover_L, ExhaustCover_R, PitotCover, WingCover_L, WingCover_R, WindshieldCover, EngineCover,
        Chocks_L, Chocks_R, Chocks_Tail, TieDown_L, TieDown_R, TieDown_Tail, Heater
    }

    /// <summary>
    /// Winter kit on the parked plane (prefab WinterKit, a child of the plane root at local identity). Shows / hides the
    /// installed forms Kit_&lt;Item&gt; and tells what they block. It only holds state: the server decides and every client
    /// calls SetInstalled, or ApplyMask with the replicated Mask (bit i = KitItem i). The interaction zones
    /// IV_Kit_&lt;Item&gt; (Interact layer, UtilityPlaneInteractable kind "kit", id = item name) stay active whether the item
    /// is on or off; switch them off with SetZonesEnabled(false) while the aircraft moves or flies.
    /// Effects are flags for your systems (engine start, airspeed, lift, taxi), see WINTER_KIT.md.
    /// </summary>
    [DisallowMultipleComponent]
    public class UtilityPlaneWinterKit : MonoBehaviour
    {
        [Serializable]
        public class Slot
        {
            public KitItem item;
            public GameObject visual;          // Kit_<Item>
            public bool installed;
            public string label;
            [Tooltip("suggested hold time to install / remove, s")]
            public float installSeconds;
            public bool groundItem;            // chocks, tie-downs, heater: stand on the ground
            [Tooltip("carry form this item becomes when removed (Prefabs/Kit/KitCarry_<Key>)")]
            public GameObject carryPrefab;
            [Tooltip("tie-downs: rope fitting on the aircraft; heater: duct end at the intake")]
            public Transform attach;
        }

        public Slot[] slots = new Slot[0];
        [Tooltip("trigger zones IV_Kit_<Item> (SetZonesEnabled)")]
        public Collider[] zones = new Collider[0];

        [Tooltip("When shown, the wheel chocks drop / rise onto the ground under them (raycast down, ignores the " +
                 "aircraft's own colliders and loose rigidbodies, at most maxSnap). The other ground items stay as " +
                 "modelled (ropes, duct). Call SnapChocks() again after the aircraft has settled.")]
        public bool snapChocksToGround = true;
        public LayerMask groundMask = Physics.DefaultRaycastLayers;
        public float maxSnap = 0.3f;
        [Tooltip("The aircraft root whose colliders the ground raycast ignores (empty: this object's parent)")]
        public Transform aircraftRoot;

        /// <summary>item, installed - raised by SetInstalled / ApplyMask when the state changes</summary>
        public event Action<KitItem, bool> Changed;

        public const int ItemCount = 15;
        static int B(KitItem i) => 1 << (int)i;
        public static readonly int EngineStartMask = B(KitItem.IntakePlug) | B(KitItem.ExhaustCover_L) |
                                                     B(KitItem.ExhaustCover_R) | B(KitItem.EngineCover) | B(KitItem.Heater);
        public static readonly int LiftMask = B(KitItem.WingCover_L) | B(KitItem.WingCover_R);
        public static readonly int ChocksMask = B(KitItem.Chocks_L) | B(KitItem.Chocks_R) | B(KitItem.Chocks_Tail);
        public static readonly int TieDownMask = B(KitItem.TieDown_L) | B(KitItem.TieDown_R) | B(KitItem.TieDown_Tail);
        public static readonly int MoveMask = ChocksMask | TieDownMask | B(KitItem.Heater);

        int _mask;
        bool _ready;
        readonly Dictionary<Transform, Vector3> _rest = new Dictionary<Transform, Vector3>();
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// <summary>bit i set = KitItem i installed (replicate this int)</summary>
        public int Mask { get { Init(); return _mask; } }

        void Awake() => Init();

        void Init()
        {
            if (_ready) return;
            _ready = true;
            if (aircraftRoot == null) aircraftRoot = transform.parent != null ? transform.parent : transform;
            _mask = 0;
            foreach (var s in slots)
            {
                if (s == null) continue;
                if (s.visual != null) _rest[s.visual.transform] = s.visual.transform.localPosition;
                if (s.installed) _mask |= B(s.item);
                Show(s);
            }
        }

        public bool IsInstalled(KitItem i) { Init(); return (_mask & B(i)) != 0; }

        /// <summary>The heater duct and the intake plug use the same intake: one excludes the other.</summary>
        public bool CanInstall(KitItem i)
        {
            if (i == KitItem.Heater) return !IsInstalled(KitItem.IntakePlug);
            if (i == KitItem.IntakePlug) return !IsInstalled(KitItem.Heater);
            return true;
        }

        /// <summary>True only when the state changed. False: it already was so, or the item is excluded (CanInstall).
        /// Spawn / consume carry forms only on true.</summary>
        public bool SetInstalled(KitItem i, bool on)
        {
            Init();
            if (on == IsInstalled(i)) return false;
            if (on && !CanInstall(i)) return false;
            Set(i, on);
            return true;
        }

        /// <summary>Apply a replicated state as is (no rule check).</summary>
        public void ApplyMask(int mask)
        {
            Init();
            for (int b = 0; b < ItemCount; b++)
            {
                bool on = (mask & (1 << b)) != 0;
                if (on != IsInstalled((KitItem)b)) Set((KitItem)b, on);
            }
        }

        public Slot GetSlot(KitItem i)
        {
            foreach (var s in slots)
                if (s != null && s.item == i) return s;
            return null;
        }

        /// <summary>Zones off while the aircraft moves or flies, and on the client of a seated player (the use-ray
        /// reaches them through the cabin). Zones are per client: do not switch them off for everybody.</summary>
        public void SetZonesEnabled(bool on)
        {
            foreach (var z in zones)
                if (z != null) z.enabled = on;
        }

        void Set(KitItem i, bool on)
        {
            if (on) _mask |= B(i); else _mask &= ~B(i);
            foreach (var s in slots)
            {
                if (s == null || s.item != i) continue;
                s.installed = on;
                Show(s);
            }
            Changed?.Invoke(i, on);
        }

        // ---------------------------------------------------------------- effects (flags for your systems)
        public bool EngineStartBlocked => (Mask & EngineStartMask) != 0;
        public bool AirspeedBlocked => IsInstalled(KitItem.PitotCover);
        public bool LiftBlocked => (Mask & LiftMask) != 0;
        public bool ViewBlocked => IsInstalled(KitItem.WindshieldCover);
        public bool EngineInsulated => IsInstalled(KitItem.EngineCover);
        public bool WheelsHeld => (Mask & ChocksMask) != 0;
        public bool TiedDown => (Mask & TieDownMask) != 0;
        public bool Preheating => IsInstalled(KitItem.Heater);
        /// <summary>chocks, tie-downs or the heater (its duct sits in the intake): the aircraft must not move</summary>
        public bool MoveBlocked => (Mask & MoveMask) != 0;
        /// <summary>nothing of the kit is on the aircraft (preflight walk-around done)</summary>
        public bool ReadyForFlight => Mask == 0;

        public List<KitItem> InstalledItems()
        {
            var r = new List<KitItem>();
            for (int b = 0; b < ItemCount; b++)
                if (IsInstalled((KitItem)b)) r.Add((KitItem)b);
            return r;
        }

        // ---------------------------------------------------------------- visuals
        void Show(Slot s)
        {
            if (s.visual == null) return;
            s.visual.SetActive(s.installed);
            if (s.installed && snapChocksToGround && IsChock(s.item)) SnapToGround(s.visual.transform);
        }

        static bool IsChock(KitItem i) => i == KitItem.Chocks_L || i == KitItem.Chocks_R || i == KitItem.Chocks_Tail;

        /// <summary>Re-snap the installed chocks, e.g. after spawn once the struts have settled.</summary>
        public void SnapChocks()
        {
            Init();
            if (!snapChocksToGround) return;
            foreach (var s in slots)
                if (s != null && s.installed && s.visual != null && IsChock(s.item)) SnapToGround(s.visual.transform);
        }

        // the chock pivot is its ground contact point in the modelled parked pose
        void SnapToGround(Transform t)
        {
            if (_rest.TryGetValue(t, out var rest)) t.localPosition = rest;
            Vector3 up = Vector3.up;
            Vector3 from = t.position + up * (maxSnap + 0.5f);
            int n = Physics.RaycastNonAlloc(from, -up, Hits, maxSnap * 2f + 0.5f, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 point = Vector3.zero;
            for (int k = 0; k < n; k++)
            {
                var c = Hits[k].collider;
                if (c == null || c.attachedRigidbody != null) continue;              // loose cans, bags, crates
                if (aircraftRoot != null && c.transform.IsChildOf(aircraftRoot)) continue;
                if (Hits[k].distance < best) { best = Hits[k].distance; point = Hits[k].point; }
            }
            if (best == float.MaxValue) return;
            float dy = Mathf.Clamp(point.y - t.position.y, -maxSnap, maxSnap);
            t.position += up * dy;
        }
    }
}
