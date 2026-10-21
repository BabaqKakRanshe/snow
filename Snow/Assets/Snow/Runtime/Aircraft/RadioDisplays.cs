using System.Collections.Generic;
using UnityEngine;
using UtilityPlane;

namespace Snow.Aircraft
{
    /// <summary>
    /// The radio screens show standard data (user decision 16.10; docs/gdd/PLANE_V12_INTEGRATION_UNITY.md D.3), drawn
    /// with the model's pixel font into the Display_* quads, one texel per millimetre of the window:
    ///  - COM1 active frequency, NAV1 active frequency, ADF frequency — from the knobs (PlaneSwitchState);
    ///  - transponder: code 1200 (TEMPORARY: no code knobs in the model) and the mode; OFF = blank;
    ///  - autopilot: engaged — heading and altitude held, else "AP OFF";
    ///  - Hobbs: engine hours;
    ///  - GPS: only the background for now (its content is §10.3, stage P06).
    /// The avionics screens follow avionicsPower, Hobbs the bus. Every peer draws from replicated state; a texture is
    /// rewritten only when its text or brightness changes. screensDrivenExternally = true: the Rig leaves the quads alone.
    /// </summary>
    [RequireComponent(typeof(UtilityPlaneRig))]
    public class RadioDisplays : MonoBehaviour
    {
        public PlaneVehicle vehicle;
        [Tooltip("LCD text colour")] public Color32 textColor = new Color32(140, 255, 180, 255);
        [Tooltip("LCD background (lit when powered)")] public Color32 background = new Color32(6, 22, 16, 255);
        [Tooltip("Emission colour at full power (HDR)")] public Color glow = new Color(0.55f, 1.4f, 0.9f);

        class Screen
        {
            public Material material;
            public Texture2D texture;
            public string shown;
            public float power = -1f;
        }

        // window size in mm = texels (Data/displays.json)
        static readonly (string name, int w, int h)[] Sizes =
        {
            ("Display_COM1", 48, 17), ("Display_NAV1", 48, 17), ("Display_XPDR", 60, 18), ("Display_ADF", 72, 17),
            ("Display_AP", 80, 13), ("Display_GPS", 92, 50), ("Display_Hobbs", 30, 10),
        };

        // the screens print a decimal point whatever the system language (ru-RU would give a comma)
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        UtilityPlaneRig _rig;
        readonly Dictionary<string, Screen> _screens = new Dictionary<string, Screen>();

        void Awake()
        {
            _rig = GetComponent<UtilityPlaneRig>();
            if (vehicle == null) vehicle = GetComponent<PlaneVehicle>();
            _rig.screensDrivenExternally = true;               // before the Rig's first Update writes its idle glow
        }

        void Start()
        {
            // the Rig made its material instances in Awake: draw into those (contract §7)
            var byName = new Dictionary<string, Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true)) if (!byName.ContainsKey(r.name)) byName[r.name] = r;
            foreach (var (name, w, h) in Sizes)
            {
                if (!byName.TryGetValue(name, out Renderer r)) continue;
                _screens[name] = new Screen { material = r.sharedMaterial, texture = UtilityPlanePixelFont.CreateTexture(w, h, background) };
            }
        }

        void OnDestroy()
        {
            foreach (var s in _screens.Values) if (s.texture != null) Destroy(s.texture);
        }

        void LateUpdate()
        {
            if (vehicle == null || !vehicle.IsSpawned) return;
            PlaneSwitchState s = vehicle.Switches;
            PlaneTelemetry t = vehicle.Telemetry;
            float av = _rig.avionicsPower;
            float bus = _rig.busPowered ? 1f : 0f;
            Show("Display_COM1", s.ComMHz.ToString("0.000", Inv), av);
            Show("Display_NAV1", s.NavMHz.ToString("0.00", Inv), av);
            Show("Display_ADF", $"ADF {s.Adf}", av);
            string[] modes = { "", "1200 SBY", "1200 ON", "1200 ALT", "8888 TST" };
            Show("Display_XPDR", modes[Mathf.Clamp(s.Xpdr, 0, 4)], av);
            Show("Display_AP", t.ApEngaged ? $"HDG{Mathf.RoundToInt(Mathf.Repeat(t.ApHeading, 360f)):000} {Mathf.RoundToInt(t.ApAltFt)}" : "AP OFF", av);
            Show("Display_GPS", "", av);
            Show("Display_Hobbs", t.HobbsHours.ToString("0000.0", Inv), bus);
        }

        void Show(string name, string text, float power)
        {
            if (!_screens.TryGetValue(name, out Screen sc) || sc.material == null) return;
            power = Mathf.Round(Mathf.Clamp01(power) * 50f) / 50f;      // brightness steps: no rewrite every frame
            if (text == sc.shown && Mathf.Approximately(power, sc.power)) return;
            sc.shown = text;
            sc.power = power;
            Texture2D tex = sc.texture;
            UtilityPlanePixelFont.Clear(tex, background);
            if (power > 0f && !string.IsNullOrEmpty(text))
            {
                // the large 5×7 font if it fits the window, else the small 3×5
                var size = UtilityPlanePixelFont.Width(text) <= tex.width - 2 && tex.height >= 9
                    ? UtilityPlanePixelFont.Size.Large57 : UtilityPlanePixelFont.Size.Small35;
                int w = UtilityPlanePixelFont.Width(text, size);
                int x = Mathf.Max(0, (tex.width - w) / 2);
                int y = Mathf.Max(0, (tex.height - UtilityPlanePixelFont.Height(size)) / 2);
                UtilityPlanePixelFont.Draw(tex, text, x, y, textColor, size);
            }
            tex.Apply(false);
            UtilityPlanePixelFont.SetScreen(sc.material, tex, glow * power);   // dark without power: the M_Screen base stays
        }
    }
}
