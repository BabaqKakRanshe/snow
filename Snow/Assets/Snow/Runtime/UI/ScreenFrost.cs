using Snow.Audio;
using Snow.Player;
using Snow.World;
using UnityEngine;

namespace Snow
{
    /// <summary>
    /// Frost on the edges of the view (user 23.10, realism list item 7): grows in from the edges while the own player is out
    /// in the cold and melts under a roof (SurfaceResolver.Indoors, as the forest ambience; a vehicle cabin counts). There is
    /// no body temperature yet, so "how cold" is the air (EnvironmentProvider: -25 °C or colder = full) plus the wind; a
    /// real cold system later only has to drive <see cref="Target"/>. Texture Resources/ScreenFrost.png (R crystal, G reach,
    /// 640 x 360 point-filtered like the PS1 frame), shader Resources/ScreenFrost.shader. Drawn in OnGUI behind the HUD,
    /// over the PS1 frame canvas. Local, created by itself on the first scene. Numbers TEMPORARY (by eye).
    /// </summary>
    public class ScreenFrost : MonoBehaviour
    {
        /// <summary>Off in the F1 menu.</summary>
        public static bool Enabled = true;
        /// <summary>F1 menu: &gt;= 0 holds the frost at this amount (a look check), &lt; 0 = normal.</summary>
        public static float Hold = -1f;

        /// <summary>°C at which the air alone gives full frost; m/s of wind that adds <see cref="WindShare"/>.</summary>
        const float FullColdC = -25f, FullWindMs = 15f, WindShare = 0.3f;
        /// <summary>s from none to full frost outside; s to melt from full under a roof.</summary>
        const float GrowTime = 120f, MeltTime = 15f;
        /// <summary>s between roof checks.</summary>
        const float CheckEvery = 0.5f;

        static ScreenFrost s_instance;

        /// <summary>0..1 frost now.</summary>
        public static float Amount { get; private set; }
        /// <summary>0..1 the frost is heading to.</summary>
        public static float Target { get; private set; }

        Material _mat;
        Texture2D _tex;
        float _checkAt;
        static readonly int AmountId = Shader.PropertyToID("_Amount");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Amount = 0f; Target = 0f; Hold = -1f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_instance != null) return;
            var go = new GameObject("ScreenFrost");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<ScreenFrost>();
        }

        void Awake()
        {
            _tex = Resources.Load<Texture2D>("ScreenFrost");
            Shader sh = Resources.Load<Shader>("ScreenFrost");
            if (_tex == null || sh == null) { Debug.LogWarning("[ScreenFrost] Resources/ScreenFrost texture or shader missing: no frost."); enabled = false; return; }
            _tex.filterMode = FilterMode.Point;
            _mat = new Material(sh) { name = "ScreenFrost (runtime)", hideFlags = HideFlags.HideAndDontSave };
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }

        void Update()
        {
            PlayerNet me = PlayerNet.Local;
            if (Time.unscaledTime >= _checkAt)
            {
                _checkAt = Time.unscaledTime + CheckEvery;
                Target = me != null ? ColdAt(me.transform.position) : 0f;
            }
            if (Hold >= 0f) { Amount = Hold; return; }
            float rate = Target > Amount ? 1f / GrowTime : 1f / MeltTime;
            Amount = Mathf.MoveTowards(Amount, Target, rate * Time.deltaTime);
        }

        static float ColdAt(Vector3 p)
        {
            if (SurfaceResolver.Indoors(p)) return 0f;
            float cold = Mathf.Clamp01(EnvironmentProvider.TemperatureAt(p.y) / FullColdC);
            float wind = Mathf.Clamp01(EnvironmentProvider.WindAt(p).magnitude / FullWindMs) * WindShare;
            return Mathf.Clamp01(cold + wind);
        }

        void OnGUI()
        {
            if (!Enabled || Amount <= 0.001f || Event.current.type != EventType.Repaint) return;
            GUI.depth = 1000;                                          // behind every other OnGUI (the HUD), over the PS1 canvas
            _mat.SetFloat(AmountId, Amount);
            Graphics.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _tex, _mat);
        }
    }
}
