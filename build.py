"""
Unified build pipeline for Custom Premiums mod.

Orchestrates the full build in one command:
  1. Sync Unity Editor scripts into the Gwent source project
  2. Build the AssetBundle via the already-running Unity Editor
     (uses a file-trigger handshake — no cold Unity launch needed)
  3. Patch shader references in the compiled bundle
  4. Build the C# MelonLoader mod (dotnet)
  5. Deploy the card texture to the game directory

Requires: Unity Editor open with the Gwent project loaded.

Usage:  python build.py
"""

import os
import sys
import glob
import shutil
import subprocess
import time

# =============================================================================
# CONFIGURATION — adjust these paths if your environment differs
# =============================================================================
ART_ID = "1832"

GWENTMODS_DIR = r"E:\Projekty\GwentMods"
UNITY_PROJECT = r"D:\Gwent_Source_Code\Gwent\Gwent\GwentUnity\Gwent"
GAME_DIR = r"E:\GOG Galaxy\Games\Gwent"

# Derived paths
UNITY_EDITOR_DIR = os.path.join(UNITY_PROJECT, "Assets", "Editor")
UNITY_SCRIPTS_DIR = os.path.join(GWENTMODS_DIR, "UnityScripts")
BUNDLE_OUTPUT_DIR = os.path.join(GAME_DIR, "Mods", "CustomPremiums", "Bundles")
TEXTURE_SRC = os.path.join(GWENTMODS_DIR, "UnityAssets", "Textures", f"{ART_ID}.png")
TEXTURE_DST_DIR = os.path.join(GAME_DIR, "Mods", "CustomPremiums", "Textures")
CSPROJ_PATH = os.path.join(GWENTMODS_DIR, "CustomPremiums", "CustomPremiums.csproj")
MOD_DLL_DST = os.path.join(GAME_DIR, "Mods", "CustomPremiums.dll")
BUNDLE_PATH = os.path.join(BUNDLE_OUTPUT_DIR, ART_ID)

# Build watcher handshake files (must match AutoBuildOnLoad.cs)
TRIGGER_FILE = os.path.join(UNITY_PROJECT, "build_trigger.txt")
RESULT_FILE = os.path.join(UNITY_PROJECT, "build_result.txt")
BUILD_TIMEOUT = 300  # seconds to wait for Unity to finish building

# Touch file inside Assets/ to nudge Unity's asset watcher and wake up the editor
UNITY_NUDGE_FILE = os.path.join(UNITY_PROJECT, "Assets", "Editor", ".build_nudge")


def step_header(num, title):
    print(f"\n{'='*60}")
    print(f"  STEP {num}: {title}")
    print(f"{'='*60}")


def fail(msg):
    print(f"\n  FAILED: {msg}")
    sys.exit(1)


# ─────────────────────────────────────────────────────────────
# STEP 1: Sync Unity Editor scripts
# ─────────────────────────────────────────────────────────────
def step1_sync_scripts():
    step_header(1, "Sync Unity Editor Scripts")

    if not os.path.isdir(UNITY_SCRIPTS_DIR):
        fail(f"Unity scripts directory not found: {UNITY_SCRIPTS_DIR}")

    scripts = glob.glob(os.path.join(UNITY_SCRIPTS_DIR, "*.cs"))
    if not scripts:
        fail(f"No .cs files found in {UNITY_SCRIPTS_DIR}")

    os.makedirs(UNITY_EDITOR_DIR, exist_ok=True)

    for src in scripts:
        dst = os.path.join(UNITY_EDITOR_DIR, os.path.basename(src))
        shutil.copy2(src, dst)
        print(f"  Synced: {os.path.basename(src)}")

    print(f"  {len(scripts)} script(s) synced to Unity project.")


