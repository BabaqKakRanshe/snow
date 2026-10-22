using System.Collections.Generic;
using Snow.Cargo;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// The key of the plane's central lock (§8.3, NEW-05; model v7 PlaneKey). A pocket-sized network item (CargoItem): E
    /// takes it into the hand, Q into a pocket, G throws it — the same pockets as every small thing (user 21.10; it had a
    /// pocket of its own before, O-03). The server decides who has it: Holder is the client with it in the hand or a
    /// pocket, None while it lies in the world. Nothing duplicates it: there is exactly one key object and only the server
    /// moves it between pocket, hand and world.
    /// </summary>
    [RequireComponent(typeof(CargoItem))]
    public class PlaneKey : NetworkBehaviour
    {
        public const ulong None = CargoItem.Nobody;
        static readonly List<PlaneKey> AllKeys = new List<PlaneKey>();

        CargoItem _item;

        public ulong Holder => _item != null && _item.Carry != CarryMode.None ? _item.CarryClient : None;
        public CargoItem Item => _item;
        public static IReadOnlyList<PlaneKey> All => AllKeys;

        /// <summary>Does this client have a key: in his pocket or in his hand?</summary>
        public static bool ClientHasKey(ulong client)
        {
            foreach (var k in AllKeys)
                if (k.IsSpawned && k.Holder == client) return true;
            return false;
        }

        void Awake() => _item = GetComponent<CargoItem>();

        void OnEnable() => AllKeys.Add(this);
        void OnDisable() => AllKeys.Remove(this);

        /// <summary>Server: into this client's pocket (from the world or from his hand; PlaneLoadout: the host starts with it).</summary>
        public bool ServerPocket(ulong client, out string reason)
        {
            if (Holder != None && Holder != client) { reason = "Ключ у другого игрока"; return false; }
            if (_item.State == CargoState.Secured) _item.ServerSecure(client, false, out _);
            return _item.ServerToPocket(client, out reason);
        }
    }
}
