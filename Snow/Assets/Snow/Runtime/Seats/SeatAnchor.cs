using Snow.Doors;
using UnityEngine;

namespace Snow.Seats
{
    /// <summary>
    /// One seat of a vehicle (§7.5): where the pelvis and the eye go, where the player stands after getting out, and
    /// which door is on that side. exitDoor = null means an open side with no door (P01 bench).
    /// On the plane: Socket_Seat_P/C, Socket_PilotEye/CopilotEye, Socket_Exit_L/R, PilotDoor_L/R.
    /// </summary>
    public class SeatAnchor : MonoBehaviour
    {
        [Tooltip("Interaction volume id, e.g. Seat_P")] public string seatId = "Seat_P";
        public int index;
        public SeatRole role;
        public Transform pelvis, eye, exit;
        public NetworkDoor exitDoor;

        Collider[] _eyeColliders;

        /// <summary>
        /// The vehicle's solid colliders that contain the eye point: the occupied seat itself (the model's convex seat
        /// hulls enclose the head: Socket_PilotEye is inside COL_Seat_P). For the occupant they are part of his own body,
        /// so his look and use rays ignore them — otherwise the eye sits on the hull and physics reports it on some frames
        /// and not on others (30.09: flicker of targets and of the Alt marks in the pilot seat).
        /// </summary>
        public Collider[] EyeColliders
        {
            get
            {
                if (_eyeColliders != null || eye == null) return _eyeColliders ?? System.Array.Empty<Collider>();
                var list = new System.Collections.Generic.List<Collider>();
                var root = GetComponentInParent<Unity.Netcode.NetworkObject>();
                Transform scope = root != null ? root.transform : transform.root;
                Vector3 p = eye.position;
                foreach (var c in scope.GetComponentsInChildren<Collider>(true))
                {
                    if (c.isTrigger || !(c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider m && m.convex))) continue;
                    if ((c.ClosestPoint(p) - p).sqrMagnitude < 0.02f * 0.02f) list.Add(c);   // inside, or within 2 cm of the eye
                }
                _eyeColliders = list.ToArray();
                return _eyeColliders;
            }
        }
    }
}
