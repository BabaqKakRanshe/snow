using System.Collections.Generic;
using Snow.Cargo;
using Snow.Character;
using Snow.Input;
using Snow.Player;
using UnityEngine;
using UtilityPlane;

namespace Snow.Interaction
{
    /// <summary>
    /// Owner-side hover card and "what can I use here" reveal (INPUT-02: small hover hints — name, state, action; no
    /// permanent HUD). Drawing only: targets and commands stay with InteractionSensor.
    ///  - Hover: corner frame around the target, name and state below, actions with keys beside it. Optionally only
    ///    while the reveal key is held (cardOnlyWithAlt).
    ///  - Reveal key held (Tab on foot, B in a seat; action Reveal in SnowControls.inputactions): faint corners on everything interactive within revealRadius that the player could actually reach
    ///    with a ray from the eye — the same InteractionSensor.Pick rule as "use", so a handle behind a closed door or
    ///    a crate behind a wall is not shown. Things out of view get an arrow at the screen edge.
    /// UI pick 26.10 (variant C «Один столбец», docs/gdd/research/RESEARCH_UI_3.md): all text in the left column, typed, no
    /// plates and no key caps. The reveal numbers the marks (nearest the centre first) and lists them as "Nahe · рядом" above
    /// the pockets list (PocketsHud.ColumnTop), the ones out of view with an arrow; the hover card is typed lines beside the
    /// target (name, state, "E  открыть"). No card while a device is raised (InteractionSensor.DeviceRaised).
    /// </summary>
    public class InteractionOverlay : MonoBehaviour
    {
        public InteractionSensor sensor;
        public PlayerController controller;
        public FirstPersonView view;
        [Tooltip("m: how far the reveal key (Tab / B in a seat) shows interactive things")] public float revealRadius = 4f;
        [Tooltip("m: a zone's parent mesh larger than this is framed by the zone box instead")] public float maxMeshSize = 0.8f;
        [Tooltip("Variant 1: the hover card only while the reveal key (Tab / B) is held (a clean screen otherwise)")] public bool cardOnlyWithAlt;
        [Tooltip("Variant C: how many revealed things the Tab list names (on screen first, then out of view)")] public int namedMarks = 8;
        [Tooltip("Variant 2: most edge arrows at once")] public int maxArrows = 10;
        public Color frameColor = new Color(1f, 1f, 1f, 0.95f);
        public Color revealColor = new Color(1f, 1f, 1f, 0.5f);
        public Color panelColor = new Color(0.08f, 0.08f, 0.09f, 0.8f);

        struct Mark
        {
            public Rect Rect;
            public string Name;
            public float CenterDistance;
        }

        readonly List<Component> _all = new List<Component>();
        readonly List<Mark> _marks = new List<Mark>();
        readonly List<Vector2> _arrows = new List<Vector2>();      // screen-edge point per arrow, GUI space
        readonly List<float> _arrowAngles = new List<float>();
        readonly List<string> _arrowNames = new List<string>();
        readonly List<string> _actions = new List<string>();
        readonly Vector3[] _corners = new Vector3[8];
        float _nextScan;
        bool _hasHover, _reveal;
        Rect _hoverRect;
        string _title;

