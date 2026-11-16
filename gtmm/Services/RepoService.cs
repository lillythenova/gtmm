using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using gtmm.Models;

namespace gtmm.Services;

public class RepoService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };

    public async Task<string> FetchRawRepoAsync(string urlOrPath)
    {
        if (File.Exists(urlOrPath))
        {
            return await File.ReadAllTextAsync(urlOrPath);
        }

        if (urlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            urlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return await HttpClient.GetStringAsync(urlOrPath);
        }

        throw new FileNotFoundException($"Cannot locate repository at {urlOrPath}");
    }

    public List<RepoModItem> ParseRepoMods(string jsonContent, string fallbackRepoName)
    {
        var list = new List<RepoModItem>();
        using var doc = JsonDocument.Parse(jsonContent);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Repository JSON must contain an object.");
        }

        string repoName = fallbackRepoName;
        if (root.TryGetProperty("repo-name", out var rn) && rn.ValueKind == JsonValueKind.String)
        {
            repoName = rn.GetString() ?? fallbackRepoName;
        }

        if (root.TryGetProperty("mods", out var modsArray))
        {
            if (modsArray.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Repository 'mods' must be an array.");
            }

            foreach (var element in modsArray.EnumerateArray())
            {
                list.Add(ParseModElement(element, repoName));
            }
            return list;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("repo-name") || property.NameEquals("format") ||
                property.NameEquals("encrypted") || property.NameEquals("kdf") || property.NameEquals("cipher"))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                var mod = ParseModElement(property.Value, repoName);
                if (string.IsNullOrEmpty(mod.Name))
                {
                    mod.Name = property.Name;
                }
                list.Add(mod);
            }
        }
        return list;
    }

    private static RepoModItem ParseModElement(JsonElement el, string repoName)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Each repository mod must be an object.");
        }

        string name = "";
        string ver = "v1.0.0";
        string desc = "";
        string author = "";
        string url = "";
        var downloadUrls = new List<string>();
        var dependencies = new List<string>();

        if (el.TryGetProperty("mod-name", out var np) || el.TryGetProperty("name", out np))
            name = np.GetString() ?? "";

        if (el.TryGetProperty("mod-ver", out var vp) || el.TryGetProperty("version", out vp))
            ver = vp.GetString() ?? "v1.0.0";

        if (el.TryGetProperty("mod-desc", out var dp) || el.TryGetProperty("description", out dp))
            desc = dp.GetString() ?? "";

        if (el.TryGetProperty("author", out var authorElement))
        {
            if (authorElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"Author for '{name}' must be a string.");
            }

            author = authorElement.GetString()?.Trim() ?? "";
        }

        if (el.TryGetProperty("mod-link", out var lp) || el.TryGetProperty("download_url", out lp) || el.TryGetProperty("url", out lp))
            url = lp.GetString() ?? "";

        if (el.TryGetProperty("download_urls", out var downloadUrlsElement))
        {
            if (downloadUrlsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"Download links for '{name}' must be an array of URLs.");
            }

            foreach (var downloadUrl in downloadUrlsElement.EnumerateArray())
            {
                if (downloadUrl.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(downloadUrl.GetString()))
                {
                    throw new InvalidDataException($"Download links for '{name}' must contain non-empty URLs.");
                }

                downloadUrls.Add(downloadUrl.GetString()!.Trim());
            }
        }
        else if (!string.IsNullOrWhiteSpace(url))
        {
            downloadUrls.Add(url);
        }

        if (el.TryGetProperty("dependencies", out var dependenciesElement))
        {
            if (dependenciesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"Dependencies for '{name}' must be an array of mod names.");
            }

            foreach (var dependency in dependenciesElement.EnumerateArray())
            {
                if (dependency.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(dependency.GetString()))
                {
                    throw new InvalidDataException($"Dependencies for '{name}' must contain non-empty mod names.");
                }

                dependencies.Add(dependency.GetString()!.Trim());
            }
        }

        return new RepoModItem
        {
            Name = name,
            Version = ver,
            Description = desc,
            Author = author,
            DownloadUrl = url,
            DownloadUrls = downloadUrls,
            RepoName = repoName,
            Dependencies = dependencies
        };
    }
}
