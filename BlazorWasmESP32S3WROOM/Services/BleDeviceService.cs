using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

namespace BlazorWasmESP32S3WROOM.Services
{
    /// <summary>
    /// Web Bluetooth client for nanoFramework firmware: WiFi provisioning + debug console
    /// on one primary GATT service (<see cref="WifiServiceUuid"/>), matching <c>BleUuids</c> on the device.
    /// </summary>
    public class BleDeviceService : IAsyncDisposable
    {
        readonly BlazorJSRuntime _js;

        BluetoothDevice? _device;
        BluetoothRemoteGATTServer? _server;
        BluetoothRemoteGATTService? _primaryService;

        BluetoothRemoteGATTCharacteristic? _wifiStatusChar;
        BluetoothRemoteGATTCharacteristic? _wifiScanChar;
        BluetoothRemoteGATTCharacteristic? _debugLogChar;

        bool _scanNotificationsStarted;

        TextDecoder? _textDecoder;

        const string WifiServiceUuid = "a0e4f2c0-0001-1000-8000-00805f9b34fb";
        const string WifiStatusUuid = "a0e4f2c0-0001-0001-8000-00805f9b34fb";
        const string WifiScanUuid = "a0e4f2c0-0001-0002-8000-00805f9b34fb";
        const string WifiCredentialsUuid = "a0e4f2c0-0001-0003-8000-00805f9b34fb";
        const string WifiCommandUuid = "a0e4f2c0-0001-0004-8000-00805f9b34fb";

        const string DebugLogOutputUuid = "a0e4f2c0-0003-0001-8000-00805f9b34fb";
        const string DebugCommandInputUuid = "a0e4f2c0-0003-0002-8000-00805f9b34fb";

        const byte WifiCmdConnect = 0x01;
        const byte WifiCmdDisconnect = 0x02;
        const byte WifiCmdForget = 0x03;

        public bool IsConnected => _server?.Connected == true;
        public bool IsGattReady => _primaryService != null && _wifiStatusChar != null;
        public string? DeviceName => _device?.Name;
        public bool IsWebBluetoothSupported { get; private set; }

        public event Action? OnConnected;
        public event Action? OnDisconnected;
        /// <summary>Fired during <see cref="ConnectAsync"/> so the UI can show what is happening (GATT can take tens of seconds).</summary>
        public event Action<string>? OnConnectionProgress;
        public event Action<WifiStatus>? OnWifiStatusChanged;
        public event Action<List<WifiNetwork>>? OnWifiScanResults;
        public event Action<string>? OnDebugLog;

        public BleDeviceService(BlazorJSRuntime js)
        {
            _js = js;
            _textDecoder = new TextDecoder();
            using var navigator = _js.Get<Navigator>("navigator");
            using var bluetooth = navigator.Bluetooth;
            IsWebBluetoothSupported = bluetooth != null;
        }

        public async Task ConnectAsync()
        {
            if (IsConnected) await DisconnectAsync();

            using var navigator = _js.Get<Navigator>("navigator");
            using var bluetooth = navigator.Bluetooth!;

            Report("Opening device picker — choose ESP32-S3-WROOM (firmware must be running).");
            _device = await bluetooth.RequestDevice(new BluetoothDeviceOptions
            {
                AcceptAllDevices = true,
                OptionalServices = new[] { WifiServiceUuid }
            });

            _device.OnGATTServerDisconnected += OnGATTDisconnected;
            Console.WriteLine($"[BLE] Device selected: {_device.Name ?? "(no name)"}");
            Report($"Device selected: {_device.Name ?? "(no name)"}. Establishing GATT…");

            try
            {
                await EstablishGattSessionAsync();
                Console.WriteLine($"[BLE] Connection complete. Server connected: {_server!.Connected}");
                OnConnected?.Invoke();
            }
            catch
            {
                await DisconnectAsync();
                throw;
            }
        }

