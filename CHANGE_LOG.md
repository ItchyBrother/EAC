# Enhanced Astronaut Complex (EAC)
# Change Log

### 2026-0901: EAC v1.6.0 — Service Records, External Data, and Career History for KSP >= 1.12.x

This release expands the Hall of History into a lightweight Kerbal career-history system, adds optional external EAC data storage and retired/lost roster archival for long-running careers, and adds a configurable maximum hire age. The 1.6.0 development build has been exercised in KSP 1.12.5 with the Service Records, Program First, migration, rehydration, revision reuse, and cleanup paths verified in-game.

#### Hall of History Service Records - GitHub issue #46
- Expanded the Flight Roster Tracker into Hall of History Service Records for each Kerbal.
- Added recovered-flight history with flight classification, vessel, primary body, recovery date, mission duration, time since last flight, and total EAC-tracked mission time.
- Added conservative import of existing KSP CAREER_LOG accomplishments for older careers without inventing missing vessel, date, duration, or crew information.
- Added Kerbal portraits to Service Records using the existing Hall of History portrait capture and fallback system.
- Added Career Distinctions and Program First recognition with visible star badges on the Service Records roster and detailed Service File.
- Program First wording is presented in Hall-style form such as First Suborbital Flight at Minmus.
- Program Firsts are assigned to the participating crew where EAC can identify the crew reliably.
- Verified in KSP 1.12.5 that native flight history is recorded, portraits display, and Program First stars persist and appear in Service Records.

#### External EAC datastore and roster archive - GitHub issue #47
- Added optional external storage for growing EAC-owned Kerbal records under the save folder EAC data directory.
- External storage is off by default for new careers and can be enabled from EAC Advanced Settings.
- Existing saves that are already using external data continue using their referenced external revision.
- Added a one-time informational message recommending external storage for long-running careers.
- Added reversible migration between embedded EAC records in persistent.sfs and the external EAC datastore.
- Added external archival of Retired and Lost Kerbals while keeping Active, Available, and Assigned Kerbals in the normal KSP roster.
- Archived Kerbals are rehydrated when loading so EAC, the Astronaut Complex, and Hall of History can continue using them.
- Added immutable save revision references so persistent, quicksaves, named saves, and backups can restore the correct EAC historical state.
- Added content hashing so unchanged EAC data reuses the current external revision instead of creating a new file on every save.
- Added reference-aware cleanup that protects revisions and roster payloads still referenced by any save and retains three additional safety copies.
- External write or validation failure falls back to embedded save data rather than risking loss of EAC records.
- Verified in KSP 1.12.5 that migration works in both directions, retired/lost roster data archives and rehydrates, unchanged revisions are reused, and old unreferenced revisions and archive payloads are cleaned up.

#### Maximum hire age - GitHub issue #49
- Added a configurable Max hire age setting to EAC Aging settings.
- Default maximum hire age remains 45, preserving the previous default hire-age distribution.
- Supported maximum hire-age range is 18 through 120.
- New Kerbal age generation now respects the configured upper limit and persists the setting with the career.
- Static boundary and persistence checks passed; dedicated in-game hire-age ceiling verification is still recommended before release.

#### Persistence and data safety
- EAC external save references are kept small so long-running career history does not continuously enlarge persistent.sfs.
- Historical Service Record data is stored with EAC-owned records when external storage is enabled.
- External archive and datastore cleanup are conservative and keep referenced data plus rollback safety copies.

## Notes for EAC 1.6.0

1. **External EAC data storage is opt-in for new careers.** It is disabled by default and can be enabled from EAC Advanced Settings. Saves already using the external datastore remain on the external path unless the player explicitly migrates them back.
2. **Retired/Lost roster archival is separate from active crew.** Active, Available, and Assigned Kerbals remain in KSP's normal roster. Retired/Lost Kerbals can be archived and rehydrated by EAC when needed.
3. **Program Firsts are conservative.** EAC only assigns a historical first when the participating crew can be identified reliably; it does not guess from incomplete legacy KSP history.
4. **Issue #49 is implemented and has passed static boundary/persistence checks.** A focused in-game test that changes the maximum hire age and hires several new Kerbals is still recommended before release.

### EAC v1.5.1 — Applicant Hiring Funds Hotfix for KSP >= 1.12.x

