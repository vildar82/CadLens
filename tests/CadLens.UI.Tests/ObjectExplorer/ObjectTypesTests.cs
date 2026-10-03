using System.Collections.Immutable;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CadLens.Lenses;
using Common;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Object Types uses the production grouping and the shared exploration behavior.</summary>
[Collection("WPF")]
public sealed class ObjectTypesTests
{
    /// <summary>Types span layers, objects retain their layer, and manual commands use the current targets.</summary>
    [Fact]
    public async Task BrowseTypesAcrossLayersAndUseManualActions()
    {
        var actions = new Actions();
        using var lens = new ObjectExplorerLens(actions, DrawingGrouping.ObjectTypes);
        var model = lens.ViewModel;
        Assert.Equal("Object Types", model.LensLabel);
        Assert.Equal("All types", model.RootLabel);
        await lens.ActivateAsync(CancellationToken.None);
        Assert.Equal("object-types", lens.Descriptor.Id);
        Assert.Equal("Search types", model.SearchPlaceholder);
        Assert.Equal("3 types", model.GroupSummary);
        Assert.False(model.IsAutoFocus || model.IsAutoSelect || model.IsAutoIsolation);
        var line = model.Items.Single(node => node.Label == "Line");
        Assert.Equal(2, line.Count);
        await model.EnterCommand.ExecuteAsync(line);
        Assert.False(model.IsObject);
        Assert.Equal(2, model.Items.Length);
        Assert.Empty(actions.Selected);
        Assert.Empty(actions.Isolated);
        Assert.Empty(actions.Focused);

        await model.FocusCommand.ExecuteAsync(null);
        await model.SelectCommand.ExecuteAsync(null);
        await model.IsolateCommand.ExecuteAsync(null);
        Assert.Equal(line.Objects, actions.Focused);
        Assert.Equal(line.Objects, actions.Selected);
        Assert.Equal(line.Objects, actions.Isolated);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        Assert.True(model.IsObject);
        Assert.Equal("1 of 2", model.ObjectPosition);
        Assert.Equal("Architecture", model.Current!.Fields.Single(field => field.Label == "Layer").Value);
        Assert.Equal(2, model.Breadcrumbs.Count);
        await model.NextCommand.ExecuteAsync(null);
        Assert.Equal("Roads", model.Current!.Fields.Single(field => field.Label == "Layer").Value);
        await model.SelectCommand.ExecuteAsync(null);
        Assert.Equal(new TestEntityId(2), Assert.Single(actions.Selected));
        await model.PreviousCommand.ExecuteAsync(null);
        await model.BackCommand.ExecuteAsync(null);
        Assert.Same(line, model.Current);
        await model.RootCommand.ExecuteAsync(null);
        Assert.Null(model.Current);
        Assert.Empty(actions.Selected);
        Assert.Empty(actions.Isolated);
    }

    /// <summary>Search and count sorting act locally on types and leave the loaded inventory intact.</summary>
    [Fact]
    public async Task SearchAndSortObjectTypesWithoutHostWork()
    {
        var actions = new Actions();
        using var lens = new ObjectExplorerLens(actions, DrawingGrouping.ObjectTypes);
        await lens.ActivateAsync(CancellationToken.None);
        var model = lens.ViewModel;
        var reads = actions.ReadCount;
        var clears = actions.ClearCount;
        Assert.Equal(["Circle", "Line", "Text"], model.Items.Select(node => node.Label));
        model.SortByCountCommand.Execute(null);
        Assert.Equal(["Line", "Circle", "Text"], model.Items.Select(node => node.Label));
        model.SortByCountCommand.Execute(null);
        Assert.Equal(["Circle", "Text", "Line"], model.Items.Select(node => node.Label));
        model.SearchText = " li ";
        Assert.Equal("Line", Assert.Single(model.Items).Label);
        Assert.Equal(3, model.GroupCount);
        model.SearchText = "missing";
        Assert.Equal("No types match your search.", model.EmptyMessage);
        model.ClearSearchCommand.Execute(null);
        Assert.Equal(3, model.Items.Length);
        Assert.Equal(reads, actions.ReadCount);
        Assert.Equal(clears, actions.ClearCount);
    }

