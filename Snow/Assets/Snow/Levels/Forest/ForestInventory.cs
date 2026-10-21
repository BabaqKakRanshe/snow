using System;
using Snow.Cargo;
using Snow.Net;
using Snow.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// Pockets of every player in Forest (user decision 21.10: "нет инвентаря, не смогу взять ключи"): four slots per player,
    /// kept by the server in one list on the Forest root (Player.prefab stays as it is). Q puts the small item in hand into the
    /// first free slot — the server despawns it and remembers its model, key label and name; 1–4 take a slot out — the server
    /// spawns the item again in front of the player, ready to pick up with E. A key in the pockets opens its locks as if it
    /// were in hand (ForestOpening.HasKey). Heavy things (jerrycan, sack, ladder) stay physical: two hands, as before.
    /// Pattern after Lethal Company / Phasmophobia (3–4 slots); numbers TEMPORARY.
    /// </summary>
    public class ForestInventory : NetworkBehaviour
    {
        public const int Slots = 4;
        [Tooltip("kg: heavier items do not go into the pockets")] public float maxMassKg = 3f;
        [Tooltip("Spawns items again when they leave the pockets (ForestItems on the same root)")] public ForestItems items;

        public struct Entry : INetworkSerializable, IEquatable<Entry>
        {
            public ulong client;
            public byte slot;
            public FixedString32Bytes model, key, note;
            public FixedString64Bytes label;

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref client);
                s.SerializeValue(ref slot);
                s.SerializeValue(ref model);
                s.SerializeValue(ref key);
                s.SerializeValue(ref note);
                s.SerializeValue(ref label);
            }

            public bool Equals(Entry o) => client == o.client && slot == o.slot && model.Equals(o.model) && key.Equals(o.key) && note.Equals(o.note) && label.Equals(o.label);
        }

        public static ForestInventory Instance { get; private set; }

        NetworkList<Entry> _list;
        int _serial;
        string _hint;
        float _hintUntil;
        GUIStyle _box, _small;

        void Awake()
        {
            Instance = this;
            _list = new NetworkList<Entry>();                          // NetworkBehaviour.OnDestroy disposes it
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) _list.Clear();                               // a second Host in the same Play session
        }

        /// <summary>Any peer: the player has a key with this lock label in the pockets.</summary>
        public bool HasKey(ulong client, string key)
        {
            if (_list == null || string.IsNullOrEmpty(key)) return false;
            for (int i = 0; i < _list.Count; i++)
                if (_list[i].client == client && _list[i].key.ToString() == key) return true;
            return false;
        }

        bool TryGet(ulong client, int slot, out int index)
        {
            for (index = 0; index < _list.Count; index++)
                if (_list[index].client == client && _list[index].slot == slot) return true;
            index = -1;
            return false;
        }

        // ------------------------------------------------------------------ input (own player, on foot)
        void Update()
        {
            if (!IsSpawned) return;
            PlayerNet me = PlayerNet.Local;
            Keyboard kb = Keyboard.current;
            if (me == null || kb == null || me.Seated) return;
            if (kb.qKey.wasPressedThisFrame)
            {
                if (me.sensor != null && me.sensor.Held != null) StashRpc();
                else Hint("Q — убрать в карман предмет из рук");
            }
            for (int s = 0; s < Slots; s++)
                if (kb[(Key)((int)Key.Digit1 + s)].wasPressedThisFrame) TakeRpc((byte)s);
        }

        void Hint(string s)
        {
            _hint = s;
            _hintUntil = Time.unscaledTime + 2.5f;
        }

        // ------------------------------------------------------------------ server
        [Rpc(SendTo.Server)]
        void StashRpc(RpcParams p = default)
        {
            ulong client = p.Receive.SenderClientId;
            CargoItem held = CargoItem.HeldBy(client);
            if (held == null) return;
            if (held.massKg > maxMassKg) { ReplyRpc("Слишком тяжело для кармана", RpcTarget.Single(client, RpcTargetUse.Temp)); return; }
            int free = -1;
            for (int s = 0; s < Slots && free < 0; s++) if (!TryGet(client, s, out _)) free = s;
            if (free < 0) { ReplyRpc("Карманы полны", RpcTarget.Single(client, RpcTargetUse.Temp)); return; }
            held.TryGetComponent(out ForestItemTag tag);
            string model = tag != null && !string.IsNullOrEmpty(tag.model) ? tag.model : held.itemKey;
            if (string.IsNullOrEmpty(model)) { ReplyRpc("Это в карман не положить", RpcTarget.Single(client, RpcTargetUse.Temp)); return; }
            _list.Add(new Entry
            {
                client = client, slot = (byte)free, model = model,
                key = tag != null && !string.IsNullOrEmpty(tag.key) ? tag.key : "",
                note = held.TryGetComponent(out ReadableNote rn) ? rn.NoteId : "",
                label = string.IsNullOrEmpty(held.label) ? model : (held.label.Length > 60 ? held.label.Substring(0, 60) : held.label),
            });
            held.ServerRelease(client);
            held.NetworkObject.Despawn(true);
            BenchLog.Event("inventory", "client", client, "stash", model, "slot", free);
        }

        [Rpc(SendTo.Server)]
        void TakeRpc(byte slot, RpcParams p = default)
        {
            ulong client = p.Receive.SenderClientId;
            if (!TryGet(client, slot, out int i)) return;
            if (items == null || !NetworkManager.ConnectedClients.TryGetValue(client, out NetworkClient nc) || nc.PlayerObject == null) return;
            Entry e = _list[i];
            Transform pl = nc.PlayerObject.transform;
            Vector3 at = pl.position + pl.forward * 0.7f + Vector3.up * 1.1f;
            NetworkObject no = items.Spawn($"inv_{client}_{++_serial}", e.model.ToString(), at, Quaternion.LookRotation(pl.forward),
                                          readable: e.note.Length > 0 ? e.note.ToString() : null);
            if (no == null) { ReplyRpc("Не удалось достать", RpcTarget.Single(client, RpcTargetUse.Temp)); return; }
            _list.RemoveAt(i);
            BenchLog.Event("inventory", "client", client, "take", e.model.ToString(), "slot", slot);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ReplyRpc(string text, RpcParams p) => Hint(text);

        // ------------------------------------------------------------------ hotbar (own player)
        void OnGUI()
        {
            if (!IsSpawned || PlayerNet.Local == null || NetworkManager == null) return;
            ulong me = NetworkManager.LocalClientId;
            _box ??= new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.UpperLeft, wordWrap = true };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            const float w = 150f, h = 46f, gap = 6f;
            float x0 = (Screen.width - (Slots * w + (Slots - 1) * gap)) * 0.5f, y = Screen.height - h - 18f;
            for (int s = 0; s < Slots; s++)
            {
                string text = $"{s + 1}  —";
                if (TryGet(me, s, out int i)) text = $"{s + 1}  {_list[i].label}{(_list[i].key.Length > 0 ? " (ключ)" : "")}";
                GUI.Box(new Rect(x0 + s * (w + gap), y, w, h), text, _box);
            }
            if (_hint != null && Time.unscaledTime < _hintUntil)
                GUI.Label(new Rect(0f, y - 26f, Screen.width, 22f), _hint, _small);
        }
    }
}
