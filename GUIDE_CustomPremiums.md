# Creating Custom Premium Cards for Gwent

This guide walks you through the entire process of adding a custom premium (animated) card to Gwent using the CustomPremiums mod. By the end, you'll have a card that was never premium by CDPR rendering with full 2.5D parallax animation in-game.

## Table of Contents

- [Overview](#overview)
- [What You Need](#what-you-need)
- [Understanding Premium Cards](#understanding-premium-cards)
- [Method 1: Clone an Existing Premium](#method-1-clone-an-existing-premium)
- [Method 2: Build from a WIP Prefab](#method-2-build-from-a-wip-prefab)
- [Method 3: Build from Scratch](#method-3-build-from-scratch)
- [The Build Pipeline](#the-build-pipeline)
- [Configuring the Source Project Path](#configuring-the-source-project-path)
- [Troubleshooting](#troubleshooting)

---

## Overview

Gwent's premium cards are 2.5D parallax dioramas: the card art is split into depth layers on separate meshes, animated with slight positional offsets to create a 3D effect when the card is tilted. Each premium card consists of:

- A **Unity scene** containing the 3D hierarchy (meshes, materials, animations, VFX)
- A **texture atlas** containing the parallax layers
- An **animation controller** driving the parallax breathing/sway
- Optional **VFX** (particles, mist, leaves, light flares)

All of this is packed into a Unity **AssetBundle** that the game loads at runtime.

The CustomPremiums mod intercepts the game's card loading pipeline (via Harmony hooks) and loads your custom AssetBundle instead of looking for one in the game's data. It also handles texture swapping, shader fixing, and audio redirection.

## What You Need

### Required

| Tool | What For | Where to Get |
|------|----------|-------------|
| **Gwent** (GOG) | The game itself | [GOG](https://www.gog.com/game/gwent_the_witcher_card_game) |
| **MelonLoader 0.7.x** | Mod framework | [GitHub](https://github.com/LavaGang/MelonLoader) |
| **.NET 6.0 SDK** | Building the C# mod | [Microsoft](https://dotnet.microsoft.com/download/dotnet/6.0) |
| **Python 3.10+** | Build pipeline + shader patcher | [python.org](https://www.python.org/downloads/) |
| **UnityPy** (Python package) | Shader patching reads/writes bundles | `pip install UnityPy` |

### Required for Building AssetBundles

| Tool | What For | Where to Get |
|------|----------|-------------|
| **Unity 2021.3.15f1** | Build AssetBundles (must be this exact version) | [Unity Archive](https://unity.com/releases/editor/archive) |
| **Gwent Source Project** | Contains all premium card assets, shaders, scripts | See [below](#configuring-the-source-project-path) |

> **Important:** The Gwent Unity source project is needed because it contains all the proprietary scripts (`PremiumCardsMeshMaterialHandler`, `CardAppearanceComponent`, etc.), shaders (GwentStandard), and existing premium card assets that we reference or clone. Without it, you can only use pre-built bundles that someone else has shared.

### Optional

| Tool | What For |
|------|----------|
| **Image editor** (Photoshop, GIMP, etc.) | Creating parallax layer textures |
| **AI image tools** (Photoshop Generative Fill, Runway, etc.) | Assisted layer separation and inpainting |

## Understanding Premium Cards

### How the Game Loads Premium Cards

1. Game determines a card should render as premium
2. Game requests the premium scene bundle (by ArtId) from StreamingAssets
3. Bundle is loaded, scene is instantiated into the card slot
4. `PremiumCardsMeshMaterialHandler` applies the atlas texture to all mesh materials
5. Animator starts playing (Intro animation, then Loop)
6. Card responds to tilt input via the RotationScript

### What Our Mod Does Differently

Our mod hijacks step 2-3: instead of loading from StreamingAssets, it loads **your** custom bundle from `Mods/CustomPremiums/Bundles/`. Everything else (texture assignment, animation, rendering) works the same because our bundle contains the same component types.

### Key Identifiers

Every card has these IDs (find them by searching the source project):

| ID | Example | Purpose |
|----|---------|---------|
| **ArtId** | 1832 | Identifies the card's art assets. This is what you put in the CARDS config |
| **TemplateId** | 202184 | Game's internal card template ID |
| **AudioId** | 1613 | Which audio bank to load for voicelines and SFX |

### CDPR Premium Scene Hierarchy

Every official premium card follows this structure:
```
{ArtId}00 (Root GameObject)
├── PremiumCardsMeshMaterialHandler  — assigns atlas texture to all materials
├── CardAppearanceComponent          — registers animators, renderers, particles
├── CameraSettings                   — fov, distance, clipping planes
│
└── Pivot (y=-2)
    ├── Animator (master controller)
    ├── RotationScript (tilt response: XRot -6..6, YRot -2..2)
    │
    ├── model / mesh layers           — the actual 3D content
    │
    └── VFX (optional)
        ├── LensPostFX
        ├── Particle systems (leaves, mist, sparks, etc.)
        └── Animated mesh overlays
```

---

## Method 1: Clone an Existing Premium

**Difficulty:** Easiest | **Result:** Card plays another card's premium animation with your texture

This is the simplest approach: take an existing card's premium scene and display it on a different card. The animation won't match the art, but it proves the pipeline works.

### Step 1: Choose a Donor Card

Pick any card that already has a premium animation. Good candidates:
- **Dryad Ranger** (ArtId 1349) — forest theme, leaves VFX
- **Milva** (ArtId 1191) — forest/action theme
- Any card with a premium scene in the source project

### Step 2: Configure `build.py`

```python
CARDS = {
    "1832": {          # Target: Elven Deadeye (the card getting premium)
        "donor": "1349",   # Donor: Dryad Ranger (provides animation + audio)
    },
}
```

### Step 3: Run the Pipeline

```bash
python build.py
```

This will:
1. Copy the donor's scene and build it as a bundle for your target ArtId
2. Patch shader references
3. Copy the donor's premium texture
4. Write `donor_config.json` so the mod knows which audio bank to load

### Step 4: Launch the Game

The target card should now render with the donor's premium animation.

---

## Method 2: Build from a WIP Prefab

**Difficulty:** Medium | **Result:** Card plays a WIP animation that CDPR started but never finished

The Gwent source project contains WIP (Work In Progress) premium cards that CDPR started but never shipped. These have all the raw assets (FBX mesh, materials, animation controllers) but no finalized scene file.

### Step 1: Find WIP Prefabs

Look in the source project under:
```
Assets/PremiumCards/WIP/_Prefabs_WIP/
```

Example: `[]Mimikr.prefab` — a complete WIP prefab with mesh, materials, and animations.

### Step 2: Configure `build.py`

```python
CARDS = {
    "1832": {
        "prefab": "Assets/PremiumCards/WIP/_Prefabs_WIP/[]Mimikr.prefab",
        "texture": "Assets/PremiumCards/WIP/_Textures_WIP/_Premium/_Uber/[]Mimikr_Atlas.png",
        "donor": "1349",  # optional: provides premium SFX audio
    },
}
```

### Step 3: Run the Pipeline

```bash
python build.py
```

The pipeline creates a temporary scene from the prefab, builds the bundle, and cleans up.

---

## Method 3: Build from Scratch

**Difficulty:** Hardest | **Result:** Fully custom premium animation unique to your card

This creates a premium card entirely from scratch — custom parallax layers, custom meshes, custom animations.

### Step 1: Create Parallax Layers

Split the card art into depth layers. For a forest archer card like Elven Deadeye, a typical split:

| Layer | Content | Z-Depth |
|-------|---------|---------|
| `far_background` | Forest, sky, distant trees | z=+10 (farthest) |
| `tree_trunk` | Large foreground tree | z=+3 |
| `elf` | The main character | z=0 (center) |
| `log` | Foreground elements | z=-5 (closest) |

**How to create each layer:**
1. Start with the original card art
2. Use an image editor to isolate each element
3. Remove the other elements and inpaint the gaps (AI tools like Photoshop Generative Fill work well for this)
4. Export each layer as a transparent PNG

### Step 2: Create the Atlas Texture

Composite all layers into a single texture atlas (2x2 grid):
```
+-------------------+-------------------+
|  far_background   |   tree_trunk      |
|  (top-left)       |   (top-right)     |
+-------------------+-------------------+
|  elf              |   log             |
|  (bottom-left)    |   (bottom-right)  |
+-------------------+-------------------+
```

Recommended resolution: 2048x2048 or 4096x4096. Save as PNG.

### Step 3: Configure `build.py`

```python
CARDS = {
    "1832": {
        "scratch": True,
        "donor": "1349",  # optional: provides premium SFX audio
    },
}
```

### Step 4: Customize the Prefab Generator

The script `UnityScripts/CreateElvenDeadeyePrefab.cs` generates:
- 4 quad meshes with UV coordinates mapped to each atlas quadrant
- A GwentStandard transparent material
- An animation controller with Intro and Loop clips
- The full prefab hierarchy with all required CDPR components

Modify this script to adjust:
- **Layer Z-depths** — how far apart the layers are (affects parallax strength)
- **Animation curves** — the sinusoidal sway patterns for each layer
- **Quad dimensions** — mesh size (default: 14x26 units matching CDPR convention)

### Step 5: Run the Pipeline

```bash
python build.py
```

The pipeline calls the prefab generator in Unity, builds the bundle, patches shaders, and deploys.

### Step 6: Iterate

The animation likely won't look perfect on the first try. Adjust the atlas, tweak layer depths and animation curves, rebuild, and test. The pipeline handles everything — just run `python build.py` again after making changes.

---

## The Build Pipeline

### Pipeline Steps in Detail

```
python build.py
```

| Step | What It Does | Time |
|------|-------------|------|
| **1. Sync scripts** | Copies `UnityScripts/*.cs` to `{SourceProject}/Assets/Editor/`. If scripts changed, waits for Unity to recompile | ~2-10s |
| **2. Build bundle** | Writes `build_trigger.txt` with the build config. Unity's file-watcher picks it up and builds the AssetBundle | ~5-30s |
| **3. Patch shaders** | Scans game's shader bundles (`shaderlibrary`, `shaders`) and rewrites the custom bundle's shader references to point to them | ~1s |
| **4. Build mod** | Runs `dotnet build` on `CustomPremiums.csproj` | ~2s |
| **5. Deploy** | Copies texture to `Mods/CustomPremiums/Textures/`, writes `donor_config.json` | instant |

### Trigger File Protocol

The build pipeline communicates with the Unity Editor via files:

1. `build.py` writes `build_trigger.txt` with content like `1832:1349` or `1832:prefab:path` or `1832:scratch`
2. `AutoBuildOnLoad.cs` (running inside Unity) polls for this file every second
3. Unity builds the bundle and writes `build_result.txt` with `OK` or `FAIL: error message`
4. `build.py` reads the result and continues or aborts

### Shader Patching Explained

**The problem:** Gwent's shaders live in separate AssetBundles (`shaderlibrary`, `shaders`). When you build a bundle in Unity, it embeds its own copy of the shaders — but these copies have different internal IDs than the game's. Result: pink/broken materials at runtime.

**The solution:** `patch_bundle_shaders.py` reads the game's shader bundles to build a mapping of `shaderName -> (CAB hash, pathID)`, then rewrites the custom bundle to reference those instead of its embedded copies. Unknown shaders automatically fall back to GwentStandard.

### Adding Audio

By default, the `donor` field in the CARDS config maps to a donor card whose AudioId is used for premium SFX (the whoosh/sparkle sounds that play during the premium intro animation). Voicelines are automatically redirected back to the original card via Hook 6.

If no donor is specified, the card uses its own AudioId (which typically doesn't have premium SFX).

For creating fully custom audio, see `CustomSoundsGuide.md`.

---

## Preparing the Source Project

The Gwent source project cannot be opened directly in Unity — it references internal CDPR build systems, thousands of gameplay scripts with missing dependencies, and packages from CDPR's private registry. You need to strip it down to just the premium card assets.

### Automated Preparation (Recommended)

Extract the source archive (`.7z`) to a directory, then run:

```bash
python prepare_source.py D:\Gwent_Source_Code
```

This script automatically performs all the steps described below — removes ~8,000 C# files, creates dummy script/shader stubs, and updates the package manifest. After it finishes, skip to [Step 6: Open in Unity](#step-6-open-in-unity-2021315f1).

### Manual Preparation

If you prefer to do it manually, follow these steps:

### Step 1: Extract a Clean Copy

Extract the source project to a dedicated directory (e.g., `D:\Gwent_Source_Code\`). The Unity project root is at:
```
Gwent\Gwent\GwentUnity\Gwent\
```

### Step 2: Remove All C# Code

The project has ~8,000+ `.cs` files that won't compile (they depend on server SDKs, internal tools, etc.). Move them **out of `Assets/`** so Unity ignores them.

Create a directory at the project root (next to `Assets/`) called `_ExcludedCode/`, then move the following:

| Original Location (in Assets/) | Move To | Notes |
|---------------------------------|---------|-------|
| `Code/` | `_ExcludedCode/Code_Ignored/` | Main game code (~1,500 files) |
| `Dependencies/` | `_ExcludedCode/Dependencies_Ignored/` | Third-party gameplay deps |
| `AutomatedTests/` | `_ExcludedCode/AutomatedTests_Ignored/` | Test code |
| `ExternalDependencyManager/` | `_ExcludedCode/ExternalDependencyManager_Ignored/` | Plugin manager |
| `UnityTestTools/` | `_ExcludedCode/UnityTestTools_Ignored/` | Unity test framework scripts |
| `Editor Default Resources/` | `_ExcludedCode/Editor Default Resources_Ignored/` | Editor-only resources |
| `Firebase/` (entire dir) | `_ExcludedCode/Firebase/` | Firebase SDK |

Also move `.cs` files (but **not** art assets) from these directories:

| Directory | What to Move |
|-----------|-------------|
| `HTMLGenerator/` | All `.cs` files → `_ExcludedCode/Remaining/HTMLGenerator/` |
| `Rewired/` | All `.cs` files → `_ExcludedCode/Remaining/Rewired/` |
| `TextMesh Pro/` | All `.cs` files → `_ExcludedCode/Remaining/TextMesh Pro/` |
| `Wwise/` | All `.cs` files → `_ExcludedCode/Remaining/Wwise/` |
| `PremiumCards/*/Tools/` | All `.cs` files → `_ExcludedCode/Remaining/PremiumCards/` |

After this, `Assets/` should have **zero `.cs` files** (we'll add back the ones we need next).

> **Keep the directories** in Assets/ even if empty — they contain `.meta` files that Unity uses to track asset references.

### Step 3: Add Dummy Scripts

Premium card prefabs reference MonoBehaviour scripts like `PremiumCardsMeshMaterialHandler` and `RotationObjectController`. Without these scripts, Unity can't open the prefabs. Create minimal stubs that declare the same serialized fields so Unity can deserialize the prefab data.

> **Critical:** Each dummy script's `.meta` file must preserve the **original GUID** from the real source code. Without matching GUIDs, Unity won't recognize the scripts as the same types that prefabs reference. Copy the `.meta` files from the original locations listed below, or find the GUIDs in the original `Code/` directory.

| Dummy Script | Original Source Location | GUID |
|-------------|-------------------------|------|
| `PremiumCardsMeshMaterialHandler.cs` | `Code/Unity/VFXScripts/PremiumCardHelperTools/` | `25093b07d5588bc42961f82eada15aee` |
| `CardAppearance.cs` | `Code/Unity/3DCards/Appereance/` | `423746d7ed4188549bd8df49a6385e62` |
| `RotationObjectController.cs` | `Code/Unity/ObjectControllers/` | `2ffef72dce217c04e9d29ce88a30d1b9` |
| `CameraValuesChanger.cs` | `Code/Unity/PremiumCardTools/` | `779e3927c97531041bd10c039690ed52` |
| `CameraPositionUVRemap.cs` | `Code/Visuals/VFX/` | `798a883cc02a0c74e91521d9cd264ad6` |
| `PairTransforms.cs` | `Code/Visuals/VFX/EffectHelpers/` | `547ff85cc4e9c4f4687997c83077314b` |
| `PremiumData.cs` | `Code/Visuals/PremiumImporter/EditorOnly/` | `37fde64410b20f24db1e6c95e4fa9c3c` |

Create `Assets/DummyScripts/` with these files:

**PremiumCardsMeshMaterialHandler.cs** — the most critical one:
```csharp
using System;
using UnityEngine;
using GwentUnity;

namespace GwentVisuals
{
    public class ACardAppearanceRegistree : MonoBehaviour
    {
        [SerializeField] protected CardAppearance CardAppearance;
        public virtual void OnSerializeSetup(CardAppearance cardAppearance) { }
    }

    [Serializable]
    public class AMaterialAssigments
    {
        public Renderer[] Renderers;
        public int[] MaterialIndex;
        public Material Material;
        public string[] Assigments;
    }

    [Serializable]
    public class CardAppearanceMaterialAssigments : AMaterialAssigments
    {
        [SerializeField] private CardAppearance m_CardAppearance;
    }

    public class PremiumCardsMeshMaterialHandler : ACardAppearanceRegistree
    {
        public CardAppearanceMaterialAssigments[] PremiumTextureAssigments;
    }
}
```

**CardAppearance.cs**:
```csharp
using UnityEngine;
using System.Collections.Generic;

namespace GwentUnity
{
    public abstract class AAppearanceComponent : MonoBehaviour { }

    public class CardAppearance : MonoBehaviour
    {
        [SerializeField] private AAppearanceComponent[] m_ActiveComponents = null;
        [SerializeField] private List<Animator> m_AllAnimators = new List<Animator>();
        [SerializeField] private List<ParticleSystem> m_AllParticles = new List<ParticleSystem>();
        [SerializeField] private List<Renderer> m_AllRenderers = new List<Renderer>();
    }
}
```

**RotationObjectController.cs**:
```csharp
using UnityEngine;

namespace GwentUnity
{
    public class BaseObjectController : MonoBehaviour { }

    public class RotationObjectController : BaseObjectController
    {
        public float XRotationStart;
        public float YRotationStart;
        public float XRotationEnd;
        public float YRotationEnd;
    }
}
```

**CameraValuesChanger.cs**:
```csharp
using UnityEngine;

namespace GwentUnity
{
    public class CameraValuesChanger : AAppearanceComponent
    {
        public float fov;
        public float camDistance;
        public float nearClippingPlane = 5.0f;
        public float farClippingPlane = 270f;
    }
}
```

**CameraPositionUVRemap.cs**:
```csharp
using UnityEngine;
using GwentVisuals;

namespace GwentUnity
{
    public class CameraPositionUVRemap : ACardAppearanceRegistree
    {
        [SerializeField] private Vector2 m_TextureOffsetMinMaxX;
        [SerializeField] private Vector2 m_TextureOffsetMinMaxY;
        [SerializeField] private string m_MatPropertyName = "_MainTex";
        [SerializeField] private Renderer m_Renderer;
        [SerializeField] private int m_MaterialIndex = 0;
        [SerializeField] private bool m_InverseAngles;
        [SerializeField] private RotationObjectController m_ObjCtrl;
        [SerializeField] private Transform m_Pivot;
    }
}
```

**PairTransforms.cs**:
```csharp
using UnityEngine;

namespace GwentVisuals
{
    public class PairTransforms : MonoBehaviour
    {
        [Serializable]
        public class TransformPair
        {
            [SerializeField] public Transform Source;
            [SerializeField] public Transform Target;
        }
        public TransformPair[] Pair;
    }
}
```

**PremiumData.cs**:
```csharp
using UnityEngine;
using GwentUnity;

namespace GwentVisuals.PremiumImporter
{
    public class PremiumData : MonoBehaviour
    {
        public int VerificationStage = 0;
        public string CardId;
        public string CardName;
        public CardAppearance RuntimePrefab;
        public GameObject FbxAsset;
        public Transform FbxRigRoot;
        public Transform Pivot;
        public Transform VFXRoot;
        public Transform VFXBones;
        public Transform ModelRoot;
        public Transform MatAnimRoot;
        public Transform RigRoot;
        public Animator MasterAnimator;
        public Animator ModelAnimator;
    }
}
```

### Step 4: Add Dummy Shaders

Premium card materials reference Gwent's shaders, which also can't compile without the full engine. Create minimal stubs that declare all the same properties (so Unity preserves material data during builds).

Create `Assets/DummyShaders/` with:

**GwentStandard.shader** — the main card shader (must match the `Shader "ShaderLibrary/Generic/GwentStandard"` path):
```
Shader "ShaderLibrary/Generic/GwentStandard" {
    Properties {
        _MainTex ("Main Texture", 2D) = "white" {}
        _SecondTex ("Second Texture", 2D) = "white" {}
        _ThirdTex ("Third Texture", 2D) = "white" {}
        _FlowTex ("Flow Texture", 2D) = "white" {}
        _Mask ("Mask", 2D) = "white" {}
        _AlphaPremultiply ("Alpha Premultiply", Float) = 1
        _Brightness ("Brightness", Float) = 1
        _Color ("Color", Color) = (1, 1, 1, 1)
        _TintColor ("Tint Color", Color) = (1, 1, 1, 1)
        _Cutoff ("Cutoff", Float) = 0.5
        _Cull ("Cull", Float) = 2
        _ZWrite ("Z Write", Float) = 1
        _ZTest ("Z Test", Float) = 4
        _SrcBlend ("Src Blend", Float) = 1
        _DstBlend ("Dst Blend", Float) = 0
        _Mode ("Mode", Float) = 0
        // ... (see DummyShaders/GwentStandard.shader for full property list)
    }
    SubShader {
        Pass { SetTexture [_MainTex] { combine texture } }
    }
}
```

> The full shader file with all 60+ properties is in the repository at `Assets/DummyShaders/GwentStandard.shader`. Only the Properties block matters — it prevents Unity from stripping material data. The actual rendering is handled by `patch_bundle_shaders.py`, which rewrites shader references to point to the game's compiled shaders at runtime.

Also add dummy VFX shaders if your cards use VFX:
- `Dummy_VFX_Common_AdditiveAlpha.shader` (path: `VFX/Common/AdditiveAlpha`)
- `Dummy_VFX_Common_AlphaBlended.shader` (path: `VFX/Common/AlphaBlended`)
- `Dummy_VFX_Effects_FakePostEffect_Additive_Mask.shader` (path: `VFX/Effects/FakePostEffect/Additive_Mask`)

### Step 5: Update Package Manifest

Edit `Packages/manifest.json` to replace CDPR's internal packages with standard Unity packages. The original references `com.unity.package-manager-ui` and older preview packages that won't resolve.

Replace the `dependencies` block with:
```json
{
  "dependencies": {
    "com.unity.2d.sprite": "1.0.0",
    "com.unity.2d.tilemap": "1.0.0",
    "com.unity.editorcoroutines": "1.0.0",
    "com.unity.ide.rider": "3.0.16",
    "com.unity.ide.visualstudio": "2.0.16",
    "com.unity.ide.vscode": "1.2.5",
    "com.unity.test-framework": "1.1.31",
    "com.unity.timeline": "1.6.4",
    "com.unity.ugui": "1.0.0",
    "com.unity.modules.ai": "1.0.0",
    "com.unity.modules.animation": "1.0.0",
    "com.unity.modules.assetbundle": "1.0.0",
    "com.unity.modules.audio": "1.0.0",
    "com.unity.modules.cloth": "1.0.0",
    "com.unity.modules.director": "1.0.0",
    "com.unity.modules.imageconversion": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.particlesystem": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.physics2d": "1.0.0",
    "com.unity.modules.screencapture": "1.0.0",
    "com.unity.modules.terrain": "1.0.0",
    "com.unity.modules.terrainphysics": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0",
    "com.unity.modules.unityanalytics": "1.0.0",
    "com.unity.modules.unitywebrequest": "1.0.0",
    "com.unity.modules.unitywebrequestassetbundle": "1.0.0",
    "com.unity.modules.unitywebrequestaudio": "1.0.0",
    "com.unity.modules.unitywebrequesttexture": "1.0.0",
    "com.unity.modules.unitywebrequestwww": "1.0.0",
    "com.unity.modules.video": "1.0.0",
    "com.unity.modules.wind": "1.0.0"
  }
}
```

> **Important:** The original manifest references CDPR internal packages (`com.cdprojektred.gwent.dependencyseeker`, `com.cdprojektred.gwent.shaderlibrary`, etc.) from an internal registry at `http://192.168.105.13/`. These will fail to resolve. Remove them from the `dependencies` block and also remove the `"registry"` line. The dummy shaders replace their functionality for our purposes.

### Step 6: Open in Unity 2021.3.15f1

Open the project in **Unity 2021.3.15f1** (the exact version the shipped Gwent game uses).

> **Note:** The source project was originally created in **Unity 2018.4.2f1**. When you first open it in 2021.3, Unity will perform a one-time project upgrade — this is expected. It will reformat `.spriteatlas` files, add new ProjectSettings files, and generate an `UpgradeLog.htm`. Accept the upgrade prompts.

Unity will then:
1. Generate the `Library/` directory (takes a few minutes on first open)
2. Import all assets
3. Compile the dummy scripts + editor scripts

You may see warnings about missing scripts on some GameObjects — this is normal. The premium card prefabs should load correctly because the dummy scripts provide the required component types.

### Configuring the Build Pipeline Path

The build pipeline needs to know where your prepared source project is. Configure this in `build.py`:

```python
UNITY_PROJECT = r"D:\Gwent_Source_Code\Gwent\Gwent\GwentUnity\Gwent"
```

Update this path to wherever you have the source project.

The Unity Editor must be running with the project loaded for the build pipeline to work — it communicates with Unity via the file-watcher system.

### What's in the Source Project

The source project contains:
- **All premium card assets**: FBX meshes, materials, animation controllers, VFX prefabs, textures
- **Proprietary scripts**: `PremiumCardsMeshMaterialHandler`, `CardAppearanceComponent`, `CardAppearanceLensPostFX`, rotation scripts, camera scripts
- **Shaders**: GwentStandard and all card-related shaders
- **WIP assets**: Unreleased/unfinished premium cards that can be used as-is or as references

Key directories:
```
Assets/
├── Prefabs/PremiumCards/       # Finalized premium scene prefabs
├── PremiumCards/
│   ├── {Faction}/[{ArtId}]{Name}/  # Per-card assets (FBX, materials, VFX)
│   └── WIP/                   # Unreleased WIP premium cards
├── BundledAssets/CardAssets/
│   ├── Scenes/                # Premium card scene files
│   └── Textures/              # Standard and premium card textures
└── VFX/Cards/Textures/        # Shared VFX textures (leaves, dust, sparks, etc.)
```

### Other Configurable Paths

All paths are at the top of `build.py`:

```python
GWENTMODS_DIR = r"E:\Projekty\GwentMods"           # This repository
UNITY_PROJECT = r"D:\Gwent_Source_Code\...\Gwent"   # Source project root
GAME_DIR = r"E:\GOG Galaxy\Games\Gwent"             # Game installation
```

### Updating .csproj Assembly References

The `.csproj` files contain **hardcoded absolute paths** to MelonLoader assemblies. These are **not** handled by `build.py` — you must update them manually if your Gwent installation is in a different location.

Each `.csproj` has `<HintPath>` entries pointing to:
```
{GameDir}\MelonLoader\net6\          — MelonLoader.dll, 0Harmony.dll, Il2CppInterop.*.dll
{GameDir}\MelonLoader\Il2CppAssemblies\  — Assembly-CSharp.dll, UnityEngine.*.dll, Il2Cpp*.dll
```

And a PostBuild target that copies the built DLL:
```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Exec Command="COPY &quot;$(TargetPath)&quot; &quot;{GameDir}\Mods&quot;" />
</Target>
```

To update for your installation, find-and-replace the game directory path across all `.csproj` files:
- `CustomPremiums/CustomPremiums.csproj`
- `Premiumify/Premiumify.csproj`
- `ModSettings/ModSettings.csproj`
- `ModSettingsTest/ModSettingsTest.csproj`

> **Note:** The `Il2CppAssemblies` directory is generated by MelonLoader on first launch. If it doesn't exist, run Gwent once with MelonLoader installed.

---

## Troubleshooting

### Pink/Magenta Materials

**Cause:** Shader references in the bundle don't match the game's shader bundles.

**Fix:** Make sure `patch_bundle_shaders.py` ran successfully. Check its output — it should report which shaders were patched. If a shader can't be found, it falls back to GwentStandard.

### Card Shows Standard Art (Not Premium)

**Cause:** The mod isn't detecting your card or the bundle failed to load.

**Fix:** Check the MelonLoader log (`Gwent/MelonLoader/Latest.log`) for `[CustomPremiums]` messages. Look for:
- `[Scan]` messages — confirms your bundle and texture were found
- `[Init]` messages — confirms the ArtId was mapped to a TemplateId
- `[HOOK 4]` messages — confirms the bundle was loaded

### Unity Build Times Out

**Cause:** Unity didn't pick up the trigger file.

**Fix:**
- Make sure Unity Editor is running and focused (click on it)
- Check that `AutoBuildOnLoad.cs` is in `Assets/Editor/` and compiled
- Look at Unity's Console for `[BuildWatcher]` messages

### AssignTexture NullReferenceException

**Cause:** Known issue with prefab-based builds where `GetMaterialCopy()` lacks renderer context.

**Fix:** Already handled in Hook 5 — it catches this exception and falls back to `material.mainTexture = customTex`. If you still see this error, it's benign.

### Bundle Name Collision

**Cause:** Unity caches AssetBundle names internally. If you rebuild, the old name might still be in use.

**Fix:** The pipeline's Hook 4 already unloads bundles after use (`bundle.Unload(false)`). If you hit this, restart the game.

### compare_bundles.py

Use this tool to compare your custom bundle against an official one:
```bash
python compare_bundles.py path/to/game_bundle path/to/custom_bundle
```

It shows materials, shaders, textures, GameObjects, and MonoBehaviours — useful for spotting differences.
