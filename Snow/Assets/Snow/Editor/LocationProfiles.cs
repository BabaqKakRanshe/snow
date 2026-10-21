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
    /// groups (IX_Door3_12 a room door, IX_Door3_12_4 a wall-unit door), so Town matches whole names by regex.
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

        // ------------------------------------------------------------------ Forest (A.2: 94 parts, names unique)
        public static readonly LocationProfile Forest = new LocationProfile
        {
            id = "Forest", rootName = "Forest", scenePath = "Assets/Locations/Forest/Scenes/Forest.unity",
            requireGrid = true,
            rules = new[]
            {
                R("IX_HutDoor", Category.Door, "Дверь хижины", "door"),
                R("IX_CabinDoor", Category.Door, "Дверь избушки", "door"),
                R("IX_PumpDoor", Category.Door, "Дверь насосной", "door"),
                R("IX_ChapelDoor", Category.Door, "Дверь часовни", "door"),
                R("IX_Door", Category.Door, "Дверь", "door"),
                R("IX_MainDoor", Category.Door, "Входная дверь", "door"),
                R("IX_BackDoor", Category.Door, "Задняя дверь", "door"),
                R("IX_ExitDoor", Category.Door, "Запасной выход", "door"),
                R("IX_BoothDoor", Category.Door, "Дверь будки КПП", "door"),
                R("IX_ContSideDoor", Category.Door, "Дверь контейнера", "door"),
                R("IX_TrafoGate", Category.Door, "Калитка трансформатора", "door"),
                R("IX_GarageSideDoor", Category.Door, "Дверь гаража", "door"),
                R("IX_ContDoorL", Category.Door, "Ворота контейнера", "door", travel: GateTravel),
                R("IX_ContDoorR", Category.Door, "Ворота контейнера", "door", travel: GateTravel),
                R("IX_GarageDoor", Category.Door, "Ворота гаража", "door", travel: GateTravel),
                Doors(R("IX_BarrierArm", Category.Door, "Шлагбаум", "door", a0: "поднять", a1: "опустить", travel: GateTravel),
                      "опущен", "поднимается", "поднят", "опускается", "упёрся"),
                Locked(R("IX_HatchLid", Category.Door, "Люк шахты", "door"), "заперт"),
                Doors(R("IX_Gate", Category.HostDoor, "Откатные ворота", "door"), "закрыты", "едут", "открыты", "едут", "упёрлись"),
                Furniture("IX_Shutter", "Ставня"),
                R("IX_Drawer", Category.Furniture, "Выдвижной ящик", "pull", "задвинут", "выдвинут", "выдвинуть", "задвинуть"),
                R("IX_CabinetDoor", Category.Furniture, "Шкаф антенны", "hinge", "закрыт", "открыт", "открыть", "закрыть"),
                Furniture("IX_RackDoor", "Дверца стойки"),
                Furniture("IX_LockerDoor", "Дверца шкафчика"),
                R("IX_FridgeDoor", Category.Furniture, "Холодильник", "hinge", "закрыт", "открыт", "открыть", "закрыть"),
                R("IX_Breaker", Category.Positional, "Автомат", "toggle", "включён", "выключен", "выключить", "включить"),
                NoPower(R("IX_MainSwitch", Category.PowerSwitch, "Рубильник", "toggle", "выключен", "включён", "включить", "выключить"),
                        "включён, сети нет"),
                R("IX_GenStart", Category.PowerSwitch, "Пуск генератора", "toggle", "остановлен", "работает", "запустить", "остановить"),
            },
        };

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
    }
}
