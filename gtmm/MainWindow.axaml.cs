using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using gtmm.Models;
using gtmm.Services;

namespace gtmm;

public partial class MainWindow : Window
{
    private const string DefaultRepositoryUrl =
        "https://raw.githubusercontent.com/lillythenova/gtmm/main/repository.json";

    private readonly ConfigService _configService = new();
    private readonly ModService _modService = new();
    private readonly RepoService _repoService = new();
    private readonly GitHubReleaseService _githubReleaseService = new();
    private readonly ManagerUpdateService _managerUpdateService = new();
    private readonly BepInExInstallerService _bepInExInstallerService = new();

    private string _activeTab = "mods";
    private string _filterMode = "all";
    private string _repoFilter = "all";
    private readonly List<ModItem> _allInstalledMods = new();
    private readonly List<RepoModItem> _allRepoMods = new();
    private readonly Dictionary<string, GithubReleaseInfo> _githubReleaseCache =
        new(StringComparer.OrdinalIgnoreCase);
    private int _githubCheckFailures;
    private ManagerUpdateInfo? _managerUpdate;
    private bool _managerUpdateInProgress;

    public ObservableCollection<ModItem> FilteredMods { get; } = new();
    public ObservableCollection<RepoModItem> FilteredRepoMods { get; } = new();
    public ObservableCollection<RepoItem> ConfiguredRepos { get; } = new();
    private readonly ObservableCollection<FilterDropdownItem> _filterDropdownItems = new();

    public MainWindow() : this("mods")
    {
    }

    public MainWindow(string initialTab)
    {
        InitializeComponent();
        _activeTab = initialTab;
        AddModPopup.PlacementTarget = AddLocalModButton;
        DragDrop.SetAllowDrop(GithubModUrlBox, true);
        DragDrop.AddDragOverHandler(GithubModUrlBox, OnGithubUrlDragOver);
        DragDrop.AddDropHandler(GithubModUrlBox, OnGithubUrlDrop);

        ModsItemsControl.ItemsSource = FilteredMods;
        ReposItemsControl.ItemsSource = FilteredRepoMods;
        ConfiguredReposControl.ItemsSource = ConfiguredRepos;
        FilterItemsControl.ItemsSource = _filterDropdownItems;

        ViewStashPanel.Children.Clear();

        SearchBox.TextChanged += (s, e) => ApplyFilters();

        AddHandler(PointerPressedEvent, (sender, e) =>
        {
            if (FilterDropdownMenu.IsVisible)
            {
                var point = e.GetCurrentPoint(this);
                var bounds = FilterDropdownMenu.Bounds;
                var filterBtnBounds = FilterButton.Bounds;
                var filterPos = FilterDropdownMenu.TranslatePoint(new Point(0, 0), this);
                var btnPos = FilterButton.TranslatePoint(new Point(0, 0), this);
                if (filterPos.HasValue)
                {
                    var dropdownRect = new Rect(filterPos.Value, bounds.Size);
                    if (!dropdownRect.Contains(point.Position))
                    {
                        if (btnPos.HasValue)
                        {
                            var btnRect = new Rect(btnPos.Value, filterBtnBounds.Size);
                            if (!btnRect.Contains(point.Position))
                            {
                                CloseFilterDropdown();
                            }
                        }
                        else
                        {
                            CloseFilterDropdown();
                        }
                    }
                }
            }
        }, RoutingStrategies.Tunnel);

        Loaded += async (s, e) =>
        {
            _configService.Load();
            AutoCheckUpdatesBox.IsChecked = _configService.Config.AutoCheckUpdates;
            CheckGithubOnRefreshBox.IsChecked = _configService.Config.CheckGithubReleasesOnRefresh;
            AutoUpdateManagerBox.IsChecked = _configService.Config.AutoUpdateManager;
            if (!_configService.Config.Repositories.Any(repo =>
                    repo.Url.Equals(DefaultRepositoryUrl, StringComparison.OrdinalIgnoreCase)))
            {
                _configService.Config.Repositories.Add(new RepoItem
                {
                    Url = DefaultRepositoryUrl,
                    Enabled = true
                });
                _configService.Save();
            }

            bool wasUnsetOrInvalid = !SteamDetectorService.IsValidGameDirectory(_configService.Config.GamePath);
            if (wasUnsetOrInvalid)
            {
                string? detected = SteamDetectorService.FindGameDirectory();
                if (!string.IsNullOrEmpty(detected))
                {
                    _configService.Config.GamePath = detected;
                    _configService.Save();
                    ShowToast("gorilla tag dir detected");
                }
                else
                {
                    ShowToast("couldnt find gorilla tag dir please set manually");
                }
            }

            GamePathBox.Text = _configService.Config.GamePath;
            EnsureLinuxBepInExLaunchOptions();
            GamePathBox.LostFocus += async (sender, args) =>
            {
                string newPath = (GamePathBox.Text ?? "").Trim();
                if (newPath != _configService.Config.GamePath)
                {
                    _configService.Config.GamePath = newPath;
                    _configService.Save();
                    EnsureLinuxBepInExLaunchOptions();
                    await ReloadAllAsync();
                }
            };

            LaunchArgsBox.Text = _configService.Config.LaunchArgs;
            LaunchArgsBox.TextChanged += (sender, args) =>
            {
                _configService.Config.LaunchArgs = LaunchArgsBox.Text ?? "";
                _configService.Save();
            };

            ThemeService.ApplyTheme(_configService.Config.ThemeMode, _configService.Config.ThemePalette);
            UpdateThemeUI();

            await ReloadAllAsync(_configService.Config.AutoCheckUpdates);
            _ = CheckForManagerUpdateAsync();
            SwitchTab(_activeTab);
            BuildFilterDropdown(); 
        };
    }

