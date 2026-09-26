using System.Windows;
using CadLens.Lenses;
using CadLens.UI;
using System.IO;

namespace CadLens.Preview;

internal static class Program
{
    private const int NormalDelayMilliseconds = 180;
    private const int SlowDelayMilliseconds = 1800;

    [STAThread]
    private static void Main(string[] args)
    {
        var captureIndex = Array.IndexOf(args, "--capture-modes");

        if (captureIndex >= 0 && captureIndex + 1 < args.Length)
        {
            RunCapture(args[captureIndex + 1]);
            return;
        }

        if (args.Contains("--modes"))
        {
            new Application { ShutdownMode = ShutdownMode.OnMainWindowClose }.Run(new ModePreviewWindow());
            return;
        }

        var source = new PreviewSnapshotSource(args.Contains("--empty"), args.Contains("--error"));
        var delay = args.Contains("--slow") ? SlowDelayMilliseconds : NormalDelayMilliseconds;
        var actions = new PreviewLayersActions(new LayersLensProvider(source), delay);
        using var layers = new LayersViewModel(actions);
        using var explorer = new ExplorerViewModel([new LayersLens(layers)]);
        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new ExplorerWindow(explorer)
        {
            Title = "CAD Lens - standalone preview",
            ShowInTaskbar = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        window.Closed += (_, _) => explorer.Close(false);
        application.Run(window);
    }

    private static void RunCapture(string directory)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Startup += async (_, _) =>
        {
            try
            {
                await ModePreviewCapture.RunAsync(directory);
                application.Shutdown();
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), exception.ToString());
                application.Shutdown(1);
            }
        };
        application.Run();
    }
}
