using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Category = Snow.Levels.SkyIslandLocationHost.Category;
using Rule = Snow.EditorTools.LocationProfile.Rule;

namespace Snow.EditorTools
{
    /// <summary>
    /// The core locations as levels (LOCATIONS_UPDATE_UNITY.md appendix A; BaseV2: BASEV2_INTEGRATION_UNITY.md appendix A).
    /// Rules are tried in order, the first match wins. Prefixes compare as StartsWith(prefix + "_"): IX_Shutter_ does not
    /// match IX_ShutterSwitch_, IX_Door_ does not match IX_HutDoor_. Town's names repeat and differ by the number of digit
    /// groups (IX_Door3_12 a room door, IX_Door3_12_4 a wall-unit door), so Town matches whole names by regex. Forest has
    /// the same prefixes on the main building and on the map (IX_GarageDoor, IX_MainSwitch, IX_Breaker, IX_Drawer): its
    /// rules name the model the part belongs to.
    /// </summary>
    public static class LocationProfiles
    {
        const float DoorTravel = 1.2f, GateTravel = 3f;                // s, TEMPORARY (as the game's other doors)

        static Rule R(string prefix, Category c, string label, string kind, string s0 = null, string s1 = null, string a0 = null,
                      string a1 = null, float travel = DoorTravel) =>
            new Rule { prefix = prefix, category = c, label = label, kind = kind, s0 = s0, s1 = s1, a0 = a0, a1 = a1, travel = travel };

        static Rule X(string regex, Category c, string label, string kind, string s0 = null, string s1 = null, string a0 = null,
                      string a1 = null) =>
            new Rule { regex = new Regex(regex, RegexOptions.CultureInvariant), category = c, label = label, kind = kind, s0 = s0, s1 = s1, a0 = a0, a1 = a1 };

        static Rule Furniture(string prefix, string label, string kind = "hinge") =>
            R(prefix, Category.Furniture, label, kind, "закрыта", "открыта", "открыть", "закрыть");

        public static LocationProfile Find(string id)
        {
            foreach (LocationProfile p in new[] { BaseV2, Forest, Town, CoalPit, Dam, AboveClouds })
                if (string.Equals(p.id, id, StringComparison.Ordinal)) return p;
            return null;
        }

        // ------------------------------------------------------------------ BaseV2 (main building)
        public static readonly LocationProfile BaseV2 = new LocationProfile
        {
            id = "BaseV2", rootName = "BaseV2", scenePath = "Assets/Locations/BaseV2/Scenes/BaseV2.unity",
            requireLights = true, requireGrid = true,
            rules = new[]
            {
                R("IX_Door", Category.Door, "Дверь", "door"),
                R("IX_MainDoor", Category.Door, "Входная дверь", "door"),
                R("IX_ExitDoor", Category.Door, "Запасной выход", "door"),
                R("IX_RoofDoor", Category.Door, "Дверь на крышу", "door"),
                R("IX_GarageDoor", Category.Door, "Дверь гаража", "door"),
                R("IX_GarageGate", Category.Door, "Ворота гаража", "door", travel: GateTravel),
                R("IX_Drawer", Category.Furniture, "Выдвижной ящик", "pull", "задвинут", "выдвинут", "выдвинуть", "задвинуть"),
                Furniture("IX_RackDoor", "Дверца стойки"),
                Furniture("IX_LockerDoor", "Дверца шкафчика"),
                Furniture("IX_TumbaDoor", "Дверца тумбы"),
                R("IX_FridgeDoor", Category.Furniture, "Холодильник", "hinge", "закрыт", "открыт", "открыть", "закрыть"),
                Furniture("IX_PanelDoor", "Дверца щита"),
                R("IX_Breaker", Category.Positional, "Автомат", "toggle", "включён", "выключен", "выключить", "включить"),
                R("IX_Isolator", Category.Positional, "Поворотный выключатель", "toggle", "включён", "выключен", "выключить", "включить"),
                R("IX_MainSwitch", Category.PowerSwitch, "Главный рубильник", "toggle", "выключен", "включён", "включить", "выключить"),
                R("IX_LightSwitch", Category.LightSwitch, "Выключатель света", "toggle"),
                R("IX_ShutterSwitch", Category.ShutterSwitch, "Рольставни", "toggle"),
                R("IX_ShutterWinder", Category.ShutterWinder, "Ремень рольставни", "pull"),
                R("IX_Shutter", Category.Shutter, null, null),
                R("IX_HeatingStop", Category.Positional, "Аварийный выключатель отопления", "toggle", "выключен", "включён", "включить", "выключить"),
            },
            roomLabels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Windfang", "тамбур" }, { "Leitstand", "пульт" }, { "Flur_EG", "коридор 1-го этажа" }, { "Lager", "склад" },
                { "Treppenhaus", "лестница" }, { "Serverraum", "серверная" }, { "Garage", "гараж" }, { "Kaserne", "казарма" },
                { "Kueche", "кухня" }, { "Flur_OG", "коридор 2-го этажа" }, { "Waschraum", "санузел" },
                { "Stationsleiter", "кабинет начальника" }, { "Heizung", "котельная" }, { "Flur_UG", "коридор подвала" },
                { "Schaltraum", "щитовая" }, { "Exterior", "наружный свет" },
            },
        };

