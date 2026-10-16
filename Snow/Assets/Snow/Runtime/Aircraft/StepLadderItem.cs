using Snow.Cargo;
using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// The step ladder to the fuel caps (model v7 StepLadder, M5 closed; И3 H). A cargo item carried by grab; the climb is
    /// the motor's ladder mode in the volume IV_Ladder_Climb. Its rear legs (Ladder_RearLegs, with their collider) fold
    /// −38° about the local X while it is carried or stowed in the bay (ENGINEERING: folded for carrying, required for
    /// stowing, H.5) and unfold when it is put down. Every peer poses them from the replicated CargoItem state.
    /// </summary>
    [RequireComponent(typeof(CargoItem))]
    public class StepLadderItem : MonoBehaviour
    {
        public const float RearLegsFoldDeg = -38f;
        [Tooltip("deg/s the rear legs swing")] public float foldSpeed = 120f;

        CargoItem _item;
        Transform _legs;
        Quaternion _rest;
        float _angle;

        void Awake()
        {
            _item = GetComponent<CargoItem>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Ladder_RearLegs") _legs = t;
            if (_legs != null) _rest = _legs.localRotation;
        }

        void Update()
        {
            if (_legs == null || !_item.IsSpawned) return;
            bool folded = _item.State == CargoState.Carried || _item.State == CargoState.Stowed;
            float target = folded ? RearLegsFoldDeg : 0f;
            if (Mathf.Approximately(_angle, target)) return;
            _angle = Mathf.MoveTowards(_angle, target, foldSpeed * Time.deltaTime);
            _legs.localRotation = _rest * Quaternion.Euler(_angle, 0f, 0f);
        }
    }
}
