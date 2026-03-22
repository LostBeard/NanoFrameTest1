namespace NanoFrameTest1.Tests;

/// <summary>
/// BLE connection tests.
///
/// Playwright cannot pick a peripheral for you — when the manual test runs, select your
/// Freenove ESP32-S3-WROOM in the Chrome / Windows Bluetooth dialog.
///
/// Other BLE checks (headed Chrome, no device chooser):
///   dotnet test --filter "Category=BLE"
///
/// Full connect — you pick the ESP32 in the system dialog:
///   dotnet test --filter "Category=BLEManual"
/// </summary>
[Category("BLE")]
public class BleConnectionTests : TestBase
{
    protected override bool RequiresHardware => true;

    [Test]
    public async Task BleAvailability_BluetoothApiExists()
    {
        var available = await Page.EvaluateAsync<bool>("() => navigator.bluetooth !== undefined");
        Assert.That(available, Is.True, "Web Bluetooth API should be available");
    }

    [Test]
    public async Task BleAvailability_AdapterPresent()
    {
        var available = await Page.EvaluateAsync<bool>(
            "async () => { if (!navigator.bluetooth) return false; return await navigator.bluetooth.getAvailability(); }");
        Assert.That(available, Is.True, "Bluetooth adapter should be available");
    }

    [Test]
    public async Task BleUi_ConnectButtonEnabled()
    {
        var connectButton = Page.GetByRole(AriaRole.Button, new() { Name = "Connect to ESP32" });
        await Expect(connectButton).ToBeEnabledAsync();
    }
}
