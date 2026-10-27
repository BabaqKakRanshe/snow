using UnityEngine;

namespace Snow.Tasks
{
    /// <summary>
    /// Where the raised pager sits in the view: Resources/PagerView (made once by Tools / Snow / Items / Build Pager, then
    /// edited by hand - a rebuild keeps it). The root is the pager's eye: its Camera's field of view is the eye's lens and the
    /// eye looks along +Z. <see cref="pager"/> is the SnowPager prefab inside it: its local place and turn are the pager held
    /// up; <see cref="lowered"/> is an empty whose local place and turn are the pager down at the hip, where the raise starts.
    /// Select the root in Prefab Mode to see the camera's preview. <see cref="Pager"/> adds the hand's life (sway, steps,
    /// breath, presses) on top of these two poses.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PagerView : MonoBehaviour
    {
        [Tooltip("The SnowPager inside: its place and turn = the pager held up")] public Transform pager;
        [Tooltip("An empty: its place and turn = the pager lowered (the raise starts here)")] public Transform lowered;
    }
}
