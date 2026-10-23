using Snow.Cargo;
using UnityEngine;

namespace Snow.Audio
{
    /// <summary>
    /// Server: a cargo item hitting something sounds by its material (glass, metal, paper, wood), guessed once from its
    /// label, key and object name; everyone hears it and creatures get a quiet noise (cue.noise). Clients see a kinematic
    /// copy, so only the server feels the hit. Numbers TEMPORARY.
    /// </summary>
    [RequireComponent(typeof(CargoItem))]
    public class ItemImpactSound : MonoBehaviour
    {
        const float MinSpeed = 1.2f, FullSpeed = 6f, Gap = 0.15f;

        static readonly (string word, string cue)[] Materials =
        {
            ("glass", "item.impact.glass"), ("bottle", "item.impact.glass"), ("jar", "item.impact.glass"), ("mug", "item.impact.glass"),
            ("plate", "item.impact.glass"), ("бутыл", "item.impact.glass"), ("банка", "item.impact.glass"), ("кружк", "item.impact.glass"),
            ("тарел", "item.impact.glass"), ("стекл", "item.impact.glass"),
            ("paper", "item.impact.paper"), ("book", "item.impact.paper"), ("box", "item.impact.paper"), ("card", "item.impact.paper"),
            ("binder", "item.impact.paper"), ("newspaper", "item.impact.paper"), ("note", "item.impact.paper"), ("magazine", "item.impact.paper"),
            ("бумаг", "item.impact.paper"), ("книг", "item.impact.paper"), ("короб", "item.impact.paper"), ("газет", "item.impact.paper"),
            ("папк", "item.impact.paper"), ("картон", "item.impact.paper"), ("записк", "item.impact.paper"),
            ("crate", "item.impact.wood"), ("wood", "item.impact.wood"), ("ящик", "item.impact.wood"), ("доск", "item.impact.wood"),
        };
        const string MetalCue = "item.impact.metal";                // tools, cans, jerrycans, flashlights … the rest

        CargoItem _item;
        string _cue;
        float _nextAt;

        void Awake()
        {
            _item = GetComponent<CargoItem>();
            string text = (_item.label + " " + _item.itemKey + " " + name).ToLowerInvariant();
            _cue = MetalCue;
            foreach ((string word, string cue) in Materials)
                if (text.Contains(word)) { _cue = cue; break; }
        }

        void OnCollisionEnter(Collision c)
        {
            if (!_item.IsServer || Time.time < _nextAt) return;
            float v = c.relativeVelocity.magnitude;
            if (v < MinSpeed) return;
            _nextAt = Time.time + Gap;
            Vector3 at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            SnowAudio.ServerPlay(_cue, at, Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(MinSpeed, FullSpeed, v)));
        }
    }
}
