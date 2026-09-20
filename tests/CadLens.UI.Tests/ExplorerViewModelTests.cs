using System.Collections.Immutable;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CadLens.Core;
using Common;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Toolkit command lifetime and standalone layout verification.</summary>
public sealed class ExplorerViewModelTests
{
    /// <summary>A pending action disables all commands and close rejects late results.</summary>
    [Fact]
    public async Task PendingCommandsAreDisabledAndLateResultsIgnored()
    {
        var actions = new Actions();
        var viewModel = new ExplorerViewModel(actions);
        var running = viewModel.EmphasizeCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.ReadCommand.CanExecute(null));
        Assert.False(viewModel.ClearCommand.CanExecute(null));
        viewModel.Dispose();
        Assert.True(actions.Token.IsCancellationRequested);
        actions.Completion.SetResult("late result");
        await running;
        Assert.DoesNotContain("late result", viewModel.Status);
        Assert.False(viewModel.EmphasizeCommand.CanExecute(null));
    }

    /// <summary>Changing documents prevents an old result from overwriting the new state.</summary>
    [Fact]
    public async Task ContextChangeRejectsLateInventory()
    {
        var actions = new Actions { DelayInventory = true };
        using var viewModel = new ExplorerViewModel(actions);
        var running = viewModel.ReadCommand.ExecuteAsync(null);
        viewModel.ResetContext();
        actions.InventoryCompletion.SetResult(new HostResult<LensPresentation>.Success(Presentation()));
        await running;
        Assert.Empty(viewModel.Groups);
        Assert.Contains("context changed", viewModel.Status);
    }

    /// <summary>Reported failures are visible and restore command availability.</summary>
    [Fact]
    public async Task FailuresAreShownAndCommandsRecover()
    {
        var actions = new Actions();
        using var viewModel = new ExplorerViewModel(actions);
        var running = viewModel.EmphasizeCommand.ExecuteAsync(null);
        actions.Completion.SetException(new InvalidOperationException("fixture failure"));
        await running;
        Assert.Contains("fixture failure", viewModel.Status);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.ReadCommand.CanExecute(null));
    }

    /// <summary>Renders the actual XAML with long labels and overflowing inventory on an STA thread.</summary>
    [Theory]
    [InlineData(370, 660, false)]
    [InlineData(300, 450, false)]
    [InlineData(370, 660, true)]
    [InlineData(300, 450, true)]
    public void RenderPanelWithLargeInventory(int width, int height, bool details)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var viewModel = new ExplorerViewModel(new Actions());
                viewModel.ReadCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                Assert.Equal(24, viewModel.GroupCount);
                if (details)
                    viewModel.EnterCommand.Execute(viewModel.Items[0]);

                var window = new ExplorerWindow(viewModel);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(width, height));
                content.Arrange(new Rect(0, 0, width, height));
                content.UpdateLayout();
                const double dpi = 96;
                var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"panel-{width}x{height}-{details}.png"));
                encoder.Save(output);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF render did not finish.");
        Assert.Null(failure);
    }

    private static LensPresentation Presentation()
    {
        var groups = Enumerable.Range(1, 24).Select(index => new LensNode(
            index.ToString(),
            index == 1 ? "Site — roads and pedestrian connections — existing conditions" : $"Drawing group {index:00}",
            [new HostObjectId(index)],
            [],
            [new DetailField("Category", "Example category"), new DetailField("Visibility", "Hidden: frozen. Inclusion does not reveal this object.")],
            [])).ToImmutableArray();
        return new LensPresentation("fixture", "Layers", "Model space", groups, [new BooleanFilter("frozen", "Include frozen", "Include hidden frozen objects", IconRole.Snowflake), new BooleanFilter("off", "Include off", "Include hidden switched-off objects", IconRole.Lightbulb)], "No objects.");
    }

    private sealed class Actions : IExplorerActions
    {
        internal bool DelayInventory { get; init; }
        internal CancellationToken Token { get; private set; }

        internal TaskCompletionSource<string> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource<HostResult<LensPresentation>> InventoryCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<HostResult<LensPresentation>> ReadAsync(IReadOnlySet<string> enabledFilters, CancellationToken cancellationToken) => DelayInventory
            ? InventoryCompletion.Task
            : Task.FromResult<HostResult<LensPresentation>>(new HostResult<LensPresentation>.Success(Presentation()));

        public Task<string> EmphasizeAsync(CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            return Completion.Task;
        }

        public Task<string> ClearAsync(CancellationToken cancellationToken) => Task.FromResult("Cleared.");
    }
}