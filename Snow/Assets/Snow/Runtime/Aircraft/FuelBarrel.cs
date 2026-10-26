using Snow.Cargo;
using Snow.Interaction;
using Snow.Net;
using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// TEMPORARY (K4.5, where to fill the jerry can is OPEN): a fuel drum by the parking spot. With a jerry can in hand,
    /// its zone Barrel_Fill fills the can to its capacity (20 l). A stand-in object, not a model asset.
    /// </summary>
    public class FuelBarrel : InteractionHost
    {
        public override bool ZoneOpen(UtilityPlane.UtilityPlaneInteractable zone, CargoItem held) =>
            held != null && held.TryGetComponent(out KitCarry k) && k.IsJerryCan;

        public override bool ServerUse(ulong clientId, string id, UsePhase phase, out string reason)
        {
            reason = null;
            if (phase != UsePhase.Press) return true;
            CargoItem held = CargoItem.HeldBy(clientId);
            if (held == null || !held.TryGetComponent(out KitCarry can) || !can.IsJerryCan) { reason = "Нужна канистра в руках"; return false; }
            can.ServerSetFuel(can.Kit.capacityLitres);
            Snow.Audio.SnowAudio.ServerPlay("plane.barrel.fill", transform.position);
            BenchLog.Event("barrel_fill", "client", clientId, "litres", can.FuelLitres);
            return true;
        }

        public override string Describe(string id) => "Jet A-1, наполнить канистру";
        public override string ActionText(string id) => "наполнить канистру";
    }
}
