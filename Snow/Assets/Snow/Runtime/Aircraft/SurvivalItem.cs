using Snow.Audio;
using Snow.Cargo;
using Snow.Character;
using Snow.Net;
using Snow.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Snow.Aircraft
{
    public enum SurvivalKind : byte
    {
        None,
        Flashlight,
        FlareGun,
        FlareShell,
        Flare,
        Shovel,
        Bag,
    }

    /// <summary>
    /// The survival kit works at a basic level (user decision 16.10; model Data/survival_kit.json sockets). The item is
    /// carried by grab (CargoItem); with it in hand the player's item action (TEMPORARY: left mouse button) asks the
    /// server, which decides and replicates:
    ///  - flashlight: light on / off from Socket_Light (+Z);
    ///  - flare gun: one round; fires a red signal flare up from Socket_Muzzle, every peer sees it (event); the shell
    ///    item reloads an empty gun within reach;
    ///  - hand flare: lit once, burns flareSeconds red at Socket_Flame, then it is spent;
    ///  - shovel: there is no snow cover to dig in the project yet — says so;
    ///  - bag: cargo only.
    /// All numbers TEMPORARY (docs/dev/Tuning/Cargo.md).
    /// Lights made here shine on every rendering layer (LOCATIONS L4.8): maps with light zones (BaseV2, Forest) move their
    /// renderers off layer 0, and a light on layer 0 only would not reach the ground, trees and walls.
    /// </summary>
    [RequireComponent(typeof(CargoItem))]
    public class SurvivalItem : NetworkBehaviour
    {
        public SurvivalKind kind;
        [Tooltip("s a hand flare burns")] public float flareSeconds = 60f;
        [Tooltip("m/s of the signal flare leaving the muzzle; s it burns in the air")] public float shotSpeed = 45f, shotSeconds = 7f;
        [Header("Flashlight lens (optional: the Forest flashlight, FOREST_U3 F4.1.5)")]
        [Tooltip("Renderer with the lens material slot; empty = no lens swap")] public Renderer lensRenderer;
        public int lensSlot = -1;
        public Material lensOn, lensOff;

        readonly NetworkVariable<bool> _on = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<bool> _loaded = new NetworkVariable<bool>(true,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<double> _burnStart = new NetworkVariable<double>(-1d,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        Light _light;
        Transform _socket;
        CargoItem _item;
        int _lensShown = -1;                                           // -1 unknown, 0 off, 1 on
        bool _sndInit, _wasOn, _wasLoaded;                             // sounds follow the replicated state on every peer
        int _flareShown;                                               // 0 unlit, 1 burning, 2 spent
        float _sndSince;
        SoundLoop _burn;

        public bool On => _on.Value;
        public bool Loaded => _loaded.Value;
        public bool Burning => _burnStart.Value >= 0d && Age < flareSeconds;
        public bool Spent => _burnStart.Value >= 0d && Age >= flareSeconds;
        double Age => NetworkManager != null ? NetworkManager.ServerTime.Time - _burnStart.Value : 0d;

        void Awake()
        {
            _item = GetComponent<CargoItem>();
            string socket = kind == SurvivalKind.Flashlight ? "Socket_Light" : kind == SurvivalKind.Flare ? "Socket_Flame"
                          : kind == SurvivalKind.FlareGun ? "Socket_Muzzle" : null;
            if (socket == null) return;
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == socket) _socket = t;
            if (_socket == null || kind == SurvivalKind.FlareGun) return;
            var go = new GameObject(kind == SurvivalKind.Flashlight ? "Light_Flashlight" : "Light_Flare");
            go.transform.SetParent(_socket, false);
            _light = go.AddComponent<Light>();
            _light.GetUniversalAdditionalLightData().renderingLayers = uint.MaxValue;
            if (kind == SurvivalKind.Flashlight)
            {
                _light.type = LightType.Spot; _light.range = 30f; _light.spotAngle = 38f; _light.innerSpotAngle = 14f;
                _light.intensity = 12f; _light.color = new Color(1f, 0.93f, 0.8f); _light.shadows = LightShadows.None;
                go.AddComponent<LightBeam>();                              // the beam in the fog (user 23.10)
            }
            else
            {
                _light.type = LightType.Point; _light.range = 14f; _light.intensity = 10f;
                _light.color = new Color(1f, 0.22f, 0.12f); _light.shadows = LightShadows.None;
            }
            _light.enabled = false;
        }

        void Update()
        {
            Sounds();
            if (_light == null || !IsSpawned) return;
            bool pocket = _item.Carry == CarryMode.Pocket;                 // a lit thing in a pocket does not shine out of it
            if (kind == SurvivalKind.Flashlight)
            {
                _light.enabled = _on.Value && !pocket;
                ShowLens(_on.Value);
            }
            else if (kind == SurvivalKind.Flare)
            {
                bool burning = Burning;
                _light.enabled = burning && !pocket;
                if (burning) _light.intensity = 10f * (0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 9f, GetInstanceID() * 0.01f));
            }
        }

        /// <summary>Every peer: switch, load, ignite and burn-out sounds from the replicated state (user 26.10.2026).</summary>
        void Sounds()
        {
            if (!IsSpawned) return;
            int flare = Burning ? 1 : Spent ? 2 : 0;
            if (!_sndInit) { _sndInit = true; _wasOn = _on.Value; _wasLoaded = _loaded.Value; _flareShown = flare; _sndSince = Time.time; }
            bool quiet = Time.time - _sndSince < 1f;
            Vector3 at = _socket != null ? _socket.position : transform.position;
            if (_on.Value != _wasOn) { if (!quiet) SnowAudio.Play(_on.Value ? "item.flashlight.on" : "item.flashlight.off", at); _wasOn = _on.Value; }
            if (_loaded.Value != _wasLoaded)
            {
                if (!quiet && _loaded.Value) { SnowAudio.Play("item.flaregun.open", at); SnowAudio.Play("item.flaregun.load", at); }
                _wasLoaded = _loaded.Value;
            }
            if (flare != _flareShown)
            {
                if (!quiet) SnowAudio.Play(flare == 1 ? "item.flare.ignite" : "item.flare.out", at);
                _flareShown = flare;
            }
            if (kind == SurvivalKind.Flare)
            {
                if (_burn == null && flare == 1) _burn = SoundLoop.Create("item.flare.burn", _socket != null ? _socket : transform, Vector3.zero, true);
                if (_burn != null) _burn.Switch(flare == 1);
            }
        }

        /// <summary>Server: a burning flare dropped in the snow hisses.</summary>
        void OnCollisionEnter(Collision c)
        {
            if (!IsServer || kind != SurvivalKind.Flare || !Burning || c.relativeVelocity.magnitude < 1f) return;
            SnowAudio.ServerPlay("item.flare.snow", transform.position);
        }

        // the lens material follows the light; written only when it changes
        void ShowLens(bool on)
        {
            if (lensRenderer == null || lensSlot < 0 || _lensShown == (on ? 1 : 0)) return;
            _lensShown = on ? 1 : 0;
            Material want = on ? lensOn : lensOff;
            Material[] mats = lensRenderer.sharedMaterials;
            if (want == null || lensSlot >= mats.Length) return;
            mats[lensSlot] = want;
            lensRenderer.sharedMaterials = mats;
        }

        // ================================================================ server
        /// <summary>Server: a flashlight on or off without a player's action (the starter flashlight, ForestItems).</summary>
        public void ServerSetOn(bool on)
        {
            if (kind == SurvivalKind.Flashlight) _on.Value = on;
        }

        /// <summary>Server: the item action of the player holding it.</summary>
        public bool ServerAction(ulong client, Vector3 playerPos, out string reason)
        {
            reason = null;
            switch (kind)
            {
                case SurvivalKind.Flashlight:
                    _on.Value = !_on.Value;
                    BenchLog.Event("item_action", "item", name, "on", _on.Value);
                    return true;
                case SurvivalKind.FlareGun:
                    if (!_loaded.Value) { reason = "Ракетница не заряжена"; SnowAudio.ServerPlay("item.flaregun.dry", transform.position); return false; }
                    _loaded.Value = false;
                    Transform m = _socket != null ? _socket : transform;
                    ShotRpc(m.position, m.forward);
                    BenchLog.Event("item_action", "item", name, "fired", m.position);
                    return true;
                case SurvivalKind.FlareShell:
                {
                    SurvivalItem gun = null;
                    float best = 2.5f;
                    foreach (var c in CargoItem.All)
                        if (c.TryGetComponent(out SurvivalItem s) && s.kind == SurvivalKind.FlareGun && !s.Loaded
                            && Vector3.Distance(c.transform.position, playerPos) < best) { best = Vector3.Distance(c.transform.position, playerPos); gun = s; }
                    if (gun == null) { reason = "Рядом нет разряженной ракетницы"; return false; }
                    gun._loaded.Value = true;
                    GetComponent<CargoItem>().ServerRelease(client);
                    NetworkObject.Despawn(true);
                    BenchLog.Event("item_action", "item", "flare_shell", "loaded", gun.name);
                    return true;
                }
                case SurvivalKind.Flare:
                    if (Spent) { reason = "Фальшфейер догорел"; return false; }
                    if (Burning) { reason = "Уже горит"; return false; }
                    _burnStart.Value = NetworkManager.ServerTime.Time;
                    BenchLog.Event("item_action", "item", name, "lit", true);
                    return true;
                case SurvivalKind.Shovel:
                    reason = "Копать нечего: снежного покрова в проекте ещё нет";
                    SnowAudio.ServerPlay("item.shovel.scrape", transform.position);
                    return false;
                default:
                    reason = "Это просто груз";
                    return false;
            }
        }

        /// <summary>Client: what the item action does now (hover hint).</summary>
        public string ActionText()
        {
            switch (kind)
            {
                case SurvivalKind.Flashlight: return _on.Value ? "выключить" : "включить";
                case SurvivalKind.FlareGun: return _loaded.Value ? "выстрелить вверх" : "не заряжена";
                case SurvivalKind.FlareShell: return "зарядить ракетницу рядом";
                case SurvivalKind.Flare: return Spent ? "догорел" : Burning ? "горит" : "зажечь";
                case SurvivalKind.Shovel: return "копать";
                default: return null;
            }
        }

        // the signal flare: a local effect on every peer (an event, late joiners do not need a flare that burnt out)
        [Rpc(SendTo.ClientsAndHost)]
        void ShotRpc(Vector3 from, Vector3 dir)
        {
            var go = new GameObject("SignalFlare");
            go.transform.position = from;
            var l = go.AddComponent<Light>();
            l.GetUniversalAdditionalLightData().renderingLayers = uint.MaxValue;
            l.type = LightType.Point; l.range = 120f; l.intensity = 400f; l.color = new Color(1f, 0.2f, 0.1f); l.shadows = LightShadows.None;
            go.AddComponent<SignalFlareEffect>().Init(dir.normalized * shotSpeed, shotSeconds);
            SnowAudio.Play("item.flaregun.fire", from);
            SnowAudio.Play("item.flaregun.flight", from + dir.normalized * 8f);
            CameraShake.AddAt(from, 0.35f, 4f);                         // the shooter's (and a neighbour's) kick
        }
    }
}
