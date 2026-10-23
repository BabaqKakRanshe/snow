#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ImGuiNET;
using Snow.Input;
using UImGui.Renderer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using ImGuiHost = UImGui.UImGui;
using Num = System.Numerics.Vector2;

namespace Snow.Dev
{
    /// <summary>
    /// Developer pult (~; editor and Development builds only, user decision 23.10: one tool instead of the F1 / F9 / F10
    /// menus). One Dear ImGui window (package com.psydack.uimgui): pages on the left, the chosen page on the right. Any
    /// system adds its own block to a page with <see cref="Add"/> (several blocks may share a page) and draws it with
    /// <see cref="DevUi"/>; a page can be pinned (★) and then stays on screen as a small window while the pult is closed
    /// (looking only: the cursor belongs to the game then). While the pult is open the game gets no input (SnowInput.UiOpen).
    ///
    /// Rendering: the game camera draws into the low-resolution PSX texture and a screen-space overlay canvas shows it, so
    /// a render-feature pass on a camera would end up under that canvas. UImGui gets a disabled screen-sized camera (only
    /// for the display size; it never renders, so the RenderImGui feature never runs) and its frame is drawn here in
    /// OnGUI, after every canvas, by the package's own mesh renderer (internal RenderDrawData, reached by reflection).
    /// </summary>
    public static class DevPult
    {
        class Block { public Action draw; public Func<bool> when; }
        class Page { public string name; public int order; public readonly List<Block> blocks = new List<Block>(); }

        static readonly List<Page> Pages = new List<Page>();

        /// <summary>The pult window is open (the game's input is held).</summary>
        public static bool IsOpen { get; private set; }
        /// <summary>The ImGui host started (false: the package failed, the old OnGUI fallbacks stay on).</summary>
        public static bool Running => DevPultHost.Instance != null && DevPultHost.Instance.Ready;
        internal static string Selected;

        /// <summary>Adds a draw block to a page (the page is made on the first block). <paramref name="when"/> hides the
        /// block while false. Keep the delegate: <see cref="Remove"/> needs the same instance.</summary>
        public static void Add(string page, int order, Action draw, Func<bool> when = null)
        {
            if (string.IsNullOrEmpty(page) || draw == null) return;
            Page p = Find(page);
            if (p == null)
            {
                p = new Page { name = page, order = order };
                Pages.Add(p);
                Pages.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : string.CompareOrdinal(a.name, b.name));
            }
            p.blocks.Add(new Block { draw = draw, when = when });
        }

        public static void Remove(string page, Action draw)
        {
            Page p = Find(page);
            if (p == null) return;
            p.blocks.RemoveAll(b => b.draw == draw);
            if (p.blocks.Count == 0) Pages.Remove(p);
        }

        /// <summary>Opens the pult on a page (a page that does not exist yet is chosen once it appears).</summary>
        public static void Show(string page)
        {
            if (!string.IsNullOrEmpty(page)) Selected = page;
            SetOpen(true);
        }

        public static void Hide() => SetOpen(false);

        internal static void SetOpen(bool open)
        {
            if (IsOpen == open || (open && !Running)) return;           // never hold the game's input without a window
            IsOpen = open;
            SnowInput.UiOpen = open;
            if (!open) Snow.Character.CameraSettings.Save();          // the sliders only keep their values while dragged
        }

        static Page Find(string name)
        {
            foreach (Page p in Pages) if (p.name == name) return p;
            return null;
        }

        internal static IReadOnlyList<string> VisiblePages(List<string> into)
        {
            into.Clear();
            foreach (Page p in Pages) if (Visible(p)) into.Add(p.name);
            return into;
        }

        static bool Visible(Page p)
        {
            foreach (Block b in p.blocks) if (Safe(b.when)) return true;
            return false;
        }

        static bool Safe(Func<bool> when)
        {
            if (when == null) return true;
            try { return when(); }
            catch (Exception e) { Debug.LogException(e); return false; }
        }

