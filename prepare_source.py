"""
Prepare the Gwent Unity source project for the custom premium card pipeline.

The Gwent source project as-extracted cannot be opened in Unity — it has ~8,000
C# files that depend on internal CDPR packages, server SDKs, and build tools.
This script strips all that code and adds minimal stubs so Unity can open the
project and build AssetBundles for custom premium cards.

Usage:
  python prepare_source.py <path_to_extracted_source>

The path should point to the root of the extracted archive (the directory
containing the 'Gwent' folder and readme.txt).

What this script does:
  1. Moves all C# code out of Assets/ into _ExcludedCode/ (Unity ignores this)
  2. Creates DummyScripts/ — minimal stubs for components used in premium prefabs
  3. Creates DummyShaders/ — property-only shaders so materials keep their data
  4. Updates Packages/manifest.json to remove CDPR internal package references
  5. Creates an Assets/Editor/ directory (build pipeline scripts go here)

After running this script, open the project in Unity 2021.3.15f1. The first open
will trigger a project upgrade from Unity 2018.4 (the original version) — this
is expected. Accept all upgrade prompts.
"""

import sys
import os
import stat
import shutil
import json
from pathlib import Path


# ---------------------------------------------------------------------------
# Directories to move entirely out of Assets/ (code-only, no art assets)
# ---------------------------------------------------------------------------
DIRS_TO_MOVE = [
    "Code",
    "Dependencies",
    "AutomatedTests",
    "ExternalDependencyManager",
    "Firebase",
    "Parse",
    "Plugins",
]

# Directories to rename in Assets/ (prevents Unity from treating them specially)
DIRS_TO_RENAME = {
    "Editor Default Resources": "Editor Default Resources_Ignored",
    "UnityTestTools": "UnityTestTools_Ignored",
}

# Directories that contain art assets we need but also have .cs files to remove
DIRS_TO_STRIP_CS = [
    "HTMLGenerator",
    "Rewired",
    "TextMesh Pro",
    "Wwise",
    "PremiumCards",
    "WIP",
    "Editor",  # remove CDPR editor scripts; our pipeline adds its own
]

# ---------------------------------------------------------------------------
# Dummy Scripts — stubs for MonoBehaviours referenced by premium card prefabs
# Each entry: (filename, guid, content)
# GUIDs MUST match the originals so Unity can deserialize existing prefabs.
# ---------------------------------------------------------------------------
DUMMY_SCRIPTS = [
    ("PremiumCardsMeshMaterialHandler.cs", "25093b07d5588bc42961f82eada15aee", r'''using System;
using UnityEngine;
using GwentUnity;

namespace GwentVisuals
{
    public class ACardAppearanceRegistree : MonoBehaviour
    {
        [SerializeField]
        protected CardAppearance CardAppearance;

        public virtual void OnSerializeSetup(CardAppearance cardAppearance) { }
    }

    [System.Serializable]
    public class AMaterialAssigments
    {
        public Renderer[] Renderers;
        public int[] MaterialIndex;
        public Material Material;
        public string[] Assigments;
    }

    [System.Serializable]
    public class CardAppearanceMaterialAssigments : AMaterialAssigments
    {
        [SerializeField]
        private CardAppearance m_CardAppearance;
    }

    public class PremiumCardsMeshMaterialHandler : ACardAppearanceRegistree
    {
        public CardAppearanceMaterialAssigments[] PremiumTextureAssigments;
    }
}
'''),
    ("CardAppearance.cs", "423746d7ed4188549bd8df49a6385e62", r'''using UnityEngine;
using System.Collections.Generic;

namespace GwentUnity
{
    public abstract class AAppearanceComponent : MonoBehaviour
    {
    }

    public class CardAppearance : MonoBehaviour
    {
        [SerializeField]
        private AAppearanceComponent[] m_ActiveComponents = null;

        [SerializeField] private List<Animator> m_AllAnimators = new List<Animator>();
        [SerializeField] private List<ParticleSystem> m_AllParticles = new List<ParticleSystem>();
        [SerializeField] private List<Renderer> m_AllRenderers = new List<Renderer>();
    }
}
'''),
    ("RotationObjectController.cs", "2ffef72dce217c04e9d29ce88a30d1b9", r'''using UnityEngine;

namespace GwentUnity
{
    public class BaseObjectController : MonoBehaviour {}

    public class RotationObjectController : BaseObjectController
    {
        public float XRotationStart;
        public float YRotationStart;
        public float XRotationEnd;
        public float YRotationEnd;
    }
}
'''),
    ("CameraValuesChanger.cs", "779e3927c97531041bd10c039690ed52", r'''using UnityEngine;

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
'''),
    ("CameraPositionUVRemap.cs", "798a883cc02a0c74e91521d9cd264ad6", r'''using UnityEngine;
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
'''),
    ("PairTransforms.cs", "547ff85cc4e9c4f4687997c83077314b", r'''using UnityEngine;

namespace GwentVisuals
{
    public class PairTransforms : MonoBehaviour
    {
        [System.Serializable]
        public class TransformPair
        {
            [SerializeField] public Transform Source;
            [SerializeField] public Transform Target;
        }

        public TransformPair[] Pair;
    }
}
'''),
    ("PremiumData.cs", "37fde64410b20f24db1e6c95e4fa9c3c", r'''using UnityEngine;
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
'''),
]

