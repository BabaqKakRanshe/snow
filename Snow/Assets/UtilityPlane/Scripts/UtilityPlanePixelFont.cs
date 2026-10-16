using System.Collections.Generic;
using UnityEngine;

namespace UtilityPlane
{
    /// <summary>
    /// v12: the two pixel fonts of the instrument atlas (T_Gauges_BC) for text the game draws itself: radio, GPS and
    /// transponder screens (Display_* quads), the Hobbs counter, any in-cabin readout. Same glyphs as the panel legends,
    /// so screen text matches the PSX cockpit and stays sharp with Point filtering.
    ///   Large57: 5 x 7 px capitals and digits; Small35: 3 x 5 px (M, N, W are wider). 1 px between glyphs.
    /// Legibility from the seat at 1920 x 1080: give one texel about 1 mm on the screen quad (Display_COM1 48 x 17 mm ->
    /// a 48 x 17 texture) and write Large57: characters 7 mm high. Small35 at that scale is fine print (5 mm).
    /// Usage: rig.screensDrivenExternally = true;
    ///        var tex = UtilityPlanePixelFont.CreateTexture(48, 17, new Color32(0, 0, 0, 255));
    ///        UtilityPlanePixelFont.Draw(tex, "118.30", 3, 5, new Color32(120, 255, 140, 255)); tex.Apply(false);
    ///        UtilityPlanePixelFont.SetScreen(displayCom1Renderer.sharedMaterial, tex, Color.white);
    ///        (sharedMaterial after the rig's Awake is the rig's own instance for that screen; the base colour of M_Screen
    ///        stays dark, the text glows through the emission map like a backlit LCD.)
    /// Characters missing from the fonts draw as '?'. Lower case is drawn as capitals; '×' (U+00D7) is the small
    /// multiplication cross ("×100").
    /// </summary>
    public static class UtilityPlanePixelFont
    {
        public enum Size { Small35, Large57 }

        static readonly Dictionary<char, string> F57 = new Dictionary<char, string>
        {
            { 'A', ".###.|#...#|#...#|#####|#...#|#...#|#...#" },
            { 'B', "####.|#...#|#...#|####.|#...#|#...#|####." },
            { 'C', ".###.|#...#|#....|#....|#....|#...#|.###." },
            { 'D', "####.|#...#|#...#|#...#|#...#|#...#|####." },
            { 'E', "#####|#....|#....|####.|#....|#....|#####" },
            { 'F', "#####|#....|#....|####.|#....|#....|#...." },
            { 'G', ".###.|#...#|#....|#.###|#...#|#...#|.####" },
            { 'H', "#...#|#...#|#...#|#####|#...#|#...#|#...#" },
            { 'I', ".###.|..#..|..#..|..#..|..#..|..#..|.###." },
            { 'J', "..###|...#.|...#.|...#.|...#.|#..#.|.##.." },
            { 'K', "#...#|#..#.|#.#..|##...|#.#..|#..#.|#...#" },
            { 'L', "#....|#....|#....|#....|#....|#....|#####" },
            { 'M', "#...#|##.##|#.#.#|#.#.#|#...#|#...#|#...#" },
            { 'N', "#...#|#...#|##..#|#.#.#|#..##|#...#|#...#" },
            { 'O', ".###.|#...#|#...#|#...#|#...#|#...#|.###." },
            { 'P', "####.|#...#|#...#|####.|#....|#....|#...." },
            { 'Q', ".###.|#...#|#...#|#...#|#.#.#|#..#.|.##.#" },
            { 'R', "####.|#...#|#...#|####.|#.#..|#..#.|#...#" },
            { 'S', ".####|#....|#....|.###.|....#|....#|####." },
            { 'T', "#####|..#..|..#..|..#..|..#..|..#..|..#.." },
            { 'U', "#...#|#...#|#...#|#...#|#...#|#...#|.###." },
            { 'V', "#...#|#...#|#...#|#...#|#...#|.#.#.|..#.." },
            { 'W', "#...#|#...#|#...#|#.#.#|#.#.#|#.#.#|.#.#." },
            { 'X', "#...#|#...#|.#.#.|..#..|.#.#.|#...#|#...#" },
            { 'Y', "#...#|#...#|.#.#.|..#..|..#..|..#..|..#.." },
            { 'Z', "#####|....#|...#.|..#..|.#...|#....|#####" },
            { '0', ".###.|#...#|#..##|#.#.#|##..#|#...#|.###." },
            { '1', "..#..|.##..|..#..|..#..|..#..|..#..|.###." },
            { '2', ".###.|#...#|....#|...#.|..#..|.#...|#####" },
            { '3', "#####|...#.|..#..|...#.|....#|#...#|.###." },
            { '4', "...#.|..##.|.#.#.|#..#.|#####|...#.|...#." },
            { '5', "#####|#....|####.|....#|....#|#...#|.###." },
            { '6', "..##.|.#...|#....|####.|#...#|#...#|.###." },
            { '7', "#####|....#|...#.|..#..|.#...|.#...|.#..." },
            { '8', ".###.|#...#|#...#|.###.|#...#|#...#|.###." },
            { '9', ".###.|#...#|#...#|.####|....#|...#.|.##.." },
            { ' ', ".....|.....|.....|.....|.....|.....|....." },
            { '.', ".....|.....|.....|.....|.....|.##..|.##.." },
            { ',', ".....|.....|.....|.....|.##..|..#..|.#..." },
            { ':', ".....|.##..|.##..|.....|.##..|.##..|....." },
            { '-', ".....|.....|.....|#####|.....|.....|....." },
            { '+', ".....|..#..|..#..|#####|..#..|..#..|....." },
            { '/', "....#|...#.|...#.|..#..|.#...|.#...|#...." },
            { '%', "##..#|##..#|...#.|..#..|.#...|#..##|#..##" },
            { '°', ".##..|#..#.|#..#.|.##..|.....|.....|....." },
            { '\'', "..#..|..#..|.#...|.....|.....|.....|....." },
            { '(', "...#.|..#..|.#...|.#...|.#...|..#..|...#." },
            { ')', ".#...|..#..|...#.|...#.|...#.|..#..|.#..." },
            { '<', "...#.|..#..|.#...|#....|.#...|..#..|...#." },
            { '>', ".#...|..#..|...#.|....#|...#.|..#..|.#..." },
            { '=', ".....|.....|#####|.....|#####|.....|....." },
            { '•', ".....|.....|.###.|.###.|.###.|.....|....." },
            { '×', ".....|.....|#...#|.#.#.|..#..|.#.#.|#...#" },
            { '?', ".###.|#...#|....#|...#.|..#..|.....|..#.." },
            { '!', "..#..|..#..|..#..|..#..|..#..|.....|..#.." },
            { '_', ".....|.....|.....|.....|.....|.....|#####" },
            { '→', ".....|..#..|...#.|#####|...#.|..#..|....." },
            { '⇆', "..#..|.#...|#####|.....|#####|...#.|..#.." },
        };

