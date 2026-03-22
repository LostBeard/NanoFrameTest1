# Blazor WASM ↔ ESP32 BLE — Connection Analysis

## Status (resolved in tree)

The problems below were fixed in code:

- **`Program.cs`** now initializes `WifiConfigService` (which registers the `a0e4f2c0-0001-*` GATT layout) and calls **`StartAdvertising` once** on that provider.
- **Debug console** characteristics are attached to the **same primary service** as WiFi (`DebugConsoleService.Initialize(GattLocalService)`), matching nanoFramework’s single–`GattServiceProvider` pattern.
- **`BleDeviceService`** uses **`BleUuids`-compatible** strings only, discovers `a0e4f2c0-0001-1000-...`, subscribes to status / scan / debug notify, and only raises **`OnConnected`** after that succeeds (otherwise it disconnects and rethrows).

The sections that follow describe the **previous** failure mode for historical context.

---

## Related reference: BlazorWebBluetoothDemo (PlatformIO)

When this board ran **PlatformIO / Arduino** firmware, a working Web Bluetooth stack lived in:

`D:\users\tj\Projects\BlazorWebBluetoothDemo\BlazorWebBluetoothDemo`

- **Firmware:** `ESP32BLEApp/src/main.cpp` — service `19b10000-e8f2-537e-4f6c-d104768a1214`, characteristics `19b10001` (sensor notify) and `19b10002` (LED write). The sketch **puts that service UUID in the advertisement** (`addServiceUUID`), which helps some centrals and matches `optionalServices` in the Blazor page.
- **Blazor:** `BlazorWebBluetoothDemo/Pages/Home.razor` — same SpawnDev.BlazorJS pattern (`RequestDevice`, `Connect`, `GetPrimaryService`, `StartNotifications`, `ReadValue`), but only **one** notify characteristic; no WiFi service.

NanoFrameTest1 uses a **different** primary service (`a0e4f2c0-0001-1000-…`) and more characteristics; the app and firmware must stay on that contract. Mixing the old Blazor UUIDs with nanoFramework firmware (or the reverse) will fail service discovery.

Another local **Blazor WASM + Web Bluetooth** reference (different product, same SpawnDev.BlazorJS style):

`D:\users\tj\Projects\SpawnDev.MatrixLEDDisplay\SpawnDev.MatrixLEDDisplay` — MI Matrix LED display; GATT service `0000ffd0-…` and framing in `MIMatrixDisplay.cs`, not applicable to the ESP32 GATT layout here.

---

This document originally recorded a static review of the repository explaining why the Blazor WebAssembly app failed to connect or behave correctly with the ESP32 over Bluetooth Low Energy.

## Summary

Communication is intended to use **Web Bluetooth** (not HTTP from the WASM app to the device). The main issues are:

1. **Firmware entry point** only registers a **legacy demo GATT** service; the WiFi GATT implementation in the project is **not started** from `Program.cs`.
2. The **Blazor client mixes UUIDs** from two different designs: it discovers the demo primary service, then requests WiFi characteristics that **do not exist** under that service on the running firmware.
3. The UI can show **“connected”** even when WiFi service discovery failed, because GATT may be up while `_wifiService` was never assigned.

If firmware were changed to expose **only** the `a0e4f2c0-0001-*` WiFi service (as in `BleUuids.cs`), the current Blazor discovery path would **not** find it, because it looks for `19b10000-*` first.

---

## 1. Two BLE designs in the repo

### What the ESP32 actually runs (`Program.cs`)

`NanoFrameTest1/Program.cs` starts a single primary service matching the old PlatformIO / Arduino demo:

| Role            | UUID |
|-----------------|------|
| Primary service | `19b10000-e8f2-537e-4f6c-d104768a1214` |
| “Sensor” char   | `19b10001-e8f2-537e-4f6c-d104768a1214` |
| LED char        | `19b10002-e8f2-537e-4f6c-d104768a1214` |

There is **no** registration of `WifiConfigService`, `DebugConsoleService`, or any use of `BleUuids.WifiServiceUuid` in `Main()`.

