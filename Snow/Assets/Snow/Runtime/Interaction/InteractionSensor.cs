using Snow.Cargo;
using Snow.Character;
using Snow.Input;
using Snow.Net;
using Snow.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UtilityPlane;

namespace Snow.Interaction
{
    /// <summary>
    /// Owner-side "use" (§7.4, INPUT-02): a 2 m ray from the eye (or from the free cursor in a seat) hits an interaction
    /// volume on the Interact layer, a physical item or nothing. Solid geometry in front blocks it. E on foot, a short F
    /// or the left button in a seat. The server decides. <see cref="Pick"/> is the one rule of what a ray from this
    /// player's eye reaches; the hover card and the Alt reveal use the same rule, so they cannot disagree.
    /// Items (user 21.10, after Lethal Company / Phasmophobia): E is a press, never a hold. A pocket-sized item goes into
    /// the hand and a pocket slot (drawn by HeldItemView, listed by PocketsHud); Q puts it away, 1–8 or the wheel take
    /// another one out, G throws it. A heavy thing is carried in front with both hands: E takes it, E or G puts it down.
    /// The hand shows a taken item at once and the server confirms (or says no and it goes back).
    /// With an item in hand on foot the left button uses the zone under the ray with that item (FOREST_U3 F4.5: pour from
    /// a jerrycan, unlock with a key, scrub with a rag); the ray passes through the held item.
    /// </summary>
    public class InteractionSensor : MonoBehaviour
    {
        public PlayerNet net;
        public PlayerController controller;
        public FirstPersonView view;
        [Tooltip("Layers the use ray can hit; triggers count (interaction volumes are triggers)")] public LayerMask mask = ~0;
        [Tooltip("s: a taken item stays in the hand this long without the server's answer, then goes back")] public float confirmTimeout = 0.75f;

        public InteractionHost TargetHost { get; private set; }
        public string TargetId { get; private set; }
        public UtilityPlaneInteractable TargetVolume { get; private set; }
        public Grabbable TargetGrab { get; private set; }
        public CargoItem TargetCargo { get; private set; }
        public Vector3 TargetPoint { get; private set; }
        public string Hint { get; private set; }
        /// <summary>The item this player holds (GRAB-01): in the hand (pocket-sized) or in front with both hands; null if none.</summary>
        public CargoItem Held { get; private set; }
        /// <summary>The held item is heavy: carried in front, both hands busy.</summary>
        public bool HeldHeavy => Held != null && !Held.Pocketable;
        /// <summary>Last pocket slot asked for with 1–8 or the wheel (-1: put away), and when (PocketsHud: the slot pops at once).</summary>
        public int RequestedSlot { get; private set; } = -1;
        public float RequestedAt { get; private set; } = -10f;
        /// <summary>When the last short message (mostly a refusal) was shown.</summary>
        public float MessageAt { get; private set; } = -10f;

        [Tooltip("m: how far past the surface of a solid part a ray still reaches a zone embedded in it (model: seat zones " +
                 "3 cm inside the cushion, the fuel shutoff pull 5 cm inside the pedestal)")]
        public float embeddedDepth = 0.1f;

        string _message;
        float _messageUntil;
        bool _pressSent;
        InteractionHost _pressHost;
        string _pressId;
        float _pressTime, _pressHold;
        // left button with an item in hand: its own press, released into the zone it pressed (F4.5)
        bool _lmbSent;
        InteractionHost _lmbHost;
        string _lmbId;
        float _lmbTime, _lmbHold;
        float _holdDistance;
        float _heldSince;
        bool _heldConfirmed;
        bool _handPose;
        Vector3 _handPosition;
        Quaternion _handRotation;
        readonly RaycastHit[] _hits = new RaycastHit[16];