        /// <summary>Draws a page's visible blocks. An exception in one block is shown in its place: DevUi opens no scopes a
        /// block could leave unclosed, and an exception reaching UImGui would switch the whole pult off.</summary>
        internal static void DrawPage(string name)
        {
            Page p = Find(name);
            if (p == null) return;
            for (int i = 0; i < p.blocks.Count; i++)
            {
                Block b = p.blocks[i];
                if (!Safe(b.when)) continue;
                ImGui.PushID(i);
                try { b.draw(); }
                catch (Exception e)
                {
                    ImGui.TextColored(new System.Numerics.Vector4(1f, 0.5f, 0.4f, 1f), e.GetType().Name + ": " + e.Message);
                    Debug.LogException(e);
                }
                ImGui.PopID();
            }
        }
    }

    /// <summary>The pult's runtime object: made by itself on the first scene, survives scene loads.</summary>
    [DefaultExecutionOrder(-100)]
    public class DevPultHost : MonoBehaviour
    {
        const string PinsKey = "snow.devpult.pins";
        const float FontPx = 17f;
        static readonly string[] FontFiles = { "arial.ttf", "segoeui.ttf", "tahoma.ttf" };

        public static DevPultHost Instance { get; private set; }
        public bool Ready { get; private set; }

        ImGuiHost _ui;
        Action<CommandBuffer> _render;
        CommandBuffer _cb;
        ImFontPtr _font;
        bool _hasFont;
        readonly HashSet<string> _pins = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> _visible = new List<string>();
        readonly List<string> _pinList = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null || Application.isBatchMode) return;
            var go = new GameObject("DevPult");
            DontDestroyOnLoad(go);
            go.AddComponent<DevPultHost>();
        }

        void Awake()
        {
            Instance = this;
            foreach (string pin in PlayerPrefs.GetString(PinsKey, "").Split('\n'))
                if (pin.Length > 0) _pins.Add(pin);

            // the display size comes from a camera: a disabled one with no target texture is screen-sized and renders nothing
            var camGo = new GameObject("DevPult_Size");
            camGo.transform.SetParent(transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;

            var uiGo = new GameObject("DevPult_ImGui");
            uiGo.SetActive(false);                                     // fields first: Awake/OnEnable read them
            uiGo.transform.SetParent(transform, false);
            _ui = uiGo.AddComponent<ImGuiHost>();
            FieldInfo camField = typeof(ImGuiHost).GetField("_camera", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo render = typeof(ImGuiHost).GetMethod("RenderDrawData", BindingFlags.Instance | BindingFlags.NonPublic);
            if (camField == null || render == null)
            {
                Debug.LogError("[Snow] DevPult: UImGui changed (no _camera / RenderDrawData); the pult is off.");
                return;
            }
            camField.SetValue(_ui, cam);
            _render = (Action<CommandBuffer>)Delegate.CreateDelegate(typeof(Action<CommandBuffer>), _ui, render);
            // the URP feature asset of the package; it is also in the renderers' feature lists (UImGui needs it there)
            var feature = Resources.Load<RenderImGui>("RenderImGui");
            if (feature != null) _ui.SetRenderFeature(feature);
            _ui.OnInitialize += OnInit;
            _ui.Layout += OnLayout;
            uiGo.SetActive(true);                                      // OnEnable logs and disables itself on failure
            Ready = _ui.enabled;
            if (!Ready) Debug.LogError("[Snow] DevPult: UImGui did not start; see the error above.");
            _cb = new CommandBuffer { name = "DevPult" };
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _cb?.Release();
        }

        void OnInit(ImGuiHost ui)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NoMouseCursorChange;   // the cursor's lock and visibility stay the game's
            _hasFont = false;
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (string f in FontFiles)
            {
                string path = Path.Combine(dir, f);
                if (!File.Exists(path)) continue;
                _font = io.Fonts.AddFontFromFileTTF(path, FontPx);     // 1.92 loads glyphs on demand: Cyrillic included
                _hasFont = true;
                break;
            }
            if (!_hasFont) Debug.LogWarning("[Snow] DevPult: no Arial / Segoe UI / Tahoma in the system fonts; Cyrillic shows as '?'.");
            ImGui.GetStyle().ScaleAllSizes(Mathf.Max(1f, Hud.S));
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (Ready && kb != null && kb.backquoteKey.wasPressedThisFrame) DevPult.SetOpen(!DevPult.IsOpen);
        }

        // ------------------------------------------------------------------ layout (inside UImGui's frame)
        void OnLayout(ImGuiHost ui)
        {
            if (_hasFont) ImGui.PushFont(_font, Hud.Px(FontPx));
            DevPult.VisiblePages(_visible);
            if (DevPult.IsOpen) DrawMain();
            _pinList.Clear();
            _pinList.AddRange(_pins);
            foreach (string page in _pinList)
                if (_visible.Contains(page)) DrawPinned(page);
            if (_hasFont) ImGui.PopFont();
        }

        void DrawMain()
        {
            float s = Hud.S;
            ImGui.SetNextWindowPos(new Num(16f * s, 16f * s), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Num(640f * s, 560f * s), ImGuiCond.FirstUseEver);
            bool open = true;
            if (ImGui.Begin(Loc.T("Дев-пульт (~)") + "###devpult", ref open, ImGuiWindowFlags.NoCollapse))
            {
                if (_visible.Count > 0 && (DevPult.Selected == null || !_visible.Contains(DevPult.Selected))) DevPult.Selected = _visible[0];
                ImGui.BeginChild("pages", new Num(150f * s, 0f), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX);
                foreach (string page in _visible)
                    if (ImGui.Selectable((_pins.Contains(page) ? "★ " : "   ") + Loc.T(page) + "###p_" + page, page == DevPult.Selected))
                        DevPult.Selected = page;
                ImGui.EndChild();
                ImGui.SameLine();
                ImGui.BeginChild("page", new Num(0f, 0f), ImGuiChildFlags.None);
                string sel = DevPult.Selected;
                if (sel != null)
                {
                    bool pinned = _pins.Contains(sel);
                    if (ImGui.Checkbox(Loc.T("★ на экране, когда пульт закрыт") + "###pin", ref pinned)) SetPin(sel, pinned);
                    ImGui.Separator();
                    DevPult.DrawPage(sel);
                }
                ImGui.EndChild();
            }
            ImGui.End();
            if (!open) DevPult.SetOpen(false);
        }

        void DrawPinned(string page)
        {
            var flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoCollapse;
            if (!DevPult.IsOpen) flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav;   // the mouse belongs to the game
            ImGui.SetNextWindowBgAlpha(DevPult.IsOpen ? 0.9f : 0.6f);
            ImGui.SetNextWindowPos(new Num(Screen.width * 0.6f, 16f * Hud.S), ImGuiCond.FirstUseEver);
            bool keep = true;
            if (ImGui.Begin("★ " + Loc.T(page) + "###pin_" + page, ref keep, flags)) DevPult.DrawPage(page);
            ImGui.End();
            if (!keep) SetPin(page, false);
        }

        void SetPin(string page, bool on)
        {
            if (on) _pins.Add(page);
            else _pins.Remove(page);
            PlayerPrefs.SetString(PinsKey, string.Join("\n", _pins));
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ drawing (after every canvas)
        void OnGUI()
        {
            if (!Ready || !_ui.enabled || Event.current.type != EventType.Repaint) return;
            GUI.depth = -10000;
            _cb.Clear();
            _render(_cb);
            GL.PushMatrix();
            Graphics.ExecuteCommandBuffer(_cb);
            GL.PopMatrix();
        }
    }
}
#endif
