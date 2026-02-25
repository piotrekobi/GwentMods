# GWENT MODDING PROJECT: COMPLETE CONTEXT AND HANDOFF DOCUMENT
**Target Agent:** Claude Code
**Current State:** Advanced Infrastructure Complete, Moving to Asset Creation

Hello Claude! You are being handed a highly complex and technical modding project for the game **Gwent: The Witcher Card Game**. We have successfully reverse-engineered, circumvented, and built a custom pipeline to inject completely new, community-made Premium (Animated) cards into the retail game.

This document serves as your ultimate source of truth. It details the exact history of our efforts, the critical Unity AssetBundle roadblocks we conquered, the Python/C# infrastructure we built, and the exact next steps required for our current task. 

Please read this entirely before executing any actions.

---

## 1. THE ULTIMATE OBJECTIVE
We are creating **Custom Premium Cards** from scratch. Specifically, our focus is on "Tokens" or "Spawned" cards that CD Projekt Red (CDPR) never provided premium animations for. 
Our current target is the **Elven Deadeye (ArtId: 1832)**. 

Since CDPR never modeled or animated this token, we must build a 2.5D parallax diorama in Unity, compile it into an AssetBundle, patch the bundle's bytecode to link against the retail game's proprietary shaders, and inject it via a C# mod runtime hook.

---

## 2. THE WORKING ENVIRONMENT
- **Game Engine:** Unity 2021.3.15f1 (CRITICAL: AssetBundles must be built with this exact version or the retail game will reject them).
- **Source Code:** We have a leaked/archived version of the Gwent Unity source project located at `D:\Gwent_Source_Code\Gwent\Gwent\GwentUnity\Gwent\`. We use this exclusively as our build environment.
- **Mod Environment:** `E:\Projekty\GwentMods\`. This directory contains our C# payload (`CustomPremiums` BepInEx/MelonLoader mod) and our Python build scripts.
- **Game Directory:** `E:\GOG Galaxy\Games\Gwent\`

---

## 3. THE JOURNEY: HOW WE GOT HERE

### Phase A: The Proof of Concept & The Texture Swap Failure
We initially created a C# mod (`CustomPremiums/Core.cs`) hooking into the game's `OnAppearanceObjectLoaded` and `HandleTextureRequestsFinished` methods. 
To test if the game would accept custom bundles, we:
1. Copied the fully animated scene of the **Elven Wardancer (ArtId 1222)** from the source code.
2. Renamed the scene and its AssetBundle tag to `1832` (Elven Deadeye).
3. Forced the C# mod to swap the main textures at runtime from Wardancer to Deadeye.

**The result:** The game loaded the 3D model, but the textures were twisted and horrifically distorted. 
**The discovery:** CDPR's premium models are heavily UV-mapped to strict "Texture Atlases" (like `12220100.png` which contains isolated arms, weapons, and torsos). You cannot simply paste a flat card art (`1832.png`) over a complex 3D mesh. 
**The conclusion:** We cannot blindly repurpose existing 3D models for new cards. We must build bespoke 2.5D scenes from scratch using flat planes (Quads).

### Phase B: The Catastrophic "Material Stripping" Problem
When we attempted to build a fresh AssetBundle out of the Unity Editor, the resulting cards appeared completely invisible in the game. 

Upon analyzing our compiled bundles using `UnityPy`, we discovered a massive problem: **Unity was stripping all material properties (Floats, Colors, Textures) during the AssetBundle build process.**

**The Root Cause:**
Gwent uses proprietary shaders (e.g., `ShaderLibrary/Generic/GwentStandard`, `VFX/Common/AdditiveAlpha`). In the retail game, these compiled shaders live inside entirely separate dependency AssetBundles (`bundledassets/dependencies/shaderlibrary` and `bundledassets/dependencies/shaders`).
Because our local Unity Editor did *not* have the compiled shader binaries, Unity treated them as "Missing/Error Shaders." During AssetBundle serialization, Unity optimizes by throwing away any material properties (like `_MainTex` or `_AlphaPremultiply`) that don't match properties explicitly declared in the assigned shader. Since the shader was missing, *all* properties were deemed invalid and were stripped.

### Phase C: The Breakthrough Solution (Dummy Shaders + GUID Spoofing)
To force Unity to preserve the material properties inside the `.mat` files, we engineered a complex workaround:

1. **Dummy Shader Creation:**
   We wrote `.shader` files in `Assets/DummyShaders/` that replicate the exact property blocks of the real Gwent shaders.
   - For `GwentStandard`, we dumped the vanilla materials via Python and mapped out all 48 Floats, 12 Colors, and 5 Textures.
   - We removed the `CGPROGRAM` blocks and replaced them with fixed-function commands (`Color (1,1,1,1) Pass { SetTexture [_MainTex] { combine texture } }`) to ensure they compile flawlessly across all Unity build targets without invoking a shader compiler error that might still strip properties.

2. **GUID Spoofing (CRITICAL):**
   Unity materials do *not* reference shaders by their string name (e.g., "GwentStandard"); they reference them by a hidden MD5 Unity GUID found in the `.shader.meta` file. 
   If our Dummy Shader generated a new GUID, the retail game would fail to link the material to the real shader upon loading.
   We extracted the real GUIDs from the retail bundles and manually injected them into our dummy `.meta` files:
   - `GwentStandard`: `d20fdbed1dcf2bc4b96946044cb8443e`
   - `VFX/Common/AdditiveAlpha`: `83d4e1dc1d5b5b84aa81ad1a67196f6f`
   - `VFX/Common/AlphaBlended`: `dba6637c54a17714ea0122d5a54fe4d8`
   - `FakePostEffect/Additive_Mask`: `fc07c209a959c994aa37bca45137595e`

### Phase D: The Python Post-Processing Pipeline
Even with properties preserved and GUIDs spoofed, our Unity editor builds referenced the wrong external CAB (Cabinet) files for where those shaders lived.
We wrote two highly advanced Python scripts to handle post-build patching and verification:

1. **`patch_bundle_shaders.py`**
   - Uses `UnityPy` to crack open our newly built Unity AssetBundle (`buildplayer-1832.sharedassets`).
   - It reads the `m_Externals` table. By default, Unity only linked the `shaderlibrary` CAB.
   - We explicitly inject the retail game's VFX `shaders` CAB (`CAB-c0bb786e78837791c9d84c9a06de6e2b`) into the external array as `fid = 5`.
   - It loops through every single Material in the bundle:
     - If it detects a `GwentStandard` material, it forces its `m_Shader` PPtr to point to `fid = 4` (the shaderlibrary).
     - If it detects a VFX material (AlphaBlended, AdditiveAlpha), it repoints the `m_Shader` to `fid = 5` (the newly injected VFX shaders CAB).
   - It saves and commits the binary bundle.

2. **`compare_bundles.py`**
   - A verification script that loads the unmodded vanilla bundle and our custom newly-patched bundle.
   - It prints a line-by-line comparison of every Float, Color, and Texture inside the material properties. 
   - **Status:** Our pipeline currently achieves exactly 100% parity with the retail game. The system is flawless.

---

## 4. OUR CURRENT TASK: BUILDING THE ELVEN DEADEYE (1832)

Having proven the pipeline with the cloned Elven Wardancer, we are now creating a **custom premium from scratch**.
We confirmed that Elven Deadeye (1832) has NO premium assets from CDPR.

Based on all your knowledge - how can we now add the premium card to the game?
