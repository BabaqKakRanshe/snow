using System;
using System.Collections.Generic;
using UnityEngine;

namespace UazVan
{
    /// <summary>
    /// Glass of the van (Data/glass.json, shader Van/Glass): 13 panes, one renderer each (lob = the windscreen), every
    /// pane with its own frost, wet snow, condensation and up to 4 hand prints; the wipers clear their arcs of the
    /// windscreen (T_Van_WipeMap); the interior mirror shows the saloon (a small camera at MirrorCam into a render
    /// texture on RearMirror_Glass); the radio display shows its channel.
    /// Your climate model owns the amounts (breath fogs the panes, the heater on the windscreen clears frost and fog,
    /// snow falls): write them here. Hand prints are events: send them to every peer and call AddHandprint there.
    /// </summary>
    public partial class VanRig
    {
        [Serializable]
        public class Pane
        {
            public string name, group;                         // group: cab / saloon
            public Renderer renderer;
            public bool wipers;
            [Tooltip("uv = (dot(uvU.xyz, p) + uvU.w, dot(uvV.xyz, p) + uvV.w), p = pane-local point (m)")]
            public Vector4 uvU, uvV;
            [NonSerialized] public float frost, snow, fog;
            [NonSerialized] public Vector4[] prints = new Vector4[4];          // u, v, size u, cell
            [NonSerialized] public float[] printStrength = new float[4];
            [NonSerialized] public int nextPrint;
            [NonSerialized] public float clearTime;
            [NonSerialized] public bool dirty = true;
            [NonSerialized] public MaterialPropertyBlock block;
        }

        [Header("Glass (filled by the setup from Data/glass.json)")]
        public List<Pane> panes = new List<Pane>();
        [Tooltip("Wiper arcs on the windscreen's UVs (R = Wiper_L, G = Wiper_R)")] public Texture2D wipeMap;

        [Header("Wipers")]
        [Tooltip("Wiper motor, sweeps per minute (0 = off: the blades run on to their park position and stop). " +
                 "Your electrics: Toggle_Wiper, Fuse_8, the bus voltage")]
        public float wiperSpeed;
        [Tooltip("Wiped glass covers again with the falling snow / frost in about this time (s) once the wipers stop")]
        public float wipeRecoverSeconds = 25f;

        [Header("Hand prints")]
        [Tooltip("A print fades when its pane's fog stays below 0.03 this long (s): the glass dried")]
        public float printFadeSeconds = 6f;
        public float printSizeM = 0.20f;

        [Header("Mirror")]
        [Tooltip("Render the interior mirror (MirrorCam -> RearMirror_Glass). Turn it on only for a local player in the cab.")]
        public bool mirrorActive;
        public Vector2Int mirrorResolution = new Vector2Int(192, 56);
        public float mirrorFarClip = 30f;
        [Tooltip("Mirror frames per second (it renders the scene once more: no shadows, no colour texture, no post; the depth " +
                 "texture stays for the lamp glows)")]
        public float mirrorRateHz = 20f;
        public Renderer mirrorRenderer;
        public Transform mirrorCameraPoint;

        [Header("Radio")]
        public bool radioPower;
        [Range(1, 40)] public int radioChannel = 9;
        [Tooltip("Non-empty: show this text instead of the channel (max 6 characters: 0-9, A-Z, space, - .)")]
        public string radioText = "";
        public Renderer radioDisplay;

        // ------------------------------------------------------------------ API
        /// <summary>Frost 0..1 on a pane (lob = the windscreen; the side panes take the same mask on their own UVs).</summary>
        public void SetFrost(string pane, float amount) => PaneDo(pane, p => p.frost = Mathf.Clamp01(amount));

        /// <summary>Wet snow stuck on a pane (outside), 0..1.</summary>
        public void SetGlassSnow(string pane, float amount) => PaneDo(pane, p => p.snow = Mathf.Clamp01(amount));

        /// <summary>Condensation inside a pane, 0..1. Prints on it stay until the pane dries.</summary>
        public void SetFog(string pane, float amount) => PaneDo(pane, p => p.fog = Mathf.Clamp01(amount));

