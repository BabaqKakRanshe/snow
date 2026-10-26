using System.Collections.Generic;
using Snow.Audio;
using Snow.Doors;
using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// The plane's sounds on every peer from its replicated switches and telemetry (SOUND_LIST_FULL «Самолёт», user
    /// 26.10.2026): every switch bit, levers, knobs and seats by their change; the battery contactor, engine light-off and
    /// shutdown, autopilot engage / disconnect; loops for the engine (pitched by Ng), propeller, avionics, flaps motor,
    /// strobes, airframe wind and wheels; touchdowns from the gear compression. The stall horn stays in PlanePresentation.
    /// </summary>
    [RequireComponent(typeof(PlanePresentation))]
    public class PlaneSounds : MonoBehaviour
    {
        const float QuietStart = 2f;

        PlaneVehicle _plane;
        readonly Dictionary<string, Transform> _sockets = new Dictionary<string, Transform>();
        PlaneSwitchState _s;
        PlaneTelemetry _t;
        readonly Dictionary<NetworkDoor, DoorState> _doors = new Dictionary<NetworkDoor, DoorState>();
        bool _init;
        float _since, _flap;
        SoundLoop _windmill, _gyro, _blower, _headset, _radioHiss, _igniter, _heater, _doorSeal, _buffet, _servo, _hot, _actuator, _brake, _doorWarn, _turb, _skid;
        float _surfaceAt;
        bool _onIce;

        /// <summary>Rough air, 0..1: the weather is not networked, so it is read from how fast the plane turns and climbs.</summary>
        float LocationGust() => Mathf.Clamp01((Mathf.Abs(_t.TurnRate) + Mathf.Abs(_t.VsiFpm) / 400f) / 6f);
        float _hungSince = -1f, _nextSlosh, _doorBitsAt = -100f;
        PlaneWinterKitNet _kit;
        int _kitMask = -1;
        bool _flapsMoving;
        float _tickUntil, _nextTick, _spoolAt = -1f, _nextCreak;
        SoundLoop _idle, _run, _prop, _avionics, _flaps, _strobe, _wind, _roll, _starter, _pump;

        void Start()
        {
            _plane = GetComponent<PlanePresentation>().vehicle;
            if (_plane != null) _kit = _plane.GetComponent<PlaneWinterKitNet>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (!_sockets.ContainsKey(t.name)) _sockets[t.name] = t;
            _since = Time.time;
        }

        Transform Socket(string name) => _sockets.TryGetValue(name, out Transform t) && t != null ? t : transform;

        void Play(string id, string socket = "Socket_Audio_Cabin")
        {
            if (Time.time - _since >= QuietStart) SnowAudio.Play(id, Socket(socket).position);
        }

        void Loop(ref SoundLoop loop, string id, string socket, bool on, float level = 1f, float pitch = 1f)
        {
            if (loop == null && on) loop = SoundLoop.Create(id, Socket(socket), Vector3.zero, true);
            if (loop == null) return;
            loop.Switch(on);
            loop.Level = level;
            loop.PitchScale = pitch;
        }

        void Update()
        {
            if (_plane == null || !_plane.IsSpawned) return;
            PlaneSwitchState s = _plane.Switches;
            PlaneTelemetry t = _plane.Telemetry;
            if (!_init)
            {
                _s = s; _t = t; _flap = t.Flap; _init = true;
                foreach (NetworkDoor d in new[] { _plane.pilotDoorL, _plane.pilotDoorR, _plane.cargoDoorL, _plane.cargoDoorR })
                    if (d != null) _doors[d] = d.State;
            }
            Switches(s);
            Telemetry(t);
            Doors();
            Kit();
            _s = s;
            _t = t;
        }

        void Switches(PlaneSwitchState s)
        {
            uint changed = s.Bits ^ _s.Bits;
            for (int b = 0; b < 31 && changed != 0; b++)
            {
                uint bit = 1u << b;
                if ((changed & bit) == 0) continue;
                bool on = (s.Bits & bit) != 0;
                switch ((PlaneSwitch)bit)
                {
                    case PlaneSwitch.AnnunTest: Play("plane.annun.test"); break;
                    case PlaneSwitch.Starter: Play("plane.starter.switch"); break;
                    case PlaneSwitch.CabinHeat: case PlaneSwitch.Defrost: case PlaneSwitch.ParkBrake: case PlaneSwitch.FuelShutoff:
                        Play(on ? "plane.pull.out" : "plane.pull.in"); break;
                    case PlaneSwitch.FuelSelL: case PlaneSwitch.FuelSelR: Play(on ? "plane.fuelsel.open" : "plane.fuelsel.close"); break;
                    case PlaneSwitch.BeltP: case PlaneSwitch.BeltC: Play(on ? "plane.belt.buckle" : "plane.belt.unbuckle"); break;
                    case PlaneSwitch.VisorP: case PlaneSwitch.VisorC: Play(on ? "plane.visor.down" : "plane.visor.up"); break;
                    case PlaneSwitch.FuelCapL: Play(on ? "plane.fuelcap.open" : "plane.fuelcap.close", "Socket_FuelCap_L"); break;
                    case PlaneSwitch.FuelCapR: Play(on ? "plane.fuelcap.open" : "plane.fuelcap.close", "Socket_FuelCap_R"); break;
                    case PlaneSwitch.ConditionCutOff: Play(on ? "plane.lever.condition.cutoff" : "plane.lever.condition.idle"); break;
                    case PlaneSwitch.Locked: Play(on ? "plane.lock.lock" : "plane.lock.unlock", "Socket_Keyhole_PilotDoor_L"); break;
                    case PlaneSwitch.Glovebox: Play(on ? "plane.glovebox.open" : "plane.glovebox.close"); break;
                    default: Play(on ? "plane.switch.on" : "plane.switch.off"); break;
                }
            }
            if (s.FlapLever != _s.FlapLever) Play("plane.lever.flap");
            if (!Mathf.Approximately(s.PropLever, _s.PropLever)) Play("plane.lever.prop");
            if (Mathf.Abs(s.RheoPanel - _s.RheoPanel) > 0.2f || Mathf.Abs(s.RheoFlood - _s.RheoFlood) > 0.2f) Play("plane.rheo.step");
            if (s.Xpdr != _s.Xpdr) Play("plane.knob.xpdr");
            if (s.Com != _s.Com || s.Nav != _s.Nav || s.Adf != _s.Adf) Play("plane.knob.radio");
            if (s.AltSetting != _s.AltSetting || s.HdgBug != _s.HdgBug) Play("plane.knob.instrument");
            if (s.PaxSeats != _s.PaxSeats) Play(BitCount(s.PaxSeats) > BitCount(_s.PaxSeats) ? "plane.paxseat.down" : "plane.paxseat.fold", "Socket_Audio_Bay");
            if (s.LadderStowed != _s.LadderStowed) Play(s.LadderStowed ? "plane.ladder.fold" : "plane.ladder.unfold", "Socket_Audio_Bay");
        }

        static int BitCount(byte b) { int n = 0; for (; b != 0; b &= (byte)(b - 1)) n++; return n; }

        void Telemetry(PlaneTelemetry t)
        {
            if (t.BusPowered != _t.BusPowered) Play(t.BusPowered ? "plane.battery.relay.on" : "plane.battery.relay.off");
            if (t.EngineRunning && !_t.EngineRunning) Play("plane.engine.lightoff", "Socket_Audio_Engine");
            if (t.EngineRunning && !_t.EngineRunning) _spoolAt = Time.time + 1.2f;
            if (_spoolAt > 0f && Time.time >= _spoolAt) { _spoolAt = -1f; Play("plane.engine.spoolup", "Socket_Audio_Engine"); }
            if (!t.EngineRunning && _t.EngineRunning)
            {
                bool cut = (_plane.Switches.Bits & (uint)PlaneSwitch.ConditionCutOff) != 0;   // stopped by the lever, or starved
                Play(cut ? "plane.engine.shutdown" : "plane.engine.flameout", "Socket_Audio_Engine");
                _tickUntil = Time.time + 60f; _nextTick = Time.time + 6f;
            }
            if (!t.EngineRunning && Time.time < _tickUntil && Time.time >= _nextTick)
            {
                _nextTick = Time.time + Random.Range(3f, 8f);
                Play("plane.engine.tick", "Socket_Audio_Engine");
            }
            if (Time.time >= _nextCreak)
            {
                _nextCreak = Time.time + Random.Range(15f, 45f);
                Play("plane.airframe.creak", "Socket_Audio_Bay");
            }
            if (t.ApEngaged != _t.ApEngaged) Play(t.ApEngaged ? "plane.ap.engage" : "plane.ap.disconnect");
            if (t.GearL > 0.05f && _t.GearL <= 0.05f || t.GearR > 0.05f && _t.GearR <= 0.05f) Play("plane.touchdown.main", "Socket_Audio_Gear_L");
            if (t.GearT > 0.05f && _t.GearT <= 0.05f) Play("plane.touchdown.tail", "Socket_Audio_Tail");
            bool flapsMoving = Mathf.Abs(t.Flap - _flap) > 1e-4f;
            _flap = t.Flap;
            if (_flapsMoving && !flapsMoving) Play("plane.flaps.stop");
            _flapsMoving = flapsMoving;

            float ng = Mathf.InverseLerp(52f, 101f, t.NgPct);
            float prop = Mathf.InverseLerp(0f, 1900f, t.PropRpm);
            bool starter = (_plane.Switches.Bits & (uint)PlaneSwitch.Starter) != 0 && t.BusPowered;
            Loop(ref _starter, "plane.starter.whine", "Socket_Audio_Engine", starter && !t.EngineRunning, 1f, Mathf.Clamp(t.BusVolts / 24f, 0.6f, 1.1f));
            Loop(ref _idle, "plane.engine.idle", "Socket_Audio_Engine", t.EngineRunning, 1f - ng * 0.6f, 0.9f + ng * 0.2f);
            Loop(ref _run, "plane.engine.run", "Socket_Audio_Engine", t.EngineRunning && ng > 0.05f, ng, 0.85f + ng * 0.3f);
            Loop(ref _prop, "plane.prop", "Socket_Audio_Engine", prop > 0.05f, prop, 0.7f + prop * 0.4f);
            Loop(ref _avionics, "plane.avionics.hum", "Socket_Audio_Cabin", t.Avionics > 0 && t.BusPowered);
            Loop(ref _pump, "plane.fuelpump", "Socket_Audio_Cabin", (_plane.Switches.Bits & (uint)PlaneSwitch.FuelPump) != 0 && t.BusPowered);
            Loop(ref _flaps, "plane.flaps.motor", "Socket_Audio_Cabin", flapsMoving && t.BusPowered);
            Loop(ref _strobe, "plane.strobe.whine", "Socket_Audio_Cabin", t.Strobe > 0 && t.BusPowered, 0.6f);
            Loop(ref _wind, "plane.wind.airframe", "Socket_Audio_Cabin", t.TrueAirspeed > 15f, Mathf.Clamp01((t.TrueAirspeed - 15f) / 90f));
            bool rolling = (t.GearL > 0.02f || t.GearR > 0.02f) && t.GroundSpeed > 1f;
            Loop(ref _roll, "plane.gear.roll", "Socket_Audio_Gear_L", rolling, Mathf.Clamp01(t.GroundSpeed / 30f));
            uint bits = _plane.Switches.Bits;
            bool bus = t.BusPowered;
            if ((t.GearL > 0.95f && _t.GearL <= 0.95f) || (t.GearR > 0.95f && _t.GearR <= 0.95f)) Play("plane.gear.bottom", "Socket_Audio_Gear_L");
            Loop(ref _doorSeal, "plane.wind.doorseal", "Socket_Audio_Wind_L", t.TrueAirspeed > 25f, Mathf.Clamp01((t.TrueAirspeed - 25f) / 80f));
            bool parkBrake = (bits & (uint)PlaneSwitch.ParkBrake) != 0;
            Loop(ref _brake, "plane.brake", "Socket_Audio_Gear_L", parkBrake && t.GroundSpeed > 1f, Mathf.Clamp01(t.GroundSpeed / 15f));
            bool onWheels = t.GearL > 0.05f || t.GearR > 0.05f;
            if (onWheels && Time.time >= _surfaceAt)                            // what the wheels stand on (this peer's own look)
            {
                _surfaceAt = Time.time + 0.5f;
                string surf = SurfaceResolver.At(transform.position, transform);
                _onIce = surf == "Ice" || surf == "Frozen";
            }
            Loop(ref _skid, "plane.gear.skid", "Socket_Audio_Gear_L", onWheels && _onIce && parkBrake && t.GroundSpeed > 2f, Mathf.Clamp01(t.GroundSpeed / 12f));
            bool doorOpen = false;
            foreach (var kv in _doors) if (kv.Value != DoorState.Closed) doorOpen = true;
            Loop(ref _doorWarn, "plane.door.warning", "Socket_Audio_Cabin", bus && doorOpen && t.TrueAirspeed > 30f, 0.6f);   // a door open in flight
            Loop(ref _turb, "plane.turbulence", "Socket_Audio_Cabin", t.TrueAirspeed > 40f && LocationGust() > 0.3f, LocationGust());
            Loop(ref _buffet, "plane.stall.buffet", "Socket_Audio_Cabin", t.StallWarning && t.TrueAirspeed > 15f);
            Loop(ref _servo, "plane.ap.servo", "Socket_Audio_Cabin", t.ApEngaged, 0.4f);
            Loop(ref _hot, "plane.engine.hot", "Socket_Audio_Engine", t.EngineRunning && t.IttC > 820f);                       // over the ITT limit (telemetry)
            // a hung start: lit, the starter let go, Ng stuck below idle
            bool hung = t.EngineRunning && t.NgPct < 45f && !starter;
            if (hung && _hungSince < 0f) _hungSince = Time.time;
            if (!hung) _hungSince = -1f;
            if (hung && Time.time - _hungSince > 3f) { _hungSince = float.MaxValue; Play("plane.engine.hungstart", "Socket_Audio_Engine"); }
            if ((t.GroundSpeed > 3f || t.TrueAirspeed > 20f) && Time.time >= _nextSlosh)
            {
                _nextSlosh = Time.time + Random.Range(10f, 30f);
                Play("plane.tank.slosh", Random.value < 0.5f ? "Socket_Audio_FuelTank_L" : "Socket_Audio_FuelTank_R");
            }
            // powered doors whir while one travels after its switch moved
            const uint DoorBits = (uint)(PlaneSwitch.CargoDoorL | PlaneSwitch.CargoDoorR | PlaneSwitch.CabinDoorL | PlaneSwitch.CabinDoorR);
            if (((bits ^ _s.Bits) & DoorBits) != 0) _doorBitsAt = Time.time;
            bool doorMoving = false;
            foreach (var kv in _doors) if (kv.Value == DoorState.Opening || kv.Value == DoorState.Closing) doorMoving = true;
            Loop(ref _actuator, "plane.door.actuator", "Socket_Audio_Bay", bus && doorMoving && Time.time - _doorBitsAt < 6f);
            Loop(ref _windmill, "plane.prop.windmill", "Socket_Audio_Engine", !t.EngineRunning && prop > 0.05f, prop);
            Loop(ref _gyro, "plane.gyro.spin", "Socket_Audio_Cabin", bus, 0.5f);
            Loop(ref _blower, "plane.blower", "Socket_Audio_Cabin", bus && (bits & (uint)(PlaneSwitch.CabinHeat | PlaneSwitch.Defrost)) != 0);
            Loop(ref _headset, "plane.headset.hiss", "Socket_Audio_Headset_P", bus && t.Avionics > 0, 0.4f);
            Loop(ref _radioHiss, "plane.radio.static", "Socket_Audio_Radio", bus && t.Avionics > 0, 0.3f);
            Loop(ref _igniter, "plane.igniter.tick", "Socket_Audio_Engine", starter && (bits & (uint)PlaneSwitch.Ignition) != 0 && !t.EngineRunning);
            Loop(ref _heater, "plane.heater.run", "Socket_Audio_Engine", _kit != null && _kit.Preheating);
        }

        /// <summary>The winter kit: each form put on or taken off (the replicated mask, bit i = KitItem i).</summary>
        void Kit()
        {
            if (_kit == null) return;
            int m = _kit.Mask;
            if (_kitMask < 0) { _kitMask = m; return; }
            int changed = m ^ _kitMask;
            for (int i = 0; changed != 0 && i < 15; i++)
            {
                if ((changed & (1 << i)) == 0) continue;
                bool on = (m & (1 << i)) != 0;
                string item = ((UtilityPlane.KitItem)i).ToString();
                string kind = item.StartsWith("Intake") ? "intake" : item.StartsWith("Exhaust") ? "exhaust" : item.StartsWith("Pitot") ? "pitot"
                    : item.StartsWith("WingCover") ? "wingcover" : item.StartsWith("Windshield") ? "windshield" : item.StartsWith("EngineCover") ? "enginecover"
                    : item.StartsWith("Chocks") ? "chocks" : item.StartsWith("TieDown") ? "tiedown" : null;
                string cue = kind != null ? $"plane.kit.{kind}.{(on ? "on" : "off")}" : on ? "plane.kit.heater.place" : "plane.kit.heater.remove";
                string socket = _sockets.ContainsKey("Kit_" + item) ? "Kit_" + item : "Socket_Audio_Bay";
                Play(cue, socket);
            }
            _kitMask = m;
        }

        void Doors()
        {
            var keys = new List<NetworkDoor>(_doors.Keys);
            foreach (NetworkDoor d in keys)
            {
                DoorState s = d.State, was = _doors[d];
                if (s == was) continue;
                bool cargo = d == _plane.cargoDoorL || d == _plane.cargoDoorR;
                string kind = cargo ? "cargo" : "cabin";
                Vector3 at = d.transform.position;
                if (Time.time - _since >= QuietStart)
                {
                    if (s == DoorState.Opening && was == DoorState.Closed) SnowAudio.Play($"plane.door.{kind}.open", at);
                    else if (s == DoorState.Closed) SnowAudio.Play($"plane.door.{kind}.close", at);
                    else if (s == DoorState.Open) SnowAudio.Play("plane.door.endstop", at);
                    else if (s == DoorState.Blocked) SnowAudio.Play("plane.door.blocked", at);
                }
                _doors[d] = s;
            }
        }
    }
}
