# Enhanced Astronaut Complex (EAC) Change Log

## 2026-0604: EAC v1.4.1 for KSP >= 1.12.x

This hotfix addresses GitHub issue #41, where the **EAC Starting Crew Setup** configuration dialog could appear repeatedly in existing saves after entering and exiting buildings or otherwise changing scenes.

### Starting crew setup

- Fixed the **EAC Starting Crew Setup** dialog appearing repeatedly in existing saves after scene changes.
- Made the starting crew setup session identity stable across scene changes by using save folder, save title, and game seed instead of unstable runtime state.
- Existing EAC-managed saves with persisted EAC roster records are now treated as already past starting crew setup if they do not yet have the EAC 1.4 setup-complete flag.
- No save-breaking changes.

## 2026-0603: EAC v1.4.0 for KSP >= 1.12.x

EAC 1.4.0 is a major stabilization, refactor, and career-management release. Crew R&R and Earn Your Stripes remain optional as they always were, but EAC now includes EAC-native systems that cover their major overlapping use cases while adding broader integrated crew-career management.

This release also fixes several Astronaut Complex tab contamination paths, adds assignment-duration display, adds EAC-native veteran/suit/starting-crew behavior, adds an advisory Suggested Next Crew tool, improves recovery handling, improves DeepFreeze compatibility, and cleans up legacy save data.

### Highlights

- Crew R&R and Earn Your Stripes remain optional; EAC does not require either mod.
- EAC now provides built-in recovery leave, veteran recognition, suit presentation, Badass progression, starting crew setup, and Suggested Next Crew behavior when the relevant external specialist mod is not loaded.
- For players who want fewer overlapping mods, EAC can now handle the major Crew R&R / Earn Your Stripes style functions itself, plus EAC-specific systems such as training, retirement, Astronaut Complex tab management, Hall of History records, DeepFreeze-aware mission-time handling, and advisory crew recommendations.
- EAC still defers to Crew R&R and Earn Your Stripes when those mods are installed as loaded assemblies.

### Astronaut Complex roster fixes

- Fixed Astronaut Complex tab contamination where Kerbals from Assigned, Retired, Lost/KIA/Dead, Training, Recovery, Frozen, or other unavailable states could appear in Available.
- Enforced tab ownership while the Astronaut Complex is open.
- Limited the Astronaut Complex watchdog/filter logic to Astronaut Complex usage.
- Fixed synthetic/custom Retired tab activation by avoiding unsafe stock `UIList.SetActive` reflection for non-stock tabs.
- Retained safe stock activation paths for native stock tabs.
- Added assignment-duration display in the Assigned tab.
- Improved row reflection caching and roster-name-set reuse during Astronaut Complex list rebuilds.
- Reduced repeated warnings from expected Astronaut Complex UI-shape variations.

### Recovery, Crew R&R, and rest handling

- Added loaded-assembly detection for Crew R&R.
- If Crew R&R is loaded, EAC recovery settings are disabled/delegated and EAC does not apply its own recovery leave.
- If Crew R&R is not loaded, EAC recovery settings remain available.
- Fixed recovery leave not appearing after some recovery paths.
- Added support for focused-vessel recovery and Space Center / Tracking Station / map-style recovery paths.
- Added recovery-state rehydration from EAC records if KSP clears stock inactive state during scene changes.
- Added a minimum visible recovery floor for positive recovery leave so very short flights do not immediately appear available when recovery is enabled.
- Recovery leave percentage and RestDay Max behavior are preserved.

### DeepFreeze compatibility

- Improved DeepFreeze freeze/thaw lifecycle handling.
- Frozen Kerbals are treated as an external lifecycle state rather than normal active, missing, or deceased crew.
- Frozen time is excluded from recovery fatigue.
- Awake mission time before freezing is accumulated and preserved.
- Awake mission time after thawing is added to the preserved pre-freeze time.
- Recovery after DeepFreeze now uses total awake mission time, not frozen duration.
- Fixed transition timing where mission tracking could be cleared before the frozen-state capture occurred.

### Earn Your Stripes compatibility and EAC-native alternatives

