using UnityEngine;

namespace Snow.Tasks
{
    /// <summary>
    /// The pager model's parts for <see cref="Pager"/> (forest_pager 1.1, docs/gdd/PAGER_CONTRACT.md), filled by
    /// Tools / Snow / Items / Build Pager on Resources/SnowPager: the LCD and the LED renderers with their lit / off materials,
    /// the three keys (they slide 1.2 mm into the body when pressed), and what keeps it readable at night
    /// (docs/gdd/research/RESEARCH_UI_3.md §5): the backlight as a real green light at the screen, a dim fill light that
    /// reaches only the pager (rendering layer <see cref="ViewmodelLayer"/>), the material that darkens the world behind it.
    /// </summary>
    public class PagerModel : MonoBehaviour
    {
        /// <summary>Rendering layer of things held in the view (the pager): its own fill light reaches only them.</summary>
        public const int ViewmodelLayer = 10;

        public MeshRenderer lcd, led;
        public Material lcdGlow, lcdOff, ledOn, ledOff;
        [Tooltip("Up, read, down")] public Transform[] keys = new Transform[3];
        [Tooltip("Green point light at the screen, on with the backlight")] public Light backlight;
        [Tooltip("Dim point light up right, the pager's rendering layer only")] public Light fill;
        [Tooltip("Unlit transparent black for the quad behind the raised pager")] public Material dim;
    }
}
