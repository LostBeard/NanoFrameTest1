using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using System.Net;

namespace NanoFrameTest1.Tests;

/// <summary>
/// Serves a Blazor WASM <c>publish/wwwroot</c> tree over HTTPS, matching the approach in
/// PlaywrightMultiTest's <c>StaticFileServer</c>: Kestrel + dev PFX, unknown file types enabled
/// (required for <c>.wasm</c>, <c>.dll</c>, etc.).
/// </summary>
sealed class BlazorStaticHost : IAsyncDisposable
{
    readonly string _wwwRoot;
    readonly string _url;
    readonly string _certPath;
    readonly string _certPassword;
    readonly string _requestPath;
    readonly string _sandboxRoot;
    readonly WasmTestHostState _wasmState;

    WebApplication? _app;
    Task? _runningTask;
    IHubContext<WasmDebugHub>? _hubContext;

    public BlazorStaticHost(string wwwroot, string url, string certPath, string certPassword, string requestPath = "")
    {
        if (string.IsNullOrWhiteSpace(wwwroot))
            throw new ArgumentNullException(nameof(wwwroot));
        if (!Directory.Exists(wwwroot))
            throw new DirectoryNotFoundException(wwwroot);
        if (!File.Exists(certPath))
            throw new FileNotFoundException("HTTPS test certificate not found.", certPath);

        _wwwRoot = Path.GetFullPath(wwwroot);
        _url = url;
        _certPath = certPath;
        _certPassword = certPassword;
        _requestPath = requestPath;

        _sandboxRoot = Path.Combine(
            Path.GetTempPath(),
            "NanoFrameTest1",
            "wasm-sandbox",
            $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        _wasmState = new WasmTestHostState(_sandboxRoot);
    }

    /// <summary>Directory exposed to the WASM app via <c>/__wasmtest/fs/*</c> (tests can read artifacts here).</summary>
    public string WasmDebugSandboxPath => _sandboxRoot;

    public IReadOnlyList<string> GetWasmHostLogSnapshot() => _wasmState.SnapshotLog();

    public void ClearWasmHostLog() => _wasmState.ClearLog();

    /// <summary>Push commands to connected browsers from Playwright tests (e.g. <c>Clients.All.SendAsync("ClientInvoke", …)</c>).</summary>
    public IHubContext<WasmDebugHub>? WasmDebugHubContext => _hubContext;

    public void Start()
    {
        _runningTask ??= StartAsync();
    }

    async Task StartAsync()
    {
        try
        {
            var builder = WebApplication.CreateBuilder();
            var port = new Uri(_url).Port;

            builder.Logging.ClearProviders();

            builder.WebHost.UseKestrel();
            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.Listen(IPAddress.Loopback, port, listenOptions =>
                {
                    listenOptions.UseHttps(_certPath, _certPassword);
                });
            });

            builder.Environment.WebRootPath = _wwwRoot;
            builder.WebHost.UseUrls(_url);

            builder.Services.AddSingleton(_wasmState);
            builder.Services.AddSignalR();

            _app = builder.Build();

            _app.MapHub<WasmDebugHub>("/__wasmtest/signalr");
            _hubContext = _app.Services.GetRequiredService<IHubContext<WasmDebugHub>>();

            _app.Use(async (context, next) =>
            {
                if (!context.Request.Path.StartsWithSegments("/__wasmtest/signalr"))
                {
                    context.Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";
                    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
                }

                await next();
            });

            // WASM / Playwright helpers: logging + sandboxed filesystem (localhost only).
            _app.UseWasmTestDebugApi(_wasmState);

            _app.UseStatusCodePagesWithReExecute(string.IsNullOrEmpty(_requestPath) ? "/" : _requestPath);

            _app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new PhysicalFileProvider(_wwwRoot),
                RequestPath = _requestPath
            });

            _app.UseFileServer(new FileServerOptions
            {
                FileProvider = new PhysicalFileProvider(_wwwRoot),
                RequestPath = _requestPath,
                EnableDirectoryBrowsing = false,
                StaticFileOptions =
                {
                    ServeUnknownFileTypes = true,
                    DefaultContentType = "application/octet-stream"
                }
            });

            await _app.RunAsync();
        }
        finally
        {
            _hubContext = null;
            _app = null;
            _runningTask = null;
        }
    }

    public async Task StopAsync()
    {
        if (_app == null || _runningTask == null) return;
        try
        {
            await _app.StopAsync();
        }
        catch
        {
            // ignore
        }

        await _app.DisposeAsync();
        try
        {
            await _runningTask;
        }
        catch
        {
            // ignore
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
