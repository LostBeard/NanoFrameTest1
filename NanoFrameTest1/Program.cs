using System;
using System.Diagnostics;
using System.Threading;
using nanoFramework.Device.Bluetooth;
using nanoFramework.Device.Bluetooth.GenericAttributeProfile;

namespace NanoFrameTest1
{
    /// <summary>
    /// Exact copy of nanoFramework BluetoothLESample1 with minimal changes.
    /// Testing if the official sample works on our ESP32-S3-WROOM.
    /// </summary>
    public class Program
    {
        static GattLocalCharacteristic _readCharacteristic;
        static GattLocalCharacteristic _readWriteCharacteristic;

        static byte _redValue = 128;
        static byte _greenValue = 128;
        static byte _blueValue = 128;

        public static void Main()
        {
            Debug.WriteLine("Hello from NanoFrameTest1 (Sample1 clone)");

            BluetoothLEServer server = BluetoothLEServer.Instance;
            server.DeviceName = "ESP32-S3-WROOM";

            Guid serviceUuid = new Guid("A7EEDF2C-DA87-4CB5-A9C5-5151C78B0057");
            Guid readCharUuid = new Guid("A7EEDF2C-DA88-4CB5-A9C5-5151C78B0057");
            Guid readStaticCharUuid = new Guid("A7EEDF2C-DA89-4CB5-A9C5-5151C78B0057");
            Guid readWriteCharUuid = new Guid("A7EEDF2C-DA8A-4CB5-A9C5-5151C78B0057");

            GattServiceProviderResult result = GattServiceProvider.Create(serviceUuid);
            if (result.Error != BluetoothError.Success)
            {
                Debug.WriteLine("Failed to create service: " + result.Error);
                return;
            }

            GattServiceProvider serviceProvider = result.ServiceProvider;
            GattLocalService service = serviceProvider.Service;

            // Static read characteristic
            DataWriter sw = new DataWriter();
            sw.WriteString("This is NanoFrameTest1");

            GattLocalCharacteristicResult characteristicResult = service.CreateCharacteristic(readStaticCharUuid,
                new GattLocalCharacteristicParameters()
                {
                    CharacteristicProperties = GattCharacteristicProperties.Read,
                    UserDescription = "My Static Characteristic",
                    StaticValue = sw.DetachBuffer()
                });

            if (characteristicResult.Error != BluetoothError.Success)
            {
                Debug.WriteLine("Failed to create static char: " + characteristicResult.Error);
                return;
            }

            // Dynamic read + notify characteristic
            characteristicResult = service.CreateCharacteristic(readCharUuid,
                new GattLocalCharacteristicParameters()
                {
                    CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify,
                    UserDescription = "My Read Characteristic"
                });

            if (characteristicResult.Error != BluetoothError.Success)
            {
                Debug.WriteLine("Failed to create read char: " + characteristicResult.Error);
                return;
            }

            _readCharacteristic = characteristicResult.Characteristic;
            _readCharacteristic.ReadRequested += ReadCharacteristic_ReadRequested;

            Timer notifyTimer = new Timer(NotifyCallBack, null, 10000, 10000);

            // Read/write characteristic
            characteristicResult = service.CreateCharacteristic(readWriteCharUuid,
                new GattLocalCharacteristicParameters()
                {
                    CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Write,
                    UserDescription = "My Read/Write Characteristic"
                });

            if (characteristicResult.Error != BluetoothError.Success)
            {
                Debug.WriteLine("Failed to create rw char: " + characteristicResult.Error);
                return;
            }

            _readWriteCharacteristic = characteristicResult.Characteristic;
            _readWriteCharacteristic.WriteRequested += _readWriteCharacteristic_WriteRequested;
            _readWriteCharacteristic.ReadRequested += _readWriteCharacteristic_ReadRequested;

            // Start advertising
            serviceProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters()
            {
                IsConnectable = true,
                IsDiscoverable = true
            });

            Debug.WriteLine("BLE advertising started. Service: " + serviceUuid);

            Thread.Sleep(Timeout.Infinite);
        }

        private static void NotifyCallBack(object state)
        {
            if (_readCharacteristic.SubscribedClients.Length > 0)
            {
                _readCharacteristic.NotifyValue(GetTimeBuffer());
            }
        }

        private static void ReadCharacteristic_ReadRequested(GattLocalCharacteristic sender, GattReadRequestedEventArgs ReadRequestEventArgs)
        {
            GattReadRequest request = ReadRequestEventArgs.GetRequest();
            request.RespondWithValue(GetTimeBuffer());
        }

        private static Buffer GetTimeBuffer()
        {
            DateTime dt = DateTime.UtcNow;
            DataWriter dw = new DataWriter();
            dw.WriteByte((Byte)dt.Hour);
            dw.WriteByte((Byte)dt.Minute);
            dw.WriteByte((Byte)dt.Second);
            return dw.DetachBuffer();
        }

        private static void _readWriteCharacteristic_ReadRequested(GattLocalCharacteristic sender, GattReadRequestedEventArgs ReadRequestEventArgs)
        {
            GattReadRequest request = ReadRequestEventArgs.GetRequest();
            DataWriter dw = new DataWriter();
            dw.WriteByte((Byte)_redValue);
            dw.WriteByte((Byte)_greenValue);
            dw.WriteByte((Byte)_blueValue);
            request.RespondWithValue(dw.DetachBuffer());
        }

        private static void _readWriteCharacteristic_WriteRequested(GattLocalCharacteristic sender, GattWriteRequestedEventArgs WriteRequestEventArgs)
        {
            GattWriteRequest request = WriteRequestEventArgs.GetRequest();

            if (request.Value.Length != 3)
            {
                request.RespondWithProtocolError((byte)BluetoothError.NotSupported);
                return;
            }

            DataReader rdr = DataReader.FromBuffer(request.Value);
            _redValue = rdr.ReadByte();
            _greenValue = rdr.ReadByte();
            _blueValue = rdr.ReadByte();

            if (request.Option == GattWriteOption.WriteWithResponse)
            {
                request.Respond();
            }

            Debug.WriteLine($"Received RGB={_redValue}/{_greenValue}/{_blueValue}");
        }
    }
}
