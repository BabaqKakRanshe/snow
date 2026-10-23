#if UNITY_EDITOR || DEVELOPMENT_BUILD
using ImGuiNET;
using Unity.Netcode;

namespace Snow.Dev
{
    /// <summary>
    /// Widgets for pult pages (<see cref="DevPult.Add"/>): pages call these instead of ImGuiNET, so they need no package
    /// reference and every label goes through Loc.T. None of them leaves a scope open (an exception in a page cannot
    /// unbalance the window). Two equal labels on one page need a suffix "##id" (shown text stops before it).
    /// </summary>
    public static class DevUi
    {
        /// <summary>The local peer runs the server (host-only buttons work here).</summary>
        public static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        public static void Header(string text) => ImGui.SeparatorText(L(text));

        public static void Text(string text) => ImGui.TextUnformatted(L(text));

        /// <summary>Text without localisation (numbers, telemetry, ids).</summary>
        public static void Raw(string text) => ImGui.TextUnformatted(text);

        public static void Note(string text) => ImGui.TextDisabled(L(text));

        /// <summary>The next widget on the same line.</summary>
        public static void Row() => ImGui.SameLine();

        /// <summary>False on clients, with a note on the page: host-only actions (user decision 23.10).</summary>
        public static bool HostOnly()
        {
            if (IsHost) return true;
            Note("меняет хост");
            return false;
        }

        public static bool Button(string label, bool enabled = true)
        {
            if (!enabled) ImGui.BeginDisabled();
            bool hit = ImGui.Button(L(label));
            if (!enabled) ImGui.EndDisabled();
            return hit && enabled;
        }

        public static bool Checkbox(string label, ref bool value, bool enabled = true)
        {
            if (!enabled) ImGui.BeginDisabled();
            bool changed = ImGui.Checkbox(L(label), ref value);
            if (!enabled) ImGui.EndDisabled();
            return changed && enabled;
        }

        public static bool Slider(string label, ref float value, float min, float max, string format = "%.2f", bool enabled = true)
        {
            if (!enabled) ImGui.BeginDisabled();
            bool changed = ImGui.SliderFloat(L(label), ref value, min, max, format);
            if (!enabled) ImGui.EndDisabled();
            return changed && enabled;
        }

        /// <summary>A text field. Typing reaches the game's own key reads (Q, G, digits...): only for pages used without a
        /// player (the network page).</summary>
        public static bool Input(string label, ref string value, uint maxLength = 64)
        {
            value ??= "";
            return ImGui.InputText(L(label), ref value, maxLength);
        }

        static string L(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int id = text.IndexOf("##", System.StringComparison.Ordinal);
            return id < 0 ? Loc.T(text) : Loc.T(text.Substring(0, id)) + text.Substring(id);
        }
    }
}
#endif
