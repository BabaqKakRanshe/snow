using UnityEngine;
using UnityEngine.Events;
#if SKYISLAND_SRP_CORE
using UnityEngine.Rendering;
#endif

namespace SkyIsland
{
    /// <summary>
    /// Fog event: when a collider with the configured tag enters the trigger collider on this object (or Trigger() is
    /// called) the fog smoothly thickens (fog density x densityMul) and can change colour over `duration` seconds.
    /// A SkyIslandFogByAltitude controller keeps working while the event is active: its output is scaled instead of
    /// being overwritten. oneShot: the change happens once and stays; otherwise the fog goes back to its previous state
    /// when the collider leaves (or Revert() is called). Optionally a Volume (SRP) is blended in with the fog.
    /// Trigger messages need a CharacterController or a Rigidbody on the player (standard Unity rule).
    /// </summary>
    [AddComponentMenu("Sky Island/Fog Event")]
    public class SkyIslandFogEvent : MonoBehaviour
    {
        [Tooltip("Fog density multiplier at full effect")] public float densityMul = 3f;
        [Tooltip("Also blend the fog colour")] public bool changeColor;
        [Tooltip("Fog colour at full effect (used when Change Color is on)")] public Color fogColor = Color.gray;
        [Tooltip("Blend time in seconds")] public float duration = 4f;
        [Tooltip("Happen once and stay; otherwise revert when the collider leaves")] public bool oneShot = true;
        [Tooltip("Tag of the collider (or of its Rigidbody) that triggers the event; empty = any collider")] public string triggerTag = "Player";
        [Tooltip("Altitude fog controller to scale (found automatically when empty)")] public SkyIslandFogByAltitude fogController;
#if SKYISLAND_SRP_CORE
        [Tooltip("Optional Volume (e.g. darker grading) whose weight is blended with the fog")] public Volume volume;
        [Tooltip("Volume weight at full effect")][Range(0f, 1f)] public float volumeWeight = 1f;
#endif
        [Tooltip("Invoked when the event starts")] public UnityEvent onTriggered = new UnityEvent();

        float _k;              // 0 = state before the event, 1 = full effect
        float _goal;           // 0 or 1
        bool _fired, _captured;
        float _baseDensity, _baseScale = 1f;
        float _baseStart, _baseEnd;
        Color _baseColor;
#if SKYISLAND_SRP_CORE
        float _baseWeight;
#endif

        /// <summary>True while the fog is (going) thick.</summary>
        public bool IsActive => _goal > 0.5f;
        /// <summary>0 = before the event, 1 = full effect.</summary>
        public float Progress => _k;

        /// <summary>Starts the event (ignored when already active, or when a one-shot event already happened).</summary>
        public void Trigger()
        {
            if (_goal > 0.5f || (oneShot && _fired)) return;
            if (!_captured || _k <= 0f) Capture();
            _fired = true;
            _goal = 1f;
            onTriggered?.Invoke();
        }

        /// <summary>Returns the fog to the state it had before the event.</summary>
        public void Revert()
        {
            if (!_captured) return;
            _goal = 0f;
        }

        void OnTriggerEnter(Collider other)
        {
            if (isActiveAndEnabled && Matches(other)) Trigger();
        }

        void OnTriggerExit(Collider other)
        {
            if (isActiveAndEnabled && !oneShot && Matches(other)) Revert();
        }

        bool Matches(Collider c)
        {
            if (string.IsNullOrEmpty(triggerTag)) return true;
            if (c.gameObject.tag == triggerTag) return true;   // string compare: no error for tags missing from the Tag Manager
            var rb = c.attachedRigidbody;
            return rb != null && rb.gameObject.tag == triggerTag;
        }

        void Capture()
        {
            if (fogController == null) fogController = FindController();
            _baseDensity = RenderSettings.fogDensity;
            _baseStart = RenderSettings.fogStartDistance; _baseEnd = RenderSettings.fogEndDistance;
            _baseScale = fogController != null ? fogController.densityScale : 1f;
            _baseColor = RenderSettings.fogColor;
#if SKYISLAND_SRP_CORE
            if (volume != null) _baseWeight = volume.weight;
#endif
            _captured = true;
        }

        void Update()
        {
            if (!_captured || Mathf.Approximately(_k, _goal)) return;
            _k = duration > 0f ? Mathf.MoveTowards(_k, _goal, Time.deltaTime / duration) : _goal;
            Apply(_k * _k * (3f - 2f * _k));
        }

        void Apply(float t)
        {
            float mul = Mathf.Lerp(1f, Mathf.Max(0f, densityMul), t);
            if (fogController != null && fogController.isActiveAndEnabled)
                fogController.densityScale = _baseScale * mul;    // the altitude controller applies it every frame
            else if (RenderSettings.fogMode == FogMode.Linear)
            {
                float k = 1f / Mathf.Max(0.05f, mul);                 // denser fog = it ends closer
                RenderSettings.fogStartDistance = _baseStart * k; RenderSettings.fogEndDistance = _baseEnd * k;
            }
            else
                RenderSettings.fogDensity = _baseDensity * mul;
            if (changeColor) RenderSettings.fogColor = Color.Lerp(_baseColor, fogColor, t);
#if SKYISLAND_SRP_CORE
            if (volume != null) volume.weight = Mathf.Lerp(_baseWeight, volumeWeight, t);
#endif
        }

        static SkyIslandFogByAltitude FindController()
        {
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<SkyIslandFogByAltitude>();
#else
            return FindObjectOfType<SkyIslandFogByAltitude>();
#endif
        }

        void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.55f, 0.6f, 0.75f, 0.12f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(0.55f, 0.6f, 0.75f, 0.7f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
