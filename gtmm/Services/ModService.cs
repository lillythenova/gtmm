using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using gtmm.Models;

namespace gtmm.Services;

public class ModService
{
    private static readonly HttpClient HttpClient = new();

    public string GetModsDirectory(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath)) return "";
        return Path.Combine(gamePath, "BepInEx", "plugins");
    }

    public List<ModItem> LoadInstalledMods(string modsDir, List<RepoModItem>? repoMods = null)
    {
        var list = new List<ModItem>();
        if (!Directory.Exists(modsDir)) return list;

        foreach (string directory in Directory.GetDirectories(modsDir, "*", SearchOption.TopDirectoryOnly))
        {
            string directoryName = Path.GetFileName(directory);
            bool isDisabled = directoryName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            string modName = isDisabled ? directoryName[..^".disabled".Length] : directoryName;
            if (!Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories).Any())
            {
                continue;
            }

            var match = repoMods?.FirstOrDefault(repoMod =>
                repoMod.Name.Equals(modName, StringComparison.OrdinalIgnoreCase));
            string currentVersion = match?.Version ?? "v1.0.0";
            list.Add(new ModItem
            {
                Name = modName,
                Version = currentVersion,
                Path = directory,
                Enabled = !isDisabled,
                HasUpdate = false,
                NewVersion = ""
            });
        }

        var files = Directory.GetFiles(modsDir, "*.dll*", SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
            string fileName = Path.GetFileName(file);
            bool isEnabled = fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            bool isDisabled = fileName.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase);

            if (!isEnabled && !isDisabled) continue;

            string modName = isEnabled
                ? fileName.Substring(0, fileName.Length - 4)
                : fileName.Substring(0, fileName.Length - 13);

            string currentVer = "v1.0.0";
            list.Add(new ModItem
            {
                Name = modName,
                Version = currentVer,
                Path = file,
                Enabled = isEnabled,
                HasUpdate = false,
                NewVersion = ""
            });
        }

        return list.OrderBy(m => m.Name).ToList();
    }

    public bool ToggleMod(ModItem mod)
    {
        try
        {
            if (Directory.Exists(mod.Path))
            {
                string parent = Path.GetDirectoryName(mod.Path)!;
                string targetDirectory = Path.Combine(parent,
                    mod.Enabled ? $"{mod.Name}.disabled" : mod.Name);
                if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory)) return false;
                Directory.Move(mod.Path, targetDirectory);
                mod.Path = targetDirectory;
                mod.Enabled = !mod.Enabled;
                return true;
            }

            if (!File.Exists(mod.Path)) return false;

            string dir = Path.GetDirectoryName(mod.Path)!;
            string targetPath;

            if (mod.Enabled)
            {
                targetPath = Path.Combine(dir, $"{mod.Name}.dll.disabled");
                File.Move(mod.Path, targetPath, overwrite: true);
                mod.Path = targetPath;
                mod.Enabled = false;
            }
            else
            {
                targetPath = Path.Combine(dir, $"{mod.Name}.dll");
                File.Move(mod.Path, targetPath, overwrite: true);
                mod.Path = targetPath;
                mod.Enabled = true;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool DeleteMod(ModItem mod)
    {
        try
        {
            if (Directory.Exists(mod.Path))
            {
                Directory.Delete(mod.Path, recursive: true);
                return true;
            }

            if (File.Exists(mod.Path))
            {
                File.Delete(mod.Path);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> InstallOrUpdateModAsync(string modsDir, RepoModItem repoMod, string? gamePath = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(modsDir)) return false;
            if (!Directory.Exists(modsDir))
            {
                Directory.CreateDirectory(modsDir);
            }

            gamePath ??= Path.GetFullPath(Path.Combine(modsDir, "..", ".."));
            if (repoMod.DownloadUrls.Count > 1)
            {
                return await InstallMultipleDllsAsync(modsDir, repoMod);
            }

            string targetPath = GetSafeFilePath(modsDir, $"{repoMod.Name}.dll");
            string downloadUrl = repoMod.DownloadUrls.FirstOrDefault() ?? repoMod.DownloadUrl;
            if (string.IsNullOrWhiteSpace(downloadUrl)) return false;

            byte[]? data = null;

            if (downloadUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                downloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                data = await HttpClient.GetByteArrayAsync(downloadUrl);
            }
            else if (File.Exists(downloadUrl))
            {
                data = await File.ReadAllBytesAsync(downloadUrl);
            }

            if (data != null && data.Length > 0)
            {
                bool isZip = data.Length > 4 && data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04;
                if (isZip || downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var ms = new MemoryStream(data);
                    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

                    string configDir = Path.Combine(gamePath, "BepInEx", "config");
                    string packageDir = GetSafeFilePath(modsDir, repoMod.Name);
                    var entriesToExtract = archive.Entries
                        .Where(entry => !string.IsNullOrEmpty(entry.Name))
                        .Select(entry =>
                        {
                            string relativePath = entry.FullName.Replace('\\', '/');
                            int pluginsIndex = relativePath.IndexOf(
                                "BepInEx/plugins/", StringComparison.OrdinalIgnoreCase);
                            if (pluginsIndex >= 0)
                            {
                                relativePath = relativePath[(pluginsIndex + "BepInEx/plugins/".Length)..];
                            }
                            else
                            {
                                int configIndex = relativePath.IndexOf(
                                    "BepInEx/config/", StringComparison.OrdinalIgnoreCase);
                                if (configIndex >= 0)
                                {
                                    relativePath = relativePath[(configIndex + "BepInEx/config/".Length)..];
                                    return (Entry: entry, Destination: configDir, RelativePath: relativePath);
                                }
                            }

                            return (Entry: entry, Destination: packageDir, RelativePath: relativePath);
                        })
                        .ToList();

                    if (!entriesToExtract.Any(item =>
                            item.Destination == packageDir &&
                            item.Entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                    {
                        return false;
                    }

                    foreach (var item in entriesToExtract)
                    {
                        string destinationPath = GetSafeArchivePath(item.Destination, item.RelativePath);
                        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(destinationDirectory))
                        {
                            Directory.CreateDirectory(destinationDirectory);
                        }

                        await using var entryStream = item.Entry.Open();
                        await using var outputStream = new FileStream(
                            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        await entryStream.CopyToAsync(outputStream);
                    }

                    return true;
                }

                await File.WriteAllBytesAsync(targetPath, data);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to install '{repoMod.Name}': {ex}");
            return false;
        }
    }

    private static async Task<bool> InstallMultipleDllsAsync(string modsDir, RepoModItem repoMod)
    {
        var files = new List<(string Name, byte[] Data)>();
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string downloadUrl in repoMod.DownloadUrls)
        {
            string fileName = GetDownloadFileName(downloadUrl);
            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"'{fileName}' is not a DLL file.");
            }
            if (!fileNames.Add(fileName))
            {
                throw new InvalidDataException($"The mod has more than one download named '{fileName}'.");
            }

            byte[] data;
            if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                data = await HttpClient.GetByteArrayAsync(uri);
            }
            else if (File.Exists(downloadUrl))
            {
                data = await File.ReadAllBytesAsync(downloadUrl);
            }
            else
            {
                throw new FileNotFoundException($"Could not find download '{downloadUrl}'.");
            }

            if (data.Length == 0)
            {
                throw new InvalidDataException($"The download '{fileName}' was empty.");
            }
            files.Add((fileName, data));
        }

        string packagePath = GetSafeFilePath(modsDir, repoMod.Name);
        string packageParent = Path.GetDirectoryName(Path.GetFullPath(modsDir))!;
        string stagingPath = GetSafeFilePath(packageParent, $".{repoMod.Name}.{Guid.NewGuid():N}.tmp");
        string backupPath = GetSafeFilePath(packageParent, $".{repoMod.Name}.{Guid.NewGuid():N}.bak");
        bool existingMoved = false;
        bool stagingMoved = false;
        try
        {
            Directory.CreateDirectory(stagingPath);
            foreach (var file in files)
            {
                await File.WriteAllBytesAsync(GetSafeFilePath(stagingPath, file.Name), file.Data);
            }

            if (Directory.Exists(packagePath))
            {
                Directory.Move(packagePath, backupPath);
                existingMoved = true;
            }
            else if (File.Exists(packagePath))
            {
                throw new IOException($"A file already exists at the install path for '{repoMod.Name}'.");
            }

            Directory.Move(stagingPath, packagePath);
            stagingMoved = true;
            if (existingMoved)
            {
                Directory.Delete(backupPath, recursive: true);
                existingMoved = false;
            }
            return true;
        }
        catch
        {
            if (stagingMoved && Directory.Exists(packagePath))
            {
                Directory.Delete(packagePath, recursive: true);
            }
            if (existingMoved && Directory.Exists(backupPath) && !Directory.Exists(packagePath))
            {
                Directory.Move(backupPath, packagePath);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }
        }
    }

    private static string GetDownloadFileName(string downloadUrl)
    {
        if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
        }

        return Path.GetFileName(downloadUrl);
    }

    private static string GetSafeFilePath(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName is "." or ".." ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.IndexOfAny(new[] { '/', '\\', '<', '>', ':', '"', '|', '?', '*' }) >= 0)
        {
            throw new InvalidDataException("The mod package contains an invalid file name.");
        }

        string fullDirectory = Path.GetFullPath(directory);
        string fullPath = Path.GetFullPath(Path.Combine(fullDirectory, fileName));
        string directoryPrefix = fullDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? fullDirectory
            : fullDirectory + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(directoryPrefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
        {
            throw new InvalidDataException("The mod package file path is outside the install directory.");
        }

        return fullPath;
    }

    private static string GetSafeArchivePath(string directory, string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') ||
            Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException("The mod archive contains an invalid file path.");
        }

        string fullDirectory = Path.GetFullPath(directory);
        string fullPath = Path.GetFullPath(Path.Combine(
            fullDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)));
        string directoryPrefix = fullDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? fullDirectory
            : fullDirectory + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(directoryPrefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
        {
            throw new InvalidDataException("The mod archive contains a path outside its install directory.");
        }

        return fullPath;
    }

    public static bool IsNewer(string currentVer, string repoVer)
    {
        string c = currentVer.TrimStart('v', 'V');
        string r = repoVer.TrimStart('v', 'V');
        if (Version.TryParse(c, out var v1) && Version.TryParse(r, out var v2))
        {
            return v2 > v1;
        }
        return string.Compare(r, c, StringComparison.OrdinalIgnoreCase) > 0;
    }
}
