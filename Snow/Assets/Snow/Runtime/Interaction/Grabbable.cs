using UnityEngine;

namespace Snow.Interaction
{
    /// <summary>
    /// Marks a physical item that "use" held down will grab at the hit point (GRAB-01). The grab itself is P02;
    /// in P01 the sensor only recognises the target and shows its hint.
    /// </summary>
    public class Grabbable : MonoBehaviour
    {
        public string label = "Ящик";
    }
}
