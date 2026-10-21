# Контейнер генератора на карте Forest (обновление 6) — справочник для Unity

Версия 1.0 от 21.10.2026. Модель `GenContainer` обновления 6: сдвоенный контейнер пакета `generator_container` (`GENCONT_CONTRACT.md` — его полный контракт: все подвижные детали, точки, сценарий) поставлен на место контейнера обновления 3 с его именами частей. Здесь — что отличается на карте: кадр, объекты FBX, части `IX_…`, свет, точки, материалы, данные. Источник чисел — `gcf_contract.json`, `gcf.json` пакета `source\assets\forest_gencont`.

## Кадр и объекты
- Кадр — префаб `GenContainer` (FBX после импорта): Unity, начало — точка установки обновления 3 (p (393,9669; 16,3300; -335,2701), поворот 8° вокруг Y, как раньше), земля y = 0, пол 0,31 м. Вдоль x — боксы, грузовые двери — на −x, дальний торец с боковой дверью и горловиной — на +x; «южный» бокс (генератор, щит; названия боксов — из `GENCONT_CONTRACT.md`) стоит на месте прежнего контейнера, «северный» (бак, бочки) — рядом, на +z.
- Габарит: x -3,54…3,43, y -0,12…3,61 (с выхлопом), z -1,34…3,81 м.
- Объекты FBX: `GenContainer` (корень: всё неподвижное — стены, рамы, генератор, щит с открытой на 165° дверцей, бак, бочки, насос, кабели, свет, жалюзи), 9 частей `IX_…`, `Window_Glass` (стекло окна, `M_GlassClear`). Треугольников 28 209: корень 26 163, створки 710 и 760, боковая дверь 394, крышка горловины 62, ключ 32, рубильник 48, автоматы по 12, стекло окна 4.
- Всё, что в исходном пакете двигалось само (крышки, краны, дверца щита, жалюзи, засовы), на карте неподвижно и входит в корень; двигаются только 9 частей обновления 3. Коллайдеры — импортные `MeshCollider` у всех объектов (как у всей карты).

## Части `IX_…`
Ось — в кадре префаба Unity, + открывает (как в данных `interactables`); точка поворота — начало объекта части.

| Часть | Ось | Ход | Точка поворота | Что это |
|---|---|---|---|---|
| `IX_ContDoorL_83` | (0; 1; 0) | 0…160° | (-3,015; 1,445; 1,216) | левая створка грузовых дверей; ключ «Generator» |
| `IX_ContDoorR_84` | (0; -1; 0) | 0…170° | (-3,015; 1,445; -1,235) | правая створка; ключ «Generator» |
| `IX_ContSideDoor_85` | (0; 1; 0) | 0…100° | (3,016; 1,320; 1,630) | боковая дверь в дальнем торце северного бокса; ключ «Generator» |
| `IX_GenStart_86` | (-1; 0; 0) | 0…52° | (-0,043; 1,310; 0,405) | ключ пуска на панели генератора: 0 — OFF, 52° — пуск/работа |
| `IX_Breaker_87` | (-1; 0; 0) | 0…60° | (-1,135; 1,360; -0,950) | автомат щита (ряд из трёх) |
| `IX_Breaker_88` | (-1; 0; 0) | 0…60° | (-1,225; 1,360; -0,950) | автомат щита |
| `IX_Breaker_89` | (-1; 0; 0) | 0…60° | (-1,315; 1,360; -0,950) | автомат щита |
| `IX_MainSwitch_90` | (0; 0; 1) | 0…60° | (-1,250; 1,560; -1,005) | перекидной рубильник щита: 0 — OFF, 60° — GEN |
| `IX_GenFillCap_300` | (0; 0; 1) | 0…110° | (3,174; 1,376; 2,945) | крышка короба заливной горловины (петля сверху); табличка DIESEL / DAY TANK 100 L |

Изменения против обновления 3 (данные `interactables` обновлены): у ключа, автоматов и рубильника — новые оси и ходы (новые рукоятки: ключ вращается вокруг оси x, автоматы — рычажки вниз-вверх, рубильник — поворот вокруг оси z), у крышки — новая ось (петля сверху), ход прежний — 0…110°; у левой створки ход 160° (за 160° она упирается в стенку северного бокса).

## Свет
Все 4 — в сети (`tag: power`, выключены на старте, `room: Exterior`), у каждого ореол `M_Glow_Bulb`; линзы плафонов — `M_Lamp_BulbOff` (сеть меняет на `M_Lamp_Bulb`), лампы «работа» на панели генератора и щита — `M_LEDOff` (→ `M_LEDOn`, зелёные).

| Тип | Точка | Направление | Цвет | Яркость | Дальность | Что |
|---|---|---|---|---|---|---|
| point | (-0,780; 2,467; 0,220) | (0,000; -1,000; 0,000) | (1,00; 0,76; 0,50) | 0,35 | 5,5 м | плафон |
| point | (0,780; 2,467; 2,320) | (0,000; -1,000; 0,000) | (1,00; 0,76; 0,50) | 0,35 | 5,5 м | плафон |
| point | (-1,450; 2,430; -1,037) | (0,000; -1,000; 0,000) | (1,00; 0,76; 0,50) | 0,35 | 5,5 м | плафон |
| spot | (-3,370; 2,690; 0,000) | (0,000; -1,000; 0,000) | (1,00; 0,72; 0,42) | 1,00 | 10,0 м | фонарь над грузовыми дверями (конус 140°) |

