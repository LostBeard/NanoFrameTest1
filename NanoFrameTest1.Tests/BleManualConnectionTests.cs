namespace NanoFrameTest1.Tests;

/// <summary>
/// Headed Chrome + your real ESP32. Run when the Freenove board is on and advertising.
/// <code>
/// dotnet test NanoFrameTest1.Tests/NanoFrameTest1.Tests.csproj --filter "Category=BLEManual"
/// </code>
/// When the Bluetooth picker opens, select <b>ESP32-S3-WROOM</b> (or the name shown for your firmware).
/// </summary>
[Category("BLEManual")]
public class BleManualConnectionTests : TestBase
{
    protected override bool RequiresHardware => true;

    [Test]
    public async Task ManualBleConnection_FullFlow()
    {
        Console.WriteLine();
        Console.WriteLine("========== MANUAL BLE TEST ==========");
        Console.WriteLine(">>> Chrome: click \"Connect to ESP32\", then choose your ESP32-S3-WROOM and Pair.");
        Console.WriteLine(">>> Waiting up to 3 minutes for GATT + service discovery…");
        Console.WriteLine("=====================================");
        Console.WriteLine();

        await Page.BringToFrontAsync();
        await Task.Delay(800);

        var connectButton = Page.GetByRole(AriaRole.Button, new() { Name = "Connect to ESP32" });
        await Expect(connectButton).ToBeEnabledAsync();
        await connectButton.ClickAsync();

        var gattMsg = await WaitForConsoleMessage("[BLE] GATT connected:", 180000);
        Assert.That(gattMsg, Is.Not.Null, "Expected [BLE] GATT connected: … (did you pick the ESP32 in the chooser?)");
        Console.WriteLine(gattMsg);

        var completeMsg = await WaitForConsoleMessage("[BLE] Connection complete", 180000);

        Console.WriteLine();
        Console.WriteLine("=== BLE console lines ===");
        foreach (var msg in ConsoleMessages.Where(m => m.Contains("[BLE]")))
            Console.WriteLine($"  {msg}");

        Assert.That(completeMsg, Is.Not.Null, "Expected [BLE] Connection complete …");
        Assert.That(completeMsg, Does.Contain("Server connected: True"),
            "GATT should stay connected after service discovery");
    }
}
