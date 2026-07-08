using System.IO;
using RPMC_Backup.Shared;

namespace RPMC_Backup.Service;

public class FolderWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, DateTime> _pending = new();
    private readonly object _lock = new();
    private Timer? _debounceTimer;
    private readonly Func<string, Task>? _onFolderSync;
    private readonly Action<string>? _logger;
    private readonly int _debounceMs;
    private bool _running;
    private DateTime _lastDebounceStart = DateTime.MinValue;
    private bool _debounceActive;

    public FolderWatcher(List<FolderConfig> folders, Func<string, Task>? onFolderSync = null, int debounceMs = 180000, Action<string>? logger = null)
    {
        _onFolderSync = onFolderSync;
        _debounceMs = debounceMs;
        _logger = logger;
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder.Path))
            {
                Directory.CreateDirectory(folder.Path);
            }
            var watcher = new FileSystemWatcher(folder.Path)
            {
                IncludeSubdirectories = folder.Recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 1048576,
                EnableRaisingEvents = false
            };
            watcher.Created += OnChanged;
            watcher.Changed += OnChanged;
            watcher.Renamed += OnChanged;
            watcher.Error += OnWatcherError;
            _watchers.Add(watcher);
        }
    }

    public void Start()
    {
        _running = true;
        foreach (var w in _watchers) w.EnableRaisingEvents = true;
        _debounceTimer = new Timer(DebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Stop()
    {
        _running = false;
        foreach (var w in _watchers) w.EnableRaisingEvents = false;
        _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (!_running) return;
        if (e.Name == null) return;

        lock (_lock)
        {
            _pending[e.FullPath] = DateTime.UtcNow;
        }
        _debounceTimer?.Change(_debounceMs, Timeout.Infinite);
        _lastDebounceStart = DateTime.UtcNow;
        _debounceActive = true;
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        try
        {
            var watcher = (FileSystemWatcher)sender;
            watcher.EnableRaisingEvents = false;
            watcher.EnableRaisingEvents = true;
        }
        catch { }
    }

    public int ConfiguredMs => _debounceMs;
    public bool DebounceActive => _debounceActive;
    public int GetRemainingMs()
    {
        if (!_debounceActive) return 0;
        var elapsed = (int)(DateTime.UtcNow - _lastDebounceStart).TotalMilliseconds;
        return Math.Max(0, _debounceMs - elapsed);
    }

    private async void DebounceElapsed(object? state)
    {
        _debounceActive = false;
        _logger?.Invoke($"[FolderWatcher] DebounceElapsed triggered, pending={_pending.Count}");
        HashSet<string> affectedFolders;
        lock (_lock)
        {
            affectedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _pending)
            {
                var watcher = _watchers.FirstOrDefault(w => kv.Key.StartsWith(w.Path, StringComparison.OrdinalIgnoreCase));
                if (watcher != null)
                    affectedFolders.Add(watcher.Path);
            }
            _pending.Clear();
        }

        if (_onFolderSync == null) { _logger?.Invoke("[FolderWatcher] _onFolderSync is NULL, aborting"); return; }

        foreach (var folder in affectedFolders)
        {
            try
            {
                _logger?.Invoke($"[FolderWatcher] Syncing folder: {folder}");
                await _onFolderSync(folder);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[FolderWatcher] Error syncing folder {folder}: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        Stop();
        foreach (var w in _watchers) { w.EnableRaisingEvents = false; w.Dispose(); }
        _debounceTimer?.Dispose();
    }
}
