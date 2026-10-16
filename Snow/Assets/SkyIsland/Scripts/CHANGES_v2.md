# Sky Island Core v2: one builder for every location

The SkyIsland editor scripts now build any location that follows data contract v2
(`Assets/Locations/<Id>/{Data,Models,Textures,Textures/LUT,Previews}`). Sky Island itself still builds from its v1 JSON
**exactly as before**. All code and shaders stay in this package. Location packages contain only data and assets.

## Entry points

| What | How |
|---|---|
| Sky Island (unchanged) | `Tools > Sky Island > Build Scene`, or `SkyIslandBuilder.BuildScene()` |
| Any location | `SkyIslandBuilder.BuildLocation("Town")` builds `<root>/Scenes/Town.unity` with root object `Town`. `BuildLocation("SkyIsland")` is the same as `BuildScene()` |
| Window | `Tools > Locations > Location Builder`: lists every location (`FindLocations()`), with **Build / Open Scene / Capture** buttons |
| Batch build | `Unity -batchmode -quit -projectPath P -executeMethod SkyIsland.EditorTools.SkyIslandBuilder.BuildLocationFromCommandLine -location Town` |
| Batch capture | `... -executeMethod SkyIsland.EditorTools.SkyIslandBuilder.CaptureLocationFromCommandLine -location Town` opens `Scenes/Town.unity` and writes `<project>/SkyIslandCaptures/Town/` (needs a GPU, so do not pass `-nographics`) |
| Capture menu | `Tools > Sky Island > Capture Viewpoints`: works in any open location scene. Sky Island output goes to `SkyIslandCaptures/` (as in v1), other locations to `SkyIslandCaptures/<Id>/` |
| Apply Look menu | Uses the location whose id equals the open scene's name. Any other scene gets Sky Island's look |

When a batch method fails it throws, so Unity exits with a non-zero code. No dialogs are shown in batch mode.

Public API added: `Context.id`, `Context.coreRoot`, `Context.rules`, `Context.defaults`, `Context.Load(root, id)`,
`Context.FindLocations()`, `SkyIslandBuilder.FindLocations()`, `FindLocationRoot(id)`, `FindCoreRoot()`, `LocationScenePath()`,
`BuildLocation(id)`, `BuildLocationAt(root, id)`, `BuildLocationFromCommandLine()`, `CaptureLocationAt(root, id)`,
`CaptureLocationFromCommandLine()`, `SharedPrefix`, `SkyIslandId`, `SmokeTexture`. No existing public member was renamed or removed.

## How each contract field is handled

**Convention.** JsonUtility cannot tell an absent field from `0`, `false` or an empty list. So **0 or empty means "not set"**
and the default below applies. This is how all v1 files keep their behaviour. To get a real zero, use a tiny value
(for example `shadowStrength: 0.001`).

