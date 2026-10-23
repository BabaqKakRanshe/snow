using System;
using System.Collections.Generic;
using Snow.Net;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Voice
{
    /// <summary>
    /// The voice on the network, by NGO named messages so no scene or prefab carries it (every level has its own
    /// NetworkManager; <see cref="VoiceSystem"/> registers the handlers on whichever is running). The server decides what
    /// is said and sends line indices of <see cref="VoiceTable.Main"/>; every peer plays them and shows the subtitle
    /// (research §9.5). Prompts (a reply by choice, QUESTIONS_VOICE 12) are state: a late joiner gets the open ones.
    /// Clients send only their choice and their body sounds; the server checks reach and cooldowns.
    /// </summary>
    public static class VoiceNet
    {
        const string MsgSay = "snow.voice.say", MsgStop = "snow.voice.stop", MsgPrompt = "snow.voice.prompt";
        const string MsgChoose = "snow.voice.choose", MsgBody = "snow.voice.body", MsgBodyPlay = "snow.voice.bodyplay";

        public enum Channel : byte { Radio, Air }

        /// <summary>An open question on a radio: who stands within <see cref="Radius"/> of it may answer.</summary>
        public sealed class Prompt
        {
            public uint Id;
            public ulong Anchor;
            public Vector3 Point;
            public float Radius;
            public string[] Options = Array.Empty<string>();
        }

        /// <summary>Server: a client chose option of prompt (validated: open prompt, in reach, first answer wins).</summary>
        public static event Action<ulong, uint, int> Chosen;

        /// <summary>Client: the open prompts as the server has them.</summary>
        public static readonly Dictionary<uint, Prompt> ClientPrompts = new Dictionary<uint, Prompt>();

        static NetworkManager s_nm;
        static readonly Dictionary<uint, Prompt> s_serverPrompts = new Dictionary<uint, Prompt>();
        static readonly Dictionary<(ulong, BodySound), float> s_bodyAt = new Dictionary<(ulong, BodySound), float>();
        static uint s_nextPrompt = 1;

        // ---------------------------------------------------------------- lifecycle (VoiceSystem)
        internal static void Attach(NetworkManager nm)
        {
            if (s_nm == nm) return;
            Detach();
            s_nm = nm;
            CustomMessagingManager m = nm.CustomMessagingManager;
            m.RegisterNamedMessageHandler(MsgSay, OnSay);
            m.RegisterNamedMessageHandler(MsgStop, OnStop);
            m.RegisterNamedMessageHandler(MsgPrompt, OnPrompt);
            m.RegisterNamedMessageHandler(MsgBodyPlay, OnBodyPlay);
            if (nm.IsServer)
            {
                m.RegisterNamedMessageHandler(MsgChoose, OnChoose);
                m.RegisterNamedMessageHandler(MsgBody, OnBody);
                nm.OnClientConnectedCallback += SendOpenPrompts;
            }
        }

        internal static void Detach()
        {
            if (s_nm != null)
            {
                s_nm.OnClientConnectedCallback -= SendOpenPrompts;
                CustomMessagingManager m = s_nm.CustomMessagingManager;
                if (m != null)
                    foreach (string n in new[] { MsgSay, MsgStop, MsgPrompt, MsgBodyPlay, MsgChoose, MsgBody })
                        m.UnregisterNamedMessageHandler(n);
            }
            s_nm = null;
            s_serverPrompts.Clear();
            s_bodyAt.Clear();
            ClientPrompts.Clear();
        }

        static bool Server => s_nm != null && s_nm.IsServer && s_nm.IsListening;
        static bool Client => s_nm != null && s_nm.IsClient && s_nm.IsListening;

        // ---------------------------------------------------------------- server API
        /// <summary>Server: everyone plays these lines one after another at the anchor (a spawned object; 0 = the point).</summary>
        public static void Say(ulong anchor, Vector3 point, Channel channel, int priority, IReadOnlyList<VoiceLine> lines)
        {
            if (!Server || lines == null || lines.Count == 0) return;
            using var w = new FastBufferWriter(64 + lines.Count * 2, Allocator.Temp);
            w.WriteValueSafe(anchor);
            w.WriteValueSafe(point);
            w.WriteValueSafe((byte)channel);
            w.WriteValueSafe((byte)Mathf.Clamp(priority, 0, 255));
            w.WriteValueSafe((byte)Mathf.Min(lines.Count, 255));
            for (int i = 0; i < lines.Count && i < 255; i++) w.WriteValueSafe((ushort)lines[i].Index);
            s_nm.CustomMessagingManager.SendNamedMessageToAll(MsgSay, w);
        }

        /// <summary>Server: cut what the anchor is saying (a click and the squelch tail, research §5: cut, don't fade).</summary>
        public static void Stop(ulong anchor, Vector3 point)
        {
            if (!Server) return;
            using var w = new FastBufferWriter(32, Allocator.Temp);
            w.WriteValueSafe(anchor);
            w.WriteValueSafe(point);
            s_nm.CustomMessagingManager.SendNamedMessageToAll(MsgStop, w);
        }

        /// <summary>Server: open a question at the anchor; options are Russian game text (each peer translates).</summary>
        public static uint OpenPrompt(ulong anchor, Vector3 point, float radius, string[] options)
        {
            if (!Server) return 0;
            var p = new Prompt { Id = s_nextPrompt++, Anchor = anchor, Point = point, Radius = radius, Options = options ?? Array.Empty<string>() };
            s_serverPrompts[p.Id] = p;
            SendPrompt(p, false, s_nm.ConnectedClientsIds);
            BenchLog.Event("voice_prompt", "id", p.Id, "options", string.Join(" | ", p.Options));
            return p.Id;
        }

        public static void ClosePrompt(uint id)
        {
            if (!Server || !s_serverPrompts.TryGetValue(id, out Prompt p)) return;
            s_serverPrompts.Remove(id);
            SendPrompt(p, true, s_nm.ConnectedClientsIds);
        }

        public static bool PromptOpen(uint id) => s_serverPrompts.ContainsKey(id);

        static void SendOpenPrompts(ulong client)
        {
            if (!Server) return;
            foreach (Prompt p in s_serverPrompts.Values) SendPrompt(p, false, new[] { client });
        }

        static void SendPrompt(Prompt p, bool close, IReadOnlyList<ulong> to)
        {
            if (to == null || to.Count == 0) return;
            using var w = new FastBufferWriter(128, Allocator.Temp, 8192);
            w.WriteValueSafe(p.Id);
            w.WriteValueSafe(close);
            w.WriteValueSafe(p.Anchor);
            w.WriteValueSafe(p.Point);
            w.WriteValueSafe(p.Radius);
            w.WriteValueSafe((byte)Mathf.Min(p.Options.Length, 9));
            for (int i = 0; i < p.Options.Length && i < 9; i++) w.WriteValueSafe(p.Options[i] ?? "");
            s_nm.CustomMessagingManager.SendNamedMessage(MsgPrompt, to, w);
        }

        /// <summary>Where an anchor is on the server (its object, or the point when it has none or is gone).</summary>
        public static Vector3 AnchorPoint(ulong anchor, Vector3 point)
        {
            if (anchor != 0 && s_nm != null && s_nm.SpawnManager != null && s_nm.SpawnManager.SpawnedObjects.TryGetValue(anchor, out NetworkObject no) && no != null)
                return no.transform.position;
            return point;
        }

        // ---------------------------------------------------------------- client API
        public static void Choose(uint prompt, int option)
        {
            if (!Client) return;
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(prompt);
            w.WriteValueSafe((byte)option);
            s_nm.CustomMessagingManager.SendNamedMessage(MsgChoose, NetworkManager.ServerClientId, w);
        }

        public static void Body(BodySound sound, Vector3 point)
        {
            if (!Client) return;
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe((byte)sound);
            w.WriteValueSafe(point);
            s_nm.CustomMessagingManager.SendNamedMessage(MsgBody, NetworkManager.ServerClientId, w);
        }

        // ---------------------------------------------------------------- handlers
        static void OnSay(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out ulong anchor);
            r.ReadValueSafe(out Vector3 point);
            r.ReadValueSafe(out byte channel);
            r.ReadValueSafe(out byte priority);
            r.ReadValueSafe(out byte n);
            var lines = new List<VoiceLine>(n);
            for (int i = 0; i < n; i++)
            {
                r.ReadValueSafe(out ushort index);
                VoiceLine l = VoiceTable.Main[index];
                if (l != null) lines.Add(l);
            }
            VoiceEmitter.For(anchor, point)?.Play(lines, (Channel)channel, priority);
        }

        static void OnStop(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out ulong anchor);
            r.ReadValueSafe(out Vector3 point);
            VoiceEmitter.For(anchor, point)?.Cut();
        }

        static void OnPrompt(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out uint id);
            r.ReadValueSafe(out bool close);
            var p = new Prompt { Id = id };
            r.ReadValueSafe(out p.Anchor);
            r.ReadValueSafe(out p.Point);
            r.ReadValueSafe(out p.Radius);
            r.ReadValueSafe(out byte n);
            p.Options = new string[n];
            for (int i = 0; i < n; i++) r.ReadValueSafe(out p.Options[i]);
            if (close) ClientPrompts.Remove(id);
            else ClientPrompts[id] = p;
        }

        static void OnChoose(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out uint id);
            r.ReadValueSafe(out byte option);
            if (!s_serverPrompts.TryGetValue(id, out Prompt p) || option >= p.Options.Length) return;
            NetworkObject player = s_nm.SpawnManager.GetPlayerNetworkObject(sender);
            Vector3 at = AnchorPoint(p.Anchor, p.Point);
            if (player == null || Vector3.Distance(player.transform.position + Vector3.up * 1f, at) > p.Radius + 1f)
            {
                BenchLog.Event("voice_choose_refused", "id", id, "client", sender);
                return;
            }
            s_serverPrompts.Remove(id);
            SendPrompt(p, true, s_nm.ConnectedClientsIds);
            BenchLog.Event("voice_choose", "id", id, "client", sender, "option", option, "text", p.Options[option]);
            Chosen?.Invoke(sender, id, option);
        }

        static void OnBody(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out byte kind);
            r.ReadValueSafe(out Vector3 point);
            var sound = (BodySound)kind;
            if (!Enum.IsDefined(typeof(BodySound), sound)) return;
            NetworkObject player = s_nm.SpawnManager.GetPlayerNetworkObject(sender);
            if (player == null) return;
            float now = Time.time;
            if (s_bodyAt.TryGetValue((sender, sound), out float at) && now - at < BodySounds.Cooldown(sound) * 0.8f) return;
            s_bodyAt[(sender, sound)] = now;

            Vector3 from = player.transform.position + Vector3.up * 1.5f;
            if (sound == BodySound.Ping)
            {
                if ((point - from).sqrMagnitude > 80f * 80f) point = from + (point - from).normalized * 80f;
            }
            else point = from;
            using (var w = new FastBufferWriter(32, Allocator.Temp))
            {
                w.WriteValueSafe(sender);
                w.WriteValueSafe(kind);
                w.WriteValueSafe((byte)UnityEngine.Random.Range(0, 8));
                w.WriteValueSafe(point);
                s_nm.CustomMessagingManager.SendNamedMessageToAll(MsgBodyPlay, w);
            }
            float radius = BodySounds.Radius(sound);
            if (radius > 0f) NoiseEvents.Emit(from, radius, "body_" + sound.ToString().ToLowerInvariant(), sender);
        }

        static void OnBodyPlay(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out ulong client);
            r.ReadValueSafe(out byte kind);
            r.ReadValueSafe(out byte variant);
            r.ReadValueSafe(out Vector3 point);
            bool mine = s_nm != null && client == s_nm.LocalClientId;
            VoiceHud.BodyHeard(client, (BodySound)kind, variant, point, mine);
        }
    }
}
