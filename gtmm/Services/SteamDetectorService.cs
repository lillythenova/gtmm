using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace gtmm.Services;

public class BepInExStatus
{
    public bool GameFound { get; set; }
    public string? GamePath { get; set; }
    public bool HasPlugins { get; set; }
    public bool HasCore { get; set; }
    public bool IsBepInExValid { get; set; }
    public string? PluginsPath { get; set; }
    public string? CorePath { get; set; }
    public List<string> MissingItems { get; set; } = new();
    public string Reason { get; set; } = "";
}

public static class SteamDetectorService
{
    public const string GorillaTagAppId = "1533390";
    public const string GorillaTagFolderName = "Gorilla Tag";

    public static bool IsValidGameDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        try
        {
            if (Directory.Exists(Path.Combine(path, "BepInEx")) ||
                Directory.Exists(Path.Combine(path, "Gorilla Tag_Data")) ||
                File.Exists(Path.Combine(path, "Gorilla Tag.exe")) ||
                File.Exists(Path.Combine(path, "GorillaTag.exe")) ||
                File.Exists(Path.Combine(path, "Gorilla Tag.x86_64")) ||
                File.Exists(Path.Combine(path, "GorillaTag.x86_64")))
            {
                return true;
            }

            var entries = Directory.GetFileSystemEntries(path);
            foreach (var entry in entries)
            {
                string name = Path.GetFileName(entry);
                if (name.Equals("Gorilla Tag.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("GorillaTag.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Gorilla Tag.x86_64", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("GorillaTag.x86_64", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Gorilla Tag_Data", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("BepInEx", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    public static string? FindGameDirectory(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && IsValidGameDirectory(customPath))
        {
            return Path.GetFullPath(customPath);
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var regPaths = new[]
                {
                    $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {GorillaTagAppId}",
                    $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {GorillaTagAppId}"
                };

                foreach (var regKey in regPaths)
                {
                    foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                    {
                        try
                        {
                            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                            using var key = baseKey.OpenSubKey(regKey);
                            if (key?.GetValue("InstallLocation") is string loc && Directory.Exists(loc))
                            {
                                return Path.GetFullPath(loc);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        var libraries = FindAllSteamLibraries();
        foreach (var lib in libraries)
        {
            string manifest = Path.Combine(lib, "steamapps", $"appmanifest_{GorillaTagAppId}.acf");
            if (File.Exists(manifest))
            {
                try
                {
                    string manifestContent = File.ReadAllText(manifest);
                    Match installDirMatch = Regex.Match(
                        manifestContent,
                        @"""installdir""\s+""([^""]+)""",
                        RegexOptions.IgnoreCase);
                    if (installDirMatch.Success)
                    {
                        string candidate = Path.Combine(
                            lib, "steamapps", "common", installDirMatch.Groups[1].Value);
                        if (Directory.Exists(candidate))
                        {
                            return Path.GetFullPath(candidate);
                        }
                    }
                }
                catch (IOException ex)
                {
                    Console.Error.WriteLine($"Could not read Gorilla Tag Steam manifest '{manifest}': {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    Console.Error.WriteLine($"Could not read Gorilla Tag Steam manifest '{manifest}': {ex.Message}");
                }
            }

            var candidates = new[]
            {
                Path.Combine(lib, "steamapps", "common", GorillaTagFolderName),
                Path.Combine(lib, "SteamApps", "common", GorillaTagFolderName)
            };

            foreach (var cand in candidates)
            {
                if (IsValidGameDirectory(cand))
                {
                    return Path.GetFullPath(cand);
                }
            }
        }

        return null;
    }

    public static List<string> ParseVdfLibraryFolders(string vdfPath)
    {
        var paths = new List<string>();
        if (!File.Exists(vdfPath)) return paths;

        try
        {
            string content = File.ReadAllText(vdfPath);

            var pathMatches = Regex.Matches(content, @"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
            foreach (Match m in pathMatches)
            {
                if (m.Groups.Count > 1)
                {
                    string raw = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (Directory.Exists(raw))
                    {
                        string normalized = Path.GetFullPath(raw);
                        if (!paths.Contains(normalized))
                        {
                            paths.Add(normalized);
                        }
                    }
                }
            }

            var legacyMatches = Regex.Matches(content, @"""\d+""\s+""([A-Za-z]:\\[^""]+|/[^""]+)""");
            foreach (Match m in legacyMatches)
            {
                if (m.Groups.Count > 1)
                {
                    string raw = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (Directory.Exists(raw))
                    {
                        string normalized = Path.GetFullPath(raw);
                        if (!paths.Contains(normalized))
                        {
                            paths.Add(normalized);
                        }
                    }
                }
            }
        }
        catch { }

        return paths;
    }

    public static List<string> GetStandardSteamPaths()
    {
        var steamPaths = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var regLocations = new (RegistryHive Hive, string SubKey, string ValueName)[]
                {
                    (RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                    (RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                    (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                    (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam", "InstallLocation"),
                };

                foreach (var (hive, subKey, valueName) in regLocations)
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                        using var key = baseKey.OpenSubKey(subKey);
                        if (key?.GetValue(valueName) is string val && !string.IsNullOrWhiteSpace(val))
                        {
                            string norm = Path.GetFullPath(val);
                            if (Directory.Exists(norm) && !steamPaths.Contains(norm))
                            {
                                steamPaths.Add(norm);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            var programFiles = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };

            foreach (var pf in programFiles)
            {
                if (!string.IsNullOrEmpty(pf))
                {
                    string steamDir = Path.Combine(pf, "Steam");
                    if (Directory.Exists(steamDir))
                    {
                        string norm = Path.GetFullPath(steamDir);
                        if (!steamPaths.Contains(norm))
                        {
                            steamPaths.Add(norm);
                        }
                    }
                }
            }
        }
        else
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
            {
                home = Environment.GetEnvironmentVariable("HOME") ?? "";
            }

            if (!string.IsNullOrEmpty(home))
            {
                var candidates = new[]
                {
                    Path.Combine(home, ".local/share/Steam"),
                    Path.Combine(home, ".steam/steam"),
                    Path.Combine(home, ".steam/root"),
                    Path.Combine(home, ".steam"),
                    Path.Combine(home, ".steam/debian-installation"),
                    Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam"),
                    Path.Combine(home, ".var/app/com.valvesoftware.Steam/data/Steam"),
                    Path.Combine(home, ".var/app/com.valvesoftware.Steam/.steam/steam"),
                    Path.Combine(home, "snap/steam/common/.local/share/Steam")
                };

                foreach (var p in candidates)
                {
                    if (Directory.Exists(p))
                    {
                        string norm = Path.GetFullPath(p);
                        if (!steamPaths.Contains(norm))
                        {
                            steamPaths.Add(norm);
                        }
                    }
                }
            }
        }

        return steamPaths;
    }

    public static List<string> FindAllSteamLibraries()
    {
        var libraries = new List<string>();
        var steamRoots = GetStandardSteamPaths();

        foreach (var root in steamRoots)
        {
            if (!libraries.Contains(root))
            {
                libraries.Add(root);
            }

            var vdfCandidates = new[]
            {
                Path.Combine(root, "steamapps", "libraryfolders.vdf"),
                Path.Combine(root, "config", "libraryfolders.vdf")
            };

            foreach (var vdf in vdfCandidates)
            {
                if (File.Exists(vdf))
                {
                    var discovered = ParseVdfLibraryFolders(vdf);
                    foreach (var lib in discovered)
                    {
                        if (!libraries.Contains(lib))
                        {
                            libraries.Add(lib);
                        }
                    }
                }
            }
        }

        return libraries;
    }

    public static BepInExStatus VerifyBepInEx(string? gameDir = null)
    {
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            gameDir = FindGameDirectory();
        }

        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
        {
            return new BepInExStatus
            {
                GameFound = false,
                GamePath = null,
                HasPlugins = false,
                HasCore = false,
                IsBepInExValid = false,
                PluginsPath = null,
                CorePath = null,
                MissingItems = new List<string> { "Gorilla Tag Installation" },
                Reason = "Gorilla Tag directory could not be located on this system."
            };
        }

        string bepInExPath = Path.Combine(gameDir, "BepInEx");
        string pluginsPath = Path.Combine(bepInExPath, "plugins");
        string corePath = Path.Combine(bepInExPath, "core");

        bool hasPlugins = Directory.Exists(pluginsPath);
        bool hasCore = Directory.Exists(corePath);

        var missing = new List<string>();
        if (!hasPlugins) missing.Add(Path.Combine("BepInEx", "plugins"));
        if (!hasCore) missing.Add(Path.Combine("BepInEx", "core"));

        bool isValid = hasPlugins && hasCore;
        string reason = isValid
            ? "BepInEx is installed with both 'plugins' and 'core' present."
            : $"BepInEx requirement check failed. Missing: {string.Join(", ", missing)} in {gameDir}.";

        return new BepInExStatus
        {
            GameFound = true,
            GamePath = Path.GetFullPath(gameDir),
            HasPlugins = hasPlugins,
            HasCore = hasCore,
            IsBepInExValid = isValid,
            PluginsPath = hasPlugins ? Path.GetFullPath(pluginsPath) : null,
            CorePath = hasCore ? Path.GetFullPath(corePath) : null,
            MissingItems = missing,
            Reason = reason
        };
    }
}
