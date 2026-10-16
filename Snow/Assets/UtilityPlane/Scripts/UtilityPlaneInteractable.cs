using UnityEngine;

namespace UtilityPlane
{
    /// <summary>Marks one interaction volume of the plane (created by Tools/Utility Plane/Build Materials + Prefabs from
    /// Data/interaction_volumes.json). The id equals the controlled part / socket name, e.g. SW_Battery,
    /// Socket_Handle_CargoDoor_L_Out, Seat_P. Your interaction system raycasts the "Interact" layer, reads this
    /// component and sends a command to the server; this component holds no state.</summary>
    [DisallowMultipleComponent]
    public class UtilityPlaneInteractable : MonoBehaviour
    {
        public string id;
        [Tooltip("toggle, momentary, rotary, pull, selector, lever, wheel, hinge, door, cap, seat, lock (v7); " +
                 "ladder (v7, StepLadder prefab)")]
        public string kind;
        public string label;
    }
}
