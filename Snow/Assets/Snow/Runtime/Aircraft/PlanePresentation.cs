using System.Collections.Generic;
using Snow.Audio;
using Snow.Interaction;
using Snow.Player;
using Snow.Seats;
using Snow.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UtilityPlane;

namespace Snow.Aircraft
{
    /// <summary>
    /// Replicated state → UtilityPlaneRig fields on every peer (§6 PlanePresentation, VIEW-01). It never computes
    /// physics: surfaces show the actuator positions, needles the sensor readings, lamps and lights what the systems give.
    /// Also the stall warning horn (§10.1) and, on the dev pult (~) page «Самолёт», the developer telemetry (not a HUD).
    /// For the local player in this plane: the start checklist ticked from the real switch and engine state — by default
    /// one line at the top with the next step, H opens the whole card, H again hides it (UI pick 23.10, version 3) — and
    /// the next control of the checklist outlined in the cockpit while help shows (29.09 "не знаю как завести").
    ///
    /// Presentation smoothing only (30.09 "интерполяция для объектов с анимацией"): the replicated values arrive at the
    /// network tick (30 Hz) and discrete controls change in one step, so everything that moves is eased here per frame:
    /// continuous channels (surfaces, gear, yoke, levers) follow with a short lag or a travel speed, two-position
    /// controls (toggles, fuel selectors, flap lever) are swung over a fraction of a second. The state itself and the
    /// physics are untouched; the lag is at most about one network tick.
    /// </summary>
    [RequireComponent(typeof(UtilityPlaneRig))]
    public class PlanePresentation : MonoBehaviour
    {
        public PlaneVehicle vehicle;
        [Tooltip("Cabin audio point (Socket_Audio_Cabin)")] public Transform cabinAudio;
        [Tooltip("deg of trim wheel rotation at full trim")] public float trimWheelTravel = 540f;

        [Header("Smoothing (presentation only, TEMPORARY)")]
        [Tooltip("s, lag for values sampled at the network tick (surfaces, gear, levers)")] public float sampleLag = 0.05f;
        [Tooltip("yoke and pedal travel per second, of the -1..1 range (keyboard steps become a throw)")] public float yokeSpeed = 5f;
        [Tooltip("deg/s a toggle switch swings (0 → 40° in 0.1 s)")] public float toggleSpeed = 400f;
        [Tooltip("deg/s of the fuel selectors (0 → 60°)")] public float selectorSpeed = 240f;
        [Tooltip("deg/s of the flap lever between detents")] public float flapLeverSpeed = 60f;
        [Tooltip("pull knob travel per second, 0..1")] public float pullSpeed = 4f;
        [Tooltip("lever travel per second for prop and condition, of the -1..1 range")] public float leverSpeed = 5f;
        [Tooltip("rheostat travel per second, 0..1")] public float rheoSpeed = 3f;

        UtilityPlaneRig _rig;
        uint _shownBits = uint.MaxValue;
        AudioSource _horn;
        bool _seatedHere, _pilotHere, _hinting;
        int _helpMode;                       // 0 = the next step in one line, 1 = the whole card, 2 = hidden (H cycles)
        bool HelpHidden => _helpMode == 2;
        bool _apShown;
        readonly List<(string id, string text, bool done)> _steps = new List<(string, string, bool)>();
        readonly Dictionary<string, float> _eased = new Dictionary<string, float>();
        readonly List<(Transform t, Quaternion rest, PlaneSwitch bit)> _toggles = new List<(Transform, Quaternion, PlaneSwitch)>();
        Transform _fuelSelL, _fuelSelR, _flapLever;
        Quaternion _fuelSelLRest, _fuelSelRRest, _flapLeverRest;

