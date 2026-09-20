using System.Collections.Immutable;
using CadLens.Core;
using Common;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Explorer behavior using a provider unrelated to Layers or AutoCAD.</summary>
public sealed class ExplorerNavigationTests
{
    /// <summary>Navigation preserves ancestors and never invokes host graphics.</summary>
    [Fact]
    public async Task BrowseObjectsAndReturnThroughBreadcrumbs()
    {
        var actions = new Actions();
        using var model = new ExplorerViewModel(actions);
        await model.ReadCommand.ExecuteAsync(null);
        var group = model.Items[0];
        model.EnterCommand.Execute(group);
        var type = model.Items[0];
        model.EnterCommand.Execute(type);
        model.EnterCommand.Execute(model.Items[0]);

        Assert.Equal("1 of 2", model.ObjectPosition);
        Assert.False(model.PreviousCommand.CanExecute(null));
        Assert.True(model.NextCommand.CanExecute(null));
        Assert.Equal("Category", model.Current!.Fields[0].Label);
        model.NextCommand.Execute(null);
        Assert.Equal("2 of 2", model.ObjectPosition);
        Assert.False(model.NextCommand.CanExecute(null));
        model.PreviousCommand.Execute(null);
        Assert.Equal("1 of 2", model.ObjectPosition);
        model.BackCommand.Execute(null);
        Assert.Same(type, model.Current);
        model.BreadcrumbCommand.Execute(group);
        Assert.Same(group, model.Current);
        model.RootCommand.Execute(null);
        Assert.Null(model.Current);
        Assert.Empty(model.Breadcrumbs);
        Assert.False(model.BackCommand.CanExecute(null));
        Assert.Equal(0, actions.HostCalls);
    }

    /// <summary>Filters are independent, preserve valid paths, and remove excluded selections.</summary>
    [Fact]
    public async Task FiltersReconcileSelectionAndStartOffInNewSessions()
    {
        var actions = new Actions();
        using var model = new ExplorerViewModel(actions);
        await model.ReadCommand.ExecuteAsync(null);
        Assert.All(model.Filters, filter => Assert.False(filter.IsEnabled));
        await model.ToggleFilterCommand.ExecuteAsync(model.Filters[0]);
        Assert.Equal(new[] { "archived" }, actions.Enabled.Order());
        model.EnterCommand.Execute(model.Items.Single(node => node.Id == "hidden"));
        await model.ToggleFilterCommand.ExecuteAsync(model.Filters[1]);
        Assert.Equal("hidden", model.Current!.Id);
        Assert.Equal(2, actions.Enabled.Count);
        await model.ToggleFilterCommand.ExecuteAsync(model.Filters[0]);
        Assert.Null(model.Current);
        Assert.Equal(new[] { "extra" }, actions.Enabled.Order());
        Assert.False(model.Filters[0].IsEnabled);
        Assert.True(model.Filters[1].IsEnabled);
        using var reopened = new ExplorerViewModel(actions);
        await reopened.ReadCommand.ExecuteAsync(null);
        Assert.Empty(actions.Enabled);
        Assert.All(reopened.Filters, filter => Assert.False(filter.IsEnabled));
    }

    /// <summary>Refresh retains existing objects and falls back to the nearest valid parent.</summary>
    [Fact]
    public async Task RefreshReconcilesObjectsAndSingletonEndpoints()
    {
        var actions = new Actions();
        using var model = new ExplorerViewModel(actions);
        await model.ReadCommand.ExecuteAsync(null);
        model.EnterCommand.Execute(model.Items[0]);
        model.EnterCommand.Execute(model.Items[0]);
        model.EnterCommand.Execute(model.Items[1]);
        await model.ReadCommand.ExecuteAsync(null);
        Assert.Equal("2 of 2", model.ObjectPosition);
        actions.SingleObject = true;
        await model.ReadCommand.ExecuteAsync(null);
        Assert.Equal("type", model.Current!.Id);
        model.EnterCommand.Execute(model.Items[0]);
        Assert.Equal("1 of 1", model.ObjectPosition);
        Assert.False(model.PreviousCommand.CanExecute(null));
        Assert.False(model.NextCommand.CanExecute(null));
    }

