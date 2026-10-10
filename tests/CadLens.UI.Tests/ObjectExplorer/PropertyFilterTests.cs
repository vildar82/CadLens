using System.Collections.Immutable;
using CadLens.Lenses;
using CadLens.Common;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Typed conditions keep the shared explorer and CAD targets consistent.</summary>
[Collection("Language changes")]
public sealed class PropertyFilterTests
{
    /// <summary>Filtering and grouping use retained facts and the same matching CAD targets.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task FilteringUpdatesCountsGroupsAndActionsWithoutReading(DrawingGrouping grouping)
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, grouping);
        await EnterType(model, grouping);
        await ApplyNumber(model, DrawingPropertyId.Length, DrawingFilterOperator.GreaterThan, "5");

        Assert.Equal(["2", "3"], model.Current!.Objects.Select(id => id.DisplayId));
        Assert.Equal(2, model.ObjectCount);
        await model.ToggleGroupingCommand.ExecuteAsync(
            model.GroupingOptions.Single(option => option.Id == DrawingPropertyId.Color));
        Assert.Equal(2, model.Items.Sum(node => node.Count));
        await model.SelectCommand.ExecuteAsync(null);
        await model.IsolateCommand.ExecuteAsync(null);
        await model.FocusCommand.ExecuteAsync(null);
        Assert.Equal(model.Current.Objects, actions.Selected);
        Assert.Equal(model.Current.Objects, actions.Isolated);
        Assert.Equal(model.Current.Objects, actions.Focused);
        Assert.Equal(1, actions.ReadCount);

        await model.ClearPropertyFilterCommand.ExecuteAsync(null);
        Assert.Null(model.AppliedPropertyFilter);
        Assert.Equal(4, model.Current.Count);
        Assert.Equal(4, model.Items.Sum(node => node.Count));
        Assert.Equal(1, actions.ReadCount);
    }

    /// <summary>No matches keeps filter choices available and clears effects without focusing an empty set.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task EmptyResultRemainsEditableAndClearsAutoEffects(DrawingGrouping grouping)
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, grouping);
        await EnterType(model, grouping);
        await model.ToggleAutoSelectCommand.ExecuteAsync(null);
        await model.ToggleAutoIsolationCommand.ExecuteAsync(null);
        await model.ToggleAutoFocusCommand.ExecuteAsync(null);
        var focusCalls = actions.FocusCalls;
        await ApplyNumber(model, DrawingPropertyId.Length, DrawingFilterOperator.LessThan, "0");

        Assert.Equal(LensNodeKind.Type, model.Current!.Kind);
        Assert.Empty(model.Items);
        Assert.Empty(actions.Selected);
        Assert.Empty(actions.Isolated);
        Assert.Equal(focusCalls, actions.FocusCalls);
        Assert.False(model.FocusCommand.CanExecute(null));
        Assert.False(model.SelectCommand.CanExecute(null));
        Assert.False(model.IsolateCommand.CanExecute(null));
        Assert.True(model.IsEmpty);
        Assert.Contains(model.PropertyFilter.PropertyOptions, option => option.Id == DrawingPropertyId.Length);
        Assert.Contains(model.GroupingOptions, option => option.Id == DrawingPropertyId.Color);
        Assert.True(model.ClearPropertyFilterCommand.CanExecute(null));

        await model.ClearPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(4, model.Current.Count);
        Assert.Equal(model.Current.Objects, actions.Selected);
        Assert.Equal(model.Current.Objects, actions.Isolated);
        Assert.Equal(1, actions.ReadCount);
    }

    /// <summary>Invalid drafts never replace the applied condition or disturb drawing effects.</summary>
    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1,000")]
    [InlineData("not a number")]
    public async Task InvalidNumberKeepsPreviousResult(string input)
    {
        var previous = UiText.Current.Preference;
        try
        {
            UiText.Current.Select(LanguagePreference.English, persist: false);
            var actions = new Actions();
            using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
            await EnterType(model, DrawingGrouping.ObjectTypes);
            await ApplyNumber(model, DrawingPropertyId.Length, DrawingFilterOperator.GreaterThan, "5");
            await model.IsolateCommand.ExecuteAsync(null);
            var applied = model.AppliedPropertyFilter;
            var targets = model.Current!.Objects;
            var effects = actions.EffectCalls;

            model.PropertyFilter.InputText = input;
            await model.ApplyPropertyFilterCommand.ExecuteAsync(null);

            Assert.Equal(applied, model.AppliedPropertyFilter);
            Assert.Equal(targets, model.Current.Objects);
            Assert.Equal(effects, actions.EffectCalls);
            Assert.False(string.IsNullOrWhiteSpace(model.PropertyFilter.Error));
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    }

    /// <summary>A surviving object stays open; a removed object falls back to its type.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task FilteringPreservesOnlySurvivingNavigation(DrawingGrouping grouping)
    {
        using var model = new ObjectExplorerViewModel(new Actions(), grouping);
        await EnterType(model, grouping);
        await model.EnterCommand.ExecuteAsync(model.Items.Single(node => node.Id == "2"));
        await ApplyNumber(model, DrawingPropertyId.Length, DrawingFilterOperator.GreaterThan, "5");
        Assert.Equal("2", model.Current!.Id);
        await ApplyNumber(model, DrawingPropertyId.Length, DrawingFilterOperator.GreaterThan, "15");
        Assert.Equal(LensNodeKind.Type, model.Current.Kind);
        Assert.Equal("3", Assert.Single(model.Items).Id);
    }

    /// <summary>Filtering a captured CAD selection never requests a new selection.</summary>
    [Fact]
    public async Task SelectedScopeRetainsItsInventory()
    {
        var actions = new Actions {NativeSelection = [new TestEntityId(1), new TestEntityId(2)]};
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await model.ActivateAsync(CancellationToken.None);
        await model.ShowSelectedObjectsCommand.ExecuteAsync(null);
        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        await ApplyNumber(model, DrawingPropertyId.Length, DrawingFilterOperator.GreaterThan, "5");
        Assert.Equal("2", Assert.Single(model.Current!.Objects).DisplayId);
        await model.ClearPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(2, model.Current.Count);
        Assert.Equal(2, actions.ReadCount);
        Assert.Equal(1, actions.SelectionRequests);
    }

    /// <summary>Filters survive collapse, stay independent per lens, and clear with drawing context.</summary>
    [Fact]
    public async Task FilterLifetimeFollowsThePanelDrawingContext()
    {
        using var layers = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.Layers);
        using var objects = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.ObjectTypes);
        await EnterType(layers, DrawingGrouping.Layers);
        await EnterType(objects, DrawingGrouping.ObjectTypes);
        await ApplyNumber(layers, DrawingPropertyId.Length, DrawingFilterOperator.GreaterThan, "15");
        Assert.Equal(4, objects.Current!.Count);
        Assert.Null(objects.AppliedPropertyFilter);
        await layers.DeactivateAsync(CancellationToken.None);
        await layers.ActivateAsync(CancellationToken.None);
        Assert.Equal(1, layers.Current!.Count);
        Assert.NotNull(layers.AppliedPropertyFilter);
        await layers.ResetContextAsync();
        await layers.EnterCommand.ExecuteAsync(Assert.Single(layers.Items));
        await layers.EnterCommand.ExecuteAsync(Assert.Single(layers.Items));
        Assert.Null(layers.AppliedPropertyFilter);
        Assert.Equal(4, layers.Current!.Count);
    }

    /// <summary>App-language decimal input and degree conversion preserve raw typed comparisons.</summary>
    [Theory]
    [InlineData(LanguagePreference.English, "1.5")]
    [InlineData(LanguagePreference.Russian, "1,5")]
    public async Task NumericInputUsesAppLanguageAndAngleUnits(LanguagePreference language, string input)
    {
        var previous = UiText.Current.Preference;
        try
        {
            UiText.Current.Select(language, persist: false);
            using var model = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.ObjectTypes);
            await EnterType(model, DrawingGrouping.ObjectTypes);
            await ApplyNumber(model, DrawingPropertyId.Length, DrawingFilterOperator.Equal, input);
            Assert.Equal("1", Assert.Single(model.Items).Id);
            await ApplyNumber(model, DrawingPropertyId.StartAngle, DrawingFilterOperator.Equal, "90");
            var number = Assert.IsType<DrawingNumberValue>(model.AppliedPropertyFilter!.Value);
            Assert.Equal(DrawingUnit.Angle, number.Unit);
            Assert.Equal(Math.PI / 2, number.Value, 12);
            Assert.Equal("2", Assert.Single(model.Items).Id);
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    }

    /// <summary>Booleans and assigned colors use typed values; text matching ignores case.</summary>
    [Fact]
    public async Task NonNumericConditionsUseTheirTypedValues()
    {
        using var model = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.ObjectTypes);
        await EnterType(model, DrawingGrouping.ObjectTypes);
        model.PropertyFilter.PropertyId = DrawingPropertyId.Color;
        model.PropertyFilter.Operator = DrawingFilterOperator.NotEqual;
        model.PropertyFilter.SelectedValue = new DrawingColorValue(new AssignedColor(AssignedColorKind.ByLayer));
        await model.ApplyPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(["2", "3"], model.Items.Select(node => node.Id));
        model.PropertyFilter.PropertyId = DrawingPropertyId.Closed;
        model.PropertyFilter.Operator = DrawingFilterOperator.Equal;
        model.PropertyFilter.SelectedValue = new DrawingBooleanValue(false);
        await model.ApplyPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(["1", "3", "4"], model.Items.Select(node => node.Id));
        model.PropertyFilter.PropertyId = DrawingPropertyId.Text;
        model.PropertyFilter.Operator = DrawingFilterOperator.Contains;
        model.PropertyFilter.InputText = "ROAD";
        await model.ApplyPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(["1", "2"], model.Items.Select(node => node.Id));
    }

    /// <summary>Zero-match choices retain the original type scope while respecting layers and visibility.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task FilterChoicesRespectTheUnfilteredIncludedScope(DrawingGrouping grouping)
    {
        var inventory = Inventory();
        var other = new LayerId("Other");
        var hidden = new LayerId("Hidden");
        var sample = inventory.Entities[0];
        var actions = new Actions
        {
            InventoryOverride = inventory with
            {
                Layers =
                [
                    .. inventory.Layers,
                    new LayerSnapshot(other, "Other", false, false, false, false),
                    new LayerSnapshot(hidden, "Hidden", true, false, false, false)
                ],
                Entities =
                [
                    .. inventory.Entities,
                    sample with
                    {
                        Id = new TestEntityId(5),
                        LayerId = other,
                        Properties = sample.Properties!.Add(DrawingPropertyId.Radius, new DrawingNumberValue(5, DrawingUnit.Distance))
                    },
                    sample with
                    {
                        Id = new TestEntityId(6),
                        LayerId = hidden,
                        Properties = sample.Properties!.Add(DrawingPropertyId.Thickness, new DrawingNumberValue(5, DrawingUnit.Distance))
                    }
                ]
            }
        };
        using var model = new ObjectExplorerViewModel(actions, grouping);
        await model.ActivateAsync(CancellationToken.None);
        if (grouping == DrawingGrouping.Layers)
            await model.EnterCommand.ExecuteAsync(model.Items.Single(node => node.Id == "Roads"));

        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        await ApplyNumber(model, DrawingPropertyId.Length, DrawingFilterOperator.LessThan, "0");
        Assert.Empty(model.Items);
        Assert.Equal(
            grouping == DrawingGrouping.ObjectTypes,
            model.PropertyFilter.PropertyOptions.Any(option => option.Id == DrawingPropertyId.Radius));
        Assert.DoesNotContain(model.PropertyFilter.PropertyOptions, option => option.Id == DrawingPropertyId.Thickness);
        Assert.Contains(model.PropertyFilter.PropertyOptions, option => option.Id == DrawingPropertyId.Length);
        Assert.Equal(1, actions.ReadCount);
    }

    /// <summary>Application-owned text offers localized captions while matching their original typed values.</summary>
    [Fact]
    public async Task RussianAssignedTextUsesObservedTypedChoices()
    {
        var previous = UiText.Current.Preference;
        try
        {
            UiText.Current.Select(LanguagePreference.Russian, persist: false);
            using var model = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.ObjectTypes);
            await EnterType(model, DrawingGrouping.ObjectTypes);
            model.PropertyFilter.PropertyId = DrawingPropertyId.Linetype;
            Assert.True(model.PropertyFilter.UsesValueOptions);
            var byLayer = Assert.Single(model.PropertyFilter.ValueOptions, option => option.Label == "По слою");
            model.PropertyFilter.SelectedValue = byLayer.Value;
            await model.ApplyPropertyFilterCommand.ExecuteAsync(null);
            Assert.Equal("1", Assert.Single(model.Items).Id);
            Assert.Equal(new DrawingTextValue("ByLayer", true), model.AppliedPropertyFilter!.Value);
            UiText.Current.Select(LanguagePreference.English, persist: false);
            Assert.Contains(model.PropertyFilter.ValueOptions, option => option.Label == "ByLayer");
            Assert.Equal(byLayer.Value, model.PropertyFilter.SelectedValue);
            await model.ApplyPropertyFilterCommand.ExecuteAsync(null);
            Assert.Equal("1", Assert.Single(model.Items).Id);
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    }

    /// <summary>A context change cancels remaining filter effects and discards the old typed condition.</summary>
    [Fact]
    public async Task ContextChangeDuringFilteringDoesNotRestoreOldTargets()
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await EnterType(model, DrawingGrouping.ObjectTypes);
        await model.ToggleAutoSelectCommand.ExecuteAsync(null);
        await model.ToggleAutoIsolationCommand.ExecuteAsync(null);
        var pending = new TaskCompletionSource<HostResult<bool>>();
        actions.PendingSelection = pending;
        model.PropertyFilter.PropertyId = DrawingPropertyId.Length;
        model.PropertyFilter.Operator = DrawingFilterOperator.GreaterThan;
        model.PropertyFilter.InputText = "5";
        var filtering = model.ApplyPropertyFilterCommand.ExecuteAsync(null);
        Assert.True(model.IsBusy);
        var resetting = model.ResetContextAsync();
        actions.PendingSelection = null;
        pending.SetResult(new HostResult<bool>.Success(true));
        await filtering;
        await resetting;
        Assert.Null(model.Current);
        Assert.Empty(actions.Selected);
        Assert.Empty(actions.Isolated);
        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        Assert.Null(model.AppliedPropertyFilter);
        Assert.Equal(4, model.Current!.Count);
    }

    /// <summary>Changed targets clear stale manual isolation while ordinary manual selection remains explicit.</summary>
    [Fact]
    public async Task ChangedTargetsFollowTheManualNavigationEffectPolicy()
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await EnterType(model, DrawingGrouping.ObjectTypes);
        await model.SelectCommand.ExecuteAsync(null);
        await model.IsolateCommand.ExecuteAsync(null);
        var selected = actions.Selected;
        await ApplyNumber(model, DrawingPropertyId.Length, DrawingFilterOperator.GreaterThan, "5");
        Assert.Equal(selected, actions.Selected);
        Assert.Empty(actions.Isolated);
        Assert.Equal(0, actions.FocusCalls);
        await ApplyNumber(model, DrawingPropertyId.Length, DrawingFilterOperator.LessThan, "0");
        Assert.Empty(actions.Selected);
        Assert.Empty(actions.Isolated);
    }

    private static async Task EnterType(ObjectExplorerViewModel model, DrawingGrouping grouping)
    {
        await model.ActivateAsync(CancellationToken.None);
        if (grouping == DrawingGrouping.Layers)
            await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));

        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
    }

    private static async Task ApplyNumber(
        ObjectExplorerViewModel model,
        DrawingPropertyId property,
        DrawingFilterOperator comparison,
        string input)
    {
        model.PropertyFilter.PropertyId = property;
        model.PropertyFilter.Operator = comparison;
        model.PropertyFilter.InputText = input;
        await model.ApplyPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, model.PropertyFilter.Error);
    }

    private static DrawingInventory Inventory()
    {
        var layer = new LayerId("Roads");
        return new DrawingInventory(
            "Model",
            [new LayerSnapshot(layer, "Roads", false, false, false, false)],
            [Entity(1, 1.5), Entity(2, 10), Entity(3, 20), Entity(4, null)]);

        EntitySnapshot Entity(int id, double? length) => new(
            new TestEntityId(id),
            layer,
            "AcDbPolyline",
            new Dictionary<DrawingPropertyId, DrawingValue?>
            {
                [DrawingPropertyId.Length] = length is { } value ? new DrawingNumberValue(value, DrawingUnit.Distance) : null,
                [DrawingPropertyId.StartAngle] = new DrawingNumberValue((id - 1) * Math.PI / 2, DrawingUnit.Angle),
                [DrawingPropertyId.Closed] = new DrawingBooleanValue(id == 2),
                [DrawingPropertyId.Linetype] = new DrawingTextValue(id == 1 ? "ByLayer" : "Dashed", id == 1),
                [DrawingPropertyId.Color] = id == 4 ? null : new DrawingColorValue(
                    new AssignedColor(id == 1 ? AssignedColorKind.ByLayer : AssignedColorKind.Index, id == 1 ? 0 : 1)),
                [DrawingPropertyId.Text] = new DrawingTextValue(id <= 2 ? "Road " + id : "Other")
            }.ToImmutableDictionary(),
            DrawingPropertyId.Length);
    }

    private sealed record LayerId(string DisplayId) : ILayerId;

    internal sealed class Actions : IObjectExplorerActions
    {
        internal int ReadCount { get; private set; }
        internal int SelectionRequests { get; private set; }
        internal int FocusCalls { get; private set; }
        internal int EffectCalls { get; private set; }
        internal DrawingInventory? InventoryOverride { get; init; }
        internal TaskCompletionSource<HostResult<bool>>? PendingSelection { get; set; }
        internal ImmutableArray<IPlacedObjectId> NativeSelection { get; init; } = [];
        internal ImmutableArray<IPlacedObjectId> Selected { get; private set; } = [];
        internal ImmutableArray<IPlacedObjectId> Isolated { get; private set; } = [];
        internal ImmutableArray<IPlacedObjectId> Focused { get; private set; } = [];

        public Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlyCollection<string> enabledFilters,
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken,
            int? maximumObjects = null)
        {
            ReadCount++;
            var inventory = InventoryOverride ?? Inventory();
            if (selectedObjects is { } selected)
                inventory = inventory with {Entities = [.. inventory.Entities.Where(entity => selected.Contains(entity.Id))]};

            return Task.FromResult<HostResult<LensPresentation>>(
                new HostResult<LensPresentation>.Success(DrawingLensProvider.Build(inventory, grouping, enabledFilters)));
        }

        public Task<HostResult<ImmutableArray<IPlacedObjectId>>> RequestObjectsAsync(CancellationToken cancellationToken)
        {
            SelectionRequests++;
            return Task.FromResult<HostResult<ImmutableArray<IPlacedObjectId>>>(
                new HostResult<ImmutableArray<IPlacedObjectId>>.Success(NativeSelection));
        }

        public void ClearImmediately(bool hostTerminating)
        {
            EffectCalls++;
            Selected = [];
            Isolated = [];
        }

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken)
        {
            ClearImmediately(false);
            return Success();
        }

        public Task<HostResult<bool>> SelectAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            EffectCalls++;
            Selected = objects;
            return PendingSelection?.Task ?? Success();
        }

        public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken)
        {
            EffectCalls++;
            Isolated = [];
            return Success();
        }

        public Task<string> IsolateObjectsAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            Assert.NotEmpty(objects);
            EffectCalls++;
            Isolated = objects;
            return Task.FromResult("Isolated.");
        }

        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            Assert.NotEmpty(objects);
            EffectCalls++;
            FocusCalls++;
            Focused = objects;
            return Task.FromResult("Focused.");
        }

        private static Task<HostResult<bool>> Success() =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
    }
}