## Горловина и бак
`Forest_Opening.json` → `Gen_FillNeck`: горловина (397,4793; 17,5000; -332,7898), место игрока (398,2220; 16,3300; -332,8942) (мир Unity), подпись «Tagestank 100 l», бак 100 л (было 60; `startL`, `burnLph` прежние). В кадре префаба горловина — точка `Pour_Fill` (3,133; 1,170; 2,945): в коробе на дальнем торце северного бокса, под крышкой `IX_GenFillCap_300`; труба идёт внутри к дневному баку.

## Точки (кадр префаба)
Точки исходного пакета — в кадре префаба; у точек частей `IX_…` объект — сама часть (двигается с ней). В FBX их нет — только в `gcf_contract.json` (`sockets`), для будущих звуков, эффектов и пугалок.

### Руки у частей и горловина
| Точка | Где | Объект | Что |
|---|---|---|---|
| `Line_In` | (-1,200; 0,000; -1,258) | `GenContainer` | LINE (grid) cable: underground from the trafo; feeds the board via Board_Brk_Line |
| `Feed_Out` | (-1,200; 0,000; -1,268) | `GenContainer` | feeder to the base: the eight circuits go underground from here |
| `Hand_IX_ContSideDoor_85_Out` | (3,073; 1,050; 2,313) | `IX_ContSideDoor_85` |  |
| `Hand_IX_ContSideDoor_85_In` | (2,908; 1,050; 2,313) | `IX_ContSideDoor_85` |  |
| `Pour_Fill` | (3,133; 1,170; 2,945) | `GenContainer` | fill neck (cap off): the jerrycan pours here, the fuel goes to the day tank (100 l) |
| `Hand_IX_GenFillCap_300` | (3,191; 1,050; 2,945) | `IX_GenFillCap_300` |  |
| `Hand_IX_GenStart_86` | (-0,078; 1,310; 0,405) | `IX_GenStart_86` |  |
| `Hand_IX_MainSwitch_90` | (-1,250; 1,610; -0,960) | `IX_MainSwitch_90` |  |
| `Hand_IX_Breaker_87` | (-1,135; 1,349; -0,931) | `IX_Breaker_87` |  |
| `Hand_IX_Breaker_88` | (-1,225; 1,349; -0,931) | `IX_Breaker_88` |  |
| `Hand_IX_Breaker_89` | (-1,315; 1,349; -0,931) | `IX_Breaker_89` |  |

### Звук (audio)
| Точка | Где | Объект | Что |
|---|---|---|---|
| `Audio_Fan` | (2,030; 1,050; 0,205) | `GenContainer` | fan roar and the belt (squeal when loose) |
| `Audio_Engine` | (1,300; 1,010; 0,205) | `GenContainer` | diesel: glow tick, cranking, knock, idle 1500 rpm, labouring under load, dying |
| `Audio_Alternator` | (0,420; 0,850; 0,205) | `GenContainer` | alternator whine, rises with the load |
| `Audio_Board` | (-1,450; 1,660; -0,955) | `GenContainer` | hum of the busbar, the clack of the breakers, the changeover thunk, a breaker tripping |
| `Audio_DrumPump` | (1,890; 1,565; 3,030) | `GenContainer` | clank of the crank, gurgle, splash in the tank |
| `Audio_Exhaust` | (3,380; 3,600; 0,325) | `GenContainer` | exhaust beat at the stack, the flap rattling |
| `Audio_Louvre` | (2,900; 1,160; 0,205) | `GenContainer` | louvre blades rattling in the wind / in the fan blast |
| `Audio_Door` | (-2,970; 1,300; 0,000) | `GenContainer` | cargo doors: the bars and cams, hinges groaning, wind |
| `Audio_SideDoor` | (3,029; 1,200; 2,060) | `GenContainer` | side door: the latch, the hinges |
| `Audio_Roof` | (0,000; 3,000; 1,220) | `GenContainer` | steel roof: ticks and booms in the cold, needles and cones dropping, footsteps up there |
| `Audio_Wind` | (-2,950; 2,220; 2,440) | `GenContainer` | wind through the two intake louvres in the north box's doors |

