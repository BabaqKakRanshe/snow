using System.Collections.Generic;
using UnityEngine;

namespace Snow
{
    /// <summary>
    /// One look for everything the player sees on screen (UI pick 23.10, docs/gdd/research/RESEARCH_UI.md; mock-ups on
    /// the canvas https://claude.ai/artifact/Lb5wh3xgBr5E4LMMCTdimc): dark plates with a thin light edge, keys in a warm
    /// frame, a warm accent bar on cards, white text, corner marks with a dark outline so they read on snow and on
    /// night. Sizes are written for a 1080-line screen and scaled with its height (<see cref="S"/>): the HUD keeps its
    /// proportions from Steam Deck to 4K, key text stays at 22 px or more on 1080 (XAG 101 asks 18 on PC). IMGUI only:
    /// call from OnGUI on Repaint.
    /// </summary>
    public static class Hud
    {
        public static readonly Color Warm = new Color(1f, 0.84f, 0.43f);
        public static readonly Color StateColor = new Color(1f, 0.77f, 0.42f);
        public static readonly Color PanelColor = new Color(0.063f, 0.067f, 0.075f, 0.84f);
        public static readonly Color Muted = new Color(1f, 1f, 1f, 0.55f);
        public static readonly Color Paper = new Color(0.95f, 0.925f, 0.863f);
        public static readonly Color Translation = new Color(0.776f, 0.816f, 0.855f);

        /// <summary>Screen height / 1080, not below 0.7 (Steam Deck's 800 lines give 0.74).</summary>
        public static float S => Mathf.Max(0.7f, Screen.height / 1080f);

        /// <summary>A size written for 1080 lines, on this screen, whole pixels.</summary>
        public static float Px(float designPx) => Mathf.Round(designPx * S);

        /// <summary>Title-safe margins (96 px at the sides, 40 at top and bottom on 1080).</summary>
        public static float SafeX => Px(96f);
        public static float SafeY => Px(40f);

        static readonly Dictionary<int, GUIStyle> Styles = new Dictionary<int, GUIStyle>();
        static float _stylesScale = -1f;

        /// <summary>A label style: size in 1080-px, bold, anchor, wrap. Cached; rebuilt when the screen height changes.</summary>
        public static GUIStyle Text(int designPx, bool bold = false, TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false)
        {
            float s = S;
            if (!Mathf.Approximately(s, _stylesScale)) { Styles.Clear(); _stylesScale = s; }
            int key = designPx * 64 + (bold ? 32 : 0) + (int)anchor * 2 + (wrap ? 1 : 0);
            if (Styles.TryGetValue(key, out GUIStyle st)) return st;
            st = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(9, Mathf.RoundToInt(designPx * s)), fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = anchor, wordWrap = wrap, clipping = TextClipping.Overflow, richText = false,
                padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
            };
            st.normal.textColor = Color.white;
            Styles[key] = st;
            return st;
        }

        public static Vector2 Size(string text, GUIStyle style)
        {
            Vector2 v = style.CalcSize(new GUIContent(text));
            return new Vector2(Mathf.Ceil(v.x), Mathf.Ceil(v.y));
        }

