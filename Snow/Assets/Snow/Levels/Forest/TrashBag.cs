using Snow.Cargo;
using Snow.Net;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The rubbish bag (FOREST_U3 F7.1): a loose rubbish item (ForestItemTag.clean) that falls into the trigger over the
    /// upper half of the bag is taken away by the server; the bag gets its mass. The contents are replicated like the
    /// jerrycan's fuel, so every peer shows the bag's weight; the server counts on the opening script.
    /// </summary>
    [RequireComponent(typeof(CargoItem))]
    public class TrashBag : NetworkBehaviour
    {
        [Tooltip("kg of the empty bag")] public float emptyKg = 3f;

        readonly NetworkVariable<float> _contentKg = new NetworkVariable<float>(0f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        CargoItem _item;

        public float ContentKg => _contentKg.Value;

        void Awake() => _item = GetComponent<CargoItem>();

        public override void OnNetworkSpawn()
        {
            _contentKg.OnValueChanged += OnContent;
            ApplyMass();
        }

        public override void OnNetworkDespawn() => _contentKg.OnValueChanged -= OnContent;

        void OnContent(float before, float now) => ApplyMass();

        void ApplyMass()
        {
            _item.massKg = emptyKg + _contentKg.Value;
            if (IsServer && _item.Body != null) _item.Body.mass = _item.massKg;
        }

        // the trigger is a child collider of this body: Unity reports it here
        void OnTriggerEnter(Collider other)
        {
            if (!IsServer || !IsSpawned) return;
            CargoItem item = other.GetComponentInParent<CargoItem>();
            if (item == null || item == _item || !item.IsSpawned || item.State != CargoState.Loose) return;
            if (!item.TryGetComponent(out ForestItemTag tag) || !tag.clean) return;
            float kg = item.massKg;
            _contentKg.Value += kg;
            BenchLog.Event("forest_trash", "item", item.name, "kg", kg, "bag", _contentKg.Value);
            if (ForestOpening.Instance != null) ForestOpening.Instance.ServerTrashed(item);
            item.NetworkObject.Despawn(true);
        }
    }
}
