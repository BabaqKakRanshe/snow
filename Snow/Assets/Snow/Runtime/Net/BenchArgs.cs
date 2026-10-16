using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Snow.Net
{
    /// <summary>
    /// Launch options of the network bench. From the command line of a build; in the Editor from the file
    /// Temp/SnowBenchArgs.txt (one line, same syntax), so a tool can start the Editor as host without a UI.
    ///   -snow-host | -snow-client ADDRESS   role; without either the dev menu is shown
    ///   -snow-port N                        default 7777
    ///   -snow-sim-delay MS -snow-sim-jitter MS -snow-sim-loss PCT   Unity Transport simulator on this process
    ///   -snow-bot SCRIPT                    scripted input instead of keyboard (see BenchBot)
    ///   -snow-log PATH                      JSON lines log (BenchLog)
    ///   -snow-quit-after SECONDS            quit after the given time since start
    ///   -snow-trace                         owner logs its pose every physics step, the server logs remote poses every frame
    ///   -snow-wind KT,FROMDEG,GUSTKT,TURBMS  test wind on the server (EnvironmentProvider)
    /// </summary>
    public class BenchArgs
    {
        public bool Host, Client;
        public string Address = "127.0.0.1";
        public ushort Port = 7777;
        public int SimDelayMs, SimJitterMs, SimLossPercent;
        public string Bot, LogPath;
        public float QuitAfter;
        public bool Trace;
        public string Wind;

        public bool Simulate => SimDelayMs > 0 || SimJitterMs > 0 || SimLossPercent > 0;
        public bool Automatic => Host || Client;

        public const string EditorArgsFile = "Temp/SnowBenchArgs.txt";

        public static BenchArgs Read()
        {
            var list = new List<string>(Environment.GetCommandLineArgs());
#if UNITY_EDITOR
            if (File.Exists(EditorArgsFile))
                list.AddRange(File.ReadAllText(EditorArgsFile).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
#endif
            return Parse(list);
        }

        public static BenchArgs Parse(IList<string> a)
        {
            var r = new BenchArgs();
            for (int i = 0; i < a.Count; i++)
            {
                string next = i + 1 < a.Count ? a[i + 1] : null;
                switch (a[i])
                {
                    case "-snow-host": r.Host = true; break;
                    case "-snow-client": r.Client = true; if (next != null && !next.StartsWith("-")) { r.Address = next; i++; } break;
                    case "-snow-port": r.Port = ushort.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-snow-sim-delay": r.SimDelayMs = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-snow-sim-jitter": r.SimJitterMs = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-snow-sim-loss": r.SimLossPercent = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-snow-bot": r.Bot = next; i++; break;
                    case "-snow-log": r.LogPath = next; i++; break;
                    case "-snow-quit-after": r.QuitAfter = float.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-snow-trace": r.Trace = true; break;
                    case "-snow-wind": r.Wind = next; i++; break;
                }
            }
            if (r.Host) r.Client = false;
            return r;
        }

        public override string ToString() =>
            $"host={Host} client={Client} addr={Address}:{Port} sim=({SimDelayMs}ms,{SimJitterMs}ms,{SimLossPercent}%) bot={Bot} log={LogPath} quit={QuitAfter} trace={Trace} wind={Wind}";
    }
}
