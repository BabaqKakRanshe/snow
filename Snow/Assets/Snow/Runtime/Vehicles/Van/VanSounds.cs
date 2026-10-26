using System.Collections.Generic;
using Snow.Audio;
using Snow.Doors;
using UnityEngine;

namespace Snow.Vehicles.Van
{
    /// <summary>
    /// The van's sounds on every peer from the state it already replicates (SOUND_LIST_FULL «Фургон», user 26.10.2026):
    /// doors (NetworkDoor states), every control of <see cref="VanControls"/> by its position change, the engine as a loop
    /// pitched by rpm with catch / stop on the Engine flag, the starter, horn, hazard relay, handbrake, gear changes,
    /// heater fan, wipers, radio static, tyres and wind by speed, suspension knocks. Nothing new goes over the network.
    /// </summary>
    [RequireComponent(typeof(VanPresentation))]
    public class VanSounds : MonoBehaviour
    {
        const float QuietStart = 2f;
        const float HazardPeriod = 60f / 90f;          // the relay ticks 90 times a minute

        VanVehicle _van;
        readonly Dictionary<string, Transform> _parts = new Dictionary<string, Transform>();
        int[] _pos;
        DoorState[] _doors;
        VanFlags _flags;
        sbyte _gear;
        bool _init;
        float _since, _hazardNext;
        readonly float[] _travel = new float[4];
        SoundLoop _engine, _load, _starter, _horn, _fan, _wiper, _radio, _wind;
        SoundLoop _offload, _limiter, _whine, _rattle, _seals, _cold, _spin, _skid, _planks;
        readonly Dictionary<string, SoundLoop> _tyreLoops = new Dictionary<string, SoundLoop>();
        string _tyre = "van.tyre.packedsnow", _surface = "";
        float _surfaceAt;
        float _engineSince = -100f, _crankSince = -1f, _nextMisfire, _nextBody;
        bool _noStartHeard;
        byte[] _fuses = new byte[8];
        int _spares = -1;
        bool _battery, _wiperOn;
        float _tickUntil, _nextTick, _nextSweep, _nextSqueal, _nextCreak;
        static readonly string[] FuseIds = { "Fuse_1", "Fuse_2", "Fuse_3", "Fuse_4", "Fuse_5", "Fuse_6", "Fuse_7", "Fuse_8" };

        void Start()
        {
            _van = GetComponent<VanPresentation>().vehicle;
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (!_parts.ContainsKey(t.name)) _parts[t.name] = t;
            _since = Time.time;
        }

        Vector3 At(string part) => _parts.TryGetValue(part, out Transform t) && t != null ? t.position : transform.position;

        void Play(string id, string part)
        {
            if (Time.time - _since >= QuietStart) SnowAudio.Play(id, At(part));
        }

        SoundLoop Loop(ref SoundLoop loop, string id, string part, bool on, float level = 1f, float pitch = 1f)
        {
            if (loop == null && on)
            {
                Transform at = _parts.TryGetValue(part, out Transform t) && t != null ? t : transform;
                loop = SoundLoop.Create(id, at, Vector3.zero, true);
            }
            if (loop == null) return null;
            loop.Switch(on);
            loop.Level = level;
            loop.PitchScale = pitch;
            return loop;
        }

        void Update()
        {
            if (_van == null || !_van.IsSpawned) return;
            var table = VanControls.Table;
            VanTelemetry t = _van.Telemetry;
            if (!_init)
            {
                _pos = new int[table.Count];
                for (int c = 0; c < table.Count; c++) _pos[c] = _van.Pos(c);
                _doors = new DoorState[_van.doors.Length];
                for (int d = 0; d < _doors.Length; d++) _doors[d] = _van.doors[d] != null ? _van.doors[d].State : DoorState.Closed;
                _flags = t.Flags;
                _gear = t.Gear;
                for (int f = 0; f < 8; f++) _fuses[f] = _van.FuseState(FuseIds[f]);
                _spares = Spares();
                _battery = _van.BatteryIn;
                _init = true;
            }

            Doors();
            for (int c = 0; c < table.Count; c++)
            {
                int p = _van.Pos(c);
                if (p != _pos[c]) Control(table[c].Zone, _pos[c], p);
                _pos[c] = p;
            }
            Flags(t);
            Loops(t);
            Parts();
        }

        int Spares() { int m = 0; for (int i = 0; i < 3; i++) if (_van.SparePresent(i)) m |= 1 << i; return m; }

