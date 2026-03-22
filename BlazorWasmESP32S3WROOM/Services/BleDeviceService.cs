using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

namespace BlazorWasmESP32S3WROOM.Services
{
    /// <summary>
    /// Manages the BLE connection to the ESP32-S3-WROOM device.
    /// Uses SpawnDev.BlazorJS Web Bluetooth API wrappers.
    /// </summary>
    public class BleDeviceService : IAsyncDisposable
    {
        readonly BlazorJSRuntime _js;

        // BLE objects — kept alive for the connection lifetime
        BluetoothDevice? _device;
        BluetoothRemoteGATTServer? _server;

        // Services
        BluetoothRemoteGATTService? _wifiService;
        BluetoothRemoteGATTService? _debugService;

        // Characteristics
        BluetoothRemoteGATTCharacteristic? _wifiStatusChar;
        BluetoothRemoteGATTCharacteristic? _wifiScanChar;
        BluetoothRemoteGATTCharacteristic? _debugLogChar;

        TextDecoder? _textDecoder;

        // UUIDs — TEMPORARY: using Sample1 UUIDs for testing
        const string WifiServiceUuid = "a7eedf2c-da87-4cb5-a9c5-5151c78b0057";
        const string WifiStatusUuid = "a7eedf2c-da89-4cb5-a9c5-5151c78b0057";
        const string WifiScanUuid = "a0e4f2c0-0001-0002-8000-00805f9b34fb";
        const string WifiCredentialsUuid = "a0e4f2c0-0001-0003-8000-00805f9b34fb";
        const string WifiCommandUuid = "a0e4f2c0-0001-0004-8000-00805f9b34fb";

        const string DebugServiceUuid = "a0e4f2c0-0003-1000-8000-00805f9b34fb";
        const string DebugLogOutputUuid = "a0e4f2c0-0003-0001-8000-00805f9b34fb";
        const string DebugCommandInputUuid = "a0e4f2c0-0003-0002-8000-00805f9b34fb";

        // WiFi commands
        const byte WifiCmdConnect = 0x01;
        const byte WifiCmdDisconnect = 0x02;
        const byte WifiCmdForget = 0x03;

        public bool IsConnected => _server?.Connected == true;
        public string? DeviceName => _device?.Name;
        public bool IsWebBluetoothSupported { get; private set; }

        // Events
        public event Action? OnConnected;
        public event Action? OnDisconnected;
        public event Action<WifiStatus>? OnWifiStatusChanged;
        public event Action<List<WifiNetwork>>? OnWifiScanResults;
        public event Action<string>? OnDebugLog;

        public BleDeviceService(BlazorJSRuntime js)
        {
            _js = js;
            _textDecoder = new TextDecoder();
            CheckBluetoothSupport();
        }

        void CheckBluetoothSupport()
        {
            using var navigator = _js.Get<Navigator>("navigator");
            using var bluetooth = navigator.Bluetooth;
            IsWebBluetoothSupported = bluetooth != null;
        }

        public async Task ConnectAsync()
        {
            if (IsConnected) await DisconnectAsync();

            using var navigator = _js.Get<Navigator>("navigator");
            using var bluetooth = navigator.Bluetooth!;

            _device = await bluetooth.RequestDevice(new BluetoothDeviceOptions
            {
                AcceptAllDevices = true,
                OptionalServices = new string[] { WifiServiceUuid, DebugServiceUuid }
            });

            _device.OnGATTServerDisconnected += OnGATTDisconnected;
            Console.WriteLine($"[BLE] Device selected: {_device.Name ?? "(no name)"}, connecting GATT...");
            _server = await _device.GATT!.Connect();
            Console.WriteLine($"[BLE] GATT connected: {_server.Connected}");

            // === MINIMAL TEST: just try to get the WiFi service and read one characteristic ===
            try
            {
                Console.WriteLine($"[BLE] Getting service: {WifiServiceUuid}");
                _wifiService = await _server.GetPrimaryService(WifiServiceUuid);
                Console.WriteLine("[BLE] Service found! Getting test characteristic...");
                _wifiStatusChar = await _wifiService.GetCharacteristic(WifiStatusUuid);
                Console.WriteLine("[BLE] Characteristic found! Reading value...");
                using var testValue = await _wifiStatusChar.ReadValue();
                var testText = _textDecoder!.Decode(testValue.Buffer);
                Console.WriteLine($"[BLE] Read value: '{testText}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BLE] Service test FAILED: {ex.Message}");
                Console.WriteLine($"[BLE] Server still connected: {_server.Connected}");
            }

            Console.WriteLine($"[BLE] Connection complete. Server connected: {_server.Connected}");

            if (false) // temporarily disabled
            {
            }

            OnConnected?.Invoke();
        }

