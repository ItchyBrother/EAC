# Enhanced Astronaut Complex (EAC)

# Change Log

# 2026-0412: EAC v1.2.0 for KSP >= 1.12.X
-	Fixed issue with Crash Detection giving a false positive.  
- 	Fixed Space Center startup lags on heavily modded installs of KSP.  
	-	Hall of History now only intiates when called, not at startup.
	-	Retired Tab added helper code so it fast loads verse scanning every object.
	-	Reduced calls from three to one on the persistent file.

### 2026-0411: EAC v1.1.9 aka "Jeremiah" for KSP >= 1.12.X
- This release is a behind-the-scenes maintenance update. It does not change gameplay, but it improves performance and reliability in crew-related screens, fixes a small retired-roster edge case, and cleans up the mod’s internals for easier future updates.

### 2026-0409: EAC v1.1.8 for KSP >= 1.12.X
- Improved retired-kerbal hiding performance by caching CrewAssignmentDialog field lookups after the first live dialog is found.
- Reduced repeated reflection overhead in ScrubRetiredFromObject() by reusing cached field references.
- Skipped unnecessary roster scans in HideRetiredKerbals() when no retired kerbals exist.

### 2026-0327: EAC v1.1.7 for KSP >= 1.12.X

### Recovery / R&R
- Fixed a recovery timing bug where `MissionStartUT` could be cleared before post-mission recovery leave was calculated.
- Fixed a related flight-scene status-change issue where `MissionStartUT` could be wiped too early when a kerbal changed from `Assigned` to another roster state.
- Normal recovery leave now explicitly requests a save after it is applied, so rest/recovery state persists reliably.
- Fixed `RestDay Max = 0` so it now behaves as a true zero cap instead of acting like “no cap.”
- Fixed a multi-crew crash-recovery issue where vessel-wide base recovery leave could be re-applied multiple times during no-injury outcomes. Base recovery leave is now only applied once per vessel recovery.

### FlightTracker / veteran progression
- Fixed a bug where the one-time EAC → FlightTracker flight-count sync only ran when verbose logging was enabled. It now runs correctly for all users.
- Fixed veteran hour progression so FlightTracker takes precedence when installed.
- Fixed a potential double-counting issue where veteran service-hour growth could add both FlightTracker recorded hours and current mission time for the same flight.
- Veteran flight counts now also prefer FlightTracker when it is installed, instead of mixing or max-merging counts.

### Retirement / morale
- Fixed a retirement-probability bug for kerbals who had never flown a mission.
- New kerbals were previously treated as if they had been inactive for an extreme amount of time, causing retirement odds that were far too high immediately after training.
- Never-flown kerbals are now treated as fresh rather than long-neglected veterans.

### Persistence / save consistency
- Fixed a save-time flight-count drift issue where writing a reconciled flight total to the save file could also overwrite the live in-memory value for the rest of the session.
- Save reconciliation now preserves the live runtime record while still writing the corrected value to disk.

### Internal cleanup / behavior consistency
- Recovery leave, crash leave, and veteran progression behavior were tightened up to better respect the intended precedence rules when external mods such as FlightTracker are installed.
- Reduced redundant recovery processing and verbose-log spam in multi-crew recovery edge cases.

### 2026-0326: 1.1.6 for KSP >= 1.12.X
-	Added mission-time tracking that runs independently of aging
-	Added syncing in:
		flight-scene startup
		kerbal status changes to/from Assigned
		KSC periodic update

### 2026-0325: 1.1.5 for KSP >= 1.12.X
-	Base recovery leave now uses each kerbal’s own MissionStartUT
-	Crash recovery leave base time also uses each kerbal’s own MissionStartUT
-	Recovery no longer uses vessel.missionTime for EAC leave calculations
-	Added per-kerbal verbose logging so you can verify:
		missionDays
		missionStartUT
		baseRecoveryDays
		maxDays
-	if a kerbal’s MissionStartUT was never set or is invalid, EAC now treats their personal mission duration as 0 for base recovery leave rather than falling back to vessel age.

###	2026-0324-1 1.1.4 for KSP >= 1.12.X

- Fixed issue with RestDay and Recovery percentages not working as expected.  Thanks Terensky!

### 2026-0324: 1.1.3 for KSP >= 1.12.X

### Fixed Training
- Level-up training now uses the configurable TrainingStarDays setting instead of hardcoded 30
- Training confirmation preview now uses TrainingStarDays
- Training overlay duration preview now uses TrainingStarDays
- Recall refresher remains fixed at 30 days, unchanged

### 2026-0323: 1.1.2 for KSP >= 1.12.X

### UI / Skinning
- Updated EAC UI styling to use **KSP’s native `HighLogic.Skin`** instead of relying on BRP-only or generic `GUI.skin` styling.
- Applied KSP skin usage to:
  - main EAC windows
  - Hall of History windows
  - related button and label styles
- Removed the temporary custom gray window override and switched to **pure KSP skin window styling**.
- Result:
  - windows now match stock KSP more closely
  - theme mods such as **HUDReplacer** and **ZTheme** can affect EAC window appearance naturally

### Memorial / Hall of History
- Changed the memorial page label from **Service Time** to **Flight Hours** for clarity.
- Memorial flight-hours display now prefers **FlightTracker** data when available.
- Removed EAC fallback mission-hours display from the memorial page.
- If FlightTracker is not installed, or no flight-hours data is available, **Flight Hours** is not shown instead of displaying an unclear “unavailable” message.
- Confirmed and retained separate **Flights** tracking in EAC, so:
  - **Flights** = number of recorded flights/missions
  - **Flight Hours** = total recorded mission/flight time

