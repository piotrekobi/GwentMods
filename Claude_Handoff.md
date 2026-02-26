# GWENT MODDING PROJECT: COMPLETE CONTEXT AND HANDOFF DOCUMENT
**Target Agent:** Claude Code
**Current State:** Step 3b — Build Elven Deadeye Premium Card From Scratch

Hello Claude! You are being handed a modding project for the game **Gwent: The Witcher Card Game**. We have built a fully generic, config-driven pipeline to inject custom Premium (animated) cards into the retail game.

This document is your source of truth. Read it entirely before executing any actions.

---

## 1. THE ULTIMATE OBJECTIVE
We are creating **Custom Premium Cards** from scratch. Our current focus is "Tokens" or "Spawned" cards that CD Projekt Red (CDPR) never provided premium animations for.

The long-term target is the **Elven Deadeye (ArtId: 1832)** — a token card that was never made premium by CDPR.

The plan has 4 steps:
1. **DONE** — Build automation pipeline (`build.py`) — fully generic, config-driven
2. **DONE** — Test pipeline with multiple donor cards (Dryad Ranger, Milva, Siren Human Form, Falbeson)
3. **DONE (3a)** — Extend pipeline to build scenes from WIP prefabs (Mimikr — tested end-to-end, works!)
4. **NOW (3b)** — Build Elven Deadeye premium card entirely from scratch

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

## 4. CURRENT TASK: BUILD ELVEN DEADEYE PREMIUM FROM SCRATCH (Step 3b)

### 4.1 Goal
Create a fully custom premium (animated) card for **Elven Deadeye (ArtId 1832)** — a token card
that CDPR never made premium. This is the first card built entirely from scratch (not cloned or
borrowed from an existing donor). The result should look like an official CDPR premium card.

**Elven Deadeye Info:**
- **ArtId:** 1832 | **TemplateId:** 202184 | **AudioId:** 1613
- **Standard texture:** `Assets/BundledAssets/CardAssets/Textures/Standard/Uber/18320000.png`
- **No premium assets exist in the project** — no FBX, no materials, no animation, no scene

**Design direction:** Heavily inspired by the Dryad Ranger premium (similar forest archer theme).
The user is not a professional artist — use AI tools for creating/modifying image assets. OK to
borrow VFX and supporting assets from existing cards in the Unity project.

### 4.2 Art Analysis — Elven Deadeye

The Elven Deadeye art (`arts/elven_deadeye_art.png`) shows:
- **An elven archer** in dark leather/red cloth armor, drawing a longbow
- Positioned behind a **large tree trunk** on the left side
- Standing on a **fallen log/mossy branch** in the foreground
- **Dense forest background** with trees, foliage, and dappled light
- Green/brown/red color palette, moody forest lighting

Compare with Dryad Ranger (`arts/dryad_ranger_art.png`):
- Green-skinned dryad with vine bow, similar forest setting
- Large tree trunk, mossy log foreground, leaves and petals flying
- Both cards share: forest theme, archer pose, tree trunk framing, log foreground

### 4.3 Parallax Layer Plan

Premium cards work by splitting the art into depth layers on separate mesh quads, then
animating them with slight positional offsets to create a parallax/3D effect when the card
is tilted. Each layer is a flat quad mesh at a different Z-depth.

**Proposed 4-layer split (back to front):**

| Layer | Content | Z-Depth | Parallax Amount |
|-------|---------|---------|-----------------|
| `far_background` | Forest trees, sky, distant foliage | z=+10 (farthest) | Most movement |
| `tree_trunk` | Large left tree trunk + branches | z=+3 | Medium movement |
| `elf` | The archer character (main subject) | z=0 (center) | Slight movement |
| `log` | Foreground fallen log + plants | z=-5 (closest) | Most movement (opposite direction) |

**How to create the layers:**
1. Use AI image editing (see Section 4.8) to isolate each layer from the original art
2. For each layer, remove the other elements and inpaint the revealed areas
3. Each layer image should be the full card dimensions with transparent regions
4. All layers are packed into a single **atlas texture** (see Section 4.5)

### 4.4 Required Assets to Create

All custom assets go in: `Assets/PremiumCards/Custom/ElvenDeadeye/`

