using CadLens.Lenses;
using System.Collections.Immutable;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        var viewModel = new LayersViewModel(actions);
        await viewModel.ActivateAsync(CancellationToken.None);
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
        using var viewModel = new LayersViewModel(actions);
        var running = viewModel.ActivateAsync(CancellationToken.None);
        viewModel.ResetContext();
        actions.InventoryCompletion.SetResult(new HostResult<LayersPresentation>.Success(Presentation()));
        await running;
        Assert.Empty(viewModel.Groups);
        Assert.Contains("context changed", viewModel.Status);
    }

    /// <summary>Reported failures are visible and restore command availability.</summary>
    [Fact]
    public async Task FailuresAreShownAndCommandsRecover()
    {
        var actions = new Actions();
        using var viewModel = new LayersViewModel(actions);
        await viewModel.ActivateAsync(CancellationToken.None);
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
                using var viewModel = new LayersViewModel(new Actions());
                viewModel.ActivateAsync(CancellationToken.None).GetAwaiter().GetResult();
                Assert.Equal(24, viewModel.GroupCount);
                if (details)
                    viewModel.EnterCommand.Execute(viewModel.Items[0]);

                using var shell = new ExplorerViewModel([new LayersLens(viewModel)]);
                shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]).GetAwaiter().GetResult();
                var window = new ExplorerWindow(shell);
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
            using var model = new ExplorerViewModel([new LayersLens(new LayersViewModel(new Actions()))]);
            var window = new ExplorerWindow(model) { Left = 20, Top = 20 };
            Assert.Equal(52, window.Height);
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
            model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]).GetAwaiter().GetResult();
            Assert.Equal(660, window.Height);
            Assert.Equal(ResizeMode.CanResizeWithGrip, window.ResizeMode);
            window.Width = 420;
            window.Height = 700;
            model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]).GetAwaiter().GetResult();
            Assert.Equal(52, window.Height);
            Assert.Equal(300, window.Width);
            Assert.Equal(20, window.Left);
            Assert.Equal(20, window.Top);
            model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]).GetAwaiter().GetResult();
            Assert.Equal(420, window.Width);
            Assert.Equal(700, window.Height);
            model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]).GetAwaiter().GetResult();
            var workArea = SystemParameters.WorkArea;
            window.Left = workArea.Right - 20;
            window.Top = workArea.Bottom - 20;
            model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]).GetAwaiter().GetResult();
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
            using var model = new ExplorerViewModel([new LayersLens(new LayersViewModel(actions))]);

            if (state is "pending" or "failed")
            {
                model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]).GetAwaiter().GetResult();
                actions.PendingCleanup = new TaskCompletionSource<HostResult<bool>>();

                if (state == "failed")
                    actions.PendingCleanup.SetResult(new HostResult<bool>.Unavailable("Fixture cleanup unavailable."));

                _ = model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]);
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

    /// <summary>The real toolbar creates bound buttons for every registration and scrolls when they overflow.</summary>
    [Fact]
    public void RegisteredLensButtonsAreGeneratedAndScrollable()
    {
        RunOnSta(() =>
        {
            var counter = new CounterViewModel(new CounterService());
            using var model = new ExplorerViewModel([
                new LayersLens(new LayersViewModel(new Actions())),
                new CounterLens(counter),
                new CounterLens(new CounterViewModel(new CounterService()), "extra", "Extra module")]);
            var window = new ExplorerWindow(model);
            var content = (FrameworkElement)window.Content;
            var size = new Size(window.Width, window.Height);
            content.Measure(size);
            content.Arrange(new Rect(size));
            content.UpdateLayout();
            var buttons = Descendants(content).OfType<ToggleButton>()
                .Where(button => ReferenceEquals(button.Command, model.ToggleLensCommand)).ToArray();
            Assert.Equal(3, buttons.Length);
            Assert.Equal(model.Lenses, buttons.Select(button => (LensOption)button.CommandParameter));
            var scroller = Descendants(content).OfType<ScrollViewer>().First();
            Assert.True(scroller.ScrollableWidth > 0);
            scroller.ScrollToRightEnd();
            content.UpdateLayout();
            Assert.True(scroller.HorizontalOffset > 0);
            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(AppContext.BaseDirectory, "panel-multiple-lenses.png"));
            encoder.Save(output);
            buttons[1].Command.Execute(buttons[1].CommandParameter);
            Assert.True(model.Lenses[1].IsActive);
            Assert.IsType<CounterLensView>(model.ActiveView);
            Assert.Same(counter, model.ActiveView!.DataContext);
            content.Measure(new Size(window.Width, window.Height));
            content.Arrange(new Rect(0, 0, window.Width, window.Height));
            content.UpdateLayout();
            var increment = Descendants(model.ActiveView).OfType<Button>().Single();
            increment.Command.Execute(null);
            Assert.Equal(1, counter.Count);
            content.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            var counterBitmap = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32);
            counterBitmap.Render(content);
            var counterEncoder = new PngBitmapEncoder();
            counterEncoder.Frames.Add(BitmapFrame.Create(counterBitmap));
            using var counterOutput = File.Create(Path.Combine(AppContext.BaseDirectory, "panel-counter-lens.png"));
            counterEncoder.Save(counterOutput);
            window.Close();
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;

            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
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

    private static LayersPresentation Presentation()
    {
        var groups = Enumerable.Range(1, 24).Select(index => new LensNode(
            index.ToString(),
            index == 1 ? "Site — roads and pedestrian connections — existing conditions" : $"Drawing group {index:00}",
            [new TestEntityId(index)],
            [],
            [new DetailField("Category", "Example category"), new DetailField("Visibility", "Hidden: frozen. Inclusion does not reveal this object.")],
            [LensAction.Focus])).ToImmutableArray();
        return new LayersPresentation("Layers", "Model space", groups, [new BooleanFilter("frozen", "Include frozen", "Include hidden frozen objects", IconRole.Snowflake), new BooleanFilter("off", "Include off", "Include hidden switched-off objects", IconRole.Lightbulb)], "No objects.");
    }

    private sealed class Actions : ILayersActions
    {
        public void ClearImmediately(bool redraw) { }

        internal TaskCompletionSource<HostResult<bool>>? PendingCleanup { get; set; }
        internal bool DelayInventory { get; init; }
        internal CancellationToken Token { get; private set; }

        internal TaskCompletionSource<string> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource<HostResult<LayersPresentation>> InventoryCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<HostResult<LayersPresentation>> ReadAsync(IReadOnlySet<string> enabledFilters, CancellationToken cancellationToken) => DelayInventory
            ? InventoryCompletion.Task
            : Task.FromResult<HostResult<LayersPresentation>>(new HostResult<LayersPresentation>.Success(Presentation()));

        public Task<string> EmphasizeAsync(CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            return Completion.Task;
        }

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken) => PendingCleanup?.Task ?? Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

        public Task<string> EmphasizeObjectsAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken) =>
            Task.FromResult("Selection updated.");

        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken) =>
            Task.FromResult("Focused.");
    }
}
