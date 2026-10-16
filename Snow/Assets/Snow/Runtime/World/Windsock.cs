using UnityEngine;

namespace Snow.World
{
    /// <summary>
    /// Presentation only: points the sock downwind and fills it with the wind at its place (full at 15 kt). Every peer
    /// computes it from the replicated weather settings and the shared session time, so all players see the same.
    /// </summary>
    public class Windsock : MonoBehaviour
    {
        public Transform sock;
        [Tooltip("m/s for a fully extended sock")] public float fullSpeed = 15f * EnvironmentProvider.Kt;

        void Update()
        {
            if (sock == null) return;
            Vector3 w = EnvironmentProvider.WindAt(sock.position);
            Vector3 flat = new Vector3(w.x, 0f, w.z);
            float fill = Mathf.Clamp01(flat.magnitude / fullSpeed);
            Quaternion yaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : sock.rotation;
            Quaternion droop = Quaternion.Euler(Mathf.Lerp(80f, 5f, fill), 0f, 0f);
            sock.rotation = Quaternion.Slerp(sock.rotation, yaw * droop, 1f - Mathf.Exp(-4f * Time.deltaTime));
        }
    }
}
