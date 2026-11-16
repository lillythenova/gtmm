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
        AssetComboBox.ItemsSource = assets;
        if (assets.Count > 0)
        {
            AssetComboBox.SelectedIndex = 0;
        }
    }

    private void OnTrackClick(object? sender, RoutedEventArgs e)
    {
        SelectedAsset = AssetComboBox.SelectedItem as GithubReleaseAsset;
        if (SelectedAsset != null)
        {
            Close();
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
