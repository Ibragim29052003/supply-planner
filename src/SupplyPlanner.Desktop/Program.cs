using Avalonia;

namespace SupplyPlanner.Desktop;

internal static class Program
{
    public static string? OpenPath { get; private set; }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        { Console.WriteLine("SupplyPlanner Desktop: --open <scenario.json|project.supply.json>\nF1: справка; Cmd/Ctrl+O: открыть; Cmd/Ctrl+S: сохранить; Cmd/Ctrl+Enter: рассчитать.\nДля расчёта без окна используйте SupplyPlanner.Cli --help."); return 0; }
        if (args.Length > 0)
        {
            if (args.Length != 2 || args[0] != "--open") { Console.Error.WriteLine("Используйте --open путь или --help."); return 2; }
            OpenPath = Path.GetFullPath(args[1]);
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        return 0;
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
