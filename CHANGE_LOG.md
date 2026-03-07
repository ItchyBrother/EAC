# Enhanced Astronaut Complex (EAC)

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
