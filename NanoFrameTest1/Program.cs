using System;
using System.Diagnostics;
using System.Threading;
using nanoFramework.Device.Bluetooth;
using nanoFramework.Device.Bluetooth.GenericAttributeProfile;

namespace NanoFrameTest1
{
    public class Program
    {
        const string DeviceName = "ESP32-S3-WROOM";

        static DebugConsoleService _debugService;
        static DeviceInfoService _deviceInfoService;
        static WifiConfigService _wifiService;

        public static void Main()
        {
            Debug.WriteLine($"[NanoFrameTest1] Starting BLE server as '{DeviceName}'...");

            // Initialize BLE server
            var server = BluetoothLEServer.Instance;
            server.DeviceName = DeviceName;

            // Debug Console first — other services use it for logging
            _debugService = new DebugConsoleService();
            if (!_debugService.Initialize())
            {
                Debug.WriteLine("[NanoFrameTest1] Failed to initialize Debug Console service");
                Thread.Sleep(Timeout.Infinite);
                return;
            }

            // Handle debug commands
            _debugService.CommandReceived += OnDebugCommand;

            // Device Information Service
            _deviceInfoService = new DeviceInfoService();
            if (!_deviceInfoService.Initialize())
            {
                _debugService.Log("[NanoFrameTest1] Failed to initialize Device Info service");
                Thread.Sleep(Timeout.Infinite);
                return;
            }

            // WiFi Configuration Service
            _wifiService = new WifiConfigService(_debugService);
            if (!_wifiService.Initialize())
            {
                _debugService.Log("[NanoFrameTest1] Failed to initialize WiFi Config service");
                Thread.Sleep(Timeout.Infinite);
                return;
            }

            // Start advertising all services
            var advParams = new GattServiceProviderAdvertisingParameters
            {
                IsDiscoverable = true,
                IsConnectable = true
            };

            _debugService.ServiceProvider.StartAdvertising(advParams);
            _deviceInfoService.ServiceProvider.StartAdvertising(advParams);
            _wifiService.ServiceProvider.StartAdvertising(advParams);

            _debugService.Log("[NanoFrameTest1] BLE server started. Advertising as: " + DeviceName);
            _debugService.Log("[NanoFrameTest1] Services: DeviceInfo, WiFiConfig, DebugConsole");

            // Keep alive
            Thread.Sleep(Timeout.Infinite);
        }

        static void OnDebugCommand(string command)
        {
            switch (command.ToLower())
            {
                case "status":
                    _debugService.Log("[NanoFrameTest1] Device is running. BLE active.");
                    break;

                case "heap":
                    _debugService.Log("[NanoFrameTest1] Free memory: " + nanoFramework.Runtime.Native.GC.Run(false) + " bytes");
                    break;

                default:
                    _debugService.Log("[NanoFrameTest1] Unknown command: " + command);
                    break;
            }
        }
    }
}