        /// <summary>Fuses in their clips and the spare tin, the battery in its box.</summary>
        void Parts()
        {
            for (int f = 0; f < 8; f++)
            {
                byte st = _van.FuseState(FuseIds[f]);
                if (st == _fuses[f]) continue;
                if (st == VanVehicle.FuseEmpty) Play(_fuses[f] == VanVehicle.FuseBlown ? "van.fuse.throw" : "van.fuse.out", FuseIds[f]);
                else if (_fuses[f] == VanVehicle.FuseEmpty) Play("van.fuse.in", FuseIds[f]);
                else if (st == VanVehicle.FuseBlown) Play("van.fuse.blow", FuseIds[f]);
                _fuses[f] = st;
            }
            int sp = Spares();
            if (sp != _spares) { Play("van.fuse.spare", "Spare_Fuse_1"); _spares = sp; }
            if (_van.BatteryIn != _battery) { Play(_van.BatteryIn ? "van.battery.in" : "van.battery.out", "Battery"); _battery = _van.BatteryIn; }
        }

        void Doors()
        {
            for (int d = 0; d < _doors.Length; d++)
            {
                NetworkDoor door = _van.doors[d];
                if (door == null) continue;
                DoorState s = door.State;
                if (s == _doors[d]) continue;
                string kind = door.doorId == "doorSalon" ? "side" : door.doorId.StartsWith("doorZ") || door.doorId.StartsWith("dooZ") ? "rear" : "cab";
                if (s == DoorState.Opening && _doors[d] == DoorState.Closed)
                {
                    Play($"van.door.{kind}.open", door.doorId);
                    if (!_van.Telemetry.Has(VanFlags.Engine) && Random.value < 0.25f) Play("van.door.frozen", door.doorId);   // the seal frozen to the frame
                }
                else if (s == DoorState.Closed) Play($"van.door.{kind}.close", door.doorId);
                else if (s == DoorState.Blocked) Play("van.door.blocked", door.doorId);
                _doors[d] = s;
            }
        }

        /// <summary>A control moved from position <paramref name="a"/> to <paramref name="b"/>.</summary>
        void Control(string zone, int a, int b)
        {
            bool up = b > a;
            switch (zone)
            {
                case "Glovebox_Lid": Play(up ? "van.glovebox.open" : "van.glovebox.close", zone); return;
                case "FuseBox_Lid": Play(up ? "van.fusebox.open" : "van.fusebox.close", zone); return;
                case "ToolBox_Lid": Play(up ? "van.toolbox.open" : "van.toolbox.close", zone); return;
                case "BatteryBox_Lid": Play(up ? "van.batterybox.open" : "van.batterybox.close", zone); return;
                case "EngineCover":
                    Play(a == 0 && b == 1 ? "van.enginecover.unlatch" : a == 1 && b == 2 ? "van.enginecover.raise" : "van.enginecover.close", zone);
                    return;
                case "FuelFlap_L":
                case "FuelFlap_R":
                    string cap = zone.Replace("FuelFlap", "FuelCap");
                    if (a == 0 && b == 1) Play("van.fuelflap.open", zone);
                    else if (a == 1 && b == 2) Play("van.fuelcap.off", cap);
                    else if (a == 2 && b == 3) Play("van.fuelcap.on", cap);
                    else Play("van.fuelflap.close", zone);
                    return;
                case "Seat_L4": Play(up ? "van.seat.fold" : "van.seat.unfold", zone); return;
                case "Curtain_L1A": Play(up ? "van.curtain.close" : "van.curtain.open", zone); return;
                case "Ignition_Key":
                    Play(a == 2 && b == 3 ? "van.ignition.key.start" : a == 3 && b == 2 ? "van.ignition.key.return" :
                         up ? "van.ignition.key.fwd" : "van.ignition.key.back", zone);
                    return;
                case "Horn_Button": return;                                  // the Horn flag sounds
                case "Heater_Fan": Play("van.heater.knob", zone); return;
                case "Radio_Volume":
                    Play(a == 0 ? "van.radio.on" : b == 0 ? "van.radio.off" : "van.radio.knob", zone);
                    return;
                case "Battery_ClampPos": Play(up ? "van.battery.clamps.off" : "van.battery.clamps.on", zone); return;
                case "Engine_Lead_1": Play(up ? "van.leads.off" : "van.leads.on", zone); return;
                case "Winch_Clutch": Play(up ? "van.winch.clutch.free" : "van.winch.clutch.engage", zone); return;
                case "MassSwitch_Key": Play(up ? "van.mass.on" : "van.mass.off", zone); return;
                case "Handbrake": return;                                    // the Handbrake flag sounds
                case "TransferLever_Axle": Play(up ? "van.transfer.axle.on" : "van.transfer.axle.off", zone); return;
                case "TransferLever_Range": Play(up ? "van.transfer.range.low" : "van.transfer.range.high", zone); return;
            }
            if (zone.StartsWith("Toggle_")) Play(up ? "van.toggle.on" : "van.toggle.off", zone);
        }

