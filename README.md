# NanoFrameTest1

ESP32-S3-WROOM camera board firmware (.NET nanoFramework) + Blazor WebAssembly companion app for BLE-based device configuration and camera access.

## Overview

This solution connects a **Freenove ESP32-S3-WROOM** camera dev board to a **Blazor WASM** progressive web app using **Bluetooth Low Energy (BLE)**. The ESP32 runs .NET nanoFramework firmware (C#), and the web app uses [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS) for Web Bluetooth API interop.

**Why nanoFramework?** Write ESP32 firmware in C# instead of C++/Arduino. Same language on both sides of the BLE connection.

## Architecture

```
┌─────────────────────────┐         BLE           ┌──────────────────────────┐
│   ESP32-S3-WROOM        │◄─────────────────────►│   Blazor WASM App        │
│   (nanoFramework)       │                       │   (Browser / PWA)        │
│                         │                       │                          │
│  • BLE GATT Server      │                       │  • Web Bluetooth Client  │
│  • WiFi Management      │                       │  • Device Configuration  │
│  • Camera Control       │                       │  • Camera Viewer (WiFi)  │
│  • Device Info Service  │                       │  • SpawnDev.BlazorJS     │
│  • Debug Log Stream     │                       │  • PWA (installable)     │
└─────────────────────────┘                       └──────────────────────────┘
          │                                                   │
          │  WiFi (after BLE config)                          │
          ▼                                                   │
  ┌────────────────┐                                          │
  │  Camera Stream │◄──────────── HTTP/MJPEG ─────────────────┘
  │  (HTTP Server) │
  └────────────────┘
```

### Communication Flow

1. **BLE Discovery** — Blazor app scans for and connects to the ESP32 via Web Bluetooth
2. **WiFi Configuration** — User enters WiFi credentials in the web app, sent to ESP32 over BLE
3. **ESP32 Connects to WiFi** — Device joins the local network, reports its IP back over BLE
4. **Camera Access** — Web app accesses the camera stream over WiFi (HTTP) for full-bandwidth video

BLE is used for configuration and control (low bandwidth is fine). The camera stream goes over WiFi for performance.

## Features

### Phase 1 — BLE Foundation
- [x] Project scaffolding (nanoFramework + Blazor WASM solution)
- [x] SpawnDev.BlazorJS 3.5.0 integrated
- [ ] ESP32 BLE GATT server with Device Info service
- [ ] Blazor BLE connection UI (scan, connect, disconnect)
- [ ] Bidirectional BLE communication verified end-to-end

### Phase 2 — WiFi Provisioning
- [ ] ESP32 WiFi network scanning (available SSIDs sent over BLE)
- [ ] Credential transfer over BLE (SSID + password)
- [ ] ESP32 connects to WiFi, reports IP address back over BLE
- [ ] Connection status monitoring (connected, signal strength, IP)
- [ ] Stored credentials — reconnect on boot without re-provisioning

### Phase 3 — Camera Access
- [ ] ESP32 HTTP server serving MJPEG stream (or JPEG snapshots) over WiFi
- [ ] Blazor app camera viewer — live preview from the ESP32 camera
- [ ] Camera controls over BLE (resolution, frame rate, flip/mirror)
- [ ] Stream URL auto-discovery (ESP32 reports its stream URL over BLE)

### Phase 4 — Device Dashboard
- [ ] GPIO explorer — read/write pins from the Blazor app over BLE
- [ ] Sensor readings (ADC, temperature) streamed via BLE notify
- [ ] LED/flash control
- [ ] Device diagnostics (free memory, uptime, firmware version)

### Phase 5 — Debug Console
- [ ] BLE notify characteristic streaming `Debug.WriteLine` output from ESP32
- [ ] Real-time log viewer in Blazor app (filterable, scrollable)
- [ ] Command input — send text commands to ESP32 over BLE (serial monitor style)
- [ ] Log levels with color coding

### Phase 6 — PWA & Polish
- [ ] Progressive Web App manifest + service worker (installable)
- [ ] BLE device pairing memory — auto-reconnect on app launch
- [ ] OTA firmware update trigger (ESP32 pulls update over WiFi, triggered via BLE)
- [ ] Responsive mobile-first UI (phone is the primary control device)
- [ ] Dark/light theme

## BLE Service Design

### Device Information Service (Standard)
**UUID:** `0000180a-0000-1000-8000-00805f9b34fb`

| Characteristic | UUID | Properties | Description |
|----------------|------|------------|-------------|
| Manufacturer Name | `00002a29-...` | Read | "SpawnDev" |
| Model Number | `00002a24-...` | Read | "ESP32-S3-WROOM" |
| Firmware Revision | `00002a26-...` | Read | nanoFramework firmware version |
| Software Revision | `00002a28-...` | Read | App firmware version |

### WiFi Configuration Service (Custom)
**UUID:** `a0e4f2c0-0001-1000-8000-00805f9b34fb`

| Characteristic | UUID | Properties | Description |
|----------------|------|------------|-------------|
| WiFi Status | `a0e4f2c0-0001-0001-...` | Read, Notify | Connection state + IP address |
| WiFi Scan | `a0e4f2c0-0001-0002-...` | Read, Write, Notify | Write to trigger scan, notify with results |
| WiFi Credentials | `a0e4f2c0-0001-0003-...` | Write | SSID + password (JSON or length-prefixed) |
| WiFi Command | `a0e4f2c0-0001-0004-...` | Write | Connect, disconnect, forget commands |

### Camera Control Service (Custom)
**UUID:** `a0e4f2c0-0002-1000-8000-00805f9b34fb`

| Characteristic | UUID | Properties | Description |
|----------------|------|------------|-------------|
| Camera Status | `a0e4f2c0-0002-0001-...` | Read, Notify | Streaming state, resolution, URL |
| Camera Command | `a0e4f2c0-0002-0002-...` | Write | Start/stop stream, set resolution |
| Stream URL | `a0e4f2c0-0002-0003-...` | Read, Notify | HTTP URL for MJPEG/JPEG access |

### Debug Console Service (Custom)
**UUID:** `a0e4f2c0-0003-1000-8000-00805f9b34fb`

| Characteristic | UUID | Properties | Description |
|----------------|------|------------|-------------|
| Log Output | `a0e4f2c0-0003-0001-...` | Notify | Debug log stream (ESP32 → app) |
| Command Input | `a0e4f2c0-0003-0002-...` | Write | Text commands (app → ESP32) |

## Projects

### NanoFrameTest1 (ESP32 Firmware)
- **Type:** .NET nanoFramework v1.0
- **Target:** Freenove ESP32-S3-WROOM (dual USB-C)
- **nanoFramework firmware target:** `ESP32_S3_BLE` (WiFi + Bluetooth)
- **Key Dependencies:**
  - nanoFramework.CoreLibrary — core runtime
  - nanoFramework.Device.Bluetooth — BLE GATT server
  - nanoFramework.System.Device.Wifi — WiFi management
  - nanoFramework.Runtime.Events — event infrastructure

### BlazorWasmESP32S3WROOM (Web App)
- **Type:** Blazor WebAssembly (.NET 10.0)
- **Key Dependencies:**
  - [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS) 3.5.0 — JS interop for Web Bluetooth API and all browser APIs
  - Microsoft.AspNetCore.Components.WebAssembly

## Hardware

### Freenove ESP32-S3-WROOM Camera Board
- **SoC:** ESP32-S3 (dual-core Xtensa LX7, 240 MHz, WiFi + BLE 5.0)
- **Camera:** OV2640
- **Memory:** 8MB PSRAM, 16MB Flash
- **Connectivity:** WiFi 802.11 b/g/n + Bluetooth 5.0 LE
- **USB:** Dual USB-C ports (one for UART/programming, one for USB-OTG)
- **GPIO:** Multiple exposed pins for sensors, LEDs, peripherals
- **Resources:**
  - [Freenove ESP32-S3-WROOM documentation](https://github.com/Freenove/Freenove_ESP32_S3_WROOM_Board)
  - [nanoFramework ESP32-S3 target](https://docs.nanoframework.net/content/reference-targets/esp32-s3.html)

## Development Setup

### Prerequisites
- **Visual Studio 2022+** with [.NET nanoFramework extension](https://marketplace.visualstudio.com/items?itemName=nanoframework.nanoFramework-VS2022-Extension)
- **.NET 10 SDK** for the Blazor WASM project
- **nanoFramework firmware** flashed to the ESP32-S3-WROOM
- **Chrome/Edge** (Web Bluetooth requires Chromium-based browser)

### Flashing nanoFramework Firmware
```bash
# Install nanoff (nanoFramework Firmware Flasher)
dotnet tool install -g nanoff

# Flash ESP32-S3 with BLE support (required for WiFi + Bluetooth)
nanoff --target ESP32_S3_BLE --serialport COMx --update
```

### Building
```bash
# Blazor WASM app
dotnet build BlazorWasmESP32S3WROOM/BlazorWasmESP32S3WROOM.csproj

# nanoFramework project — build via Visual Studio (requires nanoFramework extension)
```

### Playwright tests (Blazor)

The UI under test is **Blazor WebAssembly**. WASM must be **served** over HTTP(S) with correct `_framework/` assets (not opened as raw files from disk).

**How this repo hosts it:** `NanoFrameTest1.Tests` / `BlazorAppFixture` runs **`dotnet publish -c Release`** on `BlazorWasmESP32S3WROOM`, then starts an in-process **Kestrel** HTTPS static host (dev PFX `assets/testcert.pfx`, password `unittests`) at **`https://localhost:5210/`**. That matches the idea used in **PlaywrightMultiTest** (publish + static server) and avoids stale `dotnet run` dev-server fingerprints on the same port.

**Test-only endpoints** on the same origin (local Playwright only):

- **HTTP** under `/__wasmtest/*` — logging, sandboxed filesystem under `%TEMP%\NanoFrameTest1\wasm-sandbox\…` (see `WasmTestHostMiddleware`).
- **SignalR** at `/__wasmtest/signalr` — bidirectional hub `WasmDebugHub`: the Blazor app uses `WasmTestDebugHubClient` (connects when the hub exists; no-op when deployed elsewhere). From tests, use `TestBase.WasmDebugHub` (`IHubContext<WasmDebugHub>`) to `SendAsync("ClientInvoke", method, json)` or `SendAsync("ClientNotify", text)` into the browser.

If **Visual Studio** (or another `testhost`) still has the test DLL loaded, `dotnet test` can fail to copy `NanoFrameTest1.Tests.dll` — stop the other test run, or rely on `.runsettings` (**single test worker**) in the test project to reduce contention.

```bash
dotnet test NanoFrameTest1.Tests/NanoFrameTest1.Tests.csproj --filter "Category=Smoke"
```

If `testhost` locks `bin\Debug\…\NanoFrameTest1.Tests.dll`, use a separate output root (from repo `NanoFrameTest1/`):

```bash
dotnet test NanoFrameTest1.Tests/NanoFrameTest1.Tests.csproj -c Release --filter "Category=Smoke" --artifacts-path .artifacts-smoke
```

`BlazorAppFixture` finds `BlazorWasmESP32S3WROOM` by walking up from the test assembly directory, so `--artifacts-path` still resolves the Blazor project correctly.

Smoke tests include **SignalR** checks (`WasmDebugHub_ClientConnects_*`, round-trip via `SmokeTestRoundTrip`); the first run pays a **Release publish** of the Blazor project.

**Web Bluetooth (Freenove ESP32-S3-WROOM):** Playwright cannot choose a device for you. With the board powered and this firmware deployed, run **headed** Chrome and pick the ESP32 when the list opens:

```powershell
dotnet test NanoFrameTest1.Tests/NanoFrameTest1.Tests.csproj --filter "Category=BLEManual"
```

Optional checks without opening the chooser: `--filter "Category=BLE"`.

## Reference Projects

### BlazorWebBluetoothDemo (same board, PlatformIO — your working reference)

Local clone (Web Bluetooth client + ESP32 firmware):

`D:\users\tj\Projects\BlazorWebBluetoothDemo\BlazorWebBluetoothDemo`

| Piece | PlatformIO / Arduino (`ESP32BLEApp/src/main.cpp`) | This repo (nanoFramework) |
|-------|-----------------------------------------------------|---------------------------|
| Blazor UI | `BlazorWebBluetoothDemo/Pages/Home.razor` | `BlazorWasmESP32S3WROOM/Pages/Home.razor` |
| Connect flow | `RequestDevice` → `device.gatt.connect()` → `getPrimaryService` → one notify + read | Same Web Bluetooth pattern in `Services/BleDeviceService.cs` |
| Primary service UUID | `19b10000-e8f2-537e-4f6c-d104768a1214` | `a0e4f2c0-0001-1000-8000-00805f9b34fb` (`BleUuids.WifiServiceUuid`) |
| Other characteristics | Sensor notify `19b10001-…`, LED write `19b10002-…` | WiFi + debug characteristics under `BleUuids` (see `BleUuids.cs`) |
| Advertising | Arduino adds `SERVICE_UUID` to the BLE advertisement (`pAdvertising->addServiceUUID`) | nanoFramework `GattServiceProvider.StartAdvertising` (stack-dependent) |
| Device name | `BLEDevice::init("ESP32")` | `BluetoothLEServer.Instance.DeviceName = "ESP32-S3-WROOM"` |

The GitHub repo is the same content as the local demo: **[BlazorWebBluetoothDemo](https://github.com/LostBeard/BlazorWebBluetoothDemo)**. That project is the known-good Blazor ↔ ESP32 BLE pairing when the board runs **PlatformIO** firmware; **NanoFrameTest1** must use the **nanoFramework** UUIDs and deployed managed app, not the `19b10000` service, unless you reflash the old sketch.

### SpawnDev.MatrixLEDDisplay (Web Bluetooth + Blazor — different hardware)

Local repo (library + demo app for a consumer LED matrix over BLE):

`D:\users\tj\Projects\SpawnDev.MatrixLEDDisplay\SpawnDev.MatrixLEDDisplay`

- **What it is:** Blazor WebAssembly client for the Merkury “MI Matrix Display” (16×16 LED panel), using **SpawnDev.BlazorJS** and the same general Web Bluetooth flow (`RequestDevice`, GATT `Connect`, `GetPrimaryService`, characteristics). See `SpawnDev.MatrixLEDDisplay/MIMatrixDisplay.cs` and `SpawnDev.MatrixLEDDisplay.Demo/Pages/Home.razor`.
- **Not ESP32:** Primary service UUID there is `0000ffd0-0000-1000-8000-00805f9b34fb` (vendor matrix protocol), unrelated to `BleUuids` in this firmware project. Useful mainly as another **working Blazor + Web Bluetooth** reference in the SpawnDev stack.
- **Online:** [SpawnDev.MatrixLEDDisplay on GitHub](https://github.com/LostBeard/SpawnDev.MatrixLEDDisplay) · [hosted demo](https://lostbeard.github.io/SpawnDev.MatrixLEDDisplay/).

### SpawnDev.ILGPU — PlaywrightMultiTest (multi-target Playwright runner)

Local console test harness that drives **both** native desktop test exes and **Blazor WebAssembly** under Playwright + NUnit:

`D:\users\tj\Projects\SpawnDev.ILGPU\SpawnDev.ILGPU\PlaywrightMultiTest`

- **Idea:** `ProjectDiscovery` finds projects marked with `<PlaywrightMultiTest>` in their `.csproj`; **console** targets run as published subprocesses per test; **Blazor WASM** targets are published, served over HTTPS, and driven in Chromium (e.g. WebGPU flags). Tests surface through NUnit `TestCaseSource` so `dotnet test --filter …` works uniformly.
- **Contrast with this repo:** `NanoFrameTest1.Tests` uses a single shared `BlazorAppFixture` + headed/headless Chrome for smoke vs BLE — simpler scope. PlaywrightMultiTest is the reference if you later want **discovered multi-project** browser runs, TRX logging, or the same pattern for other SpawnDev WASM libraries.

See `PlaywrightMultiTest/CLAUDE.md` in that repo for run commands and constraints.

- **[SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS)** — Full JS interop for Blazor WASM, including Web Bluetooth API wrappers.
- **[nanoFramework Bluetooth Samples](https://github.com/nanoframework/Samples/tree/main/samples/Bluetooth)** — Official nanoFramework BLE examples.

## Technology Stack

| Layer | Technology | Language |
|-------|-----------|----------|
| ESP32 Firmware | .NET nanoFramework | C# |
| BLE GATT Server | nanoFramework.Device.Bluetooth | C# |
| WiFi Management | nanoFramework.System.Device.Wifi | C# |
| Web App | Blazor WebAssembly (.NET 10) | C# |
| Browser APIs | SpawnDev.BlazorJS | C# → JS interop |
| Web Bluetooth | SpawnDev.BlazorJS JSObjects | C# |

**C# everywhere.** That's the point.

## License

Private project by TJ (Todd Tanner / @LostBeard).
