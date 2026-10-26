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
            // generated materials (SOUND_LIST_FULL «Предметы», user 26.10.2026) before the broad words below
            ("wheel", "item.wheel.bounce"), ("колес", "item.wheel.bounce"), ("sandladder", "item.sandladder.clang"), ("сэнд", "item.sandladder.clang"),
            ("fuse", "item.fuse.glass"), ("предохран", "item.fuse.glass"),
            ("_key", "item.impact.keys"), ("ключ", "item.impact.keys"),
            ("battery", "item.impact.heavymetal"), ("аккумул", "item.impact.heavymetal"), ("anvil", "item.impact.heavymetal"),
            ("wrench", "item.impact.heavymetal"), ("ключ гаеч", "item.impact.heavymetal"), ("ломик", "item.impact.heavymetal"),
            ("_tin", "item.impact.tin"), ("can_", "item.impact.tin"), ("консерв", "item.impact.tin"), ("жестян", "item.impact.tin"),
            ("cloth", "item.impact.cloth"), ("_rag", "item.impact.cloth"), ("_bag", "item.impact.cloth"), ("glove", "item.impact.cloth"),
            ("blanket", "item.impact.cloth"), ("jacket", "item.impact.cloth"), ("тряпк", "item.impact.cloth"), ("сумк", "item.impact.cloth"),
            ("мешок", "item.impact.cloth"), ("перчат", "item.impact.cloth"), ("одеял", "item.impact.cloth"), ("куртк", "item.impact.cloth"),
            ("ceramic", "item.impact.ceramic"), ("_cup", "item.impact.ceramic"), ("керам", "item.impact.ceramic"), ("чашк", "item.impact.ceramic"),
            ("plastic", "item.impact.plastic"), ("radio", "item.impact.plastic"), ("pager", "item.impact.plastic"), ("пластик", "item.impact.plastic"),
            ("рация", "item.impact.plastic"), ("пейджер", "item.impact.plastic"),
            ("rubber", "item.impact.rubber"), ("tyre", "item.impact.rubber"), ("резин", "item.impact.rubber"), ("шина", "item.impact.rubber"),
            ("thermos", "item.impact.liquid"), ("water", "item.impact.liquid"), ("термос", "item.impact.liquid"), ("вода", "item.impact.liquid"),
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
            _cue = CueFor(_item);
        }

        /// <summary>The impact cue of an item by the words of its label, key and name (metal when none matches).</summary>
        public static string CueFor(CargoItem item)
        {
            string text = (item.label + " " + item.itemKey + " " + item.name).ToLowerInvariant();
            foreach ((string word, string cue) in Materials)
                if (text.Contains(word)) return cue;
            return MetalCue;
        }

        void OnCollisionEnter(Collision c)
        {
            if (!_item.IsServer || Time.time < _nextAt) return;
            float v = c.relativeVelocity.magnitude;
            if (c.collider != null && c.collider.name.StartsWith("HIT_Ice", System.StringComparison.Ordinal) && v > 3f)
            {
                if (c.collider.name.StartsWith("HIT_Ice_Hatch", System.StringComparison.Ordinal))   // the ice over the bunker's hatch
                    SnowAudio.ServerPlay("base.bunker.ice.hit", c.GetContact(0).point);
                else
                {
                    SnowAudio.ServerPlay("world.ice.chip", c.GetContact(0).point);     // rime knocked off a mast or a dish
                    SnowAudio.ServerPlay("world.ice.fall", c.collider.bounds.center);
                }
            }
            if (_cue == "item.impact.glass" && v > 5.5f) { SnowAudio.ServerPlay("item.glass.break", transform.position); _nextAt = Time.time + Gap; return; }
            if (v < MinSpeed) return;
            _nextAt = Time.time + Gap;
            Vector3 at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            SnowAudio.ServerPlay(_cue, at, Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(MinSpeed, FullSpeed, v)));
        }

        float _nextRoll;

        /// <summary>Server: a bottle or a can rolling over the floor.</summary>
        void OnCollisionStay(Collision c)
        {
            if (!_item.IsServer || Time.time < _nextRoll) return;
            bool bottle = _cue == "item.impact.glass", can = _cue == "item.impact.tin";
            if (!bottle && !can) return;
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null || rb.angularVelocity.magnitude < 3f || rb.linearVelocity.magnitude < 0.2f) return;
            _nextRoll = Time.time + 0.7f;
            SnowAudio.ServerPlay(bottle ? "item.roll.bottle" : "item.roll.can", transform.position, 0.7f);
        }

        /// <summary>The rime zones (HIT_Ice_*) are triggers: a thrown thing that flies through one knocks ice off.</summary>
        void OnTriggerEnter(Collider other)
        {
            if (!_item.IsServer || Time.time < _nextAt || !other.name.StartsWith("HIT_Ice", System.StringComparison.Ordinal)) return;
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null || rb.linearVelocity.magnitude < 3f) return;
            _nextAt = Time.time + Gap;
            if (other.name.StartsWith("HIT_Ice_Hatch", System.StringComparison.Ordinal)) { SnowAudio.ServerPlay("base.bunker.ice.hit", transform.position); return; }
            SnowAudio.ServerPlay("world.ice.chip", transform.position);
            SnowAudio.ServerPlay("world.ice.fall", other.bounds.center);
        }
    }
}
