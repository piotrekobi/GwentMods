# GWENT MODDING PROJECT: COMPLETE CONTEXT AND HANDOFF DOCUMENT
**Target Agent:** Claude Code
**Current State:** Step 3a Complete — Next: Test Mimikr Build, Then Elven Deadeye Premium

Hello Claude! You are being handed a modding project for the game **Gwent: The Witcher Card Game**. We have built a fully generic, config-driven pipeline to inject custom Premium (animated) cards into the retail game.

This document is your source of truth. Read it entirely before executing any actions.

---

## 1. THE ULTIMATE OBJECTIVE
We are creating **Custom Premium Cards** from scratch. Our current focus is "Tokens" or "Spawned" cards that CD Projekt Red (CDPR) never provided premium animations for.

The long-term target is the **Elven Deadeye (ArtId: 1832)** — a token card that was never made premium by CDPR.

The plan has 3 steps:
1. **DONE** — Build automation pipeline (`build.py`) — fully generic, config-driven
2. **DONE** — Test pipeline with multiple donor cards (Dryad Ranger, Milva, Siren Human Form, Falbeson)
3. **DONE (3a)** — Extend pipeline to build scenes from WIP prefabs (Mimikr)
4. **NOW** — Test Mimikr build end-to-end, then build Elven Deadeye from scratch

---

