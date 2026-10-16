using UnityEngine;

namespace SkyIsland
{
    /// <summary>
    /// Openable part: door leaf, cabinet door, drawer, lid, roller shutter. The object's pivot is the hinge / rest position.
    /// Hinge: rotates about <see cref="axis"/> (parent space, through the pivot) by an angle in [min, max] degrees.
    /// Slide: moves along <see cref="axis"/> (parent space) by [min, max] metres.
    /// Scale: scales along <see cref="axis"/> (local, about the pivot) by a factor in [min, max] (a roller-shutter curtain
    /// pivoted at its head: 1 = down, 0.03 = rolled up). openAmount 0 = closed (min), 1 = open (max).
    /// Call Toggle() / Open() / Close() / SetTarget(t) from gameplay code (SkyIslandInteractor does it for the fly camera).
    /// v2.7: <see cref="linked"/> parts follow this one (a motor switch or strap winder driving shutter curtains);
    /// <see cref="driven"/> parts refuse direct toggles; <see cref="needsPower"/> parts need the grid (a powered part that
    /// drives nothing - a shutter motor - waits while the grid is down and goes on when it returns).
    /// </summary>
    [AddComponentMenu("Sky Island/Interactable")]
    public class SkyIslandInteractable : MonoBehaviour
    {
        public enum Kind { Hinge, Slide, Scale }
        public Kind kind = Kind.Hinge;
        [Tooltip("Hinge axis / slide direction in the parent's space; scale axis in the part's own space")] public Vector3 axis = Vector3.up;
        [Tooltip("Closed value (degrees for hinges, metres for slides, factor for scale)")] public float min = 0f;
        [Tooltip("Fully open value")] public float max = 90f;
        [Tooltip("Locked parts refuse to open (onLocked is raised)")] public bool locked;
        [Tooltip("Open fraction per second")] public float speed = 1.4f;
        [Range(0f, 1f)] public float openAmount;
        [Range(0f, 1f)] public float target;
        [Tooltip("Scale: the part's full-size scale (zero = its scale at start). The builder stores it when it shows a part open")]
        public Vector3 restScale;
        [Tooltip("Parts that follow this part's target (a switch or strap winder driving roller-shutter curtains)")]
        public SkyIslandInteractable[] linked = new SkyIslandInteractable[0];
        [Tooltip("Moved only by the parts that link to it: Toggle() (the interactor) raises onLocked instead")] public bool driven;
        [Tooltip("Needs grid power: refuses commands without it (onLocked); a powered part that drives nothing waits while the grid is down")]
        public bool needsPower;
        [Tooltip("Grid for needsPower (empty: the SkyIslandPowerGrid in the parents; none found = always powered)")]
        public SkyIslandPowerGrid grid;

        public System.Action<SkyIslandInteractable> onLocked;
        public System.Action<SkyIslandInteractable, bool> onToggled;

        Quaternion _baseRot;
        Vector3 _basePos;
        Vector3 _baseScale;
        bool _init, _driving;

        void Init()
        {
            if (_init) return;
            _baseRot = transform.localRotation;
            _basePos = transform.localPosition;
            _baseScale = restScale.sqrMagnitude > 1e-8f ? restScale : transform.localScale;
            _init = true;
        }

        void Awake()
        {
            Init();
            if (kind == Kind.Scale || !Mathf.Approximately(openAmount, 0f)) Apply();
            enabled = !Mathf.Approximately(openAmount, target);
        }

        public bool IsOpen => target > 0.5f;

        /// <summary>True unless the part needs power and its grid is down.</summary>
        public bool HasPower
        {
            get
            {
                if (!needsPower) return true;
                if (grid == null) grid = GetComponentInParent<SkyIslandPowerGrid>(true);   // only a found grid is kept
                return grid == null || grid.Powered;
            }
        }

        public void Toggle()
        {
            if (driven) { onLocked?.Invoke(this); return; }
            if (target > 0.5f) Close();
            else Open();
        }

        public void Open()
        {
            if (locked || !HasPower) { onLocked?.Invoke(this); return; }
            target = 1f; enabled = true; onToggled?.Invoke(this, true);
            Drive();
        }

        public void Close()
        {
            if (!HasPower) { onLocked?.Invoke(this); return; }
            target = 0f; enabled = true; onToggled?.Invoke(this, false);
            Drive();
        }

        public void SetTarget(float t)
        {
            t = Mathf.Clamp01(t);
            if ((locked && t > openAmount) || !HasPower) { onLocked?.Invoke(this); return; }
            target = t; enabled = true;
            Drive();
        }

        /// <summary>Target given by a driving part (switch, winder): the part's own lock and power rules apply;
        /// onToggled is raised when its open state flips.</summary>
        public void Follow(float t)
        {
            t = Mathf.Clamp01(t);
            if ((locked && t > openAmount) || !HasPower) { onLocked?.Invoke(this); return; }
            bool was = target > 0.5f;
            target = t; enabled = true;
            if ((t > 0.5f) != was) onToggled?.Invoke(this, t > 0.5f);
            Drive();
        }

        void Drive()
        {
            if (linked == null || linked.Length == 0 || _driving) return;
            _driving = true;
            float t = target;                                   // every follower gets the same command
            try
            {
                for (int i = 0; i < linked.Length; i++)
                    if (linked[i] != null && linked[i] != this) linked[i].Follow(t);
            }
            finally { _driving = false; }                       // a throwing listener must not lock the driver for good
        }

        void Update()
        {
            Init();
            // a motor (powered, drives nothing) waits where it is while the grid is down and goes on when it returns;
            // a switch's own rocker always finishes its flip
            if (needsPower && (linked == null || linked.Length == 0) && !HasPower) return;
            openAmount = Mathf.MoveTowards(openAmount, target, speed * Time.deltaTime);
            Apply();
            if (Mathf.Approximately(openAmount, target)) enabled = false;
        }

        public void Apply()
        {
            Init();
            float v = Mathf.Lerp(min, max, openAmount);
            Vector3 a = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up;
            if (kind == Kind.Hinge) transform.localRotation = Quaternion.AngleAxis(v, a) * _baseRot;
            else if (kind == Kind.Slide) transform.localPosition = _basePos + a * v;
            else
            {
                float ax = Mathf.Abs(a.x), ay = Mathf.Abs(a.y), az = Mathf.Abs(a.z);
                v = Mathf.Max(v, 1e-3f);                        // never a zero / negative scale (MeshCollider)
                transform.localScale = new Vector3(_baseScale.x * (1f + (v - 1f) * ax), _baseScale.y * (1f + (v - 1f) * ay),
                                                   _baseScale.z * (1f + (v - 1f) * az));
            }
        }
    }
}
