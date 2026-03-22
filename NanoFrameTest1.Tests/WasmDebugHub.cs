using Microsoft.AspNetCore.SignalR;

namespace NanoFrameTest1.Tests;

/// <summary>
/// Bidirectional test hub (localhost only). Client invokes logging / sandbox FS; tests use
/// <see cref="IHubContext{WasmDebugHub}"/> to call <c>ClientInvoke</c> / <c>ClientNotify</c> on the browser.
/// </summary>
public sealed class WasmDebugHub : Hub
{
    readonly WasmTestHostState _state;

    public WasmDebugHub(WasmTestHostState state) => _state = state;

    public override Task OnConnectedAsync()
    {
        _state.AppendLog($"[hub] connected {Context.ConnectionId}");
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _state.AppendLog($"[hub] disconnected {Context.ConnectionId}: {exception?.Message ?? "ok"}");
        return base.OnDisconnectedAsync(exception);
    }

    public Task Log(string message)
    {
        _state.AppendLog(message);
        return Task.CompletedTask;
    }

    public string Ping() => "pong";

    public WasmDebugHubInfoDto GetInfo() =>
        new(Context.ConnectionId, _state.SandboxRoot);

    public Task FsWrite(string path, string content, string encoding)
    {
        var dto = new WasmTestHostState.FsWriteDto
        {
            Path = path,
            Content = content,
            Encoding = encoding
        };
        return _state.FsWriteRelativeAsync(dto, Context.ConnectionAborted);
    }

    public Task<string?> FsReadBase64(string path) =>
        _state.FsReadBase64Async(path, Context.ConnectionAborted);

    public Task<WasmTestHostState.FsListResult?> FsList(string path) =>
        _state.FsListAsync(path ?? ".", Context.ConnectionAborted);

    public Task FsDelete(string path) =>
        _state.FsDeleteRelativeAsync(path, Context.ConnectionAborted);

    /// <summary>Snapshot of server-side log (newest last). Optionally clear after read.</summary>
    public string[] GetLogLines(bool clear = false)
    {
        var lines = _state.SnapshotLog();
        if (clear)
            _state.ClearLog();
        return lines.ToArray();
    }

    public Task ClearLog()
    {
        _state.ClearLog();
        return Task.CompletedTask;
    }
}

public sealed record WasmDebugHubInfoDto(string ConnectionId, string SandboxRoot);
