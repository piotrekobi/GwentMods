# GwentMods

A collection of [MelonLoader](https://github.com/LavaGang/MelonLoader) mods for **Gwent: The Witcher Card Game** (retail/GOG version).

## Mods

| Mod | Description |
|-----|-------------|
| [**CustomPremiums**](#custompremiums) | Inject custom premium (animated) card arts into the game |
| [**Premiumify**](#premiumify) | Force all cards to render as premium during gameplay |
| [**ModSettings**](#modsettings) | Framework for adding a "MODS" tab to the in-game Settings panel |
| [**ModSettingsTest**](#modsettingstest) | Example mod that demonstrates the ModSettings API |

## Prerequisites

- **Gwent** (GOG retail version, with MelonLoader installed)
- **[MelonLoader 0.7.x](https://github.com/LavaGang/MelonLoader)** (net6 branch) — provides Harmony patching and Il2Cpp interop
- **.NET 6.0 SDK** — for building the C# mods (`dotnet build`)
- **Python 3.10+** — only needed for the CustomPremiums build pipeline
- **Unity 2021.3.15f1** — only needed if building AssetBundles (CustomPremiums pipeline)

### Project References

All `.csproj` files reference assemblies from the local MelonLoader installation. You'll need to update the `<HintPath>` entries if your Gwent installation is in a different location:
```
{GameDir}\MelonLoader\
├── net6\                  # MelonLoader.dll, 0Harmony.dll, Il2CppInterop.Runtime.dll
└── Il2CppAssemblies\      # Il2Cpp-generated game assemblies
```

Built DLLs are automatically copied to `{GameDir}\Mods\` via PostBuild targets in each `.csproj`.

---

## CustomPremiums

Injects fully custom premium (animated) cards into Gwent. Cards that CDPR never made premium can be given premium animations — either by cloning an existing card's animation, building from a WIP prefab in the CDPR source project, or creating one from scratch.

For a detailed step-by-step guide on creating custom premium cards, see **[GUIDE_CustomPremiums.md](GUIDE_CustomPremiums.md)**.

### How It Works

The mod uses 6 Harmony hooks to intercept the card loading pipeline:

| Hook | Target | Purpose |
|------|--------|---------|
| **0** | `GwentApp.HandleDefinitionsLoaded` | Maps ArtIds to TemplateIds, swaps AudioIds to donor's for premium SFX |
| **1** | `Card.SetDefinition` | Forces `IsPremium = true` for target cards |
| **2** | `CardDefinition.IsPremiumDisabled` | Forces return `false` (bypasses premium-disabled check) |
| **3** | `CardViewAssetComponent.ShouldLoadPremium` | Forces `true` (triggers premium asset loading) |
| **4** | `CardAppearanceRequest.HandleTextureRequestsFinished` | Loads custom AssetBundle + scene instead of game's |
| **5** | `CardAppearanceRequest.OnAppearanceObjectLoaded` | Swaps texture with custom art, fixes broken shaders |
| **6** | `VoiceDuplicateFilter.GenerateVoiceover` | Redirects voicelines back to original AudioId |

### Runtime File Layout

The mod expects its data in the game directory:
```
Gwent/Mods/
├── CustomPremiums.dll              # The mod DLL
└── CustomPremiums/
    ├── Bundles/
    │   └── {ArtId}                 # AssetBundle files (extensionless)
    ├── Textures/
    │   └── {ArtId}.png             # Card texture (atlas or standard art)
    └── donor_config.json           # Maps ArtId -> donor ArtId for audio
```

- **Bundles**: Unity AssetBundles containing the premium card scene (3D mesh, materials, animations, VFX)
- **Textures**: PNG textures applied at runtime via `PremiumCardsMeshMaterialHandler`
- **donor_config.json**: Maps each custom card to a donor card whose premium SFX/soundbank is used. Voicelines are redirected back to the original card via Hook 6

### Build Pipeline

The full build is automated via `build.py`. See [GUIDE_CustomPremiums.md](GUIDE_CustomPremiums.md) for complete instructions.

```bash
python build.py          # Build all configured cards
python build.py 1832     # Build specific card by ArtId
```

---

## Premiumify

A gameplay mod that makes **all cards appear as premium** (animated) during matches, regardless of whether the player owns the premium version. This only affects cards that already have premium animations built by CDPR — it doesn't create new animations, it just forces the game to load the premium version for every card.

Can be toggled on/off via the in-game Settings panel (requires ModSettings).

### How It Works

The mod uses multiple Harmony hooks to force `IsPremium = true` on cards at various points in the card lifecycle:

| Hook | Purpose |
|------|---------|
| `Card.Play` | Premium-ifies cards when played |
| `Card.Init` | Premium-ifies cards during initialization |
| `Card.Transform` | Premium-ifies cards after transformation (e.g., Shapeshifter) |
| `CardBattleView.OnCardStartedToMoveLogic` | Premium-ifies cards as they start moving |
| `CardBattleViewAnimation.SetFaceUpInstant` | Premium-ifies cards when flipped face-up |
| `CardBattleViewAnimation.SetFaceUp` | Premium-ifies cards during face-up animation |
| `SelectChoicesHandlerComponent.AttachToGame` | Premium-ifies choice/mulligan cards |

The mod only activates during the Gameplay scene — deck builder, collection, and menus are unaffected.

---

## ModSettings

A framework mod that adds a **"MODS"** tab to Gwent's in-game Settings panel. Other mods can register settings (currently switcher/dropdown controls) that appear in this tab, with full localization support, save/revert behavior, and integration with the game's existing UI flow.

### Features

- Adds a "MODS" button to the settings panel category bar
- Creates a scrollable settings container cloned from the game's own UI
- Supports the game's save/revert flow (settings apply on Save, revert on Back/Cancel)
- Full localization: all 12 game languages required for each translation key
- Multiple mods can register settings independently — they all appear in the same MODS tab
- Persists settings via MelonLoader's `MelonPreferences` system

### How to Add Settings to Your Mod

Your mod needs to:
1. Reference `ModSettings.dll` as a project dependency
2. Register translations for your setting labels and option labels
3. Register each setting with callbacks for get/set/apply/revert
4. Read the saved preference values in your mod's logic

#### Step 1: Add Project Reference

In your `.csproj`:
```xml
<ItemGroup>
  <ProjectReference Include="..\ModSettings\ModSettings.csproj" />
</ItemGroup>
```

#### Step 2: Set Up Preferences

Create a `MelonPreferences` category and entries for your settings. These persist to disk automatically.

```csharp
public class MyMod : MelonMod
{
    // Preference storage
    static MelonPreferences_Category _category;
    static MelonPreferences_Entry<string> _difficultyPref;

    // Pending value (tracks unsaved UI changes)
    static string _pendingDifficulty;

    public override void OnInitializeMelon()
    {
        // Create preferences (saved to UserData/MelonPreferences.cfg)
        _category = MelonPreferences.CreateCategory("MyMod");
        _difficultyPref = _category.CreateEntry("Difficulty", "normal");
        // _difficultyPref.Value is now "normal" on first run,
        // or whatever the user last saved

        RegisterTranslations();
        RegisterSettings();
    }
}
```

#### Step 3: Register Translations

Every visible string needs translations for all 12 supported languages. Call `RegisterTranslationKey` during `OnInitializeMelon`:

```csharp
// Required languages: en-us, pl-pl, de-de, ru-ru, fr-fr, it-it,
//                     es-es, es-mx, pt-br, zh-cn, ja-jp, ko-kr

// Helper for quick translations (uses same text for all languages)
Dictionary<string, string> T(string text) =>
    new[] { "en-us", "pl-pl", "de-de", "ru-ru", "fr-fr", "it-it",
            "es-es", "es-mx", "pt-br", "zh-cn", "ja-jp", "ko-kr" }
    .ToDictionary(l => l, l => text);

void RegisterTranslations()
{
    // Setting label
    ModSettings.ModSettings.RegisterTranslationKey("MyMod", "mymod_difficulty", T("Difficulty"));

    // Option labels
    ModSettings.ModSettings.RegisterTranslationKey("MyMod", "mymod_diff_easy",   T("Easy"));
    ModSettings.ModSettings.RegisterTranslationKey("MyMod", "mymod_diff_normal", T("Normal"));
    ModSettings.ModSettings.RegisterTranslationKey("MyMod", "mymod_diff_hard",   T("Hard"));
}
```

#### Step 4: Register a Switcher Setting

```csharp
void RegisterSettings()
{
    // Define options: List of (id, localization key getter)
    var options = new List<Tuple<string, Func<string>>>
    {
        Tuple.Create("easy",   (Func<string>)(() => "mymod_diff_easy")),
        Tuple.Create("normal", (Func<string>)(() => "mymod_diff_normal")),
        Tuple.Create("hard",   (Func<string>)(() => "mymod_diff_hard")),
    };

    ModSettings.ModSettings.RegisterSwitcherSetting(
        modId:            "MyMod",
        settingKey:       "Difficulty",
        displayNameKey:   "mymod_difficulty",       // localization key for setting label
        switcherOptions:  options,

        getCurrentValue:  () => _difficultyPref.Value,  // what's currently saved

        onValueChangedCallback: (val) =>            // user changed the switcher in UI
        {
            string newVal = val as string;
            // Only mark as pending if different from saved value
            _pendingDifficulty = (newVal != _difficultyPref.Value) ? newVal : null;
        },

        hasPendingChangesCallback:    () => _pendingDifficulty != null,

        applyPendingChangesCallback:  () =>          // user clicked Save
        {
            if (_pendingDifficulty != null)
            {
                _difficultyPref.Value = _pendingDifficulty;
                _pendingDifficulty = null;
                // MelonPreferences.Save() is called automatically by ModSettings
            }
        },

        revertPendingChangesCallback: () =>          // user clicked Back/Cancel
        {
            _pendingDifficulty = null;
        }
    );
}
```

#### Step 5: Use the Saved Value in Your Mod

The preference value is available anywhere in your mod via the `MelonPreferences_Entry`:

```csharp
// Read the current saved value at any time
string difficulty = _difficultyPref.Value;  // "easy", "normal", or "hard"

// Use it in your mod logic
if (difficulty == "hard")
{
    // Apply hard mode behavior
}

// The value persists across game restarts — MelonLoader saves it to
// UserData/MelonPreferences.cfg automatically
```

#### Callback Flow

| Event | What Happens |
|-------|-------------|
| User opens Settings | `HandleShowing` fires — MODS button is created, UI is populated |
| User changes a switcher | `onValueChangedCallback` fires — store pending value, Save button enables |
| User clicks Save | `applyPendingChangesCallback` fires — write to preference, `MelonPreferences.Save()` called |
| User clicks Back | `revertPendingChangesCallback` fires — discard pending value |
| Settings panel closes | `HandleHiding` fires — all UI is cleaned up, pending changes reverted |

### Harmony Patches Used

| Patch | Purpose |
|-------|---------|
| `UISettingsPanel.HandleShowing` | Creates MODS button and initializes mod UI |
| `UISettingsPanel.OnCategoryButtonToggled` | Handles switching between MODS and game categories |
| `UISettingsPanel.SaveSettings` / `OnSave` | Applies pending mod changes when user saves |
| `UISettingsPanel.HandleHiding` | Cleans up mod UI and reverts pending changes |
| `UISettingsPanel.OnBack` | Reverts pending mod changes |

---

## ModSettingsTest

A test/example mod that demonstrates the ModSettings API. Registers 13 switcher settings (3 main + 10 scroll-test) to verify the UI works correctly with many entries and scrolling.

Use this as a reference implementation when creating your own mod settings. See [ModSettingsTest/Core.cs](ModSettingsTest/Core.cs).

---

## Project Structure

```
GwentMods/
├── CustomPremiums/           # Custom premium cards mod
│   ├── Core.cs               #   6 Harmony hooks for premium injection
│   └── CustomPremiums.csproj
├── Premiumify/               # Force all cards premium during gameplay
│   ├── Core.cs               #   Harmony hooks + ModSettings integration
│   └── Premiumify.csproj
├── ModSettings/              # In-game mod settings framework
│   ├── Core.cs               #   Settings panel UI + public API
│   └── ModSettings.csproj
├── ModSettingsTest/          # Example mod using ModSettings
│   ├── Core.cs
│   └── ModSettingsTest.csproj
├── UnityScripts/             # Unity Editor scripts (for build pipeline)
│   ├── AutoBuildOnLoad.cs    #   File-watcher build trigger
│   ├── BuildPremiumBundle.cs #   AssetBundle builder
│   └── CreateElvenDeadeyePrefab.cs  # From-scratch asset generator
├── build.py                  # Unified build pipeline
├── prepare_source.py         # Prepares the Gwent source project for Unity
├── patch_bundle_shaders.py   # Shader reference patcher
├── compare_bundles.py        # Debug: compare two AssetBundles side by side
├── GwentMods.sln             # Visual Studio solution
└── GUIDE_CustomPremiums.md   # Detailed guide for creating custom premiums
```

## Building

```bash
# Build all mods (just the C# DLLs):
dotnet build GwentMods.sln

# Build CustomPremiums with the full pipeline (requires Unity Editor running):
python build.py

# Build a specific card:
python build.py 1832
```

## License

This project is for personal/educational use with a legally owned copy of Gwent.
