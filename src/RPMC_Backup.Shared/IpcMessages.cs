using System.Text.Json.Serialization;

namespace RPMC_Backup.Shared;

public class IpcRequest
{
    [JsonPropertyName("command")] public string Command { get; set; } = string.Empty;
    [JsonPropertyName("payload")] public string Payload { get; set; } = string.Empty;
}

public class IpcResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("state")] public ServiceStateInfo? State { get; set; }
}