    /// <summary>Visibility options affect types across layers and retain the selected valid object.</summary>
    [Fact]
    public async Task FiltersReconcileObjectTypesAndPreserveValidObjects()
    {
        var actions = new Actions();
        using var lens = new ObjectExplorerLens(actions, DrawingGrouping.ObjectTypes);
        await lens.ActivateAsync(CancellationToken.None);
        var model = lens.ViewModel;
        await model.EnterCommand.ExecuteAsync(model.Items.Single(node => node.Label == "Line"));
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        var id = model.Current!.Id;
        await model.ToggleFilterCommand.ExecuteAsync(
            model.Filters.Single(filter => filter.Descriptor.Id == DrawingLensProvider.IncludeFrozen));
        Assert.Equal(id, model.Current!.Id);
        Assert.Equal(4, model.GroupCount);
        await model.RootCommand.ExecuteAsync(null);
        await model.EnterCommand.ExecuteAsync(model.Items.Single(node => node.Label == "Polyline"));
        await model.ToggleFilterCommand.ExecuteAsync(
            model.Filters.Single(filter => filter.Descriptor.Id == DrawingLensProvider.IncludeFrozen));
        Assert.Null(model.Current);
        Assert.Equal(3, model.GroupCount);
        Assert.Empty(actions.Isolated);
    }

