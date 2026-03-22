using System.Diagnostics;

namespace NanoFrameTest1.Tests;

/// <summary>
/// Hosts the Blazor WebAssembly app for Playwright.
/// <para>
/// WASM cannot run from <c>file://</c> or inside the test process — it must be served over HTTP(S).
/// This fixture runs <c>dotnet run --urls …</c> in <see cref="BlazorProjectDir"/> (the
/// <c>Microsoft.NET.Sdk.BlazorWebAssembly</c> project with <c>WebAssembly.DevServer</c>), which
/// starts the official dev host (Kestrel) that serves <c>_framework/</c>, DLLs, and <c>wwwroot</c>.
/// Playwright then navigates to <see cref="BaseUrl"/> like a normal user.
/// </para>
/// </summary>
public class BlazorAppFixture : IDisposable
{
    const string BlazorProjectDir = "../BlazorWasmESP32S3WROOM";
    const int Port = 5210;

    Process? _serverProcess;
    bool _isRunning;

    public string BaseUrl => $"http://localhost:{Port}";

    public void EnsureStarted()
    {
        if (_isRunning) return;

        // Check if something is already listening on the port
        try
        {
            using var client = new HttpClient();
            var response = client.GetAsync(BaseUrl).Result;
            if (response.IsSuccessStatusCode)
            {
                _isRunning = true;
                Console.WriteLine($"[BlazorAppFixture] App already running at {BaseUrl}");
                return;
            }
        }
        catch
        {
            // Not running, we'll start it
        }

        var projectDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", BlazorProjectDir));

        Console.WriteLine($"[BlazorAppFixture] Starting Blazor app from {projectDir}");

        _serverProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                // Do not use --no-build: ensures the dev server matches the latest build from tests.
                Arguments = $"run --urls {BaseUrl}",
                WorkingDirectory = projectDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        _serverProcess.Start();

        // `dotnet run` may compile on first start — allow plenty of time before tests hit the URL.
        var ready = WaitForServer(TimeSpan.FromSeconds(180));
        if (!ready)
        {
            throw new Exception($"Blazor app failed to start at {BaseUrl} within 180 seconds (see dotnet output / port conflicts).");
        }

        _isRunning = true;
        Console.WriteLine($"[BlazorAppFixture] Blazor app ready at {BaseUrl}");
    }

    bool WaitForServer(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        using var client = new HttpClient();

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = client.GetAsync(BaseUrl).Result;
                if (response.IsSuccessStatusCode) return true;
            }
            catch { }

            Thread.Sleep(500);
        }

        return false;
    }

    public void Dispose()
    {
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            _serverProcess.Kill(entireProcessTree: true);
            _serverProcess.Dispose();
            Console.WriteLine("[BlazorAppFixture] Blazor app stopped");
        }
    }
}
