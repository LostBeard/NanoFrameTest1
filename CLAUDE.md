# NanoFrameTest1 — Project Instructions

## Project Structure

Two-project solution:
- **NanoFrameTest1/** — .NET nanoFramework firmware for Freenove ESP32-S3-WROOM camera dev board
- **BlazorWasmESP32S3WROOM/** — Blazor WASM companion app (.NET 10) for BLE device control

## Agent Assignment

**Agent #3 (Lt Commander Warf)** is the primary agent for this project. Agents #1 (Riker) and #2 (Data) are working on SpawnDev.ILGPU — do not interfere with their work.

## Key Reference

**BlazorWebBluetoothDemo** (`D:\users\tj\Projects\BlazorWebBluetoothDemo`) is the primary reference project. It demonstrates:
- SpawnDev.BlazorJS Web Bluetooth API usage (connection flow, GATT services, characteristics, notifications)
- ESP32 BLE GATT server implementation (PlatformIO/Arduino — we're porting this to nanoFramework C#)
- Proper event handling, resource disposal, and DI patterns

## SpawnDev.BlazorJS Bluetooth API

All Web Bluetooth classes live in `SpawnDev.BlazorJS.JSObjects` namespace:
- `Bluetooth` — navigator.bluetooth access, `RequestDevice()`
- `BluetoothDevice` — device reference, GATT property, disconnect events
- `BluetoothRemoteGATTServer` — `Connect()`, `GetPrimaryService()`
- `BluetoothRemoteGATTService` — `GetCharacteristic()`
- `BluetoothRemoteGATTCharacteristic` — read/write/notify, `OnCharacteristicValueChanged`
- `BluetoothRemoteGATTDescriptor` — descriptor access
- `BluetoothDeviceOptions` / `BluetoothDeviceFilter` — request filtering

Source: `D:\users\tj\Projects\SpawnDev.BlazorJS\SpawnDev.BlazorJS\SpawnDev.BlazorJS\JSObjects\Bluetooth\`

## nanoFramework BLE

nanoFramework BLE support for ESP32 uses the `nanoFramework.Device.Bluetooth` NuGet package. Key classes:
- `BluetoothLEServer` — GATT server setup
- `GattServiceProvider` — service creation
- `GattLocalCharacteristic` — characteristic definition with read/write/notify
- `BluetoothLEAdvertisementPublisher` — advertising

## Rules

- **SpawnDev.BlazorJS for ALL JS interop** — no raw JavaScript, no IJSRuntime
- **Fix libraries, don't work around** — if SpawnDev.BlazorJS is missing a Bluetooth feature, fix it there
- **DI first** — inject `BlazorJSRuntime` via constructor/`[Inject]`, never use static accessor in DI-available contexts
- **Event properties** — use `OnGATTServerDisconnected +=` not `AddEventListener`
- **Performance** — no unnecessary .NET/JS roundtrips; keep data on the side that needs it
- **Both sides in tandem** — ESP32 GATT services define the contract, Blazor app consumes them. Design services carefully.
