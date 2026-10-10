using System.Collections.Immutable;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CadLens.Lenses;
using CadLens.Common;
using CadLens.Common.AutoCAD;
using Xunit;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

/// <summary>Exercises production selection capture and scoped reads against native API doubles.</summary>
[Collection("AutoCAD")]
public sealed class DrawingInventorySourceTests
{
    /// <summary>Preselection remains detached after CAD selection changes or clears.</summary>
    [Fact]
    public async Task PreselectionIsDetachedFromLaterEditorChanges()
    {
        var document = CreateDocument();
        var entity = AddEntity(document.Database);
        document.Editor.Selection = [entity];
        var actions = new ObjectExplorerActions(null!, null!, null!, null!, null!);
        var selected = Assert.IsType<HostResult<ImmutableArray<IPlacedObjectId>>.Success>(await actions.RequestObjectsAsync(CancellationToken.None));
        document.Editor.Selection = [];

        Assert.Equal(entity, Assert.IsType<EntityId>(Assert.Single(selected.Value)).NativeId);
        Assert.Equal(0, document.Editor.PromptCount);
        Application.DocumentManager.MdiActiveDocument = null;
        Assert.IsType<HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable>(await actions.RequestObjectsAsync(CancellationToken.None));
    }

    /// <summary>Selected scope skips erased, invalid, foreign, nested, and non-entity IDs and removes duplicates.</summary>
    [Fact]
    public async Task SelectedScopeReadsOnlyLiveDirectTargets()
    {
        var document = CreateDocument();
        var database = document.Database;
        var selected = AddEntity(database);
        AddEntity(database);
        var erased = database.Add(new Entity {OwnerId = database.CurrentSpaceId, IsErased = true});
        var nested = database.Add(new Entity {OwnerId = default});
        var other = database.Add(new DBObject());
        var foreign = new Database().Add(new Entity());
        var tasks = new HostTasks();
        var source = new AutoCadDrawingInventorySource(tasks);
        var request = source.ReadAsync(
            [new EntityId(selected), new EntityId(selected), new EntityId(erased), new EntityId(nested),
                new EntityId(other), new EntityId(foreign), new EntityId(default)],
            CancellationToken.None);
        tasks.Execute();
        var inventory = Assert.IsType<HostResult<DrawingInventory>.Success>(await request).Value;

        Assert.Equal(selected, Assert.IsType<EntityId>(Assert.Single(inventory.Entities).Id).NativeId);

        var all = source.ReadAsync(null, CancellationToken.None);
        tasks.Execute();
        Assert.Equal(2, Assert.IsType<HostResult<DrawingInventory>.Success>(await all).Value.Entities.Length);
        var empty = source.ReadAsync([], CancellationToken.None);
        tasks.Execute();
        Assert.Empty(Assert.IsType<HostResult<DrawingInventory>.Success>(await empty).Value.Entities);
    }

    /// <summary>Document and space changes while host work waits cannot redirect a selected inventory read.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsChangedContextBeforeQueuedRead(bool changeDocument)
    {
        var document = CreateDocument();
        var tasks = new HostTasks();
        var source = new AutoCadDrawingInventorySource(tasks);
        var request = source.ReadAsync([], CancellationToken.None);

        if (changeDocument)
            Application.DocumentManager.MdiActiveDocument = new Document();
        else
            document.Database.CurrentSpaceId = document.Database.Add(new BlockTableRecord());

        tasks.Execute();
        Assert.IsType<HostResult<DrawingInventory>.Unavailable>(await request);
    }

    /// <summary>Native RGB values are detached while indexed and inherited assignments keep their identity.</summary>
    [Fact]
    public async Task ReadsLayerAndExplicitEntityColors()
    {
        var document = CreateDocument();
        var layer = document.Database.Objects.Values.OfType<LayerTableRecord>().Single();
        document.Database.Objects[layer.ObjectId.Value] = new LayerTableRecord
        {
            ObjectId = layer.ObjectId,
            Color = new Autodesk.AutoCAD.Colors.Color {IsByLayer = false, IsByAci = true, ColorIndex = 1}
        };
        var tasks = new HostTasks();
        var read = new AutoCadDrawingInventorySource(tasks).ReadAsync([], CancellationToken.None);
        tasks.Execute();
        var inventory = Assert.IsType<HostResult<DrawingInventory>.Success>(await read).Value;
        Assert.Equal(0xFF0000, Assert.Single(inventory.Layers).DisplayColor);

        var reader = new AutoCadEntitySnapshotReader(CancellationToken.None);
        var explicitColor = reader.Read(new Entity
        {
            Color = new Autodesk.AutoCAD.Colors.Color {IsByLayer = false, IsByAci = true, ColorIndex = 1}
        });
        Assert.Equal(0xFF0000, explicitColor.DisplayColor);
        Assert.Equal(
            new DrawingColorValue(new AssignedColor(AssignedColorKind.Index, 1)),
            explicitColor.Properties![DrawingPropertyId.Color]);
        Assert.Null(reader.Read(new Entity()).DisplayColor);
        Assert.Null(reader.Read(new Entity
        {
            Color = new Autodesk.AutoCAD.Colors.Color {IsByLayer = false, IsByBlock = true}
        }).DisplayColor);
    }

    private static Document CreateDocument()
    {
        Application.DocumentManager = new DocumentCollection();
        var document = Application.DocumentManager.MdiActiveDocument!;
        var database = document.Database;
        database.CurrentSpaceId = database.Add(new BlockTableRecord());
        var layer = database.Add(new LayerTableRecord());
        database.LayerTableId = database.Add(new LayerTable(layer));

        return document;
    }

    private static ObjectId AddEntity(Database database)
    {
        var id = database.Add(new Entity {OwnerId = database.CurrentSpaceId});
        database.Objects[database.CurrentSpaceId.Value] = new BlockTableRecord(
            [.. database.Objects.Values.OfType<Entity>().Select(entity => entity.ObjectId)])
        {
            ObjectId = database.CurrentSpaceId
        };

        return id;
    }

    private sealed class HostTasks : IHostTaskService
    {
        private Action? _pending;

        public Task<HostResult<T>> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<HostResult<T>>();
            _pending = () =>
            {
                try
                {
                    completion.SetResult(new HostResult<T>.Success(action()));
                }
                catch (Exception exception)
                {
                    completion.SetResult(new HostResult<T>.Unavailable(exception.Message));
                }
            };

            return completion.Task;
        }

        public Task StopAsync() => Task.CompletedTask;
        internal void Execute() => _pending!();
    }
}
