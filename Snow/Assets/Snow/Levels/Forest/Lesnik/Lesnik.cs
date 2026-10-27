using System;
using System.Collections.Generic;
using Snow.Aircraft;
using Snow.Cargo;
using Snow.Creatures;
using Snow.Net;
using Snow.Player;
using Snow.Voice;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

namespace Snow.Levels.Forest
{
    public enum LesnikState : byte { Away, Sleep, Walk, Watch, Peek, Sneak, Grab, Leave, Flee }

    public enum LesnikAnim : byte { Stand, Shuffle, Grab, Carry, Cower, Flee, Sleep }

    [Serializable]
    public class LesnikSpot
    {
        public string name;
        [Tooltip("Feet, world")] public float[] p;
        [Tooltip("deg: he faces this way (the window)")] public float yaw;
        [Tooltip("The neck reaches up (a first-floor window)")] public bool up;
        [Tooltip("Forest_Opening.json rooms he looks into from here")] public string[] rooms;
    }

    [Serializable]
    public class LesnikSpotList
    {
        public LesnikSpot[] spots;
    }

    /// <summary>
    /// The Lesnik, v1 (docs/gdd/monsters/02_lesnik README «Логика», answers 10–16, 27.10). Server-driven, everyone sees
    /// him; the look is the blockout (LesnikBlockout, posed from replicated state).
    ///  - By day he sleeps in his cave (ForesterCave: Sleep_Forester). Light on him or a loud sound within 5 m wakes him:
    ///    he cowers and flees; the stash stays.
    ///  - Every night from the first he goes to the base. The walk between the cave and the base happens off the map
    ///    (Away): he leaves and comes only where no awake player sees (VISION §5: never moved before the players' eyes).
    ///  - One theft a night: one or two small loose things (not from hands or pockets) outside or behind an open door,
    ///    when nobody awake is within 15 m of it or looking at it. He carries them in his hands to the stash (Slot_Stash).
    ///  - An awake player in sight: he stays 15 m away and reaches his head out to him (NeckReach). Players in a room
    ///    with a peek spot (LesnikSpots.json): his head at that window, the body outside in the dark.
    ///  - Caught by a flashlight (body or head), or an awake player within 15 m: cowers, drops what he carries, flees
    ///    with huge leaps and is gone once nobody sees him.
    /// Not yet: bulbs and the fuse (no such items), the old cellar way in, the hut and the start post.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class Lesnik : NetworkBehaviour
    {
        [Header("Distances, m")]
        [Tooltip("R6: his body never nearer to an awake player")] public float fearRadius = 15f;
        [Tooltip("He notices an awake player in sight this near")] public float seeRange = 35f;
        [Tooltip("A flashlight reaches this far (SurvivalItem: range 30, cone 38)")] public float lightRange = 30f;
        public float lightHalfAngle = 21f;
        [Tooltip("Answer 15: a loud sound this near wakes him in the cave")] public float wakeRadius = 5f;
        [Tooltip("Nobody awake this near his bed: he may come home or leave")] public float caveClear = 40f;
        [Tooltip("Awake players see him this far (a dark night)")] public float sightRange = 60f;

        [Header("Speeds, m/s")]
        public float walkSpeed = 0.9f;
        public float fleeSpeed = 6f;

        [Header("Times, s")]
        [Tooltip("The walk between the cave and the base, off the map")] public float travelSeconds = 90f;
        public float watchSeconds = 15f, peekSeconds = 14f, fleeCooldown = 60f;

        [Header("The base")]
        public Vector3 baseCenter = new Vector3(342f, 17f, -360f);
        public Vector3 baseSize = new Vector3(200f, 60f, 200f);
        [Tooltip("m from the base centre: where he comes out of the forest and goes back in")] public float exitRing = 75f;
        [Tooltip("Answer 11: one or two things a trip")] public int maxItems = 2;

        /// <summary>The "client" his hands are for CargoItem (not a player; CargoItem.Nobody is MaxValue).</summary>
        public const ulong CreatureClient = CargoItem.Nobody - 1;

        public static Lesnik Instance { get; private set; }
        /// <summary>Pult: he stands where he is and thinks nothing.</summary>
        public static bool Paused;

        readonly NetworkVariable<LesnikState> _state = new NetworkVariable<LesnikState>(LesnikState.Away,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<LesnikAnim> _anim = new NetworkVariable<LesnikAnim>(LesnikAnim.Stand,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<byte> _neck = new NetworkVariable<byte>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);   // 0 hanging, 1 reach, 2 reach up
        readonly NetworkVariable<bool> _shown = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<int> _night = new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<bool> _theftDone = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<byte> _carrying = new NetworkVariable<byte>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public LesnikState State => _state.Value;
        public bool Shown => _shown.Value;
        public int Night => _night.Value;
        public bool TheftDone => _theftDone.Value;
        public int Carrying => _carrying.Value;
        /// <summary>Server: what he is after, for the pult.</summary>
        public string Doing { get; private set; } = "";
        public LesnikBlockout Model => _model;

        struct Watcher
        {
            public PlayerNet p;
            public Vector3 eye, fwd;
            public bool awake;
            public string room;
        }

        LesnikBlockout _model;
        NavMeshAgent _agent;
        NavMeshPath _path;
        Transform _handR, _handL, _bed, _mouth;
        readonly List<Transform> _slots = new List<Transform>();
        readonly List<Vector3> _exits = new List<Vector3>();
        readonly List<CargoItem> _carried = new List<CargoItem>();
        readonly List<Watcher> _players = new List<Watcher>();
        readonly Dictionary<CargoItem, (Transform socket, SurvivalItem lamp)> _lamps = new Dictionary<CargoItem, (Transform, SurvivalItem)>();
        readonly Dictionary<PlayerNet, Transform> _heads = new Dictionary<PlayerNet, Transform>();
        ForestOrientedRoom[] _rooms = Array.Empty<ForestOrientedRoom>();
        LesnikSpot[] _spots = Array.Empty<LesnikSpot>();
        int _mask;

        CargoItem _prey;
        LesnikSpot _spot;
        PlayerNet _watch;
        Vector3 _goal, _threat;
        bool _hasGoal, _flag, _woken, _wasNight, _navAsked, _home, _lastShown;
        Vector3 _lastPos;
        float _stateT, _nextThink, _awayUntil, _idleUntil, _watchLostAt, _watchAgain, _peekAgain, _preyAgain, _atSpotT;

        // ================================================================ spawn
        public override void OnNetworkSpawn()
        {
            Instance = this;
            _model = GetComponentInChildren<LesnikBlockout>(true);
            _state.OnValueChanged += (_, __) => ApplyVisual();
            _anim.OnValueChanged += (_, __) => ApplyVisual();
            _neck.OnValueChanged += (_, __) => ApplyVisual();
            _shown.OnValueChanged += (_, __) => ApplyVisual();
            ApplyVisual();
            if (!IsServer) return;
            _path = new NavMeshPath();
            _mask = Physics.DefaultRaycastLayers;
            foreach (string layer in new[] { "Interact", "Ignore Raycast" })
            {
                int l = LayerMask.NameToLayer(layer);
                if (l >= 0) _mask &= ~(1 << l);
            }
            if (_model != null)
            {
                _handR = Deep(_model.transform, "Hand.R");
                _handL = Deep(_model.transform, "Hand.L");
            }
            FindCave();
            LoadRooms();
            LoadSpots();
            NoiseEvents.Heard += OnNoise;
            Away(2f);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (!IsServer) return;
            NoiseEvents.Heard -= OnNoise;
            DropAll(Vector3.zero);
            LesnikNav.Clear();
        }

        void FindCave()
        {
            GameObject bed = GameObject.Find("Sleep_Forester");
            if (bed == null) { Debug.LogWarning("[Snow] Лесник: пещера (Sleep_Forester) не найдена — украденное останется там, где он исчез."); return; }
            _bed = bed.transform;
            Transform cave = _bed.parent != null ? _bed.parent : _bed;
            foreach (Transform t in cave.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Slot_Stash_", StringComparison.Ordinal)) _slots.Add(t);
                else if (t.name == "View_CaveMouth") _mouth = t;
            }
            _slots.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        void LoadRooms()
        {
            var opening = ForestOpening.Instance;
            var data = opening != null ? ForestData.Read<OpeningData>(opening.opening) : null;
            if (data?.rooms != null) _rooms = data.rooms;
        }

        void LoadSpots()
        {
            var asset = Resources.Load<TextAsset>("LesnikSpots");
            var list = asset != null ? JsonUtility.FromJson<LesnikSpotList>(asset.text) : null;
            _spots = list?.spots ?? Array.Empty<LesnikSpot>();
        }

        /// <summary>Server: the peek spots changed (the pult wrote a new one).</summary>
        public void ServerSetSpots(LesnikSpot[] spots) => _spots = spots ?? Array.Empty<LesnikSpot>();
        public IReadOnlyList<LesnikSpot> Spots => _spots;

        void BuildNav()
        {
            _navAsked = true;
            var regions = new List<Bounds> { new Bounds(baseCenter, baseSize) };
            if (_bed != null) regions.Add(new Bounds(_bed.position, new Vector3(80f, 40f, 80f)));
            StartCoroutine(LesnikNav.Build(regions.ToArray(), ForestOpening.Instance != null ? ForestOpening.Instance.host : null, FindExits));
        }

        // points on a ring round the base on the NavMesh, all reachable from each other (one walkable piece)
        void FindExits()
        {
            _exits.Clear();
            var all = new List<Vector3>();
            var filter = LesnikNav.Filter;
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                Vector3 p = baseCenter + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * exitRing;
                if (Physics.Raycast(new Vector3(p.x, baseCenter.y + 80f, p.z), Vector3.down, out RaycastHit hit, 200f, _mask, QueryTriggerInteraction.Ignore))
                    p = hit.point;
                if (NavMesh.SamplePosition(p, out NavMeshHit nh, 6f, filter)) all.Add(nh.position);
            }
            int best = -1, bestCount = 0;
            for (int i = 0; i < all.Count; i++)
            {
                int count = 0;
                for (int j = 0; j < all.Count; j++)
                    if (j != i && NavMesh.CalculatePath(all[i], all[j], filter, _path) && _path.status == NavMeshPathStatus.PathComplete) count++;
                if (count > bestCount) { bestCount = count; best = i; }
            }
            for (int j = 0; j < all.Count; j++)
                if (j == best || (best >= 0 && NavMesh.CalculatePath(all[best], all[j], filter, _path) && _path.status == NavMeshPathStatus.PathComplete))
                    _exits.Add(all[j]);
            BenchLog.Event("lesnik_exits", "found", all.Count, "connected", _exits.Count);
            if (_exits.Count == 0) Debug.LogWarning("[Snow] Лесник: на кольце вокруг базы нет точек NavMesh — к базе он не выйдет.");
        }

        // ================================================================ every peer: the look
        void ApplyVisual()
        {
            if (_model == null) return;
            _model.SetVisible(_shown.Value);
            byte neck = _neck.Value;
            if (neck != 0)
            {
                if (_model.Playing != null || _model.Target < 1f || _model.ReachUp != (neck == 2)) _model.ReachTo(1f, neck == 2);
                return;
            }
            string clip = ClipOf(_anim.Value);
            if (clip == null)
            {
                if (_model.Playing != null) _model.Play(null);
                if (_model.Target > 0f) _model.ReachTo(0f, _model.ReachUp);
            }
            else if (_model.Playing == null || _model.Playing.name != clip) _model.Play(clip);
        }

        static string ClipOf(LesnikAnim a)
        {
            switch (a)
            {
                case LesnikAnim.Shuffle: return "Shuffle";
                case LesnikAnim.Grab: return "Grab";
                case LesnikAnim.Carry: return "Carry";
                case LesnikAnim.Cower: return "Cower";
                case LesnikAnim.Flee: return "Flee";
                case LesnikAnim.Sleep: return "Sleep";
                default: return null;
            }
        }

        void SetAnim(LesnikAnim a)
        {
            if (_anim.Value != a) _anim.Value = a;
            ApplyVisual();
        }

        void SetNeck(byte n)
        {
            if (_neck.Value != n) _neck.Value = n;
            ApplyVisual();
        }

        void SetState(LesnikState s, string doing)
        {
            if (_state.Value != s) BenchLog.Event("lesnik", "state", s.ToString(), "doing", doing, "at", transform.position);
            _state.Value = s;
            Doing = doing;
            _stateT = Time.time;
            _flag = false;
            _hasGoal = false;
        }

        // ================================================================ server: the loop
        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            if (_shown.Value && (transform.position - _lastPos).sqrMagnitude > 9f && _lastShown)
                BenchLog.Event("lesnik_jump", "from", _lastPos, "to", transform.position, "state", _state.Value.ToString(),
                    "agent", _agent != null && _agent.enabled);
            _lastPos = transform.position;
            _lastShown = _shown.Value;
            CarryUpdate();
            if (Paused)
            {
                if (AgentOn) _agent.isStopped = true;
                return;
            }
            if (Time.time >= _nextThink)
            {
                _nextThink = Time.time + 0.2f;
                Think();
            }
            LesnikState st = _state.Value;
            if (st == LesnikState.Walk || st == LesnikState.Sneak || st == LesnikState.Leave || (st == LesnikState.Peek && !_flag))
            {
                bool moving = AgentOn && !_agent.isStopped && _agent.velocity.sqrMagnitude > 0.04f;
                SetAnim(moving ? LesnikAnim.Shuffle : _carried.Count > 0 ? LesnikAnim.Carry : LesnikAnim.Stand);
            }
        }

        void Think()
        {
            float hour = ForestDayNight.Instance != null ? ForestDayNight.Instance.Hour : 12f;
            bool night = ForestDayNight.DayFactor(hour) < 0.05f;
            if (night && !_wasNight)
            {
                _night.Value++;
                _theftDone.Value = false;                              // answer 10: one theft every night, from the first
            }
            _wasNight = night;
            if (!_navAsked) BuildNav();
            ScanPlayers();
            switch (_state.Value)
            {
                case LesnikState.Away: ThinkAway(night); break;
                case LesnikState.Sleep: ThinkSleep(night); break;
                case LesnikState.Walk: ThinkWalk(night); break;
                case LesnikState.Watch: ThinkWatch(); break;
                case LesnikState.Peek: ThinkPeek(night); break;
                case LesnikState.Sneak: ThinkSneak(); break;
                case LesnikState.Grab: ThinkGrab(); break;
                case LesnikState.Leave: ThinkLeave(); break;
                case LesnikState.Flee: ThinkFlee(); break;
            }
        }

        // ---------------------------------------------------------------- off the map, sleeping
        void ThinkAway(bool night)
        {
            if (Time.time < _awayUntil) return;
            if (!night) _home = false;
            if (night && !_home)
            {
                if (!LesnikNav.Ready || _exits.Count == 0) { Doing = "ждёт NavMesh"; return; }
                var free = new List<Vector3>();
                foreach (Vector3 e in _exits) if (Unseen(e, 30f)) free.Add(e);
                if (free.Count == 0) { Doing = "все выходы на виду — ждёт"; _awayUntil = Time.time + 5f; return; }
                Vector3 at = free[UnityEngine.Random.Range(0, free.Count)];
                Appear(at, Quaternion.LookRotation(Flat(baseCenter - at)));
                SetState(LesnikState.Walk, "вышел из леса к базе");
                return;
            }
            if (_bed == null) { Doing = "день, пещеры нет"; return; }
            if (!CaveClear()) { Doing = "день, у пещеры люди — ждёт"; _awayUntil = Time.time + 5f; return; }
            SetAgent(false);
            Teleport(_bed.position, Quaternion.LookRotation(Flat(_bed.forward)));
            _shown.Value = true;
            SetNeck(0);
            SetAnim(LesnikAnim.Sleep);
            _woken = false;
            SetState(LesnikState.Sleep, "спит в пещере");
        }

        void ThinkSleep(bool night)
        {
            bool lit = Lit(out Vector3 light);
            if (_woken || lit)
            {
                if (lit) _threat = light;
                if (_bed != null && NavMesh.SamplePosition(_bed.position, out NavMeshHit nh, 3f, LesnikNav.Filter)) Appear(nh.position, transform.rotation);
                StartFlee(_threat, "разбудили в пещере");
                return;
            }
            if (night && !_home && CaveClear()) Away(travelSeconds, "ушёл к базе (в пути)");
        }

        void OnNoise(Vector3 point, float radius, string kind, ulong source)
        {
            if (_state.Value != LesnikState.Sleep || radius < NoiseEvents.Medium) return;
            if ((point - transform.position).sqrMagnitude <= wakeRadius * wakeRadius)
            {
                _woken = true;
                _threat = point;
            }
        }

        // ---------------------------------------------------------------- at the base
        void ThinkWalk(bool night)
        {
            if (Fear(false)) return;
            if (!night || _carried.Count > 0) { StartLeave(night ? "уносит украденное" : "светает — домой"); return; }
            if (!_theftDone.Value && Time.time >= _preyAgain)
            {
                _preyAgain = Time.time + 2f;
                if (FindPrey(transform.position, 250f, out CargoItem prey))
                {
                    _prey = prey;
                    SetState(LesnikState.Sneak, "крадётся к: " + prey.label);
                    return;
                }
            }
            if (Time.time >= _watchAgain && SeesPlayer(out PlayerNet who))
            {
                _watch = who;
                _watchLostAt = -1f;
                Stop();
                SetState(LesnikState.Watch, "подглядывает");
                return;
            }
            if (Time.time >= _peekAgain && ChooseSpot(out LesnikSpot spot))
            {
                _spot = spot;
                SetState(LesnikState.Peek, "идёт к окну: " + spot.name);
                return;
            }
            if (!_hasGoal || Arrived())
            {
                if (_hasGoal) { _idleUntil = Time.time + UnityEngine.Random.Range(4f, 10f); Stop(); _hasGoal = false; }
                if (Time.time < _idleUntil) return;
                Go(RoamPoint());
                Doing = "бродит";
            }
        }

        void ThinkWatch()
        {
            if (Fear(false)) return;
            int i = Find(_watch);
            if (i < 0 || !_players[i].awake || Time.time - _stateT > watchSeconds) { EndWatch(); return; }
            Watcher w = _players[i];
            bool see = Vector3.Distance(Eye, w.eye) < seeRange && ClearTo(Eye, w.eye);
            if (!see)
            {
                if (_watchLostAt < 0f) _watchLostAt = Time.time;
                else if (Time.time - _watchLostAt > 2f) { EndWatch(); return; }
            }
            else _watchLostAt = -1f;
            Face(w.eye);
            SetNeck((byte)(w.eye.y - transform.position.y > 3.4f ? 2 : 1));
        }

        void EndWatch()
        {
            SetNeck(0);
            _watch = null;
            _watchAgain = Time.time + 20f;
            SetState(LesnikState.Walk, "бродит");
        }

        void ThinkPeek(bool night)
        {
            if (Fear(true)) return;
            bool someone = SpotHasPlayer(_spot);
            if (!night || _spot == null || !someone || Time.time - _stateT > 150f)
            {
                SetNeck(0);
                _peekAgain = Time.time + 90f;
                SetState(LesnikState.Walk, "бродит");
                return;
            }
            Vector3 at = SpotFeet(_spot);
            if (!_flag)
            {
                Go(at);
                if (Arrived())
                {
                    _flag = true;
                    _atSpotT = Time.time;
                    Stop();
                    transform.rotation = Quaternion.Euler(0f, _spot.yaw, 0f);
                    SetAnim(LesnikAnim.Stand);
                    SetNeck((byte)(_spot.up ? 2 : 1));
                    Doing = "голова в окне: " + _spot.name;
                }
                return;
            }
            if (Time.time - _atSpotT > peekSeconds)
            {
                SetNeck(0);
                _peekAgain = Time.time + 90f;
                SetState(LesnikState.Walk, "бродит");
            }
        }

        void ThinkSneak()
        {
            if (Fear(false)) return;
            if (!PreyOk(_prey) || Time.time - _stateT > 120f)
            {
                _prey = null;
                if (_carried.Count > 0) StartLeave("уносит украденное");
                else SetState(LesnikState.Walk, "бродит");
                return;
            }
            Vector3 p = _prey.transform.position;
            Go(p);
            Vector3 d = p - transform.position;
            if (new Vector2(d.x, d.z).magnitude < 1.2f && Mathf.Abs(d.y) < 1.6f)
            {
                Stop();
                Face(p);
                SetAnim(LesnikAnim.Grab);
                SetState(LesnikState.Grab, "берёт: " + _prey.label);
            }
        }

        void ThinkGrab()
        {
            if (Fear(false)) return;
            float t = Time.time - _stateT;
            if (!_flag && t > 0.55f)
            {
                _flag = true;
                if (PreyOk(_prey) && _prey.ServerTakeInHand(CreatureClient, out _))
                {
                    _carried.Add(_prey);
                    _carrying.Value = (byte)_carried.Count;
                    BenchLog.Event("lesnik_take", "item", _prey.name, "at", _prey.transform.position);
                }
                _prey = null;
            }
            if (t < 1.05f) return;
            if (_carried.Count > 0 && _carried.Count < maxItems && FindPrey(transform.position, 12f, out CargoItem next))
            {
                _prey = next;
                SetState(LesnikState.Sneak, "ещё одна: " + next.label);
                return;
            }
            if (_carried.Count > 0) StartLeave("уносит украденное");
            else SetState(LesnikState.Walk, "бродит");
        }

        void StartLeave(string doing)
        {
            SetNeck(0);
            SetState(LesnikState.Leave, doing);
            if (PickExit(transform.position, out Vector3 e)) { _goal = e; _hasGoal = true; }
        }

        void ThinkLeave()
        {
            if (Fear(false)) return;
            if (!_hasGoal || Time.time - _stateT > 240f)
            {
                _stateT = Time.time;
                if (PickExit(transform.position, out Vector3 e)) { _goal = e; _hasGoal = true; }
                else if (UnseenHere()) { Vanish(); return; }
                else return;
            }
            Go(_goal);
            if (!Arrived()) return;
            if (UnseenHere()) Vanish();
            else if (PickExit(transform.position, out Vector3 other)) _goal = other;
        }

        // ---------------------------------------------------------------- caught
        // lit (body or head) or an awake player too near (R6); at a window only players outside the rooms count
        bool Fear(bool atWindow)
        {
            if (Lit(out Vector3 from)) { StartFlee(from, "пойман фонарём"); return true; }
            foreach (Watcher w in _players)
            {
                if (!w.awake || (atWindow && w.room != null)) continue;
                if ((w.p.transform.position - transform.position).sqrMagnitude < fearRadius * fearRadius && (w.room == null || ClearTo(Eye, w.eye)))
                {
                    StartFlee(w.p.transform.position, "игрок ближе 15 м");
                    return true;
                }
            }
            return false;
        }

        void StartFlee(Vector3 from, string why)
        {
            _threat = from;
            Vector3 away = Flat(transform.position - from).normalized;
            DropAll(away * 1.5f + Vector3.up);                      // answer 12: caught with it, he drops it
            SetNeck(0);
            Stop();
            SetAnim(LesnikAnim.Cower);
            SetState(LesnikState.Flee, why);
        }

        void ThinkFlee()
        {
            float t = Time.time - _stateT;
            if (t < 0.45f) return;                                   // Cower: a hand over the face
            if (!_flag)
            {
                _flag = true;
                SetAnim(LesnikAnim.Flee);
                if (AgentOn) { _agent.speed = fleeSpeed; _agent.acceleration = 30f; }
                if (FleePoint(out Vector3 p)) { _goal = p; Go(p, true); }
            }
            if (t > 1.5f && UnseenHere(25f)) { Vanish(fleeCooldown, "убежал"); return; }
            if (!AgentOn) return;                                    // no ground to run on: gone once nobody looks
            if (Arrived() && FleePoint(out Vector3 next)) { _goal = next; Go(next, true); }
            if (t > 40f && NearestAwake() > 40f) Vanish(fleeCooldown, "убежал");
        }

        // ================================================================ carrying and the stash
        void CarryUpdate()
        {
            for (int i = _carried.Count - 1; i >= 0; i--)
            {
                CargoItem item = _carried[i];
                if (item == null || !item.IsSpawned || !item.CarriedBy(CreatureClient)) { _carried.RemoveAt(i); continue; }
                Transform hand = i == 0 ? _handR : _handL;
                if (hand == null) hand = transform;
                item.ServerTarget(CreatureClient, hand.position, hand.rotation);
            }
            if (_carrying.Value != _carried.Count) _carrying.Value = (byte)_carried.Count;
        }

        void DropAll(Vector3 velocity)
        {
            foreach (CargoItem item in _carried)
                if (item != null && item.IsSpawned) item.ServerDrop(CreatureClient, velocity + UnityEngine.Random.insideUnitSphere * 0.5f);
            _carried.Clear();
            _carrying.Value = 0;
        }

        // what he brings home goes on the free stash places at once: nobody sees both ends of the walk
        void Stash()
        {
            if (_carried.Count == 0) return;
            foreach (CargoItem item in _carried)
            {
                if (item == null || !item.IsSpawned) continue;
                item.ServerDrop(CreatureClient, Vector3.zero);
                Vector3 to = FreeSlot(out bool found);
                if (found) item.ServerShift(to + Vector3.up * 0.08f - item.transform.position);
                BenchLog.Event("lesnik_stash", "item", item.name, "slot", found, "at", item.transform.position);
            }
            _carried.Clear();
            _carrying.Value = 0;
            _theftDone.Value = true;
        }

        Vector3 FreeSlot(out bool found)
        {
            foreach (Transform s in _slots)
            {
                bool taken = false;
                foreach (CargoItem it in CargoItem.All)
                    if (it != null && it.IsSpawned && it.Carry == CarryMode.None && (it.transform.position - s.position).sqrMagnitude < 0.09f) { taken = true; break; }
                if (!taken) { found = true; return s.position; }
            }
            found = _bed != null;
            return _bed != null ? _bed.position + UnityEngine.Random.insideUnitSphere * 0.6f : transform.position;
        }

        // ================================================================ prey
        bool PreyOk(CargoItem item)
        {
            if (item == null || !item.IsSpawned || item.Carry != CarryMode.None || item.State != CargoState.Loose || !item.Pocketable) return false;
            if (item.TryGetComponent(out ForestItemTag tag) && tag.clean) return false;          // rubbish
            Vector3 p = item.transform.position;
            if (!new Bounds(baseCenter, baseSize).Contains(p)) return false;
            return !Observed(p);
        }

        bool FindPrey(Vector3 from, float maxPath, out CargoItem best)
        {
            best = null;
            if (!AgentOn) return false;
            var near = new List<(float d, CargoItem it)>();
            foreach (CargoItem it in CargoItem.All)
                if (PreyOk(it)) near.Add(((it.transform.position - from).sqrMagnitude, it));
            near.Sort((a, b) => a.d.CompareTo(b.d));
            float bestLen = maxPath;
            var filter = LesnikNav.Filter;
            for (int i = 0; i < near.Count && i < 10; i++)
            {
                if (!NavMesh.SamplePosition(near[i].it.transform.position, out NavMeshHit nh, 1.3f, filter)) continue;
                if (!NavMesh.CalculatePath(from, nh.position, filter, _path) || _path.status != NavMeshPathStatus.PathComplete) continue;
                float len = PathLength(_path);
                if (len < bestLen) { bestLen = len; best = near[i].it; }
            }
            return best != null;
        }

        // ================================================================ senses
        Vector3 Eye => transform.position + Vector3.up * 2.3f;

        void ScanPlayers()
        {
            _players.Clear();
            var bunks = ForestOpening.Instance != null ? ForestOpening.Instance.bunks : null;
            foreach (NetworkClient c in NetworkManager.ConnectedClientsList)
            {
                if (c.PlayerObject == null || !c.PlayerObject.TryGetComponent(out PlayerNet p)) continue;
                Vector3 eye = p.transform.position + Vector3.up * (p.Seated ? 0.83f : 1.64f);
                _players.Add(new Watcher
                {
                    p = p,
                    eye = eye,
                    fwd = Look(p),
                    awake = bunks == null || !bunks.InBunk(c.ClientId),
                    room = RoomAt(eye - Vector3.up * 0.7f),
                });
            }
        }

        // the host's own player: his camera; the others: the head pivot PlayerNet turns for the facing marker
        Vector3 Look(PlayerNet p)
        {
            if (p.IsOwner && p.view != null && p.view.cam != null) return p.view.cam.transform.forward;
            if (!_heads.TryGetValue(p, out Transform h) || h == null)
            {
                h = p.transform.Find("HeadLook");
                _heads[p] = h;
            }
            return h != null ? h.forward : p.transform.forward;
        }

        int Find(PlayerNet p)
        {
            for (int i = 0; i < _players.Count; i++) if (_players[i].p == p) return i;
            return -1;
        }

        bool Clear(Vector3 a, Vector3 b) => !Physics.Linecast(a, b, _mask, QueryTriggerInteraction.Ignore);

        // to a player's eye: stop short of his own CharacterController, which the line would hit
        bool ClearTo(Vector3 from, Vector3 eye)
        {
            Vector3 d = eye - from;
            float m = d.magnitude;
            return m < 0.7f || Clear(from, eye - d / m * 0.6f);
        }

        // could an awake player see this point (a dark night: near, in front, nothing between)?
        bool Visible(Vector3 point, float range)
        {
            foreach (Watcher w in _players)
            {
                if (!w.awake) continue;
                Vector3 d = point - w.eye;
                float m = d.magnitude;
                if (m > range) continue;
                if (m < 3f || (Vector3.Dot(w.fwd, d / m) > 0.26f && Clear(w.eye, point))) return true;   // 75 deg each side
            }
            return false;
        }

        bool Unseen(Vector3 feet, float near)
        {
            foreach (Watcher w in _players)
                if (w.awake && (w.p.transform.position - feet).sqrMagnitude < near * near) return false;
            return !Visible(feet + Vector3.up * 1.5f, sightRange);
        }

        bool UnseenHere(float near = 20f) => Unseen(transform.position, near) && !Visible(Eye, sightRange);

        bool Observed(Vector3 p)
        {
            foreach (Watcher w in _players)
                if (w.awake && (w.p.transform.position - p).sqrMagnitude < fearRadius * fearRadius) return true;
            return Visible(p + Vector3.up * 0.3f, 30f);
        }

        bool SeesPlayer(out PlayerNet who)
        {
            who = null;
            float best = seeRange;
            foreach (Watcher w in _players)
            {
                if (!w.awake || w.room != null) continue;
                float d = Vector3.Distance(Eye, w.eye);
                if (d < best && ClearTo(Eye, w.eye)) { best = d; who = w.p; }
            }
            return who != null;
        }

        float NearestAwake()
        {
            float best = float.MaxValue;
            foreach (Watcher w in _players)
                if (w.awake) best = Mathf.Min(best, Vector3.Distance(w.p.transform.position, transform.position));
            return best;
        }

        bool CaveClear()
        {
            if (_bed == null) return true;
            foreach (Watcher w in _players)
                if (w.awake && (w.p.transform.position - _bed.position).sqrMagnitude < caveClear * caveClear) return false;
            return true;
        }

        // any switched-on flashlight (in a hand or lying) shining on his body or head, nothing between
        bool Lit(out Vector3 from)
        {
            from = default;
            if (!_shown.Value) return false;
            Vector3 body = transform.position + Vector3.up * 1.4f;
            Vector3 head = _model != null && _model.head != null ? _model.head.position : Eye;
            foreach (CargoItem it in CargoItem.All)
            {
                if (it == null || !it.IsSpawned || it.Carry == CarryMode.Pocket) continue;
                if (!_lamps.TryGetValue(it, out var lamp))
                {
                    lamp = default;
                    if (it.TryGetComponent(out SurvivalItem s) && s.kind == SurvivalKind.Flashlight)
                    {
                        Transform socket = Deep(it.transform, "Socket_Light");
                        lamp = (socket != null ? socket : it.transform, s);
                    }
                    _lamps[it] = lamp;
                }
                if (lamp.lamp == null || !lamp.lamp.On) continue;
                if (Beam(lamp.socket, body) || Beam(lamp.socket, head)) { from = lamp.socket.position; return true; }
            }
            return false;
        }

        bool Beam(Transform socket, Vector3 point)
        {
            Vector3 d = point - socket.position;
            float m = d.magnitude;
            if (m > lightRange || m < 0.01f) return false;
            if (Vector3.Angle(socket.forward, d) > lightHalfAngle) return false;
            return Clear(socket.position + socket.forward * 0.1f, point);
        }

        string RoomAt(Vector3 p)
        {
            foreach (ForestOrientedRoom r in _rooms)
            {
                if (r == null) continue;
                Vector3 l = Quaternion.Inverse(ForestData.Q(r.q)) * (p - ForestData.V3(r.p)), half = ForestData.V3(r.size) * 0.5f;
                if (Mathf.Abs(l.x) <= half.x && Mathf.Abs(l.y) <= half.y && Mathf.Abs(l.z) <= half.z) return r.name;
            }
            return null;
        }

        // ================================================================ peek spots
        bool SpotHasPlayer(LesnikSpot s)
        {
            if (s?.rooms == null) return false;
            foreach (Watcher w in _players)
                if (w.awake && w.room != null && Array.IndexOf(s.rooms, w.room) >= 0) return true;
            return false;
        }

        bool ChooseSpot(out LesnikSpot best)
        {
            best = null;
            if (!AgentOn) return false;
            float bestLen = float.MaxValue;
            foreach (LesnikSpot s in _spots)
            {
                if (!SpotHasPlayer(s)) continue;
                if (!NavMesh.CalculatePath(transform.position, SpotFeet(s), LesnikNav.Filter, _path) || _path.status != NavMeshPathStatus.PathComplete) continue;
                float len = PathLength(_path);
                if (len < bestLen) { bestLen = len; best = s; }
            }
            return best != null;
        }

        Vector3 SpotFeet(LesnikSpot s)
        {
            Vector3 p = ForestData.V3(s.p);
            return NavMesh.SamplePosition(p, out NavMeshHit nh, 2f, LesnikNav.Filter) ? nh.position : p;
        }

        // ================================================================ moving
        bool AgentOn => _agent != null && _agent.enabled && _agent.isOnNavMesh;

        void Appear(Vector3 at, Quaternion rot)
        {
            Teleport(at, rot);
            SetAgent(true);
            _shown.Value = true;
            SetNeck(0);
            SetAnim(LesnikAnim.Stand);
        }

        void SetAgent(bool on)
        {
            if (!on) { if (_agent != null) _agent.enabled = false; return; }
            if (!LesnikNav.Ready) return;
            if (_agent == null)
            {
                _agent = gameObject.AddComponent<NavMeshAgent>();             // here, on the NavMesh: no "not close enough" warning
                _agent.agentTypeID = LesnikNav.AgentType;
                _agent.radius = 0.3f;
                _agent.height = 1.9f;
                _agent.angularSpeed = 200f;
                _agent.stoppingDistance = 0.5f;
                _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            }
            _agent.enabled = true;
            _agent.Warp(transform.position);
            _agent.speed = walkSpeed;
            _agent.acceleration = 3f;
        }

        void Teleport(Vector3 at, Quaternion rot)
        {
            if (_agent != null && _agent.enabled) _agent.Warp(at);
            transform.SetPositionAndRotation(at, rot);
            if (TryGetComponent(out NetworkTransform nt) && nt.CanCommitToTransform) nt.Teleport(at, rot, transform.localScale);
        }

        void Go(Vector3 to, bool run = false)
        {
            if (!AgentOn) return;
            _agent.isStopped = false;
            _agent.speed = run ? fleeSpeed : walkSpeed;
            if (!_hasGoal || (to - _goal).sqrMagnitude > 0.25f || !_agent.hasPath) _agent.SetDestination(to);
            _goal = to;
            _hasGoal = true;
        }

        void Stop()
        {
            if (!AgentOn) return;
            _agent.isStopped = true;
            _agent.ResetPath();
        }

        bool Arrived() => AgentOn && !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.4f;

        void Face(Vector3 p)
        {
            Vector3 d = Flat(p - transform.position);
            if (d.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 180f * 0.2f);
        }

        void Away(float seconds, string doing = "в пути")
        {
            SetNeck(0);
            SetAgent(false);
            _shown.Value = false;
            _awayUntil = Time.time + seconds;
            SetState(LesnikState.Away, doing);
        }

        void Vanish(float seconds = -1f, string doing = "ушёл в лес (в пути)")
        {
            Stash();
            Away(seconds < 0f ? travelSeconds : seconds, doing);
        }

        // somewhere in the yard or at the forest's edge he can reach, not towards an awake player (R6)
        Vector3 RoamPoint()
        {
            for (int i = 0; i < 8; i++)
            {
                if (_exits.Count > 0 && UnityEngine.Random.value < 0.35f)
                {
                    Vector3 e = _exits[UnityEngine.Random.Range(0, _exits.Count)];
                    if (NearestAwakeTo(e) > fearRadius + 5f) return e;
                    continue;
                }
                Vector2 r = UnityEngine.Random.insideUnitCircle * 45f;
                if (NavMesh.SamplePosition(baseCenter + new Vector3(r.x, 0f, r.y), out NavMeshHit nh, 8f, LesnikNav.Filter) &&
                    NearestAwakeTo(nh.position) > fearRadius + 5f &&
                    NavMesh.CalculatePath(transform.position, nh.position, LesnikNav.Filter, _path) && _path.status == NavMeshPathStatus.PathComplete)
                    return nh.position;                                  // not inside a shut building
            }
            return transform.position;
        }

        // the exit nobody sees, farthest from the awake players, not too far for him
        bool PickExit(Vector3 from, out Vector3 best)
        {
            best = default;
            float score = float.MinValue;
            foreach (Vector3 e in _exits)
            {
                if (!Unseen(e, 25f)) continue;
                float s = NearestAwakeTo(e) - 0.3f * Vector3.Distance(from, e);
                if (s > score) { score = s; best = e; }
            }
            return score > float.MinValue;
        }

        bool FleePoint(out Vector3 best)
        {
            best = default;
            float score = float.MinValue;
            Vector3 away = Flat(transform.position - _threat).normalized;
            foreach (Vector3 e in _exits)
            {
                Vector3 d = e - transform.position;
                float s = Vector3.Dot(Flat(d).normalized, away) * 50f - 0.2f * d.magnitude;
                if (s > score && d.sqrMagnitude > 25f) { score = s; best = e; }
            }
            if (score > float.MinValue) return true;
            Vector3 p = transform.position + away * 30f;              // the cave: out through its mouth
            if (_mouth != null && _bed != null && (transform.position - _bed.position).sqrMagnitude < 900f)
                p = _mouth.position + Flat(_mouth.position - _bed.position).normalized * 25f;
            if (NavMesh.SamplePosition(p, out NavMeshHit nh, 10f, LesnikNav.Filter)) { best = nh.position; return true; }
            return false;
        }

        float NearestAwakeTo(Vector3 p)
        {
            float best = 1000f;
            foreach (Watcher w in _players)
                if (w.awake) best = Mathf.Min(best, Vector3.Distance(w.p.transform.position, p));
            return best;
        }

        static float PathLength(NavMeshPath path)
        {
            float len = 0f;
            Vector3[] c = path.corners;
            for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
            return len;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v : Vector3.forward;
        }

        static Transform Deep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = Deep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        // ================================================================ server: the pult
        /// <summary>Server: out of the forest at the base now (night or not).</summary>
        public void ServerComeNow()
        {
            if (!LesnikNav.Ready || _exits.Count == 0) return;
            _home = false;
            DropAll(Vector3.zero);
            Vector3 e = PickExit(transform.position, out Vector3 x) ? x : _exits[0];
            Appear(e, Quaternion.LookRotation(Flat(baseCenter - e)));
            SetState(LesnikState.Walk, "пульт: вышел к базе");
        }

        /// <summary>Server: home to the cave (asleep there if it is day and nobody is near).</summary>
        public void ServerHome()
        {
            _home = true;
            Vanish(1f, "пульт: домой");
        }

        /// <summary>Server: one more theft tonight.</summary>
        public void ServerAllowTheft()
        {
            _theftDone.Value = false;
            _preyAgain = 0f;
        }

        /// <summary>Server: to the nearest peek spot with someone inside, now.</summary>
        public bool ServerPeekNow()
        {
            _peekAgain = 0f;
            if (!ChooseSpot(out LesnikSpot s)) return false;
            _spot = s;
            SetState(LesnikState.Peek, "пульт: к окну " + s.name);
            return true;
        }
    }
}
