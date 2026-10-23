using Snow.Input;
using Snow.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Snow.Voice
{
    /// <summary>
    /// Starts the voice on its own (like the cheat menu: created on the first scene, survives scene loads): registers the
    /// named messages on whichever NetworkManager is running, keeps the HUD, and gives the own player its body voice.
    /// </summary>
    public class VoiceSystem : MonoBehaviour
    {
        static VoiceSystem s_instance;
        NetworkManager _attached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_instance != null) return;
            var go = new GameObject("SnowVoice");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<VoiceSystem>();
            VoiceHud.Ensure(go);
        }

        void Update()
        {
            NetworkManager nm = NetworkManager.Singleton;
            bool up = nm != null && nm.IsListening && nm.CustomMessagingManager != null;
            if (up && _attached != nm)
            {
                VoiceNet.Attach(nm);
                _attached = nm;
            }
            else if (!up && _attached != null)
            {
                VoiceNet.Detach();
                _attached = null;
            }
            PlayerNet me = PlayerNet.Local;
            if (me != null && !me.TryGetComponent(out BodyVoice _)) me.gameObject.AddComponent<BodyVoice>();
        }

        void OnDestroy()
        {
            if (_attached != null) VoiceNet.Detach();
            if (s_instance == this) s_instance = null;
        }
    }

    /// <summary>
    /// The own player's body voice (QUESTIONS_VOICE 2, 3, 13): C pressed — a callout (whistle or "Hey!", loud, creatures
    /// hear it), C held — a mark where the eyes point, seen by everyone for a few seconds; a hard landing grunts, a fall
    /// from height hurts; lifting something heavy strains. The character never says words. The server checks cooldowns,
    /// plays the sound to everyone and tells the creatures. Cold (cough, shiver) waits for the cold system. Numbers TEMPORARY.
    /// </summary>
    public class BodyVoice : MonoBehaviour
    {
        const float PingHold = 0.35f, LandSpeed = 7.5f, HurtSpeed = 12f, PingReach = 80f;

        PlayerNet _net;
        float _cDown = -1f, _airVy;
        bool _pinged, _wasGrounded = true, _wasHeavy;
        readonly System.Collections.Generic.Dictionary<BodySound, float> _last = new System.Collections.Generic.Dictionary<BodySound, float>();

        void Awake() => _net = GetComponent<PlayerNet>();

        void Update()
        {
            if (_net == null || !_net.IsOwner) return;
            bool onFoot = _net.controller != null && _net.controller.Source != null && _net.controller.Source.Context == InputContext.OnFoot;

            // C: press = callout, hold = mark
            Keyboard kb = Keyboard.current;
            if (kb != null && onFoot)
            {
                if (kb.cKey.wasPressedThisFrame) { _cDown = Time.time; _pinged = false; }
                if (_cDown >= 0f && kb.cKey.isPressed && !_pinged && Time.time - _cDown >= PingHold)
                {
                    _pinged = true;
                    Send(BodySound.Ping, PingPoint());
                }
                if (_cDown >= 0f && kb.cKey.wasReleasedThisFrame)
                {
                    if (!_pinged) Send(BodySound.Callout, transform.position);
                    _cDown = -1f;
                }
            }
            else _cDown = -1f;

            // landing: the speed just before touching the ground
            if (_net.motor != null)
            {
                bool grounded = _net.motor.Grounded;
                if (!grounded) _airVy = Mathf.Min(_airVy, _net.motor.Velocity.y);
                else if (!_wasGrounded)
                {
                    float impact = -_airVy;
                    if (impact >= HurtSpeed) Send(BodySound.PainBig, transform.position);
                    else if (impact >= LandSpeed) Send(BodySound.Land, transform.position);
                    _airVy = 0f;
                }
                if (grounded) _airVy = 0f;
                _wasGrounded = grounded;
            }

            // lifting something heavy
            bool heavy = _net.sensor != null && _net.sensor.HeldHeavy;
            if (heavy && !_wasHeavy) Send(BodySound.EffortHeavy, transform.position);
            _wasHeavy = heavy;
        }

        Vector3 PingPoint()
        {
            Vector3 eye = _net.view != null ? _net.view.EyePosition : transform.position + Vector3.up * 1.6f;
            Vector3 dir = _net.view != null ? _net.view.EyeRotation * Vector3.forward : transform.forward;
            return Physics.Raycast(eye + dir * 0.3f, dir, out RaycastHit hit, PingReach, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point : eye + dir * 30f;
        }

        void Send(BodySound sound, Vector3 point)
        {
            if (_last.TryGetValue(sound, out float at) && Time.time - at < BodySounds.Cooldown(sound)) return;
            _last[sound] = Time.time;
            VoiceNet.Body(sound, point);
        }
    }
}