#### Applicant hiring funds fix
- Fixed EAC applicant hiring bypassing the normal Astronaut Complex recruitment charge.
- EAC now calculates the next recruitment cost before changing the applicant roster state so recruitment cost scaling is preserved.
- Career-mode hires through either EAC hire entry point now deduct Funds using the CrewRecruited transaction reason.
- Hires are blocked when available Funds are below the required recruitment cost; game modes without a Funds economy remain free to hire.
- Both EAC hire paths now use one shared routine for cost validation, roster updates, saving, cache invalidation, and Astronaut Complex refresh.
- If a roster update fails after Funds were charged, EAC attempts to refund the recruitment cost and reports hire or refund failures.
- Successful EAC hires are logged with the Kerbal name and charged amount even when Verbose UI logging is disabled.
- Verified in KSP 1.12.5 with consecutive EAC hires that the expected recruitment cost is deducted.
- No save-format changes.

#### Packaging
- EAC Core and EAC Contract Configuration remain separate packages and should use matching 1.5.1 versions.
- Contract Configurator integration behavior is unchanged in this hotfix.

### EAC v1.5.0 — Calendars, Package Split, and Retired UI Fixes for KSP >= 1.12.x

#### Custom calendar support
- Added active custom-calendar day and year support for age/date calculations and presentation.
- Improved compatibility with Kronometer, JNSQ, and rescaled Kopernicus / Sigma Dimensions systems.
- Preserved stock Kerbin-time and Earth-time behavior.
- Added a safe fallback when custom calendar values are unavailable or invalid.

#### EAC Core and EAC Contract Configuration packages
- Split the 1.5.0 release into EAC Core and the optional EAC Contract Configuration add-on.
- EAC Core no longer ships the Contract Configurator bridge or exam content and has no Contract Configurator dependency.
- EAC Contract Configuration ships the active EAC_CCBridge.dll plus contracts, agencies, craft, and scenarios.
- EAC Contract Configuration requires EAC Core 1.5.0 and Contract Configurator.
- Both packages use version 1.5.0 and should be installed at matching versions.

#### Bridge installation and upgrades
- Removed the old EAC_CCBridge.dll.disabled rename workflow from 1.5.0 packaging.
- Players without Contract Configurator install only EAC Core.
- Players using final exams install the EAC Contract Configuration add-on, which provides EAC_CCBridge.dll already enabled.
- Upgrade instructions remove stale EAC_CCBridge.dll.disabled files from EAC 1.4.x or earlier.
- Updated README, troubleshooting, packaging notes, and version metadata for the new release model.

#### Reflection and optional-mod discovery performance
- Added lock-protected process-lifetime caches for ReflectionUtils.FindField and ReflectionUtils.FindProperty.
- Cache keys include the target type and ordered candidate names; successful lookups and misses are both cached.
- Routed repeated Astronaut Complex row, tooltip, and badge reflection through the shared cache without changing lookup order, event wiring, value handling, or fallbacks.
- Added a shared optional-mod registry for Earn Your Stripes, Crew R&R / CrewQueueTwo, Contract Configurator, and EAC_CCBridge assembly/type discovery.
- Removed repeated loaded-assembly and AppDomain scans from individual optional-mod adapters.
- No public or internal method signatures, call sites, settings, or save formats changed.

#### Hall of History allocation reductions
- Cached milestone day-group counts during data refresh instead of rebuilding them during every OnGUI repaint.
- Reused normalized crew-name arrays and added a case-insensitive memorial-name index for milestone portrait links.
- Cached memorial role, service, metrics, and summary display strings when entries are built.
- Cleared the new indexes with the existing Hall refresh while preserving data rules, sorting, filtering, layout, and visible wording.

#### Retired-tab tooltip and refresh fixes
- Fixed the Recall tooltip so it works regardless of pointer approach direction.
- Added hover arbitration between the Recall tooltip and the row's normal Kerbal-information tooltip.
- Restored the crew tooltip immediately when moving from Recall back onto the row.
- Replaced recall-time full Astronaut Complex rebuilding with a targeted row update that preserves unaffected rows and the native Available list.
- Replaced retirement-time full rebuilding with a deferred EAC-only Retired-row refresh for manual and automatic retirement.
- Rebound new Retired rows to the correct crew member and ensured their crew-information tooltips are active immediately.
- Removed temporary raycast and pointer diagnostics after successful in-game verification.
- Astronaut Complex tab creation, list-anchor discovery, and native-list ownership behavior were not changed.

