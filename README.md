# NariMeter

> *Because a 600 MB software suite with background telemetry, auto-updating services, and a RGB lighting SDK shouldn't be the only way to know if your headset is dying.*

A lightweight Windows system tray application that displays the **real-time battery level** of the Razer Nari wireless headset — built entirely through USB protocol reverse engineering, with zero dependency on Razer software.

![Windows 10+](https://img.shields.io/badge/Windows-10%2B-blue?logo=windows)
![.NET 8](https://img.shields.io/badge/.NET-8.0-purple?logo=dotnet)
![License: MIT](https://img.shields.io/badge/License-MIT-green)
![Portable](https://img.shields.io/badge/portable-yes-brightgreen)

---

## The Problem with Razer Synapse

The Razer Nari is a capable wireless headset. Its companion software, Razer Synapse, is not.

Synapse installs a constellation of background services — `RazerNahimic`, `RazerGameScannerService`, `RazerCentralService`, among others — that collectively consume anywhere from 300 to 600 MB of RAM at idle, register themselves for autostart without asking, and maintain persistent network connections to Razer's telemetry infrastructure. This means your headset usage patterns, audio settings, and hardware identifiers are being transmitted to external servers as a condition of knowing your battery percentage.

The interface itself buries the battery indicator under multiple clicks inside a large, slow-loading overlay. For a single piece of information — *how much battery does my headset have?* — the friction is remarkable.

NariMeter answers that question with a glanceable tray icon, ~25 MB on disk, and no network activity whatsoever.

---

## Features

- Battery percentage displayed directly in the Windows system tray
- Color-coded icons reflecting charge level at a glance
- Dedicated charging icon when the USB cable is connected
- Fully charged indicator when the cable is connected and battery is at 100%
- Headphone icon when the headset is powered off or disconnected
- Hover tooltip with current status
- **Low battery notifications** — configurable warn and critical thresholds
- **Fully charged notification** — alerts when the headset reaches 100%
- **Run at Startup** toggle in the right-click menu (via Windows registry, no installer required)
- Fully portable — single `.exe`, no installation, no registry pollution beyond the optional startup entry

---

## Tray Icon Reference

| Icon | State |
|---|---|
| 🟢 Green battery | Connected and discharging — battery > 50% |
| 🟡 Yellow battery | Connected and discharging — battery > 20% |
| 🔴 Red battery | Connected and discharging — battery ≤ 20% |
| ⚡ Charging battery | USB cable connected, actively charging |
| 🟢 Green battery | USB cable connected, 100% fully charged |
| 🎧 Headphone | Headset powered off or dongle disconnected |

---

## Requirements

- Windows 10 or later (x64)
- [.NET 8 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime) — required, free, one-click install
- Razer Nari wireless headset with USB dongle connected
- WinUSB driver installed on Interface 5 of the dongle (see setup below)

> **For developers building from source:** [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) is required instead of the runtime.

---

## Setup

### 1. Install the .NET 8 Runtime

Download and run the installer from [dotnet.microsoft.com](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime). Many users will already have this installed.

### 2. Install the WinUSB driver

NariMeter communicates with the Nari dongle at the USB protocol level, bypassing Razer's driver stack entirely. For this to work, the WinUSB generic driver must be bound to **Interface 5** of the dongle.

1. Download and run [Zadig](https://zadig.akeo.ie/)
2. In the menu, select **Options → List All Devices**
3. Locate `Razer Nari` in the dropdown — select the entry corresponding to **Interface 5**
4. Set the target driver to **WinUSB**
5. Click **Replace Driver**

> This does not affect the headset's audio functionality. Only the HID interface used for battery reporting is replaced. Razer Synapse will lose the ability to communicate with the dongle on this interface, which is entirely the point.

### 3. Run NariMeter

Download `NariMeter.exe` from the [Releases](../../releases) page and run it. No installation required. The tray icon will appear within a few seconds of the dongle being recognized.

---

## How It Works — Reverse Engineering the Protocol

NariMeter was built without access to any Razer documentation. The battery reporting protocol was discovered entirely through USB traffic analysis.

### Step 1 — Identifying the device

The Razer Nari dongle presents itself to Windows as a USB composite device with multiple interfaces. Using USBPcap and Wireshark, all HID traffic between Razer Synapse and the dongle was captured during normal operation.

The device identifiers are:

```
Vendor ID:  0x1532  (Razer Inc.)
Product ID: 0x051C  (Nari dongle)
Interface:  5
```

Interface 5 is the HID interface responsible for device status reporting, separate from the audio and standard HID interfaces used for button input.

### Step 2 — Capturing the handshake

Synapse communicates with Interface 5 via USB HID control transfers — `SET_REPORT` followed by `GET_REPORT` — a standard HID pattern for querying device state.

The captured `SET_REPORT` payload that triggers a battery response is:

```
FF 0A 00 FD 04 12 F1 02 05 00 00 ... (64 bytes total)
```

This is sent as a Class request to the interface:

```
bmRequestType: 0x21  (Host → Device, Class, Interface)
bRequest:      0x09  (SET_REPORT)
wValue:        0x03FF
wIndex:        5     (Interface number)
wLength:       64
```

### Step 3 — Reading the response

A subsequent `GET_REPORT` request retrieves the 64-byte response from the device:

```
bmRequestType: 0xA1  (Device → Host, Class, Interface)
bRequest:      0x01  (GET_REPORT)
wValue:        0x03FF
wIndex:        5
wLength:       64
```

Within the response buffer, the relevant fields are:

| Byte offset | Value | Meaning |
|---|---|---|
| `[1]` | `0x01` | Device idle / headset not active |
| `[2]` | `0x00` | Confirms idle state |
| `[9]` | `0x05` | USB cable connected (charging) |
| `[9]` | `0x03` | USB cable disconnected (discharging) |
| `[12:13]` | uint16 big-endian | Battery voltage in millivolts — primary source for percentage calculation |
| `[14]` | uint8 | Battery percentage reported by device firmware — used as sanity check only |

### Step 4 — Battery percentage calculation

The firmware value at `response[14]` is **not used as the primary battery percentage**. Empirical testing revealed that the Nari firmware updates this value in large discrete jumps — for example, holding at 80% for several hours before dropping directly to 50% on a change of only ~8 mV. This is a firmware calibration limitation, not a reflection of actual charge level.

NariMeter instead derives the battery percentage from the raw millivolt reading at `response[12:13]`, using a linear interpolation between the minimum and maximum observed voltages for the device:

```
percent = (mv - minMv) / (maxMv - minMv) × 100
```

The voltage bounds (`minMv`, `maxMv`) are **calibrated adaptively per device** — they start with conservative defaults and are refined automatically as the app observes readings near the low and high extremes during normal use. Calibrated values are persisted across sessions in `NariMeter.state.json`.

The firmware value at `response[14]` is retained as a **sanity check**: if the voltage-derived percentage and the firmware-reported percentage diverge by more than 40 percentage points, the reading is considered anomalous and the last known good value is preserved instead.

Battery percentage is displayed in **steps of 5%** and transitions smoothly — the displayed value moves at most 5% per poll cycle regardless of how large the underlying change is, preventing sudden jumps in the tray icon.

### Step 5 — Charge state detection

The device reports charge state natively via `response[9]`:

```csharp
isCharging = response[9] == 0x05;
```

During charging, the millivolt reading rises continuously as the cell charges, providing a reliable and gradual progression toward 100%. The firmware value at `response[14]` is ignored for percentage display during charging for the same reason as discharging — the firmware inflates the value immediately upon cable connection.

### Step 6 — State machine and debouncing

Raw USB readings are noisy, particularly at startup when the host controller and device are still negotiating. NariMeter implements independent debounce thresholds in `UsbDevice.cs`:

- **Powered on** is only confirmed after **4 consecutive active readings** (4 × 2s = 8 seconds)
- **Powered off** is confirmed after **4 consecutive idle readings**

Battery level is polled every **30 seconds** when active, which is sufficient given the slow rate of battery discharge.

### Step 7 — Architecture overview

```
Program.cs
└── TrayApp.cs           — ApplicationContext, timer orchestration, tray icon management
    ├── BatteryReader.cs — Charge state logic, stabilization, state persistence
    ├── UsbDevice.cs     — USB HID control transfers, static buffers, debounce state machine
    ├── HeadsetState.cs  — Immutable state record, ChargeStatus enum, tooltip formatting
    ├── StateStore.cs    — JSON persistence of last known percentage and calibrated mV bounds
    └── StartupManager.cs — Windows registry autostart toggle (HKCU\...\Run)
```

---

## Notifications

NariMeter supports optional Windows balloon notifications, toggled via the right-click menu under **Show Notifications**:

- **Low Battery Warning** — triggered at a configurable threshold (default: 20%)
- **Battery Critical** — triggered at a configurable threshold (default: 10%)
- **Fully Charged** — triggered when the headset reaches 100% while on cable

Warn and critical thresholds are configurable independently via the right-click menu and persist across sessions.

---

## Building from Source

**Requires:** .NET 8 SDK

```bash
git clone https://github.com/indina853/NariMeter.git
cd NariMeter
dotnet build
```

**Publishing a portable executable:**

```bash
dotnet publish -c Release
```

Output: `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\NariMeter.exe`

> The published executable requires the [.NET 8 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime) on the target machine.

---

## Performance

| Metric | Value |
|---|---|
| Executable size | ~25 MB |
| RAM usage (steady state) | ~15 MB |
| CPU usage | < 0.1% |
| Network activity | None |
| Disk writes | Only on battery % change and settings updates |
| Poll interval — state | 2 seconds |
| Poll interval — battery | 30 seconds |

---

## Project Structure

```
NariMeter/
├── BatteryReader.cs       — Battery charge state logic
├── HeadsetState.cs        — State record and charge status definitions
├── Program.cs             — Entry point
├── StartupManager.cs      — Run at startup via Windows registry
├── StateStore.cs          — Battery % and settings persistence (JSON)
├── TrayApp.cs             — Tray icon, timers, notifications, menu
├── UsbDevice.cs           — USB HID communication layer
├── App.ico                — Application icon (task manager, Explorer)
├── Headphone.ico          — Tray: powered off / disconnected state
├── BatteryGreen.ico       — Tray: battery > 50% or fully charged
├── BatteryYellow.ico      — Tray: battery > 20%
├── BatteryRed.ico         — Tray: battery ≤ 20%
├── BatteryCharging.ico    — Tray: charging
├── NariMeter.csproj
└── RunTimeConfig.template.json
```

---

## Contributing

Pull requests are welcome. If you own a different Razer wireless headset and want to investigate whether the same protocol applies, the starting point is capturing USB traffic with [USBPcap](https://desowin.org/usbpcap/) + [Wireshark](https://www.wireshark.org/) while Synapse queries your device, then comparing the `SET_REPORT` payload and response byte layout against the Nari protocol documented above.

---

## License

MIT — see [LICENSE](LICENSE)
