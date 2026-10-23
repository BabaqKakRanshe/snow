using System;
using Snow.Net;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// Night kinds (22.10, solution 2; DayZ's dark / bright night setting, Sons of the Forest's ask for moon phases): the
    /// server picks the next night's kind once per day while the day factor is 1, so the change never shows; every peer
    /// eases its multipliers toward the kind (about 8 s) and the calibrated night (solution 1) is the "normal" one.
    /// One byte over the network.
    /// </summary>
    public partial class ForestDayNight
    {
        /// <summary>Multipliers on the calibrated night (1 = as calibrated) and the chance the server picks it.</summary>
        [Serializable]
        public struct NightKind
        {
            public string name;
            [Tooltip("Relative chance of the server picking this kind")] public float chance;
            [Tooltip("Moonlight")] public float moon;
            [Tooltip("Ambient and reflections")] public float ambient;
            [Tooltip("Sky colours")] public float sky;
            public float stars;
            [Tooltip("The moon's disc in the sky")] public float moonDisc;
            [Tooltip("Fog colour")] public float fog;
            [Tooltip("Threat at night (multiplies threatByHour)")] public float threat;
        }

        [Header("Night kinds (22.10)")]
        public NightKind[] nightKinds =
        {
            new NightKind { name = "Тёмная (новолуние, облака)", chance = 0.35f, moon = 0.1f, ambient = 0.45f, sky = 0.45f, stars = 0f, moonDisc = 0f, fog = 0.6f, threat = 1f },
            new NightKind { name = "Обычная", chance = 0.45f, moon = 1f, ambient = 1f, sky = 1f, stars = 0.6f, moonDisc = 0.7f, fog = 1f, threat = 0.9f },
            new NightKind { name = "Светлая (полнолуние, ясно)", chance = 0.2f, moon = 1.8f, ambient = 1.5f, sky = 1.3f, stars = 1f, moonDisc = 1f, fog = 1.25f, threat = 0.75f },
        };
        [Tooltip("Kind of the session's first night (index in nightKinds)")] public int startNightKind = 1;

        readonly NetworkVariable<byte> _kind = new NetworkVariable<byte>(1);
        float _kMoon = 1f, _kAmbient = 1f, _kSky = 1f, _kStars = 1f, _kDisc = 1f, _kFog = 1f;
        bool _kInit, _pickedToday;

        public int NightKindIndex => ClampKind(IsSpawned ? _kind.Value : startNightKind);
        public NightKind CurrentNight => nightKinds != null && nightKinds.Length > 0 ? nightKinds[NightKindIndex]
                                                                                       : new NightKind { moon = 1f, ambient = 1f, sky = 1f, stars = 1f, moonDisc = 1f, fog = 1f, threat = 1f };

        int ClampKind(int k) => nightKinds != null && nightKinds.Length > 0 ? Mathf.Clamp(k, 0, nightKinds.Length - 1) : 0;

        /// <summary>Server: this kind now (cheat menu); the next full day picks again.</summary>
        public void ServerSetNightKind(int k)
        {
            if (!IsServer) return;
            _kind.Value = (byte)ClampKind(k);
            BenchLog.Event("daynight", "night_kind", _kind.Value);
        }

        void ServerPickNight()
        {
            float sum = 0f;
            foreach (NightKind k in nightKinds) sum += Mathf.Max(0f, k.chance);
            float r = UnityEngine.Random.value * sum;
            int pick = nightKinds.Length - 1;
            for (int i = 0; i < nightKinds.Length; i++)
            {
                r -= Mathf.Max(0f, nightKinds[i].chance);
                if (r <= 0f) { pick = i; break; }
            }
            ServerSetNightKind(pick);
        }

        /// <summary>The server's pick at full day, then the local multipliers eased toward the kind. True while they move.</summary>
        bool UpdateNightKind(float day, float dt)
        {
            if (IsSpawned && IsServer && nightKinds != null && nightKinds.Length > 0)
            {
                if (day >= 1f && !_pickedToday) { _pickedToday = true; ServerPickNight(); }
                else if (day <= 0f) _pickedToday = false;
            }
            NightKind k = CurrentNight;
            // snap: the first call, an editor preview (dt 0) and full day (the multipliers weigh nothing then, and a blend
            // would keep the slow path on every frame for half a minute after each morning pick)
            float a = _kInit && dt > 0f && day < 1f ? 1f - Mathf.Exp(-dt / 8f) : 1f;
            _kInit = true;
            _kMoon = Mathf.Lerp(_kMoon, k.moon, a);
            _kAmbient = Mathf.Lerp(_kAmbient, k.ambient, a);
            _kSky = Mathf.Lerp(_kSky, k.sky, a);
            _kStars = Mathf.Lerp(_kStars, k.stars, a);
            _kDisc = Mathf.Lerp(_kDisc, k.moonDisc, a);
            _kFog = Mathf.Lerp(_kFog, k.fog, a);
            return Mathf.Abs(_kAmbient - k.ambient) > 0.01f || Mathf.Abs(_kSky - k.sky) > 0.01f || Mathf.Abs(_kStars - k.stars) > 0.01f;
        }
    }
}
