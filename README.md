# Enhanced Astronaut Complex (EAC)

Enhanced Astronaut Complex (EAC) adds deeper Kerbonaut lifecycle management to Kerbal Space Program by extending the Astronaut Complex with long-term crew development, aging, retirement, training, and optional Contract Configurator final exams.

EAC is designed to feel stock-like while adding meaningful career-mode crew management decisions.

## Main Features

### Kerbal Training

- Train Kerbals over time instead of promoting them instantly.
- Training time scales by target experience level.
- Training costs can use funds, science, or both, depending on difficulty settings.
- Training notifications can be routed to the HUD, the KSP Message System, or both.
- Optional final exam support can require Kerbals to complete practical Contract Configurator exams before receiving their next EAC training level.

### Aging and Retirement

- Kerbals age over time using Kerbin time or Earth time settings.
- Retirement age ranges are configurable.
- Retired Kerbals are removed from the active roster and moved into EAC's retired/lost roster handling.
- Retired Kerbals may be recalled if rules and Astronaut Complex capacity allow it.
- Retired Kerbals can lose effective experience over time while retired.
- Optional retired-death and mission-old-age-death settings are available.

### Astronaut Complex Roster Management

- Adds EAC roster handling to the Astronaut Complex.
- Retired Kerbals are kept out of the Available roster.
- Dead, missing, and EAC-deceased Kerbals are kept out of the Available roster.
- The LOST tab shows useful age-at-death information without showing an unnecessary current-age value for deceased Kerbals.
- EAC attempts to re-apply roster cleanup after KSP rebuilds Astronaut Complex UI lists.

### Applicant Management

- Applicant rejection is handled through EAC's Astronaut Complex integration.
- Reject All uses a stable applicant snapshot so applicants are not skipped while the roster is changing.
- Applicant rejection is guarded so it only acts on valid applicants.

### Crash, Recovery, and Crew Rest Handling

- Crash severity can apply recovery penalties or force retirement depending on settings.
- Crew rest after flight can be handled by EAC.
- EAC can defer to supported external crew-management mods when they are installed.

### Notifications

Notification categories can be enabled or disabled independently:

- birthdays,
- training,
- retirement,
- deaths,
- recovery/rest events.

Notifications can appear in:

- the HUD,
- the KSP Message System,
- or both.

## Optional Mod Integrations

EAC is designed to run by itself, but it can integrate with several other mods.

### Contract Configurator

Contract Configurator is optional.

When Contract Configurator and the EAC CC bridge are installed, EAC can use Contract Configurator contracts as final exams for training advancement. EAC tracks which Kerbal needs a final exam, the Kerbal's trait, the target level, and the final exam state. Contract Configurator owns the contract objectives and completion. After the contract completes, EAC reconciles the Kerbal's EAC training level.

If Contract Configurator is not installed, EAC should still load without a hard dependency error. Final exam contract mode will simply be unavailable.

If final exams are disabled after a Kerbal has already entered the final-exam path, or if Contract Configurator is removed, EAC attempts to recover the Kerbal back into the normal EAC training-award path.

For contract authors, see the dedicated final-exam documentation:

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

### CrewRandR

EAC can work alongside CrewRandR by linuxgurugamer. When CrewRandR is installed, EAC gives preference to that mod where appropriate instead of duplicating crew-rest behavior.

### Earn Your Stripes and FlightTracker

EAC can also work alongside Earn Your Stripes and FlightTracker by severedsolo. When these mods are installed, EAC gives preference to them where appropriate instead of duplicating promotion or flight-history behavior.

## Installation

1. Download the release zip.
2. Copy the `GameData/EAC/` folder into your KSP `GameData/` directory.
3. Start KSP and open a save.
4. Review EAC settings under Difficulty Options before starting a long career save.

A typical install should look like this:

```text
Kerbal Space Program/
  GameData/
    EAC/
      Plugins/
        EAC.dll
        EAC_CCBridge.dll        # optional bridge, used when Contract Configurator is installed
      Contracts/                # optional Contract Configurator exam contracts
      Agencies/                 # optional agencies for final exam contracts
      Craft/                    # optional provided craft files
      Scenarios/                # optional scenario vessels for selected exams
```

Only one copy of EAC should be installed at a time.

## Settings

EAC settings are available at:

```text
Space Center → Settings → Difficulty Options → EAC
```

From there, the following can be configured:

- Kerbin time or Earth time,
- training time and cost rules,
- funds and science scaling,
- aging on or off,
- retirement ages,
- retired-death behavior,
- mission old-age death behavior,
- crash recovery behavior,
- notification routing and categories,
- Contract Configurator final exam behavior,
- debug and verbose logging.

Settings are saved into the save file and persist across reloads.

## Save Data

EAC stores per-Kerbal records and settings in the save file under EAC scenario data.

Typical stored items include:

- birth time and aging information,
- training state and training completion time,
- final exam pending or active state,
- retirement status and retirement time,
- experience at retirement,
- death or lost status handled by EAC,
- notification and feature toggles,
- final exam history used for exam rotation.

If manually editing saves, make a backup first.

## Compatibility Notes

- Built for Kerbal Space Program 1.12.x.
- Intended for the stock Astronaut Complex UI.
- Other mods that heavily replace or rebuild the Astronaut Complex UI may conflict with EAC roster-tab adjustments.
- EAC attempts to re-sync roster rows after KSP rebuilds Astronaut Complex lists.
- Contract Configurator is optional.
- Final exam contract mode requires Contract Configurator and the EAC CC bridge.

If Astronaut Complex UI oddities appear, check for:

- more than one installed copy of EAC,
- another mod patching Astronaut Complex roster lists or buttons,
- missing or mismatched EAC plugin files,
- errors in `KSP.log` beginning with `[EAC]`.

## Known Notes

- A very brief roster-row flash can occur when KSP rebuilds Astronaut Complex lists. EAC re-applies its roster cleanup after the rebuild.
- Some Astronaut Complex UI layouts may not expose every stock list in the same way. EAC handles missing lists as gracefully as possible.
- Facility upgrades can cause the Astronaut Complex UI to rebuild. EAC attempts to re-sync hiring and roster behavior afterward.
- Mission old-age death is controlled separately from retired-death behavior. A Kerbal assigned to an unlaunched vessel will only be eligible for mission old-age death when that setting is enabled.

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

Verbose logging can be enabled at:

```text
Space Center → Settings → Difficulty Options → EAC → Debug
```

Use verbose logging only while troubleshooting because it can create more log output.

### Contract Configurator Exams Do Not Appear

Check that:

- Contract Configurator is installed,
- `EAC_CCBridge.dll` is installed,
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

Check whether final exam mode was disabled or Contract Configurator was removed after the Kerbal entered the exam path. EAC includes recovery handling for this case, but the save may need to be loaded at the Space Center for EAC to reconcile the state.

## Packaging Checklist

A normal EAC release should include at least:

```text
GameData/EAC/Plugins/EAC.dll
GameData/EAC/Plugins/EAC_CCBridge.dll        # optional Contract Configurator bridge
GameData/EAC/Contracts/*.cfg                 # optional final exam contracts
GameData/EAC/Agencies/*.cfg                  # optional contract agencies
GameData/EAC/Agencies/*.png                  # optional agency logos
GameData/EAC/Craft/*.craft                   # optional provided craft
GameData/EAC/Scenarios/*.cfg                 # optional scenario vessels
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
