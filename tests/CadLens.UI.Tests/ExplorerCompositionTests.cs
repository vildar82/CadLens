using CadLens.AutoCAD;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using CadLens.Lenses;
using CadLens.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Validates the real managed composition with native operations replaced at their boundary.</summary>
public sealed class ExplorerCompositionTests : IDisposable
{
    private readonly SettingsFile _settings = new();

    /// <summary>The provider rejects an incomplete host registration at build time.</summary>
    [Fact]
    public void MissingHostDependenciesFailDuringBuild() =>
        Assert.Throws<AggregateException>(() => ExplorerComposition.Build(services =>
            services.AddSingleton(_settings.Service)));

    /// <summary>A singleton cannot capture the scoped drawing provider through the action adapter.</summary>
    [Fact]
    public void SingletonDependingOnScopedProviderFailsDuringBuild() =>
        Assert.Throws<AggregateException>(() => ExplorerComposition.Build(services =>
        {
            RegisterHost(services);
            services.AddSingleton<IObjectExplorerActions, Actions>();
        }));

    /// <summary>Scoped state cannot be resolved from the plugin root.</summary>
    [Fact]
    public void RootRejectsScopedResolution()
    {
        using var root = ExplorerComposition.Build(RegisterHost);

        Assert.Throws<InvalidOperationException>(root.GetRequiredService<ExplorerViewModel>);
    }

    /// <summary>Fresh panel scopes restore saved filters and dispose their commands exactly once.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReopeningCreatesFreshScopedGraph(bool enabled)
    {
        await using var root = ExplorerComposition.Build(RegisterHost);
        ExplorerViewModel previous;
        SnapshotSource source;

        using (var scope = root.CreateScope())
        {
            previous = scope.ServiceProvider.GetRequiredService<ExplorerViewModel>();
            source = (SnapshotSource) scope.ServiceProvider.GetRequiredService<IDrawingInventorySource>();
            Assert.Same(previous, scope.ServiceProvider.GetRequiredService<ExplorerViewModel>());
            await previous.ToggleLensCommand.ExecuteAsync(previous.Lenses[0]);
            var layers = DrawingViewModel(scope.ServiceProvider, "layers");
            await layers.ToggleFilterCommand.ExecuteAsync(layers.Filters[0]);

            if (!enabled)
                await layers.ToggleFilterCommand.ExecuteAsync(layers.Filters[0]);
        }

        Assert.False(previous.ToggleLensCommand.CanExecute(previous.Lenses[0]));
        Assert.Equal(1, source.DisposeCount);

        using var reopened = root.CreateScope();
        var current = reopened.ServiceProvider.GetRequiredService<ExplorerViewModel>();
        Assert.NotSame(previous, current);
        Assert.NotSame(source, reopened.ServiceProvider.GetRequiredService<IDrawingInventorySource>());
        Assert.False(current.IsLensActive);
        await current.ToggleLensCommand.ExecuteAsync(current.Lenses[0]);
        var filters = DrawingViewModel(reopened.ServiceProvider, "layers").Filters;
        Assert.Equal(enabled, filters[0].IsEnabled);
        Assert.All(filters.Skip(1), filter => Assert.False(filter.IsEnabled));
    }

