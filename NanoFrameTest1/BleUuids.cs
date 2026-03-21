using System;

namespace NanoFrameTest1
{
    /// <summary>
    /// BLE service and characteristic UUIDs.
    /// Custom UUIDs use the base: a0e4f2c0-SSSS-CCCC-8000-00805f9b34fb
    /// where SSSS = service index, CCCC = characteristic index.
    /// </summary>
    public static class BleUuids
    {
        // WiFi Configuration Service
        public static readonly Guid WifiServiceUuid = new("a0e4f2c0-0001-1000-8000-00805f9b34fb");
        public static readonly Guid WifiStatusUuid = new("a0e4f2c0-0001-0001-8000-00805f9b34fb");
        public static readonly Guid WifiScanUuid = new("a0e4f2c0-0001-0002-8000-00805f9b34fb");
        public static readonly Guid WifiCredentialsUuid = new("a0e4f2c0-0001-0003-8000-00805f9b34fb");
        public static readonly Guid WifiCommandUuid = new("a0e4f2c0-0001-0004-8000-00805f9b34fb");

        // Camera Control Service
        public static readonly Guid CameraServiceUuid = new("a0e4f2c0-0002-1000-8000-00805f9b34fb");
        public static readonly Guid CameraStatusUuid = new("a0e4f2c0-0002-0001-8000-00805f9b34fb");
        public static readonly Guid CameraCommandUuid = new("a0e4f2c0-0002-0002-8000-00805f9b34fb");
        public static readonly Guid CameraStreamUrlUuid = new("a0e4f2c0-0002-0003-8000-00805f9b34fb");

        // Debug Console Service
        public static readonly Guid DebugServiceUuid = new("a0e4f2c0-0003-1000-8000-00805f9b34fb");
        public static readonly Guid DebugLogOutputUuid = new("a0e4f2c0-0003-0001-8000-00805f9b34fb");
        public static readonly Guid DebugCommandInputUuid = new("a0e4f2c0-0003-0002-8000-00805f9b34fb");

        // WiFi commands (written to WifiCommandUuid)
        public const byte WifiCmdConnect = 0x01;
        public const byte WifiCmdDisconnect = 0x02;
        public const byte WifiCmdForget = 0x03;

        // Camera commands (written to CameraCommandUuid)
        public const byte CamCmdStartStream = 0x01;
        public const byte CamCmdStopStream = 0x02;
    }
}