        /// <summary>Fog of every pane of a group: "cab" (windscreen, cab doors), "saloon", or "all".</summary>
        public void SetFogGroup(string group, float amount)
        {
            Init();
            foreach (var p in panes)
                if (p != null && (group == "all" || p.group == group)) { p.fog = Mathf.Clamp01(amount); p.dirty = true; }
        }

        public float GetFog(string pane)
        {
            Init();
            var p = FindPane(pane);
            return p != null ? p.fog : 0f;
        }

        /// <summary>A palm pressed on a pane at a world point (from a raycast on the glass or a Handprint_* socket).
        /// cell 0..3 picks the print of the 2 x 2 atlas (-1 = by the point). Returns false if the point is off the pane.</summary>
        public bool AddHandprint(string pane, Vector3 worldPoint, int cell = -1, float sizeM = -1f, float strength = 1f)
        {
            Init();
            var p = FindPane(pane);
            if (p == null || p.renderer == null) return false;
            Vector3 l = p.renderer.transform.InverseTransformPoint(worldPoint);
            float u = Vector3.Dot(new Vector3(p.uvU.x, p.uvU.y, p.uvU.z), l) + p.uvU.w;
            float v = Vector3.Dot(new Vector3(p.uvV.x, p.uvV.y, p.uvV.z), l) + p.uvV.w;
            if (u < -0.05f || u > 1.05f || v < -0.05f || v > 1.05f) return false;
            float uPerM = new Vector3(p.uvU.x, p.uvU.y, p.uvU.z).magnitude;
            float size = (sizeM > 0f ? sizeM : printSizeM) * uPerM;
            if (cell < 0) cell = Mathf.Abs(Mathf.FloorToInt(worldPoint.x * 37f + worldPoint.y * 91f + worldPoint.z * 53f)) % 4;
            int k = p.nextPrint;
            p.nextPrint = (p.nextPrint + 1) % 4;
            p.prints[k] = new Vector4(u, v, size, cell);
            p.printStrength[k] = Mathf.Clamp01(strength);
            p.clearTime = 0f;
            p.dirty = true;
            return true;
        }

        /// <summary>A print where a Handprint_* socket of sockets.json says (its pane is in the socket data).</summary>
        public bool AddHandprintAtSocket(string socketName, int cell = -1)
        {
            var t = Socket(socketName);
            if (t == null) return false;
            foreach (var s in sockets)
                if (s != null && s.name == socketName && !string.IsNullOrEmpty(s.pane))
                    return AddHandprint(s.pane, t.position, cell);
            return false;
        }

        public void ClearHandprints(string pane) => PaneDo(pane, p =>
        {
            for (int i = 0; i < 4; i++) p.printStrength[i] = 0f;
        });

        public void ClearAllHandprints()
        {
            foreach (var p in panes)
                if (p != null) { for (int i = 0; i < 4; i++) p.printStrength[i] = 0f; p.dirty = true; }
        }

        // ------------------------------------------------------------------ state
        int _frostId, _snowId, _fogId, _wipeId, _hpsId, _aspectId, _baseMapId, _baseMapStId;
        readonly int[] _hpId = new int[4];
        float _wiperPhase, _reachL, _reachR, _wipeStrength;
        bool _wiperParking;
        Camera _mirrorCam;
        RenderTexture _mirrorRT;
        bool _mirrorShown;
        float _mirrorTimer;
        MaterialPropertyBlock _mirrorBlock, _radioBlock;
        Texture2D _radioTex;
        string _radioShown;
        bool _radioPowerShown;

        void InitGlass()
        {
            _frostId = Shader.PropertyToID("_Frost");
            _snowId = Shader.PropertyToID("_Snow");
            _fogId = Shader.PropertyToID("_FogAmt");
            _wipeId = Shader.PropertyToID("_Wipe");
            _hpsId = Shader.PropertyToID("_HPS");
            _aspectId = Shader.PropertyToID("_PrintAspect");
            _baseMapId = Shader.PropertyToID("_BaseMap");
            _baseMapStId = Shader.PropertyToID("_BaseMap_ST");
            for (int i = 0; i < 4; i++) _hpId[i] = Shader.PropertyToID("_HP" + i);
            foreach (var p in panes)
            {
                if (p == null) continue;
                p.block = new MaterialPropertyBlock();
                if (p.prints == null || p.prints.Length != 4) p.prints = new Vector4[4];
                if (p.printStrength == null || p.printStrength.Length != 4) p.printStrength = new float[4];
                p.dirty = true;
            }
            _mirrorBlock = new MaterialPropertyBlock();
            _radioBlock = new MaterialPropertyBlock();
            _radioShown = null;
        }

