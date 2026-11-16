using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace gtmm.Services;

public class ThemePaletteDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DotColor { get; set; } = "";

    public string AppBg { get; set; } = "";
    public string CardBg { get; set; } = "";
    public string CardBorder { get; set; } = "";
    public string PillBg { get; set; } = "";
    public string SearchBg { get; set; } = "";
    public string Primary { get; set; } = "";
    public string PrimaryContainer { get; set; } = "";
    public string OnPrimaryContainer { get; set; } = "";
    public string SecondaryContainer { get; set; } = "";
    public string OnSecondaryContainer { get; set; } = "";
    public string TextPrimary { get; set; } = "";
    public string TextSecondary { get; set; } = "";
    public string TextTertiary { get; set; } = "";
    public string PlayIcon { get; set; } = "";
}

public static class ThemeService
{
    public static string CurrentMode { get; private set; } = "dark";
    public static string CurrentPalette { get; private set; } = "pink";

    public static readonly List<(string Id, string Name, string DotColor)> AvailablePalettes = new()
    {
        ("pink", "pink", "#6750A4"),
        ("blue", "blue", "#0061A4"),
        ("green", "green", "#006D44"),
        ("amber", "amber", "#775A00"),
        ("red", "red", "#BA1A1A"),
        ("teal", "teal", "#006874")
    };

    public static void ApplyTheme(string? mode, string? palette)
    {
        mode = string.IsNullOrWhiteSpace(mode) ? "dark" : mode.ToLowerInvariant();
        palette = string.IsNullOrWhiteSpace(palette) ? "pink" : palette.ToLowerInvariant();

        palette = palette switch
        {
            "purple" => "pink",
            "midnight" => "pink",
            "emerald" => "green",
            "rose" => "red",
            "cyber" => "amber",
            _ => palette
        };

        CurrentMode = mode == "light" ? "light" : "dark";
        CurrentPalette = palette;

        var app = Application.Current;
        if (app == null) return;

        bool isDark = CurrentMode == "dark";
        app.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;

        var p = GetMaterialPalette(CurrentPalette, isDark);

        SetBrush(app, "AppBgBrush", p.AppBg);
        SetBrush(app, "CardBgBrush", p.CardBg);
        SetBrush(app, "CardBorderBrush", p.CardBorder);
        SetBrush(app, "PillBgBrush", p.PillBg);
        SetBrush(app, "SearchBgBrush", p.SearchBg);
        SetBrush(app, "PrimaryPurple", p.Primary);
        SetBrush(app, "PrimaryContainer", p.PrimaryContainer);
        SetBrush(app, "OnPrimaryContainer", p.OnPrimaryContainer);
        SetBrush(app, "SecondaryContainer", p.SecondaryContainer);
        SetBrush(app, "OnSecondaryContainer", p.OnSecondaryContainer);
        SetBrush(app, "TextPrimary", p.TextPrimary);
        SetBrush(app, "TextSecondary", p.TextSecondary);
        SetBrush(app, "TextTertiary", p.TextTertiary);
        SetBrush(app, "PlayIconBrush", p.PlayIcon);
    }

    private static void SetBrush(Application app, string key, string hex)
    {
        try
        {
            var brush = new SolidColorBrush(Color.Parse(hex));
            app.Resources[key] = brush;
        }
        catch { }
    }

