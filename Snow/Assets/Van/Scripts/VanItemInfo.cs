using UnityEngine;

namespace UazVan
{
    /// <summary>
    /// What an item of the van kit is (Data/items.json): on Prefabs/Items/VanItem_&lt;type&gt;. The model, a BoxCollider and
    /// (torch, flares, hand lamp) its light; no Rigidbody, no network: your NetItem recipe makes the game variant
    /// (CargoItem.itemKey = type). Mass: empty massKg, + capacityL x densityKgL of contents when full.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VanItemInfo : MonoBehaviour
    {
        public string type;
        public string label;
        public float massKg;
        [Tooltip("1 = one hand, 2 = two hands (wheel, battery, chain bag)")] public int hands = 1;
        public float capacityL;
        public string contents;
        public float densityKgL;
        [Tooltip("cord / cable: the item stays tied to the van")] public string tether;
        [Tooltip("The socket the cord / cable leaves the van from")] public string tetherFrom;
        public float tetherM;
        [Tooltip("readable: id of the text (map, logbook)")] public string readable;
        public Light lamp;
        public Renderer lens;
    }
}
