using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace NanoFrameTest1.Tests;

/// <summary>
/// Test-only HTTP API mounted at <c>/__wasmtest/*</c> (localhost HTTPS). Lets the Blazor WASM
/// client POST logs and read/write files under a dedicated sandbox directory — useful when
/// browser storage or the test process needs artifacts without touching the real filesystem.
/// </summary>
static class WasmTestHostMiddleware
{
    public static void UseWasmTestDebugApi(this WebApplication app, WasmTestHostState state)
    {
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/__wasmtest/signalr"))
            {
                await next().ConfigureAwait(false);
                return;
            }

            if (!ctx.Request.Path.StartsWithSegments("/__wasmtest"))
            {
                await next().ConfigureAwait(false);
                return;
            }

            await HandleAsync(ctx, state).ConfigureAwait(false);
        });
    }

    static async Task HandleAsync(HttpContext ctx, WasmTestHostState state)
    {
        var path = (ctx.Request.Path.Value ?? "").TrimEnd('/');
        var method = ctx.Request.Method;
        var sub = path.StartsWith("/__wasmtest", StringComparison.OrdinalIgnoreCase)
            ? path["/__wasmtest".Length..].TrimStart('/')
            : "";

        try
        {
            if (sub.Equals("info", StringComparison.OrdinalIgnoreCase) && method == HttpMethods.Get)
            {
                await ctx.Response.WriteAsJsonAsync(new
                {
                    sandbox = state.SandboxRoot,
                    endpoints = new[]
                    {
                        "GET /__wasmtest/info",
                        "POST /__wasmtest/log (text/plain or JSON {\"message\"})",
                        "GET /__wasmtest/log?clear=true",
                        "POST /__wasmtest/fs/write (JSON path, content, encoding utf8|base64)",
                        "GET /__wasmtest/fs/read?path=relative",
                        "GET /__wasmtest/fs/list?path=relative",
                        "DELETE /__wasmtest/fs/delete?path=relative",
                        "SignalR hub: /__wasmtest/signalr (bidirectional)"
                    }
                }).ConfigureAwait(false);
                return;
            }

            if (sub.Equals("log", StringComparison.OrdinalIgnoreCase) && method == HttpMethods.Post)
            {
                await HandleLogPost(ctx, state).ConfigureAwait(false);
                return;
            }

            if (sub.Equals("log", StringComparison.OrdinalIgnoreCase) && method == HttpMethods.Get)
            {
                var clear = ctx.Request.Query.TryGetValue("clear", out var c)
                    && string.Equals(c.ToString(), "true", StringComparison.OrdinalIgnoreCase);
                var lines = state.SnapshotLog();
                if (clear)
                    state.ClearLog();
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                await ctx.Response.WriteAsync(string.Join(Environment.NewLine, lines), ctx.RequestAborted)
                    .ConfigureAwait(false);
                return;
            }

            if (sub.Equals("fs/write", StringComparison.OrdinalIgnoreCase) && method == HttpMethods.Post)
            {
                await HandleFsWrite(ctx, state).ConfigureAwait(false);
                return;
            }

            if (sub.Equals("fs/read", StringComparison.OrdinalIgnoreCase) && method == HttpMethods.Get)
            {
                await HandleFsRead(ctx, state).ConfigureAwait(false);
                return;
            }

            if (sub.Equals("fs/list", StringComparison.OrdinalIgnoreCase) && method == HttpMethods.Get)
            {
                await HandleFsList(ctx, state).ConfigureAwait(false);
                return;
            }

            if (sub.Equals("fs/delete", StringComparison.OrdinalIgnoreCase) && method == HttpMethods.Delete)
            {
                await HandleFsDelete(ctx, state).ConfigureAwait(false);
                return;
            }

            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await ctx.Response.WriteAsync("Unknown __wasmtest route", ctx.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            await ctx.Response.WriteAsync(ex.Message, ctx.RequestAborted).ConfigureAwait(false);
        }
    }

    static async Task HandleLogPost(HttpContext ctx, WasmTestHostState state)
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync(ctx.RequestAborted).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        var ct = ctx.Request.ContentType ?? "";
        if (ct.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            var dto = WasmTestHostState.TryParseLogJson(body);
            if (dto?.Message != null)
                state.AppendLog(dto.Message);
        }
        else
        {
            state.AppendLog(body.Trim());
        }

        ctx.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    static async Task HandleFsWrite(HttpContext ctx, WasmTestHostState state)
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync(ctx.RequestAborted).ConfigureAwait(false);
        var dto = WasmTestHostState.TryParseFsWriteJson(body);
        if (dto?.Path == null || dto.Content == null)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync("JSON must include path and content", ctx.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            await state.FsWriteRelativeAsync(dto, ctx.RequestAborted).ConfigureAwait(false);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }
        catch (InvalidOperationException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync(ex.Message, ctx.RequestAborted).ConfigureAwait(false);
        }
    }

    static async Task HandleFsRead(HttpContext ctx, WasmTestHostState state)
    {
        if (!ctx.Request.Query.TryGetValue("path", out var qp) || string.IsNullOrWhiteSpace(qp.ToString()))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync("query path= is required", ctx.RequestAborted).ConfigureAwait(false);
            return;
        }

        if (!WasmTestHostState.TryResolveSandboxPath(state.SandboxRoot, qp.ToString(), out var full, out var err))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync(err, ctx.RequestAborted).ConfigureAwait(false);
            return;
        }

        if (!File.Exists(full))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ctx.Response.ContentType = "application/octet-stream";
        await ctx.Response.SendFileAsync(full, ctx.RequestAborted).ConfigureAwait(false);
    }

    static async Task HandleFsList(HttpContext ctx, WasmTestHostState state)
    {
        var rel = ctx.Request.Query.TryGetValue("path", out var qp) ? qp.ToString() : "";
        var listed = await state.FsListAsync(string.IsNullOrWhiteSpace(rel) ? "." : rel, ctx.RequestAborted)
            .ConfigureAwait(false);
        if (listed == null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await ctx.Response.WriteAsJsonAsync(new { directories = listed.Directories, files = listed.Files })
            .ConfigureAwait(false);
    }

    static async Task HandleFsDelete(HttpContext ctx, WasmTestHostState state)
    {
        if (!ctx.Request.Query.TryGetValue("path", out var qp) || string.IsNullOrWhiteSpace(qp.ToString()))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync("query path= is required", ctx.RequestAborted).ConfigureAwait(false);
            return;
        }

        try
        {
            await state.FsDeleteRelativeAsync(qp.ToString(), ctx.RequestAborted).ConfigureAwait(false);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }
        catch (InvalidOperationException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync(ex.Message, ctx.RequestAborted).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }
}