    /// <summary>Each drawing grouping restores only its own saved filters in a fresh panel scope.</summary>
    [Fact]
    public async Task ReopeningKeepsLensSettingsIndependent()
    {
        await using var root = ExplorerComposition.Build(RegisterHost);

        using (var scope = root.CreateScope())
        {
            var shell = scope.ServiceProvider.GetRequiredService<ExplorerViewModel>();
            await shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]);
            var layers = DrawingViewModel(scope.ServiceProvider, "layers");
            await layers.ToggleFilterCommand.ExecuteAsync(layers.Filters[0]);
            await shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[1]);
            var types = DrawingViewModel(scope.ServiceProvider, "object-types");
            await types.ToggleFilterCommand.ExecuteAsync(types.Filters[1]);
        }

        using var reopened = root.CreateScope();
        Assert.Same(_settings.Service, reopened.ServiceProvider.GetRequiredService<SettingsService>());
        var current = reopened.ServiceProvider.GetRequiredService<ExplorerViewModel>();
        Assert.False(current.IsLensActive);
        await current.ToggleLensCommand.ExecuteAsync(current.Lenses[0]);
        var layerFilters = DrawingViewModel(reopened.ServiceProvider, "layers").Filters;
        Assert.True(layerFilters[0].IsEnabled);
        Assert.False(layerFilters[1].IsEnabled);
        await current.ToggleLensCommand.ExecuteAsync(current.Lenses[1]);
        var typeFilters = DrawingViewModel(reopened.ServiceProvider, "object-types").Filters;
        Assert.False(typeFilters[0].IsEnabled);
        Assert.True(typeFilters[1].IsEnabled);
    }

    /// <summary>Appearance is scoped with the panel and saves through the host's isolated settings service.</summary>
    [Fact]
    public void ReopeningRestoresAppearanceThroughConfiguredSettingsService()
    {
        using var root = ExplorerComposition.Build(RegisterHost);
        AppearancePreferences previous;

        using (var scope = root.CreateScope())
        {
            previous = scope.ServiceProvider.GetRequiredService<AppearancePreferences>();
            previous.Theme = "Light";
        }

        using var reopened = root.CreateScope();
        var current = reopened.ServiceProvider.GetRequiredService<AppearancePreferences>();
        Assert.NotSame(previous, current);
        Assert.Equal("Light", current.Theme);
        Assert.True(File.Exists(_settings.Path));
    }

    /// <summary>The production presentation assemblies do not reference the DI container or AutoCAD.</summary>
    [Fact]
    public void PresentationAssembliesRemainIndependent()
    {
        Assembly[] assemblies =
        [
            typeof(LensNode).Assembly,
            typeof(DrawingLensProvider).Assembly,
            typeof(ExplorerViewModel).Assembly
        ];

        foreach (var assembly in assemblies)
        {
            Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                reference =>
                    reference.Name!.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal) ||
                    reference.Name.StartsWith("AcDbMgd", StringComparison.OrdinalIgnoreCase) ||
                    reference.Name.StartsWith("AcMgd", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Registering another provider and adapter automatically exposes and loads it in the panel.</summary>
    [Fact]
    public async Task AdditionalRegistrationIsDiscoveredWithoutPanelChanges()
    {
        await using var root = ExplorerComposition.Build(services =>
        {
            RegisterHost(services);
            services.AddScoped<CounterService>();
            services.AddScoped<CounterViewModel>();
            services.AddScoped<ILens, CounterLens>();
        });
        using var scope = root.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<ExplorerViewModel>();
        var counter = scope.ServiceProvider.GetServices<ILens>().OfType<CounterLens>().Single();
        Assert.Equal(["layers", "object-types", "counter"], model.Lenses.Select(lens => lens.Descriptor.Id));
        Assert.False(model.IsLensActive);
        Assert.Equal(0, counter.ActivationCount);
        await model.ToggleLensCommand.ExecuteAsync(model.Lenses[2]);
        Assert.Equal(1, counter.ActivationCount);
        Assert.True(model.Lenses[2].IsActive);
        Assert.False(model.Lenses[0].IsActive);
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<CounterService>().Count);
    }

    /// <summary>
    /// Both production lenses use one drawing inventory while retaining their own exploration settings.
    /// </summary>
    [Fact]
    public async Task ProductionLensesKeepIndependentNavigationAndSettings()
    {
        await using var root = ExplorerComposition.Build(RegisterHost);
        using var scope = root.CreateScope();
        var source = (SnapshotSource) scope.ServiceProvider.GetRequiredService<IDrawingInventorySource>();
        var first = new TestLayerId("A");
        var second = new TestLayerId("B");
        var frozen = new TestLayerId("C");
        source.Inventory = new DrawingInventory(
            "Model",
            [
                new LayerSnapshot(first, "A", false, false, false, false),
                new LayerSnapshot(second, "B", false, false, false, false),
                new LayerSnapshot(frozen, "C", false, true, false, false)
            ],
            [
                new EntitySnapshot(new TestEntityId(1), first, "AcDbLine"),
                new EntitySnapshot(new TestEntityId(2), second, "AcDbLine"),
                new EntitySnapshot(new TestEntityId(3), second, "AcDbCircle"),
                new EntitySnapshot(new TestEntityId(4), frozen, "AcDbLine")
            ]);
        var shell = scope.ServiceProvider.GetRequiredService<ExplorerViewModel>();
        var layers = DrawingViewModel(scope.ServiceProvider, "layers");
        var types = DrawingViewModel(scope.ServiceProvider, "object-types");

        Assert.Equal(["layers", "object-types"], shell.Lenses.Select(lens => lens.Descriptor.Id));
        Assert.False(shell.IsLensActive);
        await shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]);
        await layers.ToggleFilterCommand.ExecuteAsync(layers.Filters[0]);
        layers.SearchText = "A";
        await layers.EnterCommand.ExecuteAsync(Assert.Single(layers.Items));
        await layers.ToggleAutoSelectCommand.ExecuteAsync(null);
        await shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[1]);

        Assert.False(layers.IsLensActive);
        Assert.True(types.IsLensActive);
        Assert.Equal(3, types.ObjectCount);
        Assert.Equal(2, types.GroupCount);
        Assert.Equal(2, types.Groups.Single(group => group.Label == "Line").Count);
        Assert.All(types.Filters, filter => Assert.False(filter.IsEnabled));
        Assert.False(types.IsAutoSelect);
        Assert.Equal("", types.SearchText);
        Assert.Null(types.Current);
        await types.EnterCommand.ExecuteAsync(types.Groups.Single(group => group.Label == "Line"));
        Assert.Equal(2, types.Items.Length);
        await types.EnterCommand.ExecuteAsync(types.Items[1]);
        Assert.Equal("B", types.Current!.Fields.Single(field => field.Label == "Layer").Value);
        await shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]);

        Assert.False(types.IsLensActive);
        Assert.True(layers.IsLensActive);
        Assert.Equal("A", layers.SearchText);
        Assert.True(layers.Filters[0].IsEnabled);
        Assert.True(layers.IsAutoSelect);
        Assert.Equal("A", layers.Current!.Label);
        Assert.Equal(4, layers.ObjectCount);
    }

    private static ObjectExplorerViewModel DrawingViewModel(IServiceProvider services, string id) =>
        services.GetServices<ILens>().OfType<ObjectExplorerLens>().Single(lens => lens.Descriptor.Id == id).ViewModel;

    private void RegisterHost(IServiceCollection services)
    {
        services.AddSingleton(_settings.Service);
        services.AddScoped<IDrawingInventorySource, SnapshotSource>();
        services.AddScoped<IObjectExplorerActions, Actions>();
    }

    /// <inheritdoc />
    public void Dispose() => _settings.Dispose();

    private sealed class SnapshotSource : IDrawingInventorySource, IDisposable
    {
        internal int DisposeCount { get; private set; }

        internal DrawingInventory Inventory { get; set; } = new("Model", [], []);

        public Task<HostResult<DrawingInventory>> ReadAsync(ImmutableArray<IPlacedObjectId>? selectedObjects, CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<DrawingInventory>>(new HostResult<DrawingInventory>.Success(Inventory));

        public void Dispose() => DisposeCount++;
    }

    private sealed record TestLayerId(string DisplayId) : ILayerId;

    private sealed class Actions(IDrawingLensProvider provider) : IObjectExplorerActions
    {
        public Task<HostResult<ImmutableArray<IPlacedObjectId>>> RequestObjectsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<ImmutableArray<IPlacedObjectId>>>(new HostResult<ImmutableArray<IPlacedObjectId>>.Success([]));

        public void ClearImmediately(bool hostTerminating)
        {
        }

        public Task<HostResult<bool>> SelectAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

        public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken) =>
            ClearAsync(cancellationToken);

        public Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlyCollection<string> enabledFilters,
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken) =>
            provider.LoadAsync(grouping, enabledFilters, selectedObjects, cancellationToken);

        public Task<string> IsolateObjectsAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken) =>
            Task.FromResult("Selection updated.");

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken) =>
            Task.FromResult("Focused.");
    }
}
