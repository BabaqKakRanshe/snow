using Snow.Cargo;
using Snow.Interaction;
using Snow.Net;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Vehicles.Van
{
    /// <summary>
    /// The van's fuel (user 27.10, docs/gdd/locations/start_post: the van stands at the start post with an empty tank, the
    /// players fill it from the jerrycan in the booth). One pool for both tanks (the valve feeds the engine from either);
    /// the forest start begins empty, the bench with <see cref="benchFuelL"/>. Pouring: the cap open (the tank
    /// zone's third position, "можно лить"), a can with fuel in hand, hold the use near the filler neck — 1 L/s like the
    /// generator's neck. The engine catches only with fuel (the starter just cranks otherwise: VanSounds' no-start) and
    /// stops when the tank runs dry. Numbers TEMPORARY.
    /// </summary>
    public partial class VanVehicle
    {
        const float TankL = 56f, PourRate = 1f, NeckReach = 1.2f, IdleLph = 1.2f, FullLph = 19f;

        [Tooltip("Litres in the tank on the bench (the forest start is empty)")] public float benchFuelL = 18f;

        readonly NetworkVariable<float> _fuelL = new NetworkVariable<float>(0f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        ulong _pourClient = ulong.MaxValue;
        IFuelSource _pourCan;
        string _pourZone;
        Transform _neckL, _neckR;
        float _pourSoundAt;

        /// <summary>Litres in the tank (every peer).</summary>
        public float FuelL => _fuelL.Value;

        bool HasFuel => _fuelL.Value > 0.01f;

        static bool IsTankZone(string id) => id == "FuelFlap_L" || id == "FuelFlap_R";

        /// <summary>Server, on spawn.</summary>
        void FuelInit()
        {
            _fuelL.Value = startState == StartState.ForestStart ? 0f : Mathf.Clamp(benchFuelL, 0f, TankL);
            _neckL = Find(transform, "Pour_Fuel_L");
            _neckR = Find(transform, "Pour_Fuel_R");
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = Find(c, name);
                if (f != null) return f;
            }
            return null;
        }

        Transform Neck(string zone) => zone == "FuelFlap_R" ? (_neckR != null ? _neckR : transform) : (_neckL != null ? _neckL : transform);

        /// <summary>Server: a use of a tank zone with a can in hand and the cap off pours (true = handled, <paramref name="result"/>
        /// is the use's answer); otherwise the zone goes on cycling flap and cap as before (false).</summary>
        bool FuelZoneUse(ulong client, string id, UsePhase phase, out bool result, out string reason)
        {
            result = true;
            reason = null;
            if (!IsTankZone(id)) return false;
            if (phase == UsePhase.Release && _pourClient == client) { StopPour("released"); return true; }
            CargoItem held = CargoItem.HeldBy(client);
            IFuelSource can = held != null ? held.GetComponent<IFuelSource>() : null;
            if (can == null) return false;
            if (phase != UsePhase.Press) return true;
            if (Pos(id) != 2) return false;                   // the flap and cap open with the can in hand too
            if (can.FuelL <= 0.01f) { reason = "Канистра пуста"; result = false; return true; }
            if (_fuelL.Value >= TankL - 0.05f) { reason = "Бак полон"; result = false; return true; }
            if (Vector3.Distance(can.Center, Neck(id).position) > NeckReach) { reason = "Поднесите канистру к горловине"; result = false; return true; }
            _pourClient = client;
            _pourCan = can;
            _pourZone = id;
            BenchLog.Event("van_fuel", "pour", "start", "zone", id, "client", client, "tankL", _fuelL.Value, "canL", can.FuelL);
            _pourSoundAt = 0f;
            return true;
        }

        void StopPour(string why)
        {
            if (_pourCan == null) return;
            BenchLog.Event("van_fuel", "pour", "stop", "why", why, "tankL", _fuelL.Value);
            _pourCan = null;
            _pourZone = null;
            _pourClient = ulong.MaxValue;
        }

        /// <summary>Server, every physics step: the pour and the engine's burn.</summary>
        void FuelTick(float dt, float throttle)
        {
            if (_pourCan != null)
            {
                CargoItem held = CargoItem.HeldBy(_pourClient);
                if (held == null || held.GetComponent<IFuelSource>() != _pourCan) StopPour("can left the hand");
                else if (Pos(_pourZone) != 2) StopPour("cap closed");
                else if (Vector3.Distance(_pourCan.Center, Neck(_pourZone).position) > NeckReach) StopPour("out of reach");
                else
                {
                    float move = Mathf.Min(PourRate * dt, _pourCan.FuelL, TankL - _fuelL.Value);
                    if (move <= 0f) StopPour(_pourCan.FuelL <= 0.01f ? "can empty" : "tank full");
                    else
                    {
                        float taken = -_pourCan.ServerAdd(-move);
                        _fuelL.Value = Mathf.Min(TankL, _fuelL.Value + taken);
                        if (Time.time >= _pourSoundAt) { _pourSoundAt = Time.time + 5f; Snow.Audio.SnowAudio.ServerPlay("forest.fuel.pour", Neck(_pourZone).position); }
                    }
                }
            }
            if (_engine && _fuelL.Value > 0f)
            {
                float lph = Mathf.Lerp(IdleLph, FullLph, Mathf.Clamp01(Mathf.Abs(throttle)));
                _fuelL.Value = Mathf.Max(0f, _fuelL.Value - lph / 3600f * dt);
            }
        }

        string FuelDescribe(string id)
        {
            if (!IsTankZone(id)) return null;
            string litres = _fuelL.Value < 0.5f ? "бак пуст" : $"в баке {Mathf.RoundToInt(_fuelL.Value)} л";
            int c = VanControls.IndexOf(id);
            string state = c >= 0 && c < _pos.Count ? VanControls.Table[c].States[_pos[c]] : null;
            return string.IsNullOrEmpty(state) ? litres : state + ", " + litres;
        }

        string FuelActionText(string id)
        {
            CargoItem held = IsTankZone(id) && NetworkManager != null ? CargoItem.HeldBy(NetworkManager.LocalClientId) : null;
            if (!IsTankZone(id) || held == null || held.GetComponent<IFuelSource>() == null) return null;
            return Pos(id) == 2 ? "держать: залить из канистры" : null;
        }
    }
}
