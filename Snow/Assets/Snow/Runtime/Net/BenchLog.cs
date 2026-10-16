using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Snow.Net
{
    /// <summary>
    /// Evidence log of the network bench: one JSON object per line. Fields: t (realtime s), role, cid, ev, then
    /// event-specific key/value pairs. Written only when -snow-log is given; also mirrored to the Unity log.
    /// </summary>
    public static class BenchLog
    {
        static StreamWriter _writer;
        public static string Role = "none";
        public static ulong ClientId;

        public static void Open(string path)
        {
            Close();
            if (string.IsNullOrEmpty(path)) return;
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
        }

        public static void Close()
        {
            _writer?.Dispose();
            _writer = null;
        }

        /// <summary>Write an event. kv: alternating keys and values; numbers, bools, strings and Vector3 are supported.</summary>
        public static void Event(string ev, params object[] kv)
        {
            var sb = new StringBuilder(128);
            sb.Append("{\"t\":").Append(Num(Time.realtimeSinceStartup)).Append(",\"role\":\"").Append(Role)
              .Append("\",\"cid\":").Append(ClientId).Append(",\"ev\":\"").Append(ev).Append('"');
            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                sb.Append(",\"").Append(kv[i]).Append("\":");
                Value(sb, kv[i + 1]);
            }
            sb.Append('}');
            string line = sb.ToString();
            _writer?.WriteLine(line);
            Debug.Log("[Bench] " + line);
        }

        static void Value(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case float f: sb.Append(Num(f)); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case int or uint or long or ulong or ushort or byte: sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); break;
                case Vector3 p: sb.Append('[').Append(Num(p.x)).Append(',').Append(Num(p.y)).Append(',').Append(Num(p.z)).Append(']'); break;
                default: sb.Append('"').Append(v.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"'); break;
            }
        }

        static string Num(float f) => f.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
