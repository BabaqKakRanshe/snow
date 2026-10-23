using Snow.Player;
using Snow.Voice;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Audio
{
    /// <summary>
    /// Steps of one player on every peer (MONSTERS.md: the real player rustles, the Double is silent — so everyone hears
    /// everyone). Each peer counts the distance the player moved on the ground and steps every stride; nothing goes over
    /// the network. The own player reads its motor (grounded, crouched), the others a ray down and their speed. The server
    /// also tells the creatures (NoiseEvents), throttled. Numbers TEMPORARY (MovementTuning: walk 2.9, sprint 6.1 m/s).
    /// </summary>
    public class Footsteps : MonoBehaviour
    {
        const float RunFrom = 4.8f, SneakBelow = 1.6f, StandBelow = 0.3f;
        const float LandFrom = 2.5f, AirMin = 0.2f;

        PlayerNet _player;
        Vector3 _last;
        float _dist, _airTime, _fallSpeed, _noiseAt;
        bool _grounded = true;

        void Awake()
        {
            _player = GetComponent<PlayerNet>();
            _last = transform.position;
        }

        void Update()
        {
            Vector3 p = transform.position;
            Vector3 d = p - _last;
            _last = p;
            float dt = Time.deltaTime;
            if (dt <= 0f || d.sqrMagnitude > 25f) return;                          // teleport, respawn
            if (_player != null && _player.Seated) { _dist = 0f; _grounded = true; return; }

            bool own = _player != null && _player.motor != null && _player.motor.enabled;
            bool grounded = own ? _player.motor.Grounded : GroundBelow(p);
            if (!grounded)
            {
                _airTime += dt;
                _fallSpeed = Mathf.Max(_fallSpeed, -d.y / dt);
                _grounded = false;
                return;
            }
            if (!_grounded && _airTime > AirMin && _fallSpeed > LandFrom) Land(p, _fallSpeed);
            if (!_grounded) _dist = 0f;
            _grounded = true;
            _airTime = 0f;
            _fallSpeed = 0f;

            float h = new Vector2(d.x, d.z).magnitude, speed = h / dt;
            if (speed < StandBelow) { _dist = Mathf.Min(_dist, 0.3f); return; }   // standing: the first step comes soon
            bool run = speed > RunFrom;
            bool sneak = own ? _player.motor.Crouched : speed < SneakBelow;
            float stride = Mathf.Lerp(0.55f, 1.35f, Mathf.InverseLerp(1f, 6.1f, speed));
            _dist += h;
            if (_dist < stride) return;
            _dist = 0f;
            Step(p, run, sneak);
        }

        bool GroundBelow(Vector3 p) =>
            Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, 0.6f, ~0, QueryTriggerInteraction.Ignore);

        SurfaceSteps StepsHere(Vector3 p)
        {
            SoundLibrary lib = SnowAudio.Library;
            if (lib == null) return null;
            return lib.Steps(SurfaceResolver.At(p, transform)) ?? lib.Steps(lib.outdoorDefault);
        }

        void Step(Vector3 p, bool run, bool sneak)
        {
            SurfaceSteps s = StepsHere(p);
            if (s == null) return;
            string id = run && !string.IsNullOrEmpty(s.run) ? s.run : s.walk;
            SnowAudio.Play(id, p, sneak ? 0.4f : 1f);
            Noise(p, sneak ? 0f : run ? NoiseEvents.Medium : NoiseEvents.Quiet, run ? 1f : 2f);
        }

        void Land(Vector3 p, float fallSpeed)
        {
            SurfaceSteps s = StepsHere(p);
            SoundLibrary lib = SnowAudio.Library;
            string id = s != null && !string.IsNullOrEmpty(s.land) ? s.land : lib != null ? lib.defaultLand : null;
            SnowAudio.Play(id, p, Mathf.Clamp(0.6f + (fallSpeed - LandFrom) * 0.1f, 0.6f, 1.3f));
            Noise(p, NoiseEvents.Medium, 0f);
        }

        /// <summary>Server: creatures hear the steps (at most one noise per interval).</summary>
        void Noise(Vector3 p, float radius, float interval)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (radius <= 0f || nm == null || !nm.IsServer || Time.time < _noiseAt) return;
            _noiseAt = Time.time + interval;
            NoiseEvents.Emit(p, radius, "step", _player != null ? _player.OwnerClientId : ulong.MaxValue);
        }
    }
}
