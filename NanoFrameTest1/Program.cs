using System;
using System.Diagnostics;
using System.Threading;
using nanoFramework.Device.Bluetooth;
using nanoFramework.Device.Bluetooth.GenericAttributeProfile;

namespace NanoFrameTest1
{
    public class Program
    {
        public static void Main()
        {
            Debug.WriteLine("[ESP32] Starting BLE (WiFi config + debug on one primary service)...");

            BluetoothLEServer server = BluetoothLEServer.Instance;
            server.DeviceName = "ESP32-S3-WROOM";

            var debug = new DebugConsoleService();
            var wifi = new WifiConfigService(debug);

            if (!wifi.Initialize())
            {
                Debug.WriteLine("[ESP32] BLE initialization failed");
                Thread.Sleep(Timeout.Infinite);
                return;
            }

            wifi.ServiceProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters()
            {
                IsConnectable = true,
                IsDiscoverable = true
            });

            debug.Log("[ESP32] Advertising. Connect with Web Bluetooth to provision WiFi.");

            Thread.Sleep(Timeout.Infinite);
        }
    }
}
