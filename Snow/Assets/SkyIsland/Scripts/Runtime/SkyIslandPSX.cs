using UnityEngine;
#if SKYISLAND_UGUI
using UnityEngine.UI;
#endif

namespace SkyIsland
{
    /// <summary>
    /// v2.4 PSX output. The camera renders into a low-resolution RenderTexture (`height` lines, width from the screen
    /// aspect, Point filtering, no MSAA). A full-screen RawImage on an overlay canvas shows it through
    /// SkyIsland/PSXPresent: colour depth reduced to `colorBits` per channel with a 4x4 ordered (Bayer) dither at the
    /// internal resolution, then nearest-neighbour upscaling to the screen. Pipeline-agnostic (URP and Built-in);
    /// URP post-processing still runs on the low-resolution frame. Active in Play mode only.
    /// Matches the preview pipeline (loc/forest/render_psx.py + wk/post.py psx_present).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Sky Island/PSX Output")]
    public class SkyIslandPSX : MonoBehaviour
    {
        [Tooltip("Internal resolution in lines (240 = PS1)")] public int height = 240;
        [Tooltip("Bits per colour channel (5 = 15-bit colour)")] [Range(2, 8)] public int colorBits = 5;
        [Tooltip("Ordered dither strength (0 = plain posterisation)")] [Range(0f, 1f)] public float dither = 1f;
        [Tooltip("SkyIsland/PSXPresent (found by name when empty)")] public Shader presentShader;
        [Tooltip("Sorting order of the full-screen canvas: keep it under the game's own UI")] public int canvasOrder = -1000;

        Camera _cam;
        RenderTexture _rt;
        Material _mat;
        Camera _screen;
        int _sw, _sh, _height;
#if SKYISLAND_UGUI
        Canvas _canvas;
        RawImage _img;
#endif

        void OnEnable()
        {
#if SKYISLAND_UGUI
            _cam = GetComponent<Camera>();
            if (presentShader == null) presentShader = Shader.Find("SkyIsland/PSXPresent");
            if (presentShader == null)
            {
                Debug.LogWarning("[SkyIsland] Shader SkyIsland/PSXPresent not found: PSX output off.");
                enabled = false;
                return;
            }
            _mat = new Material(presentShader) { name = "PSXPresent (runtime)", hideFlags = HideFlags.HideAndDontSave };
            // a camera that renders nothing and only clears the screen; the overlay canvas draws the frame on top of it
            var sgo = new GameObject("PSX_Screen") { hideFlags = HideFlags.DontSave };
            sgo.transform.SetParent(transform, false);
            _screen = sgo.AddComponent<Camera>();
            _screen.cullingMask = 0;
            _screen.clearFlags = CameraClearFlags.SolidColor;
            _screen.backgroundColor = Color.black;
            _screen.depth = _cam.depth + 100f;
            _screen.useOcclusionCulling = false;
            _screen.allowHDR = false;
            _screen.allowMSAA = false;
            var cgo = new GameObject("PSX_Canvas") { hideFlags = HideFlags.DontSave };
            cgo.transform.SetParent(transform, false);
            _canvas = cgo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = canvasOrder;
            var igo = new GameObject("PSX_Frame") { hideFlags = HideFlags.DontSave };
            igo.transform.SetParent(cgo.transform, false);
            _img = igo.AddComponent<RawImage>();
            _img.material = _mat;
            _img.raycastTarget = false;
            var r = _img.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            Resize();
#else
            Debug.LogWarning("[SkyIsland] PSX output needs the Unity UI package (com.unity.ugui).");
            enabled = false;
#endif
        }

        void OnDisable()
        {
            if (_cam != null && _cam.targetTexture == _rt) _cam.targetTexture = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
#if SKYISLAND_UGUI
            if (_canvas != null) Destroy(_canvas.gameObject);
#endif
            if (_screen != null) Destroy(_screen.gameObject);
            if (_mat != null) Destroy(_mat);
        }

        void LateUpdate()
        {
            if (_mat == null) return;
            if (Screen.width != _sw || Screen.height != _sh || height != _height || _cam.targetTexture != _rt) Resize();
            _mat.SetFloat("_Levels", (1 << Mathf.Clamp(colorBits, 2, 8)) - 1);
            _mat.SetFloat("_Dither", dither);
            _mat.SetVector("_Res", new Vector4(_rt.width, _rt.height, 0f, 0f));
        }

        void Resize()
        {
            _sw = Screen.width; _sh = Screen.height; _height = height;
            int h = Mathf.Clamp(height, 64, 2160);
            int w = Mathf.Max(64, Mathf.RoundToInt(h * (float)_sw / Mathf.Max(1, _sh)));
            if (_rt != null)
            {
                if (_cam.targetTexture == _rt) _cam.targetTexture = null;
                _rt.Release(); Destroy(_rt);
            }
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                name = "PSX_Frame", filterMode = FilterMode.Point, antiAliasing = 1, useMipMap = false, wrapMode = TextureWrapMode.Clamp
            };
            _rt.Create();
            _cam.targetTexture = _rt;
#if SKYISLAND_UGUI
            if (_img != null) _img.texture = _rt;
#endif
        }
    }
}
