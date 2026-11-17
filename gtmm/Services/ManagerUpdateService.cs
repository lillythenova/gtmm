using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace gtmm.Services;

public sealed record ManagerUpdateInfo(string Version, string ReleaseUrl, GithubReleaseAsset? Asset);

public sealed class ManagerUpdateService
{
    private const string Repository = "lillythenova/gtmm";
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public string CurrentVersion { get; } =
        typeof(ManagerUpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GTMM", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        return client;
    }

    public async Task<ManagerUpdateInfo?> CheckForUpdateAsync()
    {
        var release = await new GitHubReleaseService().GetLatestReleaseAsync(Repository);
        if (!IsNewerVersion(CurrentVersion, release.TagName))
        {
            return null;
        }

        GithubReleaseAsset? asset = GetPlatformAsset(release);
        return new ManagerUpdateInfo(release.TagName.TrimStart('v', 'V'), release.HtmlUrl, asset);
    }

    public async Task DownloadAndRestartAsync(GithubReleaseAsset asset)
    {
        string targetPath = GetInstallPath();
        EnsureInstallDirectoryIsWritable(targetPath);

        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"gtmm-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                tempDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        string downloadPath = Path.Combine(tempDirectory, $"gtmm-update-{Guid.NewGuid():N}");
        string scriptPath = Path.Combine(tempDirectory, $"gtmm-update-{Guid.NewGuid():N}.script");
        bool updaterStarted = false;

        try
        {
            using (var response = await HttpClient.GetAsync(
                       asset.DownloadUrl,
                       HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var output = new FileStream(
                    downloadPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                await response.Content.CopyToAsync(output);
            }

            var downloadedFile = new FileInfo(downloadPath);
            if (downloadedFile.Length == 0 ||
                (asset.Size > 0 && downloadedFile.Length != asset.Size))
            {
                throw new InvalidDataException("The downloaded GTMM update is incomplete.");
            }

            ProcessStartInfo startInfo;
            if (OperatingSystem.IsWindows())
            {
                await File.WriteAllTextAsync(scriptPath, WindowsUpdaterScript);
                startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-File");
                startInfo.ArgumentList.Add(scriptPath);
                startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
                startInfo.ArgumentList.Add(downloadPath);
                startInfo.ArgumentList.Add(targetPath);
            }
            else if (OperatingSystem.IsLinux())
            {
                await File.WriteAllTextAsync(scriptPath, LinuxUpdaterScript);
                startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add(scriptPath);
                startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
                startInfo.ArgumentList.Add(downloadPath);
                startInfo.ArgumentList.Add(targetPath);
            }
            else
            {
                throw new PlatformNotSupportedException(
                    "GTMM updates are only available on Windows and Linux.");
            }

            if (Process.Start(startInfo) == null)
            {
                throw new InvalidOperationException("Could not start the GTMM update helper.");
            }
            updaterStarted = true;
        }
        finally
        {
            if (!updaterStarted)
            {
                if (File.Exists(downloadPath))
                {
                    File.Delete(downloadPath);
                }
                if (File.Exists(scriptPath))
                {
                    File.Delete(scriptPath);
                }
            }
        }
    }

    private static GithubReleaseAsset? GetPlatformAsset(GithubReleaseInfo release)
    {
        string? assetName = null;
        if (OperatingSystem.IsWindows())
        {
            assetName = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "GTMM-win-x64.exe",
                Architecture.Arm64 => "GTMM-win-arm64.exe",
                _ => null
            };
        }
        else if (OperatingSystem.IsLinux())
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APPIMAGE")) &&
                RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                assetName = "GTMM-linux-x86_64.AppImage";
            }
            else
            {
                assetName = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "GTMM-linux-x64",
                    Architecture.Arm64 => "GTMM-linux-arm64",
                    _ => null
                };
            }
        }

        return assetName == null
            ? null
            : release.Assets.FirstOrDefault(asset =>
                asset.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetInstallPath()
    {
        string? path = null;
        bool isAppImage = false;
        if (OperatingSystem.IsLinux())
        {
            string? appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
            if (!string.IsNullOrWhiteSpace(appImagePath))
            {
                path = appImagePath;
                isAppImage = true;
            }
        }

        path ??= Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Could not determine the current GTMM executable path.");
        }

        path = Path.GetFullPath(path);
        string fileName = Path.GetFileName(path);
        bool isExpectedExecutable = OperatingSystem.IsWindows()
            ? fileName.Equals("gtmm.exe", StringComparison.OrdinalIgnoreCase)
            : isAppImage
                ? fileName.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)
                : fileName.Equals("gtmm", StringComparison.Ordinal);
        if (!isExpectedExecutable)
        {
            throw new InvalidOperationException(
                "Automatic updates are available only for an installed GTMM release executable.");
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The current GTMM executable could not be found.", path);
        }

        return path;
    }

    private static void EnsureInstallDirectoryIsWritable(string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Could not determine the GTMM install directory.");
        string probePath = Path.Combine(directory, $".gtmm-update-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probePath))
            {
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException(
                "GTMM cannot update itself because its install folder is not writable.", ex);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    private static bool IsNewerVersion(string currentVersion, string releaseVersion)
    {
        if (!TryParseVersion(currentVersion, out var current) ||
            !TryParseVersion(releaseVersion, out var release))
        {
            return false;
        }

        for (int i = 0; i < Math.Max(current.Core.Length, release.Core.Length); i++)
        {
            int currentPart = i < current.Core.Length ? current.Core[i] : 0;
            int releasePart = i < release.Core.Length ? release.Core[i] : 0;
            if (releasePart != currentPart)
            {
                return releasePart > currentPart;
            }
        }

        if (current.PreRelease.Length == 0 || release.PreRelease.Length == 0)
        {
            return current.PreRelease.Length > 0 && release.PreRelease.Length == 0;
        }

        for (int i = 0; i < Math.Max(current.PreRelease.Length, release.PreRelease.Length); i++)
        {
            if (i >= current.PreRelease.Length)
            {
                return false;
            }
            if (i >= release.PreRelease.Length)
            {
                return true;
            }

            string currentPart = current.PreRelease[i];
            string releasePart = release.PreRelease[i];
            bool currentIsNumeric = int.TryParse(currentPart, out int currentNumber);
            bool releaseIsNumeric = int.TryParse(releasePart, out int releaseNumber);
            if (currentIsNumeric && releaseIsNumeric && currentNumber != releaseNumber)
            {
                return releaseNumber > currentNumber;
            }
            if (currentIsNumeric != releaseIsNumeric)
            {
                return !releaseIsNumeric;
            }

            int comparison = string.Compare(
                releasePart,
                currentPart,
                StringComparison.OrdinalIgnoreCase);
            if (comparison != 0)
            {
                return comparison > 0;
            }
        }

        return false;
    }

    private static bool TryParseVersion(string value, out ParsedVersion parsed)
    {
        parsed = default;
        string normalized = value.Trim().TrimStart('v', 'V');
        int metadataIndex = normalized.IndexOf('+');
        if (metadataIndex >= 0)
        {
            normalized = normalized[..metadataIndex];
        }

        string[] versionParts = normalized.Split('-', 2);
        string[] coreParts = versionParts[0].Split('.');
        if (coreParts.Length == 0 ||
            coreParts.Any(part => !int.TryParse(part, out int number) || number < 0))
        {
            return false;
        }

        parsed = new ParsedVersion(
            coreParts.Select(int.Parse).ToArray(),
            versionParts.Length == 2 ? versionParts[1].Split('.') : Array.Empty<string>());
        return true;
    }

    private readonly record struct ParsedVersion(int[] Core, string[] PreRelease);

    private const string LinuxUpdaterScript = """
        set -eu
        process_id="$1"
        downloaded="$2"
        target="$3"
        while kill -0 "$process_id" 2>/dev/null; do
            sleep 0.2
        done
        staged="${target}.gtmm-update.$$"
        cp -- "$downloaded" "$staged"
        chmod u+x "$staged"
        mv -f -- "$staged" "$target"
        rm -f -- "$downloaded" "$0"
        exec "$target"
        """;

    private const string WindowsUpdaterScript = """
        $ErrorActionPreference = 'Stop'
        $processIdToWait = [int]$args[0]
        $downloadPath = $args[1]
        $targetPath = $args[2]
        while (Get-Process -Id $processIdToWait -ErrorAction SilentlyContinue) {
            Start-Sleep -Milliseconds 250
        }
        $stagedPath = "$targetPath.gtmm-update-$PID"
        Copy-Item -LiteralPath $downloadPath -Destination $stagedPath -Force
        Move-Item -LiteralPath $stagedPath -Destination $targetPath -Force
        Remove-Item -LiteralPath $downloadPath -Force
        Start-Process -FilePath $targetPath
        """;
}
