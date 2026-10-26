using Snow.Audio;
using Snow.World;
using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The checkpoint's power sounds on every peer, from the state <see cref="ForestKpp"/> already shows (SOUND_LIST_FULL
    /// «КПП»): relays and the door strike with the booth's power, the batten (strike, ballast hum), the CRT monitors
    /// (degauss, flyback whine) of the booth and the Leitstand, the camera heads and floodlights with the drives, the
    /// battery on its shelf. Parts (switch, clamps, buttons, barrier, gate) sound through <see cref="LocationSounds"/>.
    /// </summary>
    [RequireComponent(typeof(ForestKpp))]
    public class ForestKppSounds : MonoBehaviour
    {
        const float QuietStart = 2f;

        ForestKpp _kpp;
        bool _init, _booth, _base, _drives, _battery;
        float _since;
        SoundLoop _batten, _radio;
        readonly bool[] _lamps = new bool[4];
        float _nextHeater;
        readonly SoundLoop[] _boothCrt = new SoundLoop[2], _leitCrt = new SoundLoop[2], _flood = new SoundLoop[2];

        void Start()
        {
            _kpp = GetComponent<ForestKpp>();
            _since = Time.time;
        }

        void Update()
        {
            if (_kpp == null) return;
            bool booth = _kpp.BoothPowered, drives = _kpp.DrivesPowered, base_ = _kpp.host != null && _kpp.host.Powered;
            bool battery = _kpp.BatteryIn;
            if (!_init) { _booth = booth; _base = base_; _drives = drives; _battery = battery; _init = true; }
            bool quiet = Time.time - _since < QuietStart;

            if (booth != _booth)
            {
                if (!quiet)
                {
                    Play(booth ? "kpp.relay.on" : "kpp.relay.off", _kpp.booth);
                    Play(booth ? "kpp.doorlock.release" : "kpp.doorlock.engage", _kpp.doorStrike != null ? _kpp.doorStrike.transform : _kpp.booth);
                    Play(booth ? "kpp.fluor.start" : "kpp.fluor.off", Lamp());
                    for (int i = 0; i < 2; i++) Play(booth ? "kpp.crt.on" : "kpp.crt.off", Kit(_kpp.boothMonitors, i));
                    if (booth && !base_) Play("kpp.clamp.spark", _kpp.cabinet != null ? _kpp.cabinet.transform : _kpp.booth);
                }
                _booth = booth;
            }
            if (base_ != _base)
            {
                if (!quiet) for (int i = 0; i < 2; i++) Play(base_ ? "kpp.crt.on" : "kpp.crt.off", Kit(_kpp.leitMonitors, i));
                _base = base_;
            }
            if (drives != _drives)
            {
                if (!quiet)
                    for (int i = 0; i < 2; i++)
                    {
                        Play(drives ? "kpp.flood.on" : "kpp.flood.off", Kit(_kpp.floods, i));
                        if (drives) Play("kpp.camera.on", Kit(_kpp.cameraHeads, i));
                    }
                _drives = drives;
            }
            if (battery != _battery)
            {
                if (!quiet) Play(battery ? "kpp.battery.place" : "kpp.battery.remove", _kpp.batterySlot != null ? _kpp.batterySlot.transform : _kpp.booth);
                _battery = battery;
            }

            Loop(ref _batten, "kpp.fluor.loop", Lamp(), booth);
            Loop(ref _radio, "kpp.radio.static", _kpp.booth, booth);
            if (booth && Time.time >= _nextHeater)                              // the convector ticks as it warms
            {
                _nextHeater = Time.time + Random.Range(10f, 30f);
                if (!quiet) Play("kpp.heater.tick", _kpp.booth);
            }
            // the lamp over a console button lights with its drive's state, as ForestKpp shows it
            bool[] lit = { booth && _kpp.BarrierUp, booth && _kpp.GateOpen, base_ && _kpp.BarrierUp, base_ && _kpp.GateOpen };
            KitItem[] buttons = { _kpp.btnBarrier, _kpp.btnGate, _kpp.leitBtnBarrier, _kpp.leitBtnGate };
            for (int i = 0; i < 4; i++)
            {
                if (lit[i] && !_lamps[i] && !quiet && buttons[i] != null) Play("kpp.console.lamp.on", buttons[i].transform);
                _lamps[i] = lit[i];
            }
            for (int i = 0; i < 2; i++)
            {
                Loop(ref _boothCrt[i], "kpp.crt.loop", Kit(_kpp.boothMonitors, i), booth);
                Loop(ref _leitCrt[i], "kpp.crt.loop", Kit(_kpp.leitMonitors, i), base_);
                Loop(ref _flood[i], "kpp.flood.loop", Kit(_kpp.floods, i), drives);
            }
        }

        Transform Lamp()
        {
            if (_kpp.boothLights.Length > 0 && _kpp.boothLights[0] != null) return _kpp.boothLights[0].transform;
            return _kpp.lampRenderers.Length > 0 && _kpp.lampRenderers[0] != null ? _kpp.lampRenderers[0].transform : _kpp.booth;
        }

        static Transform Kit(KitItem[] kits, int i) => kits != null && i < kits.Length && kits[i] != null ? kits[i].transform : null;

        static void Loop(ref SoundLoop loop, string id, Transform at, bool on)
        {
            if (at == null) return;
            if (loop == null && on) loop = SoundLoop.Create(id, at, Vector3.zero, true);
            if (loop != null) loop.Switch(on);
        }

        static void Play(string id, Transform at)
        {
            if (at != null) SnowAudio.Play(id, at.position);
        }
    }
}