        void Flags(VanTelemetry t)
        {
            VanFlags now = t.Flags, was = _flags;
            bool Rose(VanFlags f) => (now & f) != 0 && (was & f) == 0;
            bool Fell(VanFlags f) => (now & f) == 0 && (was & f) != 0;
            if (Rose(VanFlags.Engine))
            {
                Play("van.engine.catch", "Audio_Engine");
                if (Random.value < 0.4f) Play("van.engine.belt", "Audio_Engine");            // a cold belt slips
                _engineSince = Time.time;
            }
            if (Fell(VanFlags.Engine))
            {
                bool stall = t.Has(VanFlags.Ignition) && Mathf.Abs(t.SpeedKmh) > 3f;      // died under load, key still on
                Play(stall ? "van.engine.stall" : "van.engine.stop", "Audio_Engine");
                _tickUntil = Time.time + 40f; _nextTick = Time.time + 3f;
            }
            bool cranking = t.Has(VanFlags.Starter) && !t.Has(VanFlags.Engine);
            if (cranking && _crankSince < 0f) { _crankSince = Time.time; _noStartHeard = false; }
            if (!cranking) _crankSince = -1f;
            if (cranking && !_noStartHeard && Time.time - _crankSince > 4f && t.BusVolts >= 8f)
            {
                _noStartHeard = true;
                Play("van.engine.nostart", "Audio_Engine");                                // coughs, does not catch
            }
            bool cold = t.Has(VanFlags.Engine) && Time.time - _engineSince < 90f;
            if (cold && Time.time >= _nextMisfire)
            {
                _nextMisfire = Time.time + Random.Range(6f, 20f);
                Play("van.engine.misfire", "Audio_Engine");
            }
            if (!t.Has(VanFlags.Engine) && Mathf.Abs(t.SpeedKmh) < 0.5f && Time.time >= _nextBody)
            {
                _nextBody = Time.time + Random.Range(25f, 70f);
                Play(Random.value < 0.2f ? "van.snow.slide" : "van.body.creak.cold", "Audio_Engine");   // the parked van in the frost
            }
            if (Rose(VanFlags.Starter) && t.BusVolts < 8f) Play("van.starter.click", "Audio_Engine");
            if (!t.Has(VanFlags.Engine) && Time.time < _tickUntil && Time.time >= _nextTick)
            {
                _nextTick = Time.time + Random.Range(2.5f, 7f);
                Play("van.engine.tick", "Audio_Engine");                        // hot metal cooling in the frost
            }
            if (Rose(VanFlags.Handbrake)) Play("van.handbrake.pull", "Handbrake");
            if (Fell(VanFlags.Handbrake)) Play("van.handbrake.release", "Handbrake");
            if (Rose(VanFlags.Hazard) || Fell(VanFlags.Hazard)) Play("van.hazard.switch", "Audio_Engine");
            if (t.Gear != _gear)
            {
                Play(t.Gear < 0 || _gear < 0 ? "van.gear.reverse" : t.Gear > _gear ? "van.gear.shift.up" : "van.gear.shift.down", "Audio_Engine");
                _gear = t.Gear;
            }
            if ((now & VanFlags.Hazard) != 0 && Time.time >= _hazardNext)
            {
                _hazardNext = Time.time + HazardPeriod / 2f;
                Play("van.hazard.relay", "Audio_Engine");
            }
            for (int i = 0; i < 4; i++)                                   // a fast travel change is a knock
            {
                float s = t.Travel(i);
                float jump = Mathf.Abs(s - _travel[i]);
                if (jump > 0.12f && Mathf.Abs(t.SpeedKmh) > 3f) Play("van.suspension.bottom", "Audio_Tyres");
                else if (jump > 0.05f && Mathf.Abs(t.SpeedKmh) > 3f) Play("van.suspension.bump", "Audio_Tyres");
                else if (jump > 0.015f && Mathf.Abs(t.SpeedKmh) < 1f && Time.time >= _nextCreak)
                {
                    _nextCreak = Time.time + 1.5f;
                    Play("van.suspension.creak", "Audio_Tyres");                  // someone climbs in, the van rocks
                }
                _travel[i] = s;
            }
            _flags = now;
        }

