namespace Snow.Seats
{
    public enum SeatRole : byte
    {
        Pilot,
        Passenger,
    }

    /// <summary>
    /// SEAT-01, SEAT-02: who sits where, decided on the server. One player per seat, one seat per player; only the
    /// occupant of a Pilot seat may send flight commands. Pure C#.
    /// </summary>
    public class SeatRegistry
    {
        public const ulong Empty = ulong.MaxValue;

        readonly ulong[] _occupant;
        readonly SeatRole[] _roles;

        public SeatRegistry(params SeatRole[] roles)
        {
            _roles = roles;
            _occupant = new ulong[roles.Length];
            for (int i = 0; i < _occupant.Length; i++) _occupant[i] = Empty;
        }

        public int Count => _roles.Length;
        public SeatRole Role(int seat) => _roles[seat];
        public ulong Occupant(int seat) => _occupant[seat];

        public int SeatOf(ulong player)
        {
            for (int i = 0; i < _occupant.Length; i++)
                if (_occupant[i] == player) return i;
            return -1;
        }

        public bool IsPilot(ulong player)
        {
            int s = SeatOf(player);
            return s >= 0 && _roles[s] == SeatRole.Pilot;
        }

        /// <summary>Server decision for a sit request. Reason is a short player-facing text on refusal.</summary>
        public bool TryOccupy(int seat, ulong player, out string reason)
        {
            reason = null;
            if (seat < 0 || seat >= _occupant.Length) { reason = "Нет такого кресла"; return false; }
            if (SeatOf(player) >= 0) { reason = "Вы уже сидите"; return false; }
            if (_occupant[seat] != Empty) { reason = "Место занято"; return false; }
            _occupant[seat] = player;
            return true;
        }

        /// <summary>Free the player's seat (exit, disconnect). Returns the seat index or -1.</summary>
        public int Release(ulong player)
        {
            int s = SeatOf(player);
            if (s >= 0) _occupant[s] = Empty;
            return s;
        }

        /// <summary>Set the occupant directly (replicated state on clients, checkpoint restore).</summary>
        public void Set(int seat, ulong player) => _occupant[seat] = player;
    }
}
