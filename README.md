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

NariMeter answers that question with a glanceable tray icon, ~50 MB of RAM (the unavoidable cost of the .NET 8 runtime), and no network activity whatsoever.

---

## Features

- Battery percentage displayed directly in the Windows system tray
- Color-coded icons reflecting charge level at a glance
- Dedicated charging icon when the USB cable is connected
- Headphone icon when the headset is powered off or disconnected
- Hover tooltip with current status
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
| 🎧 Headphone | Headset powered off or dongle disconnected |

---

## Requirements

- Windows 10 or later (x64)
- Razer Nari wireless headset with USB dongle connected
- WinUSB driver installed on Interface 5 of the dongle (see setup below)
- No .NET installation required — the runtime is bundled inside the executable (self-contained)

> **For developers building from source:** [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) is required.

---

## Setup

### 1. Install the WinUSB driver

NariMeter communicates with the Nari dongle at the USB protocol level, bypassing Razer's driver stack entirely. For this to work, the WinUSB generic driver must be bound to **Interface 5** of the dongle.

1. Download and run [Zadig](https://zadig.akeo.ie/)
2. In the menu, select **Options → List All Devices**
3. Locate `Razer Nari` in the dropdown — select the entry corresponding to **Interface 5**
4. Set the target driver to **WinUSB**
5. Click **Replace Driver**

> This does not affect the headset's audio functionality. Only the HID interface used for battery reporting is replaced. Razer Synapse will lose the ability to communicate with the dongle on this interface, which is entirely the point.

### 2. Run NariMeter

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
| `[12:13]` | uint16 big-endian | Battery voltage in millivolts |

The headset is considered **powered on** when `response[1]` is not `0x01` or `response[2]` is not `0x00`. When powered off or sleeping, the dongle continues to respond to the control transfer but reports the idle state bytes.

### Step 4 — Voltage to percentage conversion

The battery voltage range of the Nari was determined empirically through observation over multiple charge cycles:

```
Minimum voltage (0%):   3296 mV
Maximum voltage (100%): 4128 mV
Charging threshold:     4160 mV  (cable connected)
```

The percentage is derived as:

```csharp
int percent = Math.Clamp(
    (int)Math.Round((double)(mv - MinMv) / (MaxMv - MinMv) * 100 / 5) * 5,
    0, 100);
```

Values are rounded to the nearest 5% to match the headset's internal reporting granularity and avoid spurious updates from minor voltage fluctuations.

### Step 5 — State machine and debouncing

Raw USB readings are noisy, particularly at startup when the host controller and device are still negotiating. A naive implementation that reacts to every reading will flicker between states.

NariMeter implements independent debounce thresholds in `UsbDevice.cs`:

- **Powered on** is only confirmed after **4 consecutive active readings** (4 × 2s = 8 seconds)
- **Powered off** is confirmed after **4 consecutive idle readings**
- This prevents transient USB responses at boot from triggering a false active state

Battery level is polled separately from connection state — every **30 seconds** when active, which is sufficient given the slow rate of battery discharge and eliminates unnecessary USB traffic.

### Step 6 — Architecture overview

```
Program.cs
└── TrayApp.cs          — ApplicationContext, timer orchestration, tray icon management
    ├── BatteryReader.cs — Voltage-to-percentage logic, stabilization, state persistence
    ├── UsbDevice.cs     — USB HID control transfers, static buffers, debounce state machine
    ├── HeadsetState.cs  — Immutable state record, ChargeStatus enum, tooltip formatting
    ├── StateStore.cs    — JSON persistence of last known percentage across sessions
    └── StartupManager.cs — Windows registry autostart toggle (HKCU\...\Run)
```

All tray icons are embedded directly in the executable as `EmbeddedResource` — no external files required at runtime.

---

## Building from Source

**Requires:** .NET 8 SDK

```bash
git clone https://github.com/your-username/NariMeter.git
cd NariMeter
dotnet build
```

**Publishing a self-contained portable executable:**

```bash
dotnet publish -c Release
```

Output: `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\NariMeter.exe`

The published executable is fully self-contained — it includes the .NET 8 runtime and requires no framework installation on the target machine.

---

## Performance

| Metric | Value |
|---|---|
| RAM usage (steady state) | ~50 MB |
| CPU usage | < 0.1% |
| Network activity | None |
| Disk writes | Only on battery % change (state persistence) |
| Poll interval — state | 2 seconds |
| Poll interval — battery | 30 seconds |

The RAM footprint reflects the cost of the self-contained .NET 8 runtime. The application logic itself is minimal — two timers, five icons loaded once at startup, and a static USB read buffer.

---

## Project Structure

```
NariMeter/
├── BatteryReader.cs       — Battery voltage conversion and state logic
├── HeadsetState.cs        — State record and charge status definitions
├── Program.cs             — Entry point
├── StartupManager.cs      — Run at startup via Windows registry
├── StateStore.cs          — Last known battery % persistence (JSON)
├── TrayApp.cs             — Tray icon, timers, menu
├── UsbDevice.cs           — USB HID communication layer
├── App.ico                — Application icon (task manager, Explorer)
├── Headphone.ico          — Tray: powered off / disconnected state
├── BatteryGreen.ico       — Tray: battery > 50%
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
