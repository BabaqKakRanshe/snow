using UnityEngine;

namespace Snow.Cargo
{
    /// <summary>
    /// A place where one specific item lives on a vehicle (model v9 Socket_Stow_*, Data/stowage.json; CARGO-01,
    /// SECURE-01; И3 L.1). Unlike a tie-down it gives the full pose of the item's root (position and rotation), and it
    /// rides on its parent (the pilot doors move). The zone Stow_&lt;id&gt; puts a fitting item here; taking it back is a
    /// grab of the item itself. Occupancy is server state (CargoItem.State = Stowed shows it on every peer).
    /// </summary>
    public class StowPoint : MonoBehaviour
    {
        [Tooltip("Data/stowage.json id, e.g. Flashlight; the zone is Stow_<id>")] public string stowId;
        [Tooltip("CargoItem.itemKey this place accepts, e.g. Surv_Flashlight")] public string accepts;

        /// <summary>Server: the item stowed here, or null.</summary>
        public CargoItem Occupant { get; set; }
        public string ZoneId => "Stow_" + stowId;
    }
}