# ---------------------------------------------------------------------------
# Dummy Shaders — stub shaders with full property declarations
# Actual rendering is handled by patch_bundle_shaders.py at build time.
# ---------------------------------------------------------------------------
DUMMY_SHADERS = [
    ("GwentStandard.shader", "d20fdbed1dcf2bc4b96946044cb8443e", "bundledassets/dependencies/shaderlibrary",
r'''// Dummy shader that mirrors ALL property declarations of the real GwentStandard shader.
// Uses fixed-function pipeline (no CGPROGRAM) to guarantee compilation on any Unity version.
// Only the Properties block matters - it prevents Unity from stripping material data during builds.
Shader "ShaderLibrary/Generic/GwentStandard" {
    Properties {
        // Textures
        _MainTex ("Main Texture", 2D) = "white" {}
        _SecondTex ("Second Texture", 2D) = "white" {}
        _ThirdTex ("Third Texture", 2D) = "white" {}
        _FlowTex ("Flow Texture", 2D) = "white" {}
        _Mask ("Mask", 2D) = "white" {}

        // Float properties
        _AlphaPremultiply ("Alpha Premultiply", Float) = 1
        _AlphaToMask ("Alpha To Mask", Float) = 0
        _Anim ("Anim", Float) = 0
        _BlendOp ("Blend Op", Float) = 0
        _Brightness ("Brightness", Float) = 1
        _BumpScale ("Bump Scale", Float) = 1
        _ColorMask ("Color Mask", Float) = 15
        _Cull ("Cull", Float) = 2
        _Cutoff ("Cutoff", Float) = 0.5
        _DetailNormalMapScale ("Detail Normal Map Scale", Float) = 1
        _DoubleSided ("Double Sided", Float) = 2
        _DstAlphaBlend ("Dst Alpha Blend", Float) = 10
        _DstBlend ("Dst Blend", Float) = 0
        _FlowAnimDist ("Flow Anim Dist", Float) = 0
        _FlowAnimSpeed ("Flow Anim Speed", Float) = 0
        _FramesNum ("Frames Num", Float) = 0
        _FramesX ("Frames X", Float) = 0
        _FramesY ("Frames Y", Float) = 0
        _GlossMapScale ("Gloss Map Scale", Float) = 1
        _Glossiness ("Glossiness", Float) = 0.5
        _GlossyReflections ("Glossy Reflections", Float) = 1
        _MainTex_SSUV ("MainTex SSUV", Float) = 0
        _Mask_SSUV ("Mask SSUV", Float) = 0
        _Metallic ("Metallic", Float) = 0
        _Mode ("Mode", Float) = 0
        _Modifier ("Modifier", Float) = 0
        _Multiplier ("Multiplier", Float) = 1
        _OcclusionStrength ("Occlusion Strength", Float) = 1
        _Offset ("Offset", Float) = 0
        _Overrides ("Overrides", Float) = 0
        _Parallax ("Parallax", Float) = 0.02
        _SecondTex_SSUV ("SecondTex SSUV", Float) = 0
        _SmoothnessTextureChannel ("Smoothness Texture Channel", Float) = 0
        _Special ("Special", Float) = 0
        _SpecularHighlights ("Specular Highlights", Float) = 1
        _SrcAlphaBlend ("Src Alpha Blend", Float) = 1
        _SrcBlend ("Src Blend", Float) = 1
        _TexAnimCageMaxX ("Tex Anim Cage Max X", Float) = 1
        _TexAnimCageMaxY ("Tex Anim Cage Max Y", Float) = 1
        _TexAnimCageMaxZ ("Tex Anim Cage Max Z", Float) = 1
        _TexAnimCageMinX ("Tex Anim Cage Min X", Float) = -1
        _TexAnimCageMinY ("Tex Anim Cage Min Y", Float) = -1
        _TexAnimCageMinZ ("Tex Anim Cage Min Z", Float) = -1
        _ThirdTex_SSUV ("ThirdTex SSUV", Float) = 0
        _TimeMultiplier ("Time Multiplier", Float) = 1
        _UVSec ("UV Sec", Float) = 0
        _ZTest ("Z Test", Float) = 4
        _ZWrite ("Z Write", Float) = 1

        // Color properties
        _AplhaRemap ("Alpha Remap", Color) = (0, 1, 1, 1)
        _Color ("Color", Color) = (1, 1, 1, 1)
        _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _FresnelRemap ("Fresnel Remap", Color) = (0, 1, 1, 1)
        _MainTexUVAnim ("MainTex UV Anim", Color) = (0, 0, 0, 0)
        _MaskRemap ("Mask Remap", Color) = (0, 1, 0, 0)
        _MaskUVAnim ("Mask UV Anim", Color) = (0, 0, 0, 0)
        _SecondTexUVAnim ("SecondTex UV Anim", Color) = (0, 0, 0, 0)
        _SecondTintColor ("Second Tint Color", Color) = (1, 1, 1, 1)
        _ThirdTexUVAnim ("ThirdTex UV Anim", Color) = (0, 0, 0, 0)
        _ThirdTintColor ("Third Tint Color", Color) = (1, 1, 1, 1)
        _TintColor ("Tint Color", Color) = (1, 1, 1, 1)
    }
    SubShader {
        Pass {
            SetTexture [_MainTex] { combine texture }
        }
    }
}
'''),
    ("Dummy_VFX_Common_AdditiveAlpha.shader", "73ee64626a74f5449b2821f805263111", "bundledassets/dependencies/shaders",
r'''Shader "VFX/Common/AdditiveAlpha" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _AlphaPower ("Alpha Power", Float) = 1
        _Brightness ("Brightness", Float) = 1
        _TintColor ("Tint Color", Color) = (1, 1, 1, 1)
    }
    SubShader { Pass { } }
}
'''),
    ("Dummy_VFX_Common_AlphaBlended.shader", "c8d081f28a5f6e6478c55cbaff24b089", "bundledassets/dependencies/shaders",
r'''Shader "VFX/Common/AlphaBlended" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _Cutoff ("Cutoff", Float) = 0.5
        _TintColor ("Tint Color", Color) = (1, 1, 1, 1)
    }
    SubShader { Pass { } }
}
'''),
    ("Dummy_VFX_Effects_FakePostEffect_Additive_Mask.shader", "cbe38d7a60c049445bee0a251e21ae80", "bundledassets/dependencies/shaders",
r'''Shader "VFX/Effects/FakePostEffect/Additive_Mask" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _Mask ("Mask", 2D) = "white" {}
        _TintColor ("Tint Color", Color) = (1, 1, 1, 1)
    }
    SubShader { Pass { } }
}
'''),
]

