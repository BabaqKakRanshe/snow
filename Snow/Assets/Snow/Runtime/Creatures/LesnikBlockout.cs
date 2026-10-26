using Snow.Player;
using UnityEngine;

namespace Snow.Creatures
{
    /// <summary>
    /// The Lesnik blockout (docs/gdd/monsters/02_lesnik, source/assets/lesnik_blockout): a placeholder figure on the final
    /// skeleton, posed by its two clips without an Animator controller. reach 0 = Stand, 1 = the end of NeckReach (the neck
    /// stretched to the player); ReachTo() moves there in NeckReach's own time. No behaviour and no network yet: the pult's
    /// «Лесник» page spawns it on this peer only (Resources/LesnikBlockout).
    /// </summary>
    public class LesnikBlockout : MonoBehaviour
    {
        public AnimationClip stand;
        public AnimationClip neckReach;
        public Transform head;
        [Range(0f, 1f)] public float reach;

        /// <summary>The pult's on-screen marker (name and distance, through walls).</summary>
        public static bool ShowMarker = true;

        float _target, _shown = -1f;

        public float Target => _target;

        public void SetReach(float r) { reach = _target = Mathf.Clamp01(r); }

        public void ReachTo(float r) { _target = Mathf.Clamp01(r); }

        void Awake()
        {
            var an = GetComponent<Animator>();
            if (an != null) an.enabled = false;                           // posed by SampleAnimation, not by a controller
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
            if (head == null) head = FindDeep(transform, "Head");
        }

        void Update()
        {
            if (neckReach != null && !Mathf.Approximately(reach, _target))
                reach = Mathf.MoveTowards(reach, _target, Time.deltaTime / Mathf.Max(0.05f, neckReach.length));
            if (Mathf.Approximately(reach, _shown)) return;
            _shown = reach;
            if (reach <= 0f || neckReach == null) { if (stand != null) stand.SampleAnimation(gameObject, 0f); }
            else neckReach.SampleAnimation(gameObject, reach * neckReach.length);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        GUIStyle _style;

        void OnGUI()
        {
            if (!ShowMarker || Event.current.type != EventType.Repaint) return;
            PlayerNet me = PlayerNet.Local;
            Camera cam = me != null && me.view != null && me.view.cam != null ? me.view.cam : me != null ? me.cam : Camera.main;
            if (cam == null) return;
            Vector3 at = head != null ? head.position : transform.position + Vector3.up * 2f;
            Vector3 sp = cam.WorldToScreenPoint(at);
            float dist = Vector3.Distance(cam.transform.position, at);
            float w = Screen.width, h = Screen.height;
            bool behind = sp.z < 0f;
            if (behind) { sp.x = w - sp.x; sp.y = 0f; }                     // behind: on the bottom edge, mirrored
            float x = Mathf.Clamp(sp.x, 60f, w - 60f), y = Mathf.Clamp(h - sp.y, 30f, h - 30f);
            bool off = behind || x != sp.x || y != h - sp.y;
            _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
            string arrow = !off ? "▼" : x <= 60f ? "◀" : x >= w - 60f ? "▶" : y <= 30f ? "▲" : "▼";
            string text = $"{arrow} {Loc.T("Лесник")} {dist:0} {Loc.T("м")}";
            var r = new Rect(x - 90f, y - 14f, 180f, 28f);
            _style.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _style);
            _style.normal.textColor = new Color(1f, 0.85f, 0.2f);
            GUI.Label(r, text, _style);
        }
#endif
    }
}
