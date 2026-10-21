using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>Flight of a fired signal flare: ballistic with a little drag, burning out at the end. Local effect only.</summary>
    public class SignalFlareEffect : MonoBehaviour
    {
        Vector3 _v;
        float _life, _age;
        Light _light;

        public void Init(Vector3 velocity, float seconds) { _v = velocity; _life = seconds; _light = GetComponent<Light>(); }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            _v += (Physics.gravity * 0.35f - _v * 0.15f) * dt;          // a slow fall under its own drag
            transform.position += _v * dt;
            if (_light != null) _light.intensity = 400f * Mathf.Clamp01((_life - _age) / 1.5f) * (0.85f + 0.15f * Mathf.PerlinNoise(_age * 12f, 0f));
            if (_age >= _life) Destroy(gameObject);
        }
    }
}
