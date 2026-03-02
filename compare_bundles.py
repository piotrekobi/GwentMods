"""
Compare two Unity AssetBundles side by side.

Useful for debugging custom premium card bundles by comparing them against
official CDPR bundles. Shows materials, shader references, textures,
GameObjects, and MonoBehaviours — helps spot missing components, wrong
shader FileIDs, or structural differences.

Usage:
  python compare_bundles.py <bundle_a> <bundle_b> [label_a] [label_b]

Examples:
  # Compare your custom bundle against a CDPR original:
  python compare_bundles.py "E:/GOG Galaxy/Games/Gwent/Gwent_Data/StreamingAssets/AssetBundles/13490101" "E:/GOG Galaxy/Games/Gwent/Mods/CustomPremiums/Bundles/1832" "CDPR Dryad Ranger" "Custom Elven Deadeye"

  # Compare before/after shader patching:
  python compare_bundles.py build/1832_unpatched build/1832_patched "Before patch" "After patch"

What to look for:
  - Externals: Your bundle should reference the same CAB hashes as CDPR's
    (these point to shader bundles like shaderlibrary and shaders)
  - Materials: Shader fid/pid should be non-zero and point to externals
    (fid=0 means embedded shader = will render pink/magenta in-game)
  - MonoBehaviours: Should include PremiumCardsMeshMaterialHandler,
    CardAppearanceComponent, and RotationScript (script_fid pointing to
    game assemblies via externals)
  - GameObjects: Should follow CDPR naming convention ({ArtId}00, Pivot, etc.)

Where to find CDPR bundles:
  {GameDir}/Gwent_Data/StreamingAssets/AssetBundles/
  Premium card bundles are named {ArtId}0101 (e.g., 13490101 for Dryad Ranger)

Requires: pip install UnityPy
"""
import sys
import os

try:
    import UnityPy
except ImportError:
    print("Error: UnityPy is required. Install with: pip install UnityPy")
    sys.exit(1)


def dump_bundle(path, label):
    print(f"\n{'='*70}")
    print(f"  {label}")
    print(f"  {path}")
    print(f"{'='*70}")

    if not os.path.isfile(path):
        print(f"  ERROR: File not found!")
        return

    env = UnityPy.load(path)

    print(f"\nCABs: {list(env.cabs.keys())}")

    for cab_name, cab in env.cabs.items():
        print(f"\n--- CAB: {cab_name} ---")

        exts = getattr(cab, 'm_Externals', getattr(cab, 'externals', []))
        print(f"  Externals ({len(exts)}):")
        for idx, ext in enumerate(exts):
            print(f"    [{idx+1}] {ext.path}")

        # Count object types
        type_counts = {}
        for obj in cab.objects.values():
            t = obj.type.name
            type_counts[t] = type_counts.get(t, 0) + 1
        print(f"  Objects ({len(cab.objects)}):")
        for t, c in sorted(type_counts.items()):
            print(f"    {t}: {c}")

        # Dump materials with shader info
        print(f"  Materials:")
        for obj in cab.objects.values():
            if obj.type.name == 'Material':
                data = obj.read()
                name = getattr(data, 'm_Name', 'unknown')
                fid = data.m_Shader.m_FileID
                pid = data.m_Shader.m_PathID
                print(f"    {name}: fid={fid}, pid={pid}")

                if hasattr(data, 'm_SavedProperties'):
                    tex_envs = data.m_SavedProperties.m_TexEnvs
                    if tex_envs:
                        items = tex_envs.items() if hasattr(tex_envs, 'items') else enumerate(tex_envs)
                        for key, tex_val in items:
                            try:
                                if hasattr(tex_val, 'm_Texture') and tex_val.m_Texture.m_PathID != 0:
                                    print(f"      tex '{key}': fid={tex_val.m_Texture.m_FileID}, pid={tex_val.m_Texture.m_PathID}")
                            except:
                                pass

        # Dump Texture2D objects
        print(f"  Textures:")
        for obj in cab.objects.values():
            if obj.type.name == 'Texture2D':
                data = obj.read()
                name = getattr(data, 'm_Name', 'unknown')
                w = getattr(data, 'm_Width', '?')
                h = getattr(data, 'm_Height', '?')
                fmt = getattr(data, 'm_TextureFormat', '?')
                print(f"    {name}: {w}x{h}, format={fmt}")

        # Dump MonoBehaviour scripts
        print(f"  MonoBehaviours:")
        for obj in cab.objects.values():
            if obj.type.name == 'MonoBehaviour':
                try:
                    data = obj.read()
                    name = getattr(data, 'm_Name', '')
                    script = data.m_Script
                    print(f"    name='{name}', script_fid={script.m_FileID}, script_pid={script.m_PathID}")
                except:
                    print(f"    (failed to read, pathId={obj.path_id})")

        # Dump GameObjects
        print(f"  GameObjects:")
        for obj in cab.objects.values():
            if obj.type.name == 'GameObject':
                try:
                    data = obj.read()
                    name = getattr(data, 'm_Name', 'unknown')
                    print(f"    {name}")
                except:
                    print(f"    (failed to read)")

    size = os.path.getsize(path)
    print(f"\n  File size: {size:,} bytes")


def main():
    if len(sys.argv) < 3:
        print(__doc__.strip())
        sys.exit(1)

    bundle_a = sys.argv[1]
    bundle_b = sys.argv[2]
    label_a = sys.argv[3] if len(sys.argv) > 3 else "BUNDLE A"
    label_b = sys.argv[4] if len(sys.argv) > 4 else "BUNDLE B"

    dump_bundle(bundle_a, label_a)
    dump_bundle(bundle_b, label_b)


if __name__ == "__main__":
    main()