        static readonly (PlaneSwitch bit, string name)[] SwitchObjects =
        {
            (PlaneSwitch.Battery, "SW_Battery"), (PlaneSwitch.Avionics, "SW_Avionics"), (PlaneSwitch.FuelPump, "SW_FuelPump"),
            (PlaneSwitch.Ignition, "SW_Ignition"), (PlaneSwitch.Starter, "SW_Starter"), (PlaneSwitch.Beacon, "SW_Beacon"),
            (PlaneSwitch.Strobe, "SW_Strobe"), (PlaneSwitch.NavLights, "SW_NavLights"), (PlaneSwitch.Landing, "SW_Landing"),
            (PlaneSwitch.CargoDoorL, "SW_CargoDoor_L"), (PlaneSwitch.CargoDoorR, "SW_CargoDoor_R"),
            (PlaneSwitch.CabinLight, "SW_CabinLight"), (PlaneSwitch.CargoLight, "SW_CargoLight"),
            (PlaneSwitch.CabinDoorL, "SW_CabinDoor_L"), (PlaneSwitch.CabinDoorR, "SW_CabinDoor_R"),   // v7, И2 E.1
        };

        void Awake()
        {
            _rig = GetComponent<UtilityPlaneRig>();
            if (vehicle == null) vehicle = GetComponent<PlaneVehicle>();
            if (GetComponent<PlaneSounds>() == null) gameObject.AddComponent<PlaneSounds>();   // sounds from the same state
            // parts whose Rig channel is two-position: the eased angle is applied after the Rig in LateUpdate
            var byName = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            foreach (var (bit, name) in SwitchObjects)
                if (byName.TryGetValue(name, out var sw)) _toggles.Add((sw, sw.localRotation, bit));
            if (byName.TryGetValue("FuelSel_L", out _fuelSelL)) _fuelSelLRest = _fuelSelL.localRotation;
            if (byName.TryGetValue("FuelSel_R", out _fuelSelR)) _fuelSelRRest = _fuelSelR.localRotation;
            if (byName.TryGetValue("Lever_Flap", out _flapLever)) _flapLeverRest = _flapLever.localRotation;
            // the Rig turns the gear compression fraction into the leg angle itself (N4): it only needs the travel
            if (vehicle != null && vehicle.tuning != null)
            {
                _rig.mainGearTravel = vehicle.tuning.mainTravel;
                _rig.tailGearTravel = vehicle.tuning.tailTravel;
            }
            // v11 lights in the air: glow and beams follow the scene fog (Sky Island ≈ 0.14 at the ground); no fog = atmosphere
            _rig.atmosphereFromSceneFog = true;
            if (cabinAudio != null && !Application.isBatchMode)
            {
                _horn = cabinAudio.gameObject.AddComponent<AudioSource>();
                _horn.clip = SnowAudio.Clip("plane.stall.horn");
                _horn.loop = true;
                _horn.spatialBlend = 1f;
                _horn.minDistance = 1f;
                _horn.maxDistance = 10f;
                _horn.volume = 0.35f;
                _horn.playOnAwake = false;
            }
        }

