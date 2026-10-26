using Snow.Audio;
using UnityEngine;

namespace Snow.Cargo
{
    /// <summary>
    /// Cargo straps and stow points on every peer from the replicated state (SOUND_LIST_FULL «Груз», user 26.10.2026):
    /// secured / unsecured, stowed / taken out. A strap that snaps is the server's (CargoItem.OnJointBreak).
    /// </summary>
    [RequireComponent(typeof(CargoItem))]
    public class CargoSounds : MonoBehaviour
    {
        CargoItem _item;
        CargoState _was;
        bool _init;
        float _since, _nextCarry;
        Vector3 _lastPos;
        (string pickup, string carry, string putdown) _own;

        // item-specific handling by the words of its label / key (SOUND_LIST_FULL «Предметы», user 26.10.2026);
        // the first match wins, an empty field falls back to the material sound
        static readonly (string word, string pickup, string carry, string putdown)[] Handling =
        {
            ("workertag", "item.workertag.jingle", "item.workertag.jingle", ""), ("жетон", "item.workertag.jingle", "item.workertag.jingle", ""),
            ("_key", "item.keys.jingle", "item.keys.jingle", ""), ("ключ", "item.keys.jingle", "item.keys.jingle", ""),
            ("jerrycan", "", "item.jerrycan.slosh", "item.putdown.heavy.metal"), ("канистр", "", "item.jerrycan.slosh", "item.putdown.heavy.metal"),
            ("bucket", "item.bucket.handle", "item.bucket.slosh", ""), ("ведр", "item.bucket.handle", "item.bucket.slosh", ""),
            ("bottle", "", "item.bottle.slosh", ""), ("бутыл", "", "item.bottle.slosh", ""),
            ("_can", "", "item.can.slosh", ""), ("консерв", "", "item.can.slosh", ""),
            ("medic", "", "item.medicine.rattle", ""), ("аптеч", "", "item.medicine.rattle", ""), ("таблет", "", "item.medicine.rattle", ""),
            ("toolbox", "", "item.toolbox.rattle", ""), ("инструмент", "item.tool.pickup", "item.toolbox.rattle", ""),
            ("smallparts", "", "item.smallparts.rattle", ""), ("болт", "", "item.smallparts.rattle", ""),
            ("fuse", "item.fuse.tin", "", ""), ("предохран", "item.fuse.tin", "", ""),
            ("bulb", "item.bulb.tinkle", "", ""), ("лампоч", "item.bulb.tinkle", "", ""), ("sodium", "item.sodium.glass", "", ""),
            ("wrench", "item.tool.pickup", "", ""), ("hammer", "item.tool.pickup", "", ""), ("молот", "item.tool.pickup", "", ""),
            ("chain", "", "item.chain.clink", ""), ("цеп", "", "item.chain.clink", ""),
            ("rope", "item.rope.handle", "", ""), ("верёв", "item.rope.handle", "", ""), ("трос", "item.rope.handle", "", ""),
            ("cards", "item.cards.handle", "", ""), ("карты", "item.cards.handle", "", ""),
            ("backpack", "item.backpack.handle", "", ""), ("рюкзак", "item.backpack.handle", "", ""),
            ("trash", "", "item.trashbag.carry", ""), ("мусор", "", "item.trashbag.carry", ""),
            ("_bag", "item.bag.canvas", "", ""), ("сумк", "item.bag.canvas", "", ""),
            ("newspaper", "item.newspaper.rustle", "", ""), ("газет", "item.newspaper.rustle", "", ""),
            ("binder", "item.binder.clip", "", ""), ("папк", "item.binder.clip", "", ""),
            ("envelope", "item.envelope.open", "", ""), ("конверт", "item.envelope.open", "", ""), ("letter", "item.envelope.open", "", ""),
            ("waybill", "item.waybill.peel", "", ""), ("накладн", "item.waybill.peel", "", ""),
            ("postcard", "item.postcard", "", ""), ("открытк", "item.postcard", "", ""),
            ("cassette", "item.cassette", "", ""), ("кассет", "item.cassette", "", ""),
            ("paper", "item.paper.flip", "", ""), ("бумаг", "item.paper.flip", "", ""), ("бланк", "item.paper.flip", "", ""),
            ("battery", "item.battery.heavy", "", ""), ("аккумул", "item.battery.heavy", "", ""),
            ("chocks", "", "", "item.chocks.thunk"), ("колодк", "", "", "item.chocks.thunk"),
            ("crate", "", "item.crate.carry", ""),
            ("pump", "", "", "item.pump.putdown"), ("насос", "", "", "item.pump.putdown"),
            ("crt", "", "", "item.crt.putdown"), ("монитор", "", "", "item.crt.putdown"),
            ("relay", "", "", "item.relay.putdown"), ("реле", "", "", "item.relay.putdown"),
            ("generator", "", "", "item.generator.putdown"), ("генератор", "", "", "item.generator.putdown"),
            ("funnel", "", "", "item.funnel.set"), ("воронк", "", "", "item.funnel.set"),
            ("doll", "item.doll.handle", "", ""), ("кукл", "item.doll.handle", "", ""),
            ("headlamp", "item.headlamp.wear", "", ""), ("налобн", "item.headlamp.wear", "", ""),
            ("_pot", "item.pot.lid", "", ""), ("кастрюл", "item.pot.lid", "", ""),
            ("sled", "", "item.sled.drag", ""), ("санки", "", "item.sled.drag", ""),
            ("_cart", "", "item.cart.roll", ""), ("тележк", "", "item.cart.roll", ""),
        };

