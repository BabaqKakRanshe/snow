using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Audio
{
    /// <summary>Server one-shots to the clients (an event, not state): cue id, point, volume.</summary>
    static class AudioNet
    {
        const string MsgPlay = "snow_sfx";
        static NetworkManager s_nm;

        internal static void Attach(NetworkManager nm)
        {
            if (s_nm == nm) return;
            Detach();
            s_nm = nm;
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgPlay, OnPlay);
        }

        internal static void Detach()
        {
            if (s_nm != null && s_nm.CustomMessagingManager != null) s_nm.CustomMessagingManager.UnregisterNamedMessageHandler(MsgPlay);
            s_nm = null;
        }

        /// <summary>Server: every client but this peer plays it (the host plays it itself).</summary>
        internal static void SendToOthers(string id, Vector3 at, float volume)
        {
            if (s_nm == null || !s_nm.IsServer || !s_nm.IsListening) return;
            using var w = new FastBufferWriter(96, Allocator.Temp);
            w.WriteValueSafe(id, true);
            w.WriteValueSafe(at);
            w.WriteValueSafe((byte)Mathf.Clamp(Mathf.RoundToInt(volume * 100f), 0, 255));
            foreach (ulong c in s_nm.ConnectedClientsIds)
                if (c != s_nm.LocalClientId) s_nm.CustomMessagingManager.SendNamedMessage(MsgPlay, c, w);
        }

        static void OnPlay(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out string id, true);
            r.ReadValueSafe(out Vector3 at);
            r.ReadValueSafe(out byte vol);
            SnowAudio.PlayId(id, at, vol / 100f, "сеть");
        }
    }
}
