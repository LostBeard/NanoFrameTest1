# NanoFrameTest1

ESP32-S3-WROOM camera board firmware (.NET nanoFramework) + Blazor WebAssembly companion app for BLE-based device configuration and camera access.

## Overview

This solution connects a **Freenove ESP32-S3-WROOM** camera dev board to a **Blazor WASM** progressive web app using **Bluetooth Low Energy (BLE)**. The ESP32 runs .NET nanoFramework firmware (C#), and the web app uses [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS) for Web Bluetooth API interop.

**Why nanoFramework?** Write ESP32 firmware in C# instead of C++/Arduino. Same language on both sides of the BLE connection.

## Architecture

```
┌─────────────────────────┐         BLE          ┌──────────────────────────┐
│   ESP32-S3-WROOM        │◄─────────────────────►│   Blazor WASM App        │
│   (nanoFramework)       │                       │   (Browser / PWA)        │
│                         │                       │                          │
│  • BLE GATT Server      │                       │  • Web Bluetooth Client  │
│  • WiFi Management      │                       │  • Device Configuration  │
│  • Camera Control       │                       │  • Camera Viewer (WiFi)  │
│  • Device Info Service  │                       │  • SpawnDev.BlazorJS     │
└─────────────────────────┘                       └──────────────────────────┘
                │
                │  WiFi (after BLE config)
                ▼
        ┌───────────────┐
        │  Camera Stream │
        │  (HTTP/MJPEG)  │
        └───────────────┘
```

### Communication Flow

1. **BLE Discovery** — Blazor app scans for and connects to the ESP32 via Web Bluetooth
2. **WiFi Configuration** — User enters WiFi credentials in the web app, sent to ESP32 over BLE
3. **ESP32 Connects to WiFi** — Device joins the local network, reports its IP back over BLE
4. **Camera Access** — Web app accesses the camera stream over WiFi (HTTP) for full-bandwidth video

BLE is used for configuration and control (low bandwidth is fine). The camera stream goes over WiFi for performance.

## Projects

### NanoFrameTest1 (ESP32 Firmware)
- **Type:** .NET nanoFramework v1.0
- **Target:** Freenove ESP32-S3-WROOM (dual USB-C)
- **Dependencies:** nanoFramework.CoreLibrary

### BlazorWasmESP32S3WROOM (Web App)
- **Type:** Blazor WebAssembly (.NET 10.0)
- **Key Dependencies:**
  - SpawnDev.BlazorJS — JS interop for Web Bluetooth API and all browser APIs
  - Microsoft.AspNetCore.Components.WebAssembly

## Hardware

### Freenove ESP32-S3-WROOM Camera Board
- **SoC:** ESP32-S3 (dual-core Xtensa LX7, WiFi + BLE 5.0)
- **Camera:** OV2640 (or compatible)
- **Connectivity:** WiFi 802.11 b/g/n + Bluetooth 5.0 LE
- **USB:** Dual USB-C ports (one for UART/programming, one for USB-OTG)
- **Resources:**
  - [Freenove ESP32-S3-WROOM documentation](https://github.com/Freenove/Freenove_ESP32_S3_WROOM_Board)
  - [nanoFramework ESP32-S3 target](https://docs.nanoframework.net/content/reference-targets/esp32-s3.html)

## Planned BLE Services

| Service | UUID | Purpose |
|---------|------|---------|
| Device Info | `0000180a-0000-1000-8000-00805f9b34fb` | Standard Device Information Service (manufacturer, model, firmware version) |
| WiFi Config | TBD (custom) | SSID scan, credential storage, connection status, IP address reporting |
| Camera Control | TBD (custom) | Resolution, frame rate, stream start/stop, status |

## Development Setup

### Prerequisites
- **Visual Studio 2022+** with .NET nanoFramework extension
- **.NET 10 SDK** for the Blazor WASM project
- **nanoFramework firmware** flashed to the ESP32-S3-WROOM ([nanoff tool](https://github.com/nanoframework/nanoFirmwareFlasher))
- **Chrome/Edge** (Web Bluetooth requires Chromium-based browser)

### Building
```bash
# Blazor WASM app
dotnet build BlazorWasmESP32S3WROOM/BlazorWasmESP32S3WROOM.csproj

# nanoFramework project — build via Visual Studio (MSBuild with nanoFramework targets)
```

### Flashing the ESP32
```bash
# Install nanoff
dotnet tool install -g nanoff

# Flash nanoFramework firmware to ESP32-S3
nanoff --target ESP32_S3 --serialport COMx --update
```

## Reference Projects
- **[BlazorWebBluetoothDemo](https://github.com/LostBeard/BlazorWebBluetoothDemo)** — Previous BLE demo connecting Blazor WASM to ESP32 using SpawnDev.BlazorJS Web Bluetooth. Used PlatformIO/Arduino firmware. This project ports the ESP32 side to nanoFramework.

## Status

**Early development.** Initial project scaffolding. Next steps:
- [ ] Add nanoFramework BLE packages to ESP32 project
- [ ] Implement BLE GATT server with Device Info service
- [ ] Add WiFi configuration BLE service
- [ ] Build Blazor WASM BLE connection UI (based on BlazorWebBluetoothDemo patterns)
- [ ] Add SpawnDev.BlazorJS package to Blazor project
- [ ] Implement WiFi credential transfer over BLE
- [ ] Add camera stream access over WiFi
- [ ] Add BlazorWasmESP32S3WROOM to the solution file

## License

Private project by TJ (Todd Tanner / @LostBeard).
