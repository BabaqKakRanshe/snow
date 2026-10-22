using UnityEngine;

namespace Snow.Vehicles.Van
{
    public enum SurfaceKind : byte { PackedSnow, Road, LooseSnow, Ice, SandTrack }

    /// <summary>
    /// What a ground collider is for a tyre (VAN3_INTEGRATION_UNITY.md §4.2, V-02 OPEN). A collider without it is packed
    /// snow (the whole forest by default). μ values live in <see cref="VanTuning"/>; this only names the surface.
    /// </summary>
    public class VanSurface : MonoBehaviour
    {
        public SurfaceKind kind = SurfaceKind.PackedSnow;

        public static SurfaceKind Of(Collider c)
        {
            if (c == null) return SurfaceKind.PackedSnow;
            var s = c.GetComponentInParent<VanSurface>();
            return s != null ? s.kind : SurfaceKind.PackedSnow;
        }
    }
}
