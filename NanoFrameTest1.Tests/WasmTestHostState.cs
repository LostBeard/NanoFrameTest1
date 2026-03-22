using System.Text.Json;

namespace NanoFrameTest1.Tests;

/// <summary>
/// Shared state for <see cref="BlazorStaticHost"/> debug endpoints: bounded in-memory log and a
/// single sandbox directory for optional WASM → host file I/O during Playwright runs.
/// </summary>
public sealed class WasmTestHostState
{
    const int MaxLogLines = 8000;
    const int MaxUtf8FileBytes = 10 * 1024 * 1024;

    readonly List<string> _log = new();
    readonly object _logLock = new();

    public WasmTestHostState(string sandboxRoot)
    {
        SandboxRoot = sandboxRoot;
        Directory.CreateDirectory(sandboxRoot);
    }

    public string SandboxRoot { get; }

    public void AppendLog(string line)
    {
        var entry = $"{DateTime.UtcNow:O} {line}";
        lock (_logLock)
        {
            _log.Add(entry);
            while (_log.Count > MaxLogLines)
                _log.RemoveAt(0);
        }

        Console.WriteLine($"[wasmtest] {line}");
    }

    public IReadOnlyList<string> SnapshotLog()
    {
        lock (_logLock)
            return _log.ToList();
    }

    public void ClearLog()
    {
        lock (_logLock)
            _log.Clear();
    }

    public sealed record FsListResult(string[] Directories, string[] Files);

    public async Task FsWriteRelativeAsync(FsWriteDto dto, CancellationToken ct = default)
    {
        if (dto.Path == null || dto.Content == null)
            throw new InvalidOperationException("path and content are required");

        if (!TryResolveSandboxPath(SandboxRoot, dto.Path, out var full, out var err))
            throw new InvalidOperationException(err);

        await WriteFileAsync(full, dto, ct).ConfigureAwait(false);
    }

    public async Task<string?> FsReadBase64Async(string relativePath, CancellationToken ct = default)
    {
        if (!TryResolveSandboxPath(SandboxRoot, relativePath, out var full, out _))
            return null;

        if (!File.Exists(full))
            return null;

        var bytes = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);
        return Convert.ToBase64String(bytes);
    }

    public Task<FsListResult?> FsListAsync(string relativePath, CancellationToken ct = default)
    {
        var rel = string.IsNullOrWhiteSpace(relativePath) ? "." : relativePath;
        if (!TryResolveSandboxPath(SandboxRoot, rel, out var full, out _))
            return Task.FromResult<FsListResult?>(null);

        if (!Directory.Exists(full))
            return Task.FromResult<FsListResult?>(null);

        var dirs = Directory.GetDirectories(full).Select(static p => Path.GetFileName(p)!).ToArray();
        var files = Directory.GetFiles(full).Select(static p => Path.GetFileName(p)!).ToArray();
        return Task.FromResult<FsListResult?>(new FsListResult(dirs, files));
    }

    public Task FsDeleteRelativeAsync(string relativePath, CancellationToken ct = default)
    {
        if (!TryResolveSandboxPath(SandboxRoot, relativePath, out var full, out var err))
            throw new InvalidOperationException(err);

        if (File.Exists(full))
            File.Delete(full);
        else if (Directory.Exists(full))
            Directory.Delete(full, recursive: true);
        else
            throw new FileNotFoundException(relativePath);

        return Task.CompletedTask;
    }

    public static bool TryResolveSandboxPath(string sandboxRoot, string relative, out string fullPath, out string error)
    {
        fullPath = "";
        error = "";
        if (string.IsNullOrWhiteSpace(relative))
        {
            error = "path is required";
            return false;
        }

        relative = relative.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (relative.Contains("..", StringComparison.Ordinal))
        {
            error = "path must not contain '..'";
            return false;
        }

        if (Path.IsPathRooted(relative))
        {
            error = "path must be relative";
            return false;
        }

        var rootFull = Path.GetFullPath(sandboxRoot);
        var combined = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!combined.StartsWith(rootFull, PathInternal.StringComparison))
        {
            error = "path escapes sandbox";
            return false;
        }

        fullPath = combined;
        return true;
    }

    /// <summary>JSON body for POST <c>/__wasmtest/fs/write</c>.</summary>
    public sealed class FsWriteDto
    {
        public string? Path { get; set; }
        public string? Content { get; set; }
        /// <summary><c>utf8</c> (default) or <c>base64</c>.</summary>
        public string? Encoding { get; set; }
    }

    /// <summary>JSON body for POST <c>/__wasmtest/log</c> when Content-Type is JSON.</summary>
    public sealed class LogDto
    {
        public string? Message { get; set; }
    }

    static class PathInternal
    {
        public static readonly StringComparison StringComparison =
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public static async Task WriteFileAsync(string fullPath, FsWriteDto dto, CancellationToken ct)
    {
        if (dto.Content == null)
            throw new InvalidOperationException("content is required");

        var enc = dto.Encoding?.Trim().ToLowerInvariant() ?? "utf8";
        byte[] bytes = enc switch
        {
            "utf8" or "text" => System.Text.Encoding.UTF8.GetBytes(dto.Content),
            "base64" => Convert.FromBase64String(dto.Content),
            _ => throw new InvalidOperationException("encoding must be utf8 or base64")
        };

        if (bytes.Length > MaxUtf8FileBytes)
            throw new InvalidOperationException($"content exceeds {MaxUtf8FileBytes} bytes");

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(fullPath, bytes, ct).ConfigureAwait(false);
    }

    public static LogDto? TryParseLogJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<LogDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static FsWriteDto? TryParseFsWriteJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<FsWriteDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
