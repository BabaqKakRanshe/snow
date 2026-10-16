using UnityEngine;

namespace SkyIsland
{
    /// <summary>Rotates the radar antenna head around its local up axis.</summary>
    [AddComponentMenu("Sky Island/Radar Rotator")]
    public class SkyIslandRadarRotator : MonoBehaviour
    {
        [Tooltip("Revolutions per minute")] public float rpm = 5f;
        public bool randomStartAngle = true;

        void Start()
        {
            if (randomStartAngle)
                transform.localRotation *= Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);
        }

        void Update()
        {
            transform.localRotation *= Quaternion.AngleAxis(rpm * 6f * Time.deltaTime, Vector3.up);
        }
    }
}