        void ResyncGlass()
        {
            foreach (var p in panes) if (p != null) p.dirty = true;
            _radioShown = null;
        }

        void DestroyGlass()
        {
            if (_mirrorCam != null) Destroy(_mirrorCam.gameObject);
            if (_mirrorRT != null) { _mirrorRT.Release(); Destroy(_mirrorRT); }
            if (_radioTex != null) Destroy(_radioTex);
        }

        Pane FindPane(string name)
        {
            if (name == null) return null;
            foreach (var p in panes)
                if (p != null && p.name == name) return p;
            return null;
        }

        void PaneDo(string pane, Action<Pane> a)
        {
            Init();
            var p = FindPane(pane);
            if (p == null) return;
            a(p);
            p.dirty = true;
        }

        // ------------------------------------------------------------------ per frame
        void UpdateGlass(float dt)
        {
            if (!_ready) return;
            UpdateWipers(dt);
            UpdateMirror();
            UpdateRadio();
        }

        void LateUpdateGlass(float dt)
        {
            foreach (var p in panes)
            {
                if (p == null || p.renderer == null) continue;
                // prints fade when the pane has dried
                bool any = false;
                for (int i = 0; i < 4; i++) any |= p.printStrength[i] > 0f;
                if (any && p.fog < 0.03f && printFadeSeconds > 0f)
                {
                    p.clearTime += dt;
                    if (p.clearTime > printFadeSeconds) { for (int i = 0; i < 4; i++) p.printStrength[i] = 0f; p.dirty = true; }
                }
                else p.clearTime = 0f;
                if (p.wipers) p.dirty = true;                    // the wiped arcs move
                if (!p.dirty) continue;
                p.dirty = false;
                p.renderer.GetPropertyBlock(p.block);
                p.block.SetFloat(_frostId, p.frost);
                p.block.SetFloat(_snowId, p.snow);
                p.block.SetFloat(_fogId, p.fog);
                p.block.SetVector(_wipeId, p.wipers ? new Vector4(_reachL, _reachR, _wipeStrength, 1f) : Vector4.zero);
                for (int i = 0; i < 4; i++) p.block.SetVector(_hpId[i], p.prints[i]);
                p.block.SetVector(_hpsId, new Vector4(p.printStrength[0], p.printStrength[1], p.printStrength[2], p.printStrength[3]));
                float uLen = new Vector3(p.uvU.x, p.uvU.y, p.uvU.z).magnitude, vLen = new Vector3(p.uvV.x, p.uvV.y, p.uvV.z).magnitude;
                p.block.SetFloat(_aspectId, uLen > 1e-5f ? vLen / uLen : 1f);
                p.renderer.SetPropertyBlock(p.block);
            }
        }

        // the motor turns a crank: the blades sweep 0 -> 100 deg -> 0 smoothly; off = run on to park, then stop
        void UpdateWipers(float dt)
        {
            int wl = Index("Wiper_L"), wr = Index("Wiper_R");
            if (wl < 0 && wr < 0) return;
            float rate = Mathf.Max(0f, wiperSpeed) / 60f;
            if (rate > 0f) _wiperParking = false;
            else if (_wiperPhase > 0f) { _wiperParking = true; rate = 0.75f; }
            if (rate > 0f)
            {
                _wiperPhase += rate * dt;
                if (_wiperParking && _wiperPhase >= 1f) { _wiperPhase = 0f; _wiperParking = false; }
                else _wiperPhase = Mathf.Repeat(_wiperPhase, 1f);
            }
            float a = 50f * (1f - Mathf.Cos(_wiperPhase * 2f * Mathf.PI));       // 0..100 deg
            if (wl >= 0) SetPart(wl, a);
            if (wr >= 0) SetPart(wr, a);
            bool moving = rate > 0f;
            if (moving)
            {
                _reachL = Mathf.Max(_reachL, a);
                _reachR = Mathf.Max(_reachR, a);
                _wipeStrength = 1f;
            }
            else if (_wipeStrength > 0f)
            {
                _wipeStrength = Mathf.Max(0f, _wipeStrength - dt / Mathf.Max(wipeRecoverSeconds, 0.1f));
                if (_wipeStrength <= 0f) _reachL = _reachR = 0f;
            }
        }