- Added loaded-assembly detection for Earn Your Stripes.
- If Earn Your Stripes is loaded, EAC defers veteran, suit, and starting crew behavior to Earn Your Stripes.
- If Earn Your Stripes is not loaded, EAC can provide EAC-native veteran recognition, suit presentation, and starting crew setup.

### EAC-native veteran recognition

- Added configurable veteran requirements.
- Veteran eligibility can use flight count, flight hours, optional milestone requirement, and optional class restrictions.
- Existing saves can be evaluated so Kerbals who already meet requirements can be promoted retroactively.
- Added veteran recognition notifications through HUD and Message App paths.
- Added Message App category support for veteran recognition.

### Suit presentation

- Added optional default and veteran suit presentation when Earn Your Stripes is not loaded.
- Suit handling is configurable.
- EAC does not apply suit presentation when Earn Your Stripes is loaded.

### Badass progression

- Added optional Badass progression.
- Badass progression is conservative/off by default.
- Badass progression can require veteran status and milestone qualification.
- Badass milestone rolls are tracked so save reloads cannot repeatedly reroll the same milestone.
- Added Badass recognition notifications through HUD and Message App paths.
- Added Message App category support for Badass recognition.

### New-game starting crew setup

- Added EAC-native new-game starting crew setup when Earn Your Stripes is not loaded.
- Added startup dialog flow:
  - Keep Default Crew
  - Replace Default Crew
- Added gender filters:
  - Male
  - Female
  - Both
- Added class filters:
  - Pilot
  - Engineer
  - Scientist
- Added configurable starting crew count.
- If all three classes are selected and the starting crew count is three or more, generated crews guarantee at least one Pilot, one Engineer, and one Scientist.
- Fixed startup dialog not reappearing for subsequent new games in the same KSP session.
- Centered the startup setup window to avoid awkward overlap with stock popups.

### Suggested Next Crew Advisor

- Added first-pass Suggested Next Crew Advisor for VAB/SPH.
- Advisor is intentionally suggestion-only career management.
- Advisor does not auto-fill stock crew slots.
- Advisor does not enforce morale, refusal, or hard rotation.
- Advisor can be opened from the EAC editor toolbar button in the VAB/SPH.
- Recommendation labels include:
  1. Needs experience
  2. Due for flight
  3. Long service priority
  4. Recently flew
- Fixed recommendation priority so Needs Experience outranks Due for Flight, Long Service Priority, and Recently Flew.
- Suggested Next Crew is disabled/delegated when Crew R&R is loaded.

### Settings and UI

- Reworked settings to avoid overcrowding the stock KSP Difficulty Settings screen.
- Moved detailed and lower-frequency settings into an EAC Advanced Settings window.
- Advanced Settings is accessible from the EAC toolbar/window.
- Moved the Advanced button near Close in the EAC Space Center window.
- Adjusted the Advanced Settings notice in basic settings to avoid unsupported star glyphs.
- Moved these options to Advanced Settings:
  - Auto-clean unreferenced retired/dead Kerbals
  - Message App subcategories
  - Veteran settings
  - Suit settings
  - Badass settings
  - Starting crew settings
  - Verbose/debug settings
- Changed Auto-clean unreferenced retired/dead Kerbals into a one-shot command:
  - check it,
  - click Apply,
  - EAC runs one cleanup pass,
  - the option resets unchecked.

### Message App behavior

- If Message App support is re-enabled after being disabled, all EAC message categories default back on.
- Added message categories for Veteran recognition and Badass recognition.

### Default aging values

- Retire minimum default: 37
- Retire maximum default: 47
- Retired death minimum default: 50

### Save migration and persistence

- Migrated EAC scenario save data to `EACScenario`.
- Removed stale/empty legacy `RosterRotationScenario` nodes from persistent saves to avoid future confusion.
- If legacy data-bearing EAC save information is found, EAC backs up the persistent file before cleanup and notifies the user at Space Center.
- Empty legacy scenario stubs are removed silently without backup or popup.
- Added/updated persistence for recovery state, Badass roll tracking, starting crew setup state, DeepFreeze-aware accumulated mission time, and settings.

### Internal refactor and performance

