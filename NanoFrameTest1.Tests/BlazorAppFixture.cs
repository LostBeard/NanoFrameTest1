using System.Diagnostics;
using System.Net.Http;
using Microsoft.AspNetCore.SignalR;

namespace NanoFrameTest1.Tests;

/// <summary>
/// Hosts the Blazor WebAssembly app for Playwright the same way as PlaywrightMultiTest: run
/// <c>dotnet publish</c>, then serve <c>publish/wwwroot</c> from this process via Kestrel HTTPS
/// with <c>ServeUnknownFileTypes</c>. This avoids <c>dotnet run</c> dev-server races, port hijack,
/// and stale <c>index.html</c> vs <c>_framework</c> fingerprints.
/// </summary>
public sealed class BlazorAppFixture : IAsyncDisposable
{
    const string TargetFramework = "net10.0";
    const int Port = 5210;
    const string CertPassword = "unittests";

    static readonly HttpClient s_probeClient = CreateProbeClient();

    BlazorStaticHost? _host;
    bool _started;

    public string BaseUrl => $"https://localhost:{Port}/";

    /// <summary>
    /// Per-run sandbox path served to the WASM app as <c>/__wasmtest/fs/*</c>. Populated after
    /// <see cref="EnsureStartedAsync"/> completes.
    /// </summary>
    public string? WasmDebugSandboxPath { get; private set; }

    public IReadOnlyList<string> GetWasmHostLogSnapshot() =>
        _host?.GetWasmHostLogSnapshot() ?? Array.Empty<string>();

    public void ClearWasmHostLog() => _host?.ClearWasmHostLog();

    /// <summary>
    /// Call into the browser from tests, e.g.
    /// <c>await WasmDebugHubContext!.Clients.All.SendAsync("ClientInvoke", "MyHook", "{}")</c>
    /// or <c>SendAsync("ClientNotify", "hello")</c>.
    /// </summary>
    public IHubContext<WasmDebugHub>? WasmDebugHubContext => _host?.WasmDebugHubContext;

    public async Task EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        if (_started) return;

        var projectDir = ResolveBlazorWasmProjectDirectory();

        var certPath = Path.Combine(AppContext.BaseDirectory, "assets", "testcert.pfx");
        if (!File.Exists(certPath))
            throw new FileNotFoundException(
                "Missing assets/testcert.pfx (copy from PlaywrightMultiTest or rebuild tests).", certPath);

        Console.WriteLine($"[BlazorAppFixture] Publishing Blazor app: {projectDir}");
        var pubExit = await RunDotnetAsync(
            "publish -c Release -v q",
            projectDir,
            cancellationToken,
            TimeSpan.FromMinutes(5)).ConfigureAwait(false);
        if (pubExit != 0)
            throw new InvalidOperationException($"dotnet publish failed with exit code {pubExit} for {projectDir}");

        var wwwroot = Path.Combine(projectDir, "bin", "Release", TargetFramework, "publish", "wwwroot");
        if (!File.Exists(Path.Combine(wwwroot, "index.html")))
            throw new DirectoryNotFoundException(
                $"Published wwwroot missing index.html after publish. Expected: {wwwroot}");

        await TryFreePortIfWrongHostAsync(wwwroot, cancellationToken).ConfigureAwait(false);

        _host = new BlazorStaticHost(wwwroot, BaseUrl, certPath, CertPassword);
        WasmDebugSandboxPath = _host.WasmDebugSandboxPath;
        _host.Start();

        if (!await WaitForServerAsync(TimeSpan.FromSeconds(120), cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException(
                $"Blazor static host failed to become ready at {BaseUrl} (see port conflicts / firewall).");

        _started = true;
        Console.WriteLine($"[BlazorAppFixture] Serving published WASM at {BaseUrl}");
        Console.WriteLine($"[BlazorAppFixture] WASM debug sandbox: {WasmDebugSandboxPath}");
        Console.WriteLine($"[BlazorAppFixture] WASM debug API: {BaseUrl}__wasmtest/info");
        Console.WriteLine($"[BlazorAppFixture] WASM debug SignalR: {BaseUrl}__wasmtest/signalr");
    }

    /// <summary>
    /// If something already listens on our port but does not serve this publish output, fail fast
    /// with a clear message instead of silently mixing hosts.
    /// </summary>
    async Task TryFreePortIfWrongHostAsync(string wwwroot, CancellationToken cancellationToken)
    {
        var marker = Path.Combine(wwwroot, "_framework", "blazor.boot.json");
        if (!File.Exists(marker))
            return;

        var expectedHash = await File.ReadAllTextAsync(marker, cancellationToken).ConfigureAwait(false);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "_framework/blazor.boot.json");
            using var resp = await s_probeClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return;

            var remote = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.Equals(remote.Trim(), expectedHash.Trim(), StringComparison.Ordinal))
                return;

            throw new InvalidOperationException(
                $"Port {Port} is serving a different Blazor build (blazor.boot.json mismatch). " +
                "Stop the other process using this port, then re-run tests.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            // Nothing listening or not HTTPS / cert rejected — our host will bind.
        }
    }

    static HttpClient CreateProbeClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
    }

    static async Task<int> RunDotnetAsync(
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        proc.OutputDataReceived += (_, _) => { };
        proc.ErrorDataReceived += (_, _) => { };

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        proc.Exited += (_, _) =>
        {
            try
            {
                tcs.TrySetResult(proc.ExitCode);
            }
            catch
            {
                tcs.TrySetResult(-1);
            }
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var regTimeout = timeoutCts.Token.Register(() =>
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            tcs.TrySetResult(-1);
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    async Task<bool> WaitForServerAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var resp = await s_probeClient.GetAsync(BaseUrl, cancellationToken).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                    return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // ignore
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host != null)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
        }

        WasmDebugSandboxPath = null;
        _started = false;
    }

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> until <c>BlazorWasmESP32S3WROOM.csproj</c>
    /// is found so tests work from any output layout (e.g. <c>dotnet test --artifacts-path</c>).
    /// </summary>
    static string ResolveBlazorWasmProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var csproj = Path.Combine(dir.FullName, "BlazorWasmESP32S3WROOM", "BlazorWasmESP32S3WROOM.csproj");
            if (File.Exists(csproj))
                return Path.GetDirectoryName(csproj)!;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find BlazorWasmESP32S3WROOM/BlazorWasmESP32S3WROOM.csproj by walking up from " +
            AppContext.BaseDirectory);
    }
}