        void UpdateMirror()
        {
            if (!mirrorActive)
            {
                if (_mirrorShown)
                {
                    _mirrorShown = false;
                    if (_mirrorCam != null) _mirrorCam.enabled = false;
                    if (mirrorRenderer != null)
                    {
                        mirrorRenderer.GetPropertyBlock(_mirrorBlock);
                        _mirrorBlock.SetColor(_emissionId, Color.black);
                        mirrorRenderer.SetPropertyBlock(_mirrorBlock);
                    }
                }
                return;
            }
            if (mirrorRenderer == null || mirrorCameraPoint == null) return;
            if (_mirrorCam == null)
            {
                _mirrorRT = new RenderTexture(Mathf.Max(16, mirrorResolution.x), Mathf.Max(8, mirrorResolution.y), 16)
                { name = "VanMirrorRT", filterMode = FilterMode.Point };
                var go = new GameObject("MirrorCamera");
                go.transform.SetParent(mirrorCameraPoint, false);
                _mirrorCam = go.AddComponent<Camera>();
                _mirrorCam.targetTexture = _mirrorRT;
                _mirrorCam.fieldOfView = 32f;
                _mirrorCam.nearClipPlane = 0.05f;
                _mirrorCam.farClipPlane = mirrorFarClip;
                _mirrorCam.enabled = false;
                var main = Camera.main;
                if (main != null) { _mirrorCam.cullingMask = main.cullingMask; _mirrorCam.backgroundColor = main.backgroundColor; }
                CheapUrpCamera(_mirrorCam);
            }
            if (!_mirrorShown)
            {
                _mirrorShown = true;
                _mirrorTimer = 0f;
                // the image is the emission (already lit by the mirror camera), u flipped like a mirror; the base stays dark
                mirrorRenderer.GetPropertyBlock(_mirrorBlock);
                _mirrorBlock.SetTexture("_EmissionMap", _mirrorRT);
                _mirrorBlock.SetColor(_emissionId, Color.white);
                _mirrorBlock.SetVector(_baseMapStId, new Vector4(-1f, 1f, 1f, 0f));
                mirrorRenderer.SetPropertyBlock(_mirrorBlock);
            }
            // a few frames a second: the camera is on for one frame, then off; the texture keeps the last image
            _mirrorTimer -= Time.unscaledDeltaTime;
            bool render = _mirrorTimer <= 0f;
            if (render) _mirrorTimer = Mathf.Max(0f, _mirrorTimer) + 1f / Mathf.Max(1f, mirrorRateHz);
            if (_mirrorCam.enabled != render) _mirrorCam.enabled = render;
        }

        // URP camera data by reflection (the package does not reference URP): no shadows, colour texture or post (the depth
        // texture stays: the lamp glows and beams read it)
        static void CheapUrpCamera(Camera cam)
        {
            Type type = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
                if (type != null) break;
            }
            if (type == null) return;
            var data = cam.GetComponent(type);
            if (data == null) data = cam.gameObject.AddComponent(type);
            if (data == null) return;
            foreach (var name in new[] { "renderShadows", "requiresColorTexture", "renderPostProcessing" })
            {
                var prop = type.GetProperty(name);
                if (prop == null || !prop.CanWrite || prop.PropertyType != typeof(bool)) continue;
                try { prop.SetValue(data, false); }
                catch (Exception e) { Debug.LogWarning("[UazVan] mirror camera " + name + ": " + e.Message); }
            }
        }

