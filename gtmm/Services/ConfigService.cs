using System;
using System.IO;
using System.Text.Json;
using gtmm.Models;

namespace gtmm.Services;

public class ConfigService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "gtmm", "config.json"
    );
    private static readonly string PreviousConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "melonmanager", "config.json"
    );

    public AppConfig Config { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded != null)
                {
                    Config = loaded;
                    return;
                }
            }
            else if (File.Exists(PreviousConfigPath))
            {
                string json = File.ReadAllText(PreviousConfigPath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded != null)
                {
                    Config = loaded;
                    Save();
                    return;
                }
            }
        }
        catch {         }

        Config = new AppConfig();
        Save();
    }

    public void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var opts = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(Config, opts);
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
