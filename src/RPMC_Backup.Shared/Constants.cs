namespace RPMC_Backup.Shared;

public static class Constants
{
    public const string AppName = "RPMC Backup";
    public const string ServiceName = "rpmc-backup-service";
    public const string DisplayName = "RPMC Backup Service";
    public const string Description = "Real-time backup service for MinIO AIStor";
    public const string PipeName = "RPMCBackup";
    public const int IpcPort = 51999;
    public const string ConfigDir = "RPMC\\Backup";
    public const string ConfigFileName = "config.dat";
    public const string LogsDbName = "logs.db";
    public const int DebounceMs = 5000;
    public const int FullSyncHours = 24;
    public const int LogRetentionDays = 90;
    public const int WatchdogIntervalMs = 10000;
    public const int MaxStopAttempts = 3;
    public const int StopLockoutMinutes = 5;
    public const int TrayPollIntervalMs = 3000;
    public const int PipeConnectTimeoutMs = 3000;
    public const string PinFileName = "reset_pin.dat";
    public const int PinExpiryMinutes = 30;

    public const string CmdGetStatus = "GET_STATUS";
    public const string CmdPause = "PAUSE";
    public const string CmdResume = "RESUME";
    public const string CmdReconfig = "RECONFIG";
    public const string CmdStop = "STOP";
    public const string CmdRetry = "RETRY";
    public const string CmdSyncNow = "SYNC_NOW";
    public const string CmdClearLogs = "CLEAR_LOGS";
    public const string CmdClearSysLogs = "CLEAR_SYS_LOGS";
    public const string CmdExcludeFile = "EXCLUDE_FILE";
    public const string CmdIncludeFile = "INCLUDE_FILE";
}