### What exists in source but is not wired up

`BleUuids.cs`, `WifiConfigService.cs`, and `DebugConsoleService.cs` define the **documented** WiFi and debug layout, for example:

- WiFi service: `a0e4f2c0-0001-1000-8000-00805f9b34fb`
- WiFi status / scan / credentials / command characteristics under `a0e4f2c0-0001-0001` … `0004`

These types are compiled into the firmware project but are **not invoked** from the current `Program.Main()`, so that GATT surface is **not advertised** by the firmware as checked in.

---

## 2. Blazor client UUID mix (`BleDeviceService.cs`)

The Blazor service uses:

- **Demo** primary service and “status” characteristic: `19b10000-...` and `19b10001-...` — this **matches** what `Program.cs` exposes, so `GetPrimaryService` / initial `GetCharacteristic` / read can succeed against that firmware.
- **WiFi scan, credentials, command** characteristics: `a0e4f2c0-0001-0002`, `0003`, `0004` — these are requested **on the same** `BluetoothRemoteGATTService` handle obtained for `19b10000-...`.

On the device, service `19b10000-...` only contains `19b10001` and `19b10002`. The `a0e4f2c0-0001-0002/3/4` characteristics **are not** children of that primary service. Therefore:

- **WiFi scan, credential send, and WiFi commands** cannot succeed against the current firmware layout (wrong characteristic UUIDs for the service handle in use).

Conversely, if the board were running firmware that **only** exposed the `a0e4f2c0-0001-1000-...` WiFi service (and not `19b10000-...`), the Blazor code’s `GetPrimaryService("19b10000-...")` path would **time out or fail**, so the app would not “connect properly” at the service-discovery stage.

---

## 3. Misleading “connected” state

In `BleDeviceService.ConnectAsync`:

- `OnConnected` is invoked **after** the diagnostic block, **regardless** of whether `GetPrimaryService` for the WiFi UUID succeeded within the timeout.
- If discovery fails, `_wifiService` may remain `null` while `_server.Connected` is still true.
- WiFi-related methods guard with `if (_wifiService == null) return;`, so they can **fail silently** from the user’s perspective while the UI still shows a BLE connection.

There is also a `if (false)` block suggesting follow-on setup was temporarily disabled, which may leave notifications / debug service unused even when discovery succeeds.

---

## 4. Web Bluetooth environment (orthogonal but common)

Failures that occur **before** any GATT service match often come from:

- **Browser**: Web Bluetooth is limited to **Chromium-based** browsers (e.g. Chrome, Edge) with user gesture for `requestDevice`.
- **Secure context**: **HTTPS** or **`http://localhost`** — not arbitrary HTTP hosts or `file://`.

The solution’s `README.md` already calls out Chrome/Edge and secure context; these do not fix the UUID/firmware wiring issues above.

---

## 5. `HttpClient` in `Program.cs` (Blazor)

The WASM host registers `HttpClient` with `BaseAddress` set to the **Blazor app’s origin**, not the ESP32. That is normal for loading the SPA and API relative to the site; it is **not** the BLE transport. Camera/stream over WiFi would be separate and subject to CORS, mixed content, and device IP — not analyzed in depth here.

---

## Recommended alignment (conceptual)

Pick **one** GATT design end-to-end:

1. **Demo-only**: Firmware keeps `19b10000` + `19b10001` / `19b10002`; Blazor only uses those characteristics (no `a0e4f2c0-0001-0002/3/4` on that service).
2. **WiFi product**: Firmware `Main()` must create/advertise the services in `WifiConfigService` / `DebugConsoleService` using `BleUuids`; Blazor must use **the same** primary service UUID and **only** characteristic UUIDs that exist on **that** service (including `RequestDevice` `optionalServices`).

Until firmware and Blazor agree on **one** service/characteristic matrix and `OnConnected` reflects successful binding to the services the UI needs, the app will continue to appear broken or only partially working.

---

*Generated from repository static analysis; paths are relative to the solution folder `NanoFrameTest1`.*