        void LateUpdate()
        {
            _marks.Clear();
            _arrows.Clear();
            _arrowAngles.Clear();
            _arrowNames.Clear();
            _hasHover = false;
            _reveal = false;
            if (sensor != null) sensor.HoldShownByCard = false;
            Camera cam = view != null ? view.cam : null;
            if (Application.isBatchMode || cam == null || !cam.enabled || sensor == null || controller == null || controller.Source == null
                || controller.Source.Context == InputContext.None)
            {
                if (sensor != null) sensor.CardRect = default;        // no card drawn: messages go back to their own place
                return;
            }
            bool reveal = controller.Frame.Reveal;
            if (reveal && !_reveal) Snow.Audio.SnowAudio.PlayUi("ui.reveal", 0.6f);   // the reach list opens
            _reveal = reveal;

            // hover card
            Component target = sensor.TargetVolume != null ? sensor.TargetVolume
                : sensor.TargetCargo != null ? sensor.TargetCargo
                : sensor.TargetGrab != null ? (Component)sensor.TargetGrab : null;
            if (target != _hoverSound)                            // a soft tick on a new target (SOUND_LIST_FULL ui.hover)
            {
                if (target != null) Snow.Audio.SnowAudio.PlayUi("ui.hover", 0.35f);
                _hoverSound = target;
            }
            if (target != null && !InteractionSensor.DeviceRaised && (reveal || !cardOnlyWithAlt) && ScreenRect(Bounds(target), cam, out _hoverRect))
            {
                _hasHover = true;
                sensor.HoldShownByCard = true;                    // the hold bar goes inside the card
                Describe(target);
            }
            if (!_hasHover) sensor.CardRect = default;            // else OnGUI keeps it (a frame old at most)
            if (!reveal) return;

            // reveal key: everything the player could reach from here
            if (Time.time >= _nextScan) Rescan();
            Vector3 eye = view.EyePosition;
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            foreach (Component c in _all)
            {
                if (c == null || c == target || !c.gameObject.activeInHierarchy) continue;
                Bounds b = Bounds(c);
                float dist = Vector3.Distance(eye, b.center);
                if (dist > revealRadius || !Reachable(c, b, eye)) continue;
                if (ScreenRect(b, cam, out Rect r))
                    _marks.Add(new Mark { Rect = r, Name = Name(c), CenterDistance = Vector2.Distance(r.center, center) });
                else if (_arrows.Count < maxArrows)
                    AddArrow(cam, b.center, Name(c));
            }
            _marks.Sort((a, m) => a.CenterDistance.CompareTo(m.CenterDistance));
        }

        void Rescan()
        {
            _nextScan = Time.time + 1f;
            _all.Clear();
            foreach (var host in FindObjectsByType<InteractionHost>(FindObjectsSortMode.None))
                foreach (var v in host.Volumes) _all.Add(v);
            foreach (var c in FindObjectsByType<CargoItem>(FindObjectsSortMode.None)) _all.Add(c);
            foreach (var g in FindObjectsByType<Grabbable>(FindObjectsSortMode.None)) _all.Add(g);
        }

        // Would a ray from the eye to the object reach it (sensor rule)? The centre, and for items also four points
        // around it, so a crate half behind a wall still shows.
        bool Reachable(Component c, Bounds b, Vector3 eye)
        {
            if (Hits(c, eye, b.center)) return true;
            if (c is UtilityPlaneInteractable) return false;
            Vector3 e = b.extents * 0.6f;
            return Hits(c, eye, b.center + new Vector3(e.x, e.y, 0f)) || Hits(c, eye, b.center + new Vector3(-e.x, e.y, 0f))
                || Hits(c, eye, b.center + new Vector3(0f, e.y, e.z)) || Hits(c, eye, b.center + new Vector3(0f, e.y, -e.z));
        }

        bool Hits(Component c, Vector3 eye, Vector3 point)
        {
            Vector3 d = point - eye;
            float len = d.magnitude;
            if (len < 1e-3f) return true;
            return sensor.Pick(new Ray(eye, d / len), len + 0.3f, out _) == c;
        }

