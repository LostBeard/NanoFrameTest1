using Microsoft.AspNetCore.SignalR;
using Microsoft.Playwright;

namespace NanoFrameTest1.Tests;

/// <summary>
/// Base class for all Playwright-based tests against the **hosted** Blazor WASM app.
/// <para>
/// <see cref="BlazorAppFixture"/> must be running (see <c>OneTimeSetUp</c>): it hosts the WASM
/// project at <c>https://localhost:5210/</c> and exposes <c>/__wasmtest/*</c> for client logging
/// and sandboxed file I/O. These tests are not “in” the WASM assembly — they automate Chromium
/// against that URL.
/// </para>
/// One browser per test class, one context+page per test.
///
/// Usage:
///   dotnet test                                      — run all tests
///   dotnet test --filter "Category=BLE"              — run only BLE tests
///   dotnet test --filter "Category=Smoke"            — run only smoke tests
///   dotnet test --filter "FullyQualifiedName~Dashboard" — run by name pattern
/// </summary>
public class TestBase
{
    // App fixture is shared across ALL test classes (static, never disposed mid-run)
    static readonly BlazorAppFixture _appFixture = new();
    protected string BaseUrl => _appFixture.BaseUrl;

    /// <summary>Host filesystem sandbox the Blazor app can use via <c>fetch('/__wasmtest/fs/...')</c>.</summary>
    protected static string? WasmDebugSandboxPath => _appFixture.WasmDebugSandboxPath;

    protected static IReadOnlyList<string> GetWasmHostLogs() => _appFixture.GetWasmHostLogSnapshot();

    protected static void ClearWasmHostLogs() => _appFixture.ClearWasmHostLog();

    /// <summary>
    /// Push to the Blazor app: <c>await WasmDebugHub!.Clients.All.SendAsync("ClientInvoke", method, json)</c>
    /// or <c>SendAsync("ClientNotify", text)</c>.
    /// </summary>
    protected static IHubContext<WasmDebugHub>? WasmDebugHub => _appFixture.WasmDebugHubContext;

    IPlaywright? _playwright;
    IBrowser? _browser;
    IBrowserContext? _context;
    protected IPage Page { get; private set; } = null!;

    /// <summary>
    /// Override in test classes that need real hardware (BLE, etc.)
    /// Forces headed mode since headless Chrome has no BLE adapter.
    /// </summary>
    protected virtual bool RequiresHardware => false;

    /// <summary>Console messages captured during the test.</summary>
    protected List<string> ConsoleMessages { get; } = new();

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        await _appFixture.EnsureStartedAsync().ConfigureAwait(false);

        // One browser per test class
        _playwright = await Playwright.CreateAsync();
        var args = new List<string>
        {
            "--enable-features=WebBluetooth",
            "--enable-experimental-web-platform-features"
        };

        if (RequiresHardware)
        {
            // For BLE tests: headed Chrome with Web Bluetooth (headless has no radio).
            args.Add("--enable-web-bluetooth-new-permissions-backend");
        }

        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !RequiresHardware,
            Channel = "chrome",
            Args = args.ToArray(),
            // Headed BLE runs: slow down UI so the OS/Chrome device chooser is easier to catch.
            SlowMo = RequiresHardware ? 120 : 0
        });
    }

    [SetUp]
    public async Task SetupPage()
    {
        ConsoleMessages.Clear();
        _context = await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
        });

        // Best-effort: Chromium may accept "bluetooth" even if Playwright docs omit it.
        if (RequiresHardware)
        {
            try
            {
                await _context.GrantPermissionsAsync(
                    new[] { "bluetooth" },
                    new() { Origin = BaseUrl.TrimEnd('/') });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestBase] GrantPermissionsAsync(bluetooth) skipped: {ex.Message}");
            }
        }

        Page = await _context.NewPageAsync();
        Page.Console += (_, msg) => ConsoleMessages.Add(msg.Text);
        Page.PageError += (_, err) => Console.WriteLine($"[TestBase] Page error: {err}");
        var response = await Page.GotoAsync(BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 120000
        });
        Console.WriteLine($"[TestBase] Page loaded: {response?.Status} url={Page.Url}");

        if (await Page.Locator("#blazor-error-ui").IsVisibleAsync())
        {
            var errUi = await Page.Locator("#blazor-error-ui").InnerTextAsync();
            throw new InvalidOperationException($"Blazor failed to start (error UI visible): {errUi}");
        }

        // WASM must download and boot before any h1 from routed content exists.
        await Page.GetByRole(AriaRole.Heading, new() { Name = "ESP32-S3-WROOM Dashboard" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 180000 });
    }

    [TearDown]
    public async Task TeardownPage()
    {
        if (_context != null)
        {
            await _context.CloseAsync();
            _context = null;
        }
    }

    protected ILocatorAssertions Expect(ILocator locator) =>
        Assertions.Expect(locator);

    protected async Task<string?> WaitForConsoleMessage(string contains, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var match = ConsoleMessages.FirstOrDefault(m => m.Contains(contains));
            if (match != null) return match;
            await Task.Delay(100);
        }
        return null;
    }

    [OneTimeTearDown]
    public async Task OneTimeTeardown()
    {
        if (_browser != null) { await _browser.CloseAsync(); _browser = null; }
        _playwright?.Dispose(); _playwright = null;
    }
}
