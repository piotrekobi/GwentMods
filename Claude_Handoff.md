# GWENT MODDING PROJECT: COMPLETE CONTEXT AND HANDOFF DOCUMENT
**Target Agent:** Claude Code
**Current State:** Step 2 Complete, Step 3 Ready — Build Elven Deadeye Premium

Hello Claude! You are being handed a modding project for the game **Gwent: The Witcher Card Game**. We have built a pipeline to inject custom Premium (animated) cards into the retail game.

This document is your source of truth. Read it entirely before executing any actions.

---

## 1. THE ULTIMATE OBJECTIVE
We are creating **Custom Premium Cards** from scratch. Our current focus is "Tokens" or "Spawned" cards that CD Projekt Red (CDPR) never provided premium animations for.

The long-term target is the **Elven Deadeye (ArtId: 1832)** — a token card that was never made premium by CDPR.

The plan has 3 steps:
1. **DONE** — Build automation pipeline (`build.py`)
2. **DONE** — Clone the Dryad Ranger premium as a working baseline
3. **NOW** — Build a brand-new Elven Deadeye premium from scratch

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
├── build.py                     # Unified build pipeline (Step 1 output)
├── patch_bundle_shaders.py      # Post-build shader patcher (UnityPy)
├── CustomPremiums\
│   ├── Core.cs                  # C# MelonLoader mod (Harmony hooks)
│   └── CustomPremiums.csproj    # .NET 6.0, PostBuild copies DLL to game
├── UnityScripts\
│   ├── AutoBuildOnLoad.cs       # [InitializeOnLoad] file-watcher for Unity Editor
│   ├── BuildPremiumBundle.cs    # AssetBundle build methods
│   └── GenerateElvenDeadeye.cs  # Scene generator (used later in Step 3)
├── UnityAssets\Textures\
│   └── 1832.png                 # Source texture for Elven Deadeye
├── CustomSoundsGuide.md         # Detailed guide for custom Wwise audio (future)
└── Claude_Handoff.md            # This file
```

---

## 3. WHAT WE'VE ALREADY BUILT

### 3.1 The Build Pipeline (`build.py`)
A single Python script that orchestrates 5 steps:
1. **Sync Unity scripts** — copies `UnityScripts/*.cs` → Unity project's `Assets/Editor/`
2. **Build AssetBundle** — creates `build_trigger.txt`, Unity's file-watcher detects it,
   builds the bundle, writes `build_result.txt` with OK/FAIL
3. **Patch shaders** — runs `patch_bundle_shaders.py` to fix material shader references
4. **Build C# mod** — `dotnet build CustomPremiums.csproj`, auto-deploys DLL
5. **Deploy texture** — copies PNG from `UnityAssets/Textures/` to game's mod folder

**How to use:** Open Unity Editor with the Gwent project loaded, then run `python build.py`.

The file-watcher (`AutoBuildOnLoad.cs`) uses a `System.Threading.Timer` for background
polling so it works even when Unity is unfocused. Build timeout is 300 seconds.

### 3.2 The Shader Patching Problem (Solved)
Gwent uses proprietary shaders (`ShaderLibrary/Generic/GwentStandard`, `VFX/Common/AdditiveAlpha`, etc.)
stored in separate dependency AssetBundles. Our Unity Editor doesn't have the compiled shader binaries,
so Unity treats them as missing shaders and **strips all material properties** during AssetBundle build.

**Solution:**
1. Dummy `.shader` files replicate the exact property blocks of the real Gwent shaders
2. GUID spoofing in `.meta` files matches the real shader GUIDs
3. `patch_bundle_shaders.py` uses UnityPy to fix the `m_Shader` PPtrs post-build:
   - GwentStandard → `CAB-e59affbfa21235772054ea15448f1070` (shaderlibrary)
   - VFX shaders → `CAB-c0bb786e78837791c9d84c9a06de6e2b` (shaders)

### 3.3 The C# Mod (`CustomPremiums/Core.cs`)
A MelonLoader mod with Harmony patches:
- **Hook 0** (`HandleDefinitionsLoaded`): Maps ArtIds to TemplateIds at startup. Also sets
  each custom card's `AudioId` to the donor card's AudioId for premium sound support.
- **Hook 1** (`Card.SetDefinition`): Forces `IsPremium = true`
- **Hook 2** (`CardDefinition.IsPremiumDisabled`): Forces return `false`
- **Hook 3** (`CardViewAssetComponent.ShouldLoadPremium`): Forces return `true`
- **Hook 4** (`CardAppearanceRequest.HandleTextureRequestsFinished`): Loads our custom
  scene bundle instead of the game's normal pipeline
- **Hook 5** (`OnAppearanceObjectLoaded`): Swaps textures with our custom art. Also fixes
  VFX materials whose shaders resolved to `Hidden/InternalErrorShader` at runtime.

**How the mod finds cards:** It scans `Mods/CustomPremiums/Bundles/` for extensionless
files (bundle names = ArtId) and `Mods/CustomPremiums/Textures/` for `{ArtId}.png` files.
At game startup, it cross-references these against `SharedRuntimeTemplates` to find TemplateIds.

**Audio:** Premium ambient sound is borrowed from the Elven Wardancer (`DonorArtId = 1222`).
The mod sets each custom card's `CardTemplate.AudioId` to the Wardancer's AudioId in Hook 0.
This makes the entire Wwise pipeline work automatically (soundbank loading + event playback).
See `CustomSoundsGuide.md` for details on how to bundle custom sounds in the future.

### 3.4 Game File Deployment Layout
```
E:\GOG Galaxy\Games\Gwent\
├── Mods\
│   ├── CustomPremiums.dll       # The compiled mod
│   └── CustomPremiums\
│       ├── Bundles\
│       │   └── {ArtId}          # AssetBundle (extensionless)
│       └── Textures\
│           └── {ArtId}.png      # Card texture
```

---

## 4. CURRENT TASK: STEP 2 — CLONE THE DRYAD RANGER PREMIUM

### 4.1 Why the Dryad Ranger?
The Dryad Ranger (ArtId **1349**) is a Scoia'tael card depicting a character holding a bow —
visually very similar to the Elven Deadeye. By cloning its premium and getting it working
in our pipeline, we'll study its scene structure as a functional baseline for Step 3.

### 4.2 Dryad Ranger Asset IDs
| ID | Purpose |
|---|---|
| `1349` | Base ArtId |
| `13490000` | Standard texture |
| `13490100` | Premium texture |
| `13490101` | Premium scene (the animated scene bundle) |
| `13490300` | Premium card FBX model / materials |

### 4.3 Key Source Files for the Dryad Ranger
All paths relative to `D:\Gwent_Source_Code\Gwent\Gwent\GwentUnity\Gwent\Assets\`:

- **Scene:** `BundledAssets/CardAssets/Scenes/13490101.unity`
- **FBX Model:** `PremiumCards/Scoiatael/[13490300]DryadRanger/[13490300]DryadRanger.fbx`
- **Atlas Material:** `PremiumCards/Scoiatael/[13490300]DryadRanger/Materials/[13490300]DryadRanger-Atlas.mat`
- **Animation Controllers:**
  - `[13490100]DryadRanger_AC_model.controller`
  - `[13490100]DryadRanger_AC_master.controller`
- **VFX Materials:** DryadRanger_LensPostFX.mat, DryadRanger_leaf_all.mat, etc.
- **VFX Textures:** Leaf/petal particle textures (PNG)

### 4.4 What Step 2 Requires

**Goal:** Build and deploy the Dryad Ranger premium (ArtId 1349) as a custom premium
using our pipeline, without borrowing anything from the compiled game — only from the
decompiled source code assets.

You need to:

1. **Update `build.py`** to build the Dryad Ranger (ArtId 1349) instead of / in addition
   to the Elven Deadeye. The scene file `13490101.unity` already exists in the Unity
   project, so no donor scene copying is needed.

2. **Update `BuildPremiumBundle.cs`** (and possibly `AutoBuildOnLoad.cs`) to build
   `13490101.unity` → bundle named `1349`. The build method should be similar to the
   existing `WatcherBuild1832()` but for the Dryad Ranger scene.

3. **Get the Dryad Ranger texture** from the source project assets (not from the game's
   compiled bundles). It should be in the Unity project's texture assets. Place it at
   `UnityAssets/Textures/1349.png`.

4. **Update `Core.cs`** if needed — the mod should auto-detect the new ArtId 1349 from
   the Bundles/Textures directories, so it might work without code changes. However:
   - The donor AudioId may need updating — check if the Wardancer's audio fits the
     Dryad Ranger or if we should use the Dryad Ranger's own AudioId
   - Actually, since the Dryad Ranger IS a real premium card in the game, it already
     has its own premium audio. The donor approach should still work, but you could
     also just let it keep its own AudioId.

5. **Test the full pipeline:** `python build.py` should produce a working Dryad Ranger
   premium visible in-game when viewing the card.

### 4.5 Important Technical Notes

- The Dryad Ranger scene (`13490101.unity`) references an FBX model and materials from
  `PremiumCards/Scoiatael/[13490300]DryadRanger/`. These are already in the Unity project
  so the AssetBundle build should include them automatically.

- The bundle must still go through `patch_bundle_shaders.py` because the materials use
  GwentStandard and VFX shaders that get stripped during build.

- The Dryad Ranger is a **real premium card** that exists in the retail game. When testing,
  the mod will force its own bundle to load instead of the game's official one. This means
  you'll be able to compare the custom-built result against the real thing.

- The build pipeline currently hardcodes `ART_ID = "1832"`. Consider making it configurable
  (e.g., command-line argument or building multiple cards).

### 4.6 Approach: Build From Source Assets Only

The key constraint: we build everything from the **decompiled source code** assets
(the Unity project at `D:\Gwent_Source_Code\...`), NOT from the compiled game bundles.
This proves our pipeline can produce premium cards from scratch using only source materials.

---

## 5. TECHNICAL REFERENCE

### 5.1 Premium Card Scene Structure (How CDPR Does It)
CDPR's premium cards are 2.5D parallax dioramas built from:
- **FBX mesh** with UV-mapped regions pointing to a texture atlas
- **GwentStandard material** in transparent mode (`_Mode=3`)
- **Animation controllers** driving mesh deformation for parallax/breathing effects
- **VFX particle systems** for ambient effects (leaves, dust, glow, etc.)
- **`PremiumCardsMeshMaterialHandler`** component that assigns the main texture at runtime

The scene structure follows: `Root → Pivot → model → mesh renderers + VFX`

### 5.2 AssetBundle Build Process
1. Scene goes through `BuildPipeline.BuildAssetBundles()` with `UncompressedAssetBundle`
2. Dependency bundles for `bundledassets/dependencies/*` are built alongside (so Unity
   creates external references to the shader CABs)
3. Manifests and extra files are cleaned up
4. `patch_bundle_shaders.py` fixes shader PPtrs to point at the real game's CAB dependencies

### 5.3 Wwise Audio System (Reference)
Gwent uses Wwise middleware for all audio. Premium card ambient sounds follow this chain:
```
CardTemplate.AudioId → CardAudio XML → CardAudioTrigger (PremiumCardPreview=6)
  → CardSoundEffect (Wwise EventId) → event_inclusion.json → soundbank_inclusion.json
  → .pck file → AkSoundEngine.LoadFilePackage() → AkSoundEngine.LoadBank()
  → AkSoundEngine.PostEvent()
```
See `CustomSoundsGuide.md` for the complete analysis.

### 5.4 Il2Cpp / MelonLoader Notes
- The game is compiled with Il2Cpp. MelonLoader's Il2CppInterop exposes private fields
  as properties (e.g., `template.Template.AudioId` works despite `AudioId` being a field
  on the `CardTemplate` struct).
- Harmony patches work on Il2Cpp methods but require the `Il2Cpp` prefixed type names
  in `using` statements.
- `CardDefinition` is a struct (value type). Harmony `ref` parameters work for modifying it.

### 5.5 Key Enums
```
ECardAudioTriggerType: Invalid=0, AmbushCardRevealed=2, CardPlacedOnBoard=3, PremiumCardPreview=6
ECardSoundEffectType: None=0, Standard=1, Premium=2, Ambush=3
EPremiumMode: Disabled=0, Enabled=1, ...
```

---

## 6. COMPLETED WORK LOG

### Step 1: Build Pipeline (DONE)
- Created `build.py` — single-command orchestration of the full build pipeline
- Created `AutoBuildOnLoad.cs` — file-watcher with background timer for Unity Editor
- Added `WatcherBuild1832()` to `BuildPremiumBundle.cs` — safe for running editor
- Moved source texture to `UnityAssets/Textures/1832.png`

### Audio Fix (DONE)
- Investigated Wwise audio pipeline end-to-end
- Fixed missing premium sound by setting `CardTemplate.AudioId` to donor's AudioId
- Documented full Wwise pipeline in `CustomSoundsGuide.md`

### Step 2: Dryad Ranger as Donor for Elven Deadeye (DONE)
- Switched donor from Wardancer (1222) to Dryad Ranger (1349) for both scene and audio
- `WatcherBuild1832()` now copies `13490101.unity` (Dryad Ranger) as donor scene
- `DonorArtId` changed to 1349 in `Core.cs` — premium sound now comes from Dryad Ranger
- Added `VFX/Common/AlphaBlended_TwoSided` (path_id `1572382250920393112`) to shader patcher
- Updated Hook 5 shader fixup for Dryad Ranger VFX materials (`leaf_all`, `leaf_movement`, `petals`)
- `build.py` now supports CLI args and multi-card builds (extensible for future cards)

### Commits
```
03884b6 Add premium sound fix and custom sounds guide
d6e1057 Add unified build pipeline (build.py)
6fc929f Add Claude Handoff and UnityScripts
ad8163c V5 working state
```
