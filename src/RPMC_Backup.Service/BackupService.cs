using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RPMC_Backup.Shared;

namespace RPMC_Backup.Service;

public class BackupService : BackgroundService
{
    private static readonly HttpClient _hc = new();
    private readonly ConfigManager _config;
    private readonly LogDatabase _logDb;
    private readonly ILogger<BackupService> _logger;
    private FolderWatcher? _watcher;
    private MinioUploader? _uploader;
    private CancellationTokenSource? _pauseCts;

    private volatile ServiceStatus _status = ServiceStatus.Unknown;
    private int _errors24h;
    private long _totalBytes;
    private int _totalFiles;
    private DateTime _lastSyncTime = DateTime.MinValue;
    private readonly Queue<string> _retryQueue = new();
    private volatile bool _isSyncing;
    private volatile bool _isVerifying;
    private int _syncTotal;
    private int _syncCompleted;
    private bool _initialSyncDone;
    private volatile string _dataError = string.Empty;
    private volatile string _connectionError = string.Empty;
    private int _healthCheckCounter;
    private HashSet<string> _excludedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _folderTotal = new();
    private readonly Dictionary<string, int> _folderCompleted = new();

    public ServiceStateInfo GetState()
    {
        return new ServiceStateInfo
        {
            Status = _status,
            LastSyncTime = _lastSyncTime > DateTime.MinValue ? _lastSyncTime.ToString("yyyy-MM-dd HH:mm:ss") : "",
            Errors24h = _errors24h,
            PendingFiles = _retryQueue.Count + (_isSyncing ? _syncTotal - _syncCompleted : 0),
            TotalBytesUploaded = _totalBytes,
            TotalFilesUploaded = _totalFiles,
            IsSyncing = _isSyncing,
            IsVerifying = _isVerifying,
            SyncProgress = _syncTotal > 0 ? Math.Min((int)(100.0 * _syncCompleted / _syncTotal), 100) : (_isSyncing ? 0 : 100),
            DataError = _dataError,
            ConnectionError = _connectionError,
            FoldersProgress = _folderTotal.Select(kv => new FolderProgress
            {
                Folder = kv.Key,
                Total = kv.Value,
                Completed = _folderCompleted.TryGetValue(kv.Key, out var c) ? c : 0
            }).ToList()
        };
    }

    public BackupService(ConfigManager config, LogDatabase logDb, ILogger<BackupService> logger)
    {
        _config = config;
        _logDb = logDb;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RPMC Backup Service starting...");
                _logDb.InsertSystem(new SystemLogEntry { Timestamp = DateTime.Now.ToString("o"), Level = 0, Source = "Service", Message = "Servicio iniciado." });
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RPMC Backup Service stopping...");
        LogSystem(0, "Servicio deteniéndose...");
        _watcher?.Dispose();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = StartIpcServerAsync(stoppingToken);
        _ = SyncLoopAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = _config.Load();
                if (config == null || string.IsNullOrEmpty(config.MinioEndpoint))
                {
                    _status = ServiceStatus.Degraded;
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                _uploader = new MinioUploader(config);
                _status = ServiceStatus.Running;

                _watcher?.Stop();
                _watcher = new FolderWatcher(config.Folders, async (folder, filename) =>
                {
                    await OnFileChanged(folder, filename, stoppingToken);
                }, batchSize => OnBatchStart(batchSize), () => _isSyncing = false, config.WatcherDebounceMs);
                _watcher.Start();

                var cfgEx = _config.Load();
                if (cfgEx?.ExcludedFiles != null)
                    _excludedFiles = new HashSet<string>(cfgEx.ExcludedFiles, StringComparer.OrdinalIgnoreCase);

                _ = ProcessRetryQueueAsync(stoppingToken);

                if (!_initialSyncDone)
                {
                    _initialSyncDone = true;
                    _ = Task.Run(async () => await RunInitialFullSync(config, stoppingToken), stoppingToken);
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(5000, stoppingToken);
                    var current = _config.Load();
                    if (current == null || string.IsNullOrEmpty(current.MinioEndpoint))
                        break;
                    _healthCheckCounter++;
                    var missing = new List<string>();
                    foreach (var f in current.Folders ?? new())
                    {
                        if (!Directory.Exists(f.Path)) missing.Add(f.Path);
                    }
                    if (missing.Count > 0)
                    {
                        _dataError = string.Join(", ", missing);
                        _status = ServiceStatus.Error;
                    }
                    else if (_dataError.Length > 0)
                    {
                        LogSystem(2, $"Orígenes restaurados: todos los directorios están accesibles nuevamente.");
                        _dataError = string.Empty;
                    }

                    if (_healthCheckCounter % 6 == 0 && current.Folders.Count > 0)
                    {
                        try
                        {
                            var protocol = current.MinioUseSsl ? "https" : "http";
                            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                            cts.CancelAfter(2000);
                            var resp = await _hc.GetAsync($"{protocol}://{current.MinioEndpoint}/", cts.Token);
                            if (_connectionError.Length > 0)
                            {
                                LogSystem(2, $"Conexión con el servidor S3 restaurada.");
                                _connectionError = string.Empty;
                            }
                        }
                        catch
                        {
                            _connectionError = current.MinioEndpoint;
                        }
                    }

                    if (_dataError.Length > 0 || _connectionError.Length > 0)
                        _status = ServiceStatus.Error;
                    else if (_status == ServiceStatus.Error || _status == ServiceStatus.Degraded)
                        _status = ServiceStatus.Running;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Service loop error");
                _status = ServiceStatus.Error;
                LogSystem(2, $"Error en servicio: {ex.Message}");
                try
                {
                    _logDb.Insert(new SyncLogEntry
                    {
                        Timestamp = DateTime.Now.ToString("o"),
                        Level = (int)RPMC_Backup.Shared.LogLevel.Fatal,
                        Message = $"Service error: {ex.Message}",
                        ErrorDetail = ex.ToString()
                    });
                }
                catch { }
                await Task.Delay(10000, stoppingToken);
            }
        }
    }

