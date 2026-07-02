using System.IO;
using RPMC_Backup.Shared;

namespace RPMC_Backup.Service;

public class FolderWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, DateTime> _pending = new();
    private readonly object _lock = new();
    private Timer? _debounceTimer;
    private readonly Func<string, string, Task> _onChange;
    private readonly Action<int>? _onBatchStart;
    private readonly Action? _onBatchComplete;
    private bool _running;

    public FolderWatcher(List<FolderConfig> folders, Func<string, string, Task> onChange, Action<int>? onBatchStart = null, Action? onBatchComplete = null)
    {
        _onChange = onChange;
        _onBatchStart = onBatchStart;
        _onBatchComplete = onBatchComplete;
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
                InternalBufferSize = 65536,
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
        _debounceTimer?.Change(2000, Timeout.Infinite);
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

    private async void DebounceElapsed(object? state)
    {
        Dictionary<string, string> files;
        lock (_lock)
        {
            files = new Dictionary<string, string>();
            foreach (var kv in _pending)
            {
                var watcher = _watchers.FirstOrDefault(w => kv.Key.StartsWith(w.Path, StringComparison.OrdinalIgnoreCase));
                if (watcher != null)
                    files[kv.Key] = watcher.Path;
            }
            _pending.Clear();
        }

        _onBatchStart?.Invoke(files.Count);

        foreach (var (fullPath, folder) in files)
        {
            try
            {
                var cfg = new ConfigManager().Load();
                var shouldExclude = cfg?.Folders
                    .Where(f => fullPath.StartsWith(f.Path, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(f => f.ExcludePatterns)
                    .Any(p => System.IO.Path.GetExtension(fullPath).Equals(p.TrimStart('*'), StringComparison.OrdinalIgnoreCase)) ?? false;
                if (shouldExclude) continue;

                await _onChange(folder, fullPath);
            }
            catch { }
        }
        _onBatchComplete?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        foreach (var w in _watchers) { w.EnableRaisingEvents = false; w.Dispose(); }
        _debounceTimer?.Dispose();
    }
}