        // ------------------------------------------------------------------ Forest (FOREST_U3 appendix A: 173 parts, names unique)
        /// <summary>The main building (model BaseMain, 131 parts) by the BaseV2 profile; the main switch starts off here and
        /// says "включён, сети нет" without the generator (LOCATIONS A.2). The rest of the map — 42 parts by model.</summary>
        public static readonly LocationProfile Forest = new LocationProfile
        {
            id = "Forest", rootName = "Forest", scenePath = "Assets/Locations/Forest/Scenes/Forest.unity",
            requireLights = true, requireGrid = true,
            rules = ForestRules(),
            roomLabels = BaseV2.roomLabels,
            step = ForestLevelStep.Run,
            // the KPP kit, then the hut yard's props with game doors (the outhouse): both before the parts are gathered
            preStep = (scene, root) => ForestKppStep.Place(scene, root) && ForestHutDressStep.PlaceEarly(scene, root),
        };

        static Rule[] ForestRules()
        {
            var map = new List<Rule>
            {
                On("TowerHut", R("IX_HutDoor", Category.Door, "Дверь хижины", "door")),
                On("HutOuthouse", R("IX_OuthouseDoor", Category.Door, "Дверь уборной", "door")),   // the hut yard (ForestHutDressStep.PlaceEarly)
                On("Cabin", R("IX_CabinDoor", Category.Door, "Дверь избушки", "door")),
                On("PumpHouse", R("IX_PumpDoor", Category.Door, "Дверь насосной", "door")),
                On("Chapel", R("IX_ChapelDoor", Category.Door, "Дверь часовни", "door")),
                On("Checkpoint", R("IX_BoothDoor", Category.Door, "Дверь будки КПП", "door")),
                On("GenContainer", R("IX_ContSideDoor", Category.Door, "Дверь контейнера", "door")),
                On("Trafo", R("IX_TrafoGate", Category.Door, "Калитка трансформатора", "door")),
                On("Garage", R("IX_GarageSideDoor", Category.Door, "Дверь гаража", "door")),
                On("GenContainer", R("IX_ContDoorL", Category.Door, "Ворота контейнера", "door", travel: GateTravel)),
                On("GenContainer", R("IX_ContDoorR", Category.Door, "Ворота контейнера", "door", travel: GateTravel)),
                On("Garage", R("IX_GarageDoor", Category.Door, "Ворота гаража", "door", travel: GateTravel)),
                // KPP v2: the electric barrier and the gate drive move only from the consoles (ForestKpp): the host's shared
                // way without a zone, on their own layer (they hold the car and the player); ~4 s and ~12 s (KPP_CONTRACT)
                On("Barrier", NoZone(Speed(Doors(R("IX_BarrierArm", Category.HostDoor, "Шлагбаум", "door"),
                                                 "опущен", "поднимается", "поднят", "опускается", "упёрся"), 0.25f))),
                On("Hatch", Locked(R("IX_HatchLid", Category.Door, "Люк шахты", "door"), "заперт")),
                On("Gate", NoZone(Speed(Doors(R("IX_Gate", Category.HostDoor, "Откатные ворота", "door"), "закрыты", "едут", "открыты", "едут", "упёрлись"), 1f / 12f))),
                // snow_kit instances (ForestKppStep): the consoles, the booth's power cabinet
                On("Kit_Console_Button", R("IX_ConsoleButton", Category.Scripted, "Кнопка пульта", "momentary")),
                On("Kit_Cabinet_Power", R("IX_CabinetDoor", Category.Furniture, "Дверца шкафа питания", "hinge", "закрыта", "открыта", "открыть", "закрыть")),
                On("Kit_Cabinet_Power", R("IX_CabinetSwitch", Category.Scripted, "Переключатель NETZ / BATTERIE", "toggle")),
                On("Kit_Cabinet_Power", R("IX_CabinetClamp", Category.Scripted, "Клемма аккумулятора", "pull")),
                R("IX_CabinetDoor", Category.Furniture, "Шкаф антенны", "hinge", "закрыт", "открыт", "открыть", "закрыть"),
                On("Checkpoint", R("IX_Drawer", Category.Furniture, "Выдвижной ящик", "pull", "задвинут", "выдвинут", "выдвинуть", "задвинуть")),
                On("Checkpoint", R("IX_BoothWindow", Category.Furniture, "Окошко выдачи", "pull", "закрыто", "открыто", "открыть", "закрыть")),
                On("Checkpoint", R("IX_KeyBoxDoor", Category.Furniture, "Шкафчик с ключами", "hinge", "закрыт", "открыт", "открыть", "закрыть")),
                On("GenContainer", R("IX_Breaker", Category.Positional, "Автомат", "toggle", "включён", "выключен", "выключить", "включить")),
                On("GenContainer", NoPower(R("IX_MainSwitch", Category.PowerSwitch, "Рубильник контейнера", "toggle", "выключен", "включён", "включить", "выключить"),
                                           "включён, сети нет")),
                // the opening script's parts (texts and uses there, ForestOpening)
                On("GenContainer", R("IX_GenStart", Category.Scripted, "Пуск генератора", "lever")),
                On("GenContainer", R("IX_GenFillCap", Category.Scripted, "Горловина бака генератора", "cap")),
                On("FuelTanks", R("IX_PumpLever", Category.Scripted, "Ручной насос", "lever")),
                On("BaseMain_Opening", R("IX_HeatPump", Category.Scripted, "Насос отопления", "toggle")),
                On("BaseMain_Opening", R("IX_HeatBurner", Category.Scripted, "Горелка", "toggle")),
                On("BaseMain_Opening", R("IX_HeatThermostat", Category.Scripted, "Термостат", "rotary")),
                On("BaseMain_Opening", R("IX_HeatFill", Category.Scripted, "Кран подпитки", "wheel")),
                On("BaseMain_Opening", R("IX_BurnerReset", Category.Scripted, "Сброс горелки", "momentary")),
                On("BaseMain_Opening", NoZone(R("IX_HeatGauge", Category.Scripted, null, null))),
                On("BaseMain_Opening", NoZone(R("IX_HeatTempGauge", Category.Scripted, null, null))),
                On("BaseMain_Opening", R("IX_MainDoorBar", Category.Furniture, "Засов входной двери", "lever", "опущен — дверь заперта", "поднят", "поднять", "опустить")),
            };
            foreach (Rule b in BaseV2.rules)
            {
                Rule r = On("BaseMain", Clone(b));
                if (r.category == Category.PowerSwitch) r.s1NoPower = "включён, сети нет";
                map.Add(r);
            }
            return map.ToArray();
        }