        void AddArrow(Camera cam, Vector3 world, string name)
        {
            // direction on screen from the centre towards the object (mirrored when it is behind)
            Vector3 v = cam.WorldToViewportPoint(world);
            Vector2 dir = new Vector2(v.x - 0.5f, v.y - 0.5f);
            if (v.z < 0f) dir = -dir;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;
            dir.Normalize();
            float angle = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;                   // GUI y grows down
            foreach (float a in _arrowAngles) if (Mathf.Abs(Mathf.DeltaAngle(a, angle)) < 6f) return;   // one per direction
            float hw = Screen.width * 0.5f - 24f, hh = Screen.height * 0.5f - 24f;
            float k = Mathf.Min(hw / Mathf.Max(1e-4f, Mathf.Abs(dir.x)), hh / Mathf.Max(1e-4f, Mathf.Abs(dir.y)));
            _arrows.Add(new Vector2(Screen.width * 0.5f + dir.x * k, Screen.height * 0.5f - dir.y * k));
            _arrowAngles.Add(angle);
            _arrowNames.Add(name);
        }

        static string Name(Component c) => Loc.T(c switch
        {
            UtilityPlaneInteractable z => z.label,
            CargoItem cargo => cargo.label,
            Grabbable g => g.label,
            _ => null,
        });

        Component _hoverSound;

        void Describe(Component target)
        {
            _actions.Clear();
            string key = sensor.UseKey;
            if (target is UtilityPlaneInteractable zone)
            {
                InteractionHost host = sensor.TargetHost;
                string state = host != null ? host.Describe(zone.id) : null;
                _title = string.IsNullOrEmpty(state) ? Loc.T(zone.label) : $"{Loc.T(zone.label)} — {Loc.T(state)}";
                _actions.Add($"[{Loc.T(key)}] {Loc.T(host != null ? host.ActionText(zone.id) : "использовать")}");
            }
            else if (target is CargoItem cargo)
            {
                _title = Loc.T($"{cargo.label} · {cargo.massKg:0.##} кг · {cargo.Describe()}");
                if (cargo.Pocketable)
                    _actions.Add(Loc.T(sensor.net != null && CargoItem.FreeSlot(sensor.net.OwnerClientId) < 0 ? "[E] взять — карманы полны" : "[E] взять"));
                else
                {
                    _actions.Add(Loc.T(sensor.HeldHeavy ? "[E] поставить то, что несёте" : "[E] нести обеими руками"));
                    _actions.Add(Loc.T(cargo.State == CargoState.Secured ? "[R] открепить" : "[R] закрепить"));
                }
            }
            else if (target is Grabbable grab)
            {
                _title = Loc.T(grab.label);
                _actions.Add(Loc.T("нельзя взять"));
            }
        }

        /// <summary>World box to frame: the zone's own small control mesh, else the zone box; all renderers of an item.</summary>
        Bounds Bounds(Component c)
        {
            if (c is UtilityPlaneInteractable zone)
            {
                Transform parent = zone.transform.parent;
                Renderer r = parent != null ? parent.GetComponent<Renderer>() : null;
                if (r != null && r.enabled && Mathf.Max(r.bounds.size.x, Mathf.Max(r.bounds.size.y, r.bounds.size.z)) <= maxMeshSize)
                    return r.bounds;
                var col = zone.GetComponent<Collider>();
                return col != null ? col.bounds : new Bounds(zone.transform.position, Vector3.one * 0.05f);
            }
            bool any = false;
            var b = new Bounds(c.transform.position, Vector3.zero);
            foreach (var r in c.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || !HoverOutline.Outlined(r)) continue;   // J.2: no Glow_* / Beam_* in the frame
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // GUI-space rectangle around the projected box; false if behind the camera or off screen
        bool ScreenRect(Bounds b, Camera cam, out Rect rect)
        {
            rect = default;
            Vector3 mn = b.min, mx = b.max;
            for (int i = 0; i < 8; i++)
                _corners[i] = new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (Vector3 w in _corners)
            {
                // viewport, not camera pixels: with the PS1 frame the camera renders into a ~427 x 240 RenderTexture
                Vector3 s = cam.WorldToViewportPoint(w);
                s.x *= Screen.width;
                s.y *= Screen.height;
                if (s.z < 0.05f) return false;
                x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x);
                y0 = Mathf.Min(y0, s.y); y1 = Mathf.Max(y1, s.y);
            }
            const float pad = 6f, min = 22f;
            float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
            float w2 = Mathf.Max(min, x1 - x0 + 2f * pad) * 0.5f, h2 = Mathf.Max(min, y1 - y0 + 2f * pad) * 0.5f;
            if (cx + w2 < 0f || cx - w2 > Screen.width || cy + h2 < 0f || cy - h2 > Screen.height) return false;
            rect = new Rect(cx - w2, Screen.height - (cy + h2), w2 * 2f, h2 * 2f);
            return true;
        }