        void OnEnable()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkTickSystem != null)
                NetworkManager.Singleton.NetworkTickSystem.Tick += OnTick;
        }

        void OnDisable()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkTickSystem != null)
                NetworkManager.Singleton.NetworkTickSystem.Tick -= OnTick;
            if (HeldHeavy) net.RequestRelease(Held);                 // despawn: nothing heavy stays grabbed; the hand keeps its item
            Held = null;
        }

        /// <summary>The hand target of a heavy thing: in front of the eye at the distance it was taken from.</summary>
        public Vector3 HandTarget => view.EyePosition + view.EyeRotation * Vector3.forward * _holdDistance;

        /// <summary>HeldItemView, every frame: where the item in the hand is drawn (sent to the server each tick).</summary>
        public void SetHandPose(Vector3 position, Quaternion rotation)
        {
            _handPose = true;
            _handPosition = position;
            _handRotation = rotation;
        }

        public void ClearHandPose() => _handPose = false;

        // hand target once per network tick, unreliable (GRAB-01 over NET-02)
        void OnTick()
        {
            if (Held == null) return;
            if (HeldHeavy) net.SendGrabTarget(Held, HandTarget, view.EyeRotation);
            else if (_handPose) net.SendGrabTarget(Held, _handPosition, _handRotation);
        }

        /// <summary>The server put an item into this player's hands (PlayerNet.ServerGiveItem).</summary>
        public void AcceptGiven(CargoItem item, float holdDistance)
        {
            SetHeld(item, true);
            _holdDistance = Mathf.Clamp(holdDistance, GrabMath.MinHold, GrabMath.MaxHold);
            BenchLog.Event("given_item", "item", item.name);
        }

        public void ShowMessage(string text, float seconds = 2f)
        {
            _message = text;
            _messageUntil = Time.time + seconds;
            MessageAt = Time.time;
        }

        /// <summary>A short F in the seat: press and release on the current target.</summary>
        public void Tap()
        {
            if (TargetHost == null)
            {
                ShowMessage("Удерживайте F, чтобы выйти");
                return;
            }
            net.RequestUse(TargetHost, TargetId, UsePhase.Press);
            if (TargetVolume != null && TargetVolume.kind == "momentary") net.RequestUse(TargetHost, TargetId, UsePhase.Release);
        }

        void SetHeld(CargoItem item, bool confirmed)
        {
            Held = item;
            _heldSince = Time.time;
            _heldConfirmed = confirmed;
        }

        // the hands as the server has them: a taken item waits for its answer a moment; one the server let go (thrown,
        // pocketed, used up, secured) or never gave leaves the hand; one the server put there (a pocket slot, a van part)
        // comes in
        void Reconcile(InputContext ctx)
        {
            ulong me = net.OwnerClientId;
            if (Held != null && !Held.IsSpawned) Held = null;
            if (Held != null)
            {
                if (Held.GrabbedBy(me)) _heldConfirmed = true;
                else if (_heldConfirmed || Time.time - _heldSince > confirmTimeout) Held = null;
            }
            if (ctx != InputContext.OnFoot || (Held != null && !_heldConfirmed)) return;
            CargoItem hand = CargoItem.InHandOf(me);
            if (hand != null && hand != Held) SetHeld(hand, true);
        }

        void Update()
        {
            if (controller == null || controller.Source == null) return;
            InputContext ctx = controller.Source.Context;
            PlayerInputFrame f = controller.Frame, last = controller.PreviousFrame;

            Ray ray = ctx == InputContext.SeatCursor && view.cam != null && view.cam.enabled
                // the pointer is in screen pixels, the camera may render into a smaller RenderTexture (PS1 frame)
                ? view.cam.ViewportPointToRay(new Vector3(f.Pointer.x / Screen.width, f.Pointer.y / Screen.height, 0f))
                : new Ray(view.EyePosition, view.EyeRotation * Vector3.forward);
            Resolve(ray);

            // out of the on-foot context (seated, menu): the server already put the item away (PlayerNet.ServerSeated)
            if (ctx != InputContext.OnFoot)
            {
                if (Held != null) Held = null;
                ReleaseLeft();
            }
            Reconcile(ctx);

            bool down, up;
            if (ctx == InputContext.OnFoot) { down = f.Use && !last.Use; up = !f.Use && last.Use; }
            else if (ctx == InputContext.SeatCursor) { down = f.Click && !last.Click; up = !f.Click && last.Click; }
            else { down = up = false; }                              // seat without cursor: SeatController calls Tap()

            if (down && HeldHeavy)
            {
                // E again: put the heavy thing down (a press, not a held key: Game Accessibility Guidelines, XAG 107)
                net.RequestRelease(Held);
                Held = null;
            }
            else if (down && TargetHost != null)
            {
                net.RequestUse(TargetHost, TargetId, UsePhase.Press);
                _pressSent = true;
                _pressHost = TargetHost;
                _pressId = TargetId;
                _pressTime = Time.time;
                _pressHold = TargetHost.HoldSeconds(TargetId);
            }
            else if (down && TargetCargo != null && ctx == InputContext.OnFoot)
            {
                TakeItem(TargetCargo);
            }
            else if (down && TargetGrab != null)
            {
                ShowMessage("Это нельзя взять");
            }
            if (up && _pressSent)
            {
                // the release goes to the zone that was pressed: momentary controls, held kit actions, pouring (И4)
                _pressSent = false;
                if (_pressHost != null) net.RequestUse(_pressHost, _pressId, UsePhase.Release);
                _pressHost = null;
            }

            // mouse wheel on a knob (И2 F, ENGINEERING): one step per notch, up = clockwise, down = back; elsewhere on foot
            // it goes through the pockets
            if (Mouse.current != null && ctx != InputContext.None)
            {
                float wheel = Mouse.current.scroll.ReadValue().y;
                bool knob = TargetHost != null && TargetVolume != null && (TargetVolume.kind == "rotary" || TargetVolume.kind == "selector");
                if (knob && wheel > 0.01f) net.RequestUse(TargetHost, TargetId, UsePhase.Press);
                else if (knob && wheel < -0.01f) net.RequestUse(TargetHost, TargetId, UsePhase.Back);
                else if (!knob && ctx == InputContext.OnFoot && Mathf.Abs(wheel) > 0.01f) CycleSlot(wheel < 0f ? 1 : -1);
            }

            // left mouse button with an item in hand: the zone under the ray, used with that item (F4.5); otherwise a
            // survival item's own action (TEMPORARY, user decision 16.10). Seats and kit zones are not used with an item.
            if (ctx == InputContext.OnFoot && Held != null && Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    if (TargetHost != null && TargetVolume != null && TargetVolume.kind != "seat" && TargetVolume.kind != "kit")
                    {
                        net.RequestUse(TargetHost, TargetId, UsePhase.Press);
                        _lmbSent = true;
                        _lmbHost = TargetHost;
                        _lmbId = TargetId;
                        _lmbTime = Time.time;
                        _lmbHold = TargetHost.HoldSeconds(TargetId);
                    }
                    else if (Held.TryGetComponent(out Snow.Aircraft.SurvivalItem _)) net.RequestItemAction(Held);
                }
                else if (Mouse.current.leftButton.wasReleasedThisFrame) ReleaseLeft();
            }
            else if (_lmbSent && (Held == null || Mouse.current == null || !Mouse.current.leftButton.isPressed)) ReleaseLeft();

            // pockets (TEMPORARY keys, after Lethal Company / Phasmophobia): G throw, Q put away, 1–8 take out
            Keyboard kb = Keyboard.current;
            if (ctx == InputContext.OnFoot && kb != null)
            {
                if (kb.gKey.wasPressedThisFrame && Held != null)
                {
                    net.RequestDrop();
                    if (HeldHeavy) Held = null;
                }
                if (kb.qKey.wasPressedThisFrame)
                {
                    if (Held == null) ShowMessage("Q — убрать в карман то, что в руке");
                    else if (HeldHeavy) ShowMessage("Слишком тяжело для кармана");
                    else { net.RequestPocket(); Requested(-1); }
                }
                for (int s = 0; s < CargoItem.PocketSlots; s++)
                    if (kb[(UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.Digit1 + s)].wasPressedThisFrame) TakeSlot(s);
            }

            // R: secure the held or looked-at heavy item on the nearest tie-down, or release it from its tie-down (SECURE-01)
            if (ctx == InputContext.OnFoot && f.Secure && !last.Secure)
            {
                CargoItem item = HeldHeavy ? Held : TargetCargo;
                if (item != null && item.Pocketable) ShowMessage("Мелочь не крепят: возьмите её [E]");
                else if (item != null)
                {
                    net.RequestSecure(item, item.State != CargoState.Secured);
                    if (item == Held) Held = null;                     // the server ends all grabs when it secures
                }
                else ShowMessage("Наведитесь на ящик (или несите его) и нажмите R");
            }
        }

        // E on an item: a pocket-sized one flies into the hand at once (the server confirms), a heavy one is grabbed where
        // the ray hit it (GRAB-01), in the item's own space
        void TakeItem(CargoItem item)
        {
            if (item.Pocketable && !item.CarriedBy(net.OwnerClientId) && CargoItem.FreeSlot(net.OwnerClientId) < 0)
            {
                ShowMessage($"Карманы полны ({CargoItem.PocketSlots}): выбросьте что-нибудь [G]");
                return;
            }
            SetHeld(item, false);
            _holdDistance = Mathf.Clamp(Vector3.Distance(view.EyePosition, TargetPoint), GrabMath.MinHold, GrabMath.MaxHold);
            net.RequestGrab(item, item.transform.InverseTransformPoint(TargetPoint));
            BenchLog.Event("grab_request", "item", item.name, "point", TargetPoint);
        }

        void TakeSlot(int slot)
        {
            ulong me = net.OwnerClientId;
            if (HeldHeavy) { ShowMessage("Руки заняты: сначала поставьте [E]"); return; }
            if (CargoItem.InSlot(me, slot) == null) { ShowMessage($"Карман {slot + 1} пуст"); return; }
            net.RequestSlot(slot);
            Requested(slot);
        }

        // the wheel: to the next (or previous) pocket that has something in it
        void CycleSlot(int step)
        {
            if (HeldHeavy) return;
            ulong me = net.OwnerClientId;
            CargoItem hand = CargoItem.InHandOf(me);
            int from = hand != null ? hand.Slot : step > 0 ? -1 : CargoItem.PocketSlots;
            for (int i = 1; i <= CargoItem.PocketSlots; i++)
            {
                int s = ((from + step * i) % CargoItem.PocketSlots + CargoItem.PocketSlots) % CargoItem.PocketSlots;
                if (s == from) return;
                if (CargoItem.InSlot(me, s) == null) continue;
                net.RequestSlot(s);
                Requested(s);
                return;
            }
        }

        void Requested(int slot)
        {
            RequestedSlot = slot;
            RequestedAt = Time.time;
        }

        void ReleaseLeft()
        {
            if (!_lmbSent) return;
            _lmbSent = false;
            if (_lmbHost != null) net.RequestUse(_lmbHost, _lmbId, UsePhase.Release);
            _lmbHost = null;
        }

        void Resolve(Ray ray)
        {
            TargetHost = null;
            TargetId = null;
            TargetVolume = null;
            TargetGrab = null;
            TargetCargo = null;
            Hint = null;
            Component target = Pick(ray, InteractionHost.UseRange, out Vector3 point);
            switch (target)
            {
                case UtilityPlaneInteractable volume:
                    var host = volume.GetComponentInParent<InteractionHost>();
                    TargetHost = host;
                    TargetId = volume.id;
                    TargetVolume = volume;
                    string state = host.Describe(volume.id);
                    Hint = $"{volume.label}{(string.IsNullOrEmpty(state) ? "" : " — " + state)}   [{Key()}] {host.ActionText(volume.id)}";
                    break;
                case CargoItem cargo:
                    TargetCargo = cargo;
                    TargetPoint = point;
                    Hint = $"{cargo.label} ({cargo.massKg:0.##} кг) — {cargo.Describe()}   [E] {(cargo.Pocketable ? "взять" : "нести")}";
                    if (cargo.TryGetComponent(out Snow.Aircraft.SurvivalItem si) && si.ActionText() is string act) Hint += $"   [ЛКМ в руках] {act}";
                    break;
                case Grabbable grab:
                    TargetGrab = grab;
                    Hint = grab.label;
                    break;
            }
        }

        /// <summary>
        /// What a ray from this player's eye reaches first within range: an interaction zone, a cargo item, a grabbable,
        /// or nothing (a wall). Rules, in ray order:
        ///  - the player's own colliders and, when seated, the seat's colliders around the eye are part of the player;
        ///  - a zone the player may not use from here is transparent: seats while seated, *_In from outside, *_Out from
        ///    inside (InteractionHost.SideAllows), and zones the host closes for now (InteractionHost.ZoneOpen: stowage
        ///    without a fitting item in hand, folded troop seats);
        ///  - a solid part is a wall, except that a zone embedded in that very part is reached if the ray passes through
        ///    the zone within embeddedDepth of the surface.
        /// </summary>
        public Component Pick(Ray ray, float range, out Vector3 point)
        {
            point = default;
            int n = Physics.RaycastNonAlloc(ray, _hits, range, mask, QueryTriggerInteraction.Collide);
            System.Array.Sort(_hits, 0, n, HitOrder);
            Collider[] seat = SeatColliders();
            Vector3 eye = ray.origin;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                Collider c = h.collider;
                if (c.transform.IsChildOf(transform) || System.Array.IndexOf(seat, c) >= 0) continue;
                if (Held != null && c.transform.IsChildOf(Held.transform)) continue;   // the item in hand hangs on the ray (F4.5)
                var zone = c.GetComponent<UtilityPlaneInteractable>();
                if (zone != null)
                {
                    if (!Usable(zone, eye)) continue;
                    point = h.point;
                    return zone;
                }
                if (c.isTrigger) continue;
                var cargo = c.GetComponentInParent<CargoItem>();
                if (cargo != null) { point = h.point; return cargo; }
                var grab = c.GetComponentInParent<Grabbable>();
                if (grab != null) { point = h.point; return grab; }
                var host = c.GetComponentInParent<InteractionHost>();
                if (host != null && host.EmbeddedOnRay(c, ray, h.distance, embeddedDepth, z => Usable(z, eye), out var inner))
                {
                    point = h.point;
                    return inner;
                }
                return null;                                             // a wall
            }
            return null;
        }

        static readonly System.Comparison<RaycastHit> HitOrderComparison = (a, b) => a.distance.CompareTo(b.distance);
        static readonly System.Collections.Generic.IComparer<RaycastHit> HitOrder = System.Collections.Generic.Comparer<RaycastHit>.Create(HitOrderComparison);

        Collider[] SeatColliders()
        {
            var seat = net != null && net.seatController != null ? net.seatController.Seat : null;
            return seat != null ? seat.EyeColliders : System.Array.Empty<Collider>();
        }

        bool Usable(UtilityPlaneInteractable zone, Vector3 eye)
        {
            if (zone.kind == "seat" && controller.Source.Context != InputContext.OnFoot) return false;   // get out first
            if (zone.kind == "kit" && controller.Source.Context != InputContext.OnFoot) return false;    // winter kit: from outside only (K4.2)
            var host = zone.GetComponentInParent<InteractionHost>();
            if (host != null && host.CursorOnly(zone) && controller.Source.Context != InputContext.SeatCursor) return false;   // Tab only
            return host != null && host.ZoneOpen(zone, Held) && host.SideAllows(zone.id, eye);
        }

        /// <summary>The key or button that uses the target in the current context (E / F / ЛКМ; ЛКМ on foot while a heavy
        /// thing takes both hands, F4.5).</summary>
        public string UseKey => Key();

        string Key()
        {
            InputContext ctx = controller.Source.Context;
            if (ctx == InputContext.OnFoot) return HeldHeavy ? "ЛКМ" : "E";
            return ctx == InputContext.SeatCursor ? "ЛКМ" : "F";
        }

        void OnGUI()
        {
            if (Application.isBatchMode) return;
            // aim point: where the use ray goes (not with the free cursor, which is its own pointer)
            if (controller != null && controller.Source != null && controller.Source.Context != InputContext.SeatCursor
                && controller.Source.Context != InputContext.None)
            {
                float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(new Rect(cx - 3f, cy - 3f, 6f, 6f), Texture2D.whiteTexture);
                GUI.color = TargetHost != null || TargetCargo != null ? new Color(1f, 0.85f, 0.3f) : Color.white;
                GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            // held action (winter kit): a progress bar under the aim point while the button is held
            bool lmbBar = _lmbSent && _lmbHold > 0f;
            if ((_pressSent && _pressHold > 0f) || lmbBar)
            {
                float k = lmbBar ? Mathf.Clamp01((Time.time - _lmbTime) / _lmbHold) : Mathf.Clamp01((Time.time - _pressTime) / _pressHold);
                var bar = new Rect(Screen.width * 0.5f - 100f, Screen.height * 0.55f, 200f, 8f);
                GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.85f, 0.3f); GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * k, bar.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            var rect = new Rect(0, Screen.height * 0.58f, Screen.width, 30);
            // the hover card (name, state, actions) is InteractionOverlay; here only server replies and short notes
            if (Time.time >= _messageUntil || string.IsNullOrEmpty(_message)) return;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), _message, style);
            GUI.color = new Color(1f, 0.8f, 0.4f);
            GUI.Label(rect, _message, style);
            GUI.color = Color.white;
        }
    }
}