        public async Task DisconnectAsync()
        {
            // Unsubscribe from notifications
            if (_debugLogChar != null)
            {
                _debugLogChar.OnCharacteristicValueChanged -= OnDebugLogNotification;
                if (_server?.Connected == true) try { await _debugLogChar.StopNotifications(); } catch { }
                _debugLogChar.Dispose();
                _debugLogChar = null;
            }
            if (_wifiScanChar != null)
            {
                _wifiScanChar.OnCharacteristicValueChanged -= OnWifiScanNotification;
                if (_server?.Connected == true) try { await _wifiScanChar.StopNotifications(); } catch { }
                _wifiScanChar.Dispose();
                _wifiScanChar = null;
            }
            if (_wifiStatusChar != null)
            {
                _wifiStatusChar.OnCharacteristicValueChanged -= OnWifiStatusNotification;
                if (_server?.Connected == true) try { await _wifiStatusChar.StopNotifications(); } catch { }
                _wifiStatusChar.Dispose();
                _wifiStatusChar = null;
            }
            if (_debugService != null) { _debugService.Dispose(); _debugService = null; }
            if (_wifiService != null) { _wifiService.Dispose(); _wifiService = null; }
            if (_server != null)
            {
                if (_server.Connected) _server.Disconnect();
                _server.Dispose();
                _server = null;
            }
            if (_device != null)
            {
                _device.OnGATTServerDisconnected -= OnGATTDisconnected;
                _device.Dispose();
                _device = null;
            }
        }

        #region WiFi Operations

        public async Task ScanWifiNetworks()
        {
            if (_wifiService == null) return;
            using var scanChar = await _wifiService.GetCharacteristic(WifiScanUuid);
            await scanChar.WriteValueWithoutResponse(new byte[] { 0x01 });
        }

        public async Task SendWifiCredentials(string ssid, string password)
        {
            if (_wifiService == null) return;
            using var credChar = await _wifiService.GetCharacteristic(WifiCredentialsUuid);
            var payload = Encoding.UTF8.GetBytes($"{ssid}\n{password}");
            await credChar.WriteValueWithResponse(payload);
        }

        public async Task SendWifiCommand(byte command)
        {
            if (_wifiService == null) return;
            using var cmdChar = await _wifiService.GetCharacteristic(WifiCommandUuid);
            await cmdChar.WriteValueWithoutResponse(new byte[] { command });
        }

        public Task ConnectWifi() => SendWifiCommand(WifiCmdConnect);
        public Task DisconnectWifi() => SendWifiCommand(WifiCmdDisconnect);
        public Task ForgetWifi() => SendWifiCommand(WifiCmdForget);

        #endregion

        #region Debug Console

        public async Task SendDebugCommand(string command)
        {
            if (_debugService == null) return;
            using var cmdChar = await _debugService.GetCharacteristic(DebugCommandInputUuid);
            var payload = Encoding.UTF8.GetBytes(command);
            await cmdChar.WriteValueWithResponse(payload);
        }

        #endregion

        #region Event Handlers

        void OnGATTDisconnected(Event e)
        {
            OnDisconnected?.Invoke();
        }

        void OnWifiStatusNotification(Event e)
        {
            using var characteristic = e.TargetAs<BluetoothRemoteGATTCharacteristic>();
            using var value = characteristic.Value;
            if (value != null) ParseWifiStatus(value);
        }

        void OnWifiScanNotification(Event e)
        {
            using var characteristic = e.TargetAs<BluetoothRemoteGATTCharacteristic>();
            using var value = characteristic.Value;
            if (value == null) return;

            var text = _textDecoder!.Decode(value.Buffer);
            var networks = new List<WifiNetwork>();

            if (!string.IsNullOrEmpty(text))
            {
                foreach (var line in text.Split('\n'))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out var rssi))
                    {
                        networks.Add(new WifiNetwork { Ssid = parts[0], Rssi = rssi });
                    }
                }
            }

            OnWifiScanResults?.Invoke(networks);
        }

        void OnDebugLogNotification(Event e)
        {
            using var characteristic = e.TargetAs<BluetoothRemoteGATTCharacteristic>();
            using var value = characteristic.Value;
            if (value == null) return;

            var message = _textDecoder!.Decode(value.Buffer);
            OnDebugLog?.Invoke(message);
        }

        void ParseWifiStatus(DataView dataView)
        {
            // Format: [status_byte][ip_string]
            if (dataView.ByteLength == 0) return;

            var statusByte = dataView.GetUint8(0);
            var ipAddress = "";
            if (dataView.ByteLength > 1)
            {
                // Decode the IP string portion (bytes 1..N)
                using var buffer = dataView.Buffer;
                using var ipView = new DataView(buffer, 1);
                ipAddress = _textDecoder!.Decode(ipView);
            }

            var status = new WifiStatus
            {
                State = (WifiConnectionState)statusByte,
                IpAddress = ipAddress
            };

            OnWifiStatusChanged?.Invoke(status);
        }

        #endregion

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
            _textDecoder?.Dispose();
        }
    }

    public enum WifiConnectionState : byte
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Failed = 3
    }

    public class WifiStatus
    {
        public WifiConnectionState State { get; set; }
        public string IpAddress { get; set; } = "";
    }

    public class WifiNetwork
    {
        public string Ssid { get; set; } = "";
        public int Rssi { get; set; }

        /// <summary>Signal strength as 0-4 bars.</summary>
        public int SignalBars => Rssi switch
        {
            >= -50 => 4,
            >= -60 => 3,
            >= -70 => 2,
            >= -80 => 1,
            _ => 0
        };
    }
}
