using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkyIsland.EditorTools
{
    public static partial class SkyIslandBuilder
    {
        const int CaptureW = 1280, CaptureH = 720;

        /// <summary>
        /// Renders every SkyIslandViewpoint of the open scene with the main camera (post-processing included) at 1280x720,
        /// saves PNGs to &lt;project&gt;/SkyIslandCaptures and writes report.txt comparing them with the preview renders.
        /// </summary>
        [MenuItem(Menu + "Capture Viewpoints (compare with previews)", priority = 30)]
        public static void CaptureViewpoints()
        {
            var views = FindViewpoints();
            if (views.Length == 0)
            {
                // batch mode / another scene open: open the generated scene
                string scenePath = null;
                try { scenePath = FindRoot() + "/Scenes/SkyIsland.unity"; } catch { }
                if (scenePath != null && File.Exists(Path.Combine(Application.dataPath, "..", scenePath)) &&
                    (Application.isBatchMode || EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()))
                {
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    views = FindViewpoints();
                }
            }
            if (views.Length == 0) { Debug.LogError("[SkyIsland] No SkyIslandViewpoint objects found (run Build Scene first)."); return; }
            var cam = Camera.main;
            if (cam == null) { Debug.LogError("[SkyIsland] No camera tagged MainCamera in the open scene."); return; }
            System.Array.Sort(views, (a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SkyIslandCaptures"));
            Directory.CreateDirectory(folder);
            string root = null;
            try { root = FindRoot(); } catch { /* previews comparison is optional */ }

            var fog = cam.GetComponent<SkyIsland.SkyIslandFogByAltitude>();
            var t = cam.transform;
            Vector3 pos0 = t.position; Quaternion rot0 = t.rotation; float fov0 = cam.fieldOfView; float fog0 = RenderSettings.fogDensity;
            var target0 = cam.targetTexture;
            var report = new StringBuilder();
            report.AppendLine("view;capture mean sRGB;preview mean sRGB;capture L p5/p50/p95;preview L p5/p50/p95;dL p50");
            var rt = new RenderTexture(CaptureW, CaptureH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            var tex = new Texture2D(CaptureW, CaptureH, TextureFormat.RGB24, false, false);
            try
            {
                for (int i = 0; i < views.Length; i++)
                {
                    var v = views[i];
                    EditorUtility.DisplayProgressBar("Sky Island", "Capturing " + v.name, (float)i / views.Length);
                    t.SetPositionAndRotation(v.transform.position, v.transform.rotation);
                    cam.fieldOfView = v.fieldOfView;
                    if (fog != null) RenderSettings.fogDensity = fog.DensityAt(v.transform.position);
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.Render();   // second frame: temporal effects / probes settle
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, CaptureW, CaptureH), 0, 0, false);
                    tex.Apply(false);
                    RenderTexture.active = prev;
                    string baseName = string.IsNullOrEmpty(v.previewImage) ? v.name : Path.GetFileNameWithoutExtension(v.previewImage);
                    File.WriteAllBytes(Path.Combine(folder, baseName + "_unity.png"), tex.EncodeToPNG());

                    string line = v.name + ";" + Stats(tex, out var capL);
                    if (root != null && !string.IsNullOrEmpty(v.previewImage))
                    {
                        string pp = Path.GetFullPath(Path.Combine(Application.dataPath, "..", root, "Previews", v.previewImage));
                        if (File.Exists(pp))
                        {
                            var pv = new Texture2D(2, 2, TextureFormat.RGB24, false, false);
                            pv.LoadImage(File.ReadAllBytes(pp));
                            string ps = Stats(pv, out var prvL);
                            line += ";" + ps + ";" + (capL - prvL).ToString("+0.000;-0.000");
                            Object.DestroyImmediate(pv);
                        }
                    }
                    report.AppendLine(line);
                }
            }
            finally
            {
                cam.targetTexture = target0;
                t.SetPositionAndRotation(pos0, rot0);
                cam.fieldOfView = fov0;
                RenderSettings.fogDensity = fog0;
                EditorUtility.ClearProgressBar();
                Object.DestroyImmediate(tex);
                rt.Release(); Object.DestroyImmediate(rt);
            }
            File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString());
            Debug.Log($"[SkyIsland] {views.Length} viewpoints captured to {folder} (see report.txt: L = OKLab lightness 0..1, compare with the preview renders).");
            if (!Application.isBatchMode) EditorUtility.RevealInFinder(Path.Combine(folder, "report.txt"));
        }

        static SkyIsland.SkyIslandViewpoint[] FindViewpoints()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<SkyIsland.SkyIslandViewpoint>(FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<SkyIsland.SkyIslandViewpoint>();
#endif
        }

        /// <summary>"mean r,g,b;p5/p50/p95" of OKLab lightness; returns the median lightness.</summary>
        static string Stats(Texture2D tex, out float median)
        {
            var px = tex.GetPixels32();
            var L = new List<float>(px.Length / 9 + 1);
            double r = 0, g = 0, b = 0; int n = 0;
            for (int i = 0; i < px.Length; i += 9)
            {
                var c = px[i];
                r += c.r; g += c.g; b += c.b; n++;
                L.Add(OkL(c.r / 255f, c.g / 255f, c.b / 255f));
            }
            L.Sort();
            float P(float q) => L[Mathf.Clamp(Mathf.RoundToInt(q * (L.Count - 1)), 0, L.Count - 1)];
            median = P(0.5f);
            return $"{r / n:0},{g / n:0},{b / n:0};{P(0.05f):0.000}/{P(0.5f):0.000}/{P(0.95f):0.000}";
        }

        static float OkL(float r, float g, float b)
        {
            r = Mathf.GammaToLinearSpace(r); g = Mathf.GammaToLinearSpace(g); b = Mathf.GammaToLinearSpace(b);
            float l = 0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b;
            float m = 0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b;
            float s = 0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b;
            l = Mathf.Pow(Mathf.Max(l, 0f), 1f / 3f); m = Mathf.Pow(Mathf.Max(m, 0f), 1f / 3f); s = Mathf.Pow(Mathf.Max(s, 0f), 1f / 3f);
            return 0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s;
        }
    }
}