        // ------------------------------------------------------------------ Town (A.3: 10 399 parts, ids p<n>)
        public static readonly LocationProfile Town = new LocationProfile
        {
            id = "Town", rootName = "Town", scenePath = "Assets/Locations/Town/Scenes/Town.unity",
            uniqueNames = false, incremental = true,
            rules = new[]
            {
                TownDoor(@"^IX_Entrance_L\d+_\d+$", "Дверь подъезда"),
                TownDoor(@"^IX_ShopDoor\d+_L\d+_\d+$", "Дверь магазина"),
                TownDoor(@"^IX_Door\d+_L\d+_\d+$", "Дверь"),                  // barracks
                TownDoor(@"^IX_AptDoor\d+_\d+$", "Дверь квартиры"),
                TownDoor(@"^IX_Door\d+_\d+$", "Дверь"),                       // room door: two digit groups
                TownFurniture(@"^IX_Door\d+_\d+_\d+$", "Дверца стенки"),        // wall unit: three digit groups
                TownFurniture(@"^IX_GlassDoor[LR]\d+_\d+_\d+$", "Стеклянная дверца"),
                X(@"^IX_Drawer\d*_\d+(_\d+)?$", Category.Furniture, "Выдвижной ящик", "pull", "задвинут", "выдвинут", "выдвинуть", "задвинуть"),
                TownFurniture(@"^IX_PanelDoor_\d+_\d+$", "Дверца щитка"),
                TownFurniture(@"^IX_CabDoor_\d+_\d+$", "Дверца шкафа"),
                TownFurniture(@"^IX_WallCab[LR]_\d+_\d+$", "Дверца навесного шкафа"),
                TownFurniture(@"^IX_Wardrobe[LR]_\d+_\d+$", "Дверца гардероба"),
                X(@"^IX_FridgeDoor_\d+_\d+$", Category.Furniture, "Холодильник", "hinge", "закрыт", "открыт", "открыть", "закрыть"),
                TownFurniture(@"^IX_OvenDoor_\d+_\d+$", "Духовка"),
                TownFurniture(@"^IX_PianoLid_\d+_\d+$", "Крышка пианино"),
            },
        };