        // ---------------------------------------------------------------- drawing (UI pick 26.10: variant C, typed lines)
        static readonly Color Dim = new Color(0.663f, 0.651f, 0.612f);

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;

            Color warm = new Color(Hud.Warm.r, Hud.Warm.g, Hud.Warm.b, 0.95f);
            for (int i = 0; i < _marks.Count; i++)
            {
                Mark m = _marks[i];
                Hud.Corners(m.Rect, revealColor, Hud.Px(1.5f), Hud.Px(10f));
                if (i < namedMarks && !string.IsNullOrEmpty(m.Name))                       // its number, as in the list
                    Hud.Typed(m.Rect.x - Hud.Px(2f), m.Rect.y - Hud.Px(24f), (i + 1).ToString(), Hud.Mono(19), warm);
            }
            for (int i = 0; i < _arrows.Count; i++) Arrow(_arrows[i], _arrowAngles[i]);
            if (_reveal) RevealList();
            if (_hasHover) Card();
        }

        // while the reveal key is held: "Nahe · рядом, 4 м", then the numbered things (on screen nearest the centre first,
        // then the ones out of view with an arrow), above the pockets list in the left column
        void RevealList()
        {
            GUIStyle st = Hud.Mono(21), small = Hud.Mono(18);
            float lh = Hud.Px(27f), x = Hud.SafeX;
            var rows = new List<(string text, bool away)>();
            for (int i = 0; i < _marks.Count && rows.Count < namedMarks; i++)
                if (!string.IsNullOrEmpty(_marks[i].Name)) rows.Add(((i + 1) + "  " + _marks[i].Name, false));
            for (int i = 0; i < _arrowNames.Count && rows.Count < namedMarks; i++)
            {
                if (string.IsNullOrEmpty(_arrowNames[i])) continue;
                float a = Mathf.Repeat(_arrowAngles[i] + 180f, 360f) - 180f;              // 0 = right, ±180 = left
                rows.Add(("   " + _arrowNames[i] + (Mathf.Abs(a) > 90f ? "  ←" : "  →"), true));
            }
            float y = PocketsHud.ColumnTop - Hud.Px(14f) - lh * (1 + Mathf.Max(1, rows.Count));
            float hw = Hud.Typed(x, y, "Nahe", st, Hud.Warm);
            Hud.Typed(x + hw + Hud.Px(16f), y + Hud.Px(3f), Loc.T(string.Format("рядом, {0:0} м", revealRadius)), small, Dim);
            y += lh;
            if (rows.Count == 0) Hud.Typed(x, y, Loc.T("ничего"), st, new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.7f));
            foreach (var (text, away) in rows)
            {
                Hud.Typed(x, y, text, st, away ? new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.7f) : Hud.Ink);
                y += lh;
            }
        }

        // corner marks on the target (at least 56 px, so a small switch still gets a frame) and typed lines beside it:
        // the name, the state (warm), the actions ("E  открыть": the key warm, no cap), a thin hold bar under them
        void Card()
        {
            Rect t = _hoverRect;
            float min = Hud.Px(56f);
            if (t.width < min) { t.x -= (min - t.width) * 0.5f; t.width = min; }
            if (t.height < min) { t.y -= (min - t.height) * 0.5f; t.height = min; }
            Hud.Corners(t, frameColor, Hud.Px(2f), Hud.Px(18f));

            SplitTitle(_title, out string name, out string state);
            bool hasState = !string.IsNullOrEmpty(state);
            GUIStyle nameSt = Hud.Mono(22), stateSt = Hud.Mono(19), actSt = Hud.Mono(21);
            float lh = Hud.Px(28f), gap = Hud.Px(10f);
            float w = Hud.Size(name, nameSt).x;
            if (hasState) w = Mathf.Max(w, Hud.Size(state, stateSt).x);
            foreach (string a in _actions)
            {
                Hud.SplitKey(a, out string k, out string txt);
                w = Mathf.Max(w, (string.IsNullOrEmpty(k) ? 0f : Hud.Size(k, actSt).x + Hud.Px(12f)) + Hud.Size(txt, actSt).x);
            }
            float hold = sensor != null ? sensor.HoldProgress : -1f;
            float h = lh + (hasState ? lh : 0f) + Hud.Px(4f) + _actions.Count * lh + (hold >= 0f ? Hud.Px(12f) : 0f);
            float x = t.xMax + Hud.Px(14f);
            if (x + w > Screen.width - Hud.Px(8f)) x = t.x - Hud.Px(14f) - w;
            Rect r = Clamp(new Rect(x, t.y, w, h));
            if (sensor != null) sensor.CardRect = Rect.MinMaxRect(Mathf.Min(t.xMin, r.xMin), Mathf.Min(t.yMin, r.yMin), Mathf.Max(t.xMax, r.xMax), Mathf.Max(t.yMax, r.yMax));

            float cy = r.y;
            Hud.Typed(r.x, cy, name, nameSt, Hud.Ink);
            cy += lh;
            if (hasState)
            {
                Hud.Typed(r.x, cy + Hud.Px(2f), state, stateSt, Hud.StateColor);
                cy += lh;
            }
            cy += Hud.Px(4f);
            foreach (string a in _actions)
            {
                bool hasKey = Hud.SplitKey(a, out string k, out string txt);
                float cx = r.x;
                if (hasKey) cx += Hud.Typed(cx, cy, k, actSt, Hud.Warm) + Hud.Px(12f);
                Hud.Typed(cx, cy, txt, actSt, hasKey ? Hud.Ink : Dim);
                cy += lh;
            }
            if (hold >= 0f) Hud.Bar(new Rect(r.x, cy + Hud.Px(4f), w, Hud.Px(3f)), hold);
        }

        // "Шкафчик с ключами — закрыт" / "Кружка · 0,3 кг · свободен": the name, then the state after the first separator
        static void SplitTitle(string title, out string name, out string state)
        {
            name = title ?? "";
            state = null;
            int i = name.IndexOf(" — ", System.StringComparison.Ordinal);
            if (i < 0) i = name.IndexOf(" · ", System.StringComparison.Ordinal);
            if (i < 0) return;
            state = name.Substring(i + 3);
            name = name.Substring(0, i);
        }

        static Rect Clamp(Rect r)
        {
            float m = Hud.Px(8f);
            r.x = Mathf.Clamp(r.x, m, Mathf.Max(m, Screen.width - r.width - m));
            r.y = Mathf.Clamp(r.y, m, Mathf.Max(m, Screen.height - r.height - m));
            return r;
        }

        void Arrow(Vector2 at, float angle)
        {
            // a chevron pointing outwards: two short bars rotated about the edge point
            float l = Hud.Px(14f), t = Hud.Px(3f);
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, at);
            GUI.color = revealColor;
            GUIUtility.RotateAroundPivot(35f, at);
            GUI.DrawTexture(new Rect(at.x - l, at.y - t * 0.5f, l, t), Texture2D.whiteTexture);
            GUIUtility.RotateAroundPivot(-70f, at);
            GUI.DrawTexture(new Rect(at.x - l, at.y - t * 0.5f, l, t), Texture2D.whiteTexture);
            GUI.matrix = saved;
            GUI.color = Color.white;
        }
    }
}
