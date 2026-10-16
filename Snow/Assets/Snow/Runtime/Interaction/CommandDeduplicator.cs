using System.Collections.Generic;

namespace Snow.Interaction
{
    /// <summary>
    /// NET-02: a discrete command carries a per-sender sequence number; a repeated or older number is ignored, so a
    /// resent command never opens a door twice, refuels twice or sits twice. Wrap-around safe (serial arithmetic).
    /// </summary>
    public class CommandDeduplicator
    {
        readonly Dictionary<ulong, uint> _last = new Dictionary<ulong, uint>();

        /// <summary>True the first time a sequence number newer than the last accepted one arrives from this sender.</summary>
        public bool Accept(ulong sender, uint seq)
        {
            if (_last.TryGetValue(sender, out uint last) && (int)(seq - last) <= 0) return false;
            _last[sender] = seq;
            return true;
        }

        /// <summary>Drop a sender's history (disconnect).</summary>
        public void Forget(ulong sender) => _last.Remove(sender);
    }
}