| Asset | Type | How to Create |
|-------|------|---------------|
| **Atlas texture** (`ElvenDeadeye_Atlas.png`) | PNG | AI-assisted layer separation + atlas packing |
| **4 mesh quads** (`.asset` files) | Unity Mesh | Script-generated quads with per-layer UVs |
| **Atlas material** (`ElvenDeadeye-Atlas.mat`) | Material | GwentStandard shader, transparent mode |
| **Animation controller** (`ElvenDeadeye_AC.controller`) | Controller | Intro → Loop state machine |
| **Intro animation** (`ElvenDeadeye_Intro.anim`) | AnimClip | Position keyframes for parallax entrance |
| **Loop animation** (`ElvenDeadeye_Loop.anim`) | AnimClip | Subtle breathing/sway position curves |
| **Prefab** (`ElvenDeadeye.prefab`) | Prefab | Assembled from above, with all components |

**Optional VFX assets (borrow from existing cards):**

| VFX Element | Source | Purpose |
|-------------|--------|---------|
| Leaf particles | Dryad Ranger VFX | Floating leaves in forest |
| Mist/fog | Dryad Ranger VFX | Atmospheric depth |
| Light flare | Dryad Ranger VFX | Dappled sunlight |
| LensPostFX | Dryad Ranger VFX | Parallax lens distortion overlay |
| Leaf movement meshes | Dryad Ranger VFX | Animated leaf overlays |

### 4.5 Atlas Texture Layout

The atlas is a single large PNG containing all 4 parallax layers tiled. Each mesh quad's UVs
point to its region of the atlas. A typical layout (2048x2048 or 4096x4096):

```
+-------------------+-------------------+
|                   |                   |
|  far_background   |   tree_trunk      |
|  (top-left)       |   (top-right)     |
|                   |                   |
+-------------------+-------------------+
|                   |                   |
|  elf              |   log             |
|  (bottom-left)    |   (bottom-right)  |
|                   |                   |
+-------------------+-------------------+
```

Each quadrant is ~1024x1024 (if atlas is 2048x2048) or ~2048x2048 (if atlas is 4096x4096).
The mesh quads' UV coordinates map to their respective quadrant.

### 4.6 Dryad Ranger Premium — Complete Reference Structure

The Dryad Ranger (ArtId 1349) is the primary reference. Its premium prefab lives at:
`Assets/Prefabs/PremiumCards/13490101.prefab` (37,081 lines, 881 KB)

**Full hierarchy:**
```
13490100 (Root GameObject)
├── Components: CardAppearanceComponent, PremiumCardsMeshMaterialHandler, CameraSettings
│   PremiumCardsMeshMaterialHandler:
│     PremiumTextureAssigments: [{Renderer: MainMesh/SkinnedMeshRenderer, Material: Atlas, Slot: _MainTex}]
│   CameraSettings: fov=25, camDistance=-29.87, near=20, far=110
│
└── Pivot (y=-2)
    ├── Components: Animator (AC_master controller), RotationScript (XRot -6..6, YRot -2..2)
    │
    ├── model
    │   ├── Components: Animator (AC_model controller, uses FBX Avatar)
    │   └── MainMesh
    │       └── SkinnedMeshRenderer (material: DryadRanger-Atlas.mat, mesh: DryadRanger.fbx)
    │
    ├── VFX
    │   ├── DryadRanger_LensPostFX      [MeshFilter(Quad) + MeshRenderer(LensPostFX.mat) + CardAppearanceLensPostFX script]
    │   ├── DryadRanger_petals_flowers   [ParticleSystem + ParticleSystemRenderer]
    │   ├── DryadRanger_leaf_bg          [ParticleSystem + ParticleSystemRenderer]
    │   ├── DryadRanger_flare_background [ParticleSystem + ParticleSystemRenderer]
    │   ├── DryadRanger_leaf_movement1   [MeshFilter(Quad) + MeshRenderer(leaf_movement.mat), scale=10.6x]
    │   ├── DryadRanger_leaf_movement2   [MeshFilter(Quad) + MeshRenderer(leaf_movement.mat), scale=10.6x]
    │   ├── DryadRanger_leaf_intro_position (group, pos: 0, 0.5, 10)
    │   │   ├── DryadRanger_leaf_introBG         [ParticleSystem]
    │   │   ├── DryadRanger_petals_flowers_introBG [ParticleSystem]
    │   │   └── DryadRanger_petals_flowers_fg      [ParticleSystem]
    │   ├── DryadRanger_mist_bg          [ParticleSystem + ParticleSystemRenderer]
    │   └── DryadRanger_mist_med         [ParticleSystem + ParticleSystemRenderer]
    │
    └── matanim (empty transform, animation target)
```

