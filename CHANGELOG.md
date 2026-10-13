# Changelog

## 0.1.3

- **Battery modes on the 2025 and later Blades**: on battery, Silent is sent
  as the firmware's Battery Saver and Balanced as Battery Balanced, as Synapse
  does. On that firmware, Silent and Balanced ran alike on battery. The first
  time, the app checks that the firmware keeps the battery mode, and falls
  back to the plain modes if it does not; the result and the BIOS version go
  to the log.
- **Razer Blade power plan**, from a user's tuning: plugged in, the energy
  performance preference goes to 20, 60% of the boost is used and the
  frequency rises straight to the top; on battery, the preference goes to 80
  and at least 30% of the cores stay awake.

## 0.1.2

- **Apps**: a new page with a profile for each game or app (mode, fans,
  refresh rate, screen colors, keyboard lighting and game mode), put on while
  it runs and undone when it closes. Games installed with Steam, Epic Games,
  Xbox and Battle.net are found for you.
- **Game mode**, in app profiles: blocks the Windows key, and optionally
  Alt+Tab and Alt+F4, while the game runs.
- **Shortcuts**: Fn+Del shows or hides the window, and Ctrl+Shift+F1 / F2 / F3
  switch to Silent, Balanced and Custom, with a small notice.
- **Performance**: Custom's CPU and GPU levels as dropdowns, and a short
  description on each mode.
- **Display**: keep running with the lid closed while a monitor is connected
  and the charger is in; whether the dedicated GPU is awake or asleep and which
  apps use it; Night light warmer, at about 4500 K.
- **Device panel**: the battery's watts (also on the tray icon's hover), the
  controller's firmware version and the charger in.
- **Razer Blade power plan**: also tunes the processor, Wi-Fi, PCI Express,
  NVMe and Intel graphics, for performance plugged in and battery life on
  battery. A plan installed by an earlier version keeps its old values until
  it is removed in Windows and installed again.
- **Settings**: export and import your settings to a file. An unreadable
  settings file is kept as a copy instead of being replaced.
- The battery chart's guide lines line up with their labels.

## 0.1.1

- Recognizes every Razer Blade from 2021 on, including the Blade 14 (2025),
  which the app reported as not found, and the 2026 models.

## 0.1.0 — first release

Blade Control's first version: a control center for Razer Blade laptops in a
full Windows window, without Razer Synapse.

- **Performance**: Silent, Balanced and Custom, with Custom's CPU and GPU
  levels; a mode for each power source, switched when plugging in or
  unplugging; max fan speed and the battery charge limit where the laptop
  has them. Silent turns Windows' CPU boost off, which can be turned off
  under Settings, Experimental.
- **Lighting**: keyboard and logo effects, color and brightness, and the
  keyboard going dark with the screen, when idle or on low battery.
- **Display**: the refresh rate (Auto follows the charger), color profiles
  (Standard, Night light, Neutral, Contrast), whether an external display is
  connected, and Integrated graphics only, which turns the dedicated GPU off.
- **Battery**: charge, power, time left, the last 24 hours and the battery's
  health.
- **Optimize**: free up memory, clean temporary files, clear the NVIDIA
  shader cache, close the apps keeping the dedicated GPU awake, hibernation,
  and Razer's own software.
- **Device panel**: the laptop's picture, battery, Windows' power plan and
  CPU and GPU temperatures.
- **Tray**: the app stays in the tray when its window closes, and can start
  with Windows.
- English and Spanish.
- Portable: one folder, nothing to install, .NET included.
