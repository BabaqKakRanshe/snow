using Snow.Interaction;

namespace Snow.Levels
{
    /// <summary>
    /// A location's own rules on top of <see cref="SkyIslandLocationHost"/> (FOREST_U3 F5.2): the host takes every
    /// component with this interface from its object in Awake. A script owns some ids completely (its Scripted parts, zones
    /// without parts, decal zones), may give the text of any id, and decides whether a locked part opens for this player.
    /// </summary>
    public interface ILocationScript
    {
        /// <summary>The script handles every phase of this id, Release included.</summary>
        bool Owns(string id);

        /// <summary>Server: a use of an owned id, after the reach check.</summary>
        bool ServerUse(ulong clientId, string id, UsePhase phase, out string reason);

        /// <summary>State text of the id, or null for the host's default.</summary>
        string Describe(string id);

        /// <summary>Action text of the id, or null for the host's default.</summary>
        string ActionText(string id);

        /// <summary>Seconds to hold the use for the id (0 = a click).</summary>
        float HoldSeconds(string id);

        /// <summary>Server: a locked part was used. True = the lock is off now and the use goes on; false = refused
        /// with <paramref name="reason"/> (null: the host's "Заперто").</summary>
        bool TryUnlock(ulong clientId, string id, out string reason);
    }
}
