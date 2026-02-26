"""
Unified build pipeline for Custom Premiums mod.

Orchestrates the full build in one command:
  1. Sync Unity Editor scripts into the Gwent source project
  2. Build the AssetBundle(s) via the already-running Unity Editor
  3. Patch shader references in the compiled bundle(s)
  4. Build the C# MelonLoader mod (dotnet)
  5. Deploy textures + donor config to the game directory

Config-driven: just change the CARDS dict below.

Supported config modes:
  {"donor": "1349"}                     - clone a donor card's scene
  {"prefab": "Assets/path/to.prefab"}   - build scene from a WIP prefab
  Optional keys:
    "texture": "Assets/path/to.png"     - custom texture (relative to Unity project)
    "donor": "1349"                     - donor card for premium SFX audio

Requires: Unity Editor open with the Gwent project loaded.

Usage:
  python build.py              # Build all configured cards
  python build.py 1832         # Build specific card
"""

import os
import sys
import glob
import json
import shutil
import subprocess
import time
import filecmp

# =============================================================================
# CONFIGURATION — change cards here. That's it.
# =============================================================================
CARDS = {
    "1832": {
        "prefab": "Assets/PremiumCards/WIP/_Prefabs_WIP/[]Mimikr.prefab",
        "texture": "Assets/PremiumCards/WIP/_Textures_WIP/_Premium/_Uber/[]Mimikr_Atlas.png",
        "donor": "1349",  # Dryad Ranger audio for premium SFX
    },
}

GWENTMODS_DIR = r"E:\Projekty\GwentMods"
UNITY_PROJECT = r"D:\Gwent_Source_Code\Gwent\Gwent\GwentUnity\Gwent"
GAME_DIR = r"E:\GOG Galaxy\Games\Gwent"

# Derived paths
UNITY_EDITOR_DIR = os.path.join(UNITY_PROJECT, "Assets", "Editor")
UNITY_SCRIPTS_DIR = os.path.join(GWENTMODS_DIR, "UnityScripts")
BUNDLE_OUTPUT_DIR = os.path.join(GAME_DIR, "Mods", "CustomPremiums", "Bundles")
TEXTURE_DST_DIR = os.path.join(GAME_DIR, "Mods", "CustomPremiums", "Textures")
DONOR_CONFIG_PATH = os.path.join(GAME_DIR, "Mods", "CustomPremiums", "donor_config.json")
CSPROJ_PATH = os.path.join(GWENTMODS_DIR, "CustomPremiums", "CustomPremiums.csproj")
MOD_DLL_DST = os.path.join(GAME_DIR, "Mods", "CustomPremiums.dll")

# Premium texture location in the Unity source project (CDPR convention)
PREMIUM_TEXTURE_DIR = os.path.join(
    UNITY_PROJECT, "Assets", "BundledAssets", "CardAssets", "Textures", "Premium", "Uber"
)

# Build watcher handshake files (must match AutoBuildOnLoad.cs)
TRIGGER_FILE = os.path.join(UNITY_PROJECT, "build_trigger.txt")
RESULT_FILE = os.path.join(UNITY_PROJECT, "build_result.txt")
COMPILE_STAMP_FILE = os.path.join(UNITY_PROJECT, "compile_stamp.txt")
BUILD_TIMEOUT = 300
COMPILE_TIMEOUT = 120

UNITY_NUDGE_FILE = os.path.join(UNITY_PROJECT, "Assets", "Editor", ".build_nudge")


def step_header(num, title):
    print(f"\n{'='*60}")
    print(f"  STEP {num}: {title}")
    print(f"{'='*60}")


def fail(msg):
    print(f"\n  FAILED: {msg}")
    sys.exit(1)


def nudge_unity():
    try:
        with open(UNITY_NUDGE_FILE, "w") as f:
            f.write(str(time.time()))
    except OSError:
        pass


def read_compile_stamp():
    if os.path.isfile(COMPILE_STAMP_FILE):
        return open(COMPILE_STAMP_FILE).read().strip()
    return ""


