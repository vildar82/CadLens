using System.Collections.Immutable;
using Common;
using Xunit;

namespace CadLens.Lenses.Tests;

/// <summary>Navigation is independent of concrete lenses and host actions.</summary>
public sealed class NavigationTests
{
    /// <summary>A non-layer provider supplies the complete generic vocabulary.</summary>
    [Fact]
    public async Task NonLayerLensSuppliesGroupsFieldsFiltersAndActions()
    {
        ILayersProvider provider = new FakeLens();
        var result = await provider.LoadAsync(new HashSet<string>(), default);
        var presentation = Assert.IsType<HostResult<LayersPresentation>.Success>(result).Value;
        Assert.Equal("Issues", presentation.Label);
        Assert.Equal("Severity", presentation.Groups[0].Fields[0].Label);
        Assert.Equal(IconRole.Group, presentation.Filters[0].Icon);
        Assert.Contains(LensAction.Focus, presentation.Groups[0].Actions);
    }

    /// <summary>Back restores ancestor targets; returning to root clears selection.</summary>
    [Fact]
    public void AncestorsRestoreBroaderObjectSets()
    {
        var state = CreateState();
        Assert.True(state.Enter("group"));
        Assert.True(state.Enter("type"));
        Assert.True(state.Enter("1"));
        Assert.False(state.CanPrevious);
        Assert.True(state.CanNext);
        Assert.Equal(1, state.Position);
        state.MoveObject(1);
        Assert.Equal(2, state.Position);
        Assert.False(state.CanNext);
        Assert.True(state.CanPrevious);
        state.MoveObject(1);
        Assert.Equal(2, state.Position);
        state.GoBackTo(2);
        Assert.Equal(2, state.Current!.Count);
        state.GoBackTo(1);
        Assert.Equal("group", state.Current!.Id);
        state.GoBackTo(0);
        Assert.Null(state.Current);
    }

    /// <summary>Refresh retains the nearest valid ancestor and context changes reset everything.</summary>
    [Fact]
    public void DeletedTargetsAndExcludedGroupsReconcileThePath()
    {
        var state = CreateState();
        state.Enter("group");
        state.Enter("type");
        state.Enter("1");
        state.Reset([Group([Object("2")])], true);
        Assert.Equal("type", state.Current!.Id);
        state.Enter("2");
        Assert.Equal(1, state.Position);
        Assert.Equal(1, state.ObjectCount);
        Assert.False(state.CanPrevious);
        Assert.False(state.CanNext);
        state.Reset([], true);
        Assert.Null(state.Current);
        state.Reset([Group([Object("1")])], false);
        Assert.Null(state.Current);
        Assert.False(state.Enter("missing"));
    }

    private static NavigationState CreateState()
    {
        var state = new NavigationState();
        state.Reset([Group([Object("1"), Object("2")])], false);
        return state;
    }

    private static LensNode Object(string id) => new(id, id, [new TestEntityId(id)], [], [], []);

    private static LensNode Group(ImmutableArray<LensNode> objects)
    {
        var references = objects.SelectMany(item => item.Objects).ToImmutableArray();
        var type = new LensNode("type", "Duplicates", references, objects, [], [LensAction.Focus]);
        return new LensNode(
            "group",
            "High severity",
            references,
            [type],
            [new DetailField("Severity", "High")],
            [LensAction.Focus]);
    }

    private sealed class FakeLens : ILayersProvider
    {
        public Task<HostResult<LayersPresentation>> LoadAsync(
            IReadOnlySet<string> enabledFilters,
            CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<LayersPresentation>>(
                new HostResult<LayersPresentation>.Success(
                    new LayersPresentation(
                        "Issues",
                        "Example",
                        [Group([Object("1")])],
                        [new BooleanFilter("resolved", "Include resolved", "Include resolved issues", IconRole.Group)],
                        "No issues.")));
    }
}
