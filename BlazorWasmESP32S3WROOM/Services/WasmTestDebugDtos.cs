using System.Text.Json.Serialization;

namespace BlazorWasmESP32S3WROOM.Services;

/// <summary>JSON shape returned by hub method <c>GetInfo</c> (test host).</summary>
public sealed class WasmDebugHubInfoClientDto
{
    [JsonPropertyName("connectionId")]
    public string ConnectionId { get; set; } = "";

    [JsonPropertyName("sandboxRoot")]
    public string SandboxRoot { get; set; } = "";
}

public sealed class WasmFsListClientDto
{
    [JsonPropertyName("directories")]
    public string[] Directories { get; set; } = [];

    [JsonPropertyName("files")]
    public string[] Files { get; set; } = [];
}