        void Loops(VanTelemetry t)
        {
            bool engine = t.Has(VanFlags.Engine), ign = t.Has(VanFlags.Ignition);
            float rpm01 = Mathf.InverseLerp(600f, 4200f, t.Rpm);
            float speed = Mathf.Abs(t.SpeedKmh);
            Loop(ref _engine, "van.engine.idle", "Audio_Engine", engine, 1f, 0.85f + rpm01 * 1.1f);
            Loop(ref _load, "van.engine.load", "Audio_Engine", engine && t.Gas > 20, Mathf.Clamp01(t.Gas / 255f + rpm01 * 0.5f), 0.8f + rpm01);
            Loop(ref _starter, "van.starter.crank", "Audio_Engine", t.Has(VanFlags.Starter) && t.BusVolts >= 8f, 1f, Mathf.Clamp(t.BusVolts / 12.6f, 0.6f, 1.1f));
            Loop(ref _horn, "van.horn", "Audio_Horn", t.Has(VanFlags.Horn), 1f, Mathf.Clamp(t.BusVolts / 12.6f, 0.8f, 1.05f));
            int fan = _van.Pos("Heater_Fan");
            Loop(ref _fan, "van.heater.fan", "Audio_Heater", ign && fan > 0, 0.5f + fan * 0.17f, 0.8f + fan * 0.12f);
            Loop(ref _wiper, "van.wiper.motor", "Audio_Wipers", ign && _van.CircuitOn("Toggle_Wiper", "Fuse_8"));
            Loop(ref _radio, "van.radio.static", "Audio_Radio", _van.Pos("Radio_Volume") > 0 && t.BusVolts > 9f, _van.Pos("Radio_Volume") / 10f);
            if (Time.time >= _surfaceAt)                                        // what the wheels roll on (this peer's own look)
            {
                _surfaceAt = Time.time + 0.5f;
                string surf = SurfaceResolver.At(transform.position, transform);
                _surface = surf;
                _tyre = surf == "Ice" || surf == "Frozen" ? "van.tyre.ice" : surf == "SnowDeep" ? "van.tyre.loosesnow"
                      : surf == "Asphalt" || surf == "Concrete" || surf == "Tile" || surf == "Gravel" || surf == "Rock" ? "van.tyre.road"
                      : surf == "Dirt" || surf == "Mud" ? "van.tyre.sandtrack" : "van.tyre.packedsnow";
            }
            // the wheels' own speed against the van's (both replicated): spinning or locked (≈0.39 m wheel radius)
            float ground = speed / 3.6f, wheel = 0f;
            for (int i = 0; i < 4; i++) wheel = Mathf.Max(wheel, Mathf.Abs(t.Omega(i)) * 0.39f);
            Loop(ref _spin, "van.tyre.spin", "Audio_Tyres", wheel > ground + 3f && t.Gas > 60, Mathf.Clamp01((wheel - ground) / 8f));
            Loop(ref _skid, "van.tyre.skid", "Audio_Tyres", ground > 3f && wheel < 0.5f, Mathf.Clamp01(ground / 10f));
            Loop(ref _planks, "road.bridge.planks.wheel", "Audio_Tyres", speed > 1.5f && _surface == "Wood", Mathf.Clamp01(speed / 30f));
            foreach (string id in new[] { "van.tyre.packedsnow", "van.tyre.ice", "van.tyre.loosesnow", "van.tyre.road", "van.tyre.sandtrack" })
            {
                _tyreLoops.TryGetValue(id, out SoundLoop tl);
                Loop(ref tl, id, "Audio_Tyres", speed > 1.5f && id == _tyre, Mathf.Clamp01(speed / 50f), 0.8f + Mathf.Clamp01(speed / 80f) * 0.5f);
                if (tl != null) _tyreLoops[id] = tl;
            }
            Loop(ref _wind, "van.wind.drive", "Audio_Engine", speed > 25f, Mathf.Clamp01((speed - 25f) / 60f));
            Loop(ref _cold, "van.engine.idle.cold", "Audio_Engine", engine && Time.time - _engineSince < 90f && t.Gas <= 20, 0.8f, 0.9f);
            Loop(ref _offload, "van.engine.offload", "Audio_Engine", engine && t.Gas <= 20 && rpm01 > 0.3f, rpm01, 0.8f + rpm01);
            Loop(ref _limiter, "van.engine.limiter", "Audio_Engine", engine && t.Rpm > 4150f);
            Loop(ref _whine, "van.gear.whine", "Audio_Engine", speed > 5f, Mathf.Clamp01(speed / 60f) * (t.Has(VanFlags.LowRange) ? 1f : 0.6f), 0.7f + Mathf.Clamp01(speed / 80f) * 0.6f);
            Loop(ref _rattle, "van.body.rattle", "Audio_Engine", speed > 10f, Mathf.Clamp01(speed / 60f));
            Loop(ref _seals, "van.wind.seals", "Audio_Engine", speed < 5f, 0.4f);
            if (t.Brake > 100 && speed > 2f && speed < 15f && Time.time >= _nextSqueal)
            {
                _nextSqueal = Time.time + 3f;
                Play("van.brake.squeal", "Audio_Tyres");
            }
            bool wipers = ign && _van.CircuitOn("Toggle_Wiper", "Fuse_8");
            if (wipers && Time.time >= _nextSweep) { _nextSweep = Time.time + 0.75f; Play("van.wiper.sweep", "Audio_Wipers"); }
            if (_wiperOn && !wipers) Play("van.wiper.park", "Audio_Wipers");
            _wiperOn = wipers;
        }
    }
}
