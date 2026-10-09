# The Audio page (archived)

The Audio page was taken out of Blade Control while it was still in testing.
It is not lost: the whole of it, as it last worked, is kept in the history
under the tag `archive/audio-page`. This note says what it was, what was
learned, and how to bring it back to start again with another approach.

## What it did

- **Output device**: the playback devices Windows has, the one in use picked
  from a list (picking another made Windows play to it), refreshed when a
  device was plugged in or out.
- **Equalizer**: ten bands (31 Hz to 16 kHz), from +6 to -6 dB, drawn as a
  curve with a handle per band (drag, mouse wheel, arrow keys, double click
  back to 0). Each output device kept its own curve.
- **Presets**: Flat, Bass boost, Treble boost, Vocal, Gaming and Loudness
  built in; the user's own saved, renamed and deleted.

## How it worked

Blade Control did not process sound. It wrote the curves into
[Equalizer APO](https://sourceforge.net/projects/equalizerapo/), which does:

- It wrote Equalizer APO's `config.txt`, one block per device, each a
  `GraphicEQ` line with the ten bands, marked `# Written by Blade Control.`
  The file it found there the first time was kept as
  `config.before-BladeControl.txt`.
- Before each curve, a `Preamp` of minus the highest boost, plus 1 dB more,
  so a raised band does not clip.
- At startup it wrote every saved device again, and it followed devices
  coming and going through Windows' `IMMNotificationClient`, waiting 600 ms
  for things to settle.
- It knew which devices Equalizer APO was turned on for, said so when a
  device was not, and opened the Device Selector to turn it on.

## Why it was taken out

- It needs Equalizer APO installed, every device ticked once in its Device
  Selector, and a restart of Windows: too much to ask for an app that should
  just work.
- Large boosts distorted a little, and full cuts did not silence a band
  completely. The range went from +12/-24 dB with an off floor, to +24/-24,
  and settled at +6/-6, which sounds clean but does less.
- Doing the processing in the app itself (as FxSound does) means an audio
  driver or an APO of its own, signed, installed with administrator rights:
  a project of its own, not a page.

## What to try next time

- An APO of our own, or a virtual audio device, so nothing else needs
  installing; it needs a signed driver.
- Or keep Equalizer APO but install and set it up from the app, with fewer
  steps for the user.
- A parametric curve (frequency, gain and Q per band) instead of fixed
  bands, and a real limiter instead of a fixed preamp.

## Bringing it back

The files as they were:

```bash
git checkout archive/audio-page -- \
  src/BladeControl/Views/AudioView.xaml \
  src/BladeControl/Views/AudioView.xaml.cs \
  src/BladeControl/ViewModels/AudioViewModel.cs \
  src/BladeControl/UI/EqualizerGraph.cs \
  src/BladeControl/Views/PresetNameWindow.xaml \
  src/BladeControl/Views/PresetNameWindow.xaml.cs \
  src/BladeControl.Core/Services/Equalizer.cs \
  src/BladeControl.Core/Platform/WindowsAudio.cs \
  tests/BladeControl.Core.Tests/Services/EqualizerTests.cs
```

They also need putting back in the places that used them. The commit that
took the page out shows each one (`git show` on it):

- `MainViewModel`: the `Audio` page, its view model, `IsAudio`, and its
  refresh and flush when pages change.
- `App.xaml.cs`: `Audio.Restore()` at startup, `Flush` and `Dispose` on exit.
- `MainWindow.xaml`: the Audio tab and the `AudioView`.
- `SmoothScroll`: the equalizer keeps the mouse wheel to itself.
- `AppSettings`: `EqPresets` and `AudioDevices`.
- `ExternalLinks`: the Equalizer APO download link.
- `L.cs`: the Spanish texts of the page.
