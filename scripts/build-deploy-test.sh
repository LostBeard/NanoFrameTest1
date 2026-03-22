#!/bin/bash
# Build, deploy to ESP32, and run tests.
# Usage: ./scripts/build-deploy-test.sh [test-filter]
# Examples:
#   ./scripts/build-deploy-test.sh                    # smoke tests only
#   ./scripts/build-deploy-test.sh "Category=BLE"     # BLE tests
#   ./scripts/build-deploy-test.sh "Name~Manual"      # manual BLE test

set -e
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$SCRIPT_DIR")"
MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe"
ESP32_BIN_DIR="$ROOT/NanoFrameTest1/bin/Debug"
FILTER="${1:-Category=Smoke}"

echo "=== Building ESP32 firmware ==="
"$MSBUILD" "$ROOT/NanoFrameTest1/NanoFrameTest1.nfproj" -nologo -v:q 2>&1 | tail -3

echo "=== Building Blazor WASM app ==="
cd "$ROOT/BlazorWasmESP32S3WROOM"
dotnet build 2>&1 | tail -5

echo "=== Deploying to ESP32 (COM9) ==="
# Combine PE files into deployment image
cat "$ESP32_BIN_DIR"/mscorlib.pe \
    "$ESP32_BIN_DIR"/nanoFramework.Runtime.Events.pe \
    "$ESP32_BIN_DIR"/nanoFramework.Runtime.Native.pe \
    "$ESP32_BIN_DIR"/nanoFramework.System.Collections.pe \
    "$ESP32_BIN_DIR"/nanoFramework.System.Text.pe \
    "$ESP32_BIN_DIR"/nanoFramework.Device.Bluetooth.pe \
    "$ESP32_BIN_DIR"/System.Threading.pe \
    "$ESP32_BIN_DIR"/System.IO.Streams.pe \
    "$ESP32_BIN_DIR"/System.Net.pe \
    "$ESP32_BIN_DIR"/System.Device.Wifi.pe \
    "$ESP32_BIN_DIR"/NanoFrameTest1.pe > "$ESP32_BIN_DIR/deploy.bin"

nanoff --nanodevice --serialport COM9 --deploy --image "$ESP32_BIN_DIR/deploy.bin" 2>&1 | grep -E "(OK|Error)"
echo "Waiting for device to boot..."
sleep 3

echo "=== Building tests ==="
cd "$ROOT/NanoFrameTest1.Tests"
dotnet build 2>&1 | tail -5

echo "=== Running tests (filter: $FILTER) ==="
dotnet test --no-build --filter "$FILTER" -v n 2>&1

echo "=== Done ==="