### Layout
| Field | Behaviour (default) |
|---|---|
| `version`, `id` | `id` is checked against the file name (a warning is logged if they differ; the file name wins) |
| v1 fields | Unchanged |
| `vegetationPrefixes` | Tree setup: LODGroup from `*_LODn`, capsule collider, vegetation static flags, no import collider. Default `["Spruce_","Dead_"]`. Non-Sky-Island trees with 3 or more LOD levels use the spruce LOD thresholds and radius, others use the dead-tree ones. Their capsule height is the LOD0 height, capped at 8 m |
| `grassPrefixes` | No collider (import), no shadows, per-renderer LODGroup culled at 2 %. Default `["SkyIsland_Grass","Grass_Clump"]`. Sky Island keeps v1 behaviour: only `SkyIsland_Grass` gets the LOD cull |
| `noColliderPrefixes` | Any object whose name matches (the model itself or a child, plus that object's children): colliders removed, shadows off. A matching **model** name also gets no import collider. Default `["Airfield_Lights","Approach_Lights","Power_Lines"]` |
| `rotators` | `SkyIslandRotator` is added to the named child inside the **prefab**, so every placed instance spins. Empty `child` means the model root. Default axis is up. The child's subtree loses batching-static. `radarHeads` still use `SkyIslandRadarRotator` |
| `flickers` | One `SkyIslandFlicker` on the location root with one entry per material. min = max = 0 gives 0.6..1, speed 0 gives 1. `lightPrefix` matches Light names: prefab lights are named `Beacon_<tag>_<n>`, `Lamp_<tag>_<n>` or `Light_<tag>_<n>` |
| `uvScroll` | One `SkyIslandUVScroll` on the root. `speed` scrolls `_BaseMap/_MainTex/_BaseColorMap`. `normalSpeed` scrolls `_BumpMap/_DetailNormalMap/_DetailAlbedoMap/_NormalMap/_DetailMap` |
| `emitters` | ParticleSystems under `Emitters`, named **`Emitter_<type>_<index>`** so `water.flowObjects` can refer to them. Presets: smoke, steam, spray, dust, embers. `scale` multiplies sizes, speeds, radius, noise and drift. `rate` multiplies emission. `color` is a linear colour (the preset alpha is kept). Material `<Id>_Smoke` (alpha blend) or `<Id>_SmokeAdditive` (embers, intensity 5 x emissionScale), shader `SkyIsland/Smoke`, texture `T_SmokePuff.png` (falls back to the SkyIsland copy). Fixed random seed per emitter |
| `water` | `SkyIslandWaterLevel` on the location root. Surfaces and flow objects are matched by object name (every match, but not a match nested inside another match). Surfaces lose static flags and colliders. A `startY` outside minY..maxY falls back to the modelled level. The scene is saved with the surfaces at the start level |
| `fogEvents` | Under `FogEvents`: one object per event (name, or `FogEvent_<n>`) with a trigger `BoxCollider` (size 0 gives 10 m) and `SkyIslandFogEvent`. Defaults: densityMul 3, duration 4 s, tag `Player`. `color` given means the fog colour also blends. `oneShot` is passed through |
| `spawn` | Empty `PlayerSpawn` (tag `Respawn`) at p, rotated by yaw degrees around Y |
| `giants` | Read with `SkyIslandJson`, because `bones` is an object keyed by name. Placed under `Giants`, see below |

**Giants.** `"shared:Name"` loads `<SkyIsland>/Models/Name.fbx`; a plain name loads `<location>/Models/Name.fbx`.
If the `.fbx` path fails, the model is searched by file name in that folder. The model is instantiated as a prefab instance
named `name` (default `Giant_<n>`). Its p/q go through the same AxisFix as placements, and its scale is `s` (0 means 1) x unit fix.
For each listed bone, the first child with that name (FindDeep order) gets the pose:
- a direct child of the giant root gets `localPosition`/`localRotation` = p/q as given;
- a nested bone is converted from giant-root space through its already-posed parent.

Quaternions are normalised. A missing `p` or `q` keeps the rest value, and missing bones are listed in a warning.
`hide` turns the named children off. The named location material is put on every renderer slot; if it is missing, the model
materials are kept and a warning is logged. Giants get structure static flags and shadows on. Import MeshColliders are kept,
and a MeshCollider is added to any mesh that has none.

### Look
| Field | Behaviour (default) |
|---|---|
| `fogByAltitude` | `SkyIslandFogByAltitude` on the camera. `center` (x, z) goes through AxisFix. `radii` are the half-axes. `topY` maps to islandTopY. Ranges of 0 give 350. Air density = density x `airFactor` (0 gives 1/3). The "camera under the island" rule is switched off (`thinBelow = false`). Absent: Sky Island gets its v1 ellipse (centre (0, -40), 745 x 470); other locations get no controller |
| `haze` | One `SkyIsland/FogSheet` quad (9 km) per entry at (0, y, 0): density 0 gives 0.6, missing speed gives (1.2, 0.5). Absent: Sky Island gets its 3 v1 sheets; other locations get none |
| `camera` | `far` (0 gives 6000), `fov` (0 gives the first viewpoint's fov, else 58) |
| `post.lut` | `<root>/Textures/LUT/<file>`, or `shared:` for SkyIsland's LUT folder. When URP's LUT size is 64, the file with its last "32" replaced by "64" is used if it exists. `lutContribution <= 0` (or no file) means no ColorLookup in URP and no LUT in Built-in |
| `sun.shadowStrength` | 0 gives 0.8 |
| `ambient.intensity` | Multiplies the trilight colours, like the preview (0 gives 1). Unity ignores `RenderSettings.ambientIntensity` in Trilight mode |
| `fog.density` | 0 turns RenderSettings fog off (Sky Island's 0.0036 keeps it on) |

### Materials
- Texture fields (albedo, normal, mask, emissionMap, detail.*) may be `"shared:<file>"`, which resolves to `<SkyIsland>/Textures/<file>`.
  `T_SkyClouds.png`, `T_FogNoise.png` and `T_SmokePuff.png` fall back to the SkyIsland copy when a location does not ship them.
  The missing-texture check uses the same resolution.
- New type **`transparent`**:
  - URP Lit: Surface Transparent, Blend Alpha (SrcAlpha / OneMinusSrcAlpha, alpha factors One / OneMinusSrcAlpha), preserve-specular off, ZWrite off, queue 3000, DepthOnly and ShadowCaster passes off. The same state URP's material inspector produces.
  - Built-in: Standard "Fade" mode.
  - HDRP: `_SurfaceType 1`, `_BlendMode` Alpha, `_TransparentZWrite 0`, then `HDMaterial.ValidateMaterial`.
  - Base alpha = `alpha` (0 gives 1 with `alphaFromTexture`, else 0.6, as in the preview). Renderers whose materials are all transparent get shadow casting off.
- `lit`, `cutout`, `emissive` and `glow` are unchanged. `emissive` drops its shadow caster in URP only, as in v1.

### Import rules (`SkyIslandAssetPostprocessor`)
- Applies to paths containing `SkyIsland` (as in v1) and to `.../Locations/<Id>/...` folders that have `Data/*_Layout.json`. Other "Locations" folders in the project are left alone.
- `*_Cut` textures get cutout alpha (from input, coverage-preserving mips at 0.42).
- `*_Macro*` textures get clamp wrap, 8192 max size and aniso 8.
- `T_Smoke*` textures keep their alpha, with clamp wrap.
- Albedos of `cutout` materials and of `transparent` materials with `alphaFromTexture` (read from the location's `_Materials.json`) keep their alpha.
- Model colliders: `addCollider` is off for model names matching the vegetation, grass or no-collider prefixes. The prefixes are read from the location's layout on disk and cached by file time.
- All Sky Island textures and models get exactly the same settings as before.

## Sky Island compatibility
`BuildScene()` and `BuildLocation(id)` share one implementation (`BuildLocationScene`). Sky Island gets
`LocationDefaults.Island`: the fog ellipse, the 3 haze sheets, and grass LOD only on `SkyIsland_Grass`. The model-name
defaults above are the old hard-coded names; the no-collider names occur only in `SkyIsland_Terrain.fbx` and have no children
(checked in the FBX). Beacons are created because Sky Island has `M_Light_Red` / `M_Glow_Red`. Generated asset names stay
`SkyIsland_*`. The scene has the same objects in the same order.

New serialized values in Sky Island's scene, with the same behaviour as before:
- `SkyIslandViewpoint.previewPath`
- `SkyIslandFogByAltitude.thinBelow = true`, `bottomY = -60` (identical formula)

## Files
New:
- `Editor/SkyIslandLocation.cs`: giants, flickers, UV scroll, water, fog events, spawn.
- `Editor/SkyIslandEmitters.cs`: particle presets, smoke materials.
- `Editor/SkyIslandJson.cs`: minimal JSON reader with fast top-level key lookup.
- `Editor/SkyIslandLocationWindow.cs`
- `Runtime/SkyIslandRotator.cs`, `SkyIslandFlicker.cs`, `SkyIslandUVScroll.cs`, `SkyIslandWaterLevel.cs`, `SkyIslandFogEvent.cs`
- `Runtime/SkyIslandMaterialSwap.cs`: internal runtime-copy helper.
- `Shaders/SkyIsland_Smoke.shader`

Changed:
- `SkyIslandBuilder.cs`: Context, location discovery, shared build, camera, fog-by-altitude, beacons.
- `SkyIslandData.cs`: v2 fields, `LayoutJson.ReadGiants`.
- `SkyIslandImport.cs`: `SkyIslandModelRules`, location-aware postprocessor.
- `SkyIslandMaterials.cs`: transparent type.
- `SkyIslandPrefabs.cs`: rules, rotators, transparent shadows, id-named glow quad.
- `SkyIslandLook.cs`: haze, ambient, shadow strength, LUT, id-named assets.
- `SkyIslandURP.cs`: LUT and ColorLookup.
- `SkyIslandCapture.cs`
- `Runtime/SkyIslandFogByAltitude.cs`: `densityScale` (runtime, non-serialized), `thinBelow`, `bottomY`.
- `Runtime/SkyIslandViewpoint.cs`: `previewPath`.
- `Runtime/SkyIsland.Runtime.asmdef`: references `Unity.RenderPipelines.Core.Runtime`; version define `com.unity.render-pipelines.core` >= 12.0.0 sets `SKYISLAND_SRP_CORE`.

Generated per location (in its folder):
- `Materials/<Pipe>/*.mat`
- `Materials/<Id>_Skybox.mat`, `<Id>_FogSheet*.mat`, `<Id>_GlowQuad.asset`, `<Id>_Smoke*.mat`
- `Prefabs/*.prefab`
- `Lighting/<Id>_LightingSettings.lighting`, `<Id>_SkyReflection.cubemap`, `<Id>_Probe_<n>.exr`
- `Settings/<Id>_URP_Volume.asset`
- `Scenes/<Id>.unity`

## Runtime components (namespace `SkyIsland`, menu `Sky Island/...`)
- **Rotator**: `axis` (local), `rpm`, `randomStart`. The start angle is derived from the object's position, so it is the same every run.
- **Flicker**: `root`, `entries{material, min, max, speed, lightPrefix}`. Emission (`_EmissionColor`/`_EmissiveColor`) and glow `_Intensity` follow 2-octave Perlin noise; matching lights follow the same level.
- **UV Scroll**: `root`, `entries{material, speed, normalSpeed}`.
- **Water Level**: `surfaces`, `minY`, `maxY`, `startLevel`, `maxSpeed`, `smoothTime`, `flowObjects`.
  - Methods: `SetLevel(y)`, `SetLevel01(t)`, `OpenSpillways()` (drain to minY), `CloseSpillways()` (refill to maxY), `Hold()`.
  - Properties: `Level`, `TargetLevel` (settable), `Level01`, `IsDraining`, `IsFilling`.
  - Events: `onDrainStarted`, `onDrainFinished`, `onFillStarted`, `onFillFinished`.
  - Flow objects are active only while draining.
- **Fog Event**: `densityMul`, `changeColor`/`fogColor`, `duration`, `oneShot`, `triggerTag`, `fogController`, and `volume`/`volumeWeight` (SRP only). Also `Trigger()`, `Revert()`, `onTriggered`, `IsActive`, `Progress`.
  - With a `SkyIslandFogByAltitude` present, the event scales its `densityScale`; otherwise it scales `RenderSettings.fogDensity` directly.
  - A one-shot event stays applied. Other events revert when the collider leaves.

Runtime material copies are made in `Start` (play mode only) and reverted and destroyed in `OnDestroy`; assets are never changed.

## Assumptions and limitations
- **Not compiled in Unity** (no Unity installation here). All C# files pass a tree-sitter parse. 143 newly used Unity / URP / SRP Core APIs were found in the UnityCsReference (6000.7) and Graphics (17.6) sources; `Transform` was taken from the GitHub source because the clone is sparse. Only long-standing APIs are used, and 2023.1+ APIs are behind `UNITY_2023_1_OR_NEWER`. The shader was not compiled.
- Giant bone poses are taken as giant-root-local, as in the contract. The planned flat FBX gets them applied directly.
- The shared `Giant_Skeleton.fbx` lives in `SkyIsland/Models`, so a Sky Island build also writes an (unused) `Giant_Skeleton.prefab`, and the model is imported with Sky Island's rules (import MeshColliders on).
- Each material can have only one runtime controller: Beacons, Flicker and UV Scroll each swap the shared material, so if two of them target the same material, only the first one to start works.
- UV scroll: URP Lit and Standard sample the main normal map with the base map's offset, so `normalSpeed` moves the detail layer.
- Water surfaces are treated as flat planes: the centre of their renderer bounds is the water level. All listed surfaces share one level.
- Fog events need a CharacterController or Rigidbody on the player (Unity trigger rules). In HDRP, RenderSettings fog is not used, so only the Volume blend has an effect.
- HDRP keeps the v1 policy: glow halos, haze sheets and emitters (Built-in/URP shaders) are skipped with a warning.
- `SkyIsland/Smoke` is unlit, so its colours come from the presets or JSON. Particle colours are converted linear to gamma like every other Unity colour property.
- An empty prefix list means "use the defaults" (see the convention above). To disable a list, give a prefix that matches nothing, such as `"-"`.
- Albedo alpha of `shared:` textures follows Sky Island's import rules. URP, Standard and HDRP always multiply the albedo alpha into a transparent material's alpha.

## v2.1
- `Runtime/SkyIslandFlicker.cs`: entries with the same `lightPrefix` (a lamp material and its glow halo) share one noise curve,
  so a tube and its halo flicker together; each Light is driven by the first entry with its prefix only.
- Materials JSON of every location uses the array form `{"materials":[...]}` (all fields explicit), like Sky Island.

## v2.3 (Update 4): power grid, lamps that start off

| Field | Behaviour |
|---|---|
| `prefabLights[].lights[].off` | The Light is created **disabled** (`enabled = false`). Used for lamps on a dead power grid |
| `prefabGlows[].glows[].tag` | Halo object name becomes `Glow_<tag>_<n>` (was `Glow_<n>`; untagged halos keep the old name) |
| `prefabGlows[].glows[].off` | The halo object is created inactive |
| `power` | `SkyIslandPowerGrid` on the location root: `lightPrefix` (Lights, e.g. `Light_power`), `glowPrefix` (halo objects, e.g. `Glow_power`), `switches` (interactable name prefixes that must ALL be open, e.g. `IX_GenStart`, `IX_MainSwitch`; each prefix needs at least one match), `materialsOff[i]` -> `materialsOn[i]` (swapped on every renderer under the root while powered), `startPowered` |

`SkyIslandPowerGrid` (Runtime): subscribes to `SkyIslandInteractable.onToggled` of the switches; `SetPower(bool)` and
`onPowerChanged` for gameplay code; never touches material assets (renderers get the other shared material). Lights inside
inactive proximity groups are handled too (they are collected with `includeInactive`).

## v2.4 (Update 5): PSX look, anchors, new giant skeleton

| Field | Behaviour |
|---|---|
| `look.psx` `{height, colorBits, dither}` | `SkyIslandPSX` on the location camera (Play mode): the camera renders into a RenderTexture of `height` lines (width from the screen aspect, Point filter, no MSAA); a full-screen RawImage (overlay canvas, sorting order -1000, under the game UI) shows it through `SkyIsland/PSXPresent`: `colorBits` per channel with a 4x4 Bayer dither at the internal resolution, nearest-neighbour upscaling. URP post still runs on the small frame. Needs `com.unity.ugui` (define `SKYISLAND_UGUI`) |
| `look.fog.mode = "Linear"`, `start`, `end` | RenderSettings linear fog. `SkyIslandFogByAltitude.linear`: start/end grow by 1 / `fogByAltitude.airFactor` away from the area; fog events divide start/end by `densityMul` |
| `look.fog.glowFloor` | `SkyIsland/LightGlow._FogFloor`: a halo keeps at least this fraction of its brightness in fog (beacons stay visible across the map) |
| `look.sky.starIntensity`, `starDensity`, `starScale`, `starSize`, `starColor` | Procedural stars in `SkyIsland/Skybox` (one candidate per cell of a direction grid) |
| material `noSpecular: true` | URP Lit: Specular Highlights and Environment Reflections off (PSX: no specular) |
| textures `*_px` | Imported with Point filtering, nearest mip, no aniso, uncompressed (small palettised PSX textures) |
| layout `anchors` `[{name, p, q}]` | Empty transforms `Anchors/Anchor_<name>` under the location root (places for separate assets, e.g. `Anchor_Car_Start`) |

Giant skeleton: `Models/Giant_Skeleton.fbx` and `Textures/T_Giant_A/M` are rebuilt from BodyParts3D 4.0 (real anatomy,
CC BY 4.0, (c) The Database Center for Life Science; credit line required, see `CREDITS.md`). The 150 bone names, pivots
and the hierarchy are unchanged, so every location's poses keep working; ~97k triangles in total (was ~300k): closed
meshes (source seams welded), skull 30k with the backs of both eye sockets broken through into the brain cavity (the
CoalPit adit walks through the upper one). Extra giants and flesh of every location were rebuilt for the new bones.

## Update 6 (PSX textures by the Utility Plane recipe)

`SkyIslandImport.ConfigureTexture`: textures named `*_px` are PSX textures. New: their masks (`*_M_px`, Unity
`_MetallicGlossMap` packing — R metal 0/1, G = 1, A smoothness in steps) are imported linear with their alpha kept (before,
the `_px` suffix hid the `_M`, so a PSX mask came in as sRGB without alpha), and PSX textures are never rescaled to a power
of two (`npotScale = None`: sign atlases such as 512 x 1728 keep their exact texels). Unchanged: Point filtering,
mipmaps on, anisotropy 0, no compression. The plane's own importer (`Assets/UtilityPlane/Editor`) is not touched.

Sky Island data: `SkyIsland_Materials.json` points 25 materials to `*_px` colour / `*_M_px` mask textures (shading baked
into the colour, 64-colour palette per texture, 4x4 Bayer dither; sizes by the measured texel density, about 64 px/m) and
has no normal or detail-normal maps; the terrain, runway and pavement macros stay bilinear with PSX detail maps.
`SkyIsland_Look.json` gets `psx` {height 240, colorBits 5, dither 1}: the PS1 frame is on in every location
(Town, CoalPit, Dam and AboveClouds get it in their `_Update7` packages, Forest and BaseV2 already have it).
The original textures stay in `Textures/` (other locations use them through `shared:`).

## v2.6 (Update 7): light fixtures, rendering layers per room, light LOD, shadow budget

Every lamp can be a fixture: a model child (`FX_<type>_<n>`) whose Lights, emissive parts and halo follow one brightness
level, driven by the lamp type, the power grid, its room's light switch or its battery. Rooms are light zones: their
static geometry has its own URP rendering layer, so a room lamp lights nothing behind its walls without shadows.
Locations without these fields build exactly as before.

| Field | Behaviour |
|---|---|
| `prefabLights[].lights[].fx` | The light goes under the model child with that name (world position and direction kept). The child gets a `SkyIslandLightFixture`, set up from its first light: `kind`, `circuit`, `room`, `hero`, `faulty`, `nosw`, `lod`, `warmup`, `restrike`, `flash` |
| `kind` | `fluorescent`, `incandescent` (default), `halogen`, `hps`, `mh`, `led`, `flame`, `exit`, `emergency`, `beacon` |
| `circuit` | `battery`: exit signs and emergency lights (not on the room switch). Anything else is the grid (`flame`: no supply needed) |
| `nosw` | Grid only, not on the room switch (street lights, obstruction lights) |
| `lod` | Light LOD distance in metres (0: HPS/MH 90, halogen 70, beacon 60, exit 20, others 25) |
| `warmup`, `restrike` | Gas discharge lamps: warm-up seconds (0: 90, MH 120) and restart delay after losing power (0: 60, MH 240) |
| `flash` | Beacons: `[period s, on fraction]`; empty = steady |
| `k` | Colour temperature in kelvin: `Light.useColorTemperature` on, `colorTemperature = k`, and the colour becomes the filter that keeps the exact colour `c` (Unity multiplies the linear colour by its temperature colour, so filter = c / temperature colour, magnitude into the intensity). Built-in only when Graphics > Lights Use Color Temperature is on |
| `room` | With `lighting`: the light's URP rendering layers are `(1 << layer of the room) | dynamicMask`. No room or an unknown room: every layer |
| `prefabGlows[].glows[].fx` | The halo goes under that fixture child |
| `interactables[].open` | Starts open: `openAmount = target = 1` (only the fields; `Awake` turns the part from its rest pose). Light switches that start on |
| `lighting` | `{exteriorLayer, defaultMask, dynamicMask, rooms[{name, layer, sw}], assign[{model, node, mask}], maxShadowed, shadowRange, lodFade}`, see below |

**`lighting`**
- Renderers of every prefab of the location: the `mask` of the nearest ancestor listed in `assign` for that model (node =
  object name, e.g. `R_Flur_EG`, `FX_Raster_10`, `IX_Door_5`), otherwise `defaultMask` (0: `1 << exteriorLayer`).
  Static geometry never has the Default bit, so room lamps light only their room's statics and the dynamic objects
  (players, props) whose renderers keep the Default layer (`dynamicMask`, 0: 1).
- The sun / moon lights every layer (in every location; no visible change where no layers are used).
- URP project settings (active, default and every quality level's URP asset): Lighting > Use Rendering Layers on, and
  Additional Lights > Cast Shadows on when `maxShadowed` > 0. URP lights only use **named** rendering layers, so layers
  1..highest bit get names where they have none ("Exterior", "Zone 4 (Leitstand, Stationsleiter)"): Unity 6 in Project
  Settings > Tags and Layers > Rendering Layers (`RenderPipelineEditorUtility.TrySetRenderingLayerName` /
  `TryAddRenderingLayerName`), Unity 2022.3 in the URP Global Settings asset (`m_RenderingLayerNames`, then URP's own
  refresh). Names that already exist are kept.
- `SkyIslandLightManager` on the location root: `rooms` with their switch prefixes (`sw`), the power grid of the root,
  `maxShadowed` (0: 3, negative: none), `shadowRange` (0: 14 m), `lodFade` (0: 6 m).
- HDRP and Built-in: a warning; no layers (room lamps then light the rooms behind their walls).

**GPU Resident Drawer** (Unity 6, SRP asset GPU Resident Drawer = Instanced Drawing): structures are built without
Batching Static (Occluder / Occludee / Reflection Probe static stay), and a log line asks to turn off Player > Static
Batching. Unity 2022 builds keep Batching Static.

### Runtime components
- **Light Fixture** (`SkyIslandLightFixture`, on the fixture child): `kind`, `circuit`, `room`, `switchable`, `faulty`,
  `hero`, `lodDistance`, `warmup`, `restrike`, `flash`, `lights`, `halos`, `emissive`.
  - Fluorescent: 1-3 starter blinks over 0.3-1.5 s; faulty tubes drop out in bursts every 3-14 s and sometimes go out for
    1.5-6 s and restart; under 0.85 grid voltage tubes flicker, under 0.65 they go out.
  - Incandescent / halogen: filament ramp (~0.1 s, from red to the lamp's temperature), afterglow when switched off; output
    V^3.4 (halogen V^3) and colour temperature V^0.42 under low voltage.
  - HPS / MH: warm-up from 6 % over `warmup` s (colour from white / blue to the lamp's); after losing power no restart for
    `restrike` s x how warm the lamp was; out under 0.85 voltage.
  - LED, exit, beacon: instant (beacon flashing by `flash`); emergency: 0.15 s; flame: flickers by itself.
  - Exit / emergency on battery: they sag and waver in the last 15 % of the manager's `batteryLife`.
  - Light intensity = authored x level x LOD fade; `colorTemperature` follows the warm shifts; emissive slots
    (`_EmissionColor` with `_EMISSION`, or `_EmissiveColor`) and halos (`_Intensity`, `_Color`) get runtime copies per fixture
    and follow level and colour shift. Halo renderers are off while the lamp is dark.
  - API: `SetSupply(on, voltage, battery, instant, delay)`, `Break()`, `Repair()`, `SetLodFade(f)`, `SetShadows(on, type,
    allowPoint)`; `Level`, `IsLit`, `Supplied`, `Broken`, `LodFade`, `Shadowed`; event `onEvent(fixture, Event)` with
    `Off, Starting, On, Blink, Fault, Broken, Repaired` (sound hooks: Blink = starter click, Fault = buzz).
  - Every pattern stays at or under 3 flashes per second (WCAG 2.3.1): starter blinks and fault dips are at least 0.34 s
    apart. `SkyIslandLightManager.noFlicker` (static, an accessibility option) makes every lamp steady.
  - Without a manager the fixture switches itself on at start (emergency lights stay dark). Same random pattern every run
    (seeded by position).
- **Light Manager** (`SkyIslandLightManager`, on the location root): `rooms{name, layer, switchPrefix, startOn}`, `grid`,
  `viewer` (default: main camera), `lodFade`, `lodScale`, `maxShadowed`, `shadowRange`, `shadowHysteresis`, `shadowType`,
  `pointShadows`, `shadowInterval`, `gridVoltage`, `batteryLife` (0 = endless), `restoreSpread`.
  - Grid lamps: grid powered and (not switchable, or the room on). Battery: exit always, emergency only while the grid is
    down. When the grid comes back, lamps start spread over `restoreSpread` s.
  - Room switches: each `onToggled` of an interactable with the room's prefix flips the room (two switches = two-way pair).
    Start state: on if any of its switches is open, else `startOn`.
  - First frame: the start state is applied instantly (no starter blinks at scene load). Later changes animate.
  - Light LOD every frame: Lights on within `lodDistance x lodScale`, fading out over `lodFade` m.
  - Shadow budget every `shadowInterval` s: the `maxShadowed` nearest lit hero fixtures within `shadowRange` get
    `shadowType` on their spot lights (point lights only with `pointShadows`); a shadowed fixture keeps its shadow until
    another is `shadowHysteresis` m closer.
  - API: `IsRoomOn`, `SetRoom`, `ToggleRoom`, `SetGrid`, `BreakFixtures(center, radius)`, `Rescan()`; `GridPowered`,
    `Battery`, `Fixtures`; events `onRoomChanged(room, on)`, `onFixtureEvent(fixture, event)`.
  - The power grid's own lights (`Light_power*`) are not touched; fixture lights are named `Light_fx_<n>`.

### Files
New: `Runtime/SkyIslandLightFixture.cs`, `Runtime/SkyIslandLightManager.cs`, `Editor/SkyIslandLighting.cs`.
Changed: `SkyIslandData.cs` (fields above), `SkyIslandPrefabs.cs` (fixtures, layers, temperature, open interactables,
static flags), `SkyIslandLocation.cs` (light manager, giants' static flags), `SkyIslandLook.cs` (sun layers),
`SkyIslandBuilder.cs` (project settings before the prefabs).

### Checks and limitations
- Not compiled in Unity (no installation here). All files pass a tree-sitter parse. The new and changed files compile with
  `mcs` against API stubs in three define sets (2022.3 + URP, Unity 6 + URP, no URP); every Unity / URP API they use was
  checked in UnityCsReference 2022.3 / 6000.0 and Graphics 14.0.12 / 17.0.4. The fixture and manager logic also ran in
  a simulation (mono, stub engine with a clock), 50 checks: starter blinks and the 3-flashes-per-second limit, faults,
  brownout, filament ramp and afterglow, HID warm-up and restrike, beacon flashing, battery sag, LOD fade, the shadow
  budget, two-way switches, grid loss and restore, BreakFixtures.
- Dynamic objects keep the Default layer, so a room lamp also lights a player standing behind its wall (no shadows).
- Open doors do not pass light between rooms yet (each room's lamps light only its own geometry). Next step: a dim
  spill light per doorway that is on while the door is open and the room behind it is lit.
- Emergency lenses and exit signs glow in edit mode: fixtures set their emission in play mode only.

## v2.7 (Update 8): scale parts, linked parts, powered parts (roller shutters); interactables fix

### Fix: interactables were never added in the Editor (v2.3 - v2.6)
`AddInteractables` used `GetComponent<SkyIslandInteractable>() ?? AddComponent<...>()`. In the Editor `GetComponent`
returns a "fake null" object for a missing component (a managed wrapper that only compares equal to null through
UnityEngine.Object's `==`), so `??` kept the wrapper, `AddComponent` never ran, and every field went to a detached
object: doors, drawers, light switches and main switches in built prefabs had no `SkyIslandInteractable`. Now
`TryGetComponent` (no fake null). **Rebuild every location built with v2.3 - v2.6.**

### Interactable (`SkyIslandInteractable`)
- **`Kind.Scale`**: scales the part along `axis` (the part's own space, absolute components) about its pivot by a factor
  in [min, max] (`v = Lerp(min, max, openAmount)`, scale = rest x (1 + (v - 1) x |axis|), v clamped to >= 0.001 so a
  MeshCollider never gets a zero scale). Roller-shutter curtains are pivoted at their head: `min 1` = down, `max ~0.03` =
  rolled up behind the head strip. `restScale` = the full-size scale (zero: the scale at start), so a part can be shown
  open in the editor. Awake always applies a scale part.
- **`linked`**: parts that follow this part's target after its own `Open` / `Close` / `SetTarget` (`Follow(t)`, every
  follower gets the same value). Their own lock and power rules apply; `onToggled` is raised when their open state flips.
  A part linked back is not driven again (the guard holds for the whole drive); a throwing listener does not lock the
  driver (`try / finally`).
- **`driven`**: moved only through the parts linking to it: `Toggle()` (the interactor) raises `onLocked`, nothing moves.
  `Open` / `Close` / `SetTarget` / `Follow` from code still work (storm events, scripted shutters).
- **`needsPower`** (+ `grid`): without power the part refuses commands (`onLocked`). A powered part that drives nothing
  (a shutter motor) caught by a blackout waits where it is, keeps its target and goes on when the grid returns, so the
  switch and the curtains always agree. A driver's own motion (a switch rocker) always finishes. `grid`: assigned by
  the builder to every powered part (`AddPower`, recorded as a prefab-instance override); if empty, the
  `SkyIslandPowerGrid` in the parents (inactive ones too) is looked up, and only a found grid is kept; none = powered.
- API added: `Follow(t)`, `HasPower`. Nothing renamed or removed; doors, drawers and switches behave as before
  (`Close()` now also checks power, which only matters for `needsPower` parts). `Open()` / `Close()` raise `onToggled`
  on every call as before (light switches rely on it); `Follow()` only on a real change; `SetTarget()` never.

### Power grid (`SkyIslandPowerGrid`)
- `Powered` is set to `startPowered` in `Awake`, so powered parts asked from another script's Awake / Start see the start
  state (lights, halos and materials are still applied in Start).

### Layout
| Field | Behaviour (default) |
|---|---|
| `interactables[].kind` | `hinge` (default), `slide`, **`scale`** |
| `interactables[].speed` | open fraction per second; 0 = component default 1.4 |
| `interactables[].needsPower` | see above; also set on every part a powered part drives, down the whole chain |
| `interactables[].drives` | child names of parts that follow this one -> `linked` on this part, `driven` on them. Names not found are listed in a warning; a part naming itself is skipped |
| `interactables[].open` + `scale` | the builder stores `restScale` (once) and applies the open scale in the prefab (edit-time view = play start) |

### Files
Changed: `Runtime/SkyIslandInteractable.cs`, `Runtime/SkyIslandPowerGrid.cs` (Awake), `Editor/SkyIslandData.cs`
(InteractableDef: `speed`, `needsPower`, `drives`), `Editor/SkyIslandPrefabs.cs` (AddInteractables: TryGetComponent, kind
scale, speed, power, the drives pass), `Editor/SkyIslandLocation.cs` (AddPower: powered parts get the grid).

### Checks
- Not compiled in Unity (no installation here). Tree-sitter parse clean; the changed files compile with `mcs` against API
  stubs in the three define sets (2022.3 + URP, Unity 6 + URP, no URP); `TryGetComponent`, `GetComponentInParent(bool)`,
  `PrefabUtility.IsPartOfPrefabInstance` / `RecordPrefabInstancePropertyModifications` checked in UnityCsReference
  2022.3.76 / 6000.0.84. Simulation (mono, stub engine with a clock): 69 checks, 19 new: curtain shown open in the editor
  and kept by Awake, a driven curtain refusing the interactor, a winder lowering and raising it (timing at speed 0.3,
  onToggled), a motor switch refusing in a blackout, lowering three curtains, the motors waiting through a blackout and
  finishing afterwards, the rocker finishing its flip, a throwing listener, the scale clamp, two parts linked both ways,
  doors unchanged (open, locked). The Editor path itself (fake null) cannot run in the stubs.
- An independent code review of the change set found the fake-null bug and the semantics items above; all are fixed.
- A scaled curtain keeps its MeshCollider (non-convex, PhysX takes the non-uniform scale): a closed shutter blocks rays;
  a rolled-up one keeps a 5.5 cm strip at the head, behind the head strip.

## v2.7.1 (Update 8, 16.10.2026): sky reflection cubemap in Linear colour space

`Editor/SkyIslandLook.cs`, `RenderSkyCubemap`: URP 17 renders `Camera.RenderToCubemap` faces like a back-buffer target,
so in Linear colour space the cubemap comes back sRGB-encoded (reflections too bright and washed out). After the
"not black" check the builder compares the zenith pixel with `_ZenithColor` (linear and sRGB-encoded) and, if the
encoded value is closer, decodes all six faces to linear (`Color.linear`, `Apply(true)`), logging
`sky reflection cubemap came back sRGB-encoded from RenderToCubemap; decoded to linear.` The fix was made in the game
project on 01.10.2026 (Unity 6000.0.67, URP 17.0.4) on the v1 file; v2.7.1 carries it, so installing the update chain no
longer drops it. Gamma colour space and skies without `_ZenithColor` are not touched.
