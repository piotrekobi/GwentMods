# Custom Sounds Guide for Gwent Premium Cards

This document explains exactly how Gwent's audio system works and what steps are needed
to replace the current "donor AudioId" approach with fully custom audio bundled into the mod.

**Current state:** We borrow the Elven Wardancer's AudioId so the game's existing premium
sound plays on our custom premium card. This guide covers how to replace that with your
own audio file (e.g., an MP3 you've created or sourced).

---

## Part 1: How Gwent's Audio System Works

### 1.1 Audio Middleware: Wwise

Gwent does NOT use Unity's built-in AudioSource system. It uses **Audiokinetic Wwise**,
a professional audio middleware. The native engine lives at:
```
Gwent_Data\Plugins\x86_64\AkSoundEngine.dll
```

All audio API calls go through `AkSoundEngine` (e.g., `PostEvent`, `LoadBank`,
`LoadFilePackage`). Unity is only involved as the host — Wwise handles all mixing,
playback, effects, and spatial audio independently.

### 1.2 File Formats

Wwise uses two main file types, both stored in `Gwent_Data\StreamingAssets\audio\`:

| Format | Extension | What it contains |
|--------|-----------|-----------------|
| **File Package** | `.pck` | A container that bundles one or more soundbanks + their media files. Think of it like a ZIP file for audio. |
| **Soundbank** | `.bnk` | Event definitions, metadata, and references to audio media. In Gwent, these are always packed inside `.pck` files — there are zero standalone `.bnk` files. |
| **Wwise Encoded Media** | `.wem` | The actual audio data (encoded from WAV/MP3/OGG). These are packed inside the `.pck` along with their `.bnk`. |

**Important:** Gwent has ~125 `.pck` files totaling 1.6 GB. Card sounds are organized by
faction and expansion:
```
cards_ep0_baseset_sco.pck   ← Base set Scoia'tael cards (including Elven Wardancer)
cards_ep0_baseset_mon.pck   ← Base set Monsters
cards_EP1.pck               ← Expansion 1 cards (all factions)
cards_common.pck            ← Shared/common card SFX
...
```

### 1.3 The Two JSON Mapping Files

These plain-text JSON files in `Gwent_Data\StreamingAssets\audio\` are the routing tables
that tell the game which soundbank lives in which `.pck` file, and which Wwise event IDs
live in which soundbank:

#### `soundbank_inclusion.json`
Maps `.pck` filenames → list of soundbank names inside them:
```json
{
  "InclusionMapping": {
    "cards_ep0_baseset_sco.pck": [
      "SCO_EP0_elven_scout",
      "SCO_EP0_elven_wardancer",
      "SCO_EP0_elven_wardancer_pre",
      "SCO_EP0_elven_swordmaster",
      ...
    ],
    "cards_common.pck": ["NEU_EP0_geralt_igni", ...],
    ...
  }
}
```

#### `event_inclusion.json`
Maps soundbank names → list of Wwise event IDs (as string-encoded uint32):
```json
{
  "InclusionMapping": {
    "SCO_EP0_elven_wardancer": ["3713661207"],
    "SCO_EP0_elven_wardancer_pre": ["1787157805"],
    ...
  }
}
```

The `_pre` suffix means "premium" — that's the ambient/animated sound that plays when
viewing the premium version of a card.

### 1.4 The CardAudio XML Data

Stored inside a Unity asset bundle (loaded via `assetManager.GetAsset(EAssetName.CardAudio)`),
this XML defines the audio configuration for every card. Each entry looks conceptually like:

```xml
<CardAudio>
  <Id>42</Id>  <!-- This is the AudioId referenced by CardTemplate.AudioId -->
  <Triggers>
    <Trigger type="CardPlacedOnBoard" sfx="Standard" />
    <Trigger type="PremiumCardPreview" sfx="Premium" />
  </Triggers>
  <SoundEffects>
    <SoundEffect type="Standard" eventId="3713661207" />   <!-- Normal card play sound -->
    <SoundEffect type="Premium" eventId="1787157805" />     <!-- Premium ambient loop -->
  </SoundEffects>
  <DefaultVoiceovers>...</DefaultVoiceovers>
</CardAudio>
```

Key classes in the game code:
- `CardAudio` — the data object (Id, Triggers list, SoundEffects list, Voiceovers)
- `CardAudioTrigger` — maps `ECardAudioTriggerType` → `ECardSoundEffectType`
- `CardSoundEffect` — holds `EffectType` (Standard/Premium/Ambush) and `EventId` (Wwise event ID as int)
- `CardAudioWrapper` — runtime wrapper with lookup methods
- `CardAudioRuntimeData` — singleton `Dictionary<int, CardAudioWrapper>` loaded at startup

### 1.5 The Complete Playback Chain

When a premium card is previewed, here's the exact sequence:

```
STEP 1: UI triggers playback
    UISidePreviewCardSoundHandler.StartCardSounds(cardView)
      → checks cardDefinition.IsPremium == true
      → calls cardView.SoundEffectHandler.PlayPremiumPreview()

STEP 2: Handler fires event
    CardViewSfxHandler.PlayPremiumPreview()
      → waits for card assets to load (OnAllAssetsLoaded)
      → fires OnRequestAudio with TriggerType = PremiumCardPreview

STEP 3: Manager resolves the audio
    CardSfxManager.HandleOnRequestAudio(requestInfo)
      → gets audioId = card.Template.AudioId           (e.g., 42)
      → CardAudioRuntimeData.GetCardAudio(42)          → CardAudioWrapper
      → wrapper.CheckSoundEffectOnTrigger(PremiumCardPreview) → Premium
      → wrapper.GetSoundEffect(Premium)                → CardSoundEffect { EventId = 1787157805 }

STEP 4: Soundbank was pre-loaded during card appearance creation
    CardSoundbankRequest.Start()
      → gets eventId from CardSoundEffect
      → SoundbankManager.LoadEventResources(eventId, callback)
        → RuntimeEventMapProvider:  1787157805 → "SCO_EP0_elven_wardancer_pre"
        → RuntimeSoundbanksMapProvider: "SCO_EP0_elven_wardancer_pre" → "cards_ep0_baseset_sco.pck"
        → AkSoundEngine.LoadFilePackage("cards_ep0_baseset_sco.pck")
        → AkSoundEngine.LoadBank("SCO_EP0_elven_wardancer_pre")

STEP 5: Wwise plays the audio
    AkSoundEngine.PostEvent(1787157805, gameObject)
      → Wwise finds event 1787157805 in the loaded soundbank
      → Plays the associated .wem audio data from the loaded .pck
```

### 1.6 Wwise Event IDs

Wwise event IDs are **FNV-1 hashes** of lowercase event name strings. The game has
a `WwiseShortIdGenerator` that computes them:

```csharp
public static uint Compute(string in_name)
{
    byte[] buffer = Encoding.UTF8.GetBytes(in_name.ToLowerInvariant());
    uint hval = 2166136261u;   // FNV offset basis
    for (int i = 0; i < buffer.Length; i++)
    {
        hval *= 16777619u;     // FNV prime
        hval ^= buffer[i];
    }
    return hval;
}
```

So the event name `"Play_SCO_EP0_elven_wardancer_pre"` hashes to some uint32 —
that's the event ID stored in CardSoundEffect.EventId and in event_inclusion.json.

---

## Part 2: What You Need to Create Custom Audio

### 2.1 Tools Required

1. **Wwise Authoring Tool** (free for non-commercial / budget under $150K)
   - Download from: https://www.audiokinetic.com/en/download
   - You need the **Wwise Launcher** → install **Wwise 2021.1.x** (match Gwent's version
     as closely as possible — check `AkSoundEngine.dll` version info if needed)
   - During installation, select at minimum:
     - Authoring tool
     - SDK (Windows)
     - File Packager (for creating .pck files)

2. **Your audio file** — WAV preferred (Wwise will encode it). MP3 works too but Wwise
   will transcode it. For a premium card ambient loop, you want:
   - A seamless loop, ~10-30 seconds long
   - Ambient/atmospheric in character
   - WAV 44.1kHz 16-bit stereo is ideal

### 2.2 Creating the Wwise Project and Assets

#### Step A: Create a New Wwise Project
1. Open **Wwise Authoring**
2. File → New Project → name it `GwentCustomAudio`
3. Set platform to **Windows** only
4. Set the project language to **English(US)** (this is what Gwent uses)

#### Step B: Import Your Audio
1. In the **Audio** tab, right-click **Default Work Unit** under **Actor-Mixer Hierarchy**
2. New Child → **Sound SFX**
3. Name it `custom_premium_1832` (or whatever your card is)
4. In the **Source Editor** panel, click **Import...** and select your WAV/MP3 file
5. If this is a looping ambient sound:
   - Select the sound object
   - In the **General Settings** tab, check **Loop** and set to **Infinite**
   - Under **Source Settings**, set conversion to **Vorbis** (quality ~4-6, matches Gwent's encoding)

#### Step C: Create a Wwise Event
1. In the **Events** tab, right-click **Default Work Unit**
2. New Child → **Event**
3. Name it: `Play_custom_premium_1832`
4. Drag your `custom_premium_1832` sound object into the event's action list
5. The default action type is **Play** — that's correct
6. (Optional) Create a second event `Stop_custom_premium_1832` with action type **Stop**
   targeting the same sound object — useful for clean stop behavior

#### Step D: Create a SoundBank
1. In the **SoundBanks** tab (or Layouts → SoundBank), right-click **Default Work Unit**
2. New Child → **SoundBank**
3. Name it: `CUSTOM_1832_pre` (following Gwent's naming convention)
4. Drag your `Play_custom_premium_1832` event into this soundbank
5. If you made a stop event, drag that in too
6. Click **Generate** (or Shift+F7) to build the soundbank
   - Output goes to: `<project>/GeneratedSoundBanks/Windows/`
   - You'll get: `CUSTOM_1832_pre.bnk` and associated `.wem` file(s)

#### Step E: Package into a .pck File
1. Open Wwise's **File Packager** tool (comes with Wwise installation)
   - Usually at: `C:\Program Files (x86)\Audiokinetic\Wwise <version>\Authoring\x64\Release\bin\FilePackager.exe`
   - Or accessible from: Wwise Launcher → Tools → File Packager
2. Create a new file package
3. Add your `CUSTOM_1832_pre.bnk` and all associated `.wem` files
4. Set the output filename to: `custom_cards_mod.pck`
5. Generate the package

**Alternative to File Packager:** If you can't get the File Packager to work, you can
try loading the `.bnk` directly without a `.pck` wrapper. The game's `SoundbankManager`
normally requires a `.pck`, but you could potentially hook the loading to use
`AkSoundEngine.LoadBank()` with a direct file path instead. See Part 3 for details.

### 2.3 Compute Your Event ID

You need the FNV-1 hash of your event name. Here's a Python helper:

```python
def wwise_short_id(name: str) -> int:
    """Compute Wwise ShortID (FNV-1 hash) from event name."""
    data = name.lower().encode('utf-8')
    hval = 2166136261
    for byte in data:
        hval = ((hval * 16777619) ^ byte) & 0xFFFFFFFF
    return hval

# Example:
event_name = "Play_custom_premium_1832"
event_id = wwise_short_id(event_name)
print(f"Event: {event_name} → ID: {event_id}")
```

Write down this event ID — you'll need it for the JSON mappings and the C# mod code.

---

## Part 3: Deploying and Loading Custom Audio at Runtime

### 3.1 File Deployment

Place your `.pck` file in the game's audio directory:
```
E:\GOG Galaxy\Games\Gwent\Gwent_Data\StreamingAssets\audio\custom_cards_mod.pck
```

The `build.py` script should be extended to copy this file during deployment (Step 5).

### 3.2 Patching the JSON Mapping Files

The two JSON files in `StreamingAssets\audio\` need new entries. You have two approaches:

#### Approach A: Patch the JSON files on disk (simpler but fragile)
Modify `build.py` to edit the JSON files directly during deployment:

```python
import json

AUDIO_DIR = r"E:\GOG Galaxy\Games\Gwent\Gwent_Data\StreamingAssets\audio"

# Your custom audio config
CUSTOM_EVENT_NAME = "Play_custom_premium_1832"
CUSTOM_EVENT_ID = str(wwise_short_id(CUSTOM_EVENT_NAME))  # compute with function above
CUSTOM_SOUNDBANK = "CUSTOM_1832_pre"
CUSTOM_PCK = "custom_cards_mod.pck"

# Patch soundbank_inclusion.json
sb_path = os.path.join(AUDIO_DIR, "soundbank_inclusion.json")
with open(sb_path, 'r') as f:
    sb_data = json.load(f)
if CUSTOM_PCK not in sb_data["InclusionMapping"]:
    sb_data["InclusionMapping"][CUSTOM_PCK] = [CUSTOM_SOUNDBANK]
elif CUSTOM_SOUNDBANK not in sb_data["InclusionMapping"][CUSTOM_PCK]:
    sb_data["InclusionMapping"][CUSTOM_PCK].append(CUSTOM_SOUNDBANK)
with open(sb_path, 'w') as f:
    json.dump(sb_data, f, indent=2)

# Patch event_inclusion.json
ev_path = os.path.join(AUDIO_DIR, "event_inclusion.json")
with open(ev_path, 'r') as f:
    ev_data = json.load(f)
if CUSTOM_SOUNDBANK not in ev_data["InclusionMapping"]:
    ev_data["InclusionMapping"][CUSTOM_SOUNDBANK] = [CUSTOM_EVENT_ID]
with open(ev_path, 'w') as f:
    json.dump(ev_data, f, indent=2)
```

**Downside:** Game updates will overwrite these JSON files, requiring re-patching.

#### Approach B: Hook the map providers at runtime (robust, self-contained)
Instead of modifying files on disk, hook the C# map provider dictionaries after they
load. This is the cleaner approach for a mod. See Section 3.3.

### 3.3 C# Mod Changes (Core.cs)

The mod needs several new hooks to inject custom audio data at runtime. Here's what
each hook does and where it needs to go:

#### Hook: Inject into AudioRuntimeData's event/soundbank maps

After `AudioRuntimeData.Initialize()` runs, it has built two dictionaries from the JSON
files. You need to add your custom entries to both dictionaries.

The relevant classes (all in namespace `GwentUnity.Audio`):
- `AudioRuntimeData` — singleton, holds `IInclusionMapProvider` for events and soundbanks
- `RuntimeEventMapProvider` — has internal `Dictionary<uint, string>` mapping eventId → soundbank name
- `RuntimeSoundbanksMapProvider` — has internal `Dictionary<string, string>` mapping soundbank name → .pck filename

```csharp
// Pseudo-code for the hook (actual implementation depends on Il2Cpp field access):

// After AudioRuntimeData.Initialize() completes:
[HarmonyPatch(typeof(AudioRuntimeData), "Initialize")]
[HarmonyPostfix]
static void InjectCustomAudioMappings(AudioRuntimeData __instance)
{
    // 1. Add event mapping: eventId → soundbank name
    //    Access the internal RuntimeEventMapProvider and its dictionary
    //    Add entry: { YOUR_EVENT_ID_UINT → "CUSTOM_1832_pre" }

    // 2. Add soundbank mapping: soundbank name → .pck filename
    //    Access the internal RuntimeSoundbanksMapProvider and its dictionary
    //    Add entry: { "CUSTOM_1832_pre" → "custom_cards_mod.pck" }
}
```

The tricky part is accessing the private dictionaries inside the map providers. With
Il2CppInterop (MelonLoader), you can typically access private fields as properties or
use `Il2CppSystem.Reflection` to get at them. You may need to explore the Il2Cpp class
structure at runtime using something like:

```csharp
// Explore fields at runtime to find the dictionary:
var type = audioRuntimeData.GetType();
foreach (var field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
{
    MelonLogger.Msg($"Field: {field.Name} Type: {field.FieldType}");
}
```

#### Hook: Inject into CardAudioRuntimeData

After `CardAudioRuntimeData.Initialize()` runs, you need to add a custom `CardAudioWrapper`
for your card's AudioId. This replaces the current donor-AudioId approach.

```csharp
[HarmonyPatch(typeof(CardAudioRuntimeData), "Initialize")]
[HarmonyPostfix]
static void InjectCustomCardAudio(CardAudioRuntimeData __instance)
{
    // Create a CardAudio object programmatically:
    // - Id = <your chosen AudioId, e.g. 900001 to avoid conflicts>
    // - Triggers: [{ TriggerType = PremiumCardPreview(6), TriggerSfx = Premium(2) }]
    // - SoundEffects: [{ EffectType = Premium(2), EventId = YOUR_EVENT_ID }]

    // Wrap it:
    // var wrapper = new CardAudioWrapper(cardAudio);

    // Add to the internal dictionary:
    // __instance.m_CardAudioCollection[900001] = wrapper;

    // Then in Hook 0 (DefinitionsLoaded), set:
    // template.Template.AudioId = 900001;
}
```

**Note on Il2Cpp constructors:** Creating new instances of Il2Cpp game classes can be
tricky. If `CardAudio` doesn't have a public parameterless constructor accessible from
the mod, you might need to:
1. Use `Il2CppSystem.Activator.CreateInstance()`, or
2. Allocate with `ClassInjector`, or
3. Find an existing CardAudio in the dictionary and clone/modify it

The simplest fallback is to find a CardAudio entry that has the structure you want,
copy its reference, and modify the EventId fields to point at your custom event.

#### Summary of Required Hooks

| Hook Target | When | What to Inject |
|-------------|------|----------------|
| `AudioRuntimeData.Initialize()` | After JSON maps loaded | Event→soundbank and soundbank→pck mappings |
| `CardAudioRuntimeData.Initialize()` | After CardAudio XML loaded | Custom CardAudioWrapper with your EventId |
| `SharedData` (existing Hook 0) | After definitions loaded | Set `template.Template.AudioId` to your custom AudioId |

### 3.4 Alternative: Direct Soundbank Loading (Skip the .pck)

If creating a `.pck` proves too difficult, you can try loading a standalone `.bnk` directly
by hooking the loading process:

```csharp
// Instead of going through the normal .pck → .bnk pipeline,
// directly call AkSoundEngine.LoadBank with a file path:

string customBankPath = Path.Combine(
    Application.streamingAssetsPath, "audio", "CUSTOM_1832_pre.bnk");

uint bankId;
AKRESULT result = AkSoundEngine.LoadBank(
    customBankPath,
    AkSoundEngine.AK_DEFAULT_POOL_ID,
    out bankId
);

if (result == AKRESULT.AK_Success)
{
    MelonLogger.Msg($"Custom soundbank loaded, bankId: {bankId}");
}
```

You'd call this early in the mod's initialization (e.g., in `OnSceneWasLoaded` or after
the SoundManager is initialized). You still need the event map injection so the game's
`SoundbankManager` doesn't try to load it again through the normal pipeline.

### 3.5 Alternative: Skip Wwise Entirely (Nuclear Option)

If dealing with Wwise tooling is too burdensome, there's a completely different approach:
use Unity's `AudioSource` to play audio independently of Wwise. This bypasses the entire
Wwise pipeline:

```csharp
// Load an AudioClip from a file on disk
byte[] audioData = File.ReadAllBytes(customAudioPath);
// Use a WAV loader library or NAudio to decode
// Create a Unity AudioSource on the card's GameObject
// Play it when the premium preview opens

// You'd hook UISidePreviewCardSoundHandler.StartCardSounds or
// CardViewSfxHandler.PlayPremiumPreview to trigger your custom playback
```

**Downsides:** Won't integrate with Wwise's mixing/volume system, won't respect the
game's audio settings, and requires managing your own AudioSource lifecycle. But it
avoids all Wwise tooling entirely.

---

## Part 4: Step-by-Step Checklist

When you're ready to implement custom sounds, follow this checklist:

### Preparation (one-time)
- [ ] Install Wwise Authoring Tool (2021.1.x recommended)
- [ ] Install Wwise File Packager
- [ ] Prepare your audio file (WAV, seamless loop, ~10-30 seconds)

### Create Audio Assets
- [ ] Create Wwise project `GwentCustomAudio`
- [ ] Import audio file as Sound SFX object
- [ ] Set looping to infinite (for ambient premium sounds)
- [ ] Set encoding to Vorbis quality 4-6
- [ ] Create Play event: `Play_custom_premium_1832`
- [ ] Create Stop event: `Stop_custom_premium_1832`
- [ ] Create SoundBank: `CUSTOM_1832_pre`
- [ ] Add both events to the soundbank
- [ ] Generate soundbank (outputs `.bnk` + `.wem` files)
- [ ] Package into `custom_cards_mod.pck` using File Packager

### Compute IDs
- [ ] Run the Python `wwise_short_id()` function on your event names
- [ ] Record the Play event ID and Stop event ID

### Deploy
- [ ] Copy `custom_cards_mod.pck` to `Gwent_Data\StreamingAssets\audio\`
- [ ] Add this copy step to `build.py`

### Mod Code Changes
- [ ] Add Harmony postfix on `AudioRuntimeData.Initialize()`:
  - Inject event ID → `"CUSTOM_1832_pre"` into event map
  - Inject `"CUSTOM_1832_pre"` → `"custom_cards_mod.pck"` into soundbank map
- [ ] Add Harmony postfix on `CardAudioRuntimeData.Initialize()`:
  - Create/clone a `CardAudioWrapper` with your event IDs
  - Insert into the card audio dictionary with a custom AudioId
- [ ] Update Hook 0 (DefinitionsLoaded):
  - Set `template.Template.AudioId` to your custom AudioId (instead of donor's)
- [ ] Test: verify the sound plays when viewing the premium card
- [ ] Test: verify the sound stops when closing the preview

---

## Part 5: Reference Data

### Current Donor Card Audio (Elven Wardancer)

For reference, here's the Wardancer's audio data that we're currently borrowing:

| Field | Value |
|-------|-------|
| Card | Elven Wardancer (ArtId 1222) |
| Soundbank (normal) | `SCO_EP0_elven_wardancer` |
| Soundbank (premium) | `SCO_EP0_elven_wardancer_pre` |
| Normal event ID | `3713661207` |
| Premium event ID | `1787157805` |
| Package file | `cards_ep0_baseset_sco.pck` |

### Gwent's Naming Conventions

| Pattern | Meaning | Example |
|---------|---------|---------|
| `{FACTION}_EP{N}_{name}` | Normal card soundbank | `SCO_EP0_elven_wardancer` |
| `{FACTION}_EP{N}_{name}_pre` | Premium card soundbank | `SCO_EP0_elven_wardancer_pre` |
| `cards_ep{n}_baseset_{faction}.pck` | Base set package per faction | `cards_ep0_baseset_sco.pck` |
| `cards_EP{N}.pck` | Expansion package (all factions) | `cards_EP1.pck` |

Faction codes: `SCO` (Scoia'tael), `MON` (Monsters), `NIL` (Nilfgaard),
`NOR` (Northern Realms), `SKE` (Skellige), `SYN` (Syndicate), `NEU` (Neutral)

### Key Source Code Files

All paths relative to `D:\Gwent_Source_Code\Gwent\Gwent\GwentUnity\Gwent\_ExcludedCode\Code_Ignored\`:

| File | Purpose |
|------|---------|
| `Visuals\Audio\CardAudio.cs` | CardAudio data class (Id, Triggers, SoundEffects) |
| `Visuals\Audio\CardSoundEffect.cs` | Holds EventId and EffectType |
| `Visuals\Audio\CardAudioTrigger.cs` | Maps trigger type → sound effect type |
| `Visuals\Audio\CardAudioWrapper.cs` | Runtime lookup wrapper |
| `Visuals\Audio\SoundEffect.cs` | Base class with `int EventId` |
| `Unity\Audio\Data\CardAudioRuntimeData.cs` | Singleton dictionary of all card audio |
| `Unity\Audio\Data\AudioRuntimeData\RuntimeEventMapProvider.cs` | JSON → eventId→soundbank dict |
| `Unity\Audio\Data\AudioRuntimeData\RuntimeSoundbanksMapProvider.cs` | JSON → soundbank→pck dict |
| `Unity\Audio\Core\WwiseShortIdGenerator.cs` | FNV-1 hash for event name → ID |
| `Unity\Audio\SoundbankManagement\SoundbankManager.cs` | Loads .pck and .bnk via AkSoundEngine |
| `Unity\Audio\SoundbankManagement\SoundbankWrapper.cs` | Ref-counted soundbank wrapper |
| `Unity\Audio\SoundbankManagement\AudioFilePackageWrapper.cs` | Ref-counted .pck wrapper |
| `Unity\Audio\Managers\CardAudioManager\CardSfxManager.cs` | Resolves triggers → PostEvent() |
| `Unity\Audio\Components\CardView\CardViewSfxHandler.cs` | PlayPremiumPreview / StopPremiumPreview |
| `Unity\Audio\Components\UI\Generic\UISidePreviewCardSoundHandler.cs` | UI-level premium sound trigger |
| `Unity\Cards\Factories\CardAppearance\BasicRequests\CardSoundbankRequest.cs` | Pre-loads soundbank for card |

### Enum Values

```
ECardAudioTriggerType:
  Invalid              = 0
  AmbushCardRevealed   = 2
  CardPlacedOnBoard    = 3
  PremiumCardPreview   = 6

ECardSoundEffectType:
  None     = 0
  Standard = 1
  Premium  = 2
  Ambush   = 3
```
