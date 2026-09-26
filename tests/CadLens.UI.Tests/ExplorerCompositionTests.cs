using CadLens.AutoCAD;
using System.Collections.Immutable;
using CadLens.Lenses;
using Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Validates the real managed composition with native operations replaced at their boundary.</summary>
public sealed class ExplorerCompositionTests
{
    /// <summary>The provider rejects an incomplete host registration at build time.</summary>
    [Fact]
    public void MissingHostDependenciesFailDuringBuild() =>
        Assert.Throws<AggregateException>(() => ExplorerComposition.Build(_ => { }));

    /// <summary>A singleton cannot capture a scoped lens through the action adapter.</summary>
    [Fact]
    public void SingletonDependingOnScopedLensFailsDuringBuild() =>
        Assert.Throws<AggregateException>(() => ExplorerComposition.Build(services =>
        {
            RegisterHost(services);
            services.AddSingleton<ILens, LayersLens>();
        }));

    /// <summary>Scoped state cannot be resolved from the plugin root.</summary>
    [Fact]
    public void RootRejectsScopedResolution()
    {
        using var root = ExplorerComposition.Build(RegisterHost);

        Assert.Throws<InvalidOperationException>(() => root.GetRequiredService<ExplorerViewModel>());
    }

    /// <summary>Each panel gets fresh state; disposing its scope disables its commands exactly once.</summary>
    [Fact]
    public async Task ReopeningCreatesFreshScopedGraph()
    {
        using var root = ExplorerComposition.Build(RegisterHost);
        ExplorerViewModel previous;
        SnapshotSource source;

        using (var scope = root.CreateScope())
        {
            previous = scope.ServiceProvider.GetRequiredService<ExplorerViewModel>();
            source = (SnapshotSource)scope.ServiceProvider.GetRequiredService<ILayersSnapshotSource>();
            Assert.Same(previous, scope.ServiceProvider.GetRequiredService<ExplorerViewModel>());
            await previous.ToggleLensCommand.ExecuteAsync(previous.Lenses[0]);
            var layers = scope.ServiceProvider.GetRequiredService<LayersViewModel>();
            await layers.ToggleFilterCommand.ExecuteAsync(layers.Filters[0]);
        }

        Assert.False(previous.ToggleLensCommand.CanExecute(previous.Lenses[0]));
        Assert.Equal(1, source.DisposeCount);

        using var reopened = root.CreateScope();
        var current = reopened.ServiceProvider.GetRequiredService<ExplorerViewModel>();
        Assert.NotSame(previous, current);
        Assert.NotSame(source, reopened.ServiceProvider.GetRequiredService<ILayersSnapshotSource>());
        Assert.False(current.IsLensActive);
        await current.ToggleLensCommand.ExecuteAsync(current.Lenses[0]);
        Assert.All(reopened.ServiceProvider.GetRequiredService<LayersViewModel>().Filters, filter => Assert.False(filter.IsEnabled));
    }

    /// <summary>The production presentation assemblies do not reference the DI container or AutoCAD.</summary>
    [Fact]
    public void PresentationAssembliesRemainIndependent()
    {
        var assemblies = new[] { typeof(LensNode).Assembly, typeof(LayersLensProvider).Assembly, typeof(ExplorerViewModel).Assembly };

        foreach (var assembly in assemblies)
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
                reference.Name!.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal) ||
                reference.Name.StartsWith("AcDbMgd", StringComparison.OrdinalIgnoreCase) ||
                reference.Name.StartsWith("AcMgd", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Registering another provider and adapter automatically exposes and loads it in the panel.</summary>
    [Fact]
    public async Task AdditionalRegistrationIsDiscoveredWithoutPanelChanges()
    {
        using var root = ExplorerComposition.Build(services =>
        {
            RegisterHost(services);
            services.AddScoped<CounterService>();
            services.AddScoped<CounterViewModel>();
            services.AddScoped<ILens, CounterLens>();
        });
        using var scope = root.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<ExplorerViewModel>();
        var second = scope.ServiceProvider.GetServices<ILens>().OfType<CounterLens>().Single();
        Assert.Equal(new[] { "layers", "counter" }, model.Lenses.Select(lens => lens.Descriptor.Id));
        Assert.False(model.IsLensActive);
        Assert.Equal(0, second.ActivationCount);
        await model.ToggleLensCommand.ExecuteAsync(model.Lenses[1]);
        Assert.Equal(1, second.ActivationCount);
        Assert.True(model.Lenses[1].IsActive);
        Assert.False(model.Lenses[0].IsActive);
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<CounterService>().Count);
    }

    private static void RegisterHost(IServiceCollection services)
    {
        services.AddScoped<ILayersSnapshotSource, SnapshotSource>();
        services.AddScoped<ILayersActions, Actions>();
    }

    private sealed class SnapshotSource : ILayersSnapshotSource, IDisposable
    {
        internal int DisposeCount { get; private set; }

        public Task<HostResult<LayersSnapshot>> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<LayersSnapshot>>(new HostResult<LayersSnapshot>.Success(new LayersSnapshot("Model", [], [])));

        public void Dispose() => DisposeCount++;
    }

    private sealed class Actions(ILayersProvider provider) : ILayersActions
    {
        public void ClearImmediately(bool redraw) { }

        public Task<HostResult<LayersPresentation>> ReadAsync(IReadOnlySet<string> enabledFilters, CancellationToken cancellationToken) =>
            provider.LoadAsync(enabledFilters, cancellationToken);

        public Task<string> EmphasizeAsync(CancellationToken cancellationToken) => Task.FromResult("Highlighted.");

        public Task<string> EmphasizeObjectsAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken) =>
            Task.FromResult("Selection updated.");

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken) =>
            Task.FromResult("Focused.");
    }
}
