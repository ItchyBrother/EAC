# Enhanced Astronaut Complex (EAC)

# Change Log
### 2026-0324: 1.1.2 for KSP >= 1.12.X

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