**Key components on root (13490100):**
- `PremiumCardsMeshMaterialHandler` (GUID: `25093b07d5588bc42961f82eada15aee`) — assigns atlas texture
- `CardAppearanceComponent` (GUID: `423746d7ed4188549bd8df49a6385e62`) — registers animators, particles, renderers
- Camera settings script (GUID: `779e3927c97531041bd10c039690ed52`) — fov, distance, clipping planes

**Key components on Pivot:**
- `Animator` with `AC_master` controller — drives overall scene animation
- Rotation script (GUID: `2ffef72dce217c04e9d29ce88a30d1b9`) — XRot -6..6, YRot -2..2

**Dryad Ranger source assets at:**
`Assets/PremiumCards/Scoiatael/[13490300]DryadRanger/`
```
├── [13490300]DryadRanger.fbx              (5.5 MB — the main mesh)
├── [13490100]DryadRanger_AC_master.controller  (Intro → Loop, controls Pivot)
├── [13490100]DryadRanger_AC_model.controller   (Intro → Loop, controls model/mesh deformation)
├── 13490300_editor.prefab                 (editor-only prefab variant)
├── Materials/
│   └── [13490300]DryadRanger-Atlas.mat    (GwentStandard, transparent, _Mode=3)
└── VFX/
    ├── Animator_Controllers/
    │   ├── VFXIntro.anim
    │   └── VFXLoop.anim
    ├── Materials/
    │   ├── DryadRanger_LensPostFX.mat     (shader GUID: fc07c209..., render queue 3000)
    │   ├── DryadRanger_leaf_all.mat       (shader GUID: c620303c..., render queue 3100)
    │   ├── DryadRanger_leaf_movement.mat  (shader GUID: dba6637c..., render queue 3190)
    │   ├── DryadRanger_petals_flowers.mat (shader GUID: dba6637c..., render queue 3150)
    │   └── DryadRanger_petals_flowers_fg.mat (shader GUID: dba6637c..., render queue 3200)
    └── Textures/
        ├── DryadRanger_leaf_all.png
        ├── DryadRanger_leaf_movement.png
        ├── DryadRanger_petals_flowers.png
        └── DryadRanger_petals_flowers_fg.png
```

**Dryad Ranger Atlas material settings (GwentStandard):**
```yaml
m_Shader: {fileID: 4800000, guid: 24220d20ad4c4754fb208a4668c20708, type: 3}  # GwentStandard
m_ShaderKeywords: _ALPHAPREMULTIPLY_ON
m_CustomRenderQueue: -1
_Mode: 3          # Transparent
_DstBlend: 10     # OneMinusSrcAlpha
_SrcBlend: 1      # One
_ZWrite: 0        # Off (for transparency)
_Glossiness: 0
_Metallic: 0
```

### 4.7 Creating the Elven Deadeye Prefab — Step by Step

This is the core creative work. The approach follows exactly what CDPR did for their WIP cards.

**PHASE 1: Create the Atlas Texture**

1. Start with the standard art: `arts/elven_deadeye_art.png`
2. Use AI image editing to separate into 4 layers (see Section 4.8 for tools):
   - **far_background**: Erase the elf, tree trunk, and log. Inpaint the gaps with forest/foliage
   - **tree_trunk**: Isolate the large left tree. Erase everything else. Transparent background
   - **elf**: Isolate the archer character. Erase everything else. Transparent background
   - **log**: Isolate the foreground fallen log + plants. Erase everything else. Transparent background
3. Composite all 4 layers into a 2x2 atlas grid (2048x2048 or 4096x4096 PNG)
4. Save as `ElvenDeadeye_Atlas.png` in the asset folder

**PHASE 2: Create Mesh Quads in Unity**