        public static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        public static void Outline(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y + t, t, r.height - 2f * t), c);
            Fill(new Rect(r.xMax - t, r.y + t, t, r.height - 2f * t), c);
        }

        static Color A(Color c, float alpha) => new Color(c.r, c.g, c.b, c.a * alpha);

        /// <summary>Dark plate with a 1 px light edge; accent = a 3 px warm bar on the left (cards, messages).</summary>
        public static void Panel(Rect r, float alpha = 1f, bool accent = false)
        {
            Fill(r, A(PanelColor, alpha));
            Outline(r, new Color(1f, 1f, 1f, 0.07f * alpha), Mathf.Max(1f, Mathf.Round(S)));
            if (accent) Fill(new Rect(r.x, r.y, Px(3f), r.height), A(Warm, alpha));
        }

        /// <summary>Text, optionally with a 1 px dark shadow for text without a plate.</summary>
        public static void Label(Rect r, string text, GUIStyle style, Color color, bool shadow = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (shadow)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.85f * color.a);
                float o = Mathf.Max(1f, Mathf.Round(S));
                GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), text, style);
            }
            GUI.color = color;
            GUI.Label(r, text, style);
            GUI.color = Color.white;
        }

        // ---------------------------------------------------------------- keys
        /// <summary>"[E] взять" → key "E", text "взять". False when the text has no key in front.</summary>
        public static bool SplitKey(string action, out string key, out string text)
        {
            key = null;
            text = action;
            if (string.IsNullOrEmpty(action) || action[0] != '[') return false;
            int end = action.IndexOf(']');
            if (end < 2) return false;
            key = action.Substring(1, end - 1);
            text = action.Substring(end + 1).TrimStart();
            return true;
        }

        static int CapFont(float capHeightDesign) => Mathf.RoundToInt(capHeightDesign * 0.57f);

        /// <summary>Width of a key cap of this height (1080-px) for this key name.</summary>
        public static float KeyWidth(string key, float capHeightDesign)
        {
            float textW = Size(key, Text(CapFont(capHeightDesign), true, TextAnchor.MiddleCenter)).x;
            return Mathf.Max(Px(capHeightDesign), textW + Px(12f));
        }

        /// <summary>A key in a warm frame, x/y = top left; returns its width.</summary>
        public static float Key(float x, float y, string key, float capHeightDesign, float alpha = 1f)
        {
            float w = KeyWidth(key, capHeightDesign), h = Px(capHeightDesign);
            var r = new Rect(x, y, w, h);
            Fill(r, new Color(0f, 0f, 0f, 0.25f * alpha));
            Outline(r, A(Warm, alpha), Mathf.Max(1f, Px(capHeightDesign >= 27f ? 2f : 1.5f)));
            Label(r, key, Text(CapFont(capHeightDesign), true, TextAnchor.MiddleCenter), A(Warm, alpha));
            return w;
        }

        /// <summary>Width of "[key] text" drawn by <see cref="KeyText"/>.</summary>
        public static float KeyTextWidth(string key, string text, int textPx)
        {
            float cap = textPx + (textPx >= 21 ? 6f : 7f);
            float w = Size(text, Text(textPx)).x;
            return string.IsNullOrEmpty(key) ? w : KeyWidth(key, cap) + Px(textPx >= 21 ? 10f : 6f) + w;
        }

        /// <summary>A key cap and its text on one line, vertically centred in a row of height h; returns the width.</summary>
        public static float KeyText(float x, float y, float h, string key, string text, int textPx, Color textColor, float alpha = 1f)
        {
            float cap = textPx + (textPx >= 21 ? 6f : 7f);
            float cx = x;
            if (!string.IsNullOrEmpty(key))
            {
                cx += Key(x, y + (h - Px(cap)) * 0.5f, key, cap, alpha) + Px(textPx >= 21 ? 10f : 6f);
            }
            GUIStyle st = Text(textPx);
            float w = Size(text, st).x;
            Label(new Rect(cx, y, w + 2f, h), text, st, A(textColor, alpha));
            return cx + w - x;
        }

        /// <summary>Several "[key] text" pairs in a row with a gap; returns the total width (draw = false: only measure).</summary>
        public static float Hints(float x, float y, float h, IList<(string key, string text)> hints, int textPx, float alpha, bool draw = true)
        {
            float gap = Px(14f), cx = x;
            for (int i = 0; i < hints.Count; i++)
            {
                if (i > 0) cx += gap;
                cx += draw ? KeyText(cx, y, h, Loc.T(hints[i].key), Loc.T(hints[i].text), textPx, new Color(1f, 1f, 1f, 0.88f), alpha)
                           : KeyTextWidth(Loc.T(hints[i].key), Loc.T(hints[i].text), textPx);
            }
            return cx - x;
        }

        // ---------------------------------------------------------------- marks and bars
        /// <summary>Corner marks around r with a dark outline under them (readable on bright snow).</summary>
        public static void Corners(Rect r, Color c, float thick, float arm)
        {
            float o = Mathf.Max(1f, Mathf.Round(S));
            var dark = new Color(0f, 0f, 0f, 0.55f * c.a);
            CornerBars(new Rect(r.x - o, r.y - o, r.width + 2f * o, r.height + 2f * o), dark, thick + 2f * o, arm + 2f * o);
            CornerBars(r, c, thick, arm);
        }

        static void CornerBars(Rect r, Color c, float t, float l)
        {
            l = Mathf.Min(l, Mathf.Min(r.width, r.height) * 0.45f);
            Fill(new Rect(r.x, r.y, l, t), c); Fill(new Rect(r.x, r.y, t, l), c);
            Fill(new Rect(r.xMax - l, r.y, l, t), c); Fill(new Rect(r.xMax - t, r.y, t, l), c);
            Fill(new Rect(r.x, r.yMax - t, l, t), c); Fill(new Rect(r.x, r.yMax - l, t, l), c);
            Fill(new Rect(r.xMax - l, r.yMax - t, l, t), c); Fill(new Rect(r.xMax - t, r.yMax - l, t, l), c);
        }

        /// <summary>Progress bar: a faint track and a warm fill (k = 0..1).</summary>
        public static void Bar(Rect r, float k, float alpha = 1f)
        {
            Fill(r, new Color(1f, 1f, 1f, 0.15f * alpha));
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(k), r.height), A(Warm, alpha));
        }

        /// <summary>Thin divider line inside a card.</summary>
        public static void Divider(float x, float y, float w, float alpha = 1f) =>
            Fill(new Rect(x, y, w, Mathf.Max(1f, Mathf.Round(S))), new Color(1f, 1f, 1f, 0.14f * alpha));
    }
}
