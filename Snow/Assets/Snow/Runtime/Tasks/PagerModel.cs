using UnityEngine;

namespace Snow.Tasks
{
    /// <summary>
    /// The pager model's parts for <see cref="Pager"/> (forest_pager 1.0, docs/gdd/PAGER_CONTRACT.md), filled by
    /// Tools / Snow / Items / Build Pager on Resources/SnowPager: the LCD and the LED renderers with their lit / off materials,
    /// the three keys (they slide 1.2 mm into the body when pressed).
    /// </summary>
    public class PagerModel : MonoBehaviour
    {
        public MeshRenderer lcd, led;
        public Material lcdGlow, lcdOff, ledOn, ledOff;
        [Tooltip("Up, read, down")] public Transform[] keys = new Transform[3];
    }
}