# ---------------------------------------------------------------------------
# Updated package manifest (removes CDPR internal packages, adds Unity 2021 defaults)
# ---------------------------------------------------------------------------
MANIFEST_JSON = {
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
        "com.unity.xr.legacyinputhelpers": "2.1.10",
        "com.unity.modules.ai": "1.0.0",
        "com.unity.modules.androidjni": "1.0.0",
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
        "com.unity.modules.vr": "1.0.0",
        "com.unity.modules.wind": "1.0.0",
        "com.unity.modules.xr": "1.0.0",
    }
}


def write_meta_mono(path, guid):
    """Write a Unity .meta file for a MonoScript (.cs)."""
    with open(path, "w", newline="\n") as f:
        f.write(f"fileFormatVersion: 2\n")
        f.write(f"guid: {guid}\n")
        f.write(f"MonoImporter:\n")
        f.write(f"  serializedVersion: 2\n")
        f.write(f"  defaultReferences: []\n")
        f.write(f"  executionOrder: 0\n")
        f.write(f"  icon: {{instanceID: 0}}\n")
        f.write(f"  userData: \n")
        f.write(f"  assetBundleName: \n")
        f.write(f"  assetBundleVariant: \n")


def write_meta_shader(path, guid, bundle_name):
    """Write a Unity .meta file for a Shader."""
    with open(path, "w", newline="\n") as f:
        f.write(f"fileFormatVersion: 2\n")
        f.write(f"guid: {guid}\n")
        f.write(f"ShaderImporter:\n")
        f.write(f"  externalObjects: {{}}\n")
        f.write(f"  defaultTextures: []\n")
        f.write(f"  nonModifiableTextures: []\n")
        f.write(f"  userData: \n")
        f.write(f"  assetBundleName: {bundle_name}\n")
        f.write(f"  assetBundleVariant: \n")


