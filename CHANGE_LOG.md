# Enhanced Astronaut Complex (EAC)

*	2026-0316: 1.1.1 for KSP >-1.12.X
	-	Made EAC windows more opaque
	-	Adjusted some windows to not open on top of each other
	-	Adjust portrait capture so that valid portraits are captured verses "static screens"
	-	Portraits are stored in /saves/<savegamename>/EAC/HallofPortraits 
	-	Minor logic fixes and visual fixes

* 2026-0314: 1.1 for KSP >= 1.12.X

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

* 2026-0307: 1.0.2.0 for KSP >= 1.12.X
  - Fixed issue with Debug information not being sent to the KSP.log as expected.
  - EAC will now clearly show ACOpenPolls=0  ExpensiveScans=0  ScanMs=0.0  FPS=0.0 in Debug mode
  - Fixed ACOpenCache scan throttling
  - Reworked AstronautComplexHook
  - Stop scanning entirely when AC is closed. Use the Harmony hooks as the trigger
  - Now ACOpenCache.IsOpen is triggered by Harmony hooks on KSP.UI.Screens.AstronautComplex.Start/Awake — fires the moment the dialog is created.
  - EAC is now completely harmless with Jerkiness/Freezing on opening screen.  All Unity Garbage Collection caused.

* 2026-0306: 1.0.1.0 for KSP >= 1.12.X
  - Fixed issue with slow Framerate. (Clean install improved FPS by 20+FPS)
  - Further Optmize code
  - Reorder tabs in Astronaut Complex to Available/Assigned/Retired/Lost
  - Added cost to recall retired Kerbals (configurable)
  - Add further debugging options
 
* 2026-0303: 1.0 for KSP >= 1.12.X
  - Initial release.
