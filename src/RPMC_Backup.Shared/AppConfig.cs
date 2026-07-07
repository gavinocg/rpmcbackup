using System.Text.Json.Serialization;

namespace RPMC_Backup.Shared;

public class AppConfig
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("admin_hash")] public string AdminHash { get; set; } = string.Empty;
    [JsonPropertyName("minio_endpoint")] public string MinioEndpoint { get; set; } = string.Empty;
    [JsonPropertyName("minio_access_key")] public string MinioAccessKey { get; set; } = string.Empty;
    [JsonPropertyName("minio_secret_key")] public string MinioSecretKey { get; set; } = string.Empty;
    [JsonPropertyName("minio_use_ssl")] public bool MinioUseSsl { get; set; }
    [JsonPropertyName("bucket")] public string BucketName { get; set; } = string.Empty;
    [JsonPropertyName("folders")] public List<FolderConfig> Folders { get; set; } = new();
    [JsonPropertyName("machine_name")] public string MachineName { get; set; } = Environment.MachineName;
    [JsonPropertyName("machine_user")] public string MachineUserName { get; set; } = Environment.UserName;
    [JsonPropertyName("admin_email")] public string AdminEmail { get; set; } = string.Empty;
    [JsonPropertyName("smtp_host")] public string SmtpHost { get; set; } = "smtp.gmail.com";
    [JsonPropertyName("smtp_port")] public int SmtpPort { get; set; } = 587;
    [JsonPropertyName("smtp_user")] public string SmtpUser { get; set; } = "mailingrpmcc@gmail.com";
    [JsonPropertyName("smtp_pass")] public string SmtpPass { get; set; } = "vtwlvgserdahpfrx";
    [JsonPropertyName("smtp_use_ssl")] public bool SmtpUseSsl { get; set; } = true;
    [JsonPropertyName("smtp_from")] public string SmtpFrom { get; set; } = "mailingrpmcc@gmail.com";
    [JsonPropertyName("sync_interval")] public int SyncInterval { get; set; } = 5;
    [JsonPropertyName("sync_interval_unit")] public string SyncIntervalUnit { get; set; } = "minutos";
    [JsonPropertyName("force_sync")] public bool ForceSync { get; set; }
    [JsonPropertyName("excluded_files")] public List<string> ExcludedFiles { get; set; } = new();
    [JsonPropertyName("s3_region")] public string S3Region { get; set; } = "us-east-1";
    [JsonPropertyName("watcher_debounce_ms")] public int WatcherDebounceMs { get; set; } = 180000;
    [JsonPropertyName("lightweight_sync_interval_ms")] public int LightweightSyncIntervalMs { get; set; } = 21600000; // 6 horas
}

public class FolderConfig
{
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("recursive")] public bool Recursive { get; set; } = true;
    [JsonPropertyName("exclude_patterns")] public List<string> ExcludePatterns { get; set; } = new();
}

public enum ServiceStatus
{
    Unknown,
    Running,
    Paused,
    Degraded,
    Error,
    Stopped
}

public class ServiceStateInfo
{
    public ServiceStatus Status { get; set; } = ServiceStatus.Unknown;
    public string LastSyncTime { get; set; } = string.Empty;
    public int Errors24h { get; set; }
    public int PendingFiles { get; set; }
    public long TotalBytesUploaded { get; set; }
    public int TotalFilesUploaded { get; set; }
    public bool IsSyncing { get; set; }
    public bool IsVerifying { get; set; }
    public int SyncProgress { get; set; }
    public string DataError { get; set; } = string.Empty;
    public string ConnectionError { get; set; } = string.Empty;
    public List<FolderProgress> FoldersProgress { get; set; } = new();
}

public class FolderProgress
{
    public string Folder { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Completed { get; set; }
}