    private async Task RunInitialFullSync(AppConfig config, CancellationToken ct)
    {
        _logger.LogInformation("Initial sync started...");
        var prefix = $"{config.MachineName}/{config.MachineUserName}/";
        var totalProcessed = 0;
        var folderFileMap = new Dictionary<string, List<string>>();

        // Initialize progress for all configured folders
        _folderTotal.Clear();
        _folderCompleted.Clear();
        foreach (var f in config.Folders ?? new())
        {
            _folderTotal[f.Path] = 0;
            _folderCompleted[f.Path] = 0;
        }

        // Phase 1: Verify all folders against bucket
        _isVerifying = true;
        _logger.LogInformation("Phase 1: Verifying destination for all folders...");
        foreach (var folder in config.Folders ?? new())
        {
            if (!Directory.Exists(folder.Path)) continue;
            var folderName = Path.GetFileName(folder.Path.TrimEnd('\\', '/'));
            LogSystem(0, $"Verificando destino: {folderName}");

            var searchOpt = folder.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            _logger.LogInformation($"Scanning {folder.Path} (Recursive={folder.Recursive})");
            var files = Directory.GetFiles(folder.Path, "*", searchOpt).Where(f =>
                !IsExcluded(f, folder.ExcludePatterns)).ToList();
            _logger.LogInformation($"Found {files.Count} files in {folder.Path}");

            Dictionary<string, DateTime>? existing = null;
            try
            {
                var folderPrefix = $"{prefix}{folderName}/";
                _logger.LogInformation($"Verifying destination for {folderName} with prefix: {folderPrefix}");
                var folderObjects = await _uploader.ListExistingObjectsAsync(folderPrefix, ct);
                if (folderObjects.Count > 0)
                    existing = folderObjects;
                _logger.LogInformation($"Destination check for {folderName}: {(existing != null ? $"{folderObjects.Count} objects found (differential)" : "empty (full sync)")}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not verify destination for {folderName}: {ex.Message}. Proceeding with full sync.");
                LogSystem(1, $"Error verificando destino {folderName}: {ex.Message}");
            }

            // Build filtered file list for this folder
            var folderFiles = new List<string>();
            foreach (var file in files)
            {
                if (new FileInfo(file).Length == 0) continue;
                if (existing != null)
                {
                    var relPath = file.Substring(folder.Path.Length).TrimStart('\\', '/').Replace('\\', '/');
                    var s3Key = $"{prefix}{folderName}/{relPath}";
                    if (existing.TryGetValue(s3Key, out var bucketTs))
                    {
                        var localTs = File.GetLastWriteTimeUtc(file);
                        if (localTs <= bucketTs)
                            continue;
                    }
                }
                folderFiles.Add(file);
            }

            folderFileMap[folder.Path] = folderFiles;
            _folderTotal[folder.Path] = folderFiles.Count;
            LogSystem(0, $"Origen {folderName}: {folderFiles.Count} archivos pendientes.");
        }

        _isVerifying = false;

        // Phase 2: Sync all folders
        _logger.LogInformation("Phase 2: Syncing files...");
        foreach (var kv in folderFileMap)
        {
            var folderPath = kv.Key;
            var folderFiles = kv.Value;
            var folderName = Path.GetFileName(folderPath.TrimEnd('\\', '/'));

            if (folderFiles.Count == 0) continue;

            _isSyncing = true;
            _syncTotal = folderFiles.Count;
            _syncCompleted = 0;

            foreach (var file in folderFiles)
            {
                if (ct.IsCancellationRequested || _status is ServiceStatus.Stopped or ServiceStatus.Error) break;
                await OnFileChanged(folderPath, file, ct);
                _syncCompleted++;
                _folderCompleted[folderPath] = _syncCompleted;
                totalProcessed++;
            }

            _isSyncing = false;
            LogSystem(0, $"Origen {folderName}: {folderFiles.Count} archivos sincronizados.");
        }

        _logger.LogInformation($"Initial sync completed: {totalProcessed} files across {folderFileMap.Count} folders.");
    }

