using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Media;
using gtmm.Services;

namespace gtmm.Models;

public class FilterDropdownItem
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public bool IsSelected { get; set; }
    public IBrush TextColor
    {
        get
        {
            if (IsSelected)
            {
                if (Application.Current?.Resources.TryGetResource("PrimaryPurple", null, out var b) == true && b is IBrush pb)
                    return pb;
                return SolidColorBrush.Parse("#A855F7");
            }
            if (Application.Current?.Resources.TryGetResource("TextPrimary", null, out var tb) == true && tb is IBrush tbBrush)
                return tbBrush;
            return ThemeService.CurrentMode == "dark" ? Brushes.White : SolidColorBrush.Parse("#1D1B20");
        }
    }
    public IBrush BackgroundColor
    {
        get
        {
            if (IsSelected)
            {
                if (Application.Current?.Resources.TryGetResource("PillBgBrush", null, out var b) == true && b is IBrush pb)
                    return pb;
                return SolidColorBrush.Parse("#2B2930");
            }
            return Brushes.Transparent;
        }
    }
    public FontWeight FontWeight => IsSelected ? FontWeight.SemiBold : FontWeight.Normal;
    public Action? OnClick { get; set; }
}

public class ModItem : INotifyPropertyChanged
{
    private string _name = "";
    private string _version = "v1.0.0";
    private string _path = "";
    private bool _enabled = true;
    private bool _hasUpdate = false;
    private string _newVersion = "";

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Version
    {
        get => _version;
        set => SetField(ref _version, value);
    }

    public string Path
    {
        get => _path;
        set => SetField(ref _path, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public bool HasUpdate
    {
        get => _hasUpdate;
        set => SetField(ref _hasUpdate, value);
    }

    public string NewVersion
    {
        get => _newVersion;
        set => SetField(ref _newVersion, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class RepoModItem : INotifyPropertyChanged
{
    private string _name = "";
    private string _version = "v1.0.0";
    private string _description = "";
    private string _downloadUrl = "";
    private string _repoName = "";
    private bool _isInstalled = false;
    private string _installedVersion = "";
    private bool _canUpdate = false;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Version
    {
        get => _version;
        set => SetField(ref _version, value);
    }

    public string Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public string DownloadUrl
    {
        get => _downloadUrl;
        set => SetField(ref _downloadUrl, value);
    }

    public string RepoName
    {
        get => _repoName;
        set => SetField(ref _repoName, value);
    }

    public List<string> Dependencies { get; set; } = new();

    [JsonIgnore]
    public string? GithubRepository { get; set; }

    [JsonIgnore]
    public string? GithubAssetName { get; set; }

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (SetField(ref _isInstalled, value))
            {
                OnPropertyChanged(nameof(DownloadTooltip));
            }
        }
    }

    public string InstalledVersion
    {
        get => _installedVersion;
        set => SetField(ref _installedVersion, value);
    }

    public bool CanUpdate
    {
        get => _canUpdate;
        set
        {
            if (SetField(ref _canUpdate, value))
            {
                OnPropertyChanged(nameof(DownloadTooltip));
            }
        }
    }

    public string Initial => string.IsNullOrEmpty(Name) ? "M" : Name.Substring(0, 1).ToUpper();

    public string DownloadTooltip => IsInstalled
        ? (CanUpdate ? "Update Available (Click to update)" : "Reinstall / Overwrite Mod")
        : "Download & Install Mod";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class RepoItem : INotifyPropertyChanged
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("github_repository")]
    public string? GithubRepository { get; set; }

    [JsonPropertyName("github_asset_name")]
    public string? GithubAssetName { get; set; }

    [JsonPropertyName("installed_version")]
    public string? InstalledVersion { get; set; }

    private string? _error;
    [JsonIgnore]
    public string? Error
    {
        get => _error;
        set => SetField(ref _error, value);
    }

    private string _name = "";
    [JsonIgnore]
    public string Name
    {
        get => string.IsNullOrEmpty(_name) ? Url : _name;
        set => SetField(ref _name, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class AppConfig
{
    [JsonPropertyName("game_path")]
    public string GamePath { get; set; } = "";

    [JsonPropertyName("repositories")]
    public List<RepoItem> Repositories { get; set; } = new();

    [JsonPropertyName("launch_mode")]
    public string LaunchMode { get; set; } = "steam";

    [JsonPropertyName("launch_args")]
    public string LaunchArgs { get; set; } = "";

    [JsonPropertyName("auto_check_updates")]
    public bool AutoCheckUpdates { get; set; } = true;

    [JsonPropertyName("theme")]
    public string Theme
    {
        get => ThemeMode;
        set => ThemeMode = value;
    }

    [JsonPropertyName("theme_mode")]
    public string ThemeMode { get; set; } = "dark";

    [JsonPropertyName("theme_palette")]
    public string ThemePalette { get; set; } = "pink";
}