    /// <summary>A filter result cannot republish an old document's selection.</summary>
    [Fact]
    public async Task ContextChangeRejectsPendingFilterResult()
    {
        var actions = new Actions();
        using var model = new ExplorerViewModel(actions);
        await model.ReadCommand.ExecuteAsync(null);
        model.EnterCommand.Execute(model.Items[0]);
        actions.Pending = new TaskCompletionSource<HostResult<LensPresentation>>();
        var pending = model.ToggleFilterCommand.ExecuteAsync(model.Filters[0]);
        Assert.False(model.EnterCommand.CanExecute(model.Items[0]));
        Assert.False(model.BackCommand.CanExecute(null));
        Assert.False(model.ToggleFilterCommand.CanExecute(model.Filters[1]));
        model.ResetContext();
        actions.Pending.SetResult(new HostResult<LensPresentation>.Success(CreatePresentation(false, true)));
        await pending;
        Assert.Empty(model.Items);
        Assert.Empty(model.Breadcrumbs);
        Assert.Null(model.Current);
        Assert.Contains("context changed", model.Status);
    }

    /// <summary>Empty results retain filter controls and the provider's explanation.</summary>
    [Fact]
    public async Task EmptyResultKeepsFiltersAndExplanation()
    {
        var actions = new Actions { Empty = true };
        using var model = new ExplorerViewModel(actions);
        await model.ReadCommand.ExecuteAsync(null);
        Assert.True(model.IsEmpty);
        Assert.Equal("Nothing matches these options.", model.EmptyMessage);
        Assert.Equal(2, model.Filters.Length);
    }

    /// <summary>A failed filter reload must not leave navigable results for the old options.</summary>
    [Fact]
    public async Task FailedFilterReadClearsStaleSelection()
    {
        var actions = new Actions();
        using var model = new ExplorerViewModel(actions);
        await model.ReadCommand.ExecuteAsync(null);
        model.EnterCommand.Execute(model.Items[0]);
        actions.Pending = new TaskCompletionSource<HostResult<LensPresentation>>();
        var pending = model.ToggleFilterCommand.ExecuteAsync(model.Filters[0]);
        actions.Pending.SetException(new InvalidOperationException("read failed"));
        await pending;
        Assert.Empty(model.Items);
        Assert.Null(model.Current);
        Assert.True(model.Filters[0].IsEnabled);
        Assert.Contains("read failed", model.Status);
        Assert.True(model.ReadCommand.CanExecute(null));
    }

    private static LensPresentation CreatePresentation(bool single, bool hidden)
    {
        var first = new LensNode("first", "First", [new HostObjectId(1)], [], [new DetailField("Category", "Fixture")], []);
        var second = new LensNode("second", "Second", [new HostObjectId(2)], [], first.Fields, []);
        ImmutableArray<LensNode> objects = single ? [first] : [first, second];
        var type = new LensNode("type", "Fixture type", [.. objects.SelectMany(node => node.Objects)], objects, [], []);
        var group = new LensNode("group", "Fixture group", type.Objects, [type], [], []);
        ImmutableArray<LensNode> groups = hidden ? [group, group with { Id = "hidden", Label = "Archived group" }] : [group];

        return new LensPresentation(
            "fixture",
            "Fixture",
            "Test space",
            groups,
            [new BooleanFilter("archived", "Archived", "Include archived items", IconRole.Snowflake),
             new BooleanFilter("extra", "Extra", "Include extra items", IconRole.Lightbulb)],
            "Nothing matches these options.");
    }

    private sealed class Actions : IExplorerActions
    {
        internal IReadOnlySet<string> Enabled { get; private set; } = new HashSet<string>();
        internal bool SingleObject { get; set; }
        internal bool Empty { get; init; }
        internal int HostCalls { get; private set; }
        internal TaskCompletionSource<HostResult<LensPresentation>>? Pending { get; set; }

        public Task<HostResult<LensPresentation>> ReadAsync(IReadOnlySet<string> enabledFilters, CancellationToken cancellationToken)
        {
            Enabled = enabledFilters;
            var presentation = CreatePresentation(SingleObject, Enabled.Contains("archived"));

            if (Empty)
                presentation = presentation with { Groups = [] };

            return Pending?.Task ?? Task.FromResult<HostResult<LensPresentation>>(new HostResult<LensPresentation>.Success(presentation));
        }

        public Task<string> EmphasizeAsync(CancellationToken cancellationToken)
        {
            HostCalls++;
            return Task.FromResult("Highlighted");
        }

        public Task<string> ClearAsync(CancellationToken cancellationToken)
        {
            HostCalls++;
            return Task.FromResult("Cleared");
        }
    }
}