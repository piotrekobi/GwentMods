"""
Post-build shader patcher for custom Gwent premium card bundles.

Ensures all material shader references match the vanilla game's structure:
1. Adds the game's 'shaders' CAB as a new external dependency (if not present)
2. Patches GwentStandard materials to reference the shaderlibrary CAB
3. Patches VFX materials to reference the shaders CAB

Usage:  python patch_bundle_shaders.py [bundle_path]
"""

import UnityPy
from UnityPy.files.SerializedFile import FileIdentifier
import sys
import os
import uuid

# Known CAB names from the game's dependency bundles
SHADERLIBRARY_CAB = "CAB-e59affbfa21235772054ea15448f1070"
SHADERS_CAB = "CAB-c0bb786e78837791c9d84c9a06de6e2b"

# Shader path_ids in the SHADERLIBRARY CAB
GWENT_STANDARD_PID = 2826790519698772318

# Shader path_ids in the SHADERS CAB  
VFX_SHADER_PIDS = {
    -8689923047843665890,   # VFX/Common/AdditiveAlpha (flash/glow/flare)
    2061142015117707509,    # VFX/Common/AlphaBlended (leaf_introAll)
    3262816301566506934,    # VFX/Effects/FakePostEffect/Additive_Mask (LensPostFX)
}


def find_cab_fid(exts, cab_name):
    """Find the file_id (1-indexed) for a CAB in the externals list."""
    for idx, ext in enumerate(exts):
        if cab_name.lower() in ext.path.lower():
            return idx + 1
    return None


def create_external_ref(cab_name):
    """Create a new FileIdentifier pointing to a CAB."""
    fi = FileIdentifier.__new__(FileIdentifier)
    fi.path = f"archive:/{cab_name}/{cab_name}"
    fi.temp_empty = ""
    fi.guid = b'\x00' * 16
    fi.type = 0
    return fi


def patch_bundle(bundle_path):
    if not os.path.exists(bundle_path):
        print(f"ERROR: Bundle not found: {bundle_path}")
        return False

    env = UnityPy.load(bundle_path)
    patched = 0

    for cab_name, cab in env.cabs.items():
        if 'sharedassets' not in cab_name.lower():
            continue

        exts = getattr(cab, 'm_Externals', getattr(cab, 'externals', []))

        # Print current externals
        print(f"Current externals in {cab_name}:")
        for idx, ext in enumerate(exts):
            print(f"  [{idx+1}] {ext.path}")

        # Find shaderlibrary
        shaderlibrary_fid = find_cab_fid(exts, SHADERLIBRARY_CAB)
        if shaderlibrary_fid is None:
            print("WARNING: shaderlibrary CAB not found!")
            continue

        # Find or ADD shaders CAB
        shaders_fid = find_cab_fid(exts, SHADERS_CAB)
        if shaders_fid is None:
            # APPEND a new external reference (don't replace existing ones!)
            new_ext = create_external_ref(SHADERS_CAB)
            exts.append(new_ext)
            shaders_fid = len(exts)  # new entry is at the end
            print(f"\n  ADDED ext[{shaders_fid}]: {new_ext.path}")

        print(f"\nShaderlibrary = fid {shaderlibrary_fid}")
        print(f"Shaders = fid {shaders_fid}")

        # Patch materials
        for obj in cab.objects.values():
            if obj.type.name != 'Material':
                continue

            data = obj.read()
            name = getattr(data, 'm_Name', 'unknown')
            fid = data.m_Shader.m_FileID
            pid = data.m_Shader.m_PathID

            needs_patch = False
            new_fid = fid
            new_pid = pid

            if fid == 0 and pid == 0:
                # Null shader -> GwentStandard in shaderlibrary
                new_fid = shaderlibrary_fid
                new_pid = GWENT_STANDARD_PID
                needs_patch = True
            elif pid in VFX_SHADER_PIDS and fid != shaders_fid:
                # VFX shader -> must point to shaders CAB
                new_fid = shaders_fid
                needs_patch = True

            if needs_patch:
                print(f"  PATCH: {name} ({fid},{pid}) -> ({new_fid},{new_pid})")
                data.m_Shader.m_FileID = new_fid
                data.m_Shader.m_PathID = new_pid
                data.save()
                patched += 1
            else:
                print(f"  OK: {name} (fid={fid}, pid={pid})")

    if patched > 0:
        with open(bundle_path, "wb") as f:
            f.write(env.file.save())
        print(f"\nPatched {patched} materials. Bundle saved!")
    else:
        print("\nNo materials needed patching.")

    return True


if __name__ == "__main__":
    if len(sys.argv) < 2:
        bundle_path = r"E:\GOG Galaxy\Games\Gwent\Mods\CustomPremiums\Bundles\1832"
    else:
        bundle_path = sys.argv[1]

    print(f"Patching bundle: {bundle_path}\n")
    patch_bundle(bundle_path)

    # Verify
    print("\n=== VERIFICATION ===")
    env2 = UnityPy.load(bundle_path)
    for cab_name, cab in env2.cabs.items():
        if 'sharedassets' in cab_name.lower():
            exts = getattr(cab, 'm_Externals', getattr(cab, 'externals', []))
            print("Externals:")
            for idx, ext in enumerate(exts):
                print(f"  [{idx+1}] {ext.path}")
            print("Materials:")
            for obj in cab.objects.values():
                if obj.type.name == 'Material':
                    data = obj.read()
                    name = getattr(data, 'm_Name', 'unk')
                    print(f"  {name}: fid={data.m_Shader.m_FileID}, pid={data.m_Shader.m_PathID}")
