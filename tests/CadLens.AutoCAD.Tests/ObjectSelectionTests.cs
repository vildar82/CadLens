using System.Collections.Immutable;
using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.GraphicsInterface;
using Common;
using Common.AutoCAD;
using Xunit;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

/// <summary>Exercises the production picker, queue, and temporary isolation with native API doubles.</summary>
[Collection("AutoCAD")]
public sealed class ObjectSelectionTests
{
    /// <summary>Empty preselection enters the actual native selection boundary before returning a detached result.</summary>
    [Fact]
    public Task EmptyPreselectionPromptsWithDrawingFocusedAndIsolationSuspended() => AutoCadTaskServiceTests.OnUiThread(async () =>
    {
        var document = CreateDocument();
        using var tasks = new AutoCadTaskService();
        var graphics = new EntityIsolationService();
        var target = document.Database.Add(new Entity());
        var hidden = document.Database.Add(new Entity());
        graphics.Apply(document.Database, [target], [target, hidden]);
        var actions = CreateActions(tasks, graphics);
        PromptSelectionOptions? requestedOptions = null;
        document.Editor.Pick = options =>
        {
            Assert.True(document.IsLocked);
            Assert.True(Application.DocumentManager.IsApplicationContext);
            Assert.Equal(1, document.Window.FocusCount);
            requestedOptions = options;
            Assert.False(graphics.IsApplicable(document.Database.Objects[hidden.Value]));
            document.Editor.Selection = [hidden];
            return new PromptSelectionResult([hidden]);
        };

        try
        {
            var request = actions.RequestObjectsAsync(CancellationToken.None);
            Assert.False(request.IsCompleted);
            await ExecuteQueuedRequest();
            var selected = Assert.IsType<HostResult<ImmutableArray<IPlacedObjectId>>.Success>(await request).Value;

            Assert.Equal(hidden, Assert.IsType<EntityId>(Assert.Single(selected)).NativeId);
            Assert.Equal(1, document.Editor.PromptCount);
            Assert.EndsWith("Select objects to explore:", Assert.IsType<PromptSelectionOptions>(requestedOptions).MessageForAdding);
            Assert.Empty(document.Editor.Selection);
            Assert.True(graphics.IsApplicable(document.Database.Objects[hidden.Value]));
        }
        finally
        {
            graphics.Clear(false);
            await tasks.StopAsync();
        }
    });

    /// <summary>A selection made while host work waits is reused without focusing or prompting.</summary>
    [Fact]
    public Task PreselectionAddedWhileQueuedSkipsPrompt() => AutoCadTaskServiceTests.OnUiThread(async () =>
    {
        var document = CreateDocument();
        using var tasks = new AutoCadTaskService();
        var actions = CreateActions(tasks, new EntityIsolationService());
        var request = actions.RequestObjectsAsync(CancellationToken.None);
        var selected = document.Database.Add(new Entity());
        document.Editor.Selection = [selected];
        await ExecuteQueuedRequest();

        Assert.Equal(selected, Assert.IsType<EntityId>(Assert.Single(Assert.IsType<HostResult<ImmutableArray<IPlacedObjectId>>.Success>(await request).Value)).NativeId);
        Assert.Equal(0, document.Editor.PromptCount);
        Assert.Equal(0, document.Window.FocusCount);
        await tasks.StopAsync();
    });