# ─────────────────────────────────────────────────────────────
# STEP 1: Sync Unity Editor scripts + wait for recompilation
# ─────────────────────────────────────────────────────────────
def step1_sync_scripts():
    step_header(1, "Sync Unity Editor Scripts")

    if not os.path.isdir(UNITY_SCRIPTS_DIR):
        fail(f"Unity scripts directory not found: {UNITY_SCRIPTS_DIR}")

    scripts = glob.glob(os.path.join(UNITY_SCRIPTS_DIR, "*.cs"))
    if not scripts:
        fail(f"No .cs files found in {UNITY_SCRIPTS_DIR}")

    os.makedirs(UNITY_EDITOR_DIR, exist_ok=True)

    any_changed = False
    for src in scripts:
        dst = os.path.join(UNITY_EDITOR_DIR, os.path.basename(src))
        if not os.path.isfile(dst) or not filecmp.cmp(src, dst, shallow=False):
            any_changed = True
        shutil.copy2(src, dst)
        print(f"  Synced: {os.path.basename(src)}")

    print(f"  {len(scripts)} script(s) synced to Unity project.")

    needs_recompile = any_changed or not read_compile_stamp()

    if not needs_recompile:
        print("  Scripts up to date — Unity already compiled.")
        return

    old_stamp = read_compile_stamp()
    reason = "scripts changed" if any_changed else "first run after code update"
    print(f"\n  ** Recompile needed ({reason}) **")
    print(f"  >> Click on the Unity Editor window to trigger recompilation. <<")
    print(f"  (waiting for compile_stamp.txt...)\n")

    t0 = time.time()
    while True:
        elapsed = time.time() - t0
        if elapsed > COMPILE_TIMEOUT:
            fail(f"Timed out after {COMPILE_TIMEOUT}s waiting for Unity to recompile.\n"
                 f"  Make sure Unity is open and click on its window.")

        new_stamp = read_compile_stamp()
        if new_stamp and new_stamp != old_stamp:
            print(f"  Unity recompiled! ({elapsed:.1f}s)")
            break

        time.sleep(0.5)


# ─────────────────────────────────────────────────────────────
# STEP 2: Build AssetBundle via Unity (running editor)
# ─────────────────────────────────────────────────────────────
def step2_build_bundle(art_id, card_cfg):
    prefab_path = card_cfg.get("prefab")
    donor_id = card_cfg.get("donor")

    if prefab_path:
        step_header(2, f"Build AssetBundle (ArtId {art_id} <- Prefab)")
        trigger_content = f"{art_id}:prefab:{prefab_path}"
    elif donor_id:
        step_header(2, f"Build AssetBundle (ArtId {art_id} <- Donor {donor_id})")
        trigger_content = f"{art_id}:{donor_id}"
    else:
        fail(f"Card {art_id}: needs 'prefab' or 'donor' key in config")

    bundle_path = os.path.join(BUNDLE_OUTPUT_DIR, art_id)
    os.makedirs(BUNDLE_OUTPUT_DIR, exist_ok=True)

    if os.path.isfile(bundle_path):
        os.remove(bundle_path)
        print(f"  Removed stale bundle.")

    if os.path.isfile(RESULT_FILE):
        os.remove(RESULT_FILE)

    with open(TRIGGER_FILE, "w") as f:
        f.write(trigger_content)
    print(f"  Trigger sent to Unity Editor ({trigger_content}). Waiting for build...")

    nudge_unity()

    t0 = time.time()
    last_nudge = t0
    while True:
        elapsed = time.time() - t0
        if elapsed > BUILD_TIMEOUT:
            if os.path.isfile(TRIGGER_FILE):
                os.remove(TRIGGER_FILE)
            fail(f"Timed out after {BUILD_TIMEOUT}s waiting for Unity. "
                 f"Is the Unity Editor open with the Gwent project?")

        if os.path.isfile(RESULT_FILE):
            result = open(RESULT_FILE).read().strip()
            os.remove(RESULT_FILE)
            break

        if time.time() - last_nudge > 3.0:
            nudge_unity()
            last_nudge = time.time()

        time.sleep(0.3)

    if not result.startswith("OK"):
        fail(f"Unity build failed: {result}")

    if not os.path.isfile(bundle_path):
        fail(f"Bundle not found after build: {bundle_path}")

    size = os.path.getsize(bundle_path)
    print(f"  Bundle built successfully! {size:,} bytes ({elapsed:.1f}s)")


# ─────────────────────────────────────────────────────────────
# STEP 3: Patch bundle shader references (auto-discovered)
# ─────────────────────────────────────────────────────────────
def step3_patch_shaders(art_id):
    step_header(3, f"Patch Bundle Shaders (ArtId {art_id})")

    bundle_path = os.path.join(BUNDLE_OUTPUT_DIR, art_id)
    if not os.path.isfile(bundle_path):
        fail(f"Bundle not found: {bundle_path}")

    sys.path.insert(0, GWENTMODS_DIR)
    import patch_bundle_shaders

    success = patch_bundle_shaders.patch_bundle(bundle_path)
    if not success:
        fail("Shader patching failed!")

    print("  Shader patching complete.")


