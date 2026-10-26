using Snow.Cargo;
using Unity.Netcode;
using UnityEngine;
using UtilityPlane;

namespace Snow.Aircraft
{
    /// <summary>
    /// A carry form of the winter kit or a refuelling tool as a game item (И4, K4.2): a CargoItem carried by grab plus
    /// the two runtime values of UtilityPlaneKitItem the server owns and every peer sees — the sets left in the
    /// tie-down bag and the litres in the jerry can. The item's mass follows the fuel (TotalMassKg).
    /// </summary>
    [RequireComponent(typeof(CargoItem), typeof(UtilityPlaneKitItem))]
    public class KitCarry : NetworkBehaviour
    {
        readonly NetworkVariable<int> _setsLeft = new NetworkVariable<int>(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<float> _fuel = new NetworkVariable<float>(-1f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        UtilityPlaneKitItem _kit;
        CargoItem _item;
        Rigidbody _rb;

        public UtilityPlaneKitItem Kit => _kit;
        public CargoItem Item => _item;
        public int SetsLeft => _kit.setsLeft;
        public float FuelLitres => _kit.fuelLitres;
        public bool IsJerryCan => _kit.key == "JerryCan";
        public bool IsTieDownBag => _kit.key == "TieDownBag";

        void Awake()
        {
            _kit = GetComponent<UtilityPlaneKitItem>();
            _item = GetComponent<CargoItem>();
            _rb = GetComponent<Rigidbody>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                if (_setsLeft.Value < 0) _setsLeft.Value = _kit.setsLeft;
                if (_fuel.Value < 0f) _fuel.Value = _kit.fuelLitres;
            }
            _setsLeft.OnValueChanged += (_, v) => Apply();
            _fuel.OnValueChanged += (_, v) => Apply();
            Apply();
        }

        void Apply()
        {
            if (_setsLeft.Value >= 0) _kit.setsLeft = _setsLeft.Value;
            if (_fuel.Value >= 0f) _kit.fuelLitres = _fuel.Value;
            if (IsServer && _rb != null) _rb.mass = Mathf.Max(0.05f, _kit.TotalMassKg);
        }

        /// <summary>Server.</summary>
        public void ServerSetSets(int sets)
        {
            if (sets != _setsLeft.Value) Snow.Audio.SnowAudio.ServerPlay("plane.tiedownbag.set", transform.position);   // ropes and anchors rummaged
            _setsLeft.Value = Mathf.Clamp(sets, 0, _kit.sets);
            Apply();
        }

        /// <summary>Server: litres in the can, 0..capacity.</summary>
        public void ServerSetFuel(float litres) { _fuel.Value = Mathf.Clamp(litres, 0f, _kit.capacityLitres); Apply(); }
    }
}
