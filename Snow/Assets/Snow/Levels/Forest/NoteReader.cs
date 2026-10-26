using System;
using System.Collections.Generic;
using System.Linq;
using Snow.Interaction;
using Snow.Net;
using Snow.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// Reading a note held in hand (FOREST_U3 F4.3): local IMGUI panel over the PS1 frame like the interaction card —
    /// title, author and date, the German text as written in the world and the Russian translation beside it, scrolled
    /// when long. Open with T (free on foot, docs/dev/Controls.md; the key shows in the pockets' "в руке" block) while the
    /// own player holds a readable note
    /// (InteractionSensor.Held: CargoItem.HeldBy knows hands only on the server); close with T, Esc or by letting the note
    /// go. Nothing goes over the network.
    /// </summary>
    public class NoteReader : MonoBehaviour
    {
        [Tooltip("Assets/Locations/Forest/Data/Forest_Notes.json")] public TextAsset notes;

        readonly Dictionary<string, NoteText> _notes = new Dictionary<string, NoteText>(StringComparer.Ordinal);
        NoteText _open;
        ReadableNote _openItem;
        Vector2 _scroll;

        void Awake()
        {
            var data = ForestData.Read<NotesData>(notes);
            if (data?.notes != null)
                foreach (NoteText n in data.notes)
                    if (n != null && !string.IsNullOrEmpty(n.id)) _notes[n.id] = n;
        }

        ReadableNote HeldNote()
        {
            PlayerNet me = PlayerNet.Local;
            var held = me != null && me.sensor != null ? me.sensor.Held : null;
            return held != null && held.TryGetComponent(out ReadableNote note) && !string.IsNullOrEmpty(note.NoteId) ? note : null;
        }

        void Update()
        {
            ReadableNote held = HeldNote();
            InteractionSensor.ReaderOpen = _open != null;              // before the keys: a closing T stays ours this frame
            Keyboard kb = Keyboard.current;
            if (Snow.Input.SnowInput.UiOpen) kb = null;                // the dev pult holds the keyboard
            if (_open != null && (held != _openItem || (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.tKey.wasPressedThisFrame))))
            {
                _open = null;
                Snow.Audio.SnowAudio.PlayUi("item.note.close");
                _openItem = null;
                return;
            }
            // a radio reply on offer has T first (Snow.Voice.VoiceHud): the note waits until it is gone
            bool radio = Snow.Voice.VoiceHud.ReplyOffered;
            if (_open == null && held != null && !radio && kb != null && kb.tKey.wasPressedThisFrame && _notes.TryGetValue(held.NoteId, out NoteText n))
            {
                _open = n;
                Snow.Audio.SnowAudio.PlayUi("item.note.open");
                _openItem = held;
                _scroll = Vector2.zero;
                InteractionSensor.ReaderOpen = true;
                BenchLog.Event("forest_note", "id", n.id);
            }
            if (_open == null && held != null && !radio) PocketsHud.HandHint("T", "читать");
        }

        static readonly (string key, string text)[] CloseKeys = { ("T", "закрыть"), ("Esc", "закрыть") };

        // the open note (UI pick 23.10, version 2, Hud look): a wide card with a warm edge, the title with the original
        // title, author and date beside it, the German text and the translation side by side, scrolled when long
        void OnGUI()
        {
            if (Application.isBatchMode || _open == null) return;
            bool paint = Event.current.type == EventType.Repaint;
            float w = Mathf.Min(Hud.Px(1400f), Screen.width - 2f * Hud.SafeX), h = Mathf.Min(Hud.Px(720f), Screen.height - 2f * Hud.SafeY);
            var panel = new Rect(Mathf.Round((Screen.width - w) * 0.5f), Mathf.Round((Screen.height - h) * 0.5f), w, h);
            float padX = Hud.Px(36f), padY = Hud.Px(30f), edge = Hud.Px(3f);
            float ix = panel.x + edge + padX, iw = w - edge - 2f * padX, y = panel.y + padY;

            string title = _open.title ?? "", original = null;
            int slash = title.IndexOf(" / ", StringComparison.Ordinal);
            if (slash >= 0) { original = title.Substring(0, slash); title = title.Substring(slash + 3); }
            string meta = string.Join(" · ", new[] { original, Loc.T(_open.author), Loc.T(_open.date) }.Where(p => !string.IsNullOrEmpty(p)));
            GUIStyle big = Hud.Text(28, true), small = Hud.Text(18);
            float headH = Hud.Px(38f);
            if (paint)
            {
                Hud.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.45f));
                Hud.Panel(panel, 1f, true);
                string t = Loc.T(title);
                float tw = Hud.Size(t, big).x;
                Hud.Label(new Rect(ix, y, tw + 2f, headH), t, big, Color.white);
                Hud.Label(new Rect(ix + tw + Hud.Px(16f), y + Hud.Px(3f), iw - tw - Hud.Px(16f), headH), meta, small, Hud.Muted);
                Hud.Divider(ix, y + headH + Hud.Px(12f), iw);
            }
            y += headH + Hud.Px(12f) + Hud.Px(20f);
            float footH = Hud.Px(28f), bottom = panel.yMax - padY - footH - Hud.Px(14f);

            GUILayout.BeginArea(new Rect(ix, y, iw, bottom - y));
            _scroll = GUILayout.BeginScrollView(_scroll, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginHorizontal();
            float colW = (iw - Hud.Px(36f) - Hud.Px(20f)) * 0.5f;          // the gap and room for the scroll bar
            Column(Loc.T("оригинал"), _open.de, Hud.Paper, colW);
            GUILayout.Space(Hud.Px(36f));
            Column(Loc.T("перевод"), Loc.T(_open.ru), Hud.Translation, colW);   // English mode: the English translation
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (paint)
            {
                float kw = Hud.Hints(0f, 0f, footH, CloseKeys, 17, 1f, false);
                Hud.Hints(ix + iw - kw, panel.yMax - padY - footH, footH, CloseKeys, 17, 1f);
            }
        }

        static void Column(string caption, string text, Color color, float width)
        {
            GUILayout.BeginVertical(GUILayout.Width(width));
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUILayout.Label(caption.ToUpperInvariant(), Hud.Text(13));
            GUILayout.Space(Hud.Px(8f));
            GUI.color = color;
            GUILayout.Label(text, Hud.Text(20, false, TextAnchor.UpperLeft, true));
            GUI.color = Color.white;
            GUILayout.EndVertical();
        }
    }
}
