# Enhanced Astronaut Complex (EAC)

Enhanced Astronaut Complex (EAC) adds deeper Kerbonaut lifecycle management to Kerbal Space Program 1.12.x by extending the stock Astronaut Complex with long-term crew development, training, aging, retirement, recovery leave, career history, and optional Contract Configurator final exams supplied through the separate EAC Contract Configuration add-on.

EAC is designed to feel stock-like while making career-mode crew decisions matter. It can run as a self-contained career crew-management mod, while still deferring to supported specialist mods when they are actually installed and loaded.

## What EAC Adds

EAC expands the stock Astronaut Complex into a career-management hub for Kerbals. It tracks training, service history, aging, retirement, recovery/rest time, roster state, and optional recognition systems. It also adds retired/lost roster handling, Hall of History presentation, optional Contract Configurator final-exam support, and EAC 1.5 calendar-compatibility and crew-management tools.

## Why Use EAC by Itself Instead of Adding Crew R&R or Earn Your Stripes?

Crew R&R and Earn Your Stripes have always been optional. EAC can run without either of them. EAC 1.5 includes EAC-native versions of the major overlapping systems those mods provide, plus additional EAC-specific career-management features. For many players, that means there is no need to add two more mods just to get recovery leave, veteran recognition, suit presentation, or starting crew control.

EAC's advantage is integration:

- One mod can manage aging, training, retirement, recovery leave, veteran recognition, suit presentation, Badass progression, starting crew setup, roster filtering, notifications, and career history.
- EAC combines these systems inside the Astronaut Complex, Hall of History, Message App categories, and EAC save records instead of splitting related crew-career behavior across multiple mods.
- EAC adds features beyond the Crew R&R / Earn Your Stripes overlap, including training and final exams, retirement and retired-death handling, retired/lost roster tabs, assignment-duration display, legacy save cleanup, DeepFreeze-aware mission-time handling, and Suggested Next Crew.
- EAC can work in a lightweight advisory style. Suggested Next Crew recommends Kerbals without enforcing morale, refusal, hard rotation rules, or automatic crew-slot filling.
- EAC respects existing specialist mods. If Crew R&R or Earn Your Stripes is installed and loaded, EAC defers overlapping behavior to them instead of fighting them.

EAC does not try to copy every design choice from Crew R&R or Earn Your Stripes. Instead, it provides built-in EAC-native alternatives for players who prefer one integrated crew-management experience.

Important: EAC only delegates when the other mod's DLL is actually loaded by KSP. 

## Main Features

### Kerbal Training

- Train Kerbals over time instead of promoting them instantly.
- Training time scales by target experience level.
- Training costs can use funds, science, or both, depending on difficulty settings.
- Training notifications can be routed to the HUD, the KSP Message System, or both.
- Optional final exam support can require Kerbals to complete practical Contract Configurator exams before receiving their next EAC training level.
- Training state is kept out of the Available roster and stock assignment dialogs.

### Aging and Retirement

- Kerbals age over time using the active game calendar when available, with supported stock Kerbin-time and Earth-time fallback behavior.
- Retirement age ranges are configurable.
- Current default retirement values:
  - Retire minimum: 37
  - Retire maximum: 47
  - Retired death minimum: 50
- Retired Kerbals are removed from the active roster and moved into EAC's retired/lost roster handling.
- Retired Kerbals may be recalled if rules and Astronaut Complex capacity allow it.
- Retired Kerbals can lose effective experience over time while retired.
- Optional retired-death and mission-old-age-death settings are available.

### Calendar Compatibility

EAC 1.5 improves compatibility with custom KSP calendars.

- EAC uses the active game's calendar day and year lengths when custom values are available.
- Stock Kerbin-time and Earth-time behavior remain supported.
- Custom calendars used by Kronometer, JNSQ, and rescaled Kopernicus/Sigma Dimensions installations can keep EAC age and date displays aligned with the rest of the game.
- If a custom calendar cannot be resolved, EAC falls back to its supported stock time basis instead of failing.
- Calendar support affects EAC's calendar-based age/date presentation and related lifecycle timing conversions; it does not alter the save's universal time.

### Astronaut Complex Roster Management