Each parallax layer needs a flat quad mesh with UVs pointing to its atlas region.
Write a Unity Editor script (`CreateElvenDeadeyeMeshes.cs`) that generates 4 `.asset` files:

```csharp
// For each layer, create a Mesh with 4 vertices (quad) and set UVs to the atlas quadrant:
// far_background: UV (0, 0.5) to (0.5, 1)     — top-left
// tree_trunk:     UV (0.5, 0.5) to (1, 1)      — top-right
// elf:            UV (0, 0) to (0.5, 0.5)       — bottom-left
// log:            UV (0.5, 0) to (1, 0.5)       — bottom-right
```

Quad size should be ~14x26 units (matching CDPR convention: extent {x:7, y:13}).

**PHASE 3: Create Materials**

Create `ElvenDeadeye-Atlas.mat`:
- Shader: GwentStandard (GUID: `24220d20ad4c4754fb208a4668c20708`)
- Mode: Transparent (`_Mode: 3`, `_DstBlend: 10`, `_SrcBlend: 1`, `_ZWrite: 0`)
- Keywords: `_ALPHAPREMULTIPLY_ON`
- `_MainTex`: Leave empty (assigned at runtime by PremiumCardsMeshMaterialHandler)
- Render queue: -1 (default)

For VFX materials, copy from Dryad Ranger's VFX/Materials/ and rename.

**PHASE 4: Create Animation Controller + Clips**

Create `ElvenDeadeye_AC.controller` — same structure as Dryad Ranger's AC_master:
- State machine: Entry → Intro → Loop (auto-transition after Intro plays)
- Intro clip: ~1 second, layers slide into position from offset
- Loop clip: ~5-10 seconds looping, subtle position sway for parallax breathing

Animation curves target the position of each layer's Transform:
```
// Example Loop.anim curves (subtle back-and-forth sway):
far_background:  localPosition.x oscillates ±0.3 over ~8s
tree_trunk:      localPosition.x oscillates ±0.15 over ~6s
elf:             localPosition.x oscillates ±0.05 over ~10s (barely moves)
log:             localPosition.x oscillates ±0.2 over ~7s (opposite phase)
```

**PHASE 5: Assemble the Prefab**

