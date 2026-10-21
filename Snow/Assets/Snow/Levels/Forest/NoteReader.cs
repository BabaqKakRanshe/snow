using System;
using System.Collections.Generic;
using Snow.Net;
using Snow.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// Reading a note held in hand (FOREST_U3 F4.3): local IMGUI panel over the PS1 frame like the interaction card —
    /// title, author and date, the German text as written in the world, the Russian translation under it, scrolled when
    /// long. Open with T (free on foot, docs/dev/Controls.md) while the own player holds a readable note
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
        GUIStyle _title, _body, _hint;

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
            Keyboard kb = Keyboard.current;
            if (_open != null && (held != _openItem || (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.tKey.wasPressedThisFrame))))
            {
                _open = null;
                _openItem = null;
                return;
            }
            if (_open == null && held != null && kb != null && kb.tKey.wasPressedThisFrame && _notes.TryGetValue(held.NoteId, out NoteText n))
            {
                _open = n;
                _openItem = held;
                _scroll = Vector2.zero;
                BenchLog.Event("forest_note", "id", n.id);
            }
        }

        void OnGUI()
        {
            if (Application.isBatchMode) return;
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = true };
                _body = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
                _hint = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            }
            if (_open == null)
            {
                if (HeldNote() == null) return;
                GUI.color = new Color(1f, 0.85f, 0.5f);
                GUI.Label(new Rect(0f, Screen.height * 0.64f, Screen.width, 26f), "[T] читать", _hint);
                GUI.color = Color.white;
                return;
            }
            float w = Mathf.Min(760f, Screen.width - 80f), h = Screen.height * 0.78f;
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = new Color(0.95f, 0.92f, 0.82f);
            GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 18f, panel.width - 48f, panel.height - 36f));
            GUILayout.Label(_open.title, _title);
            GUILayout.Label($"{_open.author}   {_open.date}", _body);
            GUILayout.Space(10f);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label(_open.de, _body);
            GUILayout.Space(14f);
            GUI.color = new Color(0.75f, 0.8f, 0.85f);
            GUILayout.Label(_open.ru, _body);
            GUILayout.EndScrollView();
            GUI.color = new Color(1f, 0.85f, 0.5f);
            GUILayout.Label("[T] / [Esc] закрыть", _hint);
            GUILayout.EndArea();
            GUI.color = Color.white;
        }
    }
}