- Adds EAC-aware roster handling to the stock Astronaut Complex.
- Reorders and manages roster tabs for Available, Assigned, Retired, and Lost views.
- Keeps Assigned, Retired, Lost/KIA/Dead, Training, Recovering, Frozen, and otherwise unavailable Kerbals out of the Available tab.
- Adds a synthetic Retired tab while safely avoiding unsafe stock UI activation paths for custom tabs.
- Shows assignment duration in the Assigned tab.
- Keeps the Recall-button tooltip reliable regardless of which direction the pointer enters the button.
- Switches cleanly between the Recall tooltip and the normal Kerbal-information popup while moving across a retired crew row.
- Uses targeted recall and deferred EAC-only retirement refreshes so recalling or retiring a Kerbal does not duplicate or rebuild the native Available list.
- Rebinds retired-row crew tooltips after UI updates so newly retired Kerbals and surviving rows show their information immediately without closing and reopening the Astronaut Complex.
- Re-applies tab ownership while the Astronaut Complex is open so KSP list rebuilds do not leak rows into the wrong tab.
- Keeps the watchdog/filter logic limited to Astronaut Complex use instead of running constantly.

### Applicant Management

- Applicant rejection is handled through EAC's Astronaut Complex integration.
- Reject All uses a stable applicant snapshot so applicants are not skipped while the roster is changing.
- Applicant rejection is guarded so it only acts on valid applicant Kerbals.

### Crash, Recovery, and Crew Rest Handling

- Crash severity can apply recovery penalties or force retirement depending on settings.
- EAC can apply configurable post-mission recovery leave when Crew R&R is not loaded.
- Recovery leave is based on mission time and the configured recovery percentage, capped by RestDay Max.
- Very short positive recovery leave is given a minimum visible recovery floor so crew do not appear immediately available after tiny flights when recovery is enabled.
- Recovery state is persisted using EAC records and rehydrated if KSP clears stock inactive state during scene changes.
- Space Center, Tracking Station, map-style, and focused-vessel recovery paths are handled consistently.
- DeepFreeze frozen time is excluded from recovery fatigue. Awake mission time before and after freezing is accumulated and counted.
- If Crew R&R is installed as a loaded assembly, EAC defers recovery leave handling to Crew R&R.

### Suggested Next Crew Advisor

EAC includes an optional Suggested Next Crew Advisor for the VAB/SPH.

The advisor is intentionally advisory only:

- It does not auto-fill stock crew slots.
- It does not enforce rotation.
- It does not add morale or refusal rules.
- It does not override the player's crew choices.

Recommendation labels include:

1. Needs experience
2. Due for flight
3. Long service priority
4. Recently flew

Suggested Next Crew is disabled/delegated when Crew R&R is installed as a loaded assembly.

### EAC-Native Veteran, Suit, and Badass Progression

When Earn Your Stripes is not loaded, EAC can provide built-in career recognition features:

- Configurable veteran requirements.
- Veteran eligibility based on flight count, flight hours, optional milestone requirement, and optional class restrictions.
- Existing-save evaluation so Kerbals who already meet the requirements can be promoted retroactively.
- Optional default and veteran suit presentation.
- Optional Badass progression, conservative/off by default.
- Badass progression can require veteran status and milestone qualification.
- Badass milestone rolls are tracked so save reloads cannot repeatedly reroll the same milestone.
- Veteran and Badass recognition notifications can appear through HUD and Message App paths.

When Earn Your Stripes is installed as a loaded assembly, EAC disables/delegates its native veteran, suit, and starting crew logic to avoid conflicts.

### New-Game Starting Crew Setup

When Earn Your Stripes is not loaded, EAC can show a startup crew setup window for new saves.

Options include:

- Keep Default Crew.
- Replace Default Crew.
- Male / Female / Both gender filters.
- Pilot / Engineer / Scientist class filters.
- Starting crew count.
- Guaranteed class coverage when enough crew slots are available.

For example, when all three classes are selected and the starting crew count is three or more, EAC guarantees at least one Pilot, one Engineer, and one Scientist.

The startup dialog is centered and can reappear correctly for subsequent new saves created in the same KSP session.

### Hall of History

