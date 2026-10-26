// Top-down orthographic picture of the open scene (edit mode): tools/snow_qa/topview.py fills the ARGS line.
// Lighting is raised only for this render (flat ambient, fog off, every renderer seen) and put back afterwards.
var A = new System.Collections.Generic.Dictionary<string, string>();
foreach (var kv in "ARGS".Split(';')) { var p = kv.Split('='); if (p.Length == 2) A[p[0]] = p[1]; }
System.Func<string, float> F = k => float.Parse(A[k], System.Globalization.CultureInfo.InvariantCulture);
float cx = F("x"), cz = F("z"), size = F("size"); int px = (int)F("px");
float top = A.ContainsKey("y") ? F("y") : 400f;
string outPath = A["out"];
var go = new UnityEngine.GameObject("TopViewCam");
var cam = go.AddComponent<UnityEngine.Camera>();
cam.orthographic = true; cam.orthographicSize = size * 0.5f;
cam.transform.SetPositionAndRotation(new UnityEngine.Vector3(cx, top, cz), UnityEngine.Quaternion.Euler(90f, 0f, 0f));
cam.nearClipPlane = 1f; cam.farClipPlane = 1000f;
cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor; cam.backgroundColor = new UnityEngine.Color(0.2f, 0.2f, 0.25f);
var amb = UnityEngine.RenderSettings.ambientMode; var ambC = UnityEngine.RenderSettings.ambientLight; var fog = UnityEngine.RenderSettings.fog;
UnityEngine.RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
UnityEngine.RenderSettings.ambientLight = new UnityEngine.Color(1.1f, 1.1f, 1.1f);
UnityEngine.RenderSettings.fog = false;
var vols = UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(UnityEngine.FindObjectsSortMode.None);
var volOn = new bool[vols.Length];
for (int i = 0; i < vols.Length; i++) { volOn[i] = vols[i].enabled; vols[i].enabled = false; }
var rt = new UnityEngine.RenderTexture(px, px, 24, UnityEngine.RenderTextureFormat.ARGB32);
cam.targetTexture = rt;
try
{
    cam.Render();
    var prev = UnityEngine.RenderTexture.active; UnityEngine.RenderTexture.active = rt;
    var tex = new UnityEngine.Texture2D(px, px, UnityEngine.TextureFormat.RGB24, false);
    tex.ReadPixels(new UnityEngine.Rect(0, 0, px, px), 0, 0); tex.Apply();
    UnityEngine.RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(outPath, tex.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(tex);
}
finally
{
    UnityEngine.RenderSettings.ambientMode = amb; UnityEngine.RenderSettings.ambientLight = ambC; UnityEngine.RenderSettings.fog = fog;
    for (int i = 0; i < vols.Length; i++) vols[i].enabled = volOn[i];
    cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go);
}
return "saved " + outPath;