        void Awake()
        {
            _item = GetComponent<CargoItem>();
            string text = (_item.label + " " + _item.itemKey + " " + name).ToLowerInvariant();
            foreach (var h in Handling)
                if (text.Contains(h.word)) { _own = (h.pickup, h.carry, h.putdown); break; }
        }

        void Update()
        {
            if (!_item.IsSpawned) return;
            CargoState s = _item.State;
            if (!_init) { _was = s; _init = true; _since = Time.time; _lastPos = transform.position; return; }
            Carrying(s);
            if (s == _was) return;
            if (Time.time - _since > 1f)
            {
                if (s == CargoState.Secured) SnowAudio.Play("cargo.secure", transform.position);
                else if (_was == CargoState.Secured) SnowAudio.Play("cargo.unsecure", transform.position);
                if (s == CargoState.Stowed) SnowAudio.Play("cargo.stow", transform.position);
                else if (_was == CargoState.Stowed) SnowAudio.Play("cargo.unstow", transform.position);
                if (s == CargoState.Carried && _was == CargoState.Loose) SnowAudio.Play(string.IsNullOrEmpty(_own.pickup) ? Pickup() : _own.pickup, transform.position);
                if (s == CargoState.Loose && _was == CargoState.Carried)
                {
                    Rigidbody rb = GetComponent<Rigidbody>();
                    if (rb != null && rb.linearVelocity.magnitude > 4f) SnowAudio.Play("item.throw.whoosh", transform.position);
                    else if (!string.IsNullOrEmpty(_own.putdown)) SnowAudio.Play(_own.putdown, transform.position);
                    else if (!_item.Pocketable) SnowAudio.Play(PutDown(), transform.position);
                }
            }
            _was = s;
        }

        /// <summary>Carried and walking: its own slosh, rattle or jingle every second or two.</summary>
        void Carrying(CargoState s)
        {
            Vector3 p = transform.position;
            float moved = (p - _lastPos).magnitude;
            _lastPos = p;
            if (s != CargoState.Carried || string.IsNullOrEmpty(_own.carry) || moved < 0.01f || Time.time < _nextCarry) return;
            _nextCarry = Time.time + Random.Range(1.2f, 2.8f);
            SnowAudio.Play(_own.carry, p, 0.7f);
        }

        /// <summary>item.pickup.* by the item's material (the same words as its impacts).</summary>
        string Pickup()
        {
            string m = Material();
            return m == "glass" || m == "ceramic" ? "item.pickup.glass" : m == "paper" ? "item.pickup.paper"
                 : m == "cloth" ? "item.pickup.cloth" : m == "plastic" ? "item.pickup.plastic" : "item.pickup.metal";
        }

        /// <summary>A heavy thing set down: item.putdown.* by material.</summary>
        string PutDown()
        {
            string m = Material();
            return m == "wood" ? "item.putdown.heavy.wood" : m == "paper" ? "item.putdown.box" : m == "cloth" ? "item.putdown.sack" : "item.putdown.heavy.metal";
        }

        string Material()
        {
            string cue = ItemImpactSound.CueFor(_item);                      // item.impact.<material>
            return cue.Substring(cue.LastIndexOf('.') + 1);
        }
    }
}