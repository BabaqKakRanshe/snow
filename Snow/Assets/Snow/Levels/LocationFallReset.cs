using Snow.Net;
using Snow.Player;
using Unity.Netcode.Components;
using UnityEngine;

namespace Snow.Levels
{
    /// <summary>
    /// Levels on islands and cliffs (Sky Island, AboveClouds, the core's maps): a local player who walked off the edge and
    /// fell below <see cref="fallY"/> is put back on the nearest spawn point. Only the own player (the owner moves his
    /// character, NetworkTransform carries it to the others) and only on foot: the plane flies under the island and
    /// below the cloud edge with its crew in the seats or standing in the bay (then the player rides as the plane's child).
    /// Same path as leaving a seat: motor, NetworkTransform, view.
    /// </summary>
    public class LocationFallReset : MonoBehaviour
    {
        [Tooltip("World height under which a player on foot is reset (m)")] public float fallY = -60f;
        [Tooltip("Spawn points to return to (SnowLevel/SpawnPoints)")] public Transform spawnRoot;

        void Update()
        {
            PlayerNet p = PlayerNet.Local;
            if (p == null || !p.IsSpawned || p.Seated || (p.seatController != null && p.seatController.Seat != null)) return;
            if (p.transform.parent != null) return;                    // riding a vehicle (standing in the plane's bay, §7.6)
            Vector3 at = p.transform.position;
            if (at.y >= fallY || !Nearest(at, out Transform spawn)) return;

            float yaw = spawn.eulerAngles.y;
            p.view.SetYaw(yaw);                                        // first: the motor's next step takes the view's yaw (L4.2)
            p.motor.Teleport(spawn.position, yaw, Vector3.zero);
            if (p.TryGetComponent(out NetworkTransform nt) && nt.CanCommitToTransform)
                nt.Teleport(spawn.position, Quaternion.Euler(0f, yaw, 0f), p.transform.localScale);
            BenchLog.Event("fall_reset", "from", at, "to", spawn.position, "fallY", fallY);
        }

        bool Nearest(Vector3 from, out Transform best)
        {
            best = null;
            if (spawnRoot == null) return false;
            float bestD = float.MaxValue;
            foreach (Transform s in spawnRoot)
            {
                Vector3 d = s.position - from;
                d.y = 0f;                                              // the player is far below: nearest across the map
                if (d.sqrMagnitude >= bestD) continue;
                bestD = d.sqrMagnitude;
                best = s;
            }
            return best != null;
        }
    }
}
