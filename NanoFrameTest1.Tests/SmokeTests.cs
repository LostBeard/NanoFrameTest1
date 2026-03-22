namespace NanoFrameTest1.Tests;

/// <summary>
/// Basic smoke tests — app loads, key UI elements present.
/// These don't require BLE hardware.
/// </summary>
[Category("Smoke")]
public class SmokeTests : TestBase
{
    [Test]
    public async Task AppLoads_ShowsDashboardTitle()
    {
        var heading = Page.Locator("h1");
        await Expect(heading).ToBeVisibleAsync();
        await Expect(heading).ToContainTextAsync("ESP32-S3-WROOM Dashboard");
    }

    [Test]
    public async Task ConnectButton_IsVisible()
    {
        var connectButton = Page.GetByRole(AriaRole.Button, new() { Name = "Connect to ESP32" });
        await Expect(connectButton).ToBeVisibleAsync();
        await Expect(connectButton).ToBeEnabledAsync();
    }

    [Test]
    public async Task DisconnectButton_IsDisabledWhenNotConnected()
    {
        var disconnectButton = Page.GetByRole(AriaRole.Button, new() { Name = "Disconnect" });
        await Expect(disconnectButton).ToBeVisibleAsync();
        await Expect(disconnectButton).ToBeDisabledAsync();
    }

    [Test]
    public async Task BleStatus_ShowsDisconnected()
    {
        var statusText = Page.Locator("text=BLE:");
        await Expect(statusText).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Disconnected")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WebBluetooth_IsSupported()
    {
        // Verify no "Web Bluetooth not supported" error is shown
        var errorAlert = Page.Locator(".alert-danger >> text=Web Bluetooth is not supported");
        await Expect(errorAlert).ToHaveCountAsync(0);
    }

    [Test]
    public async Task DarkTheme_IsApplied()
    {
        // Verify dark background is applied
        var bgColor = await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.body).backgroundColor");
        // #121212 = rgb(18, 18, 18)
        Assert.That(bgColor, Does.Contain("18").Or.Contains("#121212"),
            "Body background should be dark (#121212)");
    }

    [Test]
    public async Task WifiConfigPanel_NotVisibleWhenDisconnected()
    {
        // WiFi config should only show after BLE connection
        var wifiCard = Page.Locator("text=WiFi Configuration");
        await Expect(wifiCard).ToHaveCountAsync(0);
    }

    [Test]
    public async Task DebugConsole_NotVisibleWhenDisconnected()
    {
        var debugCard = Page.Locator("text=Debug Console");
        await Expect(debugCard).ToHaveCountAsync(0);
    }
}