#### Astronaut Complex source organization
- Moved roster badge updates, crew-count calculations, stock-crew filtering, roster-name lookup, and badge reflection helpers into AstronautComplexACPatch.Badges.cs.
- Routed the remaining badge-text property lookups through ReflectionUtils.FindProperty.
- Preserved the existing tab entry points, Retired-tab registration, ForceRefresh entry point, and native-list ownership methods.

### EAC v1.4.1 — Starting Crew Setup Hotfix for KSP >= 1.12.x

#### Hotfix for GitHub issue #41
- Fixed the EAC Starting Crew Setup dialog appearing repeatedly in existing saves after entering and exiting buildings or otherwise changing scenes.
- Made the starting crew setup session identity stable across scene changes by using save folder, save title, and game seed instead of unstable runtime state.
- Existing EAC-managed saves with persisted EAC roster records are now treated as already past starting crew setup if they do not yet have the EAC 1.4 setup-complete flag.
- No save-breaking changes.

### EAC v1.4.0 — Integrated Career Crew Management for KSP >= 1.12.x

#### EAC 1.4 overview
- Major stabilization, refactor, and career-management release.
- Crew R&R and Earn Your Stripes remain optional; EAC does not require either mod.
- EAC now provides EAC-native recovery leave, veteran recognition, suit presentation, Badass progression, starting crew setup, and Suggested Next Crew behavior when the relevant specialist mod is not loaded.
- EAC can now cover the major Crew R&R / Earn Your Stripes style use cases itself while adding broader EAC career systems such as training, retirement, Hall of History, DeepFreeze-aware mission-time handling, and advisory crew recommendations.
- EAC still defers to Crew R&R and Earn Your Stripes when those mods are installed as loaded assemblies.

#### Astronaut Complex roster fixes
- Fixed tab contamination where Assigned, Retired, Lost/KIA/Dead, Training, Recovering, Frozen, or otherwise unavailable Kerbals could appear in Available.
- Enforced tab ownership while the Astronaut Complex is open.
- Fixed synthetic Retired tab activation by avoiding unsafe stock UIList.SetActive reflection for custom tabs.
- Added assignment duration display in the Assigned tab.
- Improved Astronaut Complex row reflection caching and roster-name reuse during list rebuilds.

#### Recovery and Crew R&R compatibility
- Added loaded-assembly detection for Crew R&R.
- If Crew R&R is loaded, EAC recovery settings and overlapping Suggested Next Crew behavior are disabled or delegated.
- If Crew R&R is not loaded, EAC provides configurable recovery leave.
- Fixed recovery leave for focused-vessel recovery and Space Center / Tracking Station / map-style recovery paths.
- Added recovery-state rehydration from EAC records if KSP clears stock inactive state during scene changes.
- Added a minimum visible recovery floor for positive recovery leave.

#### DeepFreeze compatibility
- Improved DeepFreeze freeze/thaw lifecycle handling.
- Frozen Kerbals are treated as an external lifecycle state rather than normal active, missing, or deceased crew.
- Frozen time is excluded from recovery fatigue.
- Awake mission time before freezing is accumulated and preserved.
- Awake mission time after thawing is added to the preserved pre-freeze time.
- Recovery after DeepFreeze now uses total awake mission time, not frozen duration.

#### Earn Your Stripes compatibility and EAC-native alternatives
- Added loaded-assembly detection for Earn Your Stripes.
- If Earn Your Stripes is loaded, EAC defers veteran, suit, and starting crew behavior.
- If Earn Your Stripes is not loaded, EAC can provide EAC-native veteran recognition, suit presentation, and starting crew setup.

#### Veteran, suit, and Badass progression
- Added configurable EAC-native veteran requirements using flight count, flight hours, optional milestone requirement, and optional class restrictions.
- Existing saves can be evaluated so already-qualified Kerbals can be promoted retroactively.
- Added optional default and veteran suit presentation when Earn Your Stripes is not loaded.
- Added optional conservative Badass progression with milestone roll tracking.
- Added HUD and Message App recognition notifications for Veteran and Badass recognition.