# ─────────────────────────────────────────────────────────────
# STEP 4: Build C# mod (dotnet)
# ─────────────────────────────────────────────────────────────
def step4_build_mod():
    step_header(4, "Build C# Mod")

    if not os.path.isfile(CSPROJ_PATH):
        fail(f"C# project not found: {CSPROJ_PATH}")

    cmd = ["dotnet", "build", CSPROJ_PATH, "-c", "Debug"]
    print(f"  Running: {' '.join(cmd)}\n")

    proc = subprocess.run(cmd)

    if proc.returncode != 0:
        fail(f"dotnet build failed with exit code {proc.returncode}")

    if not os.path.isfile(MOD_DLL_DST):
        fail(f"DLL not found at deploy target: {MOD_DLL_DST}")

    size = os.path.getsize(MOD_DLL_DST)
    print(f"\n  Mod built and deployed! {size:,} bytes -> {MOD_DLL_DST}")


# ─────────────────────────────────────────────────────────────
# STEP 5: Deploy textures + donor config
# ─────────────────────────────────────────────────────────────
def step5_deploy(cards_to_build):
    step_header(5, "Deploy Textures + Donor Config")

    os.makedirs(TEXTURE_DST_DIR, exist_ok=True)

    for art_id, card_cfg in cards_to_build.items():
        texture_src = None

        # 1. Explicit texture path (relative to Unity project)
        if "texture" in card_cfg:
            texture_src = os.path.join(UNITY_PROJECT, card_cfg["texture"])
            if not os.path.isfile(texture_src):
                fail(f"Custom texture not found: {texture_src}")

        # 2. Auto-lookup from donor's premium texture
        if texture_src is None and "donor" in card_cfg:
            donor_id = card_cfg["donor"]
            texture_src = os.path.join(PREMIUM_TEXTURE_DIR, f"{donor_id}0100.png")

        # 3. Fallback to manual texture in UnityAssets/Textures/
        if texture_src is None or not os.path.isfile(texture_src):
            if texture_src:
                print(f"  WARNING: Texture not found: {texture_src}")
            fallback = os.path.join(GWENTMODS_DIR, "UnityAssets", "Textures", f"{art_id}.png")
            if os.path.isfile(fallback):
                texture_src = fallback
                print(f"  Using fallback: {fallback}")
            else:
                fail(f"No texture found for ArtId {art_id}")

        dst = os.path.join(TEXTURE_DST_DIR, f"{art_id}.png")
        shutil.copy2(texture_src, dst)
        size = os.path.getsize(dst)
        print(f"  Deployed: {art_id}.png ({size:,} bytes) from {os.path.basename(texture_src)}")

    # Write donor_config.json for the C# mod (maps artId → donor artId for audio)
    donor_config = {}
    for k, v in cards_to_build.items():
        if "donor" in v:
            donor_config[int(k)] = int(v["donor"])
    with open(DONOR_CONFIG_PATH, "w") as f:
        json.dump(donor_config, f)
    print(f"  Wrote donor_config.json: {donor_config}")


# ─────────────────────────────────────────────────────────────
# MAIN
# ─────────────────────────────────────────────────────────────
def main():
    if len(sys.argv) > 1:
        art_ids = sys.argv[1:]
        for aid in art_ids:
            if aid not in CARDS:
                fail(f"ArtId {aid} not in CARDS config. Known: {list(CARDS.keys())}")
        cards_to_build = {aid: CARDS[aid] for aid in art_ids}
    else:
        cards_to_build = CARDS

    print("=" * 60)
    print("  CUSTOM PREMIUMS — UNIFIED BUILD PIPELINE")
    for k, v in cards_to_build.items():
        source = f"prefab {v['prefab']}" if "prefab" in v else f"donor {v.get('donor', '?')}"
        print(f"  Card {k} <- {source}")
    print("=" * 60)

    t0 = time.time()

    step1_sync_scripts()

    for art_id, card_cfg in cards_to_build.items():
        step2_build_bundle(art_id, card_cfg)
        step3_patch_shaders(art_id)

    step4_build_mod()
    step5_deploy(cards_to_build)

    elapsed = time.time() - t0
    print(f"\n{'='*60}")
    print(f"  ALL STEPS COMPLETE ({elapsed:.1f}s)")
    print(f"{'='*60}")
    for art_id in cards_to_build:
        print(f"\n  [{art_id}]")
        print(f"    Bundle:  {os.path.join(BUNDLE_OUTPUT_DIR, art_id)}")
        print(f"    Texture: {os.path.join(TEXTURE_DST_DIR, art_id + '.png')}")
    print(f"\n  Mod DLL: {MOD_DLL_DST}")
    print(f"  Config:  {DONOR_CONFIG_PATH}")
    print(f"\n  Ready to launch the game!")


if __name__ == "__main__":
    main()