    private static ThemePaletteDefinition GetMaterialPalette(string palette, bool isDark)
    {
        if (isDark)
        {
            return palette switch
            {
                "blue" => new ThemePaletteDefinition
                {
                    Id = "blue", Name = "Material Blue", DotColor = "#0061A4",
                    AppBg = "#101418",
                    CardBg = "#191C20",
                    CardBorder = "#43474E",
                    PillBg = "#1D2024",
                    SearchBg = "#272A2E",
                    Primary = "#9ECAFF",
                    PrimaryContainer = "#00497D",
                    OnPrimaryContainer = "#D1E4FF",
                    SecondaryContainer = "#3B4858",
                    OnSecondaryContainer = "#D7E3F7",
                    TextPrimary = "#E1E2E8",
                    TextSecondary = "#C3C7D0",
                    TextTertiary = "#8D9199",
                    PlayIcon = "#9ECAFF"
                },
                "green" => new ThemePaletteDefinition
                {
                    Id = "green", Name = "Material Green", DotColor = "#006D44",
                    AppBg = "#0F1511",
                    CardBg = "#171D19",
                    CardBorder = "#414942",
                    PillBg = "#1B211D",
                    SearchBg = "#252C27",
                    Primary = "#74DAA3",
                    PrimaryContainer = "#005232",
                    OnPrimaryContainer = "#90F7BE",
                    SecondaryContainer = "#374B3D",
                    OnSecondaryContainer = "#D1E8D5",
                    TextPrimary = "#DEE4DE",
                    TextSecondary = "#C1C9C0",
                    TextTertiary = "#8B938B",
                    PlayIcon = "#74DAA3"
                },
                "amber" => new ThemePaletteDefinition
                {
                    Id = "amber", Name = "Material Amber", DotColor = "#775A00",
                    AppBg = "#16130E",
                    CardBg = "#1E1B16",
                    CardBorder = "#4D4639",
                    PillBg = "#231F1A",
                    SearchBg = "#2E2A24",
                    Primary = "#EFC148",
                    PrimaryContainer = "#5A4300",
                    OnPrimaryContainer = "#FFDF9E",
                    SecondaryContainer = "#51462A",
                    OnSecondaryContainer = "#F2E1BB",
                    TextPrimary = "#E9E1D8",
                    TextSecondary = "#D0C5B4",
                    TextTertiary = "#998F80",
                    PlayIcon = "#EFC148"
                },
                "red" => new ThemePaletteDefinition
                {
                    Id = "red", Name = "Material Red", DotColor = "#BA1A1A",
                    AppBg = "#1A1110",
                    CardBg = "#231918",
                    CardBorder = "#534341",
                    PillBg = "#271D1C",
                    SearchBg = "#322826",
                    Primary = "#FFB4AB",
                    PrimaryContainer = "#93000A",
                    OnPrimaryContainer = "#FFDAD6",
                    SecondaryContainer = "#5D3F3C",
                    OnSecondaryContainer = "#FFDAD6",
                    TextPrimary = "#F1DFDD",
                    TextSecondary = "#D8C2BF",
                    TextTertiary = "#A08C8A",
                    PlayIcon = "#FFB4AB"
                },
                "teal" => new ThemePaletteDefinition
                {
                    Id = "teal", Name = "Material Teal", DotColor = "#006874",
                    AppBg = "#0E1415",
                    CardBg = "#171D1E",
                    CardBorder = "#3F484A",
                    PillBg = "#1B2122",
                    SearchBg = "#252B2C",
                    Primary = "#82D3E0",
                    PrimaryContainer = "#004F58",
                    OnPrimaryContainer = "#9EEFFD",
                    SecondaryContainer = "#334B4F",
                    OnSecondaryContainer = "#CCE8ED",
                    TextPrimary = "#DEE3E4",
                    TextSecondary = "#BFC8CA",
                    TextTertiary = "#899294",
                    PlayIcon = "#82D3E0"
                },
                "pink" or _ => new ThemePaletteDefinition
                {
                    Id = "pink", Name = "pink", DotColor = "#6750A4",
                    AppBg = "#141218",
                    CardBg = "#1D1B20",
                    CardBorder = "#49454F",
                    PillBg = "#211F26",
                    SearchBg = "#2B2930",
                    Primary = "#D0BCFF",
                    PrimaryContainer = "#4F378A",
                    OnPrimaryContainer = "#EADDFF",
                    SecondaryContainer = "#4A4458",
                    OnSecondaryContainer = "#E8DEF8",
                    TextPrimary = "#E6E1E5",
                    TextSecondary = "#CAC4D0",
                    TextTertiary = "#938F99",
                    PlayIcon = "#D0BCFF"
                }
            };
        }
        else
        {
            return palette switch
            {
                "blue" => new ThemePaletteDefinition
                {
                    Id = "blue", Name = "Material Blue", DotColor = "#0061A4",
                    AppBg = "#FDF8FD",
                    CardBg = "#F2F5FD",
                    CardBorder = "#C3C7D0",
                    PillBg = "#ECF0F9",
                    SearchBg = "#E6EBF3",
                    Primary = "#0061A4",
                    PrimaryContainer = "#D1E4FF",
                    OnPrimaryContainer = "#001D36",
                    SecondaryContainer = "#D7E3F7",
                    OnSecondaryContainer = "#101C2B",
                    TextPrimary = "#191C20",
                    TextSecondary = "#43474E",
                    TextTertiary = "#73777F",
                    PlayIcon = "#0061A4"
                },
                "green" => new ThemePaletteDefinition
                {
                    Id = "green", Name = "Material Green", DotColor = "#006D44",
                    AppBg = "#F5FBF3",
                    CardBg = "#EFF6ED",
                    CardBorder = "#C1C9C0",
                    PillBg = "#EAF1E8",
                    SearchBg = "#E4EBE2",
                    Primary = "#006D44",
                    PrimaryContainer = "#90F7BE",
                    OnPrimaryContainer = "#002111",
                    SecondaryContainer = "#D1E8D5",
                    OnSecondaryContainer = "#0C1F13",
                    TextPrimary = "#171D18",
                    TextSecondary = "#414942",
                    TextTertiary = "#717971",
                    PlayIcon = "#006D44"
                },
                "amber" => new ThemePaletteDefinition
                {
                    Id = "amber", Name = "Material Amber", DotColor = "#775A00",
                    AppBg = "#FFF8F1",
                    CardBg = "#FCF2E6",
                    CardBorder = "#D0C5B4",
                    PillBg = "#F6ECE0",
                    SearchBg = "#F0E6DB",
                    Primary = "#775A00",
                    PrimaryContainer = "#FFDF9E",
                    OnPrimaryContainer = "#251A00",
                    SecondaryContainer = "#F2E1BB",
                    OnSecondaryContainer = "#231B04",
                    TextPrimary = "#1E1B16",
                    TextSecondary = "#4D4639",
                    TextTertiary = "#7F7667",
                    PlayIcon = "#775A00"
                },
                "red" => new ThemePaletteDefinition
                {
                    Id = "red", Name = "Material Red", DotColor = "#BA1A1A",
                    AppBg = "#FFF8F6",
                    CardBg = "#FFF0EE",
                    CardBorder = "#D8C2BF",
                    PillBg = "#FCEAE7",
                    SearchBg = "#F6E4E1",
                    Primary = "#BA1A1A",
                    PrimaryContainer = "#FFDAD6",
                    OnPrimaryContainer = "#410002",
                    SecondaryContainer = "#FFDAD6",
                    OnSecondaryContainer = "#2C1512",
                    TextPrimary = "#231918",
                    TextSecondary = "#534341",
                    TextTertiary = "#857371",
                    PlayIcon = "#BA1A1A"
                },
                "teal" => new ThemePaletteDefinition
                {
                    Id = "teal", Name = "Material Teal", DotColor = "#006874",
                    AppBg = "#F6FAFA",
                    CardBg = "#F1F7F8",
                    CardBorder = "#BFC8CA",
                    PillBg = "#EBF1F2",
                    SearchBg = "#E5EBEC",
                    Primary = "#006874",
                    PrimaryContainer = "#9EEFFD",
                    OnPrimaryContainer = "#001F24",
                    SecondaryContainer = "#CCE8ED",
                    OnSecondaryContainer = "#051F23",
                    TextPrimary = "#191C1D",
                    TextSecondary = "#3F484A",
                    TextTertiary = "#6F797A",
                    PlayIcon = "#006874"
                },
                "pink" or _ => new ThemePaletteDefinition
                {
                    Id = "pink", Name = "pink", DotColor = "#6750A4",
                    AppBg = "#FEF7FF",
                    CardBg = "#F7F2FA",
                    CardBorder = "#CAC4D0",
                    PillBg = "#F3EDF7",
                    SearchBg = "#ECE6F0",
                    Primary = "#6750A4",
                    PrimaryContainer = "#EADDFF",
                    OnPrimaryContainer = "#21005D",
                    SecondaryContainer = "#E8DEF8",
                    OnSecondaryContainer = "#1D192B",
                    TextPrimary = "#1D1B20",
                    TextSecondary = "#49454F",
                    TextTertiary = "#79747E",
                    PlayIcon = "#4F378A"
                }
            };
        }
    }
}