#### New-game starting crew setup
- Added EAC-native new-game starting crew setup when Earn Your Stripes is not loaded.
- Added Keep Default Crew and Replace Default Crew startup choices.
- Added gender filters, class filters, and configurable starting crew count.
- When all three classes are selected and crew count is three or more, EAC guarantees at least one Pilot, one Engineer, and one Scientist.
- Fixed startup setup not reappearing for later new saves in the same KSP session.
- Centered the startup setup window to avoid awkward stock popup overlap.

#### Suggested Next Crew Advisor
- Added first-pass Suggested Next Crew Advisor for VAB/SPH.
- Advisor is suggestion-only and does not auto-fill stock crew slots.
- Recommendation labels are Needs experience, Due for flight, Long service priority, and Recently flew.
- Fixed recommendation priority so Needs Experience is the top recommendation.
- Suggested Next Crew is disabled or delegated when Crew R&R is loaded.

#### Settings and notifications
- Reworked EAC settings to keep the stock Difficulty Settings screen more compact.
- Moved detailed and lower-frequency settings into an EAC Advanced Settings window.
- Added Advanced Settings access from the EAC Space Center window.
- Moved message subcategories, veteran, suit, Badass, starting crew, auto-clean, and debug settings into Advanced Settings.
- Changed Auto-clean unreferenced retired/dead Kerbals into a one-shot command that resets unchecked after Apply.
- Re-enabling Message App support now defaults all EAC message categories back on.

#### Defaults, save migration, and persistence
- Updated default aging values: retire minimum 37, retire maximum 47, retired death minimum 50.
- Migrated EAC scenario save data to EACScenario.
- Removed stale or empty legacy RosterRotationScenario nodes from persistent saves.
- If legacy data-bearing EAC save information is found, EAC backs up the persistent file before cleanup and notifies the user at Space Center.
- Empty legacy scenario stubs are removed silently without backup or popup.
- Added or updated persistence for recovery state, Badass roll tracking, starting crew setup state, DeepFreeze-aware accumulated mission time, and settings.

#### Internal refactor and performance
- Split broad EAC 1.4 feature code into clearer service boundaries.
- Refactored veteran, suit, Badass, starting crew, and Suggested Next Crew logic into smaller services.
- Added type-keyed reflection caches in repeated UI and recovery paths.
- Replaced repeated dictionary snapshots in stale training cleanup with a key-list cleanup pass.
- Added Space Center UI instance refresh path and DeepFreeze bridge refresh throttling.
- Preserved existing save scheduling and idle-disabled runner behavior.

### 2026-0520: EAC v1.3.1 — Roster, DeepFreeze, Contracts, and Dependencies for KSP >= 1.12.x

#### Astronaut Complex roster tabs
- Fixed a bug where Kerbals from one Astronaut Complex tab could appear under another tab after switching views.
- Fixed the reported LOST-tab case where Available Kerbals could appear while viewing LOST.
- Tightened tab-specific filtering for Available, Assigned, Retired, and Lost lists after KSP rebuilds the Astronaut Complex UI.
- Improved roster cleanup timing so EAC re-applies the correct tab filter after UI refreshes rather than leaving stale rows visible.

#### DeepFreeze compatibility
- Added optional DeepFreeze compatibility handling.
- EAC now treats DeepFreeze frozen/suspended Kerbals as an external lifecycle state instead of processing them as normal active, missing, or deceased crew.
- Fixed the reported case where a Kerbal could be marked KIA after being taken out of suspended animation.
- DeepFreeze remains optional; EAC should continue to load normally when DeepFreeze is not installed.

#### Contract Configurator final-exam contracts
- Deferred all Contract Configurator final-exam XP awards until contract completion.
- Changed all affected AwardExperience blocks from awardImmediately = true to awardImmediately = false.
- Updated Level 1/2 Scientist rover science contracts so CollectScience no longer handles recovery directly.
- Changed affected Scientist CollectScience objectives from recoveryMethod = Recover to recoveryMethod = None.
- Added an explicit final rover/test-article recovery objective using EACRecoverVesselWithPart after the science objective completes.
- Updated visible contract titles, notes, and synopses so the intended flow is clear: collect science first, then recover the rover/test article.