        /// <summary>
        /// Reconnect GATT, discover service/characteristics, enable status + debug notify only.
        /// WiFi scan notify is deferred until <see cref="ScanWifiNetworks"/> (fewer simultaneous CCCD writes — helps flaky ESP32 stacks).
        /// </summary>
        async Task EstablishGattSessionAsync()
        {
            const int serviceTimeoutMs = 28000;
            Exception? last = null;

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var gatt = _device!.GATT!;
                    Report($"GATT connect (attempt {attempt + 1}/3)…");
                    _server = await gatt.Connect();
                    Console.WriteLine($"[BLE] GATT connected: {_server.Connected} (attempt {attempt + 1})");

                    if (!_server.Connected)
                        throw new InvalidOperationException("GATT server reports disconnected immediately after connect.");

                    await Task.Delay(attempt == 0 ? 500 : 700);

                    if (!_server.Connected)
                    {
                        Console.WriteLine("[BLE] Link dropped during settle delay; reconnecting…");
                        Report("Link dropped during settle — retrying…");
                        continue;
                    }

                    Report("Discovering WiFi + debug service (a0e4f2c0-0001-…)…");
                    _primaryService = await WithTimeout(
                        _server.GetPrimaryService(WifiServiceUuid),
                        serviceTimeoutMs,
                        "GetPrimaryService(WiFi)");

                    await Task.Delay(120);
                    Report("Reading WiFi status characteristic…");
                    _wifiStatusChar = await WithTimeout(
                        _primaryService.GetCharacteristic(WifiStatusUuid),
                        15000,
                        "GetCharacteristic(WiFi status)");
                    await Task.Delay(80);
                    Report("Reading WiFi scan characteristic…");
                    _wifiScanChar = await WithTimeout(
                        _primaryService.GetCharacteristic(WifiScanUuid),
                        15000,
                        "GetCharacteristic(WiFi scan)");
                    await Task.Delay(80);
                    Report("Reading debug log characteristic…");
                    _debugLogChar = await WithTimeout(
                        _primaryService.GetCharacteristic(DebugLogOutputUuid),
                        15000,
                        "GetCharacteristic(Debug log)");

                    _wifiStatusChar.OnCharacteristicValueChanged += OnWifiStatusNotification;
                    _debugLogChar.OnCharacteristicValueChanged += OnDebugLogNotification;

                    Report("Subscribing to WiFi status + debug notifications…");
                    await _wifiStatusChar.StartNotifications();
                    await Task.Delay(150);
                    await _debugLogChar.StartNotifications();
                    await Task.Delay(100);

                    if (!_server.Connected)
                        throw new InvalidOperationException("GATT disconnected after starting notifications.");

                    Report("Reading initial WiFi status…");
                    using var initial = await WithTimeout(_wifiStatusChar.ReadValue(), 15000, "ReadValue(WiFi status)");
                    ParseWifiStatusFromBuffer(initial.Buffer);

                    _scanNotificationsStarted = false;
                    Report("BLE session ready — WiFi and debug console are available.");
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Console.WriteLine($"[BLE] Session attempt {attempt + 1} failed: {ex.Message}");
                    Report($"Attempt {attempt + 1} failed: {ex.Message}");
                    await DisposePartialGattStateAsync();
                    if (_server?.Connected == true)
                    {
                        try { _server.Disconnect(); } catch { /* best effort */ }
                    }
                    if (attempt < 2)
                        await Task.Delay(800);
                }
            }

