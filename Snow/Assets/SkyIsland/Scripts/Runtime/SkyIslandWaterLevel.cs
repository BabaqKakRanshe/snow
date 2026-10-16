using UnityEngine;
using UnityEngine.Events;

namespace SkyIsland
{
    /// <summary>
    /// Reservoir level: moves the water surfaces between minY and maxY with an eased motion (at most maxSpeed m/s).
    /// OpenSpillways() drains to minY, CloseSpillways() refills to maxY, SetLevel / SetLevel01 go to any level,
    /// Hold() stops where the water is. Flow objects (spillway jets, spray emitters) are active only while draining.
    /// Each surface keeps its own offset to the water plane (the centre of its renderers' bounds at start).
    /// </summary>
    [AddComponentMenu("Sky Island/Water Level")]
    public class SkyIslandWaterLevel : MonoBehaviour
    {
        [Tooltip("Water surface objects (moved vertically)")] public Transform[] surfaces = new Transform[0];
        [Tooltip("Lowest water level (world Y)")] public float minY = 0f;
        [Tooltip("Highest water level (world Y)")] public float maxY = 10f;
        [Tooltip("Water level when the scene starts (world Y)")] public float startLevel = 10f;
        [Tooltip("Maximum speed of the level change (m/s)")] public float maxSpeed = 0.5f;
        [Tooltip("Easing time of the level change (s)")] public float smoothTime = 4f;
        [Tooltip("Objects active only while the level goes down (spillway jets, spray emitters)")] public GameObject[] flowObjects = new GameObject[0];

        public UnityEvent onDrainStarted = new UnityEvent();
        public UnityEvent onDrainFinished = new UnityEvent();
        public UnityEvent onFillStarted = new UnityEvent();
        public UnityEvent onFillFinished = new UnityEvent();

        const float Epsilon = 0.005f;

        float _level, _target, _velocity;
        float[] _offsets = new float[0];
        int _motion;          // -1 draining, 0 still, +1 filling
        bool _ready;

        /// <summary>Current water level (world Y).</summary>
        public float Level => _level;
        /// <summary>Level the water is moving to (world Y); setting it starts the motion (clamped to minY..maxY).</summary>
        public float TargetLevel { get => _target; set => SetLevel(value); }
        /// <summary>Current level as 0 (minY) .. 1 (maxY).</summary>
        public float Level01 => Mathf.InverseLerp(minY, maxY, _level);
        public bool IsDraining => _motion < 0;
        public bool IsFilling => _motion > 0;

        void Awake()
        {
            _offsets = new float[surfaces.Length];
            for (int i = 0; i < surfaces.Length; i++)
                if (surfaces[i] != null) _offsets[i] = surfaces[i].position.y - PlaneY(surfaces[i]);
            _level = _target = Clamp(startLevel);
            _velocity = 0f;
            _ready = true;
            ApplyLevel();
            SetFlow(false);
        }

        /// <summary>Moves the water to a level (world Y, clamped to minY..maxY).</summary>
        public void SetLevel(float y)
        {
            _target = Clamp(y);
        }

        /// <summary>Moves the water to a fraction of the range: 0 = minY, 1 = maxY.</summary>
        public void SetLevel01(float t)
        {
            SetLevel(Mathf.Lerp(minY, maxY, Mathf.Clamp01(t)));
        }

        /// <summary>Drains the reservoir down to minY.</summary>
        public void OpenSpillways()
        {
            SetLevel(minY);
        }

        /// <summary>Refills the reservoir up to maxY.</summary>
        public void CloseSpillways()
        {
            SetLevel(maxY);
        }

        /// <summary>Stops the water where it is.</summary>
        public void Hold()
        {
            _target = _level;
            _velocity = 0f;
        }

        void Update()
        {
            if (!_ready) return;
            int motion = 0;
            if (Mathf.Abs(_target - _level) > Epsilon)
            {
                motion = _target < _level ? -1 : 1;
                _level = Mathf.SmoothDamp(_level, _target, ref _velocity, Mathf.Max(0.01f, smoothTime), Mathf.Max(0.001f, maxSpeed));
                if (Mathf.Abs(_target - _level) <= Epsilon) { _level = _target; _velocity = 0f; motion = 0; }
                ApplyLevel();
            }
            if (motion != _motion)
            {
                int previous = _motion;
                _motion = motion;
                if (previous < 0) onDrainFinished?.Invoke();
                if (previous > 0) onFillFinished?.Invoke();
                if (motion < 0) onDrainStarted?.Invoke();
                if (motion > 0) onFillStarted?.Invoke();
                SetFlow(motion < 0);
            }
        }

        float Clamp(float y) => Mathf.Clamp(y, Mathf.Min(minY, maxY), Mathf.Max(minY, maxY));

        void ApplyLevel()
        {
            for (int i = 0; i < surfaces.Length; i++)
            {
                var s = surfaces[i];
                if (s == null) continue;
                var p = s.position;
                p.y = _level + (i < _offsets.Length ? _offsets[i] : 0f);
                s.position = p;
            }
        }

        void SetFlow(bool on)
        {
            foreach (var go in flowObjects)
                if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        /// <summary>World Y of a surface's water plane: centre of its renderers' bounds (its pivot if it has none).</summary>
        public static float PlaneY(Transform surface)
        {
            var renderers = surface.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return surface.position.y;
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b.center.y;
        }

        void OnDrawGizmosSelected()
        {
            foreach (var s in surfaces)
            {
                if (s == null) continue;
                var p = s.position;
                Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.8f);
                Gizmos.DrawWireCube(new Vector3(p.x, maxY, p.z), new Vector3(40f, 0f, 40f));
                Gizmos.color = new Color(0.6f, 0.4f, 0.2f, 0.8f);
                Gizmos.DrawWireCube(new Vector3(p.x, minY, p.z), new Vector3(40f, 0f, 40f));
            }
        }
    }
}
