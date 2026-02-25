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

### Phase C: The Breakthrough Solution
To force Unity to preserve the material properties inside the `.mat` files, we engineered a complex workaround:
1. **Dummy Shader Creation:** We wrote `.shader` files that replicate the exact property blocks of the real Gwent shaders.
2. **GUID Spoofing:** We extracted the real GUIDs from the retail bundles and manually injected them into our dummy `.meta` files.
3. **The Python Post-Processing Pipeline:** `patch_bundle_shaders.py` uses `UnityPy` to crack open our newly built Unity AssetBundle, reassign `m_Shader` PPtrs, and link them to the correct external CAB dependencies from the real game.

Our pipeline currently achieves exactly 100% parity with the retail game. The C# payload is successfully injecting these flawlessly patched bundles.

---

## 4. OUR CLEAR PLAN & NEXT STEPS FOR CLAUDE

You will execute the following steps precisely in this sequence:

### STEP 1: Workflow Automation (The Single Build Script)
Currently, iterating on a custom premium is tedious because we have to manually run a sequence of separate steps (Unity project build, Python bundle patching, C# project compilation, moving files, testing in game).
**Your first objective is to build a single powerful script** (e.g., in Python or PowerShell) that orchestrates this entire pipeline into one single command. We want to be able to execute one script and have it automatically build the AssetBundle, run the patcher, compile the C# Mod, and copy files to the game directory. 

### STEP 2: Cloning the Dryad Ranger
Before we construct the Elven Deadeye, we will transition our working copied premium to the **Dryad Ranger**. 
The Dryad Ranger is a card that is visually very similar in composition to the Elven Deadeye. By duplicating and observing an already-working premium copy (the Dryad Ranger), we will study its scene structure in Unity as our functional baseline.
Your goal here is to replace our current "placeholder" cloned scene with a flawless functioning cloned scene of the Dryad Ranger using our new automated workflow.

### STEP 3: Recreating the Elven Deadeye (1832)
Once the cloned Dryad Ranger is functioning flawlessly in-game as an injected bundle, we will analyze exactly how the Dryad Ranger card scene is created. 
Using this exact structural knowledge (mesh layout, material setup, quads, and VFX), you will create the brand new **Elven Deadeye** premium. 
**CRITICAL RULE FOR STEP 3:** Keep the Elven Deadeye structure *as similar as possible* to the existing Dryad Ranger structure. Do NOT try novel approaches or modern Unity tricks. You must faithfully reconstruct it exactly as CDPR would do it.
