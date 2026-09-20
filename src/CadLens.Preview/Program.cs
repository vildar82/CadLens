using System.Windows;
using CadLens.Lenses;
using CadLens.UI;

namespace CadLens.Preview;

internal static class Program
{
    private const int NormalDelayMilliseconds = 180;
    private const int SlowDelayMilliseconds = 1800;

    [STAThread]
    private static void Main(string[] args)
    {
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
}