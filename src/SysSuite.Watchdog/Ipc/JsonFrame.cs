using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysSuite.Watchdog.Ipc;

public sealed record JsonFrame(
    string Type,
    string RequestId,
    string? Payload,
    int TimeoutSeconds = 30,
    string? Reason = null)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = JsonFrameJsonContext.Default
    };

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    public static JsonFrame? Deserialize(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<JsonFrame>(value, Options);
}

[JsonSerializable(typeof(JsonFrame))]
public sealed partial class JsonFrameJsonContext : JsonSerializerContext;
