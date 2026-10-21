using System.Collections.Generic;
using Snow.Cargo;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// Fuel in a jerrycan (FOREST_U3 F5.4): litres replicated by the server; every peer keeps the carried mass in step
    /// (CargoItem.massKg = empty + litres × density) from the first frame, so a client's card shows the right weight;
    /// the server also sets the body's mass. Numbers TEMPORARY.
    /// </summary>
    [RequireComponent(typeof(CargoItem))]
    public class FuelCan : NetworkBehaviour
    {
        public float capacityL = 20f;
        [Tooltip("kg of the empty can")] public float emptyKg = 4f;
        [Tooltip("kg per litre of diesel")] public float kgPerLitre = 0.725f;

        readonly NetworkVariable<float> _fuelL = new NetworkVariable<float>(0f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        static readonly List<FuelCan> AllCans = new List<FuelCan>();
        public static IReadOnlyList<FuelCan> All => AllCans;

        CargoItem _item;
        Collider _collider;

        public float FuelL => _fuelL.Value;
        public CargoItem Item => _item;

        void Awake()
        {
            _item = GetComponent<CargoItem>();
            foreach (Collider c in GetComponentsInChildren<Collider>())
                if (!c.isTrigger) { _collider = c; break; }
        }

        void OnEnable() => AllCans.Add(this);
        void OnDisable() => AllCans.Remove(this);

        /// <summary>Server, before Spawn: the litres it starts with (sent with the spawn message).</summary>
        public void SetInitial(float litres)
        {
            _fuelL.Value = Mathf.Clamp(litres, 0f, capacityL);
            ApplyMass();
        }

        public override void OnNetworkSpawn()
        {
            _fuelL.OnValueChanged += OnFuel;
            ApplyMass();                                               // OnValueChanged does not come for the initial value
        }

        public override void OnNetworkDespawn() => _fuelL.OnValueChanged -= OnFuel;

        void OnFuel(float before, float now) => ApplyMass();

        void ApplyMass()
        {
            float kg = emptyKg + kgPerLitre * _fuelL.Value;
            _item.massKg = kg;
            if ((IsServer || !IsSpawned) && _item.Body != null) _item.Body.mass = kg;
        }

        /// <summary>Server: add (or take, negative) litres; returns what was actually moved.</summary>
        public float ServerAdd(float litres)
        {
            float before = _fuelL.Value;
            float after = Mathf.Clamp(before + litres, 0f, capacityL);
            if (!Mathf.Approximately(after, before)) _fuelL.Value = after;
            return after - before;
        }

        /// <summary>Centre of the can's collider (where the pump nozzle looks for it).</summary>
        public Vector3 Center => _collider != null ? _collider.bounds.center : transform.position;
    }
}