#### Affected Scientist contracts
- EAC.Graduation.Scientist.Level1.KSCSurvey
- EAC.Graduation.Scientist.Level1.MysteryGoo
- EAC.Graduation.Scientist.Level1.InstrumentCalibration
- EAC.Graduation.Scientist.Level1.AtmosphericData
- EAC.Graduation.Scientist.Level1.ShorelineExpedition
- EAC.Graduation.Scientist.Level2.ThermometerRoverSurvey
- EAC.Graduation.Scientist.Level2.GooFieldSurvey
- EAC.Graduation.Scientist.Level2.PressureFieldSurvey
- EAC.Graduation.Scientist.Level2.GravityCalibration
- EAC.Graduation.Scientist.Level2.SeismicMotionCalibration

#### Required dependency documentation
- Documented HarmonyKSP / Harmony2 as a required dependency for EAC.
- Updated installation notes to verify GameData/000_Harmony before starting KSP with EAC enabled.
- Updated compatibility, troubleshooting, and packaging notes for the HarmonyKSP dependency.

#### Kerbal Changelog support
- Added Changelog.cfg support for the Kerbal Changelog mod.
- Added EAC release notes in Kerbal Changelog format for in-game display.
- Updated README installation, compatibility, troubleshooting, and packaging notes for Changelog.cfg.

#### Notes
- HarmonyKSP / Harmony2 is required. EAC will not load correctly without GameData/000_Harmony installed.
- Kerbal Changelog remains optional. EAC should still load without Kerbal Changelog installed.
- DeepFreeze remains optional. DeepFreeze compatibility only applies when DeepFreeze is installed.
- Contract Configurator remains optional. Final exam contract mode requires Contract Configurator and the EAC CC bridge.
- Mods that heavily replace or rebuild the Astronaut Complex UI may still conflict with EAC roster-tab adjustments.

### 2026-0516: EAC v1.3.0 — Contract Configurator Final Exams for KSP >= 1.12.x

#### Contract Configurator final exams
- Added optional Contract Configurator final-exam support for EAC training progression.
- Added support for EAC final exam requirements and completion behaviours through the EAC CC bridge.
- Added final exam tracking by Kerbal trait, target level, and exam ID.
- Added exam rotation support so the same exam is not repeatedly selected when alternatives exist.
- Added recovery handling for Kerbals who were pending or active in a Contract Configurator final exam when final exams are later disabled or Contract Configurator is removed.
- EAC now falls back to the normal training award path when final exam contracts are no longer available.

#### Scenario vessel and craft provisioning
- Added support for EAC-provided exam craft and scenario vessels for Contract Configurator exams.
- Added support for loading scenario vessels into the current save for contracts that require a pre-positioned test article.
- Added cleanup safeguards so spawned scenario vessels can be removed after use while protecting crewed vessels.

#### Applicant management
- Optimized applicant rejection by caching reflected KerbalRoster rejection methods.
- Fixed Reject All so it rejects all intended applicants instead of skipping entries while the applicant list changes.
- Added validation so applicant rejection only acts on valid applicant Kerbals.

#### Astronaut Complex roster fixes
- Fixed cases where retired Kerbals could temporarily appear in the Available tab after applicant rejection or retirement.
- Fixed cases where dead or missing Kerbals could appear in the Available tab after KSP rebuilt the Astronaut Complex roster.
- Improved Available / Retired / Lost tab cleanup after KSP UI refreshes.
- Cleaned up the EAC LOST tab so dead Kerbals no longer show an unnecessary current-age column while still showing useful age-at-death text.

#### Aging, retirement, and mission death
- Optimized aging and mission-death cleanup reflection paths.
- Cached proto-vessel and ConfigNode member lookups used when removing deceased Kerbals from assigned vessels.
- Confirmed mission old-age death cleanup removes deceased Kerbals from assigned but unlaunched vessels.
- Improved retirement and recall timestamp handling.

#### Performance and internal cleanup
- Reduced repeated reflection scans in applicant, vessel, aging, and mission-death paths.
- Reduced unnecessary roster and vessel list allocations.
- Improved crew-name cache correctness when roster contents change without a crew-count change.
- Preserved Contract Configurator spawned-vessel association order while reducing avoidable list copies.

### 2026-0505: EAC v1.2.1 for KSP >= 1.12.x