def write_meta_folder(path, guid):
    """Write a Unity .meta file for a folder."""
    with open(path, "w", newline="\n") as f:
        f.write(f"fileFormatVersion: 2\n")
        f.write(f"guid: {guid}\n")
        f.write(f"folderAsset: yes\n")
        f.write(f"DefaultImporter:\n")
        f.write(f"  externalObjects: {{}}\n")
        f.write(f"  userData: \n")
        f.write(f"  assetBundleName: \n")
        f.write(f"  assetBundleVariant: \n")


def find_unity_root(archive_root):
    """Find the Unity project root inside the extracted archive."""
    candidate = Path(archive_root) / "Gwent" / "Gwent" / "GwentUnity" / "Gwent"
    if candidate.is_dir() and (candidate / "Assets").is_dir():
        return candidate
    # Try without nesting
    if (Path(archive_root) / "Assets").is_dir():
        return Path(archive_root)
    return None


def move_dir(src, dst):
    """Move a directory tree, creating parents as needed."""
    if not src.is_dir():
        return 0
    dst.parent.mkdir(parents=True, exist_ok=True)
    count = sum(1 for _ in src.rglob("*") if _.is_file())
    shutil.move(str(src), str(dst))
    # Move the .meta file too if it exists
    meta = src.parent / f"{src.name}.meta"
    if meta.is_file():
        shutil.move(str(meta), str(dst.parent / meta.name))
    return count


def strip_cs_files(assets_dir, dir_name, excluded_root):
    """Remove all .cs files (and their .meta files) from a directory, preserving everything else."""
    src = assets_dir / dir_name
    if not src.is_dir():
        return 0

    count = 0
    cs_files = list(src.rglob("*.cs"))
    for cs_file in cs_files:
        rel = cs_file.relative_to(assets_dir)
        dst = excluded_root / "Remaining" / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(cs_file), str(dst))
        count += 1
        # Move companion .meta file
        meta = cs_file.parent / f"{cs_file.name}.meta"
        if meta.is_file():
            shutil.move(str(meta), str(dst.parent / f"{cs_file.name}.meta"))
    return count