### Recovery / Vacation Time
- Made recovery-time settings visible in the settings UI.
- Added a new **Recovery time** section in the **Aging** column.
- Added **Recovery leave (%)** setting:
  - adjustable from **0% to 100%**
  - **0%** disables EAC recovery leave calculation
- Reworked `restDays` to act as **RestDay Max**:
  - now used as the **maximum recovery/vacation time cap**
  - no longer the primary fixed recovery-time source
- EAC recovery leave is now calculated as:
  - **mission flight time × Recovery leave (%)**
  - capped by **RestDay Max**
- If **Recovery leave (%) = 0**, then:
  - EAC recovery leave is off
  - **RestDay Max** remains visible but is disabled in the settings UI
- If **CrewRandR** is installed, it takes precedence over EAC’s internal recovery-time system.

### Crash Detection / Crash Penalties
- Reworked crash detection so crash penalties apply to the **craft the crew is currently occupying**, rather than incorrectly attributing detached-stage incidents to the crewed craft.
- Fixed an issue where detached boosters or staged-off parts crashing later could trigger a penalty on the occupied vessel.
- Removed earlier passive part-loss behavior that overcounted normal staging as crash damage, then reintroduced it in a safer form with split/separation handling.
- Improved crash event handling by incorporating:
  - direct part destruction
  - explosion events
  - impact/collision events
  - crash/splashdown events
- Added logic to avoid incorrectly attributing explosion events to `FlightGlobals.ActiveVessel`.
- Added split/separation-aware crash handling:
  - buffers part loss during separation events
  - subtracts cleanly detached vessels from crash-loss calculations
  - only counts the **residual loss** against the occupied craft
- Added a fallback implicit separation window for cases where KSP does not provide the expected separation/create event chain.
- Added nearby detached-vessel matching so staged-off boosters/debris are recognized and excluded from crash penalties when appropriate.
- Verified intended behavior:
  - genuine damage to the occupied craft can produce a crash penalty
  - clean booster separation followed by booster collision does **not** produce a crash penalty on the crewed vessel

### Flight / Career Tracking
- Confirmed EAC continues to track **Flights** independently from flight hours.
- Preserved existing EAC flight-count behavior for memorial and career record display.

### Settings Layout
- Added the new recovery-time controls to the **Aging** column.
- Left the **General** column layout unchanged after review, to avoid simply shifting the scroll-box issue into another column.

###	2026-0316: 1.1.1 for KSP >-1.12.X
-	Made EAC windows more opaque
-	Adjusted some windows to not open on top of each other
-	Adjust portrait capture so that valid portraits are captured verses "static screens"
-	Portraits are stored in /saves/<savegamename>/EAC/HallofPortraits 
-	Minor logic fixes and visual fixes

### 2026-0314: 1.1 for KSP >= 1.12.X

**New features and improvements**
-	Added crash outcome handling, including configurable injury and medical-retirement style penalties on recovery.
-	Added support for mission old-age death checks for Kerbals serving beyond retirement age.
-	Added new Space Center / Astronaut Complex UI extensions for retirement, training, and retired crew management.
	
**-Added the Hall of History with**
-	Memorial Wall for fallen Kerbals
-	Added a Kerbal portrait capture (configurable) for Hall of History/Memorial
-	Milestone Wall for notable crew accomplishments and service history
-	Added veteran presentation/status support in history/archive displays.
-	Added optional cleanup of unreferenced retired Kerbals, with safeguards to avoid removing stock-referenced data.
-	Improved notifications and messaging for retirement, birthdays, training, and other crew-state changes.
-	Improved save/persistence handling to make crew-state updates more reliable.
-	Improved compatibility behavior when optional supported mods are present.
	
**-Performed major internal reliability and maintenance refactoring, including:**
-	Centralized save scheduling
-	Safer reflection helpers
-	Better logging and diagnostics
-	Fewer silent failures
-	Stronger UI/object discovery checks

Note:

This release includes substantial behind-the-scenes cleanup intended to improve reliability and make future updates easier to maintain.
EAC remains standalone aside from Harmony. Optional compatibility support can be used when supported mods are installed such as CrewRandR, EarnYourStrips and FlightTracker

### 2026-0307: 1.0.2.0 for KSP >= 1.12.X
  - Fixed issue with Debug information not being sent to the KSP.log as expected.
  - EAC will now clearly show ACOpenPolls=0  ExpensiveScans=0  ScanMs=0.0  FPS=0.0 in Debug mode
  - Fixed ACOpenCache scan throttling
  - Reworked AstronautComplexHook
  - Stop scanning entirely when AC is closed. Use the Harmony hooks as the trigger
  - Now ACOpenCache.IsOpen is triggered by Harmony hooks on KSP.UI.Screens.AstronautComplex.Start/Awake — fires the moment the dialog is created.
  - EAC is now completely harmless with Jerkiness/Freezing on opening screen.  All Unity Garbage Collection caused.

### 2026-0306: 1.0.1.0 for KSP >= 1.12.X
  - Fixed issue with slow Framerate. (Clean install improved FPS by 20+FPS)
  - Further Optmize code
  - Reorder tabs in Astronaut Complex to Available/Assigned/Retired/Lost
  - Added cost to recall retired Kerbals (configurable)
  - Add further debugging options
 
### 2026-0303: 1.0 for KSP >= 1.12.X
  - Initial release.
