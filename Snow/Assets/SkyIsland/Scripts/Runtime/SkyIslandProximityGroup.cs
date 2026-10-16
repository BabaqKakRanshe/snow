using UnityEngine;

namespace SkyIsland
{
    /// <summary>
    /// Enables its children (interiors of one building: stair cores, open flats, their lights and openable parts) only
    /// while the target (the main camera by default) is inside the group's bounds grown by <see cref="margin"/>.
    /// Children start disabled in Play mode; in the editor everything stays visible.
    /// </summary>
    [AddComponentMenu("Sky Island/Proximity Group")]
    public class SkyIslandProximityGroup : MonoBehaviour
    {
        [Tooltip("World-space bounds of the building")] public Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 10f);
        [Tooltip("Activation distance around the bounds (m)")] public float margin = 22f;
        [Tooltip("Defaults to Camera.main")] public Transform target;
        [Tooltip("Seconds between checks")] public float interval = 0.25f;

        bool _active = true;
        float _next;

        void Start()
        {
            SetChildren(false);
            _next = Time.time + Random.value * interval;
        }

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + interval;
            var t = target != null ? target : (Camera.main != null ? Camera.main.transform : null);
            if (t == null) return;
            var b = bounds;
            b.Expand(margin * 2f);
            bool want = b.Contains(t.position);
            if (want != _active) SetChildren(want);
        }

        public void SetChildren(bool on)
        {
            _active = on;
            foreach (Transform c in transform) c.gameObject.SetActive(on);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.15f);
            Gizmos.DrawWireCube(bounds.center, bounds.size + Vector3.one * margin * 2f);
        }
    }
}
