using System.Collections.Generic;

namespace Snow.Vehicles.Van
{
    public enum ControlMode : byte
    {
        /// <summary>Two positions, a use flips them.</summary>
        Toggle,
        /// <summary>Positions in a ring: a use goes to the next one (engine cover, fuel tank).</summary>
        Cycle,
        /// <summary>Positions in a row: a use (click, wheel up) goes up, wheel down goes back (fan, radio volume).</summary>
        Steps,
        /// <summary>Pressed while held (horn).</summary>
        Momentary,
        /// <summary>Ignition key: 0 → I → II by clicks, from II a held click is III (starter), released it springs back.</summary>
        Ignition,
    }

    /// <summary>One row of the van's control table: a zone, its positions and what each looks like and says.</summary>
    public sealed class VanControl
    {
        public string Zone;
        public ControlMode Mode;
        /// <summary>Rig parts this control moves; Values[position][part] in contract units.</summary>
        public string[] Parts = new string[0];
        public float[][] Values;
        /// <summary>Hover hint per position (the state) and the action a use performs from it.</summary>
        public string[] States, Actions;
        /// <summary>From this position on the control counts as open (lids, covers, the folded seat over the battery box).</summary>
        public int OpenFrom = -1;
        public int Count => States.Length;
        public bool IsOpen(int position) => OpenFrom >= 0 && position >= OpenFrom;
    }

    /// <summary>
    /// The van's zone table (VAN3_INTEGRATION_UNITY.md §6, cut to the user's 68 zones, docs/dev/Van/ZonesReview.md):
    /// every zone that changes a state of the van. Rows are in a fixed order — the index is the slot in the replicated
    /// position list (VanVehicle). Zones of items (fuses, battery, jerry cans, wheels, jack, hook) come with the item model
    /// (stages В4 / В6) and are not rows here; seats and doors have their own systems.
    /// </summary>
    public static class VanControls
    {
        public static readonly List<VanControl> Table = Build();
        static readonly Dictionary<string, int> Index = BuildIndex();

        public static int IndexOf(string zone) => zone != null && Index.TryGetValue(zone, out int i) ? i : -1;

        /// <summary>The zone whose control owns this rig part (a zone's coveredBy names a part), or -1.</summary>
        public static int IndexOfPart(string part)
        {
            for (int i = 0; i < Table.Count; i++)
                foreach (string p in Table[i].Parts)
                    if (p == part) return i;
            return -1;
        }

        /// <summary>
        /// The next position of a use. Press: up / next / on (Ignition: from II to III); Back: one down (wheel down on a
        /// knob); Release: Ignition from III back to II, Momentary off. Returns false if nothing changes.
        /// </summary>
        public static bool Next(VanControl c, int position, Interaction.UsePhase phase, out int next)
        {
            next = position;
            switch (c.Mode)
            {
                case ControlMode.Toggle:
                    if (phase == Interaction.UsePhase.Press || phase == Interaction.UsePhase.Back) next = position == 0 ? 1 : 0;
                    break;
                case ControlMode.Cycle:
                    if (phase == Interaction.UsePhase.Press) next = (position + 1) % c.Count;
                    else if (phase == Interaction.UsePhase.Back) next = (position + c.Count - 1) % c.Count;
                    break;
                case ControlMode.Steps:
                    if (phase == Interaction.UsePhase.Press) next = position + 1 < c.Count ? position + 1 : 0;
                    else if (phase == Interaction.UsePhase.Back) next = position > 0 ? position - 1 : 0;
                    break;
                case ControlMode.Momentary:
                    if (phase == Interaction.UsePhase.Press) next = 1;
                    else if (phase == Interaction.UsePhase.Release) next = 0;
                    break;
                case ControlMode.Ignition:
                    if (phase == Interaction.UsePhase.Press) next = position < 3 ? position + 1 : 3;
                    else if (phase == Interaction.UsePhase.Back) next = position == 3 ? 2 : (position > 0 ? position - 1 : 0);
                    else if (phase == Interaction.UsePhase.Release && position == 3) next = 2;
                    break;
            }
            return next != position;
        }

        static Dictionary<string, int> BuildIndex()
        {
            var d = new Dictionary<string, int>(System.StringComparer.Ordinal);
            for (int i = 0; i < Table.Count; i++) d[Table[i].Zone] = i;
            return d;
        }

        static float[][] V(params float[][] rows) => rows;
        static float[] P(params float[] values) => values;

        static VanControl Lid(string zone, float open, string closedText = "закрыта", string openText = "открыта") => new VanControl
        {
            Zone = zone, Mode = ControlMode.Toggle, Parts = new[] { zone }, Values = V(P(0f), P(open)),
            States = new[] { closedText, openText }, Actions = new[] { "открыть", "закрыть" }, OpenFrom = 1,
        };

        static VanControl Switch(string zone, string part, float off, float on, string offText, string onText, string toOn, string toOff) => new VanControl
        {
            Zone = zone, Mode = ControlMode.Toggle, Parts = new[] { part }, Values = V(P(off), P(on)),
            States = new[] { offText, onText }, Actions = new[] { toOn, toOff },
        };