# ─────────────────────────────────────────────────────────────
# STEP 2: Build AssetBundle via Unity (running editor)
# ─────────────────────────────────────────────────────────────
def step2_build_bundle():
    step_header(2, "Build AssetBundle via Unity")

    os.makedirs(BUNDLE_OUTPUT_DIR, exist_ok=True)

    # Remove stale bundle so we can detect a fresh build
    if os.path.isfile(BUNDLE_PATH):
        os.remove(BUNDLE_PATH)
        print(f"  Removed stale bundle.")

    # Clean up any leftover handshake files
    if os.path.isfile(RESULT_FILE):
        os.remove(RESULT_FILE)

    # Drop the trigger file — the background timer in Unity will detect it
    with open(TRIGGER_FILE, "w") as f:
        f.write("build")
    print(f"  Trigger sent to Unity Editor. Waiting for build...")

    # Nudge Unity's asset watcher by touching a file inside Assets/.
    # This forces AssetDatabase.Refresh which wakes up EditorApplication.update
    # even when Unity is unfocused.
    with open(UNITY_NUDGE_FILE, "w") as f:
        f.write(str(time.time()))

    # Poll for the result file
    t0 = time.time()
    nudge_interval = 3.0  # re-nudge every few seconds in case Unity is sluggish
    last_nudge = t0
    while True:
        elapsed = time.time() - t0
        if elapsed > BUILD_TIMEOUT:
            # Clean up trigger if Unity never saw it
            if os.path.isfile(TRIGGER_FILE):
                os.remove(TRIGGER_FILE)
            fail(f"Timed out after {BUILD_TIMEOUT}s waiting for Unity. "
                 f"Is the Unity Editor open with the Gwent project?")

        if os.path.isfile(RESULT_FILE):
            result = open(RESULT_FILE).read().strip()
            os.remove(RESULT_FILE)
            break

        # Periodically re-nudge Unity to keep it awake
        if time.time() - last_nudge > nudge_interval:
            try:
                with open(UNITY_NUDGE_FILE, "w") as f:
                    f.write(str(time.time()))
            except OSError:
                pass
            last_nudge = time.time()

        time.sleep(0.3)

    if not result.startswith("OK"):
        fail(f"Unity build failed: {result}")

    if not os.path.isfile(BUNDLE_PATH):
        fail(f"Bundle not found after build: {BUNDLE_PATH}")

    size = os.path.getsize(BUNDLE_PATH)
    print(f"  Bundle built successfully! {size:,} bytes ({elapsed:.1f}s)")


# ─────────────────────────────────────────────────────────────
# STEP 3: Patch bundle shader references
# ─────────────────────────────────────────────────────────────
def step3_patch_shaders():
    step_header(3, "Patch Bundle Shaders")

    if not os.path.isfile(BUNDLE_PATH):
        fail(f"Bundle not found: {BUNDLE_PATH}")

    # Import the patcher from the same directory
    sys.path.insert(0, GWENTMODS_DIR)
    import patch_bundle_shaders

    success = patch_bundle_shaders.patch_bundle(BUNDLE_PATH)
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
# STEP 5: Deploy texture
# ─────────────────────────────────────────────────────────────
def step5_deploy_texture():
    step_header(5, "Deploy Texture")

    if not os.path.isfile(TEXTURE_SRC):
        fail(f"Source texture not found: {TEXTURE_SRC}")

    os.makedirs(TEXTURE_DST_DIR, exist_ok=True)
    dst = os.path.join(TEXTURE_DST_DIR, f"{ART_ID}.png")
    shutil.copy2(TEXTURE_SRC, dst)
    size = os.path.getsize(dst)
    print(f"  Deployed: {ART_ID}.png ({size:,} bytes)")


# ─────────────────────────────────────────────────────────────
# MAIN
# ─────────────────────────────────────────────────────────────
def main():
    print("=" * 60)
    print("  CUSTOM PREMIUMS — UNIFIED BUILD PIPELINE")
    print(f"  ART ID: {ART_ID}")
    print("=" * 60)

    t0 = time.time()

    step1_sync_scripts()
    step2_build_bundle()
    step3_patch_shaders()
    step4_build_mod()
    step5_deploy_texture()

    elapsed = time.time() - t0
    print(f"\n{'='*60}")
    print(f"  ALL STEPS COMPLETE ({elapsed:.1f}s)")
    print(f"{'='*60}")
    print(f"\n  Bundle:  {BUNDLE_PATH}")
    print(f"  Texture: {os.path.join(TEXTURE_DST_DIR, ART_ID + '.png')}")
    print(f"  Mod DLL: {MOD_DLL_DST}")
    print(f"\n  Ready to launch the game!")


if __name__ == "__main__":
    main()
