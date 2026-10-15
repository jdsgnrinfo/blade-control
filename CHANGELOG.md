# Changelog

## 0.1.6

- **Fixed: the color profile could be changed only once** after opening the
  app (seen on a Blade 16 2023). Color profiles are handled exactly as in
  0.1.4: the app sets them itself, they stay on when it closes until Windows
  resets the screen, and they are no longer installed in Windows as ICC
  profiles. Neutral takes a little more red off: 85% instead of 90%.
- **Razer Blade power plan**: plugged in, the CPU's speed rises as the load
  needs (Ideal) instead of straight to the top (Rocket). A plan installed
  before keeps its old value until it is installed again.

## 0.1.5

- **Color profiles stay without the app**: the profile picked on the Display
  page (Night light, Neutral, Contrast) is also installed in Windows as the
  laptop screen's ICC profile, for your user only, so Windows keeps those
  colors at sign-in, after sleep and with Blade Control closed. Standard takes
  it out and puts back the screen's profile from before. No administrator
  permission is needed. A game's colors from an app profile still last only
  while the game runs.
- **Neutral** takes a little more red off: 85% instead of 90%. A profile
  already in Windows is replaced with the new values.
- **Battery mode**, a switch on the Performance page: on battery, Silent,
  the screen at 25% and the keyboard at 20% (never raised), 60 Hz when a
  fixed refresh rate is picked (Auto already does it), Windows' Energy
  saver on, transparency, window and pointer shadows and window contents
  while dragging off, and an offer to close the apps keeping the NVIDIA GPU
  awake. Plugging in, turning it off or exiting puts everything back; a
  level changed by hand meanwhile stays as you set it, and what a crash left
  is put back at the next start.
- **Updates from the app**: Blade Control looks on GitHub for a newer version
  10 seconds after starting and once a day (Settings, General turns it off),
  and Settings has a button to look now. A newer version puts a green dot on
  the Settings button and shows a Windows notice. Update downloads the
  portable zip, checks it against its published SHA-256, puts it in the app's
  folder and restarts; settings and profiles stay. Where the app cannot
  update itself (a folder it may not write to, or any failure), the release's
  page opens to download it by hand. A version can be skipped.
- **Performance modes in the tray menu**: Silent, Balanced and Custom, with a
  check on the one in use, switched as the mode shortcuts switch them and with
  the same notice. Custom is greyed out on battery. The menu's text is 12 px.
- **Opens faster**: from about 1.35 to 0.95 seconds on a Blade 15 Base (2020).
  The exe is compiled ahead of time and no longer compressed, so .NET does
  not unpack it at each start (450 ms down to 190). It is bigger on disk
  (about 180 MB), but the download barely changes (75 MB against 69).
- **A minimized window counts as in the tray**: sensors stop being read and
  its RAM goes back to Windows (from about 300 MB to 30).
- **New loader**: three green dots bouncing in a wave instead of the turning
  arc. On the Optimize page, when a job ends they turn into a green check, or
  a red cross if it failed, for a moment before the button comes back.
- **Pages switch at once**: they no longer fade in and rise when opened.
- The log records how long each part of starting took, and the memory left
  each time the app releases it.
- Smaller touches: the GPU temperature shows 0° when it cannot be read, the
  information notes have a new mark and the GPU switch's warning is part of
  its note, the tray menu's check and the Apps page's empty state have new
  icons, Custom's icon is a little smaller, and the Battery page's health
  card no longer points to the charge limit.

## 0.1.4

- **Fewer sensor reads on battery**: the device panel reads every 5 seconds
  instead of every second, the system monitor every 3, the Battery page every
  5, the Display page's GPU line every 10, the app profiles' watch every 5 and
  Silent's checks every 10. Plugged in, nothing changes. With the Performance
  page open on battery, the app went from about 15% of a core to 4.5%.
- **System monitor switch**: Settings, General turns the Performance page's
  system monitor off; its card goes and nothing is read for it.
- **The Performance page no longer keeps the NVIDIA GPU awake.** Its system
  monitor asked NVIDIA's driver library for the GPU's load every second, so
  once anything woke the GPU, the monitor held it awake for good, with
  Windows' System process busy on about a whole core. The library is now asked
  only while another app is using the GPU and let go when none is.

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
