using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace gtmm.Services;

public sealed record GithubReleaseAsset(string Name, string DownloadUrl, long Size);

public sealed record GithubReleaseInfo(
    string Repository,
    string TagName,
    string Name,
    string HtmlUrl,
    IReadOnlyList<GithubReleaseAsset> Assets);

public sealed record GithubReleaseSource(string Repository, string? InitialTag);

public class GitHubReleaseService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("GTMM", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static bool TryParseSourceUrl(string url, out GithubReleaseSource? source)
    {
        source = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.UserInfo.Length > 0)
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        string repositoryName = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[1][..^4]
            : segments[1];
        string repository = $"{segments[0]}/{repositoryName}";
        if (!IsValidRepository(repository))
        {
            return false;
        }
        if (segments.Length == 2 ||
            (segments.Length == 3 && segments[2].Equals("releases", StringComparison.OrdinalIgnoreCase)) ||
            (segments.Length == 4 && segments[2].Equals("releases", StringComparison.OrdinalIgnoreCase) &&
             segments[3].Equals("latest", StringComparison.OrdinalIgnoreCase)))
        {
            source = new GithubReleaseSource(repository, null);
            return true;
        }

        if (segments.Length == 5 &&
            segments[2].Equals("releases", StringComparison.OrdinalIgnoreCase) &&
            segments[3].Equals("tag", StringComparison.OrdinalIgnoreCase))
        {
            source = new GithubReleaseSource(repository, Uri.UnescapeDataString(segments[4]));
            return true;
        }

        if (segments.Length == 6 &&
            segments[2].Equals("releases", StringComparison.OrdinalIgnoreCase) &&
            segments[3].Equals("download", StringComparison.OrdinalIgnoreCase))
        {
            source = new GithubReleaseSource(repository, Uri.UnescapeDataString(segments[4]));
            return true;
        }

        return false;
    }

    public async Task<GithubReleaseInfo> GetLatestReleaseAsync(string repository)
    {
        return await GetReleaseAsync(repository, null);
    }

    public async Task<GithubReleaseInfo> GetReleaseAsync(string repository, string? tag)
    {
        if (!IsValidRepository(repository))
        {
            throw new ArgumentException("GitHub repository must be in owner/repository format.", nameof(repository));
        }

        string endpoint = string.IsNullOrWhiteSpace(tag)
            ? $"https://api.github.com/repos/{repository}/releases/latest"
            : $"https://api.github.com/repos/{repository}/releases/tags/{Uri.EscapeDataString(tag)}";
        using var response = await HttpClient.GetAsync(endpoint);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        string tagName = GetString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new InvalidOperationException($"GitHub returned no release tag for {repository}.");
        }

        var assets = new List<GithubReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsElement) &&
            assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                string name = GetString(asset, "name");
                string downloadUrl = GetString(asset, "browser_download_url");
                long size = asset.TryGetProperty("size", out var sizeElement) &&
                            sizeElement.TryGetInt64(out long parsedSize)
                    ? parsedSize
                    : 0;
                if (!string.IsNullOrWhiteSpace(name) &&
                    Uri.TryCreate(downloadUrl, UriKind.Absolute, out var assetUri) &&
                    assetUri.Scheme == Uri.UriSchemeHttps)
                {
                    assets.Add(new GithubReleaseAsset(name, downloadUrl, size));
                }
            }
        }

        return new GithubReleaseInfo(
            repository,
            tagName,
            GetString(root, "name"),
            GetString(root, "html_url"),
            assets);
    }

    public static IReadOnlyList<GithubReleaseAsset> GetInstallableAssets(GithubReleaseInfo release)
    {
        return release.Assets
            .Where(asset =>
                asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .OrderBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static bool IsValidRepository(string repository)
    {
        string[] parts = repository.Split('/');
        return parts.Length == 2 && parts.All(part =>
            part.Length > 0 && part is not "." and not ".." && part.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));
    }
}
