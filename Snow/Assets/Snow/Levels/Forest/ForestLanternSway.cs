using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The hut lantern swaying a little on its hook in the wind (user 25.10, QUESTIONS_HUT_2 10: the light "breathes", the
    /// eye catches the movement from the road). Moves only the light (and its far halo) around the place it was set at:
    /// the lantern's mesh is static. Two incommensurate sines, so the motion never looks like a loop. Purely visual.
    /// </summary>
    public class ForestLanternSway : MonoBehaviour
    {
        [Tooltip("Largest offset from the rest place (m)")] public float amplitude = 0.04f;
        [Tooltip("Main period (s)")] public float period = 2.6f;

        Vector3 _rest;
        float _phase;

        void OnEnable()
        {
            _rest = transform.localPosition;
            _phase = Random.value * 10f;
        }

        void OnDisable() => transform.localPosition = _rest;

        void Update()
        {
            float t = Time.time / Mathf.Max(0.1f, period) * Mathf.PI * 2f + _phase;
            float x = Mathf.Sin(t) * 0.7f + Mathf.Sin(t * 1.73f + 1.1f) * 0.3f;
            float z = Mathf.Sin(t * 0.61f + 2.3f) * 0.5f;
            transform.localPosition = _rest + new Vector3(x, -Mathf.Abs(x) * 0.15f, z) * amplitude;
        }
    }
}