    private void LogSystem(int level, string message)
    {
        _logDb.InsertSystem(new SystemLogEntry { Timestamp = DateTime.Now.ToString("o"), Level = level, Source = "Service", Message = message });
    }

    private void OnBatchStart(int batchSize)
    {
        if (batchSize > 0 && !_isSyncing)
        {
            _isSyncing = true;
            _syncTotal = batchSize;
            _syncCompleted = 0;
        }
    }

    private async Task StartIpcServerAsync(CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            var listener = new TcpListener(IPAddress.Loopback, Constants.IpcPort);
            try
            {
                listener.Start();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start IPC listener");
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync(ct);
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream);
                    using var writer = new StreamWriter(stream) { AutoFlush = true };
                    var line = await reader.ReadLineAsync(ct);
                    if (line != null)
                    {
                        var request = JsonSerializer.Deserialize<IpcRequest>(line);
                        var response = HandleIpcCommand(request);
                        await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "IPC error");
                    try { await Task.Delay(1000, ct); } catch { break; }
                }
            }
            listener.Stop();
        }, ct);
    }

    private IpcResponse HandleIpcCommand(IpcRequest? request)
    {
        if (request == null)
            return new IpcResponse { Success = false, Message = "Invalid request" };

        switch (request.Command)
        {
            case Constants.CmdGetStatus:
                return new IpcResponse { Success = true, State = GetState() };

            case Constants.CmdPause:
                _pauseCts?.Cancel();
                _pauseCts = new CancellationTokenSource();
                _status = ServiceStatus.Paused;
                _watcher?.Stop();
                _isSyncing = false;
                LogSystem(0, "Respaldo pausado por el usuario.");
                return new IpcResponse { Success = true, Message = "Backup paused" };

            case Constants.CmdResume:
                _pauseCts?.Cancel();
                _pauseCts = null;
                _status = ServiceStatus.Running;
                _watcher?.Start();
                LogSystem(0, "Respaldo reanudado por el usuario.");
                return new IpcResponse { Success = true, Message = "Backup resumed" };

            case Constants.CmdReconfig:
                var c = _config.Load();
                if (c != null)
                {
                    _watcher?.Stop();
                    (_watcher as IDisposable)?.Dispose();
                    _uploader = new MinioUploader(c);
                    _watcher = new FolderWatcher(c.Folders, async (f, fn) => await OnFileChanged(f, fn, CancellationToken.None), batchSize => OnBatchStart(batchSize), () => _isSyncing = false, c.WatcherDebounceMs);
                    _watcher.Start();
                    _ = Task.Run(async () => await RunInitialFullSync(c, CancellationToken.None));
                    LogSystem(0, "Configuración recargada.");
                }
                return new IpcResponse { Success = true, Message = "Configuration reloaded" };

            case Constants.CmdSyncNow:
                _ = Task.Run(async () => await RunSyncNowAsync(CancellationToken.None));
                LogSystem(0, "Sincronización manual solicitada.");
                return new IpcResponse { Success = true, Message = "Sync started" };

            case Constants.CmdClearLogs:
                _logDb.ClearSyncLogs();
                _logDb.Insert(new SyncLogEntry
                {
                    Timestamp = DateTime.Now.ToString("o"),
                    Level = (int)RPMC_Backup.Shared.LogLevel.Info,
                    Message = $"Logs eliminados {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                });
                return new IpcResponse { Success = true, Message = "Logs cleared" };

            case Constants.CmdClearSysLogs:
                _logDb.ClearSystemLogs();
                _logDb.InsertSystem(new SystemLogEntry { Timestamp = DateTime.Now.ToString("o"), Level = 0, Source = "Service", Message = "Logs de sistema eliminados." });
                return new IpcResponse { Success = true, Message = "System logs cleared" };

            case Constants.CmdExcludeFile:
                var exclPath2 = request.Payload;
                if (!string.IsNullOrEmpty(exclPath2))
                {
                    _excludedFiles.Add(exclPath2);
                    var cfg2 = _config.Load();
                    if (cfg2 != null)
                    {
                        cfg2.ExcludedFiles = _excludedFiles.ToList();
                        _config.Save(cfg2);
                    }
                }
                return new IpcResponse { Success = true, Message = "File excluded" };

            case Constants.CmdIncludeFile:
                var inclPath2 = request.Payload;
                if (!string.IsNullOrEmpty(inclPath2))
                {
                    _excludedFiles.Remove(inclPath2);
                    var cfg3 = _config.Load();
                    if (cfg3 != null)
                    {
                        cfg3.ExcludedFiles = _excludedFiles.ToList();
                        _config.Save(cfg3);
                    }
                }
                return new IpcResponse { Success = true, Message = "File included" };

            case Constants.CmdStop:
                _logger.LogWarning("Service stop requested via IPC");
                _status = ServiceStatus.Stopped;
                _watcher?.Stop();
                _isSyncing = false;
                LogSystem(1, "Servicio detenido por el usuario.");
                return new IpcResponse { Success = true, Message = "Service stopped" };

            default:
                if (request.Command.StartsWith(Constants.CmdRetry + ":"))
                {
                    var fileId = request.Command.Substring((Constants.CmdRetry + ":").Length);
                    lock (_retryQueue) _retryQueue.Enqueue(fileId);
                    return new IpcResponse { Success = true, Message = "File queued for retry" };
                }
                if (request.Command == Constants.CmdRetry)
                {
                    lock (_retryQueue) _retryQueue.Enqueue(request.Payload);
                    return new IpcResponse { Success = true, Message = "File queued for retry" };
                }
                return new IpcResponse { Success = false, Message = "Unknown command" };
        }
    }

    private async Task OnFileChanged(string folder, string filename, CancellationToken ct)
    {
        if (_uploader == null) return;
        if (_status != ServiceStatus.Running && _status != ServiceStatus.Degraded) return;
        var filePath = System.IO.Path.Combine(folder, filename);
        if (_excludedFiles.Contains(filePath)) return;
        if (!File.Exists(filePath)) return;

        if (IsFileLocked(filePath))
        {
            lock (_retryQueue) _retryQueue.Enqueue(filePath);
            _logger.LogInformation("File in use, queued for retry: {File}", filename);
            _logDb.Insert(new SyncLogEntry
            {
                Timestamp = DateTime.Now.ToString("o"),
                Level = (int)RPMC_Backup.Shared.LogLevel.Warn,
                Folder = folder,
                Filename = filename,
                Message = "Archivo en uso, se reintentará automáticamente."
            });
            return;
        }

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var cfg = _config.Load();
            var folderName = Path.GetFileName(folder.TrimEnd('\\', '/'));
            var relativePath = filename.Substring(folder.TrimEnd('\\', '/').Length).TrimStart('\\', '/');
            var objectName = $"{cfg?.MachineName ?? "unknown"}/{cfg?.MachineUserName ?? "unknown"}/{folderName}/{relativePath.Replace('\\', '/')}";
            var bytes = new FileInfo(filePath).Length;

            await _uploader.UploadAsync(objectName, filePath, ct);
            sw.Stop();

            _totalBytes += bytes;
            _totalFiles++;
            _lastSyncTime = DateTime.Now;

            _logDb.Insert(new SyncLogEntry
            {
                Timestamp = DateTime.Now.ToString("o"),
                Level = (int)RPMC_Backup.Shared.LogLevel.Info,
                Folder = folder,
                Filename = filename,
                Bytes = bytes,
                DurationMs = (int)sw.ElapsedMilliseconds,
                Message = $"Uploaded {FormatBytes(bytes)} ({sw.ElapsedMilliseconds}ms)"
            });
        }
        catch (Exception ex)
        {
            _errors24h++;
            _logger.LogWarning(ex, "Upload failed: {File}", filename);

            _logDb.Insert(new SyncLogEntry
            {
                Timestamp = DateTime.Now.ToString("o"),
                Level = (int)RPMC_Backup.Shared.LogLevel.Error,
                Folder = folder,
                Filename = filename,
                Message = $"Upload failed: {ex.Message}",
                ErrorCode = ex.GetType().Name,
                ErrorDetail = ex.ToString(),
                Suggestion = GetSuggestion(ex)
            });
        }
    }

    private static bool IsFileLocked(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private async Task RunSyncNowAsync(CancellationToken ct)
    {
        if (_status != ServiceStatus.Running || _uploader == null) return;
        _logger.LogInformation("Manual sync triggered...");
        var config = _config.Load();
        if (config == null) return;

        var cutoff = _lastSyncTime > DateTime.MinValue ? _lastSyncTime : DateTime.MinValue;
        var fileList = new List<(string folder, string file)>();
        foreach (var folder in config.Folders ?? new())
        {
            if (!Directory.Exists(folder.Path)) continue;
            var files = Directory.GetFiles(folder.Path, "*", folder.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) return;
                if (new FileInfo(file).Length == 0) continue;
                if (IsExcluded(file, folder.ExcludePatterns)) continue;
                if (_lastSyncTime > DateTime.MinValue && File.GetLastWriteTimeUtc(file) <= cutoff.ToUniversalTime()) continue;
                fileList.Add((folder.Path, file));
            }
        }

        if (fileList.Count == 0) { _logger.LogInformation("Manual sync: no new files to sync."); return; }

        _isSyncing = true;
        _syncTotal = fileList.Count;
        _syncCompleted = 0;
        _folderTotal.Clear();
        _folderCompleted.Clear();
        foreach (var f in config.Folders ?? new())
        {
            _folderTotal[f.Path] = 0;
            _folderCompleted[f.Path] = 0;
        }
        foreach (var (f, _) in fileList)
        {
            if (!_folderTotal.ContainsKey(f)) _folderTotal[f] = 0;
            _folderTotal[f]++;
            _folderCompleted.TryAdd(f, 0);
        }
        foreach (var (folder, file) in fileList)
        {
            if (ct.IsCancellationRequested || _status is ServiceStatus.Stopped or ServiceStatus.Error) break;
            await OnFileChanged(folder, file, ct);
            _syncCompleted++;
            if (_folderCompleted.ContainsKey(folder)) _folderCompleted[folder]++;
        }
        _isSyncing = false;
        LogSystem(0, $"Sincronización manual completada: {_syncCompleted} archivos.");
        _logger.LogInformation($"Manual sync completed: {_syncCompleted} files.");
    }

    private async Task ProcessRetryQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10000, ct);
                string? filePath;
                lock (_retryQueue)
                {
                    if (_retryQueue.Count == 0) continue;
                    filePath = _retryQueue.Dequeue();
                }
                if (filePath != null && File.Exists(filePath) && !_excludedFiles.Contains(filePath))
                {
                    var folder = Path.GetDirectoryName(filePath) ?? "";
                    await OnFileChanged(folder, filePath, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private async Task SyncLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var cfg = _config.Load();
                var delay = cfg?.SyncInterval ?? 5;
                var unit = cfg?.SyncIntervalUnit ?? "minutos";
                var interval = unit switch
                {
                    "horas" => TimeSpan.FromHours(delay),
                    "días" => TimeSpan.FromDays(delay),
                    _ => TimeSpan.FromMinutes(delay)
                };
                await Task.Delay(interval, ct);
                if (_status != ServiceStatus.Running || _watcher == null || _uploader == null) continue;

                var config = _config.Load();
                if (config == null) continue;
                if (!config.ForceSync) continue;

                _logger.LogInformation("Starting scheduled sync...");

                var cutoff = _lastSyncTime > DateTime.MinValue ? _lastSyncTime : DateTime.MinValue;
                var fileList = new List<(string folder, string file)>();
                foreach (var folder in config.Folders ?? new())
                {
                    if (!Directory.Exists(folder.Path)) continue;
                    var files = Directory.GetFiles(folder.Path, "*", folder.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        if (ct.IsCancellationRequested) return;
                        if (new FileInfo(file).Length == 0) continue;
                        if (IsExcluded(file, folder.ExcludePatterns)) continue;
                        if (_lastSyncTime > DateTime.MinValue && File.GetLastWriteTimeUtc(file) <= cutoff.ToUniversalTime()) continue;
                        fileList.Add((folder.Path, file));
                    }
                }

                if (fileList.Count == 0) { _logger.LogInformation("Scheduled sync: no new files to sync."); continue; }

                _isSyncing = true;
                _syncTotal = fileList.Count;
                _syncCompleted = 0;
                _folderTotal.Clear();
                _folderCompleted.Clear();
                foreach (var (f, _) in fileList)
                {
                    if (!_folderTotal.ContainsKey(f)) _folderTotal[f] = 0;
                    _folderTotal[f]++;
                    _folderCompleted.TryAdd(f, 0);
                }

                foreach (var (folder, file) in fileList)
                {
                    if (ct.IsCancellationRequested || _status is ServiceStatus.Stopped or ServiceStatus.Error) break;
                    await OnFileChanged(folder, file, ct);
                    _syncCompleted++;
                    if (_folderCompleted.ContainsKey(folder)) _folderCompleted[folder]++;
                }

                _isSyncing = false;
                _logger.LogInformation($"Scheduled sync completed: {_syncCompleted} files.");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Sync error"); }
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }

    private static bool IsExcluded(string filePath, List<string> excludePatterns)
    {
        if (excludePatterns == null || excludePatterns.Count == 0) return false;
        foreach (var pattern in excludePatterns)
        {
            if (string.IsNullOrEmpty(pattern)) continue;
            if (pattern.StartsWith("*."))
            {
                var ext = pattern.TrimStart('*');
                if (System.IO.Path.GetExtension(filePath).Equals(ext, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                var searchStr = "\\" + pattern.TrimEnd('\\', '/') + "\\";
                if (filePath.IndexOf(searchStr, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (filePath.EndsWith("\\" + pattern.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    return true;
                if (filePath.IndexOf("\\" + pattern.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var remainder = filePath.Substring(filePath.IndexOf("\\" + pattern.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) + pattern.Length + 1);
                    if (remainder.Length == 0 || remainder.StartsWith("\\"))
                        return true;
                }
            }
        }
        return false;
    }

    private static string GetSuggestion(Exception ex)
    {
        var msg = ex.Message.ToLower();
        if (msg.Contains("file is being used") || msg.Contains("file is open"))
            return "El archivo está abierto en otro programa. Ciérrelo o agréguelo a los exclude patterns.";
        if (msg.Contains("connection") || msg.Contains("timeout") || msg.Contains("network"))
            return "Error de conexión con MinIO. Verifique que el servidor esté accesible.";
        if (msg.Contains("access") || msg.Contains("forbidden") || msg.Contains("unauthorized"))
            return "Credenciales de MinIO inválidas. Verifique access key y secret key.";
        if (msg.Contains("not found") || msg.Contains("bucket"))
            return "El bucket no existe. Verifique el nombre del bucket en la configuración.";
        return "Error inesperado. Revise el detalle técnico para más información.";
    }
}