- Split broad EAC 1.4 feature code into clearer service boundaries.
- Kept compatibility facades where needed so existing callers remain stable.
- Refactored veteran, suit, Badass, starting crew, and Suggested Next Crew logic into smaller services.
- Added type-keyed reflection caches in hot or repeated paths.
- Replaced repeated dictionary snapshots in stale training cleanup with a key-list cleanup pass.
- Added a Space Center UI instance refresh path to avoid cold-path `FindObjectsOfType` scans.
- Added a small DeepFreeze bridge refresh throttle with forced refreshes for lifecycle transitions.
- Preserved existing save scheduling and idle-disabled runner behavior.

### Notes

1. HarmonyKSP/Harmony2 remains required.
2. Contract Configurator remains optional.
3. Crew R&R remains optional. If loaded, EAC delegates overlapping recovery and crew-suggestion behavior.
4. Earn Your Stripes remains optional. If loaded, EAC delegates overlapping veteran, suit, and starting-crew behavior.
5. DeepFreeze remains optional. If loaded, EAC excludes frozen time from recovery fatigue.
6. Suggested Next Crew is advisory-only in EAC 1.4. Stock crew auto-fill is intentionally deferred.

## 2026-0520: EAC v1.3.1 for KSP >= 1.12.x

This release is a targeted compatibility, roster, dependency-documentation, and Contract Configurator contract update. It addresses the three open GitHub issues for EAC 1.3.0, adds Kerbal Changelog support, and documents HarmonyKSP/Harmony2 as a required dependency.

### Astronaut Complex roster tabs

- Fixed a bug where Kerbals from one Astronaut Complex tab could appear under another tab after switching views.
- Fixed the reported LOST-tab case where Available Kerbals could appear while viewing LOST.
- Tightened tab-specific filtering for Available, Assigned, Retired, and Lost lists after KSP rebuilds the Astronaut Complex UI.
- Improved roster cleanup timing so EAC re-applies the correct tab filter after UI refreshes rather than leaving stale rows visible.

### DeepFreeze compatibility

- Added optional DeepFreeze compatibility handling.
- EAC treats DeepFreeze frozen/suspended Kerbals as an external lifecycle state instead of processing them as normal active, missing, or deceased crew.
- Fixed the reported case where a Kerbal could be marked KIA after being taken out of suspended animation.
- DeepFreeze remains optional.

### Contract Configurator final-exam contracts

- Deferred affected Contract Configurator final-exam XP awards until contract completion.
- Updated affected Scientist contracts so CollectScience no longer handles recovery directly.
- Added explicit final rover/test-article recovery objectives using `EACRecoverVesselWithPart`.

### Dependency and Kerbal Changelog documentation

- Documented HarmonyKSP/Harmony2 as a required dependency.
- Added `Changelog.cfg` support for Kerbal Changelog.
- Updated README installation, compatibility, troubleshooting, and packaging notes.

## 2026-0516: EAC v1.3.0 for KSP >= 1.12.x

This release is a targeted optimization, stability, and Contract Configurator integration update.

- Added optional Contract Configurator final-exam support for EAC training progression.
- Added EAC final exam requirements and completion behaviours through the EAC CC bridge.
- Added final exam tracking by Kerbal trait, target level, and exam ID.
- Added exam rotation support.
- Added recovery handling for Kerbals pending or active in a final exam if final exams are disabled or Contract Configurator is removed.
- Added support for EAC-provided exam craft and scenario vessels.
- Added scenario-vessel cleanup safeguards.
- Optimized applicant rejection by caching reflected KerbalRoster rejection methods.
- Fixed Reject All skipping applicants while the applicant list changes.
- Improved Available / Retired / Lost tab cleanup after KSP UI refreshes.
- Optimized aging and mission-death cleanup reflection paths.
- Reduced repeated reflection scans and avoidable list allocations.

## 2026-0505: EAC v1.2.1 for KSP >= 1.12.x

- Fixed potential issues with Kerbin/Earth time.
- Earth time now shows correctly throughout EAC.
- Fixed dismissed Kerbals who were Training still showing up.
- Minor code cleanup.

## 2026-0412: EAC v1.2.0 for KSP >= 1.12.x

- Fixed issue with Crash Detection giving a false positive.
- Fixed Space Center startup lags on heavily modded installs.
- Hall of History now only initiates when called, not at startup.
- Retired Tab helper code was added so it loads faster.
- Reduced calls to the persistent file.

