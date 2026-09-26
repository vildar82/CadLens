using System.Windows;
using System.Windows.Controls;
using Common;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>The shell coordinates arbitrary modules without imposing a data or command contract.</summary>
public sealed class MultipleLensTests
{
    /// <summary>Switching preserves each module's own state and calls only the chosen lifecycle.</summary>
    [Fact]
    public async Task SwitchingUsesIndependentModules()
    {
        var first = new Lens("first");
        var second = new Lens("second");
        using var model = new ExplorerViewModel([first, second]);
        Assert.Equal(0, first.ActivationCount + second.ActivationCount);
        await model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]);
        first.CustomValue = 42;
        await model.ToggleLensCommand.ExecuteAsync(model.Lenses[1]);
        Assert.Equal(1, first.CleanupCount);
        Assert.True(model.Lenses[1].IsActive);
        Assert.Equal(0, second.CustomValue);
        await model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]);
        Assert.Equal(42, first.CustomValue);
        Assert.Equal(2, first.ActivationCount);
        Assert.Equal(1, second.CleanupCount);
    }

    /// <summary>A canceled old activation settles before cleanup and the next activation.</summary>
    [Fact]
    public async Task SwitchingOrdersActivationAndCleanup()
    {
        var first = new Lens("first") { PendingActivation = new TaskCompletionSource(), PendingCleanup = new TaskCompletionSource<HostResult<bool>>() };
        var second = new Lens("second");
        using var model = new ExplorerViewModel([first, second]);
        var activation = model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]);
        var switching = model.ToggleLensCommand.ExecuteAsync(model.Lenses[1]);
        Assert.True(first.ActivationToken.IsCancellationRequested);
        Assert.False(model.IsLensActive);
        Assert.Equal(0, first.CleanupCount);
        Assert.Equal(0, second.ActivationCount);
        first.PendingActivation.SetResult();
        await activation;
        await first.CleanupStarted.Task;
        Assert.False(first.CleanupToken.IsCancellationRequested);
        Assert.Equal(0, second.ActivationCount);
        first.PendingCleanup.SetResult(new HostResult<bool>.Success(true));
        await switching;
        Assert.True(model.Lenses[1].IsActive);
        Assert.Equal(1, second.ActivationCount);
    }

    /// <summary>Failures remain visible and retry old-module cleanup before activating a different module.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailureCanBeRetried(bool throws)
    {
        var first = new Lens("first") { CleanupFails = true, CleanupThrows = throws };
        var second = new Lens("second");
        using var model = new ExplorerViewModel([first, second]);
        await model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]);
        await model.ToggleLensCommand.ExecuteAsync(model.Lenses[1]);
        Assert.False(model.IsLensActive);
        Assert.Equal(0, second.ActivationCount);
        Assert.Contains("Fixture", model.Status);
        first.CleanupFails = false;
        first.CleanupThrows = false;
        await model.ToggleLensCommand.ExecuteAsync(model.Lenses[1]);
        Assert.Equal(2, first.CleanupCount);
        Assert.True(model.Lenses[1].IsActive);
    }

    /// <summary>Every module receives context changes.</summary>
    [Fact]
    public async Task ContextChangesReachEveryModule()
    {
        var first = new Lens("first");
        var second = new Lens("second");
        using var model = new ExplorerViewModel([first, second]);
        await model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]);
        model.ResetContext(false);
        Assert.Equal(1, first.ContextCount);
        Assert.Equal(1, second.ContextCount);
        Assert.False(model.ToggleLensCommand.CanExecute(model.Lenses[1]));
        Assert.True(model.ToggleLensCommand.CanExecute(model.Lenses[0]));
    }

    /// <summary>Close or a context change during cleanup prevents a queued lens activation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LifetimeChangeDuringSwitchRejectsActivation(bool close)
    {
        var first = new Lens("first") { PendingCleanup = new TaskCompletionSource<HostResult<bool>>() };
        var second = new Lens("second");
        using var model = new ExplorerViewModel([first, second]);
        await model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]);
        var switching = model.ToggleLensCommand.ExecuteAsync(model.Lenses[1]);

        if (close)
            model.Close(true);
        else
            model.ResetContext();

        first.PendingCleanup.SetResult(new HostResult<bool>.Success(true));
        await switching;
        Assert.Equal(0, second.ActivationCount);
        Assert.False(model.IsLensActive);

        if (close)
        {
            Assert.Equal(1, first.CloseCount);
            Assert.Equal(1, second.CloseCount);
            Assert.True(first.HostTerminating);
        }
    }

    /// <summary>Duplicate module identities and empty configurations fail before creating any view.</summary>
    [Fact]
    public void RegistrationsRequireUniqueIdentities()
    {
        Assert.Throws<InvalidOperationException>(() => new ExplorerViewModel([]));
        Assert.Throws<InvalidOperationException>(() => new ExplorerViewModel([new Lens("same"), new Lens("same")]));
    }

    private sealed class Lens(string id) : ILens
    {
        public LensDescriptor Descriptor { get; } = new(id, id);
        public FrameworkElement View => new TextBox { Text = "Unrelated editable content" };
        internal int CustomValue { get; set; }
        internal int ActivationCount { get; private set; }
        internal int CleanupCount { get; private set; }
        internal int ContextCount { get; private set; }
        internal int CloseCount { get; private set; }
        internal bool HostTerminating { get; private set; }
        internal bool CleanupFails { get; set; }
        internal bool CleanupThrows { get; set; }
        internal CancellationToken ActivationToken { get; private set; }
        internal CancellationToken CleanupToken { get; private set; }
        internal TaskCompletionSource? PendingActivation { get; init; }
        internal TaskCompletionSource<HostResult<bool>>? PendingCleanup { get; init; }
        internal TaskCompletionSource CleanupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ActivateAsync(CancellationToken cancellationToken)
        {
            ActivationCount++;
            ActivationToken = cancellationToken;
            return PendingActivation?.Task ?? Task.CompletedTask;
        }

        public Task<HostResult<bool>> DeactivateAsync(CancellationToken cancellationToken)
        {
            CleanupCount++;
            CleanupToken = cancellationToken;
            CleanupStarted.TrySetResult();

            if (CleanupThrows)
                throw new InvalidOperationException("Fixture failure.");

            return PendingCleanup?.Task ?? Task.FromResult<HostResult<bool>>(CleanupFails
                ? new HostResult<bool>.Unavailable("Fixture unavailable.")
                : new HostResult<bool>.Success(true));
        }

        public void OnContextChanged(bool hasDrawing) => ContextCount++;

        public void Close(bool hostTerminating)
        {
            CloseCount++;
            HostTerminating = hostTerminating;
        }
    }
}
