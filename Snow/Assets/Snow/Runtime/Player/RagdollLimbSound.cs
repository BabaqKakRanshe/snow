using Snow.Audio;
using UnityEngine;

namespace Snow.Player
{
    /// <summary>A ragdoll limb knocking on the ground or a wall as the body falls (SOUND_LIST_FULL ragdoll.limb).</summary>
    public class RagdollLimbSound : MonoBehaviour
    {
        static float s_next;

        void OnCollisionEnter(Collision c)
        {
            if (Time.time < s_next || c.relativeVelocity.magnitude < 1.5f) return;
            s_next = Time.time + 0.12f;                                    // one body: not every limb at once
            SnowAudio.Play("ragdoll.limb", transform.position, Mathf.Clamp(c.relativeVelocity.magnitude / 5f, 0.4f, 1.2f));
        }
    }
}
