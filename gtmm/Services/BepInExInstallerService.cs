using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace gtmm.Services;

public class BepInExInstallerService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("GTMM", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public async Task<string> InstallLatestAsync(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) ||
            !SteamDetectorService.IsValidGameDirectory(gameDirectory))
        {
            throw new DirectoryNotFoundException(
                "Select a valid Gorilla Tag installation folder before installing BepInEx.");
        }

        string gamePath = Path.GetFullPath(gameDirectory);
        string platform = IsWindowsGame(gamePath) ? "win" : "linux";
        string releaseJson = await HttpClient.GetStringAsync(
            "https://api.github.com/repos/BepInEx/BepInEx/releases/latest");

        using var releaseDocument = JsonDocument.Parse(releaseJson);
        var releaseRoot = releaseDocument.RootElement;
        string version = ReadString(releaseRoot, "tag_name");
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidDataException("The official BepInEx release did not contain a version tag.");
        }

        if (!releaseRoot.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The official BepInEx release did not contain downloadable assets.");
        }

        string prefix = $"BepInEx_{platform}_x64_";
        var asset = assets.EnumerateArray().FirstOrDefault(candidate =>
            ReadString(candidate, "name").StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            ReadString(candidate, "name").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidDataException($"No official BepInEx x64 package was found for {platform}.");
        }

        string downloadUrl = ReadString(asset, "browser_download_url");
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("The official BepInEx release asset URL was invalid.");
        }

        byte[] archiveData = await HttpClient.GetByteArrayAsync(downloadUri);
        string expectedDigest = ReadString(asset, "digest");
        if (expectedDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            string actualDigest = Convert.ToHexString(SHA256.HashData(archiveData)).ToLowerInvariant();
            if (!actualDigest.Equals(
                    expectedDigest["sha256:".Length..], StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The downloaded BepInEx package did not match GitHub's SHA-256 digest.");
            }
        }

        string stagingDirectory = Path.Combine(
            Path.GetTempPath(), $"gtmm-bepinex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            using (var stream = new MemoryStream(archiveData))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                ExtractArchiveSafely(archive, stagingDirectory);
            }

            string stagedCore = Path.Combine(stagingDirectory, "BepInEx", "core");
            if (!Directory.Exists(stagedCore) ||
                !Directory.EnumerateFiles(stagedCore, "*.dll").Any())
            {
                throw new InvalidDataException(
                    "The downloaded BepInEx package did not contain its core loader files.");
            }

            CopyDirectory(stagingDirectory, gamePath);
            Directory.CreateDirectory(Path.Combine(gamePath, "BepInEx", "plugins"));
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }

        return version;
    }

    private static bool IsWindowsGame(string gamePath)
    {
        return File.Exists(Path.Combine(gamePath, "Gorilla Tag.exe")) ||
               File.Exists(Path.Combine(gamePath, "GorillaTag.exe"));
    }

    private static void ExtractArchiveSafely(ZipArchive archive, string destinationDirectory)
    {
        string destinationRoot = Path.GetFullPath(destinationDirectory);
        string prefix = destinationRoot.EndsWith(Path.DirectorySeparatorChar)
            ? destinationRoot
            : destinationRoot + Path.DirectorySeparatorChar;
        bool containsCore = false;

        foreach (var entry in archive.Entries)
        {
            string relativePath = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(relativePath) ||
                relativePath.StartsWith('/') ||
                relativePath.Split('/').Any(part => part is "." or ".."))
            {
                throw new InvalidDataException("The BepInEx archive contains an unsafe path.");
            }

            int unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixFileType == 0xA000)
            {
                throw new InvalidDataException("The BepInEx archive contains an unsupported symbolic link.");
            }

            string destinationPath = Path.GetFullPath(
                Path.Combine(destinationRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!destinationPath.StartsWith(prefix, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
            {
                throw new InvalidDataException("The BepInEx archive contains an unsafe path.");
            }

            string normalized = relativePath.TrimEnd('/');
            if (normalized.StartsWith("BepInEx/core/", StringComparison.OrdinalIgnoreCase))
            {
                containsCore = true;
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            string? parent = Path.GetDirectoryName(destinationPath);
            if (parent == null)
            {
                throw new InvalidDataException("The BepInEx archive contains an invalid file path.");
            }

            Directory.CreateDirectory(parent);
            using var input = entry.Open();
            using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write);
            input.CopyTo(output);
        }

        if (!containsCore)
        {
            throw new InvalidDataException("The BepInEx archive did not contain a BepInEx/core folder.");
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory))
        {
            string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }

        foreach (string sourceSubdirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            string destinationSubdirectory = Path.Combine(
                destinationDirectory, Path.GetFileName(sourceSubdirectory));
            CopyDirectory(sourceSubdirectory, destinationSubdirectory);
        }
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }
}