#### Fixed potential issues with Kerbin/Earth time. Earth time will now show correctly throughout EAC.
- Fixed issue with dismissed Kerbals who were Training still showing up.
- Minor code clean up.

### 2026-0412: EAC v1.2.0 for KSP >= 1.12.x

#### Fixed issue with Crash Detection giving a false positive.
- Fixed Space Center startup lags on heavily modded installs of KSP.
- Hall of History now only initiates when called, not at startup.
- Retired Tab added helper code so it fast loads versus scanning every object.
- Reduced calls from three to one on the persistent file.

### 2026-0411: EAC v1.1.9 — Jeremiah for KSP >= 1.12.x

#### This release is a behind-the-scenes maintenance update. It does not change gameplay, but it improves performance and reliability in crew-related screens, fixes a small retired-roster edge case, and cleans up the mod's internals for easier future updates.

### 2026-0409: EAC v1.1.8 for KSP >= 1.12.x

#### Improved retired-kerbal hiding performance by caching CrewAssignmentDialog field lookups after the first live dialog is found.
- Reduced repeated reflection overhead in ScrubRetiredFromObject() by reusing cached field references.
- Skipped unnecessary roster scans in HideRetiredKerbals() when no retired kerbals exist.

### 2026-0327: EAC v1.1.7 for KSP >= 1.12.x

#### Recovery / R&R
- Fixed a recovery timing bug where MissionStartUT could be cleared before post-mission recovery leave was calculated.
- Fixed a related flight-scene status-change issue where MissionStartUT could be wiped too early when a kerbal changed from Assigned to another roster state.
- Normal recovery leave now explicitly requests a save after it is applied, so rest/recovery state persists reliably.
- Fixed RestDay Max = 0 so it now behaves as a true zero cap instead of acting like no cap.
- Fixed a multi-crew crash-recovery issue where vessel-wide base recovery leave could be re-applied multiple times during no-injury outcomes. Base recovery leave is now only applied once per vessel recovery.

#### FlightTracker / veteran progression
- Fixed a bug where the one-time EAC to FlightTracker flight-count sync only ran when verbose logging was enabled. It now runs correctly for all users.
- Fixed veteran hour progression so FlightTracker takes precedence when installed.
- Fixed a potential double-counting issue where veteran service-hour growth could add both FlightTracker recorded hours and current mission time for the same flight.
- Veteran flight counts now also prefer FlightTracker when it is installed, instead of mixing or max-merging counts.

#### Retirement / morale
- Fixed a retirement-probability bug for kerbals who had never flown a mission.
- New kerbals were previously treated as if they had been inactive for an extreme amount of time, causing retirement odds that were far too high immediately after training.
- Never-flown kerbals are now treated as fresh rather than long-neglected veterans.

#### Persistence / save consistency
- Fixed a save-time flight-count drift issue where writing a reconciled flight total to the save file could also overwrite the live in-memory value for the rest of the session.
- Save reconciliation now preserves the live runtime record while still writing the corrected value to disk.

#### Internal cleanup / behavior consistency
- Recovery leave, crash leave, and veteran progression behavior were tightened up to better respect the intended precedence rules when external mods such as FlightTracker are installed.
- Reduced redundant recovery processing and verbose-log spam in multi-crew recovery edge cases.

### 2026-0326: EAC v1.1.6 for KSP >= 1.12.x

#### Added mission-time tracking that runs independently of aging.
- Added syncing in flight-scene startup, Kerbal status changes to/from Assigned, and KSC periodic update.

### 2026-0325: EAC v1.1.5 for KSP >= 1.12.x

#### Recovery timing updates
- Base recovery leave now uses each kerbal's own MissionStartUT.
- Crash recovery leave base time also uses each kerbal's own MissionStartUT.
- Recovery no longer uses vessel.missionTime for EAC leave calculations.
- Added per-kerbal verbose logging so you can verify missionDays, missionStartUT, baseRecoveryDays, and maxDays.
- If a kerbal's MissionStartUT was never set or is invalid, EAC now treats their personal mission duration as 0 for base recovery leave rather than falling back to vessel age.

### 2026-0324-1: EAC v1.1.4 for KSP >= 1.12.x

#### Fixed issue with RestDay and Recovery percentages not working as expected. Thanks Terensky!

### 2026-0324: EAC v1.1.3 for KSP >= 1.12.x