def main():
    if len(sys.argv) < 2:
        print(__doc__.strip())
        sys.exit(1)

    archive_root = Path(sys.argv[1])
    if not archive_root.is_dir():
        print(f"Error: '{archive_root}' is not a directory")
        sys.exit(1)

    unity_root = find_unity_root(archive_root)
    if unity_root is None:
        print(f"Error: Could not find Unity project root in '{archive_root}'")
        print(f"Expected structure: <root>/Gwent/Gwent/GwentUnity/Gwent/Assets/")
        sys.exit(1)

    assets_dir = unity_root / "Assets"
    excluded_dir = unity_root / "_ExcludedCode"

    print(f"Unity project root: {unity_root}")
    print(f"Assets directory:   {assets_dir}")
    print()

    # Sanity check: make sure this looks like a fresh extraction
    if (assets_dir / "DummyScripts").is_dir():
        print("Error: DummyScripts/ already exists — this project appears to be already prepared.")
        print("Run this script on a fresh extraction of the source archive.")
        sys.exit(1)

    total_moved = 0

    # -----------------------------------------------------------------------
    # Step 1: Move entire directories out of Assets/
    # -----------------------------------------------------------------------
    print("Step 1: Moving code directories to _ExcludedCode/...")
    excluded_dir.mkdir(exist_ok=True)

    for dir_name in DIRS_TO_MOVE:
        src = assets_dir / dir_name
        if src.is_dir():
            n = move_dir(src, excluded_dir / f"{dir_name}_Ignored")
            print(f"  {dir_name}/ -> _ExcludedCode/{dir_name}_Ignored/  ({n} files)")
            total_moved += n
            # Create empty placeholder in Assets/
            src.mkdir(exist_ok=True)

    # -----------------------------------------------------------------------
    # Step 2: Rename special Unity directories
    # -----------------------------------------------------------------------
    print("\nStep 2: Renaming special directories...")
    for old_name, new_name in DIRS_TO_RENAME.items():
        src = assets_dir / old_name
        dst = assets_dir / new_name
        if src.is_dir() and not dst.is_dir():
            n = sum(1 for _ in src.rglob("*") if _.is_file())
            # Rename the directory
            shutil.move(str(src), str(dst))
            # Rename the .meta file too
            old_meta = assets_dir / f"{old_name}.meta"
            new_meta = assets_dir / f"{new_name}.meta"
            if old_meta.is_file():
                shutil.move(str(old_meta), str(new_meta))
            print(f"  {old_name}/ -> {new_name}/  ({n} files)")

    # -----------------------------------------------------------------------
    # Step 3: Strip .cs files from directories that contain art assets
    # -----------------------------------------------------------------------
    print("\nStep 3: Removing .cs files from asset directories...")
    for dir_name in DIRS_TO_STRIP_CS:
        n = strip_cs_files(assets_dir, dir_name, excluded_dir)
        if n > 0:
            print(f"  {dir_name}/: removed {n} .cs files")
            total_moved += n

    # Also catch any remaining .cs files we might have missed
    remaining_cs = [f for f in assets_dir.rglob("*.cs")
                    if "DummyScripts" not in str(f) and "DummyShaders" not in str(f)]
    if remaining_cs:
        print(f"\n  Removing {len(remaining_cs)} additional .cs files...")
        for cs_file in remaining_cs:
            rel = cs_file.relative_to(assets_dir)
            dst = excluded_dir / "Remaining" / rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.move(str(cs_file), str(dst))
            meta = cs_file.parent / f"{cs_file.name}.meta"
            if meta.is_file():
                shutil.move(str(meta), str(dst.parent / f"{cs_file.name}.meta"))
        total_moved += len(remaining_cs)

    print(f"\n  Total .cs files moved: {total_moved}")

    # -----------------------------------------------------------------------
    # Step 4: Create DummyScripts/
    # -----------------------------------------------------------------------
    print("\nStep 4: Creating DummyScripts/...")
    dummy_scripts_dir = assets_dir / "DummyScripts"
    dummy_scripts_dir.mkdir(exist_ok=True)
    write_meta_folder(assets_dir / "DummyScripts.meta", "eefaedd17e374e241acafaf5eff13ca7")

    for filename, guid, content in DUMMY_SCRIPTS:
        filepath = dummy_scripts_dir / filename
        filepath.write_text(content, encoding="utf-8")
        write_meta_mono(dummy_scripts_dir / f"{filename}.meta", guid)
        print(f"  {filename}  (guid: {guid})")

    # -----------------------------------------------------------------------
    # Step 5: Create DummyShaders/
    # -----------------------------------------------------------------------
    print("\nStep 5: Creating DummyShaders/...")
    dummy_shaders_dir = assets_dir / "DummyShaders"
    dummy_shaders_dir.mkdir(exist_ok=True)
    write_meta_folder(assets_dir / "DummyShaders.meta", "347c664ac7b166d4f9eeec3e53a9d17d")

    for filename, guid, bundle, content in DUMMY_SHADERS:
        filepath = dummy_shaders_dir / filename
        filepath.write_text(content, encoding="utf-8")
        write_meta_shader(dummy_shaders_dir / f"{filename}.meta", guid, bundle)
        print(f"  {filename}  (bundle: {bundle})")

    # -----------------------------------------------------------------------
    # Step 6: Create empty Editor/ directory for build pipeline scripts
    # -----------------------------------------------------------------------
    editor_dir = assets_dir / "Editor"
    if not editor_dir.is_dir():
        editor_dir.mkdir(exist_ok=True)
        print("\nStep 6: Created empty Assets/Editor/ (build.py syncs scripts here)")
    else:
        print("\nStep 6: Assets/Editor/ already exists")

    # -----------------------------------------------------------------------
    # Step 7: Update Packages/manifest.json
    # -----------------------------------------------------------------------
    print("\nStep 7: Updating Packages/manifest.json...")
    manifest_path = unity_root / "Packages" / "manifest.json"
    if manifest_path.is_file():
        # Back up original
        backup = manifest_path.parent / "manifest.json.original"
        if not backup.is_file():
            shutil.copy2(str(manifest_path), str(backup))
            print(f"  Backed up original to manifest.json.original")

        # Remove read-only attribute if set (common in archives)
        manifest_path.chmod(stat.S_IWRITE | stat.S_IREAD)

        with open(manifest_path, "w", encoding="utf-8") as f:
            json.dump(MANIFEST_JSON, f, indent=2)
            f.write("\n")
        print(f"  Updated manifest.json (removed CDPR packages, added Unity 2021 defaults)")
    else:
        print(f"  Warning: {manifest_path} not found")

    # -----------------------------------------------------------------------
    # Verification
    # -----------------------------------------------------------------------
    print("\n" + "=" * 60)
    print("Verification")
    print("=" * 60)

    remaining = list(assets_dir.rglob("*.cs"))
    dummy_count = len(list(dummy_scripts_dir.rglob("*.cs")))
    editor_count = len(list((assets_dir / "Editor").rglob("*.cs"))) if (assets_dir / "Editor").is_dir() else 0
    stray = [f for f in remaining
             if "DummyScripts" not in str(f) and "Editor" not in str(f)]

    print(f"  .cs files in DummyScripts/:  {dummy_count}")
    print(f"  .cs files in Editor/:        {editor_count}")
    print(f"  Stray .cs files:             {len(stray)}")

    if stray:
        print("\n  WARNING: Stray .cs files found (may cause compilation errors):")
        for f in stray[:20]:
            print(f"    {f.relative_to(assets_dir)}")
        if len(stray) > 20:
            print(f"    ... and {len(stray) - 20} more")

    print(f"\n  DummyScripts/:  {len(list(dummy_scripts_dir.iterdir()))} files")
    print(f"  DummyShaders/:  {len(list(dummy_shaders_dir.iterdir()))} files")

    print("\nDone! Open the project in Unity 2021.3.15f1.")
    print("First open will trigger a project upgrade from 2018.4 — accept all prompts.")


if __name__ == "__main__":
    main()