            throw last ?? new InvalidOperationException("GATT session failed.");
        }

        async Task DisposePartialGattStateAsync()
        {
            if (_debugLogChar != null)
            {
                _debugLogChar.OnCharacteristicValueChanged -= OnDebugLogNotification;
                if (_server?.Connected == true)
                    try { await _debugLogChar.StopNotifications(); } catch { }
                _debugLogChar.Dispose();
                _debugLogChar = null;
            }
            if (_wifiStatusChar != null)
            {
                _wifiStatusChar.OnCharacteristicValueChanged -= OnWifiStatusNotification;
                if (_server?.Connected == true)
                    try { await _wifiStatusChar.StopNotifications(); } catch { }
                _wifiStatusChar.Dispose();
                _wifiStatusChar = null;
            }
            if (_wifiScanChar != null)
            {
                if (_scanNotificationsStarted)
                    _wifiScanChar.OnCharacteristicValueChanged -= OnWifiScanNotification;
                if (_scanNotificationsStarted && _server?.Connected == true)
                    try { await _wifiScanChar.StopNotifications(); } catch { }
                _wifiScanChar.Dispose();
                _wifiScanChar = null;
            }
            _scanNotificationsStarted = false;
            if (_primaryService != null)
            {
                _primaryService.Dispose();
                _primaryService = null;
            }
        }

        static async Task<T> WithTimeout<T>(Task<T> task, int milliseconds, string operation)
        {
            var delay = Task.Delay(milliseconds);
            var completed = await Task.WhenAny(task, delay);
            if (completed != task)
                throw new TimeoutException($"{operation} timed out after {milliseconds} ms (GATT may have dropped).");
            return await task;
        }

        public async Task DisconnectAsync()
        {
            if (_debugLogChar != null)
            {
                _debugLogChar.OnCharacteristicValueChanged -= OnDebugLogNotification;
                if (_server?.Connected == true)
                    try { await _debugLogChar.StopNotifications(); } catch { }
                _debugLogChar.Dispose();
                _debugLogChar = null;
            }
            if (_wifiScanChar != null)
            {
                if (_scanNotificationsStarted)
                    _wifiScanChar.OnCharacteristicValueChanged -= OnWifiScanNotification;
                if (_scanNotificationsStarted && _server?.Connected == true)
                    try { await _wifiScanChar.StopNotifications(); } catch { }
                _wifiScanChar.Dispose();
                _wifiScanChar = null;
            }
            _scanNotificationsStarted = false;
            if (_wifiStatusChar != null)
            {
                _wifiStatusChar.OnCharacteristicValueChanged -= OnWifiStatusNotification;
                if (_server?.Connected == true)
                    try { await _wifiStatusChar.StopNotifications(); } catch { }
                _wifiStatusChar.Dispose();
                _wifiStatusChar = null;
            }
            if (_primaryService != null)
            {
                _primaryService.Dispose();
                _primaryService = null;
            }
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

        public async Task ScanWifiNetworks()
        {
            if (_wifiScanChar == null || _server?.Connected != true) return;

            if (!_scanNotificationsStarted)
            {
                _wifiScanChar.OnCharacteristicValueChanged += OnWifiScanNotification;
                await _wifiScanChar.StartNotifications();
                _scanNotificationsStarted = true;
                await Task.Delay(100);
            }

            await _wifiScanChar.WriteValueWithoutResponse(new byte[] { 0x01 });
        }

        public async Task SendWifiCredentials(string ssid, string password)
        {
            if (_primaryService == null) return;
            using var credChar = await _primaryService.GetCharacteristic(WifiCredentialsUuid);
            var payload = Encoding.UTF8.GetBytes($"{ssid}\n{password}");
            await credChar.WriteValueWithResponse(payload);
        }

        public async Task SendWifiCommand(byte command)
        {
            if (_primaryService == null) return;
            using var cmdChar = await _primaryService.GetCharacteristic(WifiCommandUuid);
            await cmdChar.WriteValueWithoutResponse(new byte[] { command });
        }

        public Task ConnectWifi() => SendWifiCommand(WifiCmdConnect);
        public Task DisconnectWifi() => SendWifiCommand(WifiCmdDisconnect);
        public Task ForgetWifi() => SendWifiCommand(WifiCmdForget);

        public async Task SendDebugCommand(string command)
        {
            if (_primaryService == null) return;
            using var cmdChar = await _primaryService.GetCharacteristic(DebugCommandInputUuid);
            var payload = Encoding.UTF8.GetBytes(command);
            await cmdChar.WriteValueWithResponse(payload);
        }

        void Report(string message)
        {
            Console.WriteLine($"[BLE] {message}");
            OnConnectionProgress?.Invoke(message);
        }

        void OnGATTDisconnected(Event e) => OnDisconnected?.Invoke();

        void OnWifiStatusNotification(Event e)
        {
            using var characteristic = e.TargetAs<BluetoothRemoteGATTCharacteristic>();
            using var value = characteristic.Value;
            if (value != null) ParseWifiStatusFromBuffer(value.Buffer);
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
                        networks.Add(new WifiNetwork { Ssid = parts[0], Rssi = rssi });
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

        void ParseWifiStatusFromBuffer(ArrayBuffer? buffer)
        {
            if (buffer == null) return;
            using var dataView = new DataView(buffer);
            if (dataView.ByteLength == 0) return;

            var statusByte = dataView.GetUint8(0);
            var ipAddress = "";
            if (dataView.ByteLength > 1)
            {
                using var ipView = new DataView(buffer, 1);
                ipAddress = _textDecoder!.Decode(ipView);
            }

            OnWifiStatusChanged?.Invoke(new WifiStatus
            {
                State = (WifiConnectionState)statusByte,
                IpAddress = ipAddress
            });
        }

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
