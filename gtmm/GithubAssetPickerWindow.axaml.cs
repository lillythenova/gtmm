using System.Collections.Generic;
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using gtmm.Services;

namespace gtmm;

public partial class GithubAssetPickerWindow : Window
{
    public GithubReleaseAsset? SelectedAsset { get; private set; }

    public GithubAssetPickerWindow() : this(Array.Empty<GithubReleaseAsset>())
    {
    }

    public GithubAssetPickerWindow(IReadOnlyList<GithubReleaseAsset> assets)
    {
        InitializeComponent();
        if (assets.Count > 0)
        {
            SelectedAsset = assets[0];
        }
        UpdateSelectedAssetText();
        AssetItemsControl.ItemsSource = assets;
        AssetPopup.PlacementTarget = AssetSelectorButton;
    }

    private void OnTrackClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedAsset != null)
        {
            Close();
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnAssetSelectorClick(object? sender, RoutedEventArgs e)
    {
        AssetPopup.IsOpen = !AssetPopup.IsOpen;
    }

    private void OnAssetOptionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GithubReleaseAsset asset })
        {
            SelectedAsset = asset;
            UpdateSelectedAssetText();
            AssetPopup.IsOpen = false;
        }
    }

    private void UpdateSelectedAssetText()
    {
        SelectedAssetNameText.Text = SelectedAsset?.Name ?? "";
    }
}