EAC includes Hall of History style presentation for career records, including:

- Memorial-style handling for fallen Kerbals.
- Portrait support.
- Career and service history presentation.
- Veteran/status display where applicable.
- Reduced repaint-time allocations by caching milestone day-group counts, memorial-name lookups, and preformatted memorial role, service, metrics, and summary text during data refresh.
- Preserved the existing Hall data rules, sorting, filtering, layout, and visible wording.

### Performance and Internal Stability

EAC 1.5 reduces repeated reflection and optional-mod discovery work in frequently refreshed UI paths.

- `ReflectionUtils.FindField` and `ReflectionUtils.FindProperty` use lock-protected process-lifetime caches keyed by the target type and ordered candidate member names.
- Both successful reflection lookups and misses are cached, preventing the same unavailable member from being searched repeatedly.
- Repeated Astronaut Complex row, tooltip, and badge member lookups now use the shared reflection cache while preserving the existing lookup order, value-setting logic, event wiring, and fallbacks.
- A shared optional-mod registry caches assembly and bridge-type discovery for Earn Your Stripes, Crew R&R / CrewQueueTwo, Contract Configurator, and the EAC Contract Configurator bridge.
- Astronaut Complex badge code was moved into a dedicated source file without changing the existing tab entry points, tab creation, list-anchor discovery, or native-list ownership behavior.
- These changes do not alter EAC settings, save formats, public/internal method signatures, or normal gameplay rules.

### Notifications

Notification categories can be enabled or disabled independently, including:

- Birthdays.
- Training.
- Retirement.
- Deaths.
- Recovery/rest events.
- Veteran recognition.
- Badass recognition.

Notifications can appear in:

- the HUD,
- the KSP Message System,
- or both.

Message App categories can be reset when Message App support is re-enabled.

## Optional Mod Integrations

EAC is designed to run by itself, but it can integrate with several other mods.

### HarmonyKSP / Harmony2

HarmonyKSP is required. Manual installs should include:

```text
GameData/000_Harmony/
```

CKAN installs should include the Harmony2 dependency.

### Contract Configurator and the EAC Contract Configuration Add-on

Beginning with version 1.5.0, EAC is released as two coordinated packages:

1. **Enhanced Astronaut Complex (EAC Core)** — the required base mod. It contains `EAC.dll` and has no Contract Configurator dependency.
2. **EAC Contract Configuration** — an optional add-on for players who want Contract Configurator final exams. It contains the active bridge DLL and the exam contracts/content.

The optional add-on requires:

- EAC Core 1.5.0,
- Contract Configurator,
- and the normal EAC Core dependency on HarmonyKSP / Harmony2.

Install the add-on only when Contract Configurator is installed. The add-on merges into the existing `GameData/EAC/` folder and provides:

```text
GameData/EAC/Plugins/EAC_CCBridge.dll
GameData/EAC/Contracts/
GameData/EAC/Agencies/
GameData/EAC/Craft/
GameData/EAC/Scenarios/
```

The old `EAC_CCBridge.dll.disabled` rename workflow is no longer used for 1.5.0 releases. EAC Core does not ship the bridge. Players who do not use Contract Configurator install only EAC Core and take no additional action.

When EAC Core, Contract Configurator, and EAC Contract Configuration are installed, EAC can use Contract Configurator contracts as final exams for training advancement. EAC tracks which Kerbal needs a final exam, the Kerbal's trait, the target level, and the final exam state. Contract Configurator owns the contract objectives and completion. After the contract completes, EAC reconciles the Kerbal's EAC training level.

If final exams are disabled after a Kerbal has entered the final-exam path, or if Contract Configurator/the add-on is later removed, EAC attempts to recover the Kerbal back into the normal EAC training-award path.

When upgrading from EAC 1.4.x or earlier:

- remove any stale `GameData/EAC/Plugins/EAC_CCBridge.dll.disabled`,
- install EAC Core 1.5.0,
- and install EAC Contract Configuration 1.5.0 only if Contract Configurator is present.

For contract authors, see:

```text
README_EAC_CC_Final_Exams.md
```

### Scenario Vessel Loading for Final Exams