    private async Task ReloadAllAsync(bool checkGithubReleases = false)
    {
        _allRepoMods.Clear();
        ConfiguredRepos.Clear();
        int sourceFailures = 0;
        _githubCheckFailures = 0;
        var releasesCheckedThisRefresh = new Dictionary<string, GithubReleaseInfo>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var repo in _configService.Config.Repositories)
        {
            if (!repo.Enabled) continue;

            repo.Error = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(repo.GithubRepository) &&
                    !string.IsNullOrWhiteSpace(repo.GithubAssetName))
                {
                    GithubReleaseInfo? release = null;
                    if (checkGithubReleases)
                    {
                        if (!releasesCheckedThisRefresh.TryGetValue(repo.GithubRepository, out release))
                        {
                            try
                            {
                                release = await _githubReleaseService.GetLatestReleaseAsync(repo.GithubRepository);
                                releasesCheckedThisRefresh[repo.GithubRepository] = release;
                                _githubReleaseCache[repo.GithubRepository] = release;
                            }
                            catch (Exception ex)
                            {
                                _githubCheckFailures++;
                                Console.Error.WriteLine(
                                    $"Failed to check GitHub release for '{repo.GithubRepository}': {ex}");
                                _githubReleaseCache.TryGetValue(repo.GithubRepository, out release);
                            }
                        }
                    }
                    else if (!_githubReleaseCache.TryGetValue(repo.GithubRepository, out release))
                    {
                        release = null;
                    }

                    var asset = release?.Assets.FirstOrDefault(candidate =>
                        candidate.Name.Equals(repo.GithubAssetName, StringComparison.OrdinalIgnoreCase));
                    if (release != null && asset == null)
                    {
                        _githubCheckFailures++;
                        Console.Error.WriteLine(
                            $"GitHub release '{release.TagName}' for '{repo.GithubRepository}' has no asset '{repo.GithubAssetName}'.");
                    }
                    repo.Name = $"{repo.GithubRepository} · {repo.GithubAssetName}";
                    _allRepoMods.Add(new RepoModItem
                    {
                        Name = repo.GithubAssetName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                            ? Path.GetFileNameWithoutExtension(repo.GithubAssetName)
                            : repo.GithubRepository.Split('/').Last(),
                        Version = release?.TagName ?? repo.InstalledVersion ?? "latest",
                        Description = string.IsNullOrWhiteSpace(release?.Name)
                            ? $"GitHub release from {repo.GithubRepository}"
                            : release.Name,
                        DownloadUrl = asset?.DownloadUrl ??
                                      $"https://github.com/{repo.GithubRepository}/releases/latest/download/{Uri.EscapeDataString(repo.GithubAssetName)}",
                        RepoName = repo.Name,
                        GithubRepository = repo.GithubRepository,
                        GithubAssetName = repo.GithubAssetName,
                        SourceRepositoryUrl = repo.Url,
                        IsInstalled = false,
                        InstalledVersion = repo.InstalledVersion ?? "",
                        CanUpdate = false
                    });
                    ConfiguredRepos.Add(repo);
                    continue;
                }

                string raw = await _repoService.FetchRawRepoAsync(repo.Url);

