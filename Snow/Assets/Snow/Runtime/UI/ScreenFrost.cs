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
        /// <summary>On / off on the pult page «Эффекты». Off by default for now (user 25.10: «пока отключить обморожение»).</summary>
        public static bool Enabled = false;
        /// <summary>Pult: &gt;= 0 holds the frost at this amount (a look check), &lt; 0 = normal.</summary>
        public static float Hold = -1f;

        const float DefaultFullColdC = -25f, DefaultFullWindMs = 15f, DefaultWindShare = 0.3f, DefaultGrowTime = 120f,
                    DefaultMeltTime = 15f, DefaultEdge = 0.08f, DefaultOpacity = 0.85f;
        static readonly Color DefaultTint = new Color(0.85f, 0.92f, 1f);

        // tuning, live on the pult page «Эффекты»
        /// <summary>°C at which the air alone gives full frost; m/s of wind that adds <see cref="WindShare"/>.</summary>
        public static float FullColdC = DefaultFullColdC, FullWindMs = DefaultFullWindMs, WindShare = DefaultWindShare;
        /// <summary>s from none to full frost outside; s to melt from full under a roof.</summary>
        public static float GrowTime = DefaultGrowTime, MeltTime = DefaultMeltTime;
        /// <summary>Softness of the growing edge (share of the reach); 0–1 opacity of the crystals; their colour.</summary>
        public static float Edge = DefaultEdge, Opacity = DefaultOpacity;
        public static Color Tint = DefaultTint;

        public static void ResetTuning()
        {
            FullColdC = DefaultFullColdC; FullWindMs = DefaultFullWindMs; WindShare = DefaultWindShare; GrowTime = DefaultGrowTime;
            MeltTime = DefaultMeltTime; Edge = DefaultEdge; Opacity = DefaultOpacity; Tint = DefaultTint;
        }

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
        static readonly int AmountId = Shader.PropertyToID("_Amount"), EdgeId = Shader.PropertyToID("_Edge"),
                            OpacityId = Shader.PropertyToID("_Opacity"), TintId = Shader.PropertyToID("_Tint");

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
            float rate = Target > Amount ? 1f / Mathf.Max(GrowTime, 0.1f) : 1f / Mathf.Max(MeltTime, 0.1f);
            Amount = Mathf.MoveTowards(Amount, Target, rate * Time.deltaTime);
        }

        static float ColdAt(Vector3 p)
        {
            if (SurfaceResolver.Indoors(p)) return 0f;
            float cold = Mathf.Clamp01(EnvironmentProvider.TemperatureAt(p.y) / Mathf.Min(FullColdC, -0.1f));
            float wind = Mathf.Clamp01(EnvironmentProvider.WindAt(p).magnitude / Mathf.Max(FullWindMs, 0.1f)) * WindShare;
            return Mathf.Clamp01(cold + wind);
        }

        void OnGUI()
        {
            if (!Enabled || Amount <= 0.001f || Event.current.type != EventType.Repaint) return;
            GUI.depth = 1000;                                          // behind every other OnGUI (the HUD), over the PS1 canvas
            _mat.SetFloat(AmountId, Amount);
            _mat.SetFloat(EdgeId, Mathf.Max(Edge, 0.01f));
            _mat.SetFloat(OpacityId, Opacity);
            _mat.SetColor(TintId, Tint);
            Graphics.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _tex, _mat);
        }
    }
}
