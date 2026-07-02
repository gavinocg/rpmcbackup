using Microsoft.Data.Sqlite;
using RPMC_Backup.Shared;

namespace RPMC_Backup.Service;

public class LogDatabase : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public LogDatabase()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var dir = System.IO.Path.Combine(appData, Constants.ConfigDir);
        Directory.CreateDirectory(dir);
        _dbPath = System.IO.Path.Combine(dir, Constants.LogsDbName);
        Initialize();
    }

    private void Initialize()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS sync_logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                level INTEGER NOT NULL,
                folder TEXT,
                filename TEXT,
                bytes INTEGER DEFAULT 0,
                duration_ms INTEGER DEFAULT 0,
                message TEXT,
                error_code TEXT,
                error_detail TEXT,
                suggestion TEXT
            );
            CREATE TABLE IF NOT EXISTS system_logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                level INTEGER NOT NULL,
                source TEXT,
                message TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_sync_ts ON sync_logs(timestamp);
            CREATE INDEX IF NOT EXISTS idx_sys_ts ON system_logs(timestamp);
        ";
        cmd.ExecuteNonQuery();
    }

    public void Insert(SyncLogEntry entry)
    {
        try
        {
            if (_connection == null) return;
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO sync_logs (timestamp, level, folder, filename, bytes, duration_ms, message, error_code, error_detail, suggestion)
                VALUES ($ts, $level, $folder, $file, $bytes, $dur, $msg, $err, $detail, $suggest)
            ";
            cmd.Parameters.AddWithValue("$ts", entry.Timestamp);
            cmd.Parameters.AddWithValue("$level", entry.Level);
            cmd.Parameters.AddWithValue("$folder", entry.Folder);
            cmd.Parameters.AddWithValue("$file", entry.Filename);
            cmd.Parameters.AddWithValue("$bytes", entry.Bytes);
            cmd.Parameters.AddWithValue("$dur", entry.DurationMs);
            cmd.Parameters.AddWithValue("$msg", entry.Message);
            cmd.Parameters.AddWithValue("$err", entry.ErrorCode);
            cmd.Parameters.AddWithValue("$detail", entry.ErrorDetail);
            cmd.Parameters.AddWithValue("$suggest", entry.Suggestion);
            cmd.ExecuteNonQuery();
            PurgeOldLogs();
        }
        catch { }
    }

    public void InsertSystem(SystemLogEntry entry)
    {
        try
        {
            if (_connection == null) return;
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO system_logs (timestamp, level, source, message) VALUES ($ts, $level, $src, $msg)";
            cmd.Parameters.AddWithValue("$ts", entry.Timestamp);
            cmd.Parameters.AddWithValue("$level", entry.Level);
            cmd.Parameters.AddWithValue("$src", entry.Source);
            cmd.Parameters.AddWithValue("$msg", entry.Message);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    public void PurgeOldLogs()
    {
        try
        {
            if (_connection == null) return;
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM sync_logs WHERE timestamp < datetime('now', '-{Constants.LogRetentionDays} days')";
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    public void ClearSyncLogs()
    {
        try { if (_connection != null) { using var c = _connection.CreateCommand(); c.CommandText = "DELETE FROM sync_logs"; c.ExecuteNonQuery(); } } catch { }
    }

    public void ClearSystemLogs()
    {
        try { if (_connection != null) { using var c = _connection.CreateCommand(); c.CommandText = "DELETE FROM system_logs"; c.ExecuteNonQuery(); } } catch { }
    }

    public int GetErrorCount24h()
    {
        try
        {
            if (_connection == null) return 0;
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sync_logs WHERE level >= 2 AND timestamp >= datetime('now', '-1 day')";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        catch { return 0; }
    }

    public List<SyncLogEntry> QuerySync(string? levelFilter = null, string? search = null, int limit = 500)
    {
        var results = new List<SyncLogEntry>();
        try
        {
            if (_connection == null) return results;
            var sql = "SELECT id, timestamp, level, folder, filename, bytes, duration_ms, message, error_code, error_detail, suggestion FROM sync_logs WHERE 1=1";
            if (!string.IsNullOrEmpty(levelFilter) && levelFilter != "-1")
                sql += $" AND level = {levelFilter}";
            if (!string.IsNullOrEmpty(search))
                sql += " AND (filename LIKE $search OR message LIKE $search)";
            sql += " ORDER BY id DESC LIMIT $limit";

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            if (!string.IsNullOrEmpty(search)) cmd.Parameters.AddWithValue("$search", $"%{search}%");
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new SyncLogEntry
                {
                    Id = reader.GetInt64(0),
                    Timestamp = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Level = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    Folder = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Filename = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Bytes = reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                    DurationMs = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                    Message = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    ErrorCode = reader.IsDBNull(8) ? "" : reader.GetString(8),
                    ErrorDetail = reader.IsDBNull(9) ? "" : reader.GetString(9),
                    Suggestion = reader.IsDBNull(10) ? "" : reader.GetString(10)
                });
            }
        }
        catch { }
        return results;
    }

    public List<SystemLogEntry> QuerySystem(string? levelFilter = null, int limit = 500)
    {
        var results = new List<SystemLogEntry>();
        try
        {
            if (_connection == null) return results;
            var sql = "SELECT id, timestamp, level, source, message FROM system_logs WHERE 1=1";
            if (!string.IsNullOrEmpty(levelFilter) && levelFilter != "-1")
                sql += $" AND level = {levelFilter}";
            sql += " ORDER BY id DESC LIMIT $limit";

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new SystemLogEntry
                {
                    Id = reader.GetInt64(0),
                    Timestamp = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Level = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    Source = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Message = reader.IsDBNull(4) ? "" : reader.GetString(4)
                });
            }
        }
        catch { }
        return results;
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
    }
}