        void Update()
        {
            if (vehicle == null || !vehicle.IsSpawned) return;
            PlaneTelemetry t = vehicle.Telemetry;
            PlaneSwitchState s = vehicle.Switches;
            ControlState c = vehicle.Controls;
            UtilityPlaneRig r = _rig;

            // exterior: tick-sampled values eased per frame
            float lag = sampleLag;
            r.elevatorDeg = Lag("elev", t.Elevator, lag); r.aileronDeg = Lag("ail", t.Aileron, lag);
            r.rudderDeg = Lag("rud", t.Rudder, lag); r.flapDeg = Lag("flap", t.Flap, lag);
            r.propRpm = Lag("prop", t.PropRpm, lag);
            r.mainWheelSpeed = Lag("wheelM", t.WheelMain, lag); r.tailWheelSpeed = Lag("wheelT", t.WheelTail, lag);
            r.gearCompressionL = Lag("gearL", t.GearL, lag); r.gearCompressionR = Lag("gearR", t.GearR, lag);
            r.tailGearCompression = Lag("gearT", t.GearT, lag);
            r.tailWheelSteerDeg = Lag("steer", t.TailSteer, lag);
            if (vehicle.pilotDoorL != null) r.pilotDoorL = vehicle.pilotDoorL.ShownPosition;
            if (vehicle.pilotDoorR != null) r.pilotDoorR = vehicle.pilotDoorR.ShownPosition;
            if (vehicle.cargoDoorL != null) r.cargoDoorL = vehicle.cargoDoorL.ShownPosition;
            if (vehicle.cargoDoorR != null) r.cargoDoorR = vehicle.cargoDoorR.ShownPosition;
            r.fuelCapL = Mathf.MoveTowards(r.fuelCapL, s.On(PlaneSwitch.FuelCapL) ? 1f : 0f, 2f * Time.deltaTime);
            r.fuelCapR = Mathf.MoveTowards(r.fuelCapR, s.On(PlaneSwitch.FuelCapR) ? 1f : 0f, 2f * Time.deltaTime);

            // cockpit controls: both yokes and pedal sets follow the pilot's (M6: kept in sync)
            // keyboard axes are -1/0/+1 steps: the yoke and pedals are thrown at a travel speed, then eased
            r.yokePitch = Lag("yokeP", Rate("yokePr", c.Pitch, yokeSpeed), lag);
            r.yokeRoll = Lag("yokeR", Rate("yokeRr", c.Roll, yokeSpeed), lag);
            r.pedals = Lag("pedals", Rate("pedalsR", c.Yaw, yokeSpeed), lag);
            r.powerLever = Lag("power", c.Throttle * 2f - 1f, lag);
            r.propLever = Rate("propLever", s.PropLever * 2f - 1f, leverSpeed);
            r.conditionLever = Rate("condition", s.On(PlaneSwitch.ConditionCutOff) ? -1f : 1f, leverSpeed);
            r.flapLever = s.FlapLever;                                      // eased angle applied in LateUpdate
            r.trimWheelDeg = Lag("trim", c.Trim * trimWheelTravel, lag);
            r.visorP = Mathf.MoveTowards(r.visorP, s.On(PlaneSwitch.VisorP) ? 1f : 0f, 3f * Time.deltaTime);
            r.visorC = Mathf.MoveTowards(r.visorC, s.On(PlaneSwitch.VisorC) ? 1f : 0f, 3f * Time.deltaTime);
            r.beltP = s.On(PlaneSwitch.BeltP); r.beltC = s.On(PlaneSwitch.BeltC);
            r.annunciatorTestPressed = s.On(PlaneSwitch.AnnunTest);
            r.pullCabinHeat = Rate("pullHeat", s.On(PlaneSwitch.CabinHeat) ? 1f : 0f, pullSpeed);
            r.pullDefrost = Rate("pullDefrost", s.On(PlaneSwitch.Defrost) ? 1f : 0f, pullSpeed);
            r.pullParkBrake = Rate("pullPark", s.On(PlaneSwitch.ParkBrake) ? 1f : 0f, pullSpeed);
            r.pullFuelShutoff = Rate("pullShutoff", s.On(PlaneSwitch.FuelShutoff) ? 1f : 0f, pullSpeed);
            r.fuelSelL = s.On(PlaneSwitch.FuelSelL); r.fuelSelR = s.On(PlaneSwitch.FuelSelR);
            r.mapCase = Mathf.MoveTowards(r.mapCase, s.On(PlaneSwitch.Glovebox) ? 1f : 0f, 2.5f * Time.deltaTime);   // glovebox lid (И2 C.5)
            // v7 knobs (И2 C.7–C.8, F): the Rig turns ALT and HDG knobs after the values itself (autoKnobs)
            r.altimeterSettingInHg = s.AltSettingInHg;
            r.headingBugDeg = s.HdgBug;
            r.dgSetKnobDeg = Rate("dgKnob", s.DgKnob * 15f, 720f);
            for (int i = 0; i < PlaneSwitchState.RadioKnobCount && i < r.radioKnobDeg.Length; i++)
                r.radioKnobDeg[i] = Mathf.MoveTowardsAngle(r.radioKnobDeg[i], s.RadioKnob(i) * 15f, 360f * Time.deltaTime);
            r.xpdrMode = s.Xpdr;
            // troop seats fold over paxFoldTime (a view: sitting follows the state at once, И3 K.5); straps over the ladder
            float fold = Time.deltaTime / Mathf.Max(0.05f, vehicle.paxFoldTime);
            for (int i = 0; i < 4 && i < r.paxSeatDown.Length; i++)
                r.paxSeatDown[i] = Mathf.MoveTowards(r.paxSeatDown[i], s.PaxDown(i) ? 1f : 0f, fold);
            r.ladderStowed = s.LadderStowed;
            if (s.Bits != _shownBits)
            {
                _shownBits = s.Bits;
                List<string> on = r.switchesOn;
                on.Clear();
                foreach (var (bit, name) in SwitchObjects) if (s.On(bit)) on.Add(name);
            }

            // instruments: sensor readings
            r.iasKt = t.IasKt; r.altitudeFt = t.AltFt; r.vsiFpm = t.VsiFpm; r.pitchDeg = t.PitchDeg; r.bankDeg = t.BankDeg;
            r.headingDeg = t.HeadingDeg; r.turnRateDegS = t.TurnRate; r.slip = t.Slip;
            r.torqueFtLb = t.TorqueFtLb; r.propRpmGauge = t.PropGauge; r.ittC = t.IttC; r.ngPct = t.NgPct;
            r.oilPsi = t.OilPsi; r.oilC = t.OilC; r.fuelFlowPph = t.FuelFlowPph; r.fuelLGal = t.FuelLGal; r.fuelRGal = t.FuelRGal;
            r.amps = t.Amps; r.oatC = t.OatC;
            r.busVolts = t.BusVolts; r.suctionInHg = t.SuctionInHg; r.compassHeadingDeg = t.CompassDeg;   // И2 C.3, C.4, C.9
            r.windshieldFog = t.FogWindshield; r.sideWindowFog = t.FogSide;                                 // И2 G.1
            System.DateTime now = System.DateTime.Now;                    // TEMPORARY: wall clock until game time exists
            r.clockHours = now.Hour + now.Minute / 60f;
            r.clockMinutes = now.Minute + now.Second / 60f;

            // lamps and lights
            r.busPowered = t.BusPowered;
            for (int i = 0; i < 12 && i < r.lamps.Length; i++) r.lamps[i] = (t.Lamps & (1 << i)) != 0;
            r.navLights = t.Nav / 255f; r.strobeLights = t.Strobe / 255f; r.beaconLights = t.Beacon / 255f;
            r.landingLight = t.LandingLight / 255f; r.cabinLight = t.Cabin / 255f; r.cargoLight = t.Cargo / 255f;
            r.panelLight = t.Panel / 255f; r.floodLight = t.Flood / 255f;
            r.avionicsPower = t.Avionics / 255f;                         // radio legends and the idle glow of the screens

            if (_horn != null)
            {
                if (t.StallWarning && !_horn.isPlaying) _horn.Play();
                else if (!t.StallWarning && _horn.isPlaying) _horn.Stop();
            }
            BenchView(s, t);
            UpdateHelp(s, t);

            // autopilot on/off note for the players in this plane (the model has no autopilot annunciator)
            if (t.ApEngaged != _apShown)
            {
                _apShown = t.ApEngaged;
                PlayerNet local = PlayerNet.Local;
                if (_seatedHere && local != null && local.sensor != null)
                    local.sensor.ShowMessage(t.ApEngaged ? $"Автопилот ВКЛ: курс {t.ApHeading:000}°, высота {t.ApAltFt:0} ft" : Autopilot.ReasonText(t.ApReason), 3f);
            }
        }

        // after UtilityPlaneRig.Update has snapped the two-position parts: swing them to the eased angle (Rig axes and
        // signs, UNITY_CONTRACT §4: toggles +40° = on, fuel selectors +60° = off, flap lever −12° per detent). Written
        // only while the part moves (J.2, contract item 45): at rest it is where the Rig put it, the same end angle.
        void LateUpdate()
        {
            if (vehicle == null || !vehicle.IsSpawned) return;
            PlaneSwitchState s = vehicle.Switches;
            foreach (var (t, rest, bit) in _toggles)
                if (Swing(t.name, s.On(bit) ? 40f : 0f, toggleSpeed, out float a)) t.localRotation = rest * Quaternion.Euler(a, 0f, 0f);
            if (_fuelSelL != null && Swing("selL", s.On(PlaneSwitch.FuelSelL) ? 0f : 60f, selectorSpeed, out float l))
                _fuelSelL.localRotation = _fuelSelLRest * Quaternion.Euler(l, 0f, 0f);
            if (_fuelSelR != null && Swing("selR", s.On(PlaneSwitch.FuelSelR) ? 0f : 60f, selectorSpeed, out float r))
                _fuelSelR.localRotation = _fuelSelRRest * Quaternion.Euler(r, 0f, 0f);
            if (_flapLever != null && Swing("flapLever", -12f * s.FlapLever, flapLeverSpeed, out float f))
                _flapLever.localRotation = _flapLeverRest * Quaternion.Euler(f, 0f, 0f);
        }

        // eased angle of a two-position part; true while it moves, including the frame it arrives
        bool Swing(string key, float target, float perSecond, out float angle)
        {
            bool known = _eased.TryGetValue(key, out float before);
            angle = Rate(key, target, perSecond);
            return known && before != target;
        }

        float _nextView;

        /// <summary>
        /// Bench evidence (I-30, KX5): once a second, what this peer shows of the replicated plane state — switches, troop
        /// seats, ladder, winter kit, glass fog, the key holder, stowed items. Only in bench runs with a log file.
        /// </summary>
        void BenchView(PlaneSwitchState s, PlaneTelemetry t)
        {
            var args = Snow.Net.SnowNetworkBootstrap.Args;
            if (args == null || string.IsNullOrEmpty(args.LogPath) || Time.unscaledTime < _nextView) return;
            _nextView = Time.unscaledTime + 1f;
            var kit = GetComponent<PlaneWinterKitNet>();
            ulong keyHolder = PlaneKey.All.Count > 0 ? PlaneKey.All[0].Holder : PlaneKey.None;
            int stowed = 0;
            foreach (var c in Snow.Cargo.CargoItem.All) if (c.IsSpawned && c.State == Snow.Cargo.CargoState.Stowed) stowed++;
            Snow.Net.BenchLog.Event("plane_view", "bits", s.Bits, "pax", s.PaxSeats, "ladder", s.LadderStowed, "alt", s.AltSetting,
                "xpdr", s.Xpdr, "radioA", s.RadioA, "kit", kit != null ? kit.Mask : -1, "kitShown", kit != null && kit.Kit != null ? kit.Kit.Mask : -1,
                "fogW", _rig.windshieldFog, "fogS", _rig.sideWindowFog, "paxShown", _rig.paxSeatDown[0] + _rig.paxSeatDown[1] + _rig.paxSeatDown[2] + _rig.paxSeatDown[3],
                "mapCase", _rig.mapCase, "key", keyHolder == PlaneKey.None ? -1L : (long)keyHolder, "stowed", stowed,
                "doorL", vehicle.pilotDoorL != null ? vehicle.pilotDoorL.ShownPosition : -1f);
        }

        /// <summary>
        /// Dev only (И2 G.3 TEMPORARY, who prints on the glass is OPEN): a palm where the local player looks if a fogged
        /// pane is within 1 m (RaycastFog: the cabin colliders sit 5-20 cm in front of the glass), or wipe all prints. The
        /// server sends the print to every peer.
        /// </summary>
        void DevFogPrint(bool wipe)
        {
            PlayerNet local = PlayerNet.Local;
            if (local == null || local.view == null) return;
            if (wipe) { vehicle.RequestFogPrint(Vector3.zero, true); return; }
            var ray = new Ray(local.view.EyePosition, local.view.EyeRotation * Vector3.forward);
            if (_rig.RaycastFog(ray, 1f, out Vector3 point)) vehicle.RequestFogPrint(transform.InverseTransformPoint(point), false);
            else if (local.sensor != null) local.sensor.ShowMessage("Нет стекла ближе 1 м");
        }