Some Contract Configurator exams can use EAC-provided scenario vessels. This allows a contract to place a prepared test article into the save instead of requiring the player to build it manually.

A final exam contract can use EAC's scenario-loading behavior to:

- load a saved scenario vessel from `GameData/EAC/Scenarios/`,
- insert that vessel into the current save,
- associate the spawned vessel with the Contract Configurator exam,
- optionally clean up the spawned vessel after the contract ends,
- protect crewed vessels from unsafe cleanup.

This is separate from normal `.craft` file provisioning. `.craft` files are placed in the hangar for the player to use. Scenario vessels are inserted into the active save for a specific contract flow.

### Crew R&R

Crew R&R is optional.

When Crew R&R is not installed, EAC can provide its own post-mission recovery leave and Suggested Next Crew recommendations.

When Crew R&R is installed as a loaded assembly, EAC defers recovery leave behavior to Crew R&R and disables/delegates Suggested Next Crew to avoid conflicting career-rotation systems.

### Earn Your Stripes and FlightTracker

Earn Your Stripes and FlightTracker are optional.

When Earn Your Stripes is not installed, EAC can provide native veteran recognition, suit presentation, and starting crew setup.

When Earn Your Stripes is installed as a loaded assembly, EAC defers veteran/suit/starting crew behavior to Earn Your Stripes.

When FlightTracker is installed, EAC can prefer FlightTracker's flight-history data where appropriate.

Flight Tracker is optional but recommended if you want EAC veteran eligibility or career history displays to use long-term recorded mission hours.

Flight Tracker runs independently of Earn Your Stripes. If Earn Your Stripes is not installed but Flight Tracker remains installed, EAC can still read Flight Tracker’s recorded flight count and mission-hour data.

If Flight Tracker is not installed, EAC can still use its own flight-count records, but total historical mission-hour requirements should be set to 0 unless/until EAC gains its own full mission-hour tracking.

### DeepFreeze

DeepFreeze is optional.

When DeepFreeze is installed, EAC treats frozen Kerbals as an external lifecycle state. Frozen Kerbals are not treated as normal active, missing, or deceased crew by EAC.

DeepFreeze frozen time does not count toward EAC recovery fatigue. EAC accumulates awake mission time before and after freezing so long-duration cryosleep does not create year-long rest penalties, while meaningful awake mission time still counts.

### Kerbal Changelog

Kerbal Changelog is optional.

`GameData/EAC/Changelog.cfg` provides in-game release notes when Kerbal Changelog is installed. EAC does not require Kerbal Changelog to run.

## Installation

### EAC Core

1. Install HarmonyKSP / Harmony2.
2. Download the EAC Core 1.5.0 release.
3. Copy the included `GameData/EAC/` folder into the KSP `GameData/` directory.
4. Start KSP and open a save.
5. Review EAC settings under Difficulty Options before starting a long career save.

A core-only install should look like this:

```text
Kerbal Space Program/
  GameData/
    000_Harmony/
    EAC/
      Changelog.cfg
      Plugins/
        EAC.dll
```

### Optional EAC Contract Configuration

Install this package only when Contract Configurator is installed.

1. Install EAC Core 1.5.0.
2. Install Contract Configurator.
3. Copy the EAC Contract Configuration 1.5.0 `GameData/EAC/` folder into `GameData/`, merging it with the core EAC folder.
4. Confirm that `GameData/EAC/Plugins/EAC_CCBridge.dll` exists.
5. Do not rename any DLL.

A combined install should look like this:

```text
Kerbal Space Program/
  GameData/
    000_Harmony/
    ContractConfigurator/
    EAC/
      Changelog.cfg
      Plugins/
        EAC.dll
        EAC_CCBridge.dll
      Contracts/
      Agencies/
      Craft/
      Scenarios/
```

Only one copy of EAC Core and one copy of the matching EAC Contract Configuration package should be installed at a time.

## Settings

EAC settings are available at:

```text
Space Center -> Settings -> Difficulty Options -> EAC
```

EAC keeps the basic Difficulty Settings screen more compact and moves detailed/low-frequency options into an EAC Advanced Settings window.

Advanced Settings can be opened from the EAC Space Center window.

Configurable areas include:

