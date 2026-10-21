using Snow.Cargo;
using Snow.Net;
using Unity.Netcode;
using UnityEngine;

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
    /// </summary>
    [RequireComponent(typeof(CargoItem))]
    public class SurvivalItem : NetworkBehaviour
    {
        public SurvivalKind kind;
        [Tooltip("s a hand flare burns")] public float flareSeconds = 60f;
        [Tooltip("m/s of the signal flare leaving the muzzle; s it burns in the air")] public float shotSpeed = 45f, shotSeconds = 7f;

        readonly NetworkVariable<bool> _on = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<bool> _loaded = new NetworkVariable<bool>(true,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<double> _burnStart = new NetworkVariable<double>(-1d,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        Light _light;
        Transform _socket;

        public bool On => _on.Value;
        public bool Loaded => _loaded.Value;
        public bool Burning => _burnStart.Value >= 0d && Age < flareSeconds;
        public bool Spent => _burnStart.Value >= 0d && Age >= flareSeconds;
        double Age => NetworkManager != null ? NetworkManager.ServerTime.Time - _burnStart.Value : 0d;

        void Awake()
        {
            string socket = kind == SurvivalKind.Flashlight ? "Socket_Light" : kind == SurvivalKind.Flare ? "Socket_Flame"
                          : kind == SurvivalKind.FlareGun ? "Socket_Muzzle" : null;
            if (socket == null) return;
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == socket) _socket = t;
            if (_socket == null || kind == SurvivalKind.FlareGun) return;
            var go = new GameObject(kind == SurvivalKind.Flashlight ? "Light_Flashlight" : "Light_Flare");
            go.transform.SetParent(_socket, false);
            _light = go.AddComponent<Light>();
            if (kind == SurvivalKind.Flashlight)
            {
                _light.type = LightType.Spot; _light.range = 30f; _light.spotAngle = 38f; _light.innerSpotAngle = 14f;
                _light.intensity = 12f; _light.color = new Color(1f, 0.93f, 0.8f); _light.shadows = LightShadows.None;
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
            if (_light == null || !IsSpawned) return;
            if (kind == SurvivalKind.Flashlight) _light.enabled = _on.Value;
            else if (kind == SurvivalKind.Flare)
            {
                bool burning = Burning;
                _light.enabled = burning;
                if (burning) _light.intensity = 10f * (0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 9f, GetInstanceID() * 0.01f));
            }
        }

        // ================================================================ server
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
                    if (!_loaded.Value) { reason = "Ракетница не заряжена"; return false; }
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
            l.type = LightType.Point; l.range = 120f; l.intensity = 400f; l.color = new Color(1f, 0.2f, 0.1f); l.shadows = LightShadows.None;
            go.AddComponent<SignalFlareEffect>().Init(dir.normalized * shotSpeed, shotSeconds);
        }
    }
}