## 2. THE WORKING ENVIRONMENT
- **Unity Version:** 2021.3.15f1 (CRITICAL: AssetBundles must be built with this exact version)
- **Unity Installation:** `D:\Unity 2021.3.15f1\Editor\Unity.exe`
- **Source Code Project:** `D:\Gwent_Source_Code\Gwent\Gwent\GwentUnity\Gwent\`
- **Mod Environment:** `E:\Projekty\GwentMods\`
- **Game Directory:** `E:\GOG Galaxy\Games\Gwent\`
- **Python:** 3.12 with `UnityPy 1.24.2`
- **dotnet:** 9.0.100

### Key Directories
```
E:\Projekty\GwentMods\
├── build.py                     # Unified build pipeline (config-driven)
├── patch_bundle_shaders.py      # Post-build shader patcher (auto-discovers game shaders)
├── compare_bundles.py           # Debug utility (compares game vs custom bundles)
├── CustomPremiums\
│   ├── Core.cs                  # C# MelonLoader mod (6 Harmony hooks)
│   └── CustomPremiums.csproj    # .NET 6.0, PostBuild copies DLL to game
├── UnityScripts\
│   ├── AutoBuildOnLoad.cs       # [InitializeOnLoad] file-watcher for Unity Editor
│   └── BuildPremiumBundle.cs    # AssetBundle build methods (generic + legacy)
├── UnityAssets\Textures\
│   └── 1832.png                 # Fallback texture (not used when donor auto-copies)
├── CustomSoundsGuide.md         # Detailed guide for custom Wwise audio (future)
└── Claude_Handoff.md            # This file
```

---

## 3. WHAT WE'VE ALREADY BUILT

### 3.1 The Build Pipeline (`build.py`)
A single Python script that orchestrates 5 steps:
1. **Sync Unity scripts** — copies `UnityScripts/*.cs` → Unity project's `Assets/Editor/`
2. **Build AssetBundle** — writes trigger file (`targetArtId:donorArtId`), Unity's file-watcher detects it,
   copies the donor scene to `{targetArtId}.unity`, builds the bundle, cleans up, writes result
3. **Patch shaders** — runs `patch_bundle_shaders.py` to fix material shader references
4. **Build C# mod** — `dotnet build CustomPremiums.csproj`, auto-deploys DLL
5. **Deploy texture + config** — auto-copies premium texture from Unity source project, writes `donor_config.json`

**How to use:** Open Unity Editor with the Gwent project loaded, then run `python build.py`.

**Config-driven:** Just change the `CARDS` dict at the top of `build.py`:
```python
# Clone a donor card's scene:
CARDS = {
    "1832": {"donor": "1349"},  # Elven Deadeye ← Dryad Ranger premium
}

# Build from a WIP prefab (no finalized scene needed):
CARDS = {
    "1832": {
        "prefab": "Assets/PremiumCards/WIP/_Prefabs_WIP/[]Mimikr.prefab",
        "texture": "Assets/PremiumCards/WIP/_Textures_WIP/_Premium/_Uber/[]Mimikr_Atlas.png",
        "donor": "1349",  # optional: for premium SFX audio
    },
}
```
Everything else (scene generation/copying, texture lookup, audio mapping, shader patching) is automatic.

The file-watcher (`AutoBuildOnLoad.cs`) uses a `System.Threading.Timer` for background
polling so it works even when Unity is unfocused. Build timeout is 300 seconds.

### 3.2 The Shader Patching (`patch_bundle_shaders.py`)
**Problem:** Gwent uses proprietary shaders stored in separate dependency AssetBundles. Unity strips
shader references during build because it only has dummy `.shader` files (with GUID-spoofed `.meta` files).

**Solution (fully automatic):**
1. At patch time, scans the game's actual `shaderlibrary` and `shaders` bundles
2. Builds a `path_id → CAB_name` lookup table (164 shaders discovered)
3. For each material in the custom bundle:
   - Known shader pid → correct CAB reference
   - Unknown shader pid (only in Unity project, not in game) → fallback to GwentStandard
   - Null shader (fid=0, pid=0) → GwentStandard
   - Embedded/builtin shaders → left as-is

No hardcoded pids or CAB names anywhere — works for any donor card automatically.

### 3.3 The C# Mod (`CustomPremiums/Core.cs`)
A MelonLoader mod with 6 Harmony hooks:

| Hook | Target | Purpose |
|------|--------|---------|
| **Hook 0** | `GwentApp.HandleDefinitionsLoaded` | Maps ArtIds→TemplateIds. Swaps AudioId to donor's for premium SFX. Has dedup guard (fires twice). |
| **Hook 1** | `Card.SetDefinition` | Forces `IsPremium = true` |
| **Hook 2** | `CardDefinition.IsPremiumDisabled` | Forces return `false` |
| **Hook 3** | `CardViewAssetComponent.ShouldLoadPremium` | Forces return `true` |
| **Hook 4** | `CardAppearanceRequest.HandleTextureRequestsFinished` | Loads our custom scene bundle, bypasses normal pipeline |
| **Hook 5** | `CardAppearanceRequest.OnAppearanceObjectLoaded` | Swaps texture with custom art. Runtime shader fallback for InternalErrorShader. |
| **Hook 6** | `VoiceDuplicateFilter.GenerateVoiceover(int, ECardAudioTriggerType)` | Redirects voicelines back to original card (since AudioId was swapped to donor's for SFX) |

**How the mod finds cards:** Scans `Mods/CustomPremiums/Bundles/` for extensionless files (bundle name = ArtId)
and `Mods/CustomPremiums/Textures/` for `{ArtId}.png`. Cross-references against `SharedRuntimeTemplates` at startup.

**Audio dual-path:** Premium ambient SFX comes from the donor card (via AudioId swap in Hook 0).
Voicelines are redirected back to the original card (via Hook 6 on the `int` overload of `GenerateVoiceover`,
since the `Card` overload is a one-liner inlined by Il2Cpp AOT compiler).

**Runtime shader fallback (Hook 5):** If any material still has `Hidden/InternalErrorShader` after the
build-time patcher, tries: `ShaderLibrary/Generic/GwentStandard` → `GwentStandard` → `VFX/Common/AlphaBlended`.

**Config file:** Reads `donor_config.json` from the mod directory (written by `build.py`):
```json
{"1832": 1349}
```
Maps target ArtId → donor ArtId for audio.

### 3.4 Game File Deployment Layout
```
E:\GOG Galaxy\Games\Gwent\
├── Mods\
│   ├── CustomPremiums.dll       # The compiled mod
│   └── CustomPremiums\
│       ├── Bundles\
│       │   └── {ArtId}          # AssetBundle (extensionless)
│       ├── Textures\
│       │   └── {ArtId}.png      # Card texture
│       └── donor_config.json    # ArtId → donor ArtId mapping
```

---

## 4. CURRENT TASK: TEST MIMIKR BUILD + ELVEN DEADEYE (Step 3b)

### 4.1 Why Mimikr First?
Mimikr is an unreleased WIP card in the CDPR source code. It has all the raw assets needed for a
premium card (FBX, materials, animation controller, atlas texture) but **no finalized scene file**.
Building a scene for it teaches us the scene structure needed to create the Elven Deadeye premium from scratch.

### 4.2 What Mimikr Has (WIP Assets)
All at `D:\Gwent_Source_Code\Gwent\Gwent\GwentUnity\Gwent\Assets\PremiumCards\WIP\Scoiatael_WIP\[]Mimikr\`:

| File | Purpose |
|------|---------|
| `[]Mimikr.fbx` | 3D mesh (UV-mapped parallax diorama) |
| `[]Mimikr_AC.controller` | Animation controller (parallax/breathing animation) |
| `[]Mimikr_mat_1_1.mat` through `_mat_1_4.mat` | Materials (4 material slots on the mesh) |

Additional assets:
- **Prefab:** `Assets/PremiumCards/WIP/_Prefabs_WIP/[]Mimikr.prefab` — pre-assembled prefab (may have scene structure)
- **Atlas texture:** `Assets/PremiumCards/WIP/_Textures_WIP/_Premium/_Uber/[]Mimikr_Atlas.png`

### 4.3 What Mimikr Needs
A Unity scene file (`{artId}0101.unity`) that the pipeline can build into an AssetBundle. This requires:

1. **Understanding the scene hierarchy** — study existing finalized scenes (e.g., Dryad Ranger `13490101.unity`)
   to understand the required GameObject structure:
   ```
   Root → Pivot → model → mesh renderers + VFX
   ```
   Key components: `PremiumCardsMeshMaterialHandler` (assigns texture at runtime), `Animator`, `MeshRenderer`

2. **Building the scene** — either:
   - Create the scene programmatically from a Unity Editor script (like `GenerateElvenDeadeye.cs` was intended to do)
   - Or open the WIP prefab `[]Mimikr.prefab` in Unity and save it as a scene with the right structure

3. **The `[]` naming issue** — WIP assets have empty ArtId brackets `[]` instead of `[14XX0300]`. The scene
   builder may need to handle this. The prefab may already have the correct internal references.

### 4.4 How CDPR Scene Structure Works (Reference)
CDPR's premium cards are 2.5D parallax dioramas:
- **FBX mesh** with UV-mapped regions pointing to a texture atlas
- **GwentStandard material** in transparent mode (`_Mode=3`)
- **Animation controllers** driving mesh deformation for parallax/breathing effects
- **VFX particle systems** for ambient effects (leaves, dust, glow, etc.)
- **`PremiumCardsMeshMaterialHandler`** component that assigns the main texture at runtime
- Scene structure: `Root → Pivot → model → mesh renderers + VFX`

**CRITICAL COMPONENT:** `PremiumCardsMeshMaterialHandler` is what makes the texture swap work.
Without it, Hook 5 can't find where to assign the custom texture. The handler has
`PremiumTextureAssigments` — an array of material slots that get the atlas texture applied.

### 4.5 Approach (Step 3a DONE — prefab pipeline built)
The pipeline now supports building directly from WIP prefabs. To test:
1. Open Unity Editor with the Gwent project
2. Run `python build.py` (CARDS config already points to Mimikr prefab)
3. Launch game and verify Elven Deadeye card renders with Mimikr's animation
4. Check MelonLoader log for shader status and texture application
5. Apply same approach to build the Elven Deadeye premium from scratch

### 4.6 After Mimikr: Elven Deadeye Premium (Step 3b)
Once we know how to build a scene from raw assets, we create the Elven Deadeye premium:

**Elven Deadeye Info:**
- **ArtId:** 1832
- **TemplateId:** 202184
- **AudioId:** 1613
- **Standard texture:** `18320000.png` (in the Unity source project, Standard/Uber/)
- **No premium assets exist** — no FBX, no materials, no animation, no scene

This is the real challenge: creating a premium card entirely from scratch. Options:
- Design a custom 3D mesh and materials
- Repurpose/modify assets from a similar card (e.g., Dryad Ranger's FBX modified for Deadeye)
- Use the Mimikr approach as a template

---

## 5. TECHNICAL REFERENCE

### 5.1 Tested Donor Cards
The generic pipeline has been successfully tested with these donors:

| Donor | ArtId | Status | Notes |
|-------|-------|--------|-------|
| Dryad Ranger | 1349 | Works perfectly | Released card, full audio |
| Milva | 1191 | Works perfectly | Released card, full audio |
| Siren Human Form | 1415 | Renders, no audio | Unreleased — no CardTemplate, no AudioId |
| Falbeson | 1542 | Renders, no audio | Unreleased — no CardTemplate, no AudioId |

Unreleased cards render fine but have no premium audio because they were never shipped
(no card template in `data_definitions`, so AudioId lookup fails gracefully).

### 5.2 Available Unreleased Cards with Finalized Scenes
39 unreleased cards have finalized scenes in the Unity project (can be used as donors):
```
Assets/BundledAssets/CardAssets/Scenes/{artId}0101.unity
```
These include cards like SirenHumanForm (1415), Falbeson (1542), GernichorasParasite (1544),
Otkell (1379), and many others from various factions.

### 5.3 Audio System
**`data_definitions`** is a ZIP file at `StreamingAssets/data_definitions` containing:
- `Templates.xml` — card templates with AudioId mappings
- `CardAudio.xml` — 1358 audio definitions mapping AudioId → Wwise events
- `ArtDefinitions.xml`, `Abilities.xml`, `Personalities.xml`, etc.

**Audio pipeline:**
```
CardTemplate.AudioId → CardAudio.xml → CardAudioTrigger (PremiumCardPreview=6)
  → CardSoundEffect (Wwise EventId) → event_inclusion.json → soundbank_inclusion.json
  → .pck file → AkSoundEngine.LoadFilePackage() → AkSoundEngine.LoadBank()
  → AkSoundEngine.PostEvent()
```

**Wwise soundbanks** (`.pck` files) are in `StreamingAssets/audio/`. Card SFX are in
`cards_ep0_baseset_*.pck`, `cards_EP*.pck`, etc.

### 5.4 Il2Cpp / MelonLoader Notes
- The game is compiled with Il2Cpp. MelonLoader's Il2CppInterop exposes private fields
  as properties (e.g., `template.Template.AudioId` works).
- Harmony patches work on Il2Cpp methods but require `Il2Cpp` prefixed type names.
- `CardDefinition` is a struct (value type). Harmony `ref` parameters work for modifying it.
- Some methods are inlined by Il2Cpp AOT compiler (e.g., `GenerateVoiceover(Card, ...)` is
  a one-liner wrapper — must hook the `int` overload instead).
- `HandleDefinitionsLoaded` fires twice during game init — use dedup guard.

### 5.5 Key Enums
```
ECardAudioTriggerType: Invalid=0, AmbushCardRevealed=2, CardPlacedOnBoard=3, PremiumCardPreview=6
ECardSoundEffectType: None=0, Standard=1, Premium=2, Ambush=3
EPremiumMode: Disabled=0, Enabled=1, ...
```

### 5.6 AssetBundle Build Process
1. `AutoBuildOnLoad.cs` detects trigger file, parses `targetArtId:donorArtId`
2. `BuildPremiumBundle.WatcherBuildGeneric()` copies donor scene → `{targetArtId}.unity`
3. `BuildPipeline.BuildAssetBundles()` with `UncompressedAssetBundle` + dependency bundles
4. Cleanup: delete temp scene, manifests, dependency bundle files
5. `patch_bundle_shaders.py` fixes shader PPtrs to point at real game CABs

### 5.7 Shader System
- **shaderlibrary** bundle: Contains `GwentStandard` and other main shaders
  - CAB: `CAB-e59affbfa21235772054ea15448f1070`
- **shaders** bundle: Contains VFX shaders (`AlphaBlended`, `AdditiveAlpha`, etc.)
  - CAB: `CAB-c0bb786e78837791c9d84c9a06de6e2b`
- Dummy `.shader` files in the Unity project replicate property blocks with GUID-spoofed `.meta` files
- Post-build patcher auto-discovers pid→CAB mapping — no hardcoded values

---

## 6. COMPLETED WORK LOG

### Step 1: Build Pipeline (DONE)
- Created `build.py` — single-command orchestration of the full build pipeline
- Created `AutoBuildOnLoad.cs` — file-watcher with background timer for Unity Editor
- Created `BuildPremiumBundle.cs` — AssetBundle build with dependency bundles

### Audio Fix (DONE)
- Investigated Wwise audio pipeline end-to-end
- Fixed premium sound via AudioId swap (Hook 0) + voiceline redirect (Hook 6)
- Documented full Wwise pipeline in `CustomSoundsGuide.md`

### Step 2: Generic Config-Driven Pipeline (DONE)
- Made entire pipeline config-driven: just change `CARDS` dict in `build.py`
- `build.py`: Config dict, trigger format `targetArtId:donorArtId`, auto-texture from source, `donor_config.json`
- `AutoBuildOnLoad.cs`: Parses `targetArtId:donorArtId`, calls `WatcherBuildGeneric()`
- `BuildPremiumBundle.cs`: Added `WatcherBuildGeneric(targetArtId, donorArtId)` — derives donor scene path automatically
- `patch_bundle_shaders.py`: Auto-discovers all shader→CAB mappings from game bundles, fallback to GwentStandard
- `Core.cs`: Reads `donor_config.json` for per-card donor audio, runtime shader fallback chain, Hook 6 voiceline redirect
- Deleted `GenerateElvenDeadeye.cs` (superseded by generic pipeline)

### Step 3a: Build from WIP Prefabs (DONE)
- Extended pipeline to build scenes from WIP prefabs (cards without finalized scene files)
- `BuildPremiumBundle.cs`: Added `WatcherBuildFromPrefab(targetArtId, prefabPath)` — creates new scene,
  instantiates prefab via `PrefabUtility.InstantiatePrefab()`, builds bundle, cleans up
- `AutoBuildOnLoad.cs`: Extended trigger format to support `targetArtId:prefab:path/to.prefab`
- `build.py`: Added `prefab` and `texture` config keys; `donor` now optional (for audio only)
- Configured for Mimikr WIP card: prefab has complete hierarchy (PremiumCardsMeshMaterialHandler,
  Animator, SkinnedMeshRenderer with 4 materials, FBX mesh)
- Materials: `mat_1_1` uses GwentStandard, `mat_1_2-1_4` use Unity Standard (patched to GwentStandard)

### Donor Testing (DONE)
- Dryad Ranger (1349): Full audio + rendering
- Milva (1191): Full audio + rendering
- Siren Human Form (1415): Rendering works (shader fallback needed for 2 VFX materials), no audio (unreleased)
- Falbeson (1542): Clean rendering (all shaders in game bundles), no audio (unreleased)

### Commits
```
cc6adb0 Add shader fallback for unknown shaders and test multiple donors
7ae2365 Make pipeline fully generic and config-driven
244294c Update handoff document for Step 2 (Dryad Ranger clone)
03884b6 Add premium sound fix and custom sounds guide
d6e1057 Add unified build pipeline (build.py)
6fc929f Add Claude Handoff and UnityScripts
ad8163c V5 working state
```