    /// <summary>Escape, native failure, and exceptions preserve isolation and the original empty CAD preselection.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public Task FailedPickRestoresPreviousEffects(int failure, bool focusSuccess) => AutoCadTaskServiceTests.OnUiThread(async () =>
    {
        var document = CreateDocument();
        using var tasks = new AutoCadTaskService();
        var graphics = new EntityIsolationService();
        var hidden = document.Database.Add(new Entity());
        graphics.Apply(document.Database, [], [hidden]);
        document.Window.FocusSuccess = focusSuccess;
        Application.MainWindow.FocusSuccess = false;
        document.Editor.Pick = _ =>
        {
            document.Editor.Selection = [hidden];

            return failure switch
            {
                0 => new PromptSelectionResult(PromptStatus.Cancel),
                1 => new PromptSelectionResult(PromptStatus.Error),
                _ => throw new InvalidOperationException("selection fixture failure")
            };
        };

        try
        {
            var request = CreateActions(tasks, graphics).RequestObjectsAsync(CancellationToken.None);
            await ExecuteQueuedRequest();

            Assert.IsType<HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable>(await request);
            Assert.Equal(1, document.Editor.PromptCount);
            Assert.Empty(document.Editor.Selection);
            Assert.True(graphics.IsApplicable(document.Database.Objects[hidden.Value]));
        }
        finally
        {
            graphics.Clear(false);
            await tasks.StopAsync();
        }
    });

    /// <summary>Failure to redraw restored isolation still restores the original CAD preselection.</summary>
    [Fact]
    public Task FailedRestoreRedrawStillRestoresPreselection() => AutoCadTaskServiceTests.OnUiThread(async () =>
    {
        var document = CreateDocument();
        using var tasks = new AutoCadTaskService();
        var graphics = new EntityIsolationService();
        var hidden = document.Database.Add(new Entity());
        graphics.Apply(document.Database, [], [hidden]);
        document.Editor.Pick = _ =>
        {
            document.Editor.Selection = [hidden];
            document.Drawing.OnRegen = () => throw new InvalidOperationException("redraw fixture failure");
            return new PromptSelectionResult([hidden]);
        };

        try
        {
            var request = CreateActions(tasks, graphics).RequestObjectsAsync(CancellationToken.None);
            await ExecuteQueuedRequest();

            Assert.IsType<HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable>(await request);
            Assert.Equal(1, document.Editor.PromptCount);
            Assert.Empty(document.Editor.Selection);
            Assert.True(graphics.IsApplicable(document.Database.Objects[hidden.Value]));
        }
        finally
        {
            graphics.Clear(false);
            await tasks.StopAsync();
        }
    });

    /// <summary>Context changes during queue waiting, native focus, redraw, or selection cannot produce a stale set.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public Task ChangedSpaceRejectsSelectionAtEveryNativeBoundary(int boundary) => AutoCadTaskServiceTests.OnUiThread(async () =>
    {
        var document = CreateDocument();
        using var tasks = new AutoCadTaskService();
        var graphics = new EntityIsolationService();
        var hidden = document.Database.Add(new Entity());
        graphics.Apply(document.Database, [], [hidden]);

        switch (boundary)
        {
            case 1:
                document.Window.OnFocus = ChangeSpace;
                break;
            case 2:
                document.Drawing.OnRegen = ChangeSpace;
                break;
            case 4:
                document.Window.FocusSuccess = false;
                Application.MainWindow.OnFocus = ChangeSpace;
                break;
            case 5:
                document.Window.FocusSuccess = false;
                document.Window.OnFocus = ChangeSpace;
                break;
        }

        document.Editor.Pick = _ =>
        {
            ChangeSpace();
            document.Editor.Selection = [hidden];
            return new PromptSelectionResult([hidden]);
        };

        try
        {
            var request = CreateActions(tasks, graphics).RequestObjectsAsync(CancellationToken.None);

            if (boundary == 0)
                ChangeSpace();

            await ExecuteQueuedRequest();
            Assert.IsType<HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable>(await request);
            Assert.Equal(boundary == 3 ? 1 : 0, document.Editor.PromptCount);

            switch (boundary)
            {
                case 5:
                    Assert.Equal(0, Application.MainWindow.FocusCount);
                    break;
                case 3:
                    Assert.Equal([hidden], document.Editor.Selection);
                    break;
            }
        }
        finally
        {
            graphics.Clear(false);
            await tasks.StopAsync();
        }

        return;

        void ChangeSpace() => document.Database.CurrentSpaceId = document.Database.Add(new BlockTableRecord());
    });

    /// <summary>Panel cancellation during focus or redraw prevents starting native selection.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public Task CancellationBeforePickDoesNotPrompt(int boundary) => AutoCadTaskServiceTests.OnUiThread(async () =>
    {
        var document = CreateDocument();
        using var tasks = new AutoCadTaskService();
        using var cancellation = new CancellationTokenSource();
        var graphics = new EntityIsolationService();
        var hidden = document.Database.Add(new Entity());
        graphics.Apply(document.Database, [], [hidden]);

        switch (boundary)
        {
            case 0:
                document.Window.OnFocus = Cancel;
                break;
            case 1:
                document.Window.FocusSuccess = false;
                Application.MainWindow.OnFocus = Cancel;
                break;
            case 2:
                document.Drawing.OnRegen = Cancel;
                break;
            case 3:
                document.Window.FocusSuccess = false;
                document.Window.OnFocus = Cancel;
                break;
        }

        var request = CreateActions(tasks, graphics).RequestObjectsAsync(cancellation.Token);
        await ExecuteQueuedRequest();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);

        Assert.Equal(0, document.Editor.PromptCount);

        if (boundary == 3)
            Assert.Equal(0, Application.MainWindow.FocusCount);

        Assert.False(graphics.IsApplicable(document.Database.Objects[hidden.Value]));
        await tasks.StopAsync();

        return;

        void Cancel()
        {
            // ReSharper disable once AccessToDisposedClosure -- Native callbacks complete before this test disposes the source.
            cancellation.Cancel();
            graphics.Clear(false);
        }
    });

    /// <summary>Closing during native input rejects a late successful pick and never restores cleared effects.</summary>
    [Fact]
    public Task CancellationDuringPickRejectsLateSuccess() => AutoCadTaskServiceTests.OnUiThread(async () =>
    {
        var document = CreateDocument();
        using var tasks = new AutoCadTaskService();
        using var cancellation = new CancellationTokenSource();
        var graphics = new EntityIsolationService();
        var hidden = document.Database.Add(new Entity());
        graphics.Apply(document.Database, [], [hidden]);
        document.Editor.Pick = _ =>
        {
            // ReSharper disable once AccessToDisposedClosure -- The native callback completes before this test disposes the source.
            cancellation.Cancel();
            graphics.Clear(false);
            document.Editor.Selection = [hidden];
            return new PromptSelectionResult([hidden]);
        };

        var request = CreateActions(tasks, graphics).RequestObjectsAsync(cancellation.Token);
        await ExecuteQueuedRequest();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);

        Assert.Equal(1, document.Editor.PromptCount);
        Assert.Equal([hidden], document.Editor.Selection);
        Assert.False(graphics.IsApplicable(document.Database.Objects[hidden.Value]));
        Assert.DoesNotContain(graphics, DrawableOverrule.Registered);
        await tasks.StopAsync();
    });

    /// <summary>Focus return values never block selection, and the public main-window fallback is attempted.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FocusFailureStillReturnsChosenObjects(bool mainWindowFocusSuccess) => AutoCadTaskServiceTests.OnUiThread(async () =>
    {
        var document = CreateDocument();
        using var tasks = new AutoCadTaskService();
        var graphics = new EntityIsolationService();
        var hidden = document.Database.Add(new Entity());
        graphics.Apply(document.Database, [], [hidden]);
        document.Window.FocusSuccess = false;
        Application.MainWindow.FocusSuccess = mainWindowFocusSuccess;
        document.Editor.Pick = _ =>
        {
            Assert.False(graphics.IsApplicable(document.Database.Objects[hidden.Value]));
            document.Editor.Selection = [hidden];
            return new PromptSelectionResult([hidden]);
        };

        try
        {
            var request = CreateActions(tasks, graphics).RequestObjectsAsync(CancellationToken.None);
            await ExecuteQueuedRequest();

            var selected = Assert.IsType<HostResult<ImmutableArray<IPlacedObjectId>>.Success>(await request).Value;
            Assert.Equal(hidden, Assert.IsType<EntityId>(Assert.Single(selected)).NativeId);
            Assert.Equal(1, document.Window.FocusCount);
            Assert.Equal(1, Application.MainWindow.FocusCount);
            Assert.Equal(1, document.Editor.PromptCount);
            Assert.Empty(document.Editor.Selection);
            Assert.True(graphics.IsApplicable(document.Database.Objects[hidden.Value]));
        }
        finally
        {
            graphics.Clear(false);
            await tasks.StopAsync();
        }
    });

    private static ObjectExplorerActions CreateActions(IHostTaskService tasks, IEntityIsolationService graphics) =>
        new(null!, null!, graphics, null!, tasks);

    private static Document CreateDocument()
    {
        var document = Application.DocumentManager.MdiActiveDocument!;
        document.Database.CurrentSpaceId = document.Database.Add(new BlockTableRecord());
        return document;
    }

    private static async Task ExecuteQueuedRequest()
    {
        await Dispatcher.Yield();
        Application.DocumentManager.ExecuteNext();
    }
}