        // eased values keyed by channel; the first sample snaps (no swing from zero on spawn or late join)
        float Lag(string key, float target, float tau)
        {
            if (!_eased.TryGetValue(key, out float x)) x = target;
            else x += (target - x) * (1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, tau)));
            _eased[key] = x;
            return x;
        }

        float Rate(string key, float target, float perSecond)
        {
            float x = _eased.TryGetValue(key, out float v) ? Mathf.MoveTowards(v, target, perSecond * Time.deltaTime) : target;
            _eased[key] = x;
            return x;
        }

        // ---------------------------------------------------------------- help card and next-step outline (local player)
        void UpdateHelp(PlaneSwitchState s, PlaneTelemetry t)
        {
            PlayerNet local = PlayerNet.Local;
            SeatAnchor seat = local != null && local.seatController != null ? local.seatController.Seat : null;
            _seatedHere = seat != null && seat.GetComponentInParent<PlaneVehicle>() == vehicle;
            _pilotHere = _seatedHere && seat.role == SeatRole.Pilot;
            if (_seatedHere && Keyboard.current != null && !Snow.Input.SnowInput.UiOpen && Keyboard.current.hKey.wasPressedThisFrame) _helpMode = (_helpMode + 1) % 3;

            _steps.Clear();
            bool running = t.EngineRunning;
            _steps.Add(("SW_Battery", "АКБ — включить", s.On(PlaneSwitch.Battery)));
            _steps.Add(("FuelSel_L", "Краны баков — открыты (хотя бы один)", s.On(PlaneSwitch.FuelSelL) || s.On(PlaneSwitch.FuelSelR)));
            _steps.Add(("Pull_FuelShutoff", "Перекрывной кран (красная тяга) — утоплен", !s.On(PlaneSwitch.FuelShutoff)));
            _steps.Add(("Lever_Condition", "Рычаг останова — МАЛЫЙ ГАЗ", !s.On(PlaneSwitch.ConditionCutOff)));
            _steps.Add(("SW_FuelPump", "Насос — включить", s.On(PlaneSwitch.FuelPump)));
            _steps.Add(("SW_Ignition", "Зажигание — включить", s.On(PlaneSwitch.Ignition)));
            _steps.Add(("SW_Starter", $"СТАРТЕР — держать ЛКМ, пока Ng не дойдёт до 52 %  (сейчас {t.NgPct:0} %)", running && t.NgPct >= 51f));
            _steps.Add(("Lever_Power", "Газ — Shift чуть вперёд, пока не погаснет GEN OFF (Ng > 55 %)", running && (t.Lamps & (1 << 4)) == 0));
            _steps.Add(("Pull_ParkBrake", "Стояночный тормоз — утопить, можно рулить", !s.On(PlaneSwitch.ParkBrake)));

            // outline the next control for the pilot while the card is shown
            HoverOutline outline = local != null ? local.GetComponent<HoverOutline>() : null;
            if (outline == null) return;
            UtilityPlane.UtilityPlaneInteractable next = null;
            if (_pilotHere && !HelpHidden)
                foreach (var step in _steps)
                    if (!step.done) { vehicle.TryGetVolume(step.id, out next); break; }
            if (next != null) { outline.SetHint(next); _hinting = true; }
            else if (_hinting) { outline.SetHint(null); _hinting = false; }
        }

        static string Key(string action)
        {
            PlayerNet local = PlayerNet.Local;
            return local != null && local.humanInput != null ? local.humanInput.KeyName(action) : "?";
        }

        static readonly (string key, string text)[] HelpKeysLine = { ("H", "весь список") };
        static readonly (string key, string text)[] HelpKeysCard = { ("H", "скрыть") };

        // UI pick 23.10, version 3 (Hud look): one line at the top with the next start step; H — the whole card; H — hidden
        void HelpCard()
        {
            if (!_seatedHere || HelpHidden || Event.current.type != EventType.Repaint) return;
            if (_helpMode == 0) HelpLine();
            else HelpFull();
        }

        void HelpLine()
        {
            int done = 0, next = -1;
            for (int i = 0; i < _steps.Count; i++)
            {
                if (_steps[i].done) done++;
                else if (next < 0) next = i;
            }
            string lead = null, text;
            if (!_pilotHere) text = Loc.T("Правое кресло: полётом управляет только левое. Тумблеры и двери — можно.");
            else if (next < 0) return;                                        // the engine runs: nothing to say until H
            else
            {
                lead = $"{done} / {_steps.Count}";                               // steps done (they are not done in order)
                text = Loc.T("Следующее:") + " " + Loc.T(_steps[next].text);
            }
            GUIStyle leadSt = Hud.Text(16, true), st = Hud.Text(22);
            float pad = Hud.Px(18f), sp = Hud.Px(14f), h = Hud.Px(44f);
            float lw = lead != null ? Hud.Size(lead, leadSt).x + sp : 0f, tw = Hud.Size(text, st).x;
            float kw = Hud.Hints(0f, 0f, h, HelpKeysLine, 16, 1f, false);
            float w = pad * 2f + lw + tw + sp + kw;
            var r = new Rect(Mathf.Round((Screen.width - w) * 0.5f), Hud.SafeY, w, h);
            Hud.Panel(r);
            float x = r.x + pad;
            if (lead != null)
            {
                Hud.Label(new Rect(x, r.y, lw, h), lead, leadSt, Hud.Warm);
                x += lw;
            }
            Hud.Label(new Rect(x, r.y, tw + 2f, h), text, st, Color.white);
            Hud.Hints(x + tw + sp, r.y, h, HelpKeysLine, 16, 1f);
        }

        void HelpFull()
        {
            float w = Mathf.Min(Hud.Px(700f), Screen.width - 2f * Hud.SafeX), pad = Hud.Px(20f), rowH = Hud.Px(28f), headH = Hud.Px(30f);
            float iw = w - 2f * pad, box = Hud.Px(20f);
            GUIStyle head = Hud.Text(20, true), row = Hud.Text(18), rowNext = Hud.Text(18, true), cap = Hud.Text(13);
            GUIStyle tail = Hud.Text(16, false, TextAnchor.UpperLeft, true);
            string tailText = Loc.T(HelpTail());
            float tailH = tail.CalcHeight(new GUIContent(tailText), iw);
            int rows = _pilotHere ? _steps.Count : 0;
            float h = pad + headH + (rows > 0 ? Hud.Px(24f) + rows * rowH : 0f) + Hud.Px(12f) + tailH + pad;
            var r = new Rect(Screen.width - Hud.SafeX - w, Hud.SafeY, w, h);
            Hud.Panel(r);
            float x = r.x + pad, y = r.y + pad;
            Hud.Label(new Rect(x, y, iw, headH), Loc.T("Подсказка"), head, Color.white);
            float kw = Hud.Hints(0f, 0f, headH, HelpKeysCard, 16, 1f, false);
            Hud.Hints(r.xMax - pad - kw, y, headH, HelpKeysCard, 16, 1f);
            y += headH;
            if (rows > 0)
            {
                Hud.Label(new Rect(x, y, iw, Hud.Px(24f)), Loc.T("Запуск двигателя (следующий орган подсвечен)").ToUpperInvariant(), cap, Hud.Muted);
                y += Hud.Px(24f);
                bool nextMarked = false;
                foreach (var step in _steps)
                {
                    bool next = !step.done && !nextMarked;
                    if (!step.done) nextMarked = true;
                    var b = new Rect(x, y + Mathf.Round((rowH - box) * 0.5f), box, box);
                    if (step.done)
                    {
                        Hud.Fill(b, new Color(Hud.Warm.r, Hud.Warm.g, Hud.Warm.b, 0.2f));
                        float k = Hud.Px(6f);
                        Hud.Fill(new Rect(b.x + k, b.y + k, b.width - 2f * k, b.height - 2f * k), Hud.Warm);
                    }
                    else Hud.Outline(b, next ? Hud.Warm : new Color(1f, 1f, 1f, 0.35f), Mathf.Max(1f, Hud.Px(next ? 2f : 1.5f)));
                    Color c = next ? Color.white : step.done ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.85f);
                    Hud.Label(new Rect(x + box + Hud.Px(10f), y, iw - box - Hud.Px(10f), rowH), Loc.T(step.text), next ? rowNext : row, c);
                    y += rowH;
                }
            }
            y += Hud.Px(12f);
            Hud.Label(new Rect(x, y, iw, tailH), tailText, tail, new Color(1f, 1f, 1f, 0.85f));
        }

        // the flight and cabin keys under the start list (same lines as the old card)
        string HelpTail()
        {
            var sb = new System.Text.StringBuilder();
            if (_pilotHere)
            {
                sb.Append("Полёт: W/S — штурвал от себя / на себя, A/D — крен, Q/E — педали,\n");
                sb.Append("Shift / Ctrl — газ, Space — тормоза, [ ] — триммер, закрылки — рычаг курсором.\n");
                sb.Append(vehicle.Telemetry.ApEngaged
                    ? $"{Key("Seat/Autopilot")} — автопилот: ВКЛ, держит курс {vehicle.Telemetry.ApHeading:000}° и {vehicle.Telemetry.ApAltFt:0} ft (штурвал или Z — выключить)\n"
                    : $"{Key("Seat/Autopilot")} — автопилот: держит курс и высоту на момент включения (в полёте, нужна авионика)\n");
            }
            else sb.Append("Правое кресло: полётом управляет только левое. Тумблеры и двери — можно.\n");
            sb.Append("\nTab — курсор (ЛКМ — нажать, держать — для стартера) / обзор мышью\n");
            sb.Append("F — использовать то, на что смотрите;  держать F — выйти (дверь должна быть открыта)\n");
            sb.Append($"{Key("Seat/Reveal")} — показать всё, с чем можно взаимодействовать;  {Key("Seat/Zoom")} — приблизить;  ~ — телеметрия (дев-пульт)");
            return sb.ToString();
        }

        void OnGUI()
        {
            if (vehicle == null || !vehicle.IsSpawned) return;
            HelpCard();
        }

        // ------------------------------------------------------------------ dev pult page «Самолёт» (pin it: telemetry over the game)
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        System.Action _page;

        void OnEnable() => Snow.Dev.DevPult.Add("Самолёт", 60, _page ??= DrawPage, () => vehicle != null && vehicle.IsSpawned);
        void OnDisable() => Snow.Dev.DevPult.Remove("Самолёт", _page);

        void DrawPage()
        {
            PlaneTelemetry t = vehicle.Telemetry;
            PlaneSwitchState s = vehicle.Switches;
            ControlState c = vehicle.Controls;
            EnvironmentProvider env = EnvironmentProvider.Current;
            string wind = env != null ? $"{env.Now.SpeedMs / EnvironmentProvider.Kt:0} уз с {env.Now.FromDeg:0}°, порывы +{env.Now.GustMs / EnvironmentProvider.Kt:0}, турб. {env.Now.TurbulenceMs:0.0}" : "нет";
            Snow.Dev.DevUi.Text(
                $"IAS {t.IasKt:0} kt   TAS {t.TrueAirspeed * 1.944f:0} kt   GS {t.GroundSpeed * 1.944f:0} kt   AoA {t.AlphaDeg:0.0}°\n" +
                $"Alt {t.AltFt:0} ft   AGL {t.HeightAgl:0.0} m   VSI {t.VsiFpm:0} fpm   pitch {t.PitchDeg:0} bank {t.BankDeg:0}\n" +
                $"Ng {t.NgPct:0.0}%   ITT {t.IttC:0}°C   TRQ {t.TorqueFtLb:0}   prop {t.PropRpm:0}   FF {t.FuelFlowPph:0} pph   {(t.EngineRunning ? "РАБОТАЕТ" : "не работает")}\n" +
                $"шина {t.BusVolts:0.0} В   ток {t.Amps:0} А   заряд {t.BatteryCharge * 100f:0}%   топливо {t.FuelTotalL:0} л   масса {t.MassKg:0} кг\n" +
                $"вакуум {t.SuctionInHg:0.0}   компас {t.CompassDeg:000}°   запотевание {t.FogWindshield * 100f:0} / {t.FogSide * 100f:0} %\n" +
                $"газ {c.Throttle * 100f:0}%   тормоз {c.Brake:0.0}   триммер {c.Trim:0.00}   закрылки {t.Flap:0}°   стоян. тормоз {(s.On(PlaneSwitch.ParkBrake) ? "ВЫТЯНУТ" : "нет")}\n" +
                $"ветер {wind}");
            if (Snow.Dev.DevUi.Button("Отпечаток на стекле (куда смотрю)")) DevFogPrint(false);
            Snow.Dev.DevUi.Row();
            if (Snow.Dev.DevUi.Button("Стереть отпечатки")) DevFogPrint(true);
            Snow.Dev.DevUi.Note("Запуск: Tab — курсор. АКБ ВКЛ → краны баков открыты, перекрывной утоплен → насос ВКЛ → зажигание ВКЛ →\n" +
                                "держать СТАРТЕР (ЛКМ) до Ng ≥ 52% → отпустить. Стояночный тормоз — тяга Pull_ParkBrake. Тормоза — Space, триммер — [ ].");
        }
#endif
    }
}