        // ------------------------------------------------------------------ no openable parts (A.4)
        public static readonly LocationProfile CoalPit = new LocationProfile
        {
            id = "CoalPit", rootName = "CoalPit", scenePath = "Assets/Locations/CoalPit/Scenes/CoalPit.unity",
        };

        public static readonly LocationProfile Dam = new LocationProfile
        {
            id = "Dam", rootName = "Dam", scenePath = "Assets/Locations/Dam/Scenes/Dam.unity",
        };

        /// <summary>The sea of clouds has no collider and no terrain lies below -60 m.</summary>
        public static readonly LocationProfile AboveClouds = new LocationProfile
        {
            id = "AboveClouds", rootName = "AboveClouds", scenePath = "Assets/Locations/AboveClouds/Scenes/AboveClouds.unity",
            fallY = -40f,
        };

        // ------------------------------------------------------------------ helpers
        static Rule TownDoor(string regex, string label) =>
            Locked(X(regex, Category.HostDoor, label, "door", a0: "открыть", a1: "закрыть"), "заперта");

        static Rule TownFurniture(string regex, string label) =>
            X(regex, Category.Furniture, label, "hinge", "закрыта", "открыта", "открыть", "закрыть");

        static Rule Doors(Rule r, string closed, string opening, string open, string closing, string blocked)
        {
            r.doorStates = new[] { closed, opening, open, closing, blocked };
            return r;
        }

        static Rule Locked(Rule r, string text)
        {
            r.lockedState = text;
            return r;
        }

        static Rule NoPower(Rule r, string text)
        {
            r.s1NoPower = text;
            return r;
        }

        static Rule On(string model, Rule r)
        {
            r.model = model;
            return r;
        }

        static Rule Speed(Rule r, float speed)
        {
            r.speed = speed;
            return r;
        }

        static Rule NoZone(Rule r)
        {
            r.noZone = true;
            return r;
        }

        static Rule Clone(Rule r) => new Rule
        {
            prefix = r.prefix, regex = r.regex, category = r.category, label = r.label, kind = r.kind, s0 = r.s0, s1 = r.s1,
            a0 = r.a0, a1 = r.a1, s1NoPower = r.s1NoPower, lockedState = r.lockedState, doorStates = r.doorStates,
            travel = r.travel, model = r.model, speed = r.speed, noZone = r.noZone,
        };
    }
}