        // ------------------------------------------------------------------ radio display: 3 x 5 pixel font
        static readonly Dictionary<char, string> Font = new Dictionary<char, string>
        {
            { '0', "111101101101111" }, { '1', "010110010010111" }, { '2', "111001111100111" }, { '3', "111001111001111" },
            { '4', "101101111001001" }, { '5', "111100111001111" }, { '6', "111100111101111" }, { '7', "111001010010010" },
            { '8', "111101111101111" }, { '9', "111101111001111" }, { 'C', "111100100100111" }, { 'H', "101101111101101" },
            { '-', "000000111000000" }, { '.', "000000000000010" }, { ' ', "000000000000000" }, { 'A', "010101111101101" },
            { 'E', "111100111100111" }, { 'R', "110101110101101" }, { 'O', "111101101101111" }, { 'F', "111100111100100" },
            { 'N', "101111111111101" }, { 'S', "111100111001111" }, { 'T', "111010010010010" }, { 'L', "100100100100111" },
            { 'B', "110101110101110" }, { 'D', "110101101101110" }, { 'G', "111100101101111" }, { 'I', "111010010010111" },
            { 'M', "101111111101101" }, { 'P', "111101111100100" }, { 'U', "101101101101111" }, { 'V', "101101101101010" },
            { 'W', "101101111111101" }, { 'X', "101101010101101" }, { 'Y', "101101010010010" }, { 'K', "101110100110101" },
            { 'J', "001001001101111" }, { 'Q', "111101101111001" }, { 'Z', "111001010100111" }
        };

        void UpdateRadio()
        {
            if (radioDisplay == null) return;
            string text = radioPower ? (string.IsNullOrEmpty(radioText) ? "CH " + Mathf.Clamp(radioChannel, 1, 40).ToString("00") : radioText) : "";
            if (text == _radioShown && radioPower == _radioPowerShown) return;
            _radioShown = text;
            _radioPowerShown = radioPower;
            const int W = 64, H = 16;
            if (_radioTex == null)
                _radioTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "VanRadioDisplay", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H];
            var bg = new Color32(10, 18, 10, 255);
            var unlit = new Color32(18, 30, 18, 255);
            var lit = new Color32(140, 255, 120, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            string s = text.Length > 6 ? text.Substring(0, 6) : text;
            // 2 x scaled 3 x 5 glyphs, 1 px gaps: a glyph is 6 x 10 + 2 px
            int total = s.Length * 8 - 2;
            int x0 = (W - Mathf.Max(total, 0)) / 2, y0 = (H - 10) / 2;
            DrawText(px, W, "888888".Substring(0, Mathf.Max(1, s.Length)), x0, y0, unlit);
            DrawText(px, W, s.ToUpperInvariant(), x0, y0, lit);
            _radioTex.SetPixels32(px);
            _radioTex.Apply(false);
            radioDisplay.GetPropertyBlock(_radioBlock);
            _radioBlock.SetTexture(_baseMapId, _radioTex);
            _radioBlock.SetTexture("_EmissionMap", _radioTex);
            _radioBlock.SetColor(_emissionId, radioPower ? new Color(0.5f, 0.9f, 0.45f) * 1.6f : Color.black);
            radioDisplay.SetPropertyBlock(_radioBlock);
        }

        static void DrawText(Color32[] px, int w, string s, int x0, int y0, Color32 c)
        {
            for (int k = 0; k < s.Length; k++)
            {
                if (!Font.TryGetValue(s[k], out var g)) continue;
                for (int gy = 0; gy < 5; gy++)
                    for (int gx = 0; gx < 3; gx++)
                    {
                        if (g[gy * 3 + gx] != '1') continue;
                        for (int sy = 0; sy < 2; sy++)
                            for (int sx = 0; sx < 2; sx++)
                            {
                                int x = x0 + k * 8 + gx * 2 + sx, y = y0 + (4 - gy) * 2 + sy;      // row 0 = bottom
                                if (x >= 0 && x < w && y >= 0 && y * w + x < px.Length) px[y * w + x] = c;
                            }
                    }
            }
        }
    }
}
