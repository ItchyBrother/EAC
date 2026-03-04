# Enhanced Astronaut Complex (EAC)

Enhanced Astronaut Complex (EAC) adds deeper Kerbonaut lifecycle management to Kerbal Space Program by extending the Astronaut Complex with:

- **Training** (time + costs + optional funding/science scaling)
- **Aging** (Kerbals age over time)
- **Retirement** (Kerbals retire, can be recalled under rules)
- **Experience decay while retired** (stars decrease with time in retirement)
- **Configurable notifications** routed to KSP’s Message System and/or HUD

Designed to feel “stock-like” while adding meaningful long-term crew management.

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
