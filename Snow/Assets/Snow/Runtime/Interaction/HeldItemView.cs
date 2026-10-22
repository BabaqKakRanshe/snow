using System.Collections.Generic;
using Snow.Cargo;
using Snow.Character;
using UnityEngine;

namespace Snow.Interaction
{
    /// <summary>
    /// The owner's hand (user 21.10, game feel): a pocket-sized item in the hand is drawn every frame in front of the
    /// camera, after the camera has moved, so it answers the mouse at once — no network delay, no spring. It flies in from
    /// where it lay or from the pocket (ease-out), lags a little behind fast turns and settles (follow-through), swings
    /// with the steps, and near a wall it comes back towards the eye instead of going through. It keeps the way it lay
    /// when it was picked up (a sheet seen from above stays seen from above); a survival item turns its light, muzzle or
    /// flame socket where the eye looks (the flashlight shines forward). The pose goes to the server each tick (InteractionSensor), which moves the real
    /// item there for everyone else. Numbers TEMPORARY (docs/dev/Tuning/Cargo.md).
    /// </summary>
    [DefaultExecutionOrder(1000)]                                        // after FirstPersonView.LateUpdate moved the camera
    public class HeldItemView : MonoBehaviour
    {
        public InteractionSensor sensor;
        public FirstPersonView view;
        public HLCharacterMotor motor;
        [Tooltip("m: the hand point right / down / forward of the camera, for a small item")] public Vector3 handOffset = new Vector3(0.2f, -0.17f, 0.3f);
        [Tooltip("s: flight into the hand")] public float pickupSeconds = 0.15f;
        [Tooltip("1/s: how fast the hand follows a turn (lower = more lag)")] public float swayRate = 18f;
        [Tooltip("m: step swing at a walk")] public float bobAmount = 0.007f;
        [Tooltip("m: closest the hand comes to the eye at a wall")] public float minReach = 0.12f;

        // how each item sat in the hand last time (an item back from the pocket comes out the same way)
        static readonly Dictionary<CargoItem, Quaternion> Rest = new Dictionary<CargoItem, Quaternion>();
        readonly RaycastHit[] _hits = new RaycastHit[8];
        CargoItem _item;
        float _since, _radius, _phase;
        Vector3 _fromPos, _center;
        Quaternion _fromRot, _rest, _sway = Quaternion.identity;
        bool _aim;

        void LateUpdate()
        {
            CargoItem item = sensor != null ? sensor.Held : null;
            Camera cam = view != null ? view.cam : null;
            if (item == null || !item.Pocketable || cam == null || !cam.enabled)
            {
                _item = null;
                if (sensor != null) sensor.ClearHandPose();
                return;
            }
            Transform eye = cam.transform;
            if (item != _item) Begin(item, eye);

            float dt = Time.deltaTime;
            _sway = Quaternion.Slerp(_sway, eye.rotation, 1f - Mathf.Exp(-swayRate * dt));
            float speed = motor != null && motor.Grounded ? new Vector2(motor.Velocity.x, motor.Velocity.z).magnitude : 0f;
            _phase += speed * dt * 2.4f;
            float k = Mathf.Clamp01(speed / 4f) * bobAmount;
            Vector3 offset = handOffset + new Vector3(Mathf.Sin(_phase) * k, -Mathf.Abs(Mathf.Sin(_phase)) * k, _radius);

            // a wall between the eye and the hand pulls the hand back towards the eye
            Vector3 point = eye.position + _sway * offset;
            Vector3 d = point - eye.position;
            float len = d.magnitude;
            float free = Free(eye.position, d / len, len + _radius, item);
            if (free < len + _radius) point = eye.position + d / len * Mathf.Max(minReach, free - _radius);

            Quaternion rot = _sway * _rest;
            Vector3 pivot = point - rot * _center;
            float t = pickupSeconds > 0f ? Mathf.Clamp01((Time.time - _since) / pickupSeconds) : 1f;
            float e = 1f - (1f - t) * (1f - t) * (1f - t);                 // ease-out cubic: fast start, soft landing
            pivot = Vector3.LerpUnclamped(_fromPos, pivot, e);
            rot = Quaternion.SlerpUnclamped(_fromRot, rot, e);
            item.transform.SetPositionAndRotation(pivot, rot);
            sensor.SetHandPose(pivot, rot);
        }

        void Begin(CargoItem item, Transform eye)
        {
            _item = item;
            _since = Time.time;
            _fromPos = item.transform.position;
            _fromRot = item.transform.rotation;
            _sway = eye.rotation;
            Transform tip = Tip(item.transform);
            _aim = tip != null;
            // the socket forward where the eye looks (the Forest flashlight's Socket_Light faces the model's -X)
            if (_aim) _rest = Quaternion.Inverse(Quaternion.Inverse(item.transform.rotation) * tip.rotation);
            else if (!Rest.TryGetValue(item, out _rest))
            {
                if (Rest.Count > 64) Rest.Clear();                     // entries of items long gone
                Rest[item] = _rest = Quaternion.Inverse(eye.rotation) * item.transform.rotation;
            }
            // the middle of its meshes, in the item's own rotation (renderers may be off: it may come from a pocket)
            bool any = false;
            var b = new Bounds(item.transform.position, Vector3.zero);
            foreach (MeshFilter mf in item.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds m = mf.sharedMesh.bounds;
                var w = new Bounds(mf.transform.TransformPoint(m.center), Vector3.Scale(m.size, Abs(mf.transform.lossyScale)));
                if (!any) { b = w; any = true; } else b.Encapsulate(w);
            }
            _center = Quaternion.Inverse(item.transform.rotation) * (b.center - item.transform.position);
            _radius = Mathf.Clamp(b.extents.magnitude, 0.03f, 0.3f);
        }

        float Free(Vector3 from, Vector3 dir, float range, CargoItem item)
        {
            int n = Physics.RaycastNonAlloc(from, dir, _hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = range;
            Transform me = sensor.transform;
            for (int i = 0; i < n; i++)
            {
                Collider c = _hits[i].collider;
                if (c.transform.IsChildOf(me) || c.transform.IsChildOf(item.transform)) continue;
                if (_hits[i].distance < best) best = _hits[i].distance;
            }
            return best;
        }

        // the light, muzzle or flame socket of a survival item (SurvivalItem's names), or null
        static Transform Tip(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Socket_Light" || t.name == "Socket_Muzzle" || t.name == "Socket_Flame") return t;
            return null;
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
