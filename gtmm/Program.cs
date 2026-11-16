using Avalonia;
using System;
using System.Reflection;
using gtmm.Services;

namespace gtmm;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args != null)
        {
            foreach (var arg in args)
            {
                if (arg == "--version" || arg == "-v")
                {
                    var version = typeof(Program).Assembly
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                        .InformationalVersion ?? "unknown";
                    Console.WriteLine($"GTMM {version} (Gorilla Tag Edition)");
                    return 0;
                }
                if (arg == "--help" || arg == "-h")
                {
                    Console.WriteLine("Gorilla Tag Mod Manager (GTMM)");
                    Console.WriteLine("Usage: gtmm [options]");
                    Console.WriteLine("Options:");
                    Console.WriteLine("  --version, -v       Show version information");
                    Console.WriteLine("  --help, -h          Show help options");
                    Console.WriteLine("  --test              Run internal self-tests and exit");
                    Console.WriteLine("  --tab=<name>        Initial tab (mods, repos, settings)");
                    return 0;
                }
                if (arg == "--test")
                {
                    Console.WriteLine("[GTMM] Running self-test diagnostics...");
                    var cfg = new ConfigService();
                    cfg.Load();
                    Console.WriteLine($"[GTMM] Config loaded: {cfg.Config.Repositories.Count} repositories configured.");
                    string? gameDir = SteamDetectorService.FindGameDirectory(cfg.Config.GamePath);
                    var loaderStatus = SteamDetectorService.VerifyBepInEx(gameDir);
                    Console.WriteLine($"[GTMM] Gorilla Tag Directory: {gameDir ?? "Not found (manual selection required)"}");
                    Console.WriteLine($"[GTMM] BepInEx Valid: {loaderStatus.IsBepInExValid}");
                    Console.WriteLine("[GTMM] Self-test diagnostics completed successfully.");
                    return 0;
                }
            }
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args ?? Array.Empty<string>());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Application error: {ex.Message}");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