#### Fixed Training
- Level-up training now uses the configurable TrainingStarDays setting instead of hardcoded 30.
- Training confirmation preview now uses TrainingStarDays.
- Training overlay duration preview now uses TrainingStarDays.
- Recall refresher remains fixed at 30 days, unchanged.

### 2026-0323: EAC v1.1.2 for KSP >= 1.12.x

#### UI / Skinning
- Updated EAC UI styling to use KSP's native HighLogic.Skin instead of relying on BRP-only or generic GUI.skin styling.
- Applied KSP skin usage to main EAC windows, Hall of History windows, and related button and label styles.
- Removed the temporary custom gray window override and switched to pure KSP skin window styling.
- Windows now match stock KSP more closely, and theme mods such as HUDReplacer and ZTheme can affect EAC window appearance naturally.

#### Memorial / Hall of History
- Changed the memorial page label from Service Time to Flight Hours for clarity.
- Memorial flight-hours display now prefers FlightTracker data when available.
- Removed EAC fallback mission-hours display from the memorial page.
- If FlightTracker is not installed, or no flight-hours data is available, Flight Hours is not shown instead of displaying an unclear unavailable message.
- Confirmed and retained separate Flights tracking in EAC.

#### Recovery / Vacation Time
- Made recovery-time settings visible in the settings UI.
- Added a new Recovery time section in the Aging column.
- Added Recovery leave percentage setting, adjustable from 0 percent to 100 percent; 0 percent disables EAC recovery leave calculation.
- Reworked restDays to act as RestDay Max, now used as the maximum recovery/vacation time cap.
- If CrewRandR is installed, it takes precedence over EAC's internal recovery-time system.

#### Crash Detection / Crash Penalties
- Reworked crash detection so crash penalties apply to the craft the crew is currently occupying rather than detached-stage incidents.
- Fixed an issue where detached boosters or staged-off parts crashing later could trigger a penalty on the occupied vessel.
- Added split/separation-aware crash handling and detached-vessel matching so clean staging does not count against the crewed vessel.

### 2026-0316: EAC v1.1.1 for KSP >= 1.12.x

#### UI and portrait fixes
- Made EAC windows more opaque.
- Adjusted some windows to not open on top of each other.
- Adjusted portrait capture so that valid portraits are captured versus static screens.
- Portraits are stored in /saves/(savegamename)/EAC/HallofPortraits.
- Minor logic fixes and visual fixes.

### 2026-0314: EAC v1.1.0 for KSP >= 1.12.x

#### New features and improvements
- Added crash outcome handling, including configurable injury and medical-retirement style penalties on recovery.
- Added support for mission old-age death checks for Kerbals serving beyond retirement age.
- Added new Space Center / Astronaut Complex UI extensions for retirement, training, and retired crew management.
- Added Hall of History with Memorial Wall, portrait capture, Milestone Wall, and veteran presentation/status support.
- Added optional cleanup of unreferenced retired Kerbals, with safeguards to avoid removing stock-referenced data.
- Improved notifications, save/persistence handling, and optional mod compatibility behavior.

#### Internal reliability and maintenance refactoring
- Centralized save scheduling.
- Safer reflection helpers.
- Better logging and diagnostics.
- Fewer silent failures.
- Stronger UI/object discovery checks.

### 2026-0307: EAC v1.0.2 for KSP >= 1.12.x

#### Debug and Astronaut Complex performance fixes
- Fixed issue with Debug information not being sent to the KSP.log as expected.
- EAC will now clearly show ACOpenPolls=0 ExpensiveScans=0 ScanMs=0.0 FPS=0.0 in Debug mode.
- Fixed ACOpenCache scan throttling.
- Reworked AstronautComplexHook.
- Stopped scanning entirely when Astronaut Complex is closed; Harmony hooks are used as the trigger.

### 2026-0306: EAC v1.0.1 for KSP >= 1.12.x

#### Initial performance and Astronaut Complex fixes
- Fixed issue with slow framerate. Clean install improved FPS by 20+ FPS.
- Further optimized code.
- Reordered tabs in Astronaut Complex to Available/Assigned/Retired/Lost.
- Added configurable cost to recall retired Kerbals.
- Added further debugging options.

### 2026-0303: EAC v1.0.0 for KSP >= 1.12.x

#### Initial release.
