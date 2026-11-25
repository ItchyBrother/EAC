# Enhanced Astronaut Complex (EAC)

**Version 1.7.0 | Kerbal Space Program 1.12.x (developed and tested on 1.12.5)**

Enhanced Astronaut Complex gives your Kerbals careers, not just crew assignments. It extends the stock Astronaut Complex with training, recruitment, aging, retirement, recovery leave, service history and optional practical graduation examinations. Build an experienced astronaut corps, manage availability between missions and preserve the history of your space program as generations of Kerbals come and go.

EAC is designed to complement stock gameplay. It can provide an integrated personnel-management experience on its own, or defer overlapping functions to supported specialist mods when their assemblies are loaded.

[Downloads](https://github.com/ItchyBrother/EAC/releases) · [Change log](https://github.com/ItchyBrother/EAC/blob/main/CHANGE_LOG.md) · [Report an issue](https://github.com/ItchyBrother/EAC/issues) · [KSP forum](https://forum.kerbalspaceprogram.com/topic/229909-enhanced-astronaut-complex-eac)

## What's new in 1.7.0

EAC 1.7 completes and extends the career-history features introduced in 1.6, improves performance during long careers, strengthens save-data protection and makes graduation examinations more varied and practical.

- **Mission Archive and Mission Timeline:** follow missions, their participating crew and significant events recorded as they happen, alongside the existing Hall of History and Service Records.
- **Improved Service Records and crew advice:** search, filter and sort flight-qualified personnel; follow links between missions and service history; view richer, expandable Suggested Next Crew recommendations.
- **Practical graduation exams:** all 40 examination scenarios are retained. Six advanced laboratory exams have been redesigned, with automatic deployment to the field laboratory and revised hands-on objectives. Airlift and aborted-takeoff examinations have also been corrected.
- **One installation package:** Contract Configurator support is embedded in `EAC.dll`; the former separate EAC Contract Configuration package is no longer required. Contract Configurator itself remains optional.
- **Safer career data:** external history revisions and archived Kerbal versions are associated with the loaded save. Missing or unverifiable data triggers a protective warning with read-only inspection and explicit recovery options.
- **Recruitment and roster improvements:** accurate stock hiring prices, a configurable astronaut-profession allowlist, applicant reload controls in both recruitment interfaces, and improved retirement, Lost and R&R handling.
- **Performance and stability:** reduced repeated roster and history processing, cached frequently used UI and reflection data, and moved suitable file-scanning work away from the main game thread.

See the [detailed change log](https://github.com/ItchyBrother/EAC/blob/main/CHANGE_LOG.md) for the complete 1.7 release notes and earlier versions.

## Requirements and installation

| Component | Requirement |
| --- | --- |
| Kerbal Space Program | 1.12.x; development testing on 1.12.5 |
| HarmonyKSP / Harmony2 | Required |
| Contract Configurator | Optional; required only for contract-based final examinations |
| Crew R&R, Earn Your Stripes, FlightTracker, DeepFreeze, Kronometer | Optional integrations |
| Kerbal Changelog | Optional in-game release notes |

### Manual installation

1. Exit KSP and back up the complete career folder for each save you intend to upgrade.
2. Install [HarmonyKSP](https://github.com/KSPModdingLibs/HarmonyKSP), if not already installed.
3. Download the EAC 1.7.0 unified release from [GitHub Releases](https://github.com/ItchyBrother/EAC/releases).
4. For an upgrade, follow the migration instructions below before installing the new package.
5. Extract the supplied `GameData/EAC` folder into your KSP installation's `GameData` directory. Do not create a nested `GameData/GameData` directory.
6. If you want graduation exams, install Contract Configurator as well. Review the EAC final exam setting after loading your career.
7. Start KSP, load your career at the Space Center and review EAC's settings.

A typical installation contains:

```text
Kerbal Space Program/
  GameData/
    000_Harmony/
    ContractConfigurator/          (optional)
    EAC/
      Plugins/
        EAC.dll
      Config/
        ApplicantTraits.cfg
      Agencies/
      Contracts/
      Craft/
      Icons/
      PluginData/
        Scenarios/
      changelog.cfg
      EAC.version
      README.md
      CHANGE_LOG.md
      LICENSE.md
```

### Upgrading from EAC 1.6.x or earlier

**Important: the old standalone EAC Contract Configuration add-on must not be installed alongside EAC 1.7.0.**

- Back up the entire `saves/<career>/` directory, especially its `EAC/` subdirectory. Back up any custom EAC configuration or contract edits separately.
- For manual installations, move the old `GameData/EAC` installation outside `GameData` and install the new unified package in its place. Reapply compatible customizations selectively; do not copy old DLLs or outdated examination contracts back into the new installation.
- Ensure neither `EAC_CCBridge.dll` nor `EAC_CCBridge.dll.disabled` remains in the installed EAC folder. Do not rename the old bridge to reactivate it. A safety check protects against conflicts if a legacy bridge is detected.
- If possible, complete or resolve active graduation contracts before upgrading, and keep a pre-upgrade backup of careers with examinations in progress.
- Do **not** remove `saves/<career>/EAC/` as part of a mod upgrade. That folder can contain the authoritative history and archived roster of your career.

For CKAN-managed installations, use the updated package metadata when it becomes available. The former EAC Contract Configuration companion package is superseded for 1.7.0; remove it through CKAN rather than mixing managed files and a manual installation. If the updated CKAN package is not yet indexed, use the GitHub release for a manual installation after removing the old managed packages. Contract Configurator may remain installed for EAC or other mods.

## Main features

### Training and graduation

Train Kerbals over a period of game time rather than awarding experience instantly. Training duration and costs scale with the intended experience level and can use funds, science or both. Trainees are kept out of the ordinary available-crew and assignment lists while unavailable for duty.

With Contract Configurator present and EAC final exams enabled, training can culminate in practical, profession-specific examinations. EAC manages trainee eligibility, the assigned Kerbal and graduation state; Contract Configurator handles the mission objectives. Without Contract Configurator, EAC's normal training path remains available.

### Recruitment and starting crews

The Space Center EAC window and Astronaut Management interface provide applicant management and applicant replenishment. Hiring costs follow KSP's recruitment-cost calculation, and the Space Center interface displays the cost and available funds.

By default only Pilots, Engineers and Scientists are eligible as EAC applicants. Tourist-type Kerbals and the Tourist trait are excluded. Other professions can be authorized by internal trait name in `GameData/EAC/Config/ApplicantTraits.cfg`, including through ModuleManager patches. The stock astronaut professions remain enabled even if the file is absent. Changes to the allowlist take effect after restarting KSP.

EAC does not delete excluded applicants or retroactively remove existing hired Kerbals. This protects passengers and other third-party contract personnel.

When Earn Your Stripes is not loaded, EAC can also offer new-game starting crew setup: retain or replace the original crew, select gender and profession filters and set the initial crew count. When sufficient slots are requested, the selected stock professions are represented.

### Aging, retirement and recall

Kerbals age using the active game's calendar where available. Retirement and mortality can be configured or disabled, and optional mission old-age death rules are available. Retirement removes personnel from the active assignment pool; eligible retirees can return through Recall, subject to EAC's rules and available capacity. Experience can decline during retirement, and permanent recall ineligibility is retained across reloads and archival.

Default age settings are retirement minimum 37, retirement maximum 47, retired-death minimum 50 and maximum hire age 45 (configurable from 18 to 120).

### Astronaut Complex and crew management

EAC extends the stock roster with retirement and historical status handling, assignment duration and appropriate availability filtering. Assigned, training, retired, Lost, frozen and recovering personnel are kept out of ordinary crew assignment where applicable. The Space Center R&R view shows internal EAC recovery leave and Crew R&R vacations; it is informational rather than a place to train or retire recovering crew.

### Crash recovery and rest

Crash severity can lead to recovery leave or retirement depending on difficulty settings. EAC's native post-mission rest is based on mission time and a configurable recovery percentage, with a maximum rest duration. Positive short recovery periods receive a minimum visible duration. When Crew R&R is loaded, EAC delegates overlapping recovery behavior to that mod.

DeepFreeze-aware mission-time tracking counts time spent awake, not cryosleep, when calculating recovery fatigue. Recovery save requests are coordinated with stock vessel recovery to avoid interfering with an unfinished recovery transaction.

### Suggested Next Crew

The VAB/SPH advisor uses career history to suggest available crew based on experience, time since their last mission and service history. Expand the recommendation details to understand why a Kerbal was suggested. It does not fill seats automatically, enforce a rotation, impose morale penalties or override the player's choices. The advisor is delegated when Crew R&R is loaded.

### Veteran recognition, suits and Badass progression

When Earn Your Stripes is not loaded, EAC can manage veteran eligibility, suit presentation and optional Badass progression. Recognition can use flight count, mission hours, milestones and profession restrictions. Existing qualifying careers can be evaluated, and milestone rolls are persisted to avoid repeat rolls after reload. Badass progression remains optional and off by default.

### Calendar support and notifications

EAC supports stock Kerbin and Earth time bases and compatible active custom calendars. When available, Kronometer's formatter is used for lifecycle dates and historical labels; otherwise EAC falls back to its own formatting. The formatter does not change stored universal time.

Birthday, training, retirement, death, recovery, veteran and Badass notifications can be configured by category, with HUD and KSP Message System presentation options.

## Hall of History

The Hall of History preserves the people and achievements of your space program:

| View | Purpose |
| --- | --- |
| Memorial | Remember fallen Kerbals, including portraits when available. |
| Milestones | Review program achievements and recognized firsts. |
| Service Records | Explore individual career files, flight history, mission time and distinctions. Search, filter and sort flight-qualified Kerbals. |
| Missions | Explore the Mission Archive, crew participation and chronological mission events. |

Mission events such as orbital milestones, landings, EVAs and flag planting are recorded with event-time context when available. Program First recognition is attributed to identifiable participants, not an arbitrarily selected crew member. Service Records require confirmed flight evidence: hiring or classroom training alone does not create a flight-qualified record. This does not erase the Kerbal's underlying lifecycle information.

Earlier careers may contribute stock `CAREER_LOG` accomplishments and compatible FlightTracker history. EAC does not invent missing mission dates, vessels, crews or firsts when the original data does not identify them.

## Optional Contract Configurator graduation examinations

EAC 1.7 includes examination contracts, agencies, craft and scenario assets in the unified release. No separate bridge DLL or changes to Contract Configurator itself are required.

EAC tracks the trainee, profession, target level and exam state. Only the assigned trainee can receive graduation credit; completion is reconciled without duplicate graduation awards. Failed or cancelled exams have recovery handling so a trainee can return to a valid training path.

### Advanced field laboratory examinations (Level 3)

| Profession | Examination | Practical objective |
| --- | --- | --- |
| Engineer | Operation Cold Start | Expand the electrical power system. |
| Engineer | Operation Silent Station | Install communications equipment. |
| Engineer | Operation Night Shift | Install exterior access and lighting equipment. |
| Scientist | Operation Weather Watch | Conduct environmental observations and research. |
| Scientist | Operation Strange Goo | Perform experiments, retrieve data and restore instrumentation. |
| Scientist | Operation Deep Ground | Conduct geological, gravity and seismic research. |

Accept the laboratory exam, then select the deployed laboratory in the Tracking Station. EAC deploys the trainee and support personnel automatically; a separate transportation flight is no longer mandatory. Only one advanced laboratory examination may be offered or active at a time.

The Level 1 Abort Takeoff exam no longer requires the aircraft to become airborne. The Level 3 Scientist and Engineer Island Airlift exams have revised passenger objectives, including delivery of the engineer to the Island Airfield and return to KSC without that passenger for the Engineer variant.

### Temporary Academy personnel

Laboratory assistants and designated exam passengers created by EAC are temporary. They are cleaned up when safe after their examination duties and are not intended to become permanent astronauts. Brief roster appearances during recovery are normal, and personnel left aboard an EVA vessel may need separate recovery. Normal recruits, rescue-contract Kerbals and unrelated Contract Configurator participants are outside this cleanup's intended scope.

## Settings

Open **Space Center > Settings > Difficulty Options > EAC** for the basic configuration. The EAC Space Center window provides **Advanced Settings** for detailed options.

Settings include training duration and costs, age and retirement rules, recovery leave, starting crew options, crew advice, veteran/suit/Badass behavior, notifications, final exams when available, external data storage, cold roster archival and diagnostic logging. Settings persist with the career.

## Save data, backup and recovery

### Embedded and external records

EAC-owned records can be kept in the `.sfs` save or stored externally by revision. External History/Record storage and the hybrid cold roster archive are optional and default off for new careers. They can help manage long-running saves with substantial historical data.

Typical external data locations are:

```text
saves/<career>/
  persistent.sfs
  EAC/
    data/revisions/
    cold-roster/
    HallPortraits/
```

Cold archiving is intended for permanently Lost and non-recallable retired Kerbals. Recallable retirees and protected personnel (including crew in active vessels or contracts, DeepFreeze crew and vacationing crew) remain in the stock roster. Archived entries can be viewed in the appropriate Retired or Lost lists without becoming available for ordinary crew assignment.

EAC 1.7 uses save-specific archive membership and immutable payload versions, with identity and lifecycle checks during loading. Older snapshots are protected from later retirement or death states. The previous legacy archive migration remains supported. Disabling cold archival is intended to restore verified archived Kerbals to the stock roster through a normal save. Archive payloads may remain as safety copies for older saves; do not manually prune them.

**Always back up or transfer the entire career folder, not just `persistent.sfs`.** Copy a different career to a separate save folder with its matching external data. A `.sfs` alone is not necessarily a complete EAC backup.

### If EAC reports a career data warning

If a required revision or archived payload is missing or cannot be verified, EAC protects the career by suspending normal EAC writes and archive changes. You can inspect the available roster in read-only mode.

The preferred solution is to restore the matching dataset and reload the career. If the missing information is permanently unavailable, EAC offers two explicit recovery paths:

| Option | Result |
| --- | --- |
| Repair Current Career | Prepare a repaired snapshot for the existing career, retaining its folder, craft, named saves and unrelated mod files. |
| Create Recovered Career Copy | Prepare a separate playable copy based on the loaded save, retaining the original career. Other named saves, saved craft and unrelated external mod files are not automatically copied. |
| Continue Read-Only | Inspect the available information without rebuilding the protected EAC data. |

Recovery requires confirmation and verified backups. It retains information that can be verified from the loaded save and available external records. History or archived Kerbals stored exclusively in lost files cannot be reconstructed. A repaired career initially uses embedded records with external storage, cold archival and automatic roster cleanup disabled; review the roster before re-enabling those options.

**To complete Repair Current Career:** after the repair-prepared message, close EAC, return to KSP's main menu without quitting KSP or loading another save, wait for the activation result and then reload the original career. Read-only mode remains active until this reload; it is not necessary to repeat the repair. Keep the recovery backups until the repaired career has been checked and saved successfully.

## Compatibility and known limitations

| Optional mod | EAC behavior |
| --- | --- |
| Contract Configurator | Enables contract-based graduation exams when selected. Other CC contracts remain independent. |
| Crew R&R (CrewRandR) | Delegates overlapping rest and crew-advice functions when its DLL is loaded. |
| Earn Your Stripes | Delegates overlapping recognition, suit and starting-crew functions when loaded. |
| FlightTracker | Uses compatible historical flight information where appropriate; EAC has native mission history independently. |
| DeepFreeze | Respects frozen status and awake mission time, with improved death persistence. |
| Kronometer | Uses compatible active calendar formatting. |
| Kerbal Changelog | Shows the supplied release notes in game. |

Merely having a mod ZIP in `GameData` does not activate delegation. The relevant assembly must be loaded.

- Temporary exam personnel may be visible briefly during recovery, and third-party flight trackers may retain their historical entries.
- Classroom trainees are not yet consistently represented in the Assigned roster category.
- Five of the six redesigned laboratory exams were completed during development testing. Operation Strange Goo and the final Island Airlift changes still need additional validation. Report any objective that does not register.
- The applicant allowlist was tested with standard EAC recruitment. Live Kerbin-Side Remastered GAP compatibility, passenger contracts and third-party trait extensions require further community testing.
- Intermittent duplicate stock recovery remains an area to monitor. EAC has recovery save coordination and duplicate-processing protection; report reproducible cases.
- Historical data recovery is limited to surviving, verifiable records. Retain complete backups.

## Reporting problems

Open an [issue on GitHub](https://github.com/ItchyBrother/EAC/issues) with the EAC and KSP versions, installed relevant mods, reproduction steps and `KSP.log`. Include screenshots and relevant EAC settings where useful. For an exam issue, include the contract name, assigned Kerbal, profession, target level and objective that did not register. For a persistence issue, describe which career/save was loaded and whether the corresponding external dataset was present. Preserve backups before attempting repairs.

## Development and distribution

EAC 1.7 consolidates the core and embedded Contract Configurator integration into a single DLL and one distribution package. Development build artifacts, standalone bridge binaries and obsolete companion-package content should not be distributed. Release packaging includes the required examination assets, configuration, documentation, version metadata and a SHA-256 checksum for the ZIP.

Development history: [#50](https://github.com/ItchyBrother/EAC/issues/50), [#53](https://github.com/ItchyBrother/EAC/issues/53), [#57](https://github.com/ItchyBrother/EAC/issues/57), [#58](https://github.com/ItchyBrother/EAC/issues/58), [#59](https://github.com/ItchyBrother/EAC/issues/59), [#60](https://github.com/ItchyBrother/EAC/issues/60), [#61](https://github.com/ItchyBrother/EAC/issues/61).

## License and credits

Copyright © 2026 ItchyBrother. Licensed under [Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International](https://creativecommons.org/licenses/by-nc-nd/4.0/). See [LICENSE.md](https://github.com/ItchyBrother/EAC/blob/main/LICENSE.md) for the full terms. Use at your own risk. Preserve attribution and license notices when redistributing the unmodified mod for noncommercial purposes.

Thanks to Squad / Private Division and the KSP modding community; beta testers edgomes27, John Marley, Chaotic Protocol and green_stalk; celino for recruitment and compatibility reports; the HarmonyKSP and Harmony projects; the Contract Configurator and Kerbal Changelog maintainers; linuxgurugamer; severedsolo (Earn Your Stripes and FlightTracker); and JPLRepo and the DeepFreeze Continued maintainers. Thanks also to everyone who has supplied logs, testing feedback and suggestions throughout EAC's development.
