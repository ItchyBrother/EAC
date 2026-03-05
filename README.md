# Enhanced Astronaut Complex (EAC)

Enhanced Astronaut Complex (EAC) adds deeper Kerbonaut lifecycle management to Kerbal Space Program by extending the Astronaut Complex with:

- **Training** (time + costs + optional funding/science scaling)
- **Aging** (Kerbals age over time)
- **Retirement** (Kerbals retire, can be recalled under rules)
- **Experience decay while retired** (stars decrease with time in retirement)
- **Configurable notifications** routed to KSP’s Message System and/or HUD

Designed to feel “stock-like” while adding meaningful long-term crew management.

**I would recommend using this mod in conjuction with linuxgurugamer CrewRandR mod.  CrewRandR is not required for EAC to work.**

---

## Features

### Training
- Training time scales with experience level.
- Training costs scale with configured multipliers and per-star adjustments.
- Training messages are optional and configurable.

### Aging & Retirement
- Kerbals age in years (Kerbin time supported).
- Retirement age range is configurable.
- Optional “retired death” minimum age (if enabled).
- Retirement events generate optional notifications.

### Retired Tab (Astronaut Complex)
- Adds a **Retired** tab to the Astronaut Complex roster UI.
- Shows retired Kerbals with their **current effective experience stars**.
- Experience stars **decay over time in retirement** (default: lose 1 star per year retired).
- Recall eligibility respects Astronaut Complex capacity (retired Kerbals do **not** consume active slots, but recall is blocked when at max).

### Notifications
You can enable/disable notifications by category:
- Birthdays
- Training
- Retirement
- Deaths

And choose where they appear:
- HUD
- Message App (KSP Message System)

---

## Installation

1. Download the release zip.
2. Copy the `GameData/EAC/` folder into your KSP `GameData/` directory.

You should end up with something like:

```text
Kerbal Space Program/
  GameData/
    EAC/
      Plugins/
        EAC.dll
      [Assets...]
```

---

## Settings

EAC settings are available in:

**Space Center → Settings → Difficulty Options → EAC**

From there you can configure:
- Kerbin time vs Earth time
- Training parameters (days, funds/science scaling)
- Aging on/off
- Retirement ages
- Notification routing and categories
- Debug/verbose logging

All settings are saved into your save file and persist across reloads.

---

## Save Data

EAC stores per-Kerbal records and settings in the save file under the ScenarioModule and EAC nodes.  
If you manually edit saves, make backups first.

Typical stored items include:
- retirement status / retirement time
- experience at retirement
- birth time / aging info
- training state and end time
- message and feature toggles

---

## Compatibility

- Built for **Kerbal Space Program 1.12** but should work with earlier versions.
- Intended to be compatible with stock Astronaut Complex UI.
- Other mods that heavily replace Astronaut Complex UI may conflict.

If you see UI oddities, try:
- verifying only one copy of the plugin DLL is installed
- checking for other mods that patch Astronaut Complex lists/buttons

---

## Known Notes

- Some Astronaut Complex UI layouts may not include certain stock lists (e.g., Lost tab variations). EAC handles this gracefully.
- Facility upgrades can cause the Astronaut Complex UI to rebuild; EAC attempts to re-sync hiring/cap behavior automatically.

---

## Troubleshooting

### Where to find logs
Check `KSP.log` for lines starting with:
- `[EAC]`

### Enable verbose logging
In **Difficulty Options → EAC → Debug**, enable:
- Verbose UI logs
- Verbose aging logs

Use verbose logging only when troubleshooting.

---

## Planned / Ideas
- Currently no plans...we'll see how the mod is received and what the KSP community would like in maybe future releases.

---

## License
MIT License.  You are free to use.  I guarantee nothing other than this will take up disk space.  

* By downloading this mod you agree to hold me harmless of any issues that may or may not occur.  (e.g. you will not hold me liable for defects cause by the software)
* If you reuse or modify any of my code, you must include the copyright and license with that code.
* You are free to reuse and modify - just give credit where credit is due.
* You are free to distribute my code.

---

## Credits
- Kerbal Space Program by Squad / Private Division
- Thanks to the KSP modding community especially linuxgurugamer who indirectly inspired me.

---

## Thank you!
Thank you for downloading my mod.  I appreciate your support and look forward to your constructive feedback.  
I'd love to hear how you are using it!