        static readonly Dictionary<char, string> F35 = new Dictionary<char, string>
        {
            { 'A', ".#.|#.#|###|#.#|#.#" },
            { 'B', "##.|#.#|##.|#.#|##." },
            { 'C', ".##|#..|#..|#..|.##" },
            { 'D', "##.|#.#|#.#|#.#|##." },
            { 'E', "###|#..|##.|#..|###" },
            { 'F', "###|#..|##.|#..|#.." },
            { 'G', ".##|#..|#.#|#.#|.##" },
            { 'H', "#.#|#.#|###|#.#|#.#" },
            { 'I', "###|.#.|.#.|.#.|###" },
            { 'J', "..#|..#|..#|#.#|.#." },
            { 'K', "#.#|#.#|##.|#.#|#.#" },
            { 'L', "#..|#..|#..|#..|###" },
            { 'M', "#...#|##.##|#.#.#|#...#|#...#" },
            { 'N', "#..#|##.#|#.##|#..#|#..#" },
            { 'O', ".#.|#.#|#.#|#.#|.#." },
            { 'P', "##.|#.#|##.|#..|#.." },
            { 'Q', ".#.|#.#|#.#|##.|.##" },
            { 'R', "##.|#.#|##.|#.#|#.#" },
            { 'S', ".##|#..|.#.|..#|##." },
            { 'T', "###|.#.|.#.|.#.|.#." },
            { 'U', "#.#|#.#|#.#|#.#|###" },
            { 'V', "#.#|#.#|#.#|#.#|.#." },
            { 'W', "#...#|#...#|#.#.#|##.##|#...#" },
            { 'X', "#.#|#.#|.#.|#.#|#.#" },
            { 'Y', "#.#|#.#|.#.|.#.|.#." },
            { 'Z', "###|..#|.#.|#..|###" },
            { '0', "###|#.#|#.#|#.#|###" },
            { '1', ".#.|##.|.#.|.#.|###" },
            { '2', "##.|..#|.#.|#..|###" },
            { '3', "##.|..#|.#.|..#|##." },
            { '4', "#.#|#.#|###|..#|..#" },
            { '5', "###|#..|##.|..#|##." },
            { '6', ".##|#..|###|#.#|###" },
            { '7', "###|..#|.#.|.#.|.#." },
            { '8', "###|#.#|###|#.#|###" },
            { '9', "###|#.#|###|..#|##." },
            { ' ', "...|...|...|...|..." },
            { '.', "...|...|...|...|.#." },
            { ',', "...|...|...|.#.|#.." },
            { ':', "...|.#.|...|.#.|..." },
            { '-', "...|...|###|...|..." },
            { '+', "...|.#.|###|.#.|..." },
            { '/', "..#|..#|.#.|#..|#.." },
            { '%', "#.#|..#|.#.|#..|#.#" },
            { '°', ".#.|#.#|.#.|...|..." },
            { '\'', ".#.|.#.|...|...|..." },
            { '(', "..#|.#.|.#.|.#.|..#" },
            { ')', "#..|.#.|.#.|.#.|#.." },
            { '<', "..#|.#.|#..|.#.|..#" },
            { '>', "#..|.#.|..#|.#.|#.." },
            { '=', "...|###|...|###|..." },
            { '•', "...|...|.#.|...|..." },
            { '×', "...|#.#|.#.|#.#|..." },
            { '?', "##.|..#|.#.|...|.#." },
            { '!', ".#.|.#.|.#.|...|.#." },
            { '_', "...|...|...|...|###" },
            { '→', "...|.#.|###|.#.|..." },
            { '⇆', ".#.|###|...|###|.#." },
        };

