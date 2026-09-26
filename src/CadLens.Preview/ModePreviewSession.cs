using System.ComponentModel;
using System.Windows.Threading;
using CadLens.Lenses;
using CadLens.UI;
using CommunityToolkit.Mvvm.Input;

namespace CadLens.Preview;

internal sealed class ModePreviewSession
{
    private readonly string _scenario;
    private Task? _closeTask;
    private bool _closing;
    private bool _allowClose;

    public ModePreviewSession(bool useActionStrip, string scenario)
    {
        _scenario = scenario;
        var source = new PreviewSnapshotSource(scenario == "Empty", scenario == "Error");
        var delay = scenario == "Slow" ? 1800 : 180;
        Actions = new ModePreviewActions(new PreviewLayersActions(new LayersLensProvider(source), delay));
        Model = new LayersViewModel(Actions);
        Explorer = new ExplorerViewModel([new ModePreviewLens(Model, useActionStrip)]);
        Window = new ExplorerWindow(Explorer)
        {
            Title = useActionStrip ? "CAD Lens - Action strip" : "CAD Lens - Action rows",
            ShowInTaskbar = true,
            Left = 450,
            Top = 80
        };
        Window.Closing += OnClosing;
    }

    public ModePreviewActions Actions { get; }
    public LayersViewModel Model { get; }
    public ExplorerViewModel Explorer { get; }
    public ExplorerWindow Window { get; }
    public Task Startup { get; private set; } = Task.CompletedTask;

    public void Show()
    {
        Window.Show();
        Startup = InitializeAsync();
    }

    public Task CloseAsync() => _closeTask ??= CloseCoreAsync();

    private async Task InitializeAsync()
    {
        if (_scenario == "Compact")
            return;

        await Explorer.ToggleLensCommand.ExecuteAsync(Explorer.Lenses[0]);
        var depth = _scenario switch { "Layer" => 1, "Type" => 2, "Object" => 3, _ => 0 };

        for (var level = 0; level < depth && !_closing && !Model.Items.IsEmpty; level++)
            await Model.EnterCommand.ExecuteAsync(Model.Items[0]);
    }

    private async Task CloseCoreAsync()
    {
        _closing = true;
        IAsyncRelayCommand[] commands =
        [
            Explorer.ToggleLensCommand, Model.ReadCommand, Model.EnterCommand,
            Model.BackCommand, Model.RootCommand, Model.BreadcrumbCommand,
            Model.PreviousCommand, Model.NextCommand, Model.ToggleFilterCommand,
            Model.FocusCommand, Model.SelectCommand, Model.HighlightCommand, Model.ResetCommand,
            Model.ToggleAutoFocusCommand, Model.ToggleAutoSelectCommand, Model.ToggleAutoHighlightCommand
        ];
        var pending = commands.Select(command => command.ExecutionTask ?? Task.CompletedTask).Append(Startup).ToArray();
        Explorer.Dispose();
        Model.Dispose();

        try
        {
            await Task.WhenAll(pending);
        }
        finally
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            _allowClose = true;
            Window.Close();
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_allowClose)
            return;

        args.Cancel = true;
        await CloseAsync();
    }
}
