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
        await viewModel.ToggleLensCommand.ExecuteAsync(null);
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
        var running = viewModel.ToggleLensCommand.ExecuteAsync(null);
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
        await viewModel.ToggleLensCommand.ExecuteAsync(null);
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
                viewModel.ToggleLensCommand.ExecuteAsync(null).GetAwaiter().GetResult();
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

    /// <summary>The real window collapses to its bar and restores resized content without moving its origin.</summary>
    [Fact]
    public void WindowRestoresExpandedDimensions()
    {
        RunOnSta(() =>
        {
            using var model = new ExplorerViewModel(new Actions());
            var window = new ExplorerWindow(model) { Left = 20, Top = 20 };
            Assert.Equal(52, window.Height);
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
            model.ToggleLensCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.Equal(660, window.Height);
            Assert.Equal(ResizeMode.CanResizeWithGrip, window.ResizeMode);
            window.Width = 420;
            window.Height = 700;
            model.ToggleLensCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.Equal(52, window.Height);
            Assert.Equal(300, window.Width);
            Assert.Equal(20, window.Left);
            Assert.Equal(20, window.Top);
            model.ToggleLensCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.Equal(420, window.Width);
            Assert.Equal(700, window.Height);
            model.ToggleLensCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            var workArea = SystemParameters.WorkArea;
            window.Left = workArea.Right - 20;
            window.Top = workArea.Bottom - 20;
            model.ToggleLensCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.True(window.Left + window.Width <= workArea.Right);
            Assert.True(window.Top + window.Height <= workArea.Bottom);
            window.Close();
        });
    }

    /// <summary>Compact states render the actual bar with accessible toggle and close controls.</summary>
    [Theory]
    [InlineData("inactive")]
    [InlineData("disabled")]
    [InlineData("pending")]
    [InlineData("failed")]
    public void RenderCompactStates(string state)
    {
        RunOnSta(() =>
        {
            var actions = new Actions();
            using var model = new ExplorerViewModel(actions);

            if (state is "pending" or "failed")
            {
                model.ToggleLensCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                actions.PendingCleanup = new TaskCompletionSource<HostResult<bool>>();

                if (state == "failed")
                    actions.PendingCleanup.SetResult(new HostResult<bool>.Unavailable("Fixture cleanup unavailable."));

                _ = model.ToggleLensCommand.ExecuteAsync(null);
            }

            if (state == "disabled")
                model.ResetContext(false);

            var window = new ExplorerWindow(model);
            var content = (FrameworkElement)window.Content;
            var size = new Size(window.Width, window.Height);
            content.Measure(size);
            content.Arrange(new Rect(size));
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"panel-compact-{state}.png"));
            encoder.Save(output);
            window.Close();
            actions.PendingCleanup?.TrySetResult(new HostResult<bool>.Success(true));
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF check did not finish.");
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
            [LensAction.Focus])).ToImmutableArray();
        return new LensPresentation("fixture", "Layers", "Model space", groups, [new BooleanFilter("frozen", "Include frozen", "Include hidden frozen objects", IconRole.Snowflake), new BooleanFilter("off", "Include off", "Include hidden switched-off objects", IconRole.Lightbulb)], "No objects.");
    }

    private sealed class Actions : IExplorerActions
    {
        internal TaskCompletionSource<HostResult<bool>>? PendingCleanup { get; set; }
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

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken) => PendingCleanup?.Task ?? Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

        public Task<string> EmphasizeObjectsAsync(ImmutableArray<HostObjectId> objects, CancellationToken cancellationToken) =>
            Task.FromResult("Selection updated.");

        public Task<string> FocusAsync(ImmutableArray<HostObjectId> objects, CancellationToken cancellationToken) =>
            Task.FromResult("Focused.");
    }
}