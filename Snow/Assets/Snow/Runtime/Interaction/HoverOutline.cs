using UnityEngine;
using UtilityPlane;

namespace Snow.Interaction
{
    /// <summary>
    /// Owner-side highlight (INPUT-02 hover feedback): an outline around what the use ray or cursor points at, and a
    /// second colour for a hint target (e.g. the next control of the start checklist). A control with its own small
    /// mesh (switch, lever, knob, cap, crate) gets a pixel-wide ring around its silhouette (Snow/Outline); a zone whose
    /// parent is large or has no mesh (seat, door handle, belt buckle) gets a thin box around the zone itself.
    /// Drawing only: nothing here is replicated or changes state.
    /// </summary>
    public class HoverOutline : MonoBehaviour
    {
        public InteractionSensor sensor;
        [Tooltip("Snow/Outline")] public Material outlineMaterial;
        [Tooltip("Snow/LineColor")] public Material lineMaterial;
        public Color hoverColor = new Color(1f, 0.85f, 0.3f, 1f);
        public Color hintColor = new Color(0.3f, 0.9f, 1f, 0.9f);
        [Tooltip("px")] public float width = 3f;
        [Tooltip("m: a zone's parent mesh larger than this is not outlined; the zone box is drawn instead")] public float maxMeshSize = 0.8f;
        [Tooltip("m, box line width")] public float lineWidth = 0.004f;

        Component _hint;
        LineRenderer _hoverBox, _hintBox;
        MaterialPropertyBlock _hoverProps, _hintProps;
        static readonly int ColorId = Shader.PropertyToID("_Color"), WidthId = Shader.PropertyToID("_Width"), CenterId = Shader.PropertyToID("_Center");
        static readonly int[] BoxPath = { 0, 1, 2, 3, 0, 4, 5, 1, 5, 6, 2, 6, 7, 3, 7, 4 };
        readonly Vector3[] _corners = new Vector3[8];
        readonly Vector3[] _path = new Vector3[16];

        /// <summary>Show a second outline on this zone or object (null = none).</summary>
        public void SetHint(Component target) => _hint = target;

        void Awake()
        {
            _hoverProps = new MaterialPropertyBlock();
            _hintProps = new MaterialPropertyBlock();
        }

        void OnDisable()
        {
            if (_hoverBox != null) _hoverBox.enabled = false;
            if (_hintBox != null) _hintBox.enabled = false;
        }

        void OnDestroy()
        {
            if (_hoverBox != null) Destroy(_hoverBox.gameObject);
            if (_hintBox != null) Destroy(_hintBox.gameObject);
        }

        void LateUpdate()
        {
            if (Application.isBatchMode || sensor == null) return;
            Component hover = sensor.TargetVolume != null ? sensor.TargetVolume
                : sensor.TargetCargo != null ? sensor.TargetCargo
                : sensor.TargetGrab != null ? (Component)sensor.TargetGrab : null;
            bool boxHover = Draw(hover, hoverColor, _hoverProps, ref _hoverBox);
            if (_hoverBox != null) _hoverBox.enabled = boxHover;
            bool boxHint = _hint != null && _hint != hover && Draw(_hint, hintColor, _hintProps, ref _hintBox);
            if (_hintBox != null) _hintBox.enabled = boxHint;
        }

        // returns true if the target is shown as a box (the line renderer is in use)
        bool Draw(Component target, Color color, MaterialPropertyBlock props, ref LineRenderer box)
        {
            if (target == null) return false;
            if (target is UtilityPlaneInteractable zone)
            {
                Transform parent = zone.transform.parent;
                Renderer r = parent != null ? parent.GetComponent<Renderer>() : null;
                if (r != null && r.enabled && Max(r.bounds.size) <= maxMeshSize)
                {
                    Hull(r, color, props);
                    return false;
                }
                var col = zone.GetComponent<BoxCollider>();
                if (col == null) return false;
                Box(col, color, ref box);
                return true;
            }
            foreach (var r in target.GetComponentsInChildren<MeshRenderer>())
                if (r.enabled && r.gameObject.activeInHierarchy && Outlined(r)) Hull(r, color, props);
            return false;
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

        void Hull(Renderer r, Color color, MaterialPropertyBlock props)
        {
            if (outlineMaterial == null) return;
            var mf = r.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) return;
            props.SetColor(ColorId, color);
            props.SetFloat(WidthId, FrameWidth());
            props.SetVector(CenterId, mesh.bounds.center);
            var rp = new RenderParams(outlineMaterial)
            {
                matProps = props,
                layer = r.gameObject.layer,
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = false,
            };
            for (int i = 0; i < mesh.subMeshCount; i++) Graphics.RenderMesh(rp, mesh, i, r.localToWorldMatrix);
        }

        void Box(BoxCollider col, Color color, ref LineRenderer box)
        {
            if (box == null)
            {
                var go = new GameObject("HoverBox") { hideFlags = HideFlags.DontSave };
                box = go.AddComponent<LineRenderer>();
                box.useWorldSpace = true;
                box.positionCount = _path.Length;
                box.sharedMaterial = lineMaterial;
                box.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                box.receiveShadows = false;
                box.numCornerVertices = 0;
            }
            box.startWidth = box.endWidth = lineWidth;
            box.startColor = box.endColor = color;
            Transform t = col.transform;
            Vector3 c = col.center, e = col.size * 0.5f;
            int k = 0;
            for (int y = -1; y <= 1; y += 2)
            {
                _corners[k++] = t.TransformPoint(c + new Vector3(-e.x, y * e.y, -e.z));
                _corners[k++] = t.TransformPoint(c + new Vector3(e.x, y * e.y, -e.z));
                _corners[k++] = t.TransformPoint(c + new Vector3(e.x, y * e.y, e.z));
                _corners[k++] = t.TransformPoint(c + new Vector3(-e.x, y * e.y, e.z));
            }
            for (int i = 0; i < _path.Length; i++) _path[i] = _corners[BoxPath[i]];
            box.SetPositions(_path);
        }

        /// <summary>
        /// Outline width in the camera's own pixels. A camera that renders into a RenderTexture (the PS1 frame of the
        /// location levels, 240 lines) has pixels ~4.5 screen pixels high, so 3 px would be ~13 px on screen: 1 px there
        /// (TEMPORARY, from a screenshot).
        /// </summary>
        float FrameWidth()
        {
            Camera cam = sensor != null && sensor.view != null ? sensor.view.cam : null;
            return cam != null && cam.targetTexture != null ? 1f : width;
        }

        static float Max(Vector3 v) => Mathf.Max(v.x, Mathf.Max(v.y, v.z));
    }
}
