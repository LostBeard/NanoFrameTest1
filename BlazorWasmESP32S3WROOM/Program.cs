// Optional: <see cref="WasmTestDebugHubClient"/> connects to the Playwright test hub when present; otherwise it stays disconnected.
using BlazorWasmESP32S3WROOM;
using BlazorWasmESP32S3WROOM.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SpawnDev.BlazorJS;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddBlazorJSRuntime();
builder.Services.AddScoped<BleDeviceService>();
builder.Services.AddSingleton<WasmTestDebugHubClient>();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

await builder.Build().BlazorJSRunAsync();