    /// <summary>Lens switching preserves search, options, sorting, and navigation while clearing effects.</summary>
    [Fact]
    public async Task DrawingLensesKeepIndependentStateDuringSwitches()
    {
        var actions = new Actions();
        using var layers = new ObjectExplorerLens(actions, DrawingGrouping.Layers);
        using var types = new ObjectExplorerLens(actions, DrawingGrouping.ObjectTypes);
        using var shell = new ExplorerViewModel([layers, types]);
        await shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]);
        layers.ViewModel.SearchText = "road";
        layers.ViewModel.SortByCountCommand.Execute(null);
        await layers.ViewModel.ToggleFilterCommand.ExecuteAsync(
            layers.ViewModel.Filters.Single(filter => filter.Descriptor.Id == DrawingLensProvider.IncludeOff));
        await layers.ViewModel.EnterCommand.ExecuteAsync(Assert.Single(layers.ViewModel.Items));
        var layerId = layers.ViewModel.Current!.Id;
        await layers.ViewModel.ToggleAutoSelectCommand.ExecuteAsync(null);
        Assert.NotEmpty(actions.Selected);

        await shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[1]);
        Assert.Empty(actions.Selected);
        Assert.Empty(actions.Isolated);
        Assert.False(layers.ViewModel.IsLensActive);
        Assert.Equal("", types.ViewModel.SearchText);
        Assert.True(types.ViewModel.IsNameSortActive);
        Assert.All(types.ViewModel.Filters, filter => Assert.False(filter.IsEnabled));
        Assert.False(types.ViewModel.IsAutoSelect);
        types.ViewModel.SearchText = "line";
        await types.ViewModel.EnterCommand.ExecuteAsync(Assert.Single(types.ViewModel.Items));
        await types.ViewModel.EnterCommand.ExecuteAsync(types.ViewModel.Items[1]);
        var objectId = types.ViewModel.Current!.Id;
        await shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]);
        Assert.Equal("road", layers.ViewModel.SearchText);
        Assert.Equal(layerId, layers.ViewModel.Current!.Id);
        Assert.True(layers.ViewModel.IsCountSortActive);
        Assert.True(
            layers.ViewModel.Filters.Single(filter => filter.Descriptor.Id == DrawingLensProvider.IncludeOff)
                .IsEnabled);
        Assert.True(layers.ViewModel.IsAutoSelect);
        await shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[1]);
        Assert.Equal("line", types.ViewModel.SearchText);
        Assert.Equal(objectId, types.ViewModel.Current!.Id);
        Assert.False(types.ViewModel.IsAutoSelect);
        Assert.All(types.ViewModel.Filters, filter => Assert.False(filter.IsEnabled));
        Assert.NotSame(layers.ViewModel, types.ViewModel);
    }

    /// <summary>The next lens waits for canceled reads and successful cleanup before loading.</summary>
    [Fact]
    public async Task SwitchingCancelsOldReadAndWaitsForCleanup()
    {
        var source = new Source {Pending = new TaskCompletionSource<HostResult<DrawingInventory>>()};
        var actions = new Actions(source);
        using var types = new ObjectExplorerLens(actions, DrawingGrouping.ObjectTypes);
        using var layers = new ObjectExplorerLens(actions, DrawingGrouping.Layers);
        using var shell = new ExplorerViewModel([types, layers]);
        var activation = shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]);
        var oldRead = source.Pending;
        var oldToken = source.Token;
        actions.PendingClear = new TaskCompletionSource<HostResult<bool>>();
        var switching = shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[1]);
        Assert.True(oldToken.IsCancellationRequested);
        Assert.False(shell.IsLensActive);
        Assert.Equal(1, actions.ReadCount);
        source.Pending = null;
        oldRead.SetResult(new HostResult<DrawingInventory>.Success(Inventory()));
        await activation;
        Assert.Empty(types.ViewModel.Groups);
        Assert.Equal(1, actions.ReadCount);
        var clear = actions.PendingClear;
        actions.PendingClear = null;
        clear.SetResult(new HostResult<bool>.Success(true));
        await switching;
        Assert.True(layers.ViewModel.IsLensActive);
        Assert.Equal(2, actions.ReadCount);
        Assert.False(types.ViewModel.IsBusy);
    }

    /// <summary>Failed inventory clears stale targets; a later refresh restores the right grouping.</summary>
    [Fact]
    public async Task ReadFailureClearsOldTypesAndRefreshRecovers()
    {
        var source = new Source();
        var actions = new Actions(source);
        using var lens = new ObjectExplorerLens(actions, DrawingGrouping.ObjectTypes);
        await lens.ActivateAsync(CancellationToken.None);
        var model = lens.ViewModel;
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        source.Result = new HostResult<DrawingInventory>.Unavailable("Fixture drawing unavailable.");
        await model.ReadCommand.ExecuteAsync(null);
        Assert.Empty(model.Groups);
        Assert.Null(model.Current);
        Assert.Contains("Fixture drawing unavailable", model.Status);
        Assert.True(model.ReadCommand.CanExecute(null));
        source.Result = new HostResult<DrawingInventory>.Success(Inventory());
        await model.ReadCommand.ExecuteAsync(null);
        Assert.Equal(3, model.GroupCount);
        Assert.Equal("Object Types", model.LensLabel);
    }

    /// <summary>Close and disposal cancel pending work and remove effects only once.</summary>
    [Fact]
    public async Task CloseAndDisposeAreIdempotentDuringLoading()
    {
        var source = new Source {Pending = new TaskCompletionSource<HostResult<DrawingInventory>>()};
        var actions = new Actions(source);
        var lens = new ObjectExplorerLens(actions, DrawingGrouping.ObjectTypes);
        var pending = lens.ActivateAsync(CancellationToken.None);
        var token = source.Token;
        lens.Close(true);
        lens.Dispose();
        lens.Close(false);
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(1, actions.ImmediateClearCount);
        Assert.True(actions.HostTerminating);
        source.Pending.SetResult(new HostResult<DrawingInventory>.Success(Inventory()));
        await pending;
        Assert.Empty(lens.ViewModel.Groups);
        Assert.False(lens.ViewModel.ReadCommand.CanExecute(null));
    }

    /// <summary>The production view renders every Object Types level at compact and expanded widths.</summary>
    [Theory]
    [InlineData(300, "root")]
    [InlineData(370, "root")]
    [InlineData(300, "type")]
    [InlineData(370, "type")]
    [InlineData(300, "object")]
    [InlineData(370, "object")]
    public void RenderObjectTypesLevels(int width, string level)
    {
        WpfTest.Run(() =>
        {
            var actions = new Actions();
            using var layers = new ObjectExplorerLens(actions, DrawingGrouping.Layers);
            using var lens = new ObjectExplorerLens(actions, DrawingGrouping.ObjectTypes);
            using var shell = new ExplorerViewModel([layers, lens]);
            shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[1]).GetAwaiter().GetResult();
            var model = lens.ViewModel;

            if (level != "root")
                model.EnterCommand.ExecuteAsync(model.Items.Single(node => node.Label == "Line")).GetAwaiter()
                    .GetResult();

            if (level == "object")
                model.EnterCommand.ExecuteAsync(model.Items[0]).GetAwaiter().GetResult();

            using var windowSettings = new SettingsFile();

            var window = new ExplorerWindow(shell, settings: windowSettings.Service);
            var content = (FrameworkElement) window.Content;
            const int height = 660;
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();
            Assert.Same(model, shell.ActiveView!.DataContext);
            string[] text =
            [
                .. WpfTest.Descendants(content).OfType<TextBlock>()
                    .Where(block => block.Visibility == Visibility.Visible)
                    .Select(block => block.Text)
            ];
            var activeLens = WpfTest.Descendants(content).OfType<ToggleButton>()
                .Single(button => ReferenceEquals(button.CommandParameter, shell.Lenses[1]));
            Assert.Equal("Objects", activeLens.Content);
            Assert.True(activeLens.IsChecked);
            Assert.DoesNotContain("All layers", text);
            Assert.DoesNotContain("Search layers", text);

            if (level == "root")
            {
                Assert.Contains("3 types", text);
                Assert.Contains("Search types", text);
            }
            else
            {
                Assert.Contains(
                    "All types",
                    WpfTest.Descendants(content).OfType<Button>().Select(button => button.Content));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var directory = Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "..",
                    "artifacts",
                    "modes-object-types"));
            Directory.CreateDirectory(directory);
            using var output = File.Create(Path.Combine(directory, $"object-types-{width}-{level}.png"));
            encoder.Save(output);
            window.Close();
        });
    }

    /// <summary>Both production lens choices remain fully visible in the 300-pixel collapsed bar.</summary>
    [Fact]
    public void BothDrawingLensButtonsFitCompactToolbar()
    {
        WpfTest.Run(() =>
        {
            var actions = new Actions();
            using var layers = new ObjectExplorerLens(actions, DrawingGrouping.Layers);
            using var types = new ObjectExplorerLens(actions, DrawingGrouping.ObjectTypes);
            using var shell = new ExplorerViewModel([layers, types]);
            using var windowSettings = new SettingsFile();
            var window = new ExplorerWindow(shell, settings: windowSettings.Service);
            var content = (FrameworkElement) window.Content;
            var size = new Size(window.Width, window.Height);
            content.Measure(size);
            content.Arrange(new Rect(size));
            content.UpdateLayout();
            var toggleLensCommand = shell.ToggleLensCommand;
            ToggleButton[] buttons =
            [
                .. WpfTest.Descendants(content).OfType<ToggleButton>()
                    .Where(button => ReferenceEquals(button.Command, toggleLensCommand))
            ];
            Assert.Equal(["Layers", "Objects"], buttons.Select(button => button.Content));
            Assert.All(buttons, button => Assert.True(button.ActualWidth > 0));
            var scroller = WpfTest.Descendants(content).OfType<ScrollViewer>().Single();
            Assert.True(
                scroller.ExtentWidth <= scroller.ViewportWidth,
                "Both lens choices must fit without scrolling.");
            Assert.All(buttons, button => Assert.True(button.Command.CanExecute(button.CommandParameter)));
            buttons[1].Command.Execute(buttons[1].CommandParameter);
            Assert.True(types.ViewModel.IsLensActive);
            Assert.IsType<ObjectExplorerView>(shell.ActiveView);
            window.Close();
        });
    }

    private static DrawingInventory Inventory()
    {
        var architecture = new LayerId("Architecture");
        var roads = new LayerId("Roads");
        var frozen = new LayerId("Frozen");
        var off = new LayerId("Off");
        return new DrawingInventory(
            "Model space",
            [
                new LayerSnapshot(architecture, architecture.DisplayId, false, false, false, false),
                new LayerSnapshot(roads, roads.DisplayId, false, false, false, true),
                new LayerSnapshot(frozen, frozen.DisplayId, false, true, false, false),
                new LayerSnapshot(off, off.DisplayId, true, false, false, false)
            ],
            [
                new EntitySnapshot(new TestEntityId(1), architecture, "AcDbLine"),
                new EntitySnapshot(new TestEntityId(2), roads, "AcDbLine"),
                new EntitySnapshot(new TestEntityId(3), roads, "AcDbCircle"),
                new EntitySnapshot(new TestEntityId(4), architecture, "AcDbText"),
                new EntitySnapshot(new TestEntityId(5), frozen, "AcDbPolyline"),
                new EntitySnapshot(new TestEntityId(6), off, "AcDbArc")
            ]);
    }

    private sealed record LayerId(string DisplayId) : ILayerId;

    private sealed class Source : IDrawingInventorySource
    {
        internal TaskCompletionSource<HostResult<DrawingInventory>>? Pending { get; set; }

        internal HostResult<DrawingInventory> Result { get; set; } =
            new HostResult<DrawingInventory>.Success(Inventory());

        internal CancellationToken Token { get; private set; }

        public Task<HostResult<DrawingInventory>> ReadAsync(ImmutableArray<IPlacedObjectId>? selectedObjects, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            return Pending?.Task ?? Task.FromResult(Result);
        }
    }

    private sealed class Actions : IObjectExplorerActions
    {
        private readonly DrawingLensProvider _provider;

        internal Actions(Source? source = null) => _provider = new DrawingLensProvider(source ?? new Source());

        internal int ReadCount { get; private set; }
        internal int ClearCount { get; private set; }
        internal int ImmediateClearCount { get; private set; }
        internal bool HostTerminating { get; private set; }
        internal TaskCompletionSource<HostResult<bool>>? PendingClear { get; set; }
        internal ImmutableArray<IPlacedObjectId> Selected { get; private set; } = [];
        internal ImmutableArray<IPlacedObjectId> Isolated { get; private set; } = [];
        internal ImmutableArray<IPlacedObjectId> Focused { get; private set; } = [];

        public Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlySet<string> enabledFilters,
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return _provider.LoadAsync(grouping, enabledFilters, selectedObjects, cancellationToken);
        }

        public Task<HostResult<bool>> SelectAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken)
        {
            Selected = objects;
            return Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
        }

        public Task<string> IsolateObjectsAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken)
        {
            Isolated = objects;
            return Task.FromResult("Isolated.");
        }

        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            Focused = objects;
            return Task.FromResult("Focused.");
        }

        public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken)
        {
            Isolated = [];
            return Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
        }

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken)
        {
            ClearCount++;
            Selected = [];
            Isolated = [];
            return PendingClear?.Task ?? Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
        }

        public Task<HostResult<ImmutableArray<IPlacedObjectId>>> RequestObjectsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<ImmutableArray<IPlacedObjectId>>>(new HostResult<ImmutableArray<IPlacedObjectId>>.Success([]));

        public void ClearImmediately(bool hostTerminating)
        {
            ImmediateClearCount++;
            HostTerminating = hostTerminating;
            Selected = [];
            Isolated = [];
        }
    }
}
