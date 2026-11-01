using CadLens.Common;
using CadLens.Lenses;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Checks how Map mode follows the list and when it leaves the drawing.</summary>
public sealed class MapModeTests
{
    /// <summary>Each of the 12 rows gets its own color, later rows share gray, and rows show the drawing colors.</summary>
    [Fact]
    public async Task RowsBecomeTheLegend()
    {
        var (actions, lens) = await CreateLensAsync(14);
        using var _ = lens;
        var model = lens.ViewModel;

        await model.ToggleMapCommand.ExecuteAsync(null);

        var groups = Assert.Single(actions.Paints);
        Assert.Equal(14, groups.Length);
        Assert.Equal(13, groups.Select(group => group.Color).Distinct().Count());
        Assert.Equal(2, groups.Count(group => group.Color == 0x808080));
        Assert.All(
            model.Items,
            row => Assert.Equal(groups.Single(group => group.Objects.SequenceEqual(row.Objects)).Color, row.DisplayColor));
    }

    /// <summary>An action that leaves the list unchanged does not regenerate the drawing again.</summary>
    [Fact]
    public async Task UnchangedListIsNotRepainted()
    {
        var (actions, lens) = await CreateLensAsync();
        using var _ = lens;
        var model = lens.ViewModel;
        await model.ToggleMapCommand.ExecuteAsync(null);

        await model.ToggleAutoFocusCommand.ExecuteAsync(null);

        Assert.Single(actions.Paints);
    }

    /// <summary>Refresh colors the objects that were read again.</summary>
    [Fact]
    public async Task RefreshRepaints()
    {
        var (actions, lens) = await CreateLensAsync();
        using var _ = lens;
        var model = lens.ViewModel;
        await model.ToggleMapCommand.ExecuteAsync(null);

        await model.ReadCommand.ExecuteAsync(null);

        Assert.Equal(2, actions.Paints.Count);
    }

    /// <summary>Turning the map off clears the drawing and the legend.</summary>
    [Fact]
    public async Task ToggleOffClearsPaintAndLegend()
    {
        var (actions, lens) = await CreateLensAsync();
        using var _ = lens;
        var model = lens.ViewModel;
        await model.ToggleMapCommand.ExecuteAsync(null);

        await model.ToggleMapCommand.ExecuteAsync(null);

        Assert.False(model.IsMapMode);
        Assert.Empty(actions.Paints[^1]);
        Assert.All(model.Items, row => Assert.Null(row.DisplayColor));
    }

    /// <summary>Reset turns the map off together with the other effects.</summary>
    [Fact]
    public async Task ResetTurnsMapOff()
    {
        var (actions, lens) = await CreateLensAsync();
        using var _ = lens;
        var model = lens.ViewModel;
        await model.ToggleMapCommand.ExecuteAsync(null);

        await model.ResetCommand.ExecuteAsync(null);

        Assert.False(model.IsMapMode);
        Assert.Empty(actions.Paints[^1]);
    }

    /// <summary>Collapsing the lens removes the paint, and expanding it brings the map back.</summary>
    [Fact]
    public async Task DeactivationClearsPaintUntilReactivated()
    {
        var (actions, lens) = await CreateLensAsync();
        using var _ = lens;
        var model = lens.ViewModel;
        await model.ToggleMapCommand.ExecuteAsync(null);

        await lens.DeactivateAsync(CancellationToken.None);

        Assert.Empty(actions.Paints[^1]);
        Assert.True(model.IsMapMode);

        await lens.ActivateAsync(CancellationToken.None);

        Assert.NotEmpty(actions.Paints[^1]);
    }

    /// <summary>A drawing change clears the paint through the host and repaints the new list.</summary>
    [Fact]
    public async Task ContextChangeRepaints()
    {
        var (actions, lens) = await CreateLensAsync();
        using var _ = lens;
        var model = lens.ViewModel;
        await model.ToggleMapCommand.ExecuteAsync(null);

        await model.ResetContextAsync();

        Assert.Equal(2, actions.Paints.Count);
        Assert.NotEmpty(actions.Paints[^1]);
    }

    /// <summary>A failed paint turns the map off and explains why.</summary>
    [Fact]
    public async Task FailedPaintTurnsMapOff()
    {
        var (actions, lens) = await CreateLensAsync();
        using var _ = lens;
        var model = lens.ViewModel;
        actions.PaintResult = new HostResult<bool>.Unavailable("No active drawing.");

        await model.ToggleMapCommand.ExecuteAsync(null);

        Assert.False(model.IsMapMode);
        Assert.Contains("No active drawing.", model.Status);
        Assert.All(model.Items, row => Assert.Null(row.DisplayColor));
    }

    private static async Task<(AppearanceTests.Actions Actions, ObjectExplorerLens Lens)> CreateLensAsync(int rows = 3)
    {
        var actions = new AppearanceTests.Actions(rows);
        var lens = new ObjectExplorerLens(actions, DrawingGrouping.ObjectTypes);
        await lens.ActivateAsync(CancellationToken.None);

        return (actions, lens);
    }
}