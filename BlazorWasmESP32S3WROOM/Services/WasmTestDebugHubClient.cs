using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.SignalR.Client;

namespace BlazorWasmESP32S3WROOM.Services;

/// <summary>
/// Optional connection to the Playwright test host SignalR hub at <c>/__wasmtest/signalr</c>.
/// If the hub is missing (e.g. static deploy), all operations no-op or return default without throwing.
/// </summary>
public sealed class WasmTestDebugHubClient : IAsyncDisposable
{
    readonly string _hubUrl;
    readonly ConcurrentDictionary<string, Func<string, Task>> _handlers = new(StringComparer.Ordinal);

    HubConnection? _connection;

    public WasmTestDebugHubClient(IWebAssemblyHostEnvironment env)
    {
        _hubUrl = new Uri(new Uri(env.BaseAddress, UriKind.Absolute), "__wasmtest/signalr").ToString();
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    /// <summary>Raised for every server push on <c>ClientInvoke</c> (method name + JSON payload).</summary>
    public event Action<string, string>? ServerClientInvoke;

    /// <summary>Raised for server push <c>ClientNotify</c> (plain text).</summary>
    public event Action<string>? ServerNotify;

    /// <summary>Register a handler for a specific <paramref name="methodName"/> from <c>ClientInvoke</c>.</summary>
    public void RegisterClientHandler(string methodName, Func<string, Task> jsonPayloadHandler) =>
        _handlers[methodName] = jsonPayloadHandler;

    public void UnregisterClientHandler(string methodName) => _handlers.TryRemove(methodName, out _);

    public async Task EnsureConnectedAsync()
    {
        if (_connection != null)
            return;

        var conn = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .Build();

        conn.On<string, string>("ClientInvoke", OnClientInvokeAsync);
        conn.On<string>("ClientNotify", OnClientNotify);

        try
        {
            await conn.StartAsync().ConfigureAwait(false);
            _connection = conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
        }
    }

    Task OnClientInvokeAsync(string methodName, string jsonPayload)
    {
        ServerClientInvoke?.Invoke(methodName, jsonPayload);
        if (_handlers.TryGetValue(methodName, out var handler))
            return handler(jsonPayload);
        return Task.CompletedTask;
    }

    void OnClientNotify(string message) => ServerNotify?.Invoke(message);

    public Task LogAsync(string message) =>
        SafeInvokeAsync(c => c.InvokeAsync("Log", message));

    public Task<string?> PingAsync() =>
        SafeInvokeAsync(c => c.InvokeAsync<string>("Ping"));

    public Task<WasmDebugHubInfoClientDto?> GetInfoAsync() =>
        SafeInvokeAsync(c => c.InvokeAsync<WasmDebugHubInfoClientDto>("GetInfo"));

    public Task FsWriteAsync(string path, string content, string encoding = "utf8") =>
        SafeInvokeAsync(c => c.InvokeAsync("FsWrite", path, content, encoding));

    public Task<string?> FsReadBase64Async(string path) =>
        SafeInvokeAsync(c => c.InvokeAsync<string>("FsReadBase64", path));

    public Task<WasmFsListClientDto?> FsListAsync(string path) =>
        SafeInvokeAsync(c => c.InvokeAsync<WasmFsListClientDto>("FsList", path ?? "."));

    public Task FsDeleteAsync(string path) =>
        SafeInvokeAsync(c => c.InvokeAsync("FsDelete", path));

    public Task<string[]?> GetLogLinesAsync(bool clear = false) =>
        SafeInvokeAsync(c => c.InvokeAsync<string[]>("GetLogLines", clear));

    public Task ClearServerLogAsync() =>
        SafeInvokeAsync(c => c.InvokeAsync("ClearLog"));

    async Task SafeInvokeAsync(Func<HubConnection, Task> invoke)
    {
        if (_connection?.State != HubConnectionState.Connected)
            return;

        try
        {
            await invoke(_connection).ConfigureAwait(false);
        }
        catch
        {
            // Hub gone mid-test — ignore.
        }
    }

    async Task<T?> SafeInvokeAsync<T>(Func<HubConnection, Task<T>> invoke)
    {
        if (_connection?.State != HubConnectionState.Connected)
            return default;

        try
        {
            return await invoke(_connection).ConfigureAwait(false);
        }
        catch
        {
            return default;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }
}
