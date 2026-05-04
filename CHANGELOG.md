# Changelog

All notable changes to NariMeter are documented here.

---

## [v1.1.1] — 2026-05-04

### Fixed

* `UsbDevice`: transient device open failures no longer reset accumulated debounce state (`_idleCount` / `_activeCount`), preventing false `PoweredOff` transitions after USB glitches
* `TrayApp`: staleness guard in `OnBatteryTick` was recursing into itself instead of deferring to the next timer cycle — replaced with a plain `return`
* `TrayApp`: `_activeConfirm` was never reset after confirming an inactive→active transition, accumulating indefinitely across reconnections
* `TrayApp`: `BatteryReader.NotifyCableRemoved` was never called — wired to the `Charging → Discharging` transition in `CheckNotifications`, activating post-cable stabilization
* `BatteryReader`: `PollState` had two identical branches behind a dead `_initialized` flag — collapsed into a single return path; flag removed
* `StateStore`: `Save*` methods each called `Load()` from disk on every invocation — replaced with an in-memory cache; state is loaded once and mutated in place
* `StateStore`: file writes are now atomic via a temp file + `File.Move(overwrite: true)`, preventing state corruption on process crash mid-write

---

## [v1.1.0] — 2026-05-01

### Added

* Low battery notifications with configurable warn and critical thresholds (default: 20% / 10%)
* Fully charged notification when headset reaches 100% while on cable
* Notification settings and battery thresholds now persist across sessions

### Changed

* Reduced executable size by switching to framework-dependent publish (`SelfContained: false`)
* GC and thread pool tuned for lower memory footprint at steady state

### Requirements

* [.NET 8 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime) now required

---

## [v1.0.2] — 2026-04-29

### Changed

* Charging state now read directly from USB response (`resp[9]`), replacing mV trend analysis
* Battery percentage now sourced from `resp[14]` natively, removing voltage-to-percent conversion

### Fixed

* Tray tooltip now correctly displays `100% Fully Charged` when cable is connected and battery is full

---

## [v1.0.1] — 2026-04-29

### Added

* Battery state persistence across sessions via `NariMeter.state.json`

### Changed

* Charging detection via mV trend analysis across consecutive readings

### Fixed

* Charging not detected when opening the app with cable already connected
* mV inflated by charger being incorrectly saved as a valid discharge reading

---

## [v1.0.0] — 2026-04-21

### Added

* Initial release
* Real-time battery level displayed in Windows system tray
* Color-coded tray icons (green / yellow / red / charging / powered off / disconnected)
* Charging and fully charged states
* Run at Startup toggle via Windows registry
* Hover tooltip with current battery status
* USB protocol reverse engineered from Wireshark captures — no Razer Synapse required
* Portable single `.exe`, no installation needed
