using System.Globalization;
using Snow.Cargo;
using Snow.Input;
using Snow.Interaction;
using Snow.Net;
using Snow.Player;
using Snow.Seats;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Dev
{
    /// <summary>
    /// Scripted input for bench runs without a keyboard (headless builds). -snow-bot takes steps separated by ';':
    ///   wait:S                wander:YAW:S[:sprint]      goto:X:Z[:TOL]      face:YAW
    ///   look:ID               lookat:X:Y:Z               use                  exit
    ///   axes:P:R:Y:T:S        forceaxes:P:R:Y:T:S        atserver:T           jump       crouch:S
    ///   run:MX:MY:S[:sprint]  log:TAG
    ///   brake:V:S             seat Brake button V for S s     drive:P:R:B:S   W/S, A/D, Brake for S s     horn:S
    ///   state:ID              log the zone's hover state as this peer sees it
    ///   players:N             wait for N players, then for the next whole 10 s of server time + 10 s (bots start together)
    ///   leap:RUNUP[:crouch]   run forward RUNUP s, jump without stopping, optionally tuck the legs after take-off
    ///   bhop:S                sprint forward for S s, jump on every landing, strafe and turn in the air
    ///   grab:ITEM:LX:LY:LZ    look at that item-local point of the cargo item named ITEM and press E (GRAB-01: a press takes it)
    ///   release               G once: throw the item in the hand / put down the heavy one     secure   press R once (SECURE-01)
    /// Steps only produce input frames (and, for forceaxes, a raw RPC); everything else is the normal player code.
    /// </summary>
    public class BenchBot : MonoBehaviour, IPlayerInputSource
    {
        string _script;
        string[] _steps;
        int _index = -1;
        float _stepStart;
        string[] _a;
        bool _entered;
        PlayerNet _player;
        PlayerInputFrame _frame;
        int _frames;
        float _syncAt = -1f;

        public InputContext Context { get; set; }
        public PlayerInputFrame Read() => _frame;
        public bool Done => _steps != null && _index >= _steps.Length;

        public void Init(string script, PlayerNet player)
        {
            _script = script;
            _steps = script.Split(';');
            _player = player;
            BenchLog.Event("bot", "script", script);
            Next();
        }

        void Next()
        {
            _index++;
            _stepStart = Time.time;
            _entered = false;
            _frames = 0;
            _a = _index < _steps.Length ? _steps[_index].Split(':') : null;
            if (_a != null) BenchLog.Event("bot_step", "i", _index, "step", _steps[_index]);
            else BenchLog.Event("bot_done", "script", _script, "p", _player.transform.position);
        }

        void Update()
        {
            if (_a == null) { _frame = default; return; }
            float t = Time.time - _stepStart;
            var f = new PlayerInputFrame();
            bool done = false;
            _frames++;
            switch (_a[0])
            {
                case "wait":
                    done = t >= F(1, 1f);
                    break;
                case "wander":
                    if (!_entered) _player.view.SetYaw(F(1, 0f));
                    f.Move = new Vector2(0f, 1f);
                    f.Sprint = _a.Length > 3 && _a[3] == "sprint";
                    done = t >= F(2, 10f);
                    break;
                case "run":
                    f.Move = new Vector2(F(1, 0f), F(2, 1f));
                    f.Sprint = _a.Length > 4 && _a[4] == "sprint";
                    done = t >= F(3, 1f);
                    break;
                case "face":
                    _player.view.SetYaw(F(1, 0f));
                    done = true;
                    break;
                case "goto":
                {
                    var target = new Vector3(F(1, 0f), 0f, F(2, 0f));
                    Vector3 d = target - _player.transform.position;
                    d.y = 0f;
                    if (d.magnitude <= F(3, 0.35f) || t > 30f)
                    {
                        if (t > 30f) BenchLog.Event("bot_timeout", "step", _steps[_index]);
                        done = true;
                    }
                    else
                    {
                        _player.view.SetYaw(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
                        f.Move = new Vector2(0f, d.magnitude < 1f ? 0.5f : 1f);
                    }
                    break;
                }
                case "look":
                {
                    if (!_entered && FindVolume(_a[1], out Vector3 p)) _player.view.LookAt(p);
                    if (_frames > 3)
                    {
                        var s = _player.sensor;
                        BenchLog.Event("target", "want", _a[1], "got", s.TargetId ?? (s.TargetGrab != null ? "grab:" + s.TargetGrab.label : "none"),
                            "hint", s.Hint ?? "", "ctx", Context.ToString());
                        done = true;
                    }
                    break;
                }
                case "lookat":
                    _player.view.LookAt(new Vector3(F(1, 0f), F(2, 0f), F(3, 0f)));
                    if (_frames > 3)
                    {
                        var s = _player.sensor;
                        BenchLog.Event("target", "want", _steps[_index], "got", s.TargetId ?? (s.TargetGrab != null ? "grab:" + s.TargetGrab.label : "none"),
                            "hint", s.Hint ?? "", "ctx", Context.ToString());
                        done = true;
                    }
                    break;
                case "use":
                    // on foot: E; in a seat: a short F (tap)
                    bool press = _frames <= 2;
                    if (Context == InputContext.OnFoot) f.Use = press;
                    else f.UseExit = press;
                    done = _frames > 6;
                    break;
                case "exit":
                    f.UseExit = t < 0.6f;
                    done = t >= 1.0f;
                    break;
                case "axes":
                    f.Pitch = F(1, 0f); f.Roll = F(2, 0f); f.Yaw = F(3, 0f); f.Throttle = F(4, 0f);
                    done = t >= F(5, 1f);
                    break;
                case "brake":
                    // the seat's Brake button (plane: toe brakes; van: a press toggles the hand brake) held for S s
                    f.Brake = F(1, 1f);
                    done = t >= F(2, 0.2f);
                    break;
                case "drive":
                    // van driver: W/S axis P, A/D axis R and the brake button B for S s (VAN3 AT-V02/V05/V06)
                    f.Pitch = F(1, 0f); f.Roll = F(2, 0f); f.Brake = F(3, 0f);
                    done = t >= F(4, 1f);
                    break;
                case "tp":
                {
                    // bench only: put the own character at X, Z (ground) facing YAW, the way leaving a seat does (owner
                    // authority); walking with goto stops 0.3 m off and door arcs need exact spots (VAN3 AT-V02..V04)
                    var p = new Vector3(F(1, 0f), 0.05f, F(2, 0f));
                    _player.motor.Teleport(p, F(3, 0f), Vector3.zero);
                    if (_player.TryGetComponent(out Unity.Netcode.Components.NetworkTransform nt) && nt.CanCommitToTransform)
                        nt.Teleport(p, Quaternion.Euler(0f, F(3, 0f), 0f), _player.transform.localScale);
                    _player.view.SetYaw(F(3, 0f));
                    done = _frames > 3;
                    break;
                }
                case "players":
                {
                    // wait until N players are in, then until the next whole 10 s of server time + 10 s: bots on
                    // several clients start the next step together however late the user pressed Join (VAN3 AT-V03)
                    var nm = NetworkManager.Singleton;
                    if (nm == null) break;
                    if (_syncAt < 0f && FindObjectsByType<PlayerNet>(FindObjectsSortMode.None).Length >= (int)F(1, 2f))
                    {
                        _syncAt = Mathf.Ceil(nm.ServerTime.TimeAsFloat / 10f) * 10f + 10f;
                        BenchLog.Event("bot_sync", "serverTime", nm.ServerTime.TimeAsFloat, "at", _syncAt);
                    }
                    done = _syncAt >= 0f && nm.ServerTime.TimeAsFloat >= _syncAt;
                    if (done) _syncAt = -1f;
                    break;
                }
                case "state":
                {
                    // what this peer sees of a zone: its hover state, from the replicated state (VAN3 AT-V09)
                    string id = _a.Length > 1 ? _a[1] : "";
                    string seen = "no zone";
                    foreach (var host in FindObjectsByType<InteractionHost>(FindObjectsSortMode.None))
                        if (host.TryGetVolume(id, out _)) { seen = host.Describe(id) ?? ""; break; }
                    BenchLog.Event("zone_state", "zone", id, "seen", seen);
                    done = true;
                    break;
                }
                case "cursor":
                    // Tab in a seat: the free cursor (the van's cab controls work only with it, user decision 21.10)
                    f.CursorToggle = _frames <= 2;
                    done = _frames > 4;
                    break;
                case "click":
                    // the left button with the cursor at the screen centre (look:ID aims the view first); held S s
                    f.Click = t < Mathf.Max(F(1, 0f), 0.05f);
                    done = t >= Mathf.Max(F(1, 0f), 0.05f) + 0.1f;
                    break;
                case "horn":
                    f.Horn = true;
                    done = t >= F(1, 0.5f);
                    break;
                case "forceaxes":
                    _player.SendAxes(new ControlInput { Pitch = F(1, 0f), Roll = F(2, 0f), Yaw = F(3, 0f), ThrottleRate = F(4, 0f) });
                    done = t >= F(5, 1f);
                    break;
                case "atserver":
                {
                    var nm = NetworkManager.Singleton;
                    done = nm != null && nm.ServerTime.TimeAsFloat >= F(1, 0f);
                    break;
                }
                case "jump":
                    f.Jump = _frames <= 2;
                    done = _frames > 3;
                    break;
                case "crouch":
                    f.Crouch = true;
                    done = t >= F(1, 1f);
                    break;
                case "leap":
                {
                    float runup = F(1, 0.5f);
                    bool tuck = _a.Length > 2 && _a[2] == "crouch";
                    f.Move = new Vector2(0f, 1f);
                    f.Jump = t >= runup && t < runup + 0.04f;
                    f.Crouch = tuck && t >= runup + 0.06f && t < runup + 0.9f;
                    done = t >= runup + 1.6f;
                    break;
                }
                case "bhop":
                {
                    // hold jump only on the frames we are grounded, so every landing is a new press
                    bool grounded = _player.motor.Grounded;
                    f.Sprint = true;
                    f.Move = grounded ? new Vector2(0f, 1f) : new Vector2(Mathf.Sign(Mathf.Sin(t * 3f)), 0f);
                    f.Jump = grounded && (_frames % 2 == 0);
                    if (!grounded) _player.view.SetYaw(_player.view.Yaw + Mathf.Sign(Mathf.Sin(t * 3f)) * 90f * Time.deltaTime);
                    done = t >= F(1, 5f);
                    break;
                }
                case "grab":
                {
                    var item = FindCargo(_a.Length > 1 ? _a[1] : "");
                    if (item == null) { BenchLog.Event("bot_no_item", "item", _a.Length > 1 ? _a[1] : ""); done = true; break; }
                    _player.view.LookAt(item.transform.TransformPoint(new Vector3(F(2, 0f), F(3, 0f), F(4, 0f))));
                    if (_frames == 4)
                    {
                        var s = _player.sensor;
                        BenchLog.Event("target", "want", _a[1], "got", s.TargetCargo != null ? s.TargetCargo.name : (s.TargetId ?? "none"),
                            "hint", s.Hint ?? "", "ctx", Context.ToString());
                    }
                    if (Context == InputContext.OnFoot) f.Use = _frames == 5 || _frames == 6;
                    done = _frames >= 8;
                    break;
                }
                case "release":
                    if (_frames == 0) _player.RequestDrop();
                    done = _frames >= 3;
                    break;
                case "secure":
                    f.Secure = _frames <= 2;
                    done = _frames > 4;
                    break;
                case "log":
                    BenchLog.Event("bot_log", "tag", _a.Length > 1 ? _a[1] : "", "p", _player.transform.position,
                        "ctx", Context.ToString(), "seat", _player.seatController.Seat != null ? _player.seatController.Seat.seatId : "none");
                    done = true;
                    break;
                default:
                    BenchLog.Event("bot_unknown_step", "step", _steps[_index]);
                    done = true;
                    break;
            }
            _entered = true;
            if (Context == InputContext.SeatCursor) f.Pointer = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);   // the cursor where the eye looks
            _frame = f;
            if (done) Next();
        }

        static CargoItem FindCargo(string name)
        {
            foreach (var c in FindObjectsByType<CargoItem>(FindObjectsSortMode.None))
                if (c.name == name) return c;
            return null;
        }

        static bool FindVolume(string id, out Vector3 point)
        {
            foreach (var host in FindObjectsByType<InteractionHost>(FindObjectsSortMode.None))
                if (host.TryGetVolume(id, out var v))
                {
                    var c = v.GetComponent<Collider>();
                    point = c != null ? c.bounds.center : v.transform.position;
                    return true;
                }
            point = default;
            return false;
        }

        float F(int i, float fallback) =>
            _a.Length > i && float.TryParse(_a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
    }
}
