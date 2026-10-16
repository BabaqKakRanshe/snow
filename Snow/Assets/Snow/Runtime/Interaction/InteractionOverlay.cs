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
    ///  - Reveal key held (TEMPORARY B, action Reveal in SnowControls.inputactions): faint corners on everything interactive within revealRadius that the player could actually reach
    ///    with a ray from the eye — the same InteractionSensor.Pick rule as "use", so a handle behind a closed door or
    ///    a crate behind a wall is not shown. Things out of view get an arrow at the screen edge; the few marks nearest
    ///    the screen centre get their names.
    /// </summary>
    public class InteractionOverlay : MonoBehaviour
    {
        public InteractionSensor sensor;
        public PlayerController controller;
        public FirstPersonView view;
        [Tooltip("m: how far the reveal key (B) shows interactive things")] public float revealRadius = 4f;
        [Tooltip("m: a zone's parent mesh larger than this is framed by the zone box instead")] public float maxMeshSize = 0.8f;
        [Tooltip("Variant 1: the hover card only while the reveal key (B) is held (a clean screen otherwise)")] public bool cardOnlyWithAlt;
        [Tooltip("Variant 3: how many revealed marks near the screen centre get their names")] public int namedMarks = 6;
        [Tooltip("Variant 3: only marks within this fraction of the screen height from the centre are named")] public float namedRadius = 0.3f;
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
        readonly List<string> _actions = new List<string>();
        readonly Vector3[] _corners = new Vector3[8];
        float _nextScan;
        bool _hasHover;
        Rect _hoverRect;
        string _title;
        GUIStyle _titleStyle, _actionStyle, _nameStyle;

        void LateUpdate()
        {
            _marks.Clear();
            _arrows.Clear();
            _arrowAngles.Clear();
            _hasHover = false;
            Camera cam = view != null ? view.cam : null;
            if (Application.isBatchMode || cam == null || !cam.enabled || sensor == null || controller == null || controller.Source == null) return;
            if (controller.Source.Context == InputContext.None) return;
            bool reveal = controller.Frame.Reveal;

            // hover card
            Component target = sensor.TargetVolume != null ? sensor.TargetVolume
                : sensor.TargetCargo != null ? sensor.TargetCargo
                : sensor.TargetGrab != null ? (Component)sensor.TargetGrab : null;
            if (target != null && (reveal || !cardOnlyWithAlt) && ScreenRect(Bounds(target), cam, out _hoverRect))
            {
                _hasHover = true;
                Describe(target);
            }
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
                    AddArrow(cam, b.center);
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

        void AddArrow(Camera cam, Vector3 world)
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
        }

        static string Name(Component c) => c switch
        {
            UtilityPlaneInteractable z => z.label,
            CargoItem cargo => cargo.label,
            Grabbable g => g.label,
            _ => null,
        };

        void Describe(Component target)
        {
            _actions.Clear();
            string key = sensor.UseKey;
            if (target is UtilityPlaneInteractable zone)
            {
                InteractionHost host = sensor.TargetHost;
                string state = host != null ? host.Describe(zone.id) : null;
                _title = string.IsNullOrEmpty(state) ? zone.label : $"{zone.label} — {state}";
                _actions.Add($"[{key}] {(host != null ? host.ActionText(zone.id) : "использовать")}");
            }
            else if (target is CargoItem cargo)
            {
                _title = $"{cargo.label} · {cargo.massKg:0} кг · {cargo.Describe()}";
                _actions.Add("[E] держать — нести");
                _actions.Add(cargo.State == CargoState.Secured ? "[R] открепить" : "[R] закрепить");
            }
            else if (target is Grabbable grab)
            {
                _title = grab.label;
                _actions.Add("нельзя взять");
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
                Vector3 s = cam.WorldToScreenPoint(w);
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

        // ---------------------------------------------------------------- drawing
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();

            float named = namedRadius * Screen.height;
            for (int i = 0; i < _marks.Count; i++)
            {
                Mark m = _marks[i];
                Corners(m.Rect, revealColor, 2f);
                if (i < namedMarks && m.CenterDistance <= named && !string.IsNullOrEmpty(m.Name))
                    Label(new Vector2(m.Rect.x, m.Rect.yMax + 2f), m.Name, _nameStyle, 0.6f);
            }
            for (int i = 0; i < _arrows.Count; i++) Arrow(_arrows[i], _arrowAngles[i]);

            if (!_hasHover) return;
            Corners(_hoverRect, frameColor, 3f);
            // name and state under the frame, actions to the right of it; every box clamped inside the screen
            Label(new Vector2(_hoverRect.x, _hoverRect.yMax + 4f), _title, _titleStyle, panelColor.a);
            float y = _hoverRect.y;
            foreach (string a in _actions)
            {
                Vector2 size = Size(a, _actionStyle);
                float x = _hoverRect.xMax + 6f;
                if (x + size.x > Screen.width - 4f) x = _hoverRect.x - size.x - 6f;
                Label(new Vector2(x, y), a, _actionStyle, panelColor.a);
                y += size.y + 2f;
            }
        }

        void EnsureStyles()
        {
            if (_titleStyle != null) return;
            // single line, no clipping: the box is sized to the text (the default label wraps and cut the text, 30.09)
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow,
                padding = new RectOffset(8, 8, 4, 4), margin = new RectOffset(0, 0, 0, 0),
            };
            _titleStyle.normal.textColor = Color.white;
            _actionStyle = new GUIStyle(_titleStyle) { fontSize = 15 };
            _nameStyle = new GUIStyle(_titleStyle) { fontSize = 13, padding = new RectOffset(5, 5, 2, 2) };
        }

        static Vector2 Size(string text, GUIStyle style)
        {
            Vector2 s = style.CalcSize(new GUIContent(text));
            return new Vector2(Mathf.Ceil(s.x) + 2f, Mathf.Ceil(s.y) + 2f);
        }

        void Label(Vector2 at, string text, GUIStyle style, float alpha)
        {
            if (string.IsNullOrEmpty(text)) return;
            Vector2 s = Size(text, style);
            var r = new Rect(Mathf.Clamp(at.x, 4f, Mathf.Max(4f, Screen.width - s.x - 4f)),
                             Mathf.Clamp(at.y, 4f, Mathf.Max(4f, Screen.height - s.y - 4f)), s.x, s.y);
            GUI.color = new Color(panelColor.r, panelColor.g, panelColor.b, alpha);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(r, text, style);
        }

        void Arrow(Vector2 at, float angle)
        {
            // a chevron pointing outwards: two short bars rotated about the edge point
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, at);
            GUI.color = revealColor;
            GUIUtility.RotateAroundPivot(35f, at);
            GUI.DrawTexture(new Rect(at.x - 14f, at.y - 1.5f, 14f, 3f), Texture2D.whiteTexture);
            GUIUtility.RotateAroundPivot(-70f, at);
            GUI.DrawTexture(new Rect(at.x - 14f, at.y - 1.5f, 14f, 3f), Texture2D.whiteTexture);
            GUI.matrix = saved;
            GUI.color = Color.white;
        }

        static void Corners(Rect r, Color color, float t)
        {
            float l = Mathf.Min(14f, Mathf.Min(r.width, r.height) * 0.3f);
            GUI.color = color;
            Tex(r.x, r.y, l, t); Tex(r.x, r.y, t, l);
            Tex(r.xMax - l, r.y, l, t); Tex(r.xMax - t, r.y, t, l);
            Tex(r.x, r.yMax - t, l, t); Tex(r.x, r.yMax - l, t, l);
            Tex(r.xMax - l, r.yMax - t, l, t); Tex(r.xMax - t, r.yMax - l, t, l);
            GUI.color = Color.white;
        }

        static void Tex(float x, float y, float w, float h) => GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
    }
}
