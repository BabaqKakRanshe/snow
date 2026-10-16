using UnityEngine;

namespace SkyIsland
{
    /// <summary>Spins an object around an axis of its own local space (bucket wheels, fans, turbines, antennas).</summary>
    [AddComponentMenu("Sky Island/Rotator")]
    public class SkyIslandRotator : MonoBehaviour
    {
        [Tooltip("Rotation axis in this object's local space")] public Vector3 axis = Vector3.up;
        [Tooltip("Revolutions per minute (negative = reverse)")] public float rpm = 5f;
        [Tooltip("Start at a different angle per object (derived from its position, so every run looks the same)")]
        public bool randomStart = true;

        Vector3 Axis => axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up;

        void Start()
        {
            if (!randomStart) return;
            var p = transform.position;
            float h = Mathf.Sin(p.x * 12.9898f + p.y * 78.233f + p.z * 37.719f) * 43758.5453f;
            transform.localRotation *= Quaternion.AngleAxis((h - Mathf.Floor(h)) * 360f, Axis);
        }

        void Update()
        {
            transform.localRotation *= Quaternion.AngleAxis(rpm * 6f * Time.deltaTime, Axis);
        }
    }
}
