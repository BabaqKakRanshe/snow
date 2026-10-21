using Snow.Interaction;
using Snow.Seats;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The four bunks of the barracks as seats of the project's seat system (FOREST_U3 F6.1, ENGINEERING): a static seat
    /// host with one seat per mattress (pelvis on the mattress centre, eye over the pillow looking along the bunk). Lie
    /// down with the use key on the zone at the pillow (Sleep_1 … Sleep_4), get up like from any seat (hold F); the seat
    /// system parents the player to this object, so it is its own root NetworkObject in the scene (ForestBunks).
    /// </summary>
    public class ForestBunks : SeatedVehicle
    {
        public override bool ServerUse(ulong clientId, string id, UsePhase phase, out string reason)
        {
            reason = null;
            if (phase != UsePhase.Press) return true;
            int seat = SeatIndex(id);
            if (seat < 0) { reason = "Нет действия"; return false; }
            if (OccupantOf(seat) != SeatRegistry.Empty) { reason = "Койка занята"; return false; }
            return ServerSit(clientId, seat, out reason);
        }

        public override string Describe(string id)
        {
            int seat = SeatIndex(id);
            if (seat < 0) return null;
            return OccupantOf(seat) == SeatRegistry.Empty ? "свободна" : "занята";
        }

        public override string ActionText(string id) => SeatIndex(id) >= 0 ? "лечь" : base.ActionText(id);

        /// <summary>Is this client lying in one of the bunks (replicated)?</summary>
        public bool InBunk(ulong clientId)
        {
            if (seats == null) return false;
            for (int i = 0; i < seats.Length; i++)
                if (OccupantOf(i) == clientId) return true;
            return false;
        }
    }
}