Build the prefab with this hierarchy (mirroring Dryad Ranger's structure):
```
ElvenDeadeye (Root)
├── Components:
│   ├── PremiumCardsMeshMaterialHandler
│   │     PremiumTextureAssigments: [
│   │       {Renderer: <each layer's MeshRenderer>, Material: Atlas.mat, Slot: _MainTex}
│   │     ]
│   ├── CardAppearanceComponent (registers all animators, renderers, particles)
│   └── Camera settings (fov=25, camDistance=-30, near=20, far=110)
│
└── Pivot (localPosition: 0, -2, 0)
    ├── Animator (ElvenDeadeye_AC controller)
    ├── RotationScript (XRot -6..6, YRot -2..2)
    │
    ├── far_background (z=+10)
    │   └── MeshFilter (far_background.asset) + MeshRenderer (Atlas.mat)
    ├── tree_trunk (z=+3)
    │   └── MeshFilter (tree_trunk.asset) + MeshRenderer (Atlas.mat)
    ├── elf (z=0)
    │   └── MeshFilter (elf.asset) + MeshRenderer (Atlas.mat)
    ├── log (z=-5)
    │   └── MeshFilter (log.asset) + MeshRenderer (Atlas.mat)
    │
    └── VFX (optional, borrowed from Dryad Ranger — see Section 4.9)
        ├── LensPostFX
        ├── leaf_particles
        ├── mist
        └── flare
```

**IMPORTANT:** Unlike Dryad Ranger which uses a single SkinnedMeshRenderer + FBX for all
layers, our approach uses separate MeshRenderer quads per layer. This is simpler to create
without professional 3D modeling tools. The PremiumCardsMeshMaterialHandler needs to have
ALL layer renderers in its PremiumTextureAssigments array so the atlas texture gets applied
to each one.

**PHASE 6: Configure Pipeline and Build**

Update `build.py` CARDS config:
```python
CARDS = {
    "1832": {
        "prefab": "Assets/PremiumCards/Custom/ElvenDeadeye/ElvenDeadeye.prefab",
        "texture": "Assets/PremiumCards/Custom/ElvenDeadeye/Textures/ElvenDeadeye_Atlas.png",
        "donor": "1349",  # Dryad Ranger audio for premium SFX
    },
}
```

Then run `python build.py` — the pipeline handles everything else automatically.

### 4.8 AI Tools for Asset Creation

The user is not a professional artist. These AI tools can help create the parallax layers:

**For layer separation (removing/inpainting elements from the art):**
- **Adobe Firefly / Photoshop Generative Fill** — best for precise object removal + inpainting
- **Runway ML (Inpainting)** — web-based, good at filling removed areas with context-aware content
- **DALL-E 3 (via ChatGPT)** — can edit/extend existing images with natural language prompts
- **Stable Diffusion + ControlNet (Inpainting model)** — local, most control, needs setup
- **ClipDrop / Cleanup.pictures** — simple web tools for object removal

**For texture creation/upscaling:**
- **Magnific AI** — AI upscaler that adds detail (good for upscaling layer crops)
- **Topaz Gigapixel AI** — offline upscaler
- **ESRGAN / Real-ESRGAN** — free, local, high-quality upscaling

**For 3D mesh generation (alternative to manual quad creation):**
- **Nanobanana (nano banana)** — can generate simple 3D meshes from images
- **Meshy.ai** — image-to-3D, could generate a relief mesh from the card art
- **Tripo3D** — fast image-to-3D generation

**Recommended workflow:**
1. Open `elven_deadeye_art.png` in Photoshop or GIMP
2. Use AI-powered selection tools to isolate each layer
3. For each layer, mask out everything else and use generative fill to inpaint gaps
4. Export each layer as a transparent PNG at the target atlas quadrant resolution
5. Composite all 4 into the final atlas grid using any image editor

### 4.9 Reusable VFX Assets from Existing Cards

These VFX materials and textures from the Dryad Ranger can be directly reused or slightly modified:

**Dryad Ranger VFX materials (copy these to your asset folder):**

| Material | Shader GUID | Render Queue | Texture | Use For |
|----------|-------------|--------------|---------|---------|
| `DryadRanger_leaf_all.mat` | `c620303c...` | 3100 | `DryadRanger_leaf_all.png` | Floating leaf particles |
| `DryadRanger_leaf_movement.mat` | `dba6637c...` | 3190 | `DryadRanger_leaf_movement.png` | Animated leaf overlay quads |
| `DryadRanger_petals_flowers.mat` | `dba6637c...` | 3150 | `DryadRanger_petals_flowers.png` | Floating petal particles |
| `DryadRanger_petals_flowers_fg.mat` | `dba6637c...` | 3200 | `DryadRanger_petals_flowers_fg.png` | Foreground petal particles |
| `DryadRanger_LensPostFX.mat` | `fc07c209...` | 3000 | Lens texture + mask | Lens distortion overlay |

**VFX texture library (available for any card):**
```
Assets/VFX/Cards/Textures/
├── Plants/           — 64+ leaf/vine textures (green, red, brown, all seasons)
│   ├── leaf_all_green.png, leaf_all_green2.png, leaf_all_orange.png
│   ├── leaf_movement_green.png, leaf_movement_red.png
│   ├── vine_green.png, moss_particles.png
│   └── ...
├── DustAndRocks/     — 40+ dust/debris particle textures
├── Glow/             — 30+ glow/aura textures
├── Sparks/           — 40+ spark/ember textures
├── Smoke/            — 60+ smoke/mist/fog textures
├── Insects/          — butterflies, fireflies, dragonflies
├── LightStreaks/     — light shaft textures (lightshafts.png)
├── Flares/           — bokeh, lens flares
└── Water/            — water surface, ripple, splash textures
```

**Recommended VFX for Elven Deadeye (forest archer theme):**
- Green/brown leaf particles (from `Plants/leaf_all_green.png` or Dryad Ranger's)
- Forest mist (from `Smoke/` or Dryad Ranger's mist materials)
- Dappled light flare (from Dryad Ranger's `flare_background` or `LightStreaks/`)
- LensPostFX overlay (copy Dryad Ranger's setup — it adds depth to any forest scene)
- Optional: fireflies or dust motes for ambient life

You can reference VFX textures directly from `Assets/VFX/Cards/Textures/` in your materials —
they'll be included in the AssetBundle automatically by Unity's dependency resolution.

### 4.10 Mimikr as a Working Reference

The Mimikr WIP card was successfully built and rendered in-game. Its structure is simpler than
Dryad Ranger (no VFX particles) and serves as a proven minimal working example:

**Mimikr prefab hierarchy** (`Assets/PremiumCards/WIP/_Prefabs_WIP/[]Mimikr.prefab`):
```
[]Mimikr (Root)
├── PremiumCardsMeshMaterialHandler (4 material assignments, _MainTex slot)
├── CardAppearanceComponent
├── Camera settings
│
└── Pivot (y=-2)
    ├── Animator ([]Mimikr_AC controller, uses FBX Avatar)
    ├── RotationScript (XRot -6..6, YRot -2..2)
    │
    ├── _1_mesh (SkinnedMeshRenderer with 4 materials, FBX mesh)
    ├── TreeCreature1_Rig (bone hierarchy)
    └── TreeCreature1_Rig_ROOTSHJnt (root bone)
```

**Key differences from our Elven Deadeye approach:**
- Mimikr uses SkinnedMeshRenderer + FBX (single mesh with bone deformation)
- Our Elven Deadeye uses multiple MeshRenderer quads (one per parallax layer)
- Both need PremiumCardsMeshMaterialHandler with correct texture assignments
- Both use the same pipeline: prefab → scene → bundle → shader patch → deploy

**Mimikr build config (currently in build.py):**
```python
CARDS = {
    "1832": {
        "prefab": "Assets/PremiumCards/WIP/_Prefabs_WIP/[]Mimikr.prefab",
        "texture": "Assets/PremiumCards/WIP/_Textures_WIP/_Premium/_Uber/[]Mimikr_Atlas.png",
        "donor": "1349",
    },
}
```

### 4.11 Implementation Approach — Summary

**Option A: Script-Generated Prefab (Recommended)**
Write a Unity Editor script (`CreateElvenDeadeyePrefab.cs`) that:
1. Creates mesh quad `.asset` files with correct UVs for each layer
2. Creates the material (GwentStandard, transparent)
3. Creates animation controller + clips programmatically
4. Assembles the full prefab hierarchy with all components
5. Saves everything to `Assets/PremiumCards/Custom/ElvenDeadeye/`

This is the most reproducible approach — run the script, get the prefab, build with pipeline.

**Option B: Manual Assembly in Unity Editor**
1. Create assets individually (meshes via script, materials manually, animations in Animation window)
2. Build the hierarchy manually in the Scene view
3. Save as prefab
4. Less automated but allows visual tweaking

**Either way, the final step is the same:**
1. Update `build.py` CARDS config to point to the new prefab
2. Run `python build.py`
3. Launch game and verify

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
- Tested end-to-end with Mimikr WIP card: renders correctly in-game on Elven Deadeye card slot
- Fixed `AssignTexture` NullReferenceException in Hook 5: added try/catch with direct `material.mainTexture` fallback
  (the `GetMaterialCopy()` call needs renderer context that isn't available on freshly-loaded prefab instances)
- Materials: `mat_1_1` uses GwentStandard, `mat_1_2-1_4` use Unity Standard (auto-patched to GwentStandard)

### Donor Testing (DONE)
- Dryad Ranger (1349): Full audio + rendering
- Milva (1191): Full audio + rendering
- Siren Human Form (1415): Rendering works (shader fallback needed for 2 VFX materials), no audio (unreleased)
- Falbeson (1542): Clean rendering (all shaders in game bundles), no audio (unreleased)

### Commits
```
af39a11 Add prefab-to-scene pipeline and fix texture assignment
cc6adb0 Add shader fallback for unknown shaders and test multiple donors
7ae2365 Make pipeline fully generic and config-driven
244294c Update handoff document for Step 2 (Dryad Ranger clone)
03884b6 Add premium sound fix and custom sounds guide
d6e1057 Add unified build pipeline (build.py)
6fc929f Add Claude Handoff and UnityScripts
ad8163c V5 working state
```