                bool isEncrypted = false;
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    if ((root.TryGetProperty("kdf", out var kdfElement) &&
                         kdfElement.ValueKind == JsonValueKind.Object &&
                         root.TryGetProperty("cipher", out var cipherElement) &&
                         cipherElement.ValueKind == JsonValueKind.Object) ||
                        (root.TryGetProperty("encrypted", out var ep) &&
                         ep.ValueKind == JsonValueKind.True))
                    {
                        isEncrypted = true;
                    }
                }
                catch (JsonException)
                {
                    isEncrypted = false;
                }

                if (isEncrypted)
                {
                    throw new InvalidDataException("Password-protected repositories are not supported.");
                }

                var repoParsedMods = _repoService.ParseRepoMods(raw, repo.Name);
                if (repoParsedMods.Count > 0 && !string.IsNullOrEmpty(repoParsedMods[0].RepoName))
                {
                    repo.Name = repoParsedMods[0].RepoName;
                }
                foreach (var repoMod in repoParsedMods)
                {
                    repoMod.SourceRepositoryUrl = repo.Url;
                }
                _allRepoMods.AddRange(repoParsedMods);
            }
            catch (Exception ex)
            {
                repo.Error = ex.Message;
                sourceFailures++;
                Console.Error.WriteLine($"Failed to refresh repository '{repo.Url}': {ex.Message}");
            }

            ConfiguredRepos.Add(repo);
        }

        await RefreshGithubReleaseMetadataAsync(checkGithubReleases, releasesCheckedThisRefresh);

        _allInstalledMods.Clear();
        var loaderStatus = SteamDetectorService.VerifyBepInEx(_configService.Config.GamePath);
        if (loaderStatus.IsBepInExValid)
        {
            string modsDir = _modService.GetModsDirectory(_configService.Config.GamePath);
            var localMods = _modService.LoadInstalledMods(
                modsDir, _allRepoMods.Where(repoMod => repoMod.GithubRepository == null).ToList());
            _allInstalledMods.AddRange(localMods);
        }

        foreach (var repoMod in _allRepoMods)
        {
            if (repoMod.GithubRepository != null)
            {
                if (repoMod.GithubAssetNames.Count > 0 &&
                    !string.IsNullOrEmpty(repoMod.SourceRepositoryUrl))
                {
                    string versionKey = GetInstalledVersionKey(repoMod);
                    if (_configService.Config.InstalledModVersions.TryGetValue(versionKey, out string? installedVersion))
                    {
                        repoMod.InstalledVersion = installedVersion;
                    }
                }

                var installed = _allInstalledMods.FirstOrDefault(mod =>
                    mod.Name.Equals(repoMod.Name, StringComparison.OrdinalIgnoreCase));
                if (installed != null)
                {
                    repoMod.IsInstalled = true;
                    repoMod.CanUpdate = !string.IsNullOrWhiteSpace(repoMod.InstalledVersion) &&
                                        ModService.IsNewer(repoMod.InstalledVersion, repoMod.Version);
                    if (repoMod.CanUpdate)
                    {
                        installed.HasUpdate = true;
                        installed.NewVersion = repoMod.Version;
                    }
                    else
                    {
                        installed.HasUpdate = false;
                        installed.NewVersion = "";
                    }
                }
                continue;
            }

            var match = _allInstalledMods.FirstOrDefault(m =>
                m.Name.Equals(repoMod.Name, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(repoMod.DownloadUrl) && Path.GetFileNameWithoutExtension(repoMod.DownloadUrl).Equals(m.Name, StringComparison.OrdinalIgnoreCase)));

            if (match != null)
            {
                repoMod.IsInstalled = true;
                string versionKey = GetInstalledVersionKey(repoMod);
                if (_configService.Config.InstalledModVersions.TryGetValue(
                        versionKey, out string? installedVersion))
                {
                    repoMod.InstalledVersion = installedVersion;
                    repoMod.CanUpdate = ModService.IsNewer(installedVersion, repoMod.Version);
                }
                else
                {
                    repoMod.InstalledVersion = "";
                    repoMod.CanUpdate = false;
                }

                match.HasUpdate = repoMod.CanUpdate;
                match.NewVersion = repoMod.CanUpdate ? repoMod.Version : "";
            }
            else
            {
                repoMod.IsInstalled = false;
                repoMod.InstalledVersion = "";
                repoMod.CanUpdate = false;
            }
        }

        ApplyFilters();
        BuildFilterDropdown();
        if (sourceFailures > 0)
        {
            ShowToast($"Could not refresh {sourceFailures} source(s). Check Repositories.", true);
        }
        else if (_githubCheckFailures > 0)
        {
            ShowToast($"Could not check {_githubCheckFailures} GitHub release(s). Check your connection or GitHub rate limit.", true);
        }

        if (checkGithubReleases)
        {
            int availableUpdates = _allRepoMods.Count(repoMod =>
                repoMod.GithubRepository != null && repoMod.IsInstalled && repoMod.CanUpdate);
            if (availableUpdates > 0)
            {
                ShowToast($"{availableUpdates} GitHub mod update(s) available");
            }
        }
    }

    private async Task RefreshGithubReleaseMetadataAsync(
        bool checkGithubReleases,
        Dictionary<string, GithubReleaseInfo> releasesCheckedThisRefresh)
    {
        var releaseMods = new List<(RepoModItem Mod, string Repository, List<string> AssetNames)>();
        foreach (var mod in _allRepoMods)
        {
            var parsedAssets = mod.DownloadUrls
                .Select(url => GitHubReleaseService.TryParseLatestAssetUrl(
                    url, out string repository, out string assetName)
                    ? (Success: true, Repository: repository, AssetName: assetName)
                    : (Success: false, Repository: "", AssetName: ""))
                .ToList();
            if (parsedAssets.Count == 0 || parsedAssets.Any(asset => !asset.Success))
            {
                continue;
            }

            string repository = parsedAssets[0].Repository;
            if (parsedAssets.Any(asset =>
                    !asset.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var assetNames = parsedAssets.Select(asset => asset.AssetName).ToList();
            mod.GithubRepository = repository;
            mod.GithubAssetNames = assetNames;
            mod.GithubAssetName = assetNames.Count == 1 ? assetNames[0] : null;
            releaseMods.Add((mod, repository, assetNames));
        }

        if (checkGithubReleases)
        {
            foreach (string repository in releaseMods
                         .Select(item => item.Repository)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (releasesCheckedThisRefresh.ContainsKey(repository))
                {
                    continue;
                }

                try
                {
                    var release = await _githubReleaseService.GetLatestReleaseAsync(repository);
                    releasesCheckedThisRefresh[repository] = release;
                    _githubReleaseCache[repository] = release;
                }
                catch (Exception ex)
                {
                    _githubCheckFailures++;
                    Console.Error.WriteLine($"Failed to check GitHub release for '{repository}': {ex}");
                }
            }
        }

        foreach (var (mod, repository, assetNames) in releaseMods)
        {
            if (!_githubReleaseCache.TryGetValue(repository, out var release))
            {
                continue;
            }

            var resolvedAssets = new List<GithubReleaseAsset>();
            foreach (string assetName in assetNames)
            {
                var asset = release.Assets.FirstOrDefault(candidate =>
                    candidate.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase));
                if (asset == null)
                {
                    if (checkGithubReleases)
                    {
                        _githubCheckFailures++;
                        Console.Error.WriteLine(
                            $"GitHub release '{release.TagName}' for '{repository}' has no asset '{assetName}'.");
                    }
                    resolvedAssets.Clear();
                    break;
                }
                resolvedAssets.Add(asset);
            }

            if (resolvedAssets.Count != assetNames.Count)
            {
                continue;
            }

            mod.Version = release.TagName;
            mod.DownloadUrls = resolvedAssets.Select(asset => asset.DownloadUrl).ToList();
            mod.DownloadUrl = mod.DownloadUrls[0];
        }
    }

    private static string GetInstalledVersionKey(RepoModItem mod)
    {
        return $"{mod.SourceRepositoryUrl}\n{mod.Name}";
    }

    private void ApplyFilters()
    {
        string query = (SearchBox.Text ?? "").Trim().ToLower();

        if (_activeTab == "mods")
        {
            var loaderStatus = SteamDetectorService.VerifyBepInEx(_configService.Config.GamePath);
            if (!loaderStatus.IsBepInExValid)
            {
                BepInExMissingPanel.IsVisible = true;
                ModsScrollViewer.IsVisible = false;
                ModsEmptyPanel.IsVisible = false;
                FilteredMods.Clear();
                ModActionsContainer.IsEnabled = false;
                AddLocalModButton.IsEnabled = false;
                return;
            }

            BepInExMissingPanel.IsVisible = false;
            ModsScrollViewer.IsVisible = true;
            ModActionsContainer.IsEnabled = true;
            AddLocalModButton.IsEnabled = true;

            FilteredMods.Clear();
            foreach (var mod in _allInstalledMods)
            {
                if (_filterMode == "enabled" && !mod.Enabled) continue;
                if (_filterMode == "disabled" && mod.Enabled) continue;
                if (_filterMode == "updates" && !mod.HasUpdate) continue;
                if (!string.IsNullOrEmpty(query) && !mod.Name.ToLower().Contains(query)) continue;
                FilteredMods.Add(mod);
            }
            ModsEmptyPanel.IsVisible = FilteredMods.Count == 0;
        }
        else if (_activeTab == "repos")
        {
            FilteredRepoMods.Clear();

            foreach (var rMod in _allRepoMods)
            {
                if (_repoFilter.StartsWith("repo:"))
                {
                    string targetName = _repoFilter.Substring(5);
                    if (!rMod.RepoName.Equals(targetName, StringComparison.OrdinalIgnoreCase)) continue;
                }

                if (!string.IsNullOrEmpty(query))
                {
                    bool match = rMod.Name.ToLower().Contains(query) ||
                                 rMod.Description.ToLower().Contains(query) ||
                                 rMod.RepoName.ToLower().Contains(query);
                    if (!match) continue;
                }

                FilteredRepoMods.Add(rMod);
            }

            ReposEmptyPanel.IsVisible = FilteredRepoMods.Count == 0;
        }
    }

    public async void OpenFilterDropdownForTest()
    {
        await Task.Delay(500);
        OpenFilterDropdown();
    }

    private void OnFilterButtonClick(object? sender, RoutedEventArgs e)
    {
        if (FilterDropdownMenu.IsVisible)
        {
            CloseFilterDropdown();
        }
        else
        {
            OpenFilterDropdown();
        }
    }

    public void OpenFilterDropdown()
    {
        BuildFilterDropdown();
        FilterDropdownMenu.IsVisible = true;
        FilterDropdownMenu.Opacity = 1;
        FilterDropdownMenu.RenderTransform = TransformOperations.Parse("translateY(0px)");
    }

    public void CloseFilterDropdown()
    {
        FilterDropdownMenu.Opacity = 0;
        FilterDropdownMenu.RenderTransform = TransformOperations.Parse("translateY(-8px)");
        FilterDropdownMenu.IsVisible = false;
    }

    private void OnFilterItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is FilterDropdownItem item)
        {
            CloseFilterDropdown();
            item.OnClick?.Invoke();
        }
    }

    private void BuildFilterDropdown()
    {
        _filterDropdownItems.Clear();

        if (_activeTab == "mods")
        {
            _filterDropdownItems.Add(new FilterDropdownItem
            {
                Id = "all",
                Label = "all mods",
                IsSelected = _filterMode == "all",
                OnClick = () => { _filterMode = "all"; ApplyFilters(); }
            });
            _filterDropdownItems.Add(new FilterDropdownItem
            {
                Id = "enabled",
                Label = "enabled only",
                IsSelected = _filterMode == "enabled",
                OnClick = () => { _filterMode = "enabled"; ApplyFilters(); }
            });
            _filterDropdownItems.Add(new FilterDropdownItem
            {
                Id = "disabled",
                Label = "disabled only",
                IsSelected = _filterMode == "disabled",
                OnClick = () => { _filterMode = "disabled"; ApplyFilters(); }
            });
            _filterDropdownItems.Add(new FilterDropdownItem
            {
                Id = "updates",
                Label = "updates available",
                IsSelected = _filterMode == "updates",
                OnClick = () => { _filterMode = "updates"; ApplyFilters(); }
            });
        }
        else if (_activeTab == "repos")
        {
            _filterDropdownItems.Add(new FilterDropdownItem
            {
                Id = "all",
                Label = "all repositories",
                IsSelected = _repoFilter == "all",
                OnClick = () => { _repoFilter = "all"; ApplyFilters(); }
            });
            foreach (var repo in _configService.Config.Repositories)
            {
                string repoName = repo.Name;
                _filterDropdownItems.Add(new FilterDropdownItem
                {
                    Id = "repo:" + repoName,
                    Label = repoName,
                    IsSelected = _repoFilter == "repo:" + repoName,
                    OnClick = () => { _repoFilter = "repo:" + repoName; ApplyFilters(); }
                });
            }
        }
    }

    private void OnNavModsClick(object? sender, RoutedEventArgs e) => SwitchTab("mods");
    private void OnNavReposClick(object? sender, RoutedEventArgs e) => SwitchTab("repos");
    private void OnNavSettingsClick(object? sender, RoutedEventArgs e) => SwitchTab("settings");

    private void SwitchTab(string tab)
    {
        CloseFilterDropdown();
        _activeTab = tab;

        NavModsBtn.Classes.Set("active", tab == "mods");
        NavReposBtn.Classes.Set("active", tab == "repos");
        NavSettingsBtn.Classes.Set("active", tab == "settings");

        Control targetView = tab switch
        {
            "repos" => ReposViewGrid,
            "settings" => SettingsViewGrid,
            _ => ModsViewGrid
        };

        targetView.IsVisible = true;

        if (MainContentHost.Content != targetView)
        {
            MainContentHost.Content = targetView;
        }

        if (tab == "settings")
        {
            TopControlsRow.Opacity = 0;
            TopControlsRow.RenderTransform = TransformOperations.Parse("translateY(-8px)");
            TopControlsRow.IsHitTestVisible = false;

            SettingsTopHeader.Opacity = 1;
            SettingsTopHeader.RenderTransform = TransformOperations.Parse("translateY(0px)");
            SettingsTopHeader.IsHitTestVisible = true;
        }
        else
        {
            SettingsTopHeader.Opacity = 0;
            SettingsTopHeader.RenderTransform = TransformOperations.Parse("translateY(72px)");
            SettingsTopHeader.IsHitTestVisible = false;

            TopControlsRow.Opacity = 1;
            TopControlsRow.RenderTransform = TransformOperations.Parse("translateY(0px)");
            TopControlsRow.IsHitTestVisible = true;

            bool isMods = tab == "mods";
            ModActionsContainer.Width = isMods ? 128 : 0;
            ModActionsContainer.Opacity = isMods ? 1 : 0;
            ModActionsContainer.IsHitTestVisible = isMods;

            SearchBox.PlaceholderText = isMods ? "search installed mods" : "search mods";
        }

        BuildFilterDropdown();
        ApplyFilters();
    }

    private System.Threading.CancellationTokenSource? _toastCts;

    public void ShowToast(string message, bool isAlert = false)
    {
        _toastCts?.Cancel();
        _toastCts = new System.Threading.CancellationTokenSource();
        var token = _toastCts.Token;

        ToastText.Text = message;

        ToastNotificationPill.IsVisible = true;
        ToastNotificationPill.Opacity = 1;
        ToastNotificationPill.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translateY(0px)");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2500, token);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ToastNotificationPill.Opacity = 0;
                    ToastNotificationPill.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translateY(20px)");
                });
                await Task.Delay(250, token);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ToastNotificationPill.Opacity == 0)
                    {
                        ToastNotificationPill.IsVisible = false;
                    }
                });
            }
            catch (TaskCanceledException) { }
        });
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        await ReloadAllAsync(_configService.Config.CheckGithubReleasesOnRefresh);
        int failures = _configService.Config.Repositories.Count(repo =>
            repo.Enabled && !string.IsNullOrEmpty(repo.Error));
        if (failures > 0)
        {
            ShowToast($"Reloaded with {failures} source error(s); check Repositories", true);
        }
        else if (_githubCheckFailures == 0)
        {
            ShowToast("Reloaded mods & repositories");
        }
    }

    private void OnUpdateSettingsChanged(object? sender, RoutedEventArgs e)
    {
        _configService.Config.AutoCheckUpdates = AutoCheckUpdatesBox.IsChecked == true;
        _configService.Config.CheckGithubReleasesOnRefresh = CheckGithubOnRefreshBox.IsChecked == true;
        _configService.Config.AutoUpdateManager = AutoUpdateManagerBox.IsChecked == true;
        _configService.Save();

        if (_configService.Config.AutoUpdateManager && _managerUpdate != null)
        {
            _ = InstallManagerUpdateAsync();
        }
    }

    private async Task CheckForManagerUpdateAsync()
    {
        ManagerUpdateStatusText.Text = "Checking whether GTMM has an update...";
        try
        {
            _managerUpdate = await _managerUpdateService.CheckForUpdateAsync();
            if (_managerUpdate == null)
            {
                ManagerUpdateStatusText.Text = "GTMM is up to date.";
                UpdateManagerButton.IsVisible = false;
                return;
            }

            if (_managerUpdate.Asset == null)
            {
                ManagerUpdateStatusText.Text =
                    $"GTMM {_managerUpdate.Version} is available, but there is no update for this kind of computer.";
                UpdateManagerButton.IsVisible = false;
                return;
            }

            ManagerUpdateStatusText.Text = $"A newer version of GTMM ({_managerUpdate.Version}) is ready.";
            UpdateManagerButton.Content = $"Update GTMM to v{_managerUpdate.Version}";
            UpdateManagerButton.IsVisible = true;
            if (_configService.Config.AutoUpdateManager)
            {
                await InstallManagerUpdateAsync();
            }
        }
        catch (Exception ex)
        {
            ManagerUpdateStatusText.Text = $"Could not check for updates. Check your internet connection. Details: {ex.Message}";
        }
    }

    private async void OnUpdateManagerClick(object? sender, RoutedEventArgs e)
    {
        await InstallManagerUpdateAsync();
    }

    private async Task InstallManagerUpdateAsync()
    {
        if (_managerUpdateInProgress || _managerUpdate?.Asset == null)
        {
            return;
        }

        _managerUpdateInProgress = true;
        UpdateManagerButton.IsEnabled = false;
        AutoUpdateManagerBox.IsEnabled = false;
        ManagerUpdateStatusText.Text = $"Downloading GTMM update {_managerUpdate.Version}...";
        try
        {
            await _managerUpdateService.DownloadAndRestartAsync(_managerUpdate.Asset);
            Close();
        }
        catch (Exception ex)
        {
            _managerUpdateInProgress = false;
            UpdateManagerButton.IsEnabled = true;
            AutoUpdateManagerBox.IsEnabled = true;
            ManagerUpdateStatusText.Text = $"GTMM could not install the update. Details: {ex.Message}";
            ShowToast($"GTMM could not install the update: {ex.Message}", true);
        }
    }

    private async void OnUpdateAllClick(object? sender, RoutedEventArgs e)
    {
        int count = 0;
        int failed = 0;
        foreach (var mod in _allInstalledMods.Where(m => m.HasUpdate))
        {
            var match = _allRepoMods.FirstOrDefault(rm =>
                rm.GithubRepository == null &&
                rm.Name.Equals(mod.Name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                try
                {
                    await InstallWithDependenciesAsync(match);
                    count++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to update '{match.Name}': {ex}");
                    failed++;
                }
            }
        }
        foreach (var repoMod in _allRepoMods.Where(repoMod =>
                     repoMod.GithubRepository != null && repoMod.IsInstalled && repoMod.CanUpdate))
        {
            try
            {
                await InstallWithDependenciesAsync(repoMod);
                count++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to update '{repoMod.Name}': {ex}");
                failed++;
            }
        }
        _configService.Save();
        await ReloadAllAsync();
        ShowToast(failed > 0
            ? $"Updated {count} mod(s); failed to update {failed}"
            : count > 0 ? $"Updated {count} mod(s)" : "All mods are up to date");
    }

    private void OnCheckAllClick(object? sender, RoutedEventArgs e)
    {
        foreach (var mod in _allInstalledMods)
        {
            if (!mod.Enabled)
            {
                _modService.ToggleMod(mod);
            }
        }
        ApplyFilters();
        ShowToast("All mods enabled");
    }

    private void OnUncheckAllClick(object? sender, RoutedEventArgs e)
    {
        foreach (var mod in _allInstalledMods)
        {
            if (mod.Enabled)
            {
                _modService.ToggleMod(mod);
            }
        }
        ApplyFilters();
        ShowToast("All mods disabled");
    }

    private void OnAddLocalModClick(object? sender, RoutedEventArgs e)
    {
        AddModPopup.IsOpen = !AddModPopup.IsOpen;
    }

    private static void OnGithubUrlDragOver(object? sender, DragEventArgs e)
    {
        string? text = e.DataTransfer.TryGetText()?.Trim();
        e.DragEffects = GitHubReleaseService.TryParseSourceUrl(text ?? "", out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnGithubUrlDrop(object? sender, DragEventArgs e)
    {
        string? text = e.DataTransfer.TryGetText()?.Trim();
        if (GitHubReleaseService.TryParseSourceUrl(text ?? "", out var source) &&
            Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
            source != null)
        {
            GithubModUrlBox.Text = uri.AbsoluteUri;
            GithubModUrlBox.CaretIndex = GithubModUrlBox.Text.Length;
        }

        e.Handled = true;
    }

    private async void OnChooseLocalModFileClick(object? sender, RoutedEventArgs e)
    {
        AddModPopup.IsOpen = false;
        AddLocalModButton.IsEnabled = false;
        try
        {
            var files = new List<(string Name, string Path, Func<Task<Stream>> OpenRead)>();
            if (OperatingSystem.IsLinux())
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "zenity",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("--file-selection");
                startInfo.ArgumentList.Add("--multiple");
                startInfo.ArgumentList.Add("--separator=\n");
                startInfo.ArgumentList.Add("--title=Select Mod DLL");
                startInfo.ArgumentList.Add("--file-filter=Mod files (*.dll, *.zip) | *.dll *.zip");

                using var picker = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Could not start the Linux file picker.");
                Task<string> selectedFilesTask = picker.StandardOutput.ReadToEndAsync();
                Task<string> pickerErrorTask = picker.StandardError.ReadToEndAsync();
                await picker.WaitForExitAsync();
                string selectedFiles = await selectedFilesTask;
                string pickerError = await pickerErrorTask;
                if (picker.ExitCode == 1) return;
                if (picker.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(pickerError)
                            ? $"The file picker exited with code {picker.ExitCode}."
                            : pickerError.Trim());
                }

                foreach (string path in selectedFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    string selectedPath = path.TrimEnd('\r');
                    files.Add((Path.GetFileName(selectedPath), selectedPath,
                        () => Task.FromResult<Stream>(File.OpenRead(selectedPath))));
                }
            }
            else
            {
                var selectedFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Mod DLL",
                    AllowMultiple = true,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Mod files (*.dll, *.zip)")
                        {
                            Patterns = new[] { "*.dll", "*.zip" }
                        }
                    }
                });
                foreach (var file in selectedFiles)
                {
                    files.Add((file.Name, file.Path.LocalPath, async () => await file.OpenReadAsync()));
                }
            }

            if (files.Count == 0) return;

            var loaderStatus = SteamDetectorService.VerifyBepInEx(_configService.Config.GamePath);
            if (!loaderStatus.IsBepInExValid)
            {
                ShowToast("Mod loader is missing. Install Mod Loader before adding mods.", true);
                return;
            }

            string modsDir = _modService.GetModsDirectory(_configService.Config.GamePath);
            if (string.IsNullOrWhiteSpace(modsDir))
            {
                ShowToast("Set a valid Gorilla Tag folder in Settings first.", true);
                return;
            }

            Directory.CreateDirectory(modsDir);
            int installed = 0;
            var failures = new List<string>();
            foreach (var (name, sourcePath, openRead) in files)
            {
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var localPackage = new RepoModItem
                    {
                        Name = Path.GetFileNameWithoutExtension(name),
                        Version = "v1.0.0",
                        DownloadUrl = sourcePath
                    };
                    if (await _modService.InstallOrUpdateModAsync(
                            modsDir, localPackage, _configService.Config.GamePath))
                    {
                        installed++;
                    }
                    else
                    {
                        failures.Add($"{name}: could not extract a mod from the ZIP");
                    }
                    continue;
                }

                if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{name}: not a DLL");
                    continue;
                }

                try
                {
                    string destination = Path.Combine(modsDir, Path.GetFileName(name));
                    string temporaryDestination = destination + ".tmp";
                    await using var source = await openRead();
                    await using (var target = new FileStream(
                                     temporaryDestination, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await source.CopyToAsync(target);
                    }
                    File.Move(temporaryDestination, destination, overwrite: true);
                    installed++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{name}: {ex.Message}");
                }
            }

            await ReloadAllAsync();
            ShowToast(failures.Count == 0
                ? $"Installed {installed} local mod(s)"
                : $"Installed {installed}; failed: {string.Join("; ", failures)}", failures.Count > 0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to install local mod: {ex}");
            ShowToast($"Could not install local mod: {ex.Message}", true);
        }
        finally
        {
            AddLocalModButton.IsEnabled = true;
        }
    }

    private async void OnDownloadModFromGithubClick(object? sender, RoutedEventArgs e)
    {
        string url = GithubModUrlBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowToast("Paste a GitHub release URL first.", true);
            return;
        }

        GithubModUrlBox.Text = "";
        AddModPopup.IsOpen = false;
        await AddGithubModAsync(url);
    }

    private async Task AddGithubModAsync(string url)
    {
        if (!GitHubReleaseService.TryParseSourceUrl(url, out var githubSource) ||
            githubSource == null)
        {
            ShowToast("Enter a valid GitHub repository or release URL.", true);
            return;
        }

        var loaderStatus = SteamDetectorService.VerifyBepInEx(_configService.Config.GamePath);
        if (!loaderStatus.IsBepInExValid)
        {
            ShowToast("Mod loader is missing. Install Mod Loader before adding mods.", true);
            return;
        }

        AddLocalModButton.IsEnabled = false;
        try
        {
            var release = await _githubReleaseService.GetReleaseAsync(
                githubSource.Repository, githubSource.InitialTag);
            var assets = GitHubReleaseService.GetInstallableAssets(release);
            if (assets.Count == 0)
            {
                ShowToast("That GitHub release has no DLL or ZIP assets.", true);
                return;
            }

            var picker = new GithubAssetPickerWindow(assets);
            await picker.ShowDialog(this);
            if (picker.SelectedAsset == null) return;

            var latestRelease = await _githubReleaseService.GetLatestReleaseAsync(githubSource.Repository);
            var latestAsset = latestRelease.Assets.FirstOrDefault(asset =>
                asset.Name.Equals(picker.SelectedAsset.Name, StringComparison.OrdinalIgnoreCase));
            if (latestAsset == null)
            {
                ShowToast(
                    $"'{picker.SelectedAsset.Name}' is not in the latest release. Choose an asset name used by the latest release.",
                    true);
                return;
            }

            var source = _configService.Config.Repositories.FirstOrDefault(repo =>
                repo.GithubRepository == githubSource.Repository &&
                repo.GithubAssetName == latestAsset.Name);
            if (source == null)
            {
                source = new RepoItem
                {
                    Url = url,
                    GithubRepository = githubSource.Repository,
                    GithubAssetName = latestAsset.Name,
                    Enabled = true
                };
                _configService.Config.Repositories.Add(source);
            }
            else
            {
                source.Enabled = true;
            }

            _configService.Save();
            await ReloadAllAsync();

            var trackedMod = _allRepoMods.FirstOrDefault(mod =>
                mod.GithubRepository == githubSource.Repository &&
                mod.GithubAssetName == latestAsset.Name);
            if (trackedMod == null)
            {
                ShowToast("The selected GitHub mod could not be loaded.", true);
                return;
            }

            var installedMods = await InstallWithDependenciesAsync(trackedMod);
            await ReloadAllAsync();
            string dependenciesMessage = installedMods.Count > 1
                ? $" (including {string.Join(", ", installedMods.Where(name =>
                    !name.Equals(trackedMod.Name, StringComparison.OrdinalIgnoreCase)))})"
                : "";
            ShowToast($"Installed {trackedMod.Name}{dependenciesMessage}; checking GitHub releases for updates.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to add GitHub mod from '{url}': {ex}");
            ShowToast($"Could not download GitHub mod: {ex.Message}", true);
        }
        finally
        {
            AddLocalModButton.IsEnabled = true;
        }
    }

    private void OnModCheckboxPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is ModItem mod)
        {
            if (_modService.ToggleMod(mod))
            {
                ApplyFilters();
            }
        }
    }

    private void OnModDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ModItem mod)
        {
            string name = mod.Name;
            if (_modService.DeleteMod(mod))
            {
                _allInstalledMods.Remove(mod);
                ApplyFilters();
                ShowToast($"Deleted {name}");
            }
        }
    }

    private async void OnModUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ModItem mod)
        {
            var match = _allRepoMods.FirstOrDefault(rm => rm.Name.Equals(mod.Name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                string modsDir = _modService.GetModsDirectory(_configService.Config.GamePath);
                btn.IsEnabled = false;
                try
                {
                    await InstallWithDependenciesAsync(match);
                    await ReloadAllAsync();
                    ShowToast($"Updated {mod.Name}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to update '{mod.Name}': {ex}");
                    ShowToast($"Could not update {mod.Name}: {ex.Message}", true);
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }
    }

    private async void OnRepoModDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RepoModItem repoMod)
        {
            btn.IsEnabled = false;
            try
            {
                var installedMods = await InstallWithDependenciesAsync(repoMod);
                await ReloadAllAsync();
                string dependenciesMessage = installedMods.Count > 1
                    ? $" (including {string.Join(", ", installedMods.Where(name =>
                        !name.Equals(repoMod.Name, StringComparison.OrdinalIgnoreCase)))})"
                    : "";
                ShowToast($"Installed {repoMod.Name}{dependenciesMessage}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to install '{repoMod.Name}': {ex}");
                ShowToast($"Could not install {repoMod.Name}: {ex.Message}", true);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }

    private async Task<List<string>> InstallWithDependenciesAsync(RepoModItem mod)
    {
        var loaderStatus = SteamDetectorService.VerifyBepInEx(_configService.Config.GamePath);
        if (!loaderStatus.IsBepInExValid)
        {
            throw new InvalidOperationException("Mod loader is missing. Install Mod Loader first.");
        }

        string modsDirectory = _modService.GetModsDirectory(_configService.Config.GamePath);
        if (string.IsNullOrWhiteSpace(modsDirectory))
        {
            throw new InvalidOperationException("Set a valid Gorilla Tag folder in Settings first.");
        }

        var installedNames = new HashSet<string>(
            _modService.LoadInstalledMods(modsDirectory).Select(installed => installed.Name),
            StringComparer.OrdinalIgnoreCase);
        var states = new Dictionary<RepoModItem, int>();
        var path = new List<string>();
        var installOrder = new List<RepoModItem>();

        void Visit(RepoModItem current)
        {
            if (states.TryGetValue(current, out int state))
            {
                if (state == 2) return;
                if (state == 1)
                {
                    int cycleStart = path.FindIndex(name =>
                        name.Equals(current.Name, StringComparison.OrdinalIgnoreCase));
                    var cycle = path.Skip(Math.Max(cycleStart, 0)).Append(current.Name);
                    throw new InvalidDataException($"Dependency cycle: {string.Join(" -> ", cycle)}.");
                }
            }

            states[current] = 1;
            path.Add(current.Name);
            foreach (string dependencyName in current.Dependencies)
            {
                var matches = _allRepoMods.Where(candidate =>
                    candidate.Name.Equals(dependencyName, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0)
                {
                    throw new InvalidDataException(
                        $"'{current.Name}' requires '{dependencyName}', but it was not found in any enabled repository.");
                }
                if (matches.Count > 1)
                {
                    throw new InvalidDataException(
                        $"'{dependencyName}' is listed in multiple enabled repositories. Keep only one source for this dependency.");
                }

                Visit(matches[0]);
            }

            path.RemoveAt(path.Count - 1);
            states[current] = 2;
            if (!installedNames.Contains(current.Name) || ReferenceEquals(current, mod))
            {
                installOrder.Add(current);
            }
        }

        Visit(mod);
        Directory.CreateDirectory(modsDirectory);
        var installedNow = new List<string>();
        foreach (var item in installOrder)
        {
            bool success = await _modService.InstallOrUpdateModAsync(
                modsDirectory, item, _configService.Config.GamePath);
            if (!success)
            {
                throw new InvalidOperationException($"Could not download or install '{item.Name}'.");
            }

            installedNames.Add(item.Name);
            installedNow.Add(item.Name);
            if (!string.IsNullOrEmpty(item.SourceRepositoryUrl))
            {
                _configService.Config.InstalledModVersions[GetInstalledVersionKey(item)] = item.Version;
                item.InstalledVersion = item.Version;
            }
            if (item.GithubRepository != null)
            {
                var source = _configService.Config.Repositories.FirstOrDefault(repo =>
                    repo.GithubRepository == item.GithubRepository &&
                    repo.GithubAssetName == item.GithubAssetName);
                if (source != null)
                {
                    source.InstalledVersion = item.Version;
                }
            }
        }

        _configService.Save();
        return installedNow;
    }

    private async void OnAutoDetectPathClick(object? sender, RoutedEventArgs e)
    {
        string? detected = SteamDetectorService.FindGameDirectory();
        if (!string.IsNullOrEmpty(detected))
        {
            GamePathBox.Text = detected;
            _configService.Config.GamePath = detected;
            _configService.Save();
            await ReloadAllAsync();
            ShowToast("gorilla tag dir detected");
        }
        else
        {
            ShowToast("couldnt find gorilla tag dir please set manually");
        }
    }

    private async void OnInstallBepInExClick(object? sender, RoutedEventArgs e)
    {
        InstallBepInExButton.IsEnabled = false;
        try
        {
            string version = await _bepInExInstallerService.InstallLatestAsync(
                _configService.Config.GamePath);
            var status = SteamDetectorService.VerifyBepInEx(_configService.Config.GamePath);
            if (!status.IsBepInExValid)
            {
                throw new InvalidOperationException(
                    $"BepInEx installation completed, but verification failed: {status.Reason}");
            }

            await ReloadAllAsync();
            ShowToast($"BepInEx {version} installed");
        }
        catch (Exception ex)
        {
            ShowToast($"Could not install BepInEx: {ex.Message}", true);
        }
        finally
        {
            InstallBepInExButton.IsEnabled = true;
        }
    }

    private async void OnBrowsePathClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Gorilla Tag Installation Folder"
        });

        if (folders.Count > 0)
        {
            string path = folders[0].Path.LocalPath;
            GamePathBox.Text = path;
            _configService.Config.GamePath = path;
            _configService.Save();
            await ReloadAllAsync();
        }
    }

    private void UpdateThemeUI()
    {
        string mode = (_configService.Config.ThemeMode ?? "dark").ToLowerInvariant();
        string palette = (_configService.Config.ThemePalette ?? "pink").ToLowerInvariant();
        if (palette == "purple") palette = "pink";
        bool isDark = mode == "dark";

        if (DarkModeButton != null && LightModeButton != null)
        {
            SetButtonActive(DarkModeButton, isDark);
            SetButtonActive(LightModeButton, !isDark);
        }

        var paletteBtns = new (Button? Btn, string Id)[]
        {
            (PalettePinkBtn, "pink"),
            (PaletteBlueBtn, "blue"),
            (PaletteGreenBtn, "green"),
            (PaletteAmberBtn, "amber"),
            (PaletteRedBtn, "red"),
            (PaletteTealBtn, "teal")
        };

        foreach (var (btn, id) in paletteBtns)
        {
            if (btn != null)
            {
                SetButtonActive(btn, palette == id);
            }
        }
    }

    private void SetButtonActive(Button btn, bool isActive)
    {
        if (isActive)
        {
            btn.Background = GetResourceBrush("PrimaryContainer") ?? SolidColorBrush.Parse("#4F378A");
            btn.Foreground = GetResourceBrush("OnPrimaryContainer") ?? SolidColorBrush.Parse("#EADDFF");
            btn.BorderBrush = GetResourceBrush("PrimaryPurple") ?? SolidColorBrush.Parse("#D0BCFF");
            btn.BorderThickness = new Thickness(1.5);
        }
        else
        {
            btn.Background = GetResourceBrush("SearchBgBrush") ?? SolidColorBrush.Parse("#25232A");
            btn.Foreground = GetResourceBrush("TextSecondary") ?? SolidColorBrush.Parse("#CAC4D0");
            btn.BorderBrush = Brushes.Transparent;
            btn.BorderThickness = new Thickness(0);
        }
    }

    private IBrush? GetResourceBrush(string key)
    {
        if (Application.Current?.Resources.TryGetResource(key, null, out var b) == true && b is IBrush brush)
            return brush;
        return null;
    }

    private void OnThemeModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string mode)
        {
            _configService.Config.ThemeMode = mode;
            _configService.Save();

            ThemeService.ApplyTheme(mode, _configService.Config.ThemePalette);
            UpdateThemeUI();
            BuildFilterDropdown();

            ShowToast(mode == "dark" ? "Dark mode enabled" : "Light mode enabled");
        }
    }

    private void OnPaletteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string palette)
        {
            ApplyPalette(palette);
        }
    }

    public void ApplyPalette(string palette)
    {
        _configService.Config.ThemePalette = palette;
        _configService.Save();

        ThemeService.ApplyTheme(_configService.Config.ThemeMode, palette);
        UpdateThemeUI();
        BuildFilterDropdown();

        string colorName = palette.ToLowerInvariant() switch
        {
            "pink" or "purple" => "pink",
            "blue" => "blue",
            "green" => "green",
            "amber" => "amber",
            "red" => "red",
            "teal" => "teal",
            _ => palette.ToLowerInvariant()
        };
        ShowToast($"theme set to {colorName}");
    }

    private async void OnRepoDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RepoItem repo)
        {
            _configService.Config.Repositories.Remove(repo);
            _configService.Save();
            ConfiguredRepos.Remove(repo);
            BuildFilterDropdown();
            await ReloadAllAsync();
        }
    }

    private async void OnAddRepoClick(object? sender, RoutedEventArgs e)
    {
        string url = (NewRepoUrlBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(url)) return;

        if (Uri.TryCreate(url, UriKind.Absolute, out var suppliedUri) &&
            suppliedUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            ShowToast("Use the Mods + button to install GitHub release assets.", true);
            return;
        }

        var newRepo = new RepoItem
        {
            Url = url,
            Enabled = true
        };
        _configService.Config.Repositories.Add(newRepo);
        _configService.Save();
        NewRepoUrlBox.Text = "";
        BuildFilterDropdown();
        await ReloadAllAsync();
    }

    private void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string args = _configService.Config.LaunchArgs;
            if (!EnsureLinuxBepInExLaunchOptions())
            {
                return;
            }
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd",
                    ArgumentList = { "/c", "start", "", "steam://rungameid/1533390" },
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "steam",
                    Arguments = string.IsNullOrWhiteSpace(args)
                        ? "steam://rungameid/1533390"
                        : $"-applaunch 1533390 {args}",
                    UseShellExecute = false
                };

                try
                {
                    if (Process.Start(psi) == null)
                    {
                        throw new InvalidOperationException("Steam did not start.");
                    }
                }
                catch (Exception steamException)
                {
                    try
                    {
                        if (Process.Start(new ProcessStartInfo
                            {
                                FileName = "xdg-open",
                                ArgumentList = { "steam://rungameid/1533390" },
                                UseShellExecute = false
                            }) == null)
                        {
                            throw new InvalidOperationException("The game link did not open.");
                        }
                    }
                    catch (Exception openException)
                    {
                        throw new InvalidOperationException(
                            $"Could not open Steam: {steamException.Message}; " +
                            $"could not open the game link: {openException.Message}",
                            openException);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Could not launch Gorilla Tag: {ex.Message}", true);
        }
    }

    private bool EnsureLinuxBepInExLaunchOptions()
    {
        if (!OperatingSystem.IsLinux() ||
            !SteamDetectorService.VerifyBepInEx(_configService.Config.GamePath).IsBepInExValid)
        {
            return true;
        }

        try
        {
            if (SteamLaunchOptionsService.EnsureBepInExLaunchOption())
            {
                ShowToast("Set Steam's launch options to load BepInEx.");
            }
            return true;
        }
        catch (Exception ex)
        {
            ShowToast($"Could not set BepInEx's Steam launch option: {ex.Message}", true);
            return false;
        }
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string gamePath = _configService.Config.GamePath;
            if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
            {
                ShowToast("Set a valid Gorilla Tag folder in Settings first.", true);
                return;
            }

            string targetDir = gamePath;
            string modsDir = _modService.GetModsDirectory(_configService.Config.GamePath);
            if (Directory.Exists(modsDir))
            {
                targetDir = modsDir;
            }

            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetDir,
                    UseShellExecute = true
                });
            }
            else
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add(targetDir);
                Process.Start(startInfo);
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Could not open folder: {ex.Message}", true);
        }
    }
}