        static Dictionary<char, bool[,]> _f57, _f35;

        static Dictionary<char, bool[,]> Glyphs(Size size)
        {
            if (size == Size.Large57) return _f57 ?? (_f57 = Compile(F57));
            return _f35 ?? (_f35 = Compile(F35));
        }

        static Dictionary<char, bool[,]> Compile(Dictionary<char, string> src)
        {
            var d = new Dictionary<char, bool[,]>(src.Count);
            foreach (var kv in src)
            {
                string[] rows = kv.Value.Split('|');
                var g = new bool[rows.Length, rows[0].Length];
                for (int r = 0; r < rows.Length; r++)
                    for (int c = 0; c < rows[r].Length; c++)
                        g[r, c] = rows[r][c] == '#';
                d[kv.Key] = g;
            }
            return d;
        }

        static bool[,] Glyph(Dictionary<char, bool[,]> g, char ch)
        {
            if (g.TryGetValue(ch, out var m)) return m;
            if (g.TryGetValue(char.ToUpperInvariant(ch), out m)) return m;
            return g['?'];
        }

        /// <summary>Glyph height in texels (7 or 5) times scale.</summary>
        public static int Height(Size size = Size.Large57, int scale = 1) => (size == Size.Large57 ? 7 : 5) * Mathf.Max(1, scale);

        /// <summary>Width of a string in texels (1 texel between glyphs, both times scale).</summary>
        public static int Width(string text, Size size = Size.Large57, int scale = 1)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var g = Glyphs(size);
            int w = 0;
            foreach (char ch in text) w += Glyph(g, ch).GetLength(1) + 1;
            return (w - 1) * Mathf.Max(1, scale);
        }

        /// <summary>A screen texture: RGBA32, Point filter, Clamp, no mipmaps, filled with the background colour
        /// (not yet applied: call Apply after drawing).</summary>
        public static Texture2D CreateTexture(int width, int height, Color32 background)
        {
            var t = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "UtilityPlane screen"
            };
            Clear(t, background);
            return t;
        }

        /// <summary>Shows a screen texture on a Display_* material (the rig's instance): the texture becomes the
        /// emission map with the glow colour (URP / Built-in _EmissionMap + _EmissionColor and _EMISSION; HDRP
        /// _EmissiveColorMap + _EmissiveColor, in nits there). Set UtilityPlaneRig.screensDrivenExternally = true first,
        /// otherwise the rig overwrites the emission colour with its idle glow.</summary>
        public static void SetScreen(Material m, Texture2D tex, Color glow)
        {
            if (m == null) return;
            if (m.HasProperty("_EmissiveColorMap"))
            {
                m.SetTexture("_EmissiveColorMap", tex);
                m.SetColor("_EmissiveColor", glow);
            }
            else
            {
                m.SetTexture("_EmissionMap", tex);
                m.SetColor("_EmissionColor", glow);
                m.EnableKeyword("_EMISSION");
            }
        }

        public static void Clear(Texture2D tex, Color32 background)
        {
            var px = new Color32[tex.width * tex.height];
            for (int i = 0; i < px.Length; i++) px[i] = background;
            tex.SetPixels32(px);
        }

        /// <summary>Draws text into a readable texture; (x, yTop) = top-left of the first glyph in texels, y counted
        /// from the TOP edge (as on the screen). Call tex.Apply() afterwards. Returns the width drawn.</summary>
        public static int Draw(Texture2D tex, string text, int x, int yTop, Color32 color, Size size = Size.Large57, int scale = 1)
        {
            var px = tex.GetPixels32();
            int w = Draw(px, tex.width, tex.height, text, x, yTop, color, size, scale);
            tex.SetPixels32(px);
            return w;
        }

        /// <summary>Same on a pixel buffer (row 0 = bottom, as Texture2D.GetPixels32 returns it).</summary>
        public static int Draw(Color32[] pixels, int width, int height, string text, int x, int yTop, Color32 color,
                               Size size = Size.Large57, int scale = 1)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            scale = Mathf.Max(1, scale);
            var g = Glyphs(size);
            int cx = x;
            foreach (char ch in text)
            {
                var m = Glyph(g, ch);
                int gh = m.GetLength(0), gw = m.GetLength(1);
                for (int r = 0; r < gh; r++)
                    for (int c = 0; c < gw; c++)
                    {
                        if (!m[r, c]) continue;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int px = cx + c * scale + sx;
                                int py = height - 1 - (yTop + r * scale + sy);
                                if (px < 0 || px >= width || py < 0 || py >= height) continue;
                                pixels[py * width + px] = color;
                            }
                    }
                cx += (gw + 1) * scale;
            }
            return cx - x - scale;
        }
    }
}