- Calendar/time behavior, including active custom-calendar compatibility.
- Training time and cost rules.
- Funds and science scaling.
- Aging on or off.
- Retirement ages.
- Retired-death behavior.
- Mission old-age death behavior.
- Crash recovery behavior.
- Recovery leave percentage and RestDay Max.
- Suggested Next Crew.
- Message App categories.
- Veteran recognition.
- Suit presentation.
- Badass progression.
- Starting crew setup.
- Contract Configurator final exam behavior when the EAC Contract Configuration add-on is installed.
- Debug and verbose logging.

Settings are saved into the save file and persist across reloads.

## Save Data and Migration

EAC stores per-Kerbal records and settings in the save file under EAC scenario data.

Typical stored items include:

- birth time and aging information,
- training state and training completion time,
- final exam pending or active state,
- retirement status and retirement time,
- experience at retirement,
- death or lost status handled by EAC,
- recovery/rest state,
- accumulated awake mission time used for DeepFreeze-aware recovery,
- veteran/suit/Badass tracking,
- notification and feature toggles,
- final exam history used for exam rotation.

EAC 1.4 migrates older save data from the old `RosterRotationScenario` name to `EACScenario`.

If EAC finds legacy data-bearing save information, it backs up the persistent file before cleanup and shows a Space Center notice. Empty legacy scenario stubs are removed silently to avoid future confusion.

If manually editing saves, make a backup first.

## Compatibility Notes

- Built for Kerbal Space Program 1.12.x.
- Requires HarmonyKSP / Harmony2.
- Intended for the stock Astronaut Complex UI.
- Other mods that heavily replace or rebuild the Astronaut Complex UI may conflict with EAC roster-tab adjustments.
- EAC attempts to re-sync roster rows after KSP rebuilds Astronaut Complex lists.
- Contract Configurator is optional and is not required by EAC Core.
- Final exam contract mode requires the separate, matching-version EAC Contract Configuration package and Contract Configurator.
- EAC Core 1.5.0 does not ship `EAC_CCBridge.dll` or `EAC_CCBridge.dll.disabled`.
- The EAC Contract Configuration package ships the active `EAC_CCBridge.dll` and must not be installed without Contract Configurator.
- Custom calendar support is designed for stock calendars and calendar providers used by setups such as Kronometer, JNSQ, and rescaled Kopernicus systems.
- Crew R&R, Earn Your Stripes, FlightTracker, DeepFreeze, and Kerbal Changelog are optional.
- EAC delegates to Crew R&R or Earn Your Stripes only when those mods are actually loaded as assemblies.

If Astronaut Complex UI oddities appear, check for:

- more than one installed copy of EAC,
- another mod patching Astronaut Complex roster lists or buttons,
- missing or mismatched EAC plugin files,
- missing HarmonyKSP,
- errors in `KSP.log` beginning with `[EAC]`.

## Known Notes

- A very brief roster-row flash can occur when KSP rebuilds Astronaut Complex lists. EAC re-applies its roster cleanup after the rebuild.
- Some Astronaut Complex UI layouts may not expose every stock list in the same way. EAC handles missing lists as gracefully as possible.
- Facility upgrades can cause the Astronaut Complex UI to rebuild. EAC attempts to re-sync hiring and roster behavior afterward.
- Mission old-age death is controlled separately from retired-death behavior. A Kerbal assigned to an unlaunched vessel will only be eligible for mission old-age death when that setting is enabled.
- Suggested Next Crew is advisory-only. It does not auto-populate stock crew slots.

## Troubleshooting

### Logs

Check `KSP.log` for lines beginning with:

```text
[EAC]
```

Useful final exam search terms include:

```text
EACGraduation
EACGraduationAward
EACGraduationExamPending
EACLoadScenario
ContractConfigurator
Unknown requirement
Unknown behaviour
Unknown parameter
No contract group with name
NullReferenceException
Exception
```

### Verbose Logging

Verbose logging can be enabled in EAC Advanced Settings.

Use verbose logging only while troubleshooting because it can create more log output.

### Contract Configurator Exams Do Not Appear

Check that:

