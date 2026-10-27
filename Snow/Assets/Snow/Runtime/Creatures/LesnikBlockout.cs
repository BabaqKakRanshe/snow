using Snow.Player;
using UnityEngine;

namespace Snow.Creatures
{
    /// <summary>
    /// The Lesnik blockout (docs/gdd/monsters/02_lesnik, source/assets/lesnik_blockout): a placeholder figure on the final
    /// skeleton, posed by its clips without an Animator controller. reach 0 = Stand, 1 = the end of NeckReach (the neck
    /// stretched to the player); ReachTo() moves there in NeckReach's own time. Play() runs one of the other clips (Shuffle,
    /// Grab, Carry, Cower, Flee) in place, looping or holding its last frame; the neck goes back to Stand meanwhile.
    /// No behaviour and no network yet: the pult's «Лесник» page spawns it on this peer only (Resources/LesnikBlockout).
    /// </summary>
    public class LesnikBlockout : MonoBehaviour
    {
        public AnimationClip stand;
        public AnimationClip neckReach;
        [Tooltip("NeckReach aimed up (a first-floor window); empty: NeckReach")] public AnimationClip neckReachUp;
        public Transform head;
        /// <summary>The logic's clips (lesnik_blockout README): Shuffle, Grab, Carry, Cower, Flee.</summary>
        public AnimationClip[] clips;
        [Range(0f, 1f)] public float reach;

        /// <summary>The pult's on-screen marker (name and distance, through walls).</summary>
        public static bool ShowMarker = true;

        float _target, _shown = -1f;
        AnimationClip _playing;
        float _playT;
        bool _up, _visible = true;
        Renderer[] _renderers;

        public float Target => _target;
        public AnimationClip Playing => _playing;
        /// <summary>The neck reaches up (NeckReachUp) rather than to a standing player's face.</summary>
        public bool ReachUp => _up;
        public bool Visible => _visible;

        /// <summary>Run a clip by name (null or "Stand": back to the neck pose).</summary>
        public void Play(string clip)
        {
            _playing = null;
            if (clips != null && !string.IsNullOrEmpty(clip))
                foreach (var c in clips)
                    if (c != null && c.name == clip) _playing = c;
            _playT = 0f;
            if (_playing != null) reach = _target = 0f;
            _shown = -1f;
        }

        public void SetReach(float r, bool up = false) { _playing = null; _up = up; reach = _target = Mathf.Clamp01(r); _shown = -1f; }

        public void ReachTo(float r, bool up = false)
        {
            if (_playing != null) { _playing = null; _shown = -1f; }
            if (up != _up && reach > 0f) reach = 0f;                     // the other clip starts from the hanging pose
            if (up != _up) _shown = -1f;
            _up = up;
            _target = Mathf.Clamp01(r);
        }

        /// <summary>Shown or not (the networked Lesnik on his way between the cave and the base is nowhere).</summary>
        public void SetVisible(bool on)
        {
            if (on == _visible) return;
            _visible = on;
            foreach (var r in _renderers) if (r != null) r.enabled = on;
        }

        void Awake()
        {
            var an = GetComponent<Animator>();
            if (an != null) an.enabled = false;                           // posed by SampleAnimation, not by a controller
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
            _renderers = GetComponentsInChildren<Renderer>(true);
            if (head == null) head = FindDeep(transform, "Head");
        }

        void Update()
        {
            if (_playing != null)
            {
                _playT += Time.deltaTime;
                float len = Mathf.Max(0.01f, _playing.length);
                _playing.SampleAnimation(gameObject, _playing.isLooping ? Mathf.Repeat(_playT, len) : Mathf.Min(_playT, len));
                return;
            }
            AnimationClip neck = _up && neckReachUp != null ? neckReachUp : neckReach;
            if (neck != null && !Mathf.Approximately(reach, _target))
                reach = Mathf.MoveTowards(reach, _target, Time.deltaTime / Mathf.Max(0.05f, neck.length));
            if (Mathf.Approximately(reach, _shown)) return;
            _shown = reach;
            if (reach <= 0f || neck == null) { if (stand != null) stand.SampleAnimation(gameObject, 0f); }
            else neck.SampleAnimation(gameObject, reach * neck.length);
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
            if (!ShowMarker || !_visible || Event.current.type != EventType.Repaint) return;
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