        static List<VanControl> Build()
        {
            var t = new List<VanControl>
            {
                // lids and covers (what they hide is closed for the ray while they are shut, §3.3)
                Lid("Glovebox_Lid", 80f),
                Lid("FuseBox_Lid", 100f),
                Lid("ToolBox_Lid", 88f),
                Lid("BatteryBox_Lid", 65f),
                new VanControl
                {
                    Zone = "EngineCover", Mode = ControlMode.Cycle, Parts = new[] { "EngineCover" }, Values = V(P(0f), P(0f), P(85f)),
                    States = new[] { "на защёлках", "защёлки открыты", "поднят" },
                    Actions = new[] { "открыть защёлки", "поднять", "опустить и защёлкнуть" }, OpenFrom = 2,
                },
                // fuel tanks: flap, cap and filler as one zone (user decision 21.10); the cap is off in position 2
                Tank("FuelFlap_L", "FuelCap_L"),
                Tank("FuelFlap_R", "FuelCap_R"),
                // seats and curtains
                new VanControl
                {
                    Zone = "Seat_L4", Mode = ControlMode.Toggle, Parts = new[] { "Seat_L4", "Seat_L4_Leg" }, Values = V(P(0f, 0f), P(90f, 90f)),
                    States = new[] { "разложено", "сложено (под ним ящик АКБ)" }, Actions = new[] { "сложить", "разложить" }, OpenFrom = 1,
                },
                new VanControl
                {
                    Zone = "Curtain_L1A", Mode = ControlMode.Toggle, Values = V(new float[0], new float[0]),
                    States = new[] { "открыты", "задёрнуты" }, Actions = new[] { "задёрнуть все", "открыть все" },
                },
                // cab: keys, hand brake, transfer, horn, heater fan, radio (Tab cursor only: user decision 21.10)
                new VanControl
                {
                    Zone = "Ignition_Key", Mode = ControlMode.Ignition, Parts = new[] { "Ignition_Key" }, Values = V(P(0f), P(30f), P(60f), P(90f)),
                    States = new[] { "0 — выключено", "I", "II — зажигание", "III — стартер" },
                    Actions = new[] { "повернуть в I", "повернуть в II (колесо вниз — назад)", "держать — стартер; колесо вниз — выключить", "" },
                },
                Switch("MassSwitch_Key", "MassSwitch_Key", 0f, 90f, "масса выключена", "масса включена", "включить массу", "выключить массу"),
                Switch("Handbrake", "Handbrake", 0f, 0.12f, "отпущен", "затянут", "затянуть", "отпустить"),
                Switch("TransferLever_Axle", "TransferLever_Axle", -15f, 15f, "передний мост выключен", "передний мост включён",
                       "включить передний мост", "выключить передний мост"),
                Switch("TransferLever_Range", "TransferLever_Range", -15f, 15f, "ряд H (повышенный)", "ряд L (пониженный)",
                       "включить L", "включить H"),
                new VanControl
                {
                    Zone = "Horn_Button", Mode = ControlMode.Momentary, Parts = new[] { "Horn_Button" }, Values = V(P(0f), P(0.005f)),
                    States = new[] { "", "звучит" }, Actions = new[] { "сигнал (держать)", "" },
                },
                new VanControl
                {
                    Zone = "Heater_Fan", Mode = ControlMode.Steps, Parts = new[] { "Heater_Fan" },
                    Values = V(P(0f), P(0.0169f), P(0.0338f), P(0.0507f)),
                    States = new[] { "вентилятор 0", "вентилятор 1", "вентилятор 2", "вентилятор 3" },
                    Actions = new[] { "включить 1", "включить 2", "включить 3", "выключить" },
                },
                Radio(),
                // under the battery box lid, under the engine cover
                new VanControl
                {
                    Zone = "Battery_ClampPos", Mode = ControlMode.Toggle, Parts = new[] { "Battery_ClampPos", "Battery_ClampNeg" },
                    Values = V(P(0f, 0f), P(0.04f, 0.04f)), States = new[] { "надеты", "сняты" }, Actions = new[] { "снять клеммы", "надеть клеммы" },
                },
                new VanControl
                {
                    Zone = "Engine_Lead_1", Mode = ControlMode.Toggle, Parts = new[] { "Engine_Lead_1", "Engine_Lead_2", "Engine_Lead_3", "Engine_Lead_4" },
                    Values = V(P(0f, 0f, 0f, 0f), P(0.04f, 0.04f, 0.04f, 0.04f)), States = new[] { "на свечах", "сняты" },
                    Actions = new[] { "снять провода", "надеть провода" },
                },
                Switch("Winch_Clutch", "Winch_Clutch", 0f, 60f, "муфта включена", "свободный ход (трос разматывается)", "выключить муфту", "включить муфту"),
            };
            foreach (string toggle in new[] { "Head", "Fog", "Spot", "Roof", "Work", "Cabin", "Stove", "Wiper" })
                t.Add(Switch("Toggle_" + toggle, "Toggle_" + toggle, 0f, 57.6f, "выключен", "включён", "включить", "выключить"));
            return t;
        }

        static VanControl Tank(string zone, string cap) => new VanControl
        {
            Zone = zone, Mode = ControlMode.Cycle, Parts = new[] { zone, cap },
            Values = V(P(0f, 0f), P(110f, 0f), P(110f, 360f), P(110f, 0f)),
            States = new[] { "закрыт", "лючок открыт", "крышка снята — можно лить", "лючок открыт" },
            Actions = new[] { "открыть лючок", "открутить крышку", "закрутить крышку", "закрыть лючок" }, OpenFrom = 1,
        };

        static VanControl Radio()
        {
            const int steps = 10;
            var values = new float[steps][];
            var states = new string[steps];
            var actions = new string[steps];
            for (int i = 0; i < steps; i++)
            {
                values[i] = P(270f * i / (steps - 1));
                states[i] = i == 0 ? "выключена" : $"громкость {i}";
                actions[i] = i == 0 ? "включить (колесо — громче / тише)" : i == steps - 1 ? "выключить" : "громче (колесо — тише)";
            }
            return new VanControl { Zone = "Radio_Volume", Mode = ControlMode.Steps, Parts = new[] { "Radio_Volume" }, Values = values, States = states, Actions = actions };
        }
    }
}