- EAC Core and EAC Contract Configuration are both version 1.5.0,
- Contract Configurator is installed,
- `GameData/EAC/Plugins/EAC_CCBridge.dll` exists,
- no stale `EAC_CCBridge.dll.disabled` from an older release is being mistaken for the active bridge,
- the EAC Contract Configuration `Contracts`, `Agencies`, `Craft`, and `Scenarios` content was copied into `GameData/EAC/`,
- the contract group exists,
- the contract uses the expected EAC requirement and behaviour blocks,
- the contract's trait, target level, and exam ID match the pending Kerbal's final exam state.

### Scenario Vessel Does Not Load

Check that:

- the scenario file is packaged under `GameData/EAC/Scenarios/`,
- the contract references the correct scenario file path,
- the scenario file contains a valid vessel node,
- the contract uses the EAC scenario-loading behavior,
- `KSP.log` does not show scenario-load or vessel-association errors.

### A Kerbal Is Stuck in Training or Final Exam State

Check whether final exam mode was disabled, Contract Configurator was removed, or the EAC Contract Configuration add-on was removed after the Kerbal entered the exam path. EAC includes recovery handling for this case, but the save may need to be loaded at the Space Center for EAC to reconcile the state.

### Crew R&R or Earn Your Stripes Options Are Unavailable

This is expected if the corresponding mod DLL is installed and loaded. EAC disables/delegates overlapping features to avoid conflicts.

A ZIP file in `GameData` should not trigger delegation by itself. The mod assembly must actually be loaded by KSP.

## Packaging Checklist

### EAC Core 1.5.0

The core release should include at least:

```text
GameData/EAC/Changelog.cfg
GameData/EAC/Plugins/EAC.dll
EAC.version
README.md
CHANGE_LOG.md
```

The core release should not include:

```text
GameData/EAC/Plugins/EAC_CCBridge.dll
GameData/EAC/Plugins/EAC_CCBridge.dll.disabled
GameData/EAC/Contracts/
GameData/EAC/Agencies/
GameData/EAC/Craft/
GameData/EAC/Scenarios/
```

### EAC Contract Configuration 1.5.0

The optional add-on should include its bridge and Contract Configurator content:

```text
GameData/EAC/Plugins/EAC_CCBridge.dll
GameData/EAC/Contracts/*.cfg
GameData/EAC/Agencies/*.cfg
GameData/EAC/Agencies/*.png
GameData/EAC/Craft/*.craft
GameData/EAC/Scenarios/*.cfg
EAC_CC_README_FIRST.md
```

Both release packages should identify version 1.5.0 and should be published together so users do not mix incompatible core and bridge builds.

Do not include development artifacts such as:

```text
bin/
obj/
*.bak.cs
*.pdb
*.user
KSP.log
temporary patch files
```

## License

This mod is licensed under a [Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International License](https://creativecommons.org/licenses/by-nc-nd/4.0/).

[![CC BY-NC-ND 4.0](https://licensebuttons.net/l/by-nc-nd/4.0/88x31.png)](https://creativecommons.org/licenses/by-nc-nd/4.0/)

You are free to:

- download and use this mod personally,
- share or redistribute the original mod unchanged for non-commercial purposes.

You are not allowed to:

- modify, adapt, or create derivative works from the code or assets,
- use any part of this mod for commercial purposes.

By downloading or using this mod, you agree that:

- you use this mod at your own risk,
- the author is not liable for issues, damages, or problems that may occur from using it,
- you agree to hold the author harmless from claims or liability,
- if you share the mod, you must give appropriate credit to the original author, provide a link to the license, and indicate if any changes were made,
- you may not remove or alter copyright or license information.

Copyright © 2026 ItchyBrother. All Rights Reserved except as expressly licensed above.

## Credits

- Kerbal Space Program by Squad / Private Division.
- Thanks to the KSP modding community.
- Thanks to KSP forum user edgomes27 for beta testing EAC 1.3.0.
- Thanks to linuxgurugamer, whose work indirectly inspired parts of EAC's crew-management direction.
- Thanks to severedsolo for the Earn Your Stripes and FlightTracker mods, which EAC can integrate with when present.
- Thanks to JPLRepo / DeepFreeze Continued maintainers for the DeepFreeze ecosystem that EAC can detect and respect when present.