### Стук снаружи (knock) — для пугалок вроде X07
| Точка | Где | Объект | Что |
|---|---|---|---|
| `Knock_WallS_1` | (-2,302; 1,200; -1,179) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_WallS_2` | (-0,902; 1,200; -1,185) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_WallS_3` | (1,000; 1,200; -1,179) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_WallS_4` | (2,198; 1,200; -1,207) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_WallN_1` | (-2,002; 1,200; 3,631) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_WallN_2` | (-0,700; 1,200; 3,619) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_WallN_3` | (0,902; 1,200; 3,652) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_WallN_4` | (2,198; 1,200; 3,647) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_WallE_1` | (3,026; 1,300; -0,730) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_WallE_3` | (3,182; 1,300; 3,020) | `IX_GenFillCap_300` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_Louvre` | (2,963; 1,049; 0,220) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_WallW_1` | (-2,982; 1,453; 1,820) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_WallW_2` | (-2,982; 1,453; 3,020) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_Window` | (-0,100; 1,620; -1,190) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_Roof_1` | (-1,500; 2,736; 2,420) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_Roof_2` | (1,500; 2,736; 2,420) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_Roof_3` | (-1,500; 2,736; 0,020) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_Roof_4` | (1,500; 2,736; 0,020) | `GenContainer` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_SideDoor` | (3,018; 1,300; 2,060) | `IX_ContSideDoor_85` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_IX_ContDoorR_84` | (-2,999; 1,700; -0,580) | `IX_ContDoorR_84` | something knocks / scratches here from outside (sound, a print in the dew) |
| `Knock_IX_ContDoorL_83` | (-2,981; 1,050; 0,620) | `IX_ContDoorL_83` | something knocks / scratches here from outside (sound, a print in the dew) |

### Тепло, укрытия, эффекты
| Точка | Где | Объект | Что |
|---|---|---|---|
| `Heat_Silencer` | (1,320; 1,740; 0,345) | `GenContainer` | hot silencer: burns, warms hands, melts snow |
| `Heat_Louvre` | (3,229; 1,160; 0,205) | `GenContainer` | warm spot outside the louvre while the set runs |
| `Hide_UnderWindow` | (-0,100; 0,610; -0,815) | `GenContainer` | crouched under the window: not seen from outside through the glass |
| `Hide_ShelfCorner` | (-2,550; 0,610; 3,220) | `GenContainer` | crouched in the north-west corner behind the shelf: not seen from the cargo doors |
| `VFX_RadSteam` | (2,160; 1,550; 0,355) | `GenContainer` | steam out of the neck when the hot cap is opened / the engine boils over |
| `VFX_RadDrain` | (2,202; 0,555; -0,215) | `GenContainer` | coolant runs out here |
| `VFX_FuelBleed` | (1,540; 1,036; -0,145) | `GenContainer` | froth, then clear diesel dribbles out while bleeding |
| `VFX_WaterDrain` | (1,820; 0,540; -0,275) | `GenContainer` | water, then diesel drips |
| `VFX_BoardSparks` | (-1,640; 1,560; -0,945) | `GenContainer` | sparks / smoke when a breaker trips under load or the changeover is thrown on load |
| `VFX_TankDrain` | (-0,530; 1,430; 3,345) | `GenContainer` | drips into the tray |
| `Nozzle_Tip` | (1,898; 1,414; 3,460) | `GenContainer` | fuel stream while cranking (splash if not in a filler) |
| `VFX_Extinguisher` | (-2,895; 1,030; 1,770) | `GenContainer` | powder cloud |
| `VFX_Exhaust` | (3,380; 3,630; 0,325) | `GenContainer` | exhaust: a black puff on starting, grey smoke under load, thin haze idling |
| `VFX_LouvreHeat` | (3,079; 1,160; 0,205) | `GenContainer` | warm air from the radiator through the open louvre: steam in the cold night air |
| `VFX_FillSpill` | (3,131; 1,120; 2,945) | `GenContainer` | diesel spilling at the fill neck (poured without the funnel) |

## Материалы и текстуры
- Атласы (`lit`, без бликов): `M_GCont_Cont` (`T_GCont_Cont_A_px`, 1024, 44,3 px/м), `M_GCont_Genset` (`T_GCont_Genset_A_px`, 512, 64,6 px/м), `M_GCont_Props` (`T_GCont_Props_A_px`, 512, 61,6 px/м), `M_GCont_Panel` (`T_GCont_Panel_A_px`, 512: таблички, приборы, щит, табличка горловины).
- Цвет: `M_GCont_Needle` (стрелки приборов), `M_GCont_Diesel` (дизель в мерной трубке бака).
- Имена карты: `M_Lamp_BulbOff`, `M_LEDOff` (пары сети), `M_GlassClear` (окно).
- Надписи на оборудовании — по-английски (MAIN SWITCHBOARD, CIRCUITS, DIESEL / DAY TANK 100 L), таблички на стенах — сначала по-немецки, ниже — по-английски.

## Данные (обновление 6)
`Forest_Layout.json`: `prefabLights` / `prefabGlows` контейнера (2 → 4), оси и ходы 7 частей, точка обзора `GenContainer` (p (391,3652; 18,1800; -335,4599), цель (394,4473; 17,3800; -335,0852), fov 62°: из грузового проёма вдоль генератора; прежняя камера оказалась бы внутри генератора); `Forest_Opening.json`: `Gen_FillNeck`. Дословно — блок `u6` → `changes`. Замки (`locks`: ключ «Generator» у `IX_ContSideDoor_85`, `IX_ContDoorL_83`, `IX_ContDoorR_84`) и шаг S7 — прежние.