## 2026-0411: EAC v1.1.9 "Jeremiah" for KSP >= 1.12.x

- Behind-the-scenes maintenance update.
- Improved performance and reliability in crew-related screens.
- Fixed a small retired-roster edge case.
- Cleaned up internals for easier future updates.

## 2026-0409: EAC v1.1.8 for KSP >= 1.12.x

- Improved retired-Kerbal hiding performance by caching CrewAssignmentDialog field lookups.
- Reduced repeated reflection overhead in retired-Kerbal scrubbing.
- Skipped unnecessary roster scans when no retired Kerbals exist.

## 2026-0327: EAC v1.1.7 for KSP >= 1.12.x

- Fixed recovery timing where `MissionStartUT` could be cleared before post-mission recovery leave was calculated.
- Normal recovery leave now explicitly requests a save after it is applied.
- Fixed `RestDay Max = 0` so it behaves as a true zero cap.
- Fixed multi-crew crash-recovery edge cases.
- Fixed FlightTracker veteran progression and sync behavior.
- Fixed retirement-probability bug for never-flown Kerbals.
- Improved save reconciliation and recovery precedence rules.

## 2026-0326: EAC v1.1.6 for KSP >= 1.12.x

- Added mission-time tracking independent of aging.
- Added syncing in flight-scene startup, Kerbal status changes, and KSC periodic update.

## 2026-0325: EAC v1.1.5 for KSP >= 1.12.x

- Base recovery leave now uses each Kerbal's own `MissionStartUT`.
- Crash recovery leave base time also uses each Kerbal's own `MissionStartUT`.
- Recovery no longer uses vessel mission time for EAC leave calculations.
- Added per-Kerbal verbose recovery logging.

## 2026-0324-1: EAC v1.1.4 for KSP >= 1.12.x

- Fixed issue with RestDay and Recovery percentages not working as expected.

## 2026-0324: EAC v1.1.3 for KSP >= 1.12.x

- Level-up training now uses the configurable TrainingStarDays setting instead of hardcoded 30.
- Training confirmation preview now uses TrainingStarDays.
- Training overlay duration preview now uses TrainingStarDays.

## 2026-0323: EAC v1.1.2 for KSP >= 1.12.x

- Updated EAC UI styling to use KSP's native `HighLogic.Skin`.
- Improved Hall of History and Memorial presentation.
- Memorial flight-hours display now prefers FlightTracker data when available.
- Added visible recovery-time settings.
- Added Recovery leave percentage and RestDay Max behavior.
- Reworked crash detection to avoid detached-stage false positives.
- Preserved EAC flight-count behavior for career records.
- Added recovery-time controls to the Aging column.

## 2026-0316: EAC v1.1.1 for KSP >= 1.12.x

- Made EAC windows more opaque.
- Adjusted some windows to avoid opening on top of each other.
- Improved portrait capture.
- Minor logic and visual fixes.

## 2026-0314: EAC v1.1.0 for KSP >= 1.12.x

- Added crash outcome handling.
- Added mission old-age death checks.
- Added Space Center / Astronaut Complex UI extensions for retirement, training, and retired crew management.
- Added Hall of History with Memorial Wall, portraits, Milestone Wall, and veteran presentation/status support.
- Added optional cleanup of unreferenced retired Kerbals.
- Improved notifications and messaging.
- Improved save/persistence handling.
- Improved optional mod compatibility behavior.
- Performed major internal reliability and maintenance refactoring.

## 2026-0307: EAC v1.0.2.0 for KSP >= 1.12.x

- Fixed debug information not being sent to `KSP.log`.
- Fixed ACOpenCache scan throttling.
- Reworked AstronautComplexHook.
- Stopped scanning entirely when the Astronaut Complex is closed.

## 2026-0306: EAC v1.0.1.0 for KSP >= 1.12.x

- Fixed slow framerate issue.
- Further optimized code.
- Reordered tabs in Astronaut Complex to Available / Assigned / Retired / Lost.
- Added configurable recall cost for retired Kerbals.
- Added further debugging options.

## 2026-0303: EAC v1.0.0 for KSP >= 1.12.x

- Initial release.
