using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UtilityPlane;

namespace Snow.Interaction
{
    /// <summary>
    /// Owner-side highlight (INPUT-02 hover feedback): an outline around what the use ray or cursor points at, and a
    /// second colour for a hint target (e.g. the next control of the start checklist). A screen-space ring around the
    /// outer edge of the silhouette (user 25.10: the wire box and then the inverted hull looked awful — the hull filled
    /// concave parts such as a door's wheel arch and broke into dashes):
    ///  1. LateUpdate: the meshes drawn flat into a mask the size of the camera frame (Resources/HoverMask.shader; with
    ///     the PS1 frame ~427 x 240, so the ring is one PS1 pixel wide), R = hover, G = hint;
    ///  2. OnGUI: the ring from the mask over the screen (Resources/HoverRing.shader), behind the HUD.
    /// What is outlined:
    ///  - an item: all its parts;
    ///  - a zone: the meshes of the object it sits on that lie mostly inside the zone and are not larger than
    ///    <see cref="maxPartSize"/> (the door leaf with its glass, the fuse, the seat); else its parent's own small mesh
    ///    (switch, lever, knob). None found (a seat that is part of the body mesh) = no ring; the hover card frame
    ///    (InteractionOverlay) still shows.
    /// The mask has no depth: the whole silhouette is ringed even where something stands in front (the target is what
    /// the ray hit, so it is mostly in view). Drawing only: nothing here is replicated or changes state.
    /// </summary>
    [DefaultExecutionOrder(1000)]                                        // after FirstPersonView.LateUpdate moved the camera
    public class HoverOutline : MonoBehaviour
    {
        public InteractionSensor sensor;
        public Color hoverColor = new Color(1f, 0.85f, 0.3f, 1f);
        public Color hintColor = new Color(0.3f, 0.9f, 1f, 0.9f);
        [Tooltip("px of the camera frame without the PS1 frame; in the PS1 frame always 1 (one PS1 pixel)")] public float width = 2f;
        [Tooltip("0-1: tint inside the hovered thing (0 = ring only)")] public float fill = 0f;
        [Tooltip("m: a zone's parent mesh up to this size is the control itself")] public float maxMeshSize = 0.8f;
        [Tooltip("m: largest part outlined for a zone on a big object (a door leaf is ~2.1 m)")] public float maxPartSize = 2.5f;

        Component _hint;
        Material _hoverMask, _hintMask, _ringMat;
        RenderTexture _mask;
        CommandBuffer _cmd;
        bool _drawn;
        // zone -> its parts, found once (the parts move with the zone: a door leaf turns with its handle)
        readonly Dictionary<UtilityPlaneInteractable, Renderer[]> _parts = new Dictionary<UtilityPlaneInteractable, Renderer[]>();
        readonly List<Renderer> _found = new List<Renderer>();
        static readonly int MaskColorId = Shader.PropertyToID("_MaskColor"), HoverColorId = Shader.PropertyToID("_HoverColor"),
                            HintColorId = Shader.PropertyToID("_HintColor"), TexelId = Shader.PropertyToID("_Texel"),
                            RadiusId = Shader.PropertyToID("_Radius"), FillId = Shader.PropertyToID("_Fill");

        /// <summary>Show a second outline on this zone or object (null = none).</summary>
        public void SetHint(Component target) => _hint = target;

        void Awake()
        {
            Shader mask = Resources.Load<Shader>("HoverMask"), ring = Resources.Load<Shader>("HoverRing");
            if (mask == null || ring == null) { Debug.LogWarning("[HoverOutline] Resources/HoverMask or HoverRing shader missing: no outline."); enabled = false; return; }
            _hoverMask = new Material(mask) { name = "HoverMask hover (runtime)", hideFlags = HideFlags.HideAndDontSave };
            _hoverMask.SetColor(MaskColorId, new Color(1f, 0f, 0f, 0f));
            _hintMask = new Material(mask) { name = "HoverMask hint (runtime)", hideFlags = HideFlags.HideAndDontSave };
            _hintMask.SetColor(MaskColorId, new Color(0f, 1f, 0f, 0f));
            _ringMat = new Material(ring) { name = "HoverRing (runtime)", hideFlags = HideFlags.HideAndDontSave };
            _cmd = new CommandBuffer { name = "HoverOutline mask" };
        }

        void OnDestroy()
        {
            if (_mask != null) { _mask.Release(); Destroy(_mask); }
            if (_hoverMask != null) Destroy(_hoverMask);
            if (_hintMask != null) Destroy(_hintMask);
            if (_ringMat != null) Destroy(_ringMat);
            _cmd?.Release();
        }

        void LateUpdate()
        {
            _drawn = false;
            if (Application.isBatchMode || sensor == null) return;
            Camera cam = sensor.view != null ? sensor.view.cam : null;
            if (cam == null || !cam.enabled) return;
            Component hover = sensor.TargetVolume != null ? sensor.TargetVolume
                : sensor.TargetCargo != null ? sensor.TargetCargo
                : sensor.TargetGrab != null ? (Component)sensor.TargetGrab : null;
            Component hint = _hint != null && _hint != hover ? _hint : null;
            if (hover == null && hint == null) return;

            RenderTexture frame = cam.targetTexture;
            int w = frame != null ? frame.width : cam.pixelWidth, h = frame != null ? frame.height : cam.pixelHeight;
            if (_mask == null || _mask.width != w || _mask.height != h)
            {
                if (_mask != null) { _mask.Release(); Destroy(_mask); }
                _mask = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
                {
                    name = "HoverOutline mask", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false,
                };
                _mask.Create();
            }
            _cmd.Clear();
            _cmd.SetRenderTarget(_mask);
            _cmd.ClearRenderTarget(false, true, Color.clear);
            // the plain projection: SetViewProjectionMatrices itself adapts it to the platform and the render texture
            // (GL.GetGPUProjectionMatrix here as well flipped the mask upside down, user 25.10)
            _cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
            int n = Draw(hover, _hoverMask) + Draw(hint, _hintMask);
            if (n == 0) return;
            Graphics.ExecuteCommandBuffer(_cmd);
            _ringMat.SetColor(HoverColorId, hoverColor);
            _ringMat.SetColor(HintColorId, hintColor);
            _ringMat.SetVector(TexelId, new Vector4(1f / w, 1f / h, 0f, 0f));
            _ringMat.SetFloat(RadiusId, frame != null ? 1f : Mathf.Clamp(Mathf.Round(width), 1f, 3f));
            _ringMat.SetFloat(FillId, fill);
            _drawn = true;
        }

        void OnGUI()
        {
            if (!_drawn || Event.current.type != EventType.Repaint) return;
            GUI.depth = 999;                                           // behind the HUD (depth 0), over the screen frost (1000)
            Graphics.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _mask, _ringMat);
        }

        // the target's meshes into the mask; returns how many
        int Draw(Component target, Material mat)
        {
            if (target == null) return 0;
            int n = 0;
            if (target is UtilityPlaneInteractable zone)
            {
                foreach (var r in Parts(zone))
                    if (r != null && r.enabled && r.gameObject.activeInHierarchy) n += Mask(r, mat);
                return n;
            }
            foreach (var r in target.GetComponentsInChildren<Renderer>())
                if (r.enabled && r.gameObject.activeInHierarchy && Part(r)) n += Mask(r, mat);
            return n;
        }

        // DrawRenderer, not DrawMesh: a skinned part (the van wheels, blend shapes) and a statically batched one are
        // drawn as they are on screen
        int Mask(Renderer r, Material mat)
        {
            int subs = Mathf.Max(1, r.sharedMaterials.Length);
            for (int i = 0; i < subs; i++) _cmd.DrawRenderer(r, mat, i, 0);
            return 1;
        }

        // a mesh part of a model (not a light effect, a lower LOD or a shadow copy, not a particle or line renderer)
        static bool Part(Renderer r) => (r is MeshRenderer || r is SkinnedMeshRenderer) && Outlined(r);

        // a zone's parts (user 25.10: a seat zone on the van root took the wheel, the pedals and the door - anything that
        // only touched it):
        //  1. the part named like the zone under its parent (IV_Wheel_FL -> Wheel_FL, IV_doorL -> doorL) with what hangs
        //     on it (nuts, glass, lock);
        //  2. else the meshes under its parent that lie mostly inside the zone box (Sit_R1 -> Seat_R1);
        //  3. else the parent's own small mesh (a cockpit control whose zone is tighter than the mesh).
        // A seat that is part of the body mesh has none.
        Renderer[] Parts(UtilityPlaneInteractable zone)
        {
            if (_parts.TryGetValue(zone, out Renderer[] parts)) return parts;
            if (_parts.Count > 512) _parts.Clear();                       // zones of unloaded scenes
            _found.Clear();
            Transform parent = zone.transform.parent;
            if (parent != null)
            {
                string bare = zone.name.StartsWith("IV_", System.StringComparison.Ordinal) ? zone.name.Substring(3) : zone.name;
                Renderer named = null;
                foreach (var r in parent.GetComponentsInChildren<Renderer>())
                    if (Part(r) && (r.name == zone.id || r.name == bare)) { named = r; break; }
                if (named != null)
                    foreach (var r in named.GetComponentsInChildren<Renderer>())
                        if (Part(r) && Max(r.bounds.size) <= maxPartSize) _found.Add(r);
                if (_found.Count == 0 && zone.TryGetComponent(out BoxCollider box))
                    foreach (var r in parent.GetComponentsInChildren<Renderer>())
                        if (Part(r) && Max(r.bounds.size) <= maxPartSize && InsideShare(r, zone.transform, box) >= MinInside) _found.Add(r);
                if (_found.Count == 0 && parent.TryGetComponent(out MeshRenderer own) && Max(own.bounds.size) <= maxMeshSize)
                    _found.Add(own);
            }
            parts = _found.ToArray();
            _parts[zone] = parts;
            return parts;
        }

        const float MinInside = 0.5f;
        readonly Vector3[] _corner = new Vector3[8];

        // share of the mesh's box inside the zone box, both in the zone's own axes (a turned van inflates world boxes)
        float InsideShare(Renderer r, Transform zone, BoxCollider box)
        {
            Bounds m = r.localBounds;                                  // a skinned mesh's are in its root bone's space
            Transform space = r is SkinnedMeshRenderer sk && sk.rootBone != null ? sk.rootBone : r.transform;
            Matrix4x4 toZone = zone.worldToLocalMatrix * space.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
                _corner[i] = toZone.MultiplyPoint3x4(m.center + Vector3.Scale(m.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
            var mb = new Bounds(_corner[0], Vector3.zero);
            for (int i = 1; i < 8; i++) mb.Encapsulate(_corner[i]);
            Vector3 zmin = box.center - box.size * 0.5f, zmax = box.center + box.size * 0.5f;
            float inside = 1f, whole = 1f;
            for (int a = 0; a < 3; a++)
            {
                const float thin = 0.01f;                              // a flat pane still has a share
                float lo = mb.min[a], hi = Mathf.Max(mb.max[a], lo + thin);
                inside *= Mathf.Max(0f, Mathf.Min(hi, zmax[a]) - Mathf.Max(lo, zmin[a]));
                whole *= hi - lo;
            }
            return inside / whole;
        }

        /// <summary>
        /// One copy of each part (J.2): the lower LOD levels (*_LOD1 / *_LOD2) and the shadow-only copies (*_Shadow) of the
        /// model would draw the same outline two or three times; the Rig's light effects (Glow_* / Beam_*) are not parts.
        /// </summary>
        public static bool Outlined(Renderer r)
        {
            string n = r.name;
            return !n.EndsWith("_LOD1", System.StringComparison.Ordinal) && !n.EndsWith("_LOD2", System.StringComparison.Ordinal)
                && !n.EndsWith("_Shadow", System.StringComparison.Ordinal) && !UtilityPlaneRig.IsEffectRenderer(r);
        }

        static float Max(Vector3 v) => Mathf.Max(v.x, Mathf.Max(v.y, v.z));
    }
}
