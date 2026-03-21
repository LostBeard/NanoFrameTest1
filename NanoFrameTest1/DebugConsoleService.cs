using System;
using System.Diagnostics;
using System.Text;
using nanoFramework.Device.Bluetooth;
using nanoFramework.Device.Bluetooth.GenericAttributeProfile;

namespace NanoFrameTest1
{
    /// <summary>
    /// BLE service that streams debug log output to connected clients and accepts text commands.
    /// Replaces needing a serial monitor — debug output goes straight to the Blazor app.
    /// </summary>
    public class DebugConsoleService
    {
        GattLocalCharacteristic _logOutputChar;
        GattLocalCharacteristic _commandInputChar;
        bool _hasSubscribers;

        public GattServiceProvider ServiceProvider { get; private set; }

        /// <summary>
        /// Fired when a text command is received from the Blazor app.
        /// </summary>
        public event CommandReceivedHandler CommandReceived;
        public delegate void CommandReceivedHandler(string command);

        public bool Initialize()
        {
            var result = GattServiceProvider.Create(BleUuids.DebugServiceUuid);
            if (result.Error != BluetoothError.Success)
            {
                Debug.WriteLine("[DebugConsole] Failed to create service: " + result.Error);
                return false;
            }

            ServiceProvider = result.ServiceProvider;
            var service = ServiceProvider.Service;

            // Log Output — notify only (ESP32 → app)
            var logParams = new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Notify,
                UserDescription = "Debug Log Output"
            };
            var logResult = service.CreateCharacteristic(BleUuids.DebugLogOutputUuid, logParams);
            if (logResult.Error != BluetoothError.Success)
            {
                Debug.WriteLine("[DebugConsole] Failed to create log output characteristic");
                return false;
            }
            _logOutputChar = logResult.Characteristic;
            _logOutputChar.SubscribedClientsChanged += (sender, args) =>
            {
                _hasSubscribers = sender.SubscribedClients.Length > 0;
            };

            // Command Input — write only (app → ESP32)
            var cmdParams = new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse,
                UserDescription = "Command Input"
            };
            var cmdResult = service.CreateCharacteristic(BleUuids.DebugCommandInputUuid, cmdParams);
            if (cmdResult.Error != BluetoothError.Success)
            {
                Debug.WriteLine("[DebugConsole] Failed to create command input characteristic");
                return false;
            }
            _commandInputChar = cmdResult.Characteristic;
            _commandInputChar.WriteRequested += OnCommandWriteRequested;

            Debug.WriteLine("[DebugConsole] Service initialized");
            return true;
        }

        /// <summary>
        /// Send a log message to all subscribed BLE clients.
        /// Call this instead of Debug.WriteLine when you want output to reach the Blazor app.
        /// </summary>
        public void Log(string message)
        {
            Debug.WriteLine(message);
            if (!_hasSubscribers) return;

            // BLE MTU is typically 20-512 bytes. Truncate if needed.
            if (message.Length > 500)
            {
                message = message.Substring(0, 497) + "...";
            }

            var writer = new DataWriter();
            writer.WriteString(message);
            _logOutputChar.NotifyValue(writer.DetachBuffer());
        }

        void OnCommandWriteRequested(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
        {
            var request = args.GetRequest();
            var reader = DataReader.FromBuffer(request.Value);
            var command = reader.ReadString(request.Value.Length);

            if (request.Option == GattWriteOption.WriteWithResponse)
            {
                request.Respond();
            }

            Debug.WriteLine("[DebugConsole] Command received: " + command);
            CommandReceived?.Invoke(command);
        }
    }
}
