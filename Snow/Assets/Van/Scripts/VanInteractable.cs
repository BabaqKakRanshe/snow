using UnityEngine;

namespace UazVan
{
    /// <summary>
    /// Interaction zone of the van (Data/zones.json): a trigger BoxCollider on the Interact layer, IV_&lt;id&gt;. Stateless:
    /// the van's InteractionHost decides what a use does (ServerUse by id). kind - how the client drives it (the game's
    /// toggle, momentary, rotary, selector, pull, lever, hinge, door, cap, lock, seat, ladder, stow + the van's slot,
    /// hide, item); side - in / out / both (the _In / _Out rule against the interior box of model_facts.json).
    /// Zones of parts the rig can hide (taken items, unscrewed caps, plugs, keys, wheels, nuts) sit on the part's parent,
    /// so they stay to put the part back.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VanInteractable : MonoBehaviour
    {
        public string id;
        public string kind;
        public string side = "both";
        public string label;
        [Tooltip("The part this zone moves (parts.json), empty for socket / pane zones")] public string part;
        [Tooltip("The socket this zone is built on (sockets.json), empty for part zones")] public string socket;
        [Tooltip("Stow / cap zones: the item type that belongs here (items.json)")] public string itemType;
        [Tooltip("Glass zones (Glass_<pane>): the pane of glass.json for SetFrost / SetFog / AddHandprint (the windscreen is lob)")]
        public string pane;
        [Tooltip("At rest this zone lies inside that bigger zone: the ray enters the outer one first (rule: the inner zone wins)")]
        public string insideOf;
        [Tooltip("A lid / flap / cover (a part) that hides this zone while it is closed (its value below 50 % of its range): " +
                 "not usable then. The cover may have a cover of its own (battery box lid under seat L4: folded = open).")]
        public string coveredBy;
    }
}
