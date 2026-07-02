using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RPMC_Backup.Shared;

namespace RPMC_Backup.Service;

public class ConfigManager
{
    private readonly string _configPath;
    private static readonly byte[] MagicHeader = { 0x52, 0x50, 0x4D, 0x43 }; // RPMC

    public ConfigManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var dir = System.IO.Path.Combine(appData, Constants.ConfigDir);
        Directory.CreateDirectory(dir);
        _configPath = System.IO.Path.Combine(dir, Constants.ConfigFileName);
    }

    public string ConfigPath => _configPath;

    public AppConfig? Load()
    {
        if (!File.Exists(_configPath)) return null;
        try
        {
            var data = File.ReadAllBytes(_configPath);
            if (data.Length < 4 || !data.Take(4).SequenceEqual(MagicHeader)) return null;
            var json = Encoding.UTF8.GetString(data, 4, data.Length - 4);
            return JsonSerializer.Deserialize<AppConfig>(json);
        }
        catch { return null; }
    }

    public void Save(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config);
        var data = MagicHeader.Concat(Encoding.UTF8.GetBytes(json)).ToArray();
        File.WriteAllBytes(_configPath, data);
    }

    public bool ValidateAdminPassword(string password)
    {
        var config = Load();
        if (config == null || string.IsNullOrEmpty(config.AdminHash)) return false;
        return BCrypt.Net.BCrypt.Verify(password, config.AdminHash);
    }

    public bool IsConfigured()
    {
        var config = Load();
        return config != null && !string.IsNullOrEmpty(config.MinioEndpoint);
    }
}
