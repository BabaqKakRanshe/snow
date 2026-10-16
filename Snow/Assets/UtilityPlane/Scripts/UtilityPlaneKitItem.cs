using UnityEngine;

namespace UtilityPlane
{
    /// <summary>
    /// Carry form of a winter-kit item or a refuelling tool (prefabs Prefabs/Kit/KitCarry_&lt;Key&gt;, built by
    /// Tools/Utility Plane/Install Winter Kit). Static data for your inventory and carry physics plus two runtime values
    /// (fuelLitres, setsLeft) that your code replicates. The root holds the mesh and a convex MeshCollider on the
    /// Default layer; the child IV_KitItem is the trigger on the Interact layer with UtilityPlaneInteractable
    /// (kind "kit_item"). No Rigidbody is added - your carry system owns physics (use TotalMassKg); add Rigidbody /
    /// NetworkObject in your own prefab variants. Grip = hand point (IK target); Pour = the fuel can spout tip.
    /// installs = the installed forms this carry form becomes on the aircraft.
    /// </summary>
    [DisallowMultipleComponent]
    public class UtilityPlaneKitItem : MonoBehaviour
    {
        public string key;                      // IntakePlug, ExhaustCover, ..., JerryCan, Funnel
        public float massKg;
        [Tooltip("how many of this carry form a full kit has (2 exhaust plugs, 2 wing-cover bags, 2 main chock pairs)")]
        public int count = 1;
        [Tooltip("tie-down bag: anchor + rope sets it holds when full")]
        public int sets = 1;
        [Tooltip("runtime: sets still in the bag (tie-down bag), replicate it")]
        public int setsLeft = 1;
        public KitItem[] installs = new KitItem[0];
        public Transform grip;
        public Transform pour;

        [Header("Fuel can")]
        public float capacityLitres;
        [Tooltip("runtime: litres in the can, replicate it")]
        public float fuelLitres;
        public float fuelDensity = 0.8f;        // Jet A-1, kg/l

        public float TotalMassKg => massKg + Mathf.Max(0f, fuelLitres) * fuelDensity;
    }
}
