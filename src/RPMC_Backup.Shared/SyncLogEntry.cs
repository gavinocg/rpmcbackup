using System.Text.Json.Serialization;

namespace RPMC_Backup.Shared;

public enum LogLevel
{
    Info,
    Warn,
    Error,
    Fatal
}

public class SyncLogEntry
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = string.Empty;
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("folder")] public string Folder { get; set; } = string.Empty;
    [JsonPropertyName("filename")] public string Filename { get; set; } = string.Empty;
    [JsonPropertyName("bytes")] public long Bytes { get; set; }
    [JsonPropertyName("duration_ms")] public int DurationMs { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("error_code")] public string ErrorCode { get; set; } = string.Empty;
    [JsonPropertyName("error_detail")] public string ErrorDetail { get; set; } = string.Empty;
    [JsonPropertyName("suggestion")] public string Suggestion { get; set; } = string.Empty;
}

public class SystemLogEntry
{
    public long Id { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public int Level { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
