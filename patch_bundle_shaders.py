"""
Post-build shader patcher for custom Gwent premium card bundles.

Auto-discovers the correct shader → CAB mapping by scanning the game's
actual 'shaderlibrary' and 'shaders' dependency bundles. No hardcoded
shader pids or CAB names — works for any card.

For each material in the custom bundle:
  1. Looks up its shader path_id in the auto-discovered mapping
  2. Sets the external file reference to point to the correct game CAB
  3. For null shader refs (fid=0,pid=0), defaults to GwentStandard

Usage:  python patch_bundle_shaders.py [bundle_path]
"""

import UnityPy
from UnityPy.files.SerializedFile import FileIdentifier
import sys
import os

# Game dependency bundles — scanned at patch time to build the pid → CAB mapping
GAME_DEPS_DIR = os.path.join(
    os.environ.get("GWENT_GAME_DIR", r"E:\GOG Galaxy\Games\Gwent"),
    "Gwent_Data", "StreamingAssets", "bundledassets", "dependencies"
)
DEP_BUNDLE_NAMES = ["shaderlibrary", "shaders"]


def scan_game_shaders():
    """Scan the game's shader dependency bundles and build a pid → cab_name mapping.

    Returns:
        pid_to_cab: dict mapping shader path_id → CAB name (e.g. "CAB-c0bb...")
        gwent_standard_pid: the path_id of GwentStandard (for null shader fallback)
        gwent_standard_cab: the CAB name containing GwentStandard
    """
    pid_to_cab = {}
    gwent_standard_pid = None
    gwent_standard_cab = None

    for bundle_name in DEP_BUNDLE_NAMES:
        bundle_path = os.path.join(GAME_DEPS_DIR, bundle_name)
        if not os.path.exists(bundle_path):
            print(f"  WARNING: Game bundle not found: {bundle_path}")
            continue

        env = UnityPy.load(bundle_path)
        for cab_name, cab in env.cabs.items():
            # Extract the CAB identifier (e.g. "CAB-c0bb786e...")
            # cab_name format is like "cab-c0bb786e..." (lowercase)
            cab_id = cab_name.split("/")[-1] if "/" in cab_name else cab_name
            # Normalize to uppercase CAB- prefix to match bundle externals
            if cab_id.startswith("cab-"):
                cab_id = "CAB-" + cab_id[4:]

            for obj in cab.objects.values():
                if obj.type.name != "Shader":
                    continue
                try:
                    data = obj.read()
                    parsed = getattr(data, "m_ParsedForm", None)
                    shader_name = parsed.m_Name if parsed else getattr(data, "m_Name", "?")
                except Exception:
                    shader_name = "?"

                pid_to_cab[obj.path_id] = cab_id

                # Track GwentStandard for the null-shader fallback
                if "GwentStandard" in shader_name and gwent_standard_pid is None:
                    gwent_standard_pid = obj.path_id
                    gwent_standard_cab = cab_id

    print(f"  Scanned {len(pid_to_cab)} shaders from {len(DEP_BUNDLE_NAMES)} game bundles")
    if gwent_standard_pid:
        print(f"  GwentStandard: pid={gwent_standard_pid} in {gwent_standard_cab}")

    return pid_to_cab, gwent_standard_pid, gwent_standard_cab


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


def ensure_cab_external(exts, cab_name):
    """Find or add a CAB in the externals list. Returns the fid (1-indexed)."""
    fid = find_cab_fid(exts, cab_name)
    if fid is None:
        new_ext = create_external_ref(cab_name)
        exts.append(new_ext)
        fid = len(exts)
        print(f"  ADDED ext[{fid}]: {new_ext.path}")
    return fid


def patch_bundle(bundle_path):
    if not os.path.exists(bundle_path):
        print(f"ERROR: Bundle not found: {bundle_path}")
        return False

    # Auto-discover shader locations from the game
    print("Scanning game shader bundles...")
    pid_to_cab, gs_pid, gs_cab = scan_game_shaders()
    if not pid_to_cab:
        print("ERROR: No shaders found in game bundles!")
        return False

    env = UnityPy.load(bundle_path)
    patched = 0

    for cab_name, cab in env.cabs.items():
        if "sharedassets" not in cab_name.lower():
            continue

        exts = getattr(cab, "m_Externals", getattr(cab, "externals", []))

        print(f"\nCurrent externals in {cab_name}:")
        for idx, ext in enumerate(exts):
            print(f"  [{idx+1}] {ext.path}")

        # Cache of CAB name → fid (lazily populated)
        cab_fid_cache = {}

        def get_fid_for_cab(target_cab):
            if target_cab not in cab_fid_cache:
                cab_fid_cache[target_cab] = ensure_cab_external(exts, target_cab)
            return cab_fid_cache[target_cab]

        # Patch materials
        print(f"\nPatching materials:")
        for obj in cab.objects.values():
            if obj.type.name != "Material":
                continue

            data = obj.read()
            name = getattr(data, "m_Name", "unknown")
            fid = data.m_Shader.m_FileID
            pid = data.m_Shader.m_PathID

            needs_patch = False
            new_fid = fid
            new_pid = pid

            if fid == 0 and pid == 0:
                # Null shader → default to GwentStandard
                if gs_pid is not None and gs_cab is not None:
                    new_fid = get_fid_for_cab(gs_cab)
                    new_pid = gs_pid
                    needs_patch = True
                else:
                    print(f"  SKIP: {name} (null shader, no GwentStandard found)")
                    continue
            elif fid == 0 or fid == 1:
                # fid=0 + non-zero pid = embedded shader (leave as-is)
                # fid=1 = unity default resources (leave as-is)
                print(f"  OK: {name} (fid={fid}, pid={pid}) [embedded/builtin]")
                continue
            else:
                # Check if pid is in our mapping
                if pid in pid_to_cab:
                    correct_cab = pid_to_cab[pid]
                    correct_fid = get_fid_for_cab(correct_cab)
                    if fid != correct_fid:
                        new_fid = correct_fid
                        needs_patch = True
                else:
                    # Unknown pid — leave as-is but warn
                    print(f"  WARN: {name} (fid={fid}, pid={pid}) [pid not in game shaders]")
                    continue

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
        if "sharedassets" in cab_name.lower():
            exts = getattr(cab, "m_Externals", getattr(cab, "externals", []))
            print("Externals:")
            for idx, ext in enumerate(exts):
                print(f"  [{idx+1}] {ext.path}")
            print("Materials:")
            for obj in cab.objects.values():
                if obj.type.name == "Material":
                    data = obj.read()
                    name = getattr(data, "m_Name", "unk")
                    print(f"  {name}: fid={data.m_Shader.m_FileID}, pid={data.m_Shader.m_PathID}")
