using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using gtmm.Services;

namespace gtmm;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var configService = new ConfigService();
            configService.Load();
            ThemeService.ApplyTheme(configService.Config.ThemeMode, configService.Config.ThemePalette);

            string initTab = "mods";
            bool openFilter = false;
            string? customToast = null;
            if (desktop.Args != null)
            {
                foreach (var arg in desktop.Args)
                {
                    if (arg.StartsWith("--tab="))
                    {
                        initTab = arg.Substring(6);
                    }
                    else if (arg == "--open-filter")
                    {
                        openFilter = true;
                    }
                    else if (arg.StartsWith("--test-toast="))
                    {
                        customToast = arg.Substring(13);
                    }
                    else if (arg == "--test-fail-toast")
                    {
                        customToast = "couldnt find gorilla tag dir please set manually";
                    }
                }
            }
            var win = new MainWindow(initTab);
            if (openFilter)
            {
                win.Loaded += (s, e) => win.OpenFilterDropdownForTest();
            }
            if (!string.IsNullOrEmpty(customToast))
            {
                win.Loaded += (s, e) => win.ShowToast(customToast);
            }
            desktop.MainWindow = win;
        }

        base.OnFrameworkInitializationCompleted();
    }
}