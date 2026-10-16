using UnityEngine;

namespace Snow.Cargo
{
    /// <summary>
    /// A tie-down point on a vehicle floor (SECURE-01; the plane's test sockets are Socket_TieDown_01..15, O-02 open).
    /// The socket transform is where the item's pivot (bottom centre) goes; its up is the floor normal, its forward the
    /// reference for the snapped yaw. Occupancy is server state.
    /// </summary>
    public class TieDownSocket : MonoBehaviour
    {
        public string socketId = "Socket_TieDown_01";
        [Tooltip("m: how far the item may be from the socket and still snap onto it")] public float captureRadius = 0.6f;
        [Tooltip("deg: how far the item's up may lean from the socket's up")] public float maxTilt = 25f;

        public CargoItem Occupant { get; set; }
        /// <summary>Server: why this ring cannot be used now (covered by the stowed ladder or the jerry can, И3 L.5), or null.</summary>
        public string Blocked { get; set; }
    }
}
