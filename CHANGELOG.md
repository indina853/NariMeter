# Changelog

All notable changes to NariMeter are documented here.

---

## [v1.4.3] — 2026-08-01

### Fixed
- `UsbDevice`: device enumeration now targets the `mi_05`/`col03` HID collection (UsagePage `0xFF00`) — the only collection exposing Feature Reports; `col01`/`col02` expose none
- `UsbDevice`: `ERROR_INSUFFICIENT_BUFFER` (122) from `SetupDiGetDeviceInterfaceDetail` is now handled as the normal size query instead of being treated as failure
- `UsbDevice`: device path now read from the correct offset — `cbSize` is 4 bytes on 32-bit (was 6) and the path string starts at offset 4 after the `cbSize` DWORD (was `DetailDataSize`)
- `UsbDevice`: `Response[0]` is set to `0xFF` before `HidD_GetFeature` — without the Report ID the call returns `ERROR_INVALID_PARAMETER` (87) and the response buffer stays empty
- `BatteryReader`: reconnecting the cable during the 90s post-unplug stabilization hold is now detected — two consecutive `isCharging` firmware readings end the hold and report `Charging` immediately, instead of showing `Discharging` for up to 90 seconds

### Changed
- Executable size reduced from ~349 KB to ~204 KB
- Version bumped to 1.4.3

---

## [v1.4.2] — 2026-08-01

### Changed
- Replaced LibUsbDotNet USB stack with native Windows HID API (`hid.dll` + `setupapi.dll`) via P/Invoke
- No WinUSB driver installation required anymore — NariMeter now uses the standard Windows HID driver (`hidusb.sys`) directly on Interface 5
- Open device enumeration (SetupAPI) filters by `vid_1532&pid_051c` and `mi_05`, matching the interface that answers Feature Report 0xFF
- Set/Get Feature reports now use the HID API equivalent of the captured USB control transfers (`SET_REPORT`/`GET_REPORT`, wValue `0x03FF`, wIndex 5)
- README: removed the entire Zadig/WinUSB section and updated requirements
- Version bumped to 1.4.2

---

## [v1.4.1] — 2026-08-01

### Changed
- Upgraded target framework from `net8.0-windows` to `net10.0-windows`
- LibUsbDotNet updated from 2.2.85 to 3.0.224, migrated to its new context-based API (`UsbContext` + `Find`, control transfer returning transferred bytes)
- Version bumped to 1.4.1

---

## [v1.3.0] — 2026-07-15

### Added
- Native detection of USB dongle connect/disconnect events, replacing polling-based inference
- Last saved battery percent now stored with a timestamp, so a stale cache is ignored on app startup

### Changed
- Charging and discharge confirmation now use elapsed time instead of counting fixed poll cycles
- USB connection stays open across reads instead of reconnecting on every poll
- Tray polling now adapts its interval to the current device state instead of running on two fixed timers
- GitHub Actions updated to v5 for Node 22+ compatibility (Node 20 deprecated Sept 2025)

### Fixed
- Bootstrap charging now correctly handles `percentRaw=100`, preventing 0% display when starting with full battery

---

## [v1.2.0] — 2026-06-19

### Added
- Headset Powered On notification when the headset becomes active
- Headset Powered Off notification when the headset is turned off with dongle connected
- Headset Disconnected notification when the dongle is removed

### Changed
- LibUsbDotNet updated from 2.2.29 to 2.2.85

---

## [v1.1.4] — 2026-05-12

### Changed

* Executable size reduced from ~25 MB to ~336 KB by switching `TargetFramework` from `net8.0-windows10.0.19041.0` to `net8.0-windows`
* CI migrated to `windows-2025-vs2026`
* Assembly metadata cleaned up; git hash suppressed from product version

---

## [v1.1.3] — 2026-05-06

### Fixed

* `BatteryReader`: mV calibration restricted to discharge only — charger voltage no longer inflates `_maxMv`, preventing premature percentage drops after unplugging
* `BatteryReader`: `DefaultMaxMv` corrected to 4128mV (discharge ceiling); saved values above this threshold are discarded on load via `StateStore`
* `BatteryReader`: battery display holds at 100% for `DischargeConfirmTicks` cycles after cable removal before allowing a drop, preventing the immediate 100% → 95% jump caused by charger voltage settling
* `BatteryReader`: `_wasCharging` transition debounced — transient `resp[9] == 0x03` glitches during charging no longer reset confirmation progress
* `StateStore`: saved `MaxMv` values above `DefaultMaxMv` are discarded on load, cleaning up state files written by previous builds

---

## [v1.1.2] — 2026-05-06

### Fixed

* `UsbDevice`: `resp[9] == 0x06` is now exposed as a dedicated `isFullyCharged` out parameter, separate from `isCharging` (`0x05`) — previously both were collapsed into a single flag, forcing `BatteryReader` to use heuristics for full-charge detection
* `BatteryReader`: full-charge is now detected exclusively via `isFullyCharged` from `resp[9]`; `_fullChargeConfirmCounter`, voltage comparison, and `percentRaw == 100` fallback paths removed; `DefaultMaxMv` corrected from 4128 → 4200 mV based on observed readings
* `HeadsetState` / `TrayApp`: tray icon and tooltip now show Fully Charged as soon as `BatteryPercent >= 100` during a Charging state, covering the trickle charge window where firmware holds `resp[9] == 0x05` for several minutes after the cell reaches capacity

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
