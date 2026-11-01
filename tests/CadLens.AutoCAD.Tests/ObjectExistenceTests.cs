using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CadLens.Common;
using CadLens.Common.AutoCAD;
using Xunit;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

/// <summary>
/// Exercises single-object liveness through the production host queue without rebuilding the drawing inventory.
/// </summary>
[Collection("AutoCAD")]
public sealed class ObjectExistenceTests
{
    /// <summary>
    /// Direct entities follow native null, validity and erased state without enumerating the current space.
    /// </summary>
    /// <param name="state">Native identifier state.</param>
    /// <param name="expected">Whether the target still exists.</param>
    [Theory]
    [InlineData("live", true)]
    [InlineData("null", false)]
    [InlineData("invalid", false)]
    [InlineData("erased", false)]
    public Task DirectIdsMatchNativeLiveness(string state, bool expected) =>
        AutoCadTaskServiceTests.OnUiThread(async () =>
        {
            var document = CreateDocument();
            var database = document.Database;
            var id = state switch
            {
                "null" => default,
                "invalid" => new ObjectId(int.MaxValue, database),
                _ => database.Add(new Entity
                {
                    OwnerId = database.CurrentSpaceId,
                    IsErased = state == "erased"
                })
            };
            using var tasks = new AutoCadTaskService();
            var request = CreateActions(tasks).ExistsAsync(new EntityId(id), CancellationToken.None);

            Assert.False(request.IsCompleted);
            await ExecuteQueuedRequest();

            Assert.Equal(expected, Assert.IsType<HostResult<bool>.Success>(await request).Value);
            Assert.Equal(0, ((BlockTableRecord) database.Objects[database.CurrentSpaceId.Value]).EnumerationCount);
            Assert.All(
                database.TransactionManager.TopTransaction.OpenRequests,
                open => Assert.Equal(OpenMode.ForRead, open.Mode));
            await tasks.StopAsync();
        });

    /// <summary>
    /// A live definition member no longer exists at an erased insertion or under an unrelated definition.
    /// </summary>
    /// <param name="eraseParent">Whether to erase the parent rather than use a different definition.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task NestedPathsRequireTheirLiveParent(bool eraseParent) =>
        AutoCadTaskServiceTests.OnUiThread(async () =>
        {
            var database = CreateDocument().Database;
            var definition = database.Add(new BlockTableRecord());
            var otherDefinition = database.Add(new BlockTableRecord());
            var root = database.Add(new BlockReference
            {
                OwnerId = database.CurrentSpaceId,
                BlockTableRecord = eraseParent ? definition : otherDefinition,
                IsErased = eraseParent
            });
            var leaf = database.Add(new Line {OwnerId = definition});
            var target = new EntityId(leaf, new EntityId(root));
            using var tasks = new AutoCadTaskService();
            var request = CreateActions(tasks).ExistsAsync(target, CancellationToken.None);

            await ExecuteQueuedRequest();

            Assert.True(leaf.IsValid);
            Assert.False(leaf.IsErased);
            Assert.False(Assert.IsType<HostResult<bool>.Success>(await request).Value);
            Assert.Equal(0, ((BlockTableRecord) database.Objects[definition.Value]).EnumerationCount);
            await tasks.StopAsync();
        });

    /// <summary>
    /// Hidden entities, invisible attributes and hidden or frozen layers still contain existing objects.
    /// </summary>
    [Fact]
    public Task HiddenOccurrencesStillExist() => AutoCadTaskServiceTests.OnUiThread(async () =>
    {
        var document = CreateDocument();
        var database = document.Database;
        var layer = database.Add(new LayerTableRecord {IsOff = true, IsFrozen = true});
        document.Editor.CurrentViewportObjectId = database.Add(new Viewport {FrozenLayers = [layer]});
        var definition = database.Add(new BlockTableRecord());
        var root = database.Add(new BlockReference
        {
            OwnerId = database.CurrentSpaceId,
            LayerId = layer,
            BlockTableRecord = definition,
            Visible = false
        });
        var attribute = database.Add(new AttributeReference
        {
            OwnerId = root,
            LayerId = layer,
            Invisible = true,
            Visible = false
        });
        using var tasks = new AutoCadTaskService();
        var target = new EntityId(attribute, new EntityId(root));
        var request = CreateActions(tasks).ExistsAsync(target, CancellationToken.None);

        await ExecuteQueuedRequest();

        Assert.True(Assert.IsType<HostResult<bool>.Success>(await request).Value);
        await tasks.StopAsync();
    });

    /// <summary>
    /// A queued existence read cannot switch to a different document or current space.
    /// </summary>
    /// <param name="changeDocument">Whether to switch document rather than space.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ChangedContextRejectsQueuedRead(bool changeDocument) =>
        AutoCadTaskServiceTests.OnUiThread(async () =>
        {
            var document = CreateDocument();
            var database = document.Database;
            var id = database.Add(new Entity {OwnerId = database.CurrentSpaceId});
            using var tasks = new AutoCadTaskService();
            var request = CreateActions(tasks).ExistsAsync(new EntityId(id), CancellationToken.None);

            if (changeDocument)
                Application.DocumentManager.MdiActiveDocument = new Document();
            else
                database.CurrentSpaceId = database.Add(new BlockTableRecord());

            await ExecuteQueuedRequest();

            Assert.IsType<HostResult<bool>.Unavailable>(await request);
            Assert.Empty(database.TransactionManager.TopTransaction.OpenRequests);
            await tasks.StopAsync();
        });

    /// <summary>
    /// Cancellation while the native callback waits is preserved without opening the target object.
    /// </summary>
    [Fact]
    public Task CancellationSkipsQueuedRead() => AutoCadTaskServiceTests.OnUiThread(async () =>
    {
        var database = CreateDocument().Database;
        var id = database.Add(new Entity {OwnerId = database.CurrentSpaceId});
        using var tasks = new AutoCadTaskService();
        using var cancellation = new CancellationTokenSource();
        var request = CreateActions(tasks).ExistsAsync(new EntityId(id), cancellation.Token);
        await Dispatcher.Yield();
        // ReSharper disable once MethodHasAsyncOverload -- Cancel before executing the queued native callback.
        cancellation.Cancel();
        Application.DocumentManager.ExecuteNext();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Empty(database.TransactionManager.TopTransaction.OpenRequests);
        await tasks.StopAsync();
    });

    /// <summary>
    /// Missing drawing context and another host's identifier are unavailable rather than deleted entities.
    /// </summary>
    /// <param name="missingDrawing">Whether to remove drawing context rather than use another host ID.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task MissingContextOrForeignTargetIsUnavailable(bool missingDrawing) =>
        AutoCadTaskServiceTests.OnUiThread(async () =>
        {
            var database = CreateDocument().Database;
            var id = database.Add(new Entity {OwnerId = database.CurrentSpaceId});
            using var tasks = new AutoCadTaskService();

            if (missingDrawing)
                Application.DocumentManager.MdiActiveDocument = null;

            IPlacedObjectId target = missingDrawing ? new EntityId(id) : new ForeignId();
            var result = await CreateActions(tasks).ExistsAsync(target, CancellationToken.None);

            Assert.IsType<HostResult<bool>.Unavailable>(result);
            Assert.Equal(0, Application.DocumentManager.PendingCount);
            await tasks.StopAsync();
        });

    private sealed record ForeignId : IPlacedObjectId
    {
        public string DisplayId => "foreign";
    }

    private static ObjectExplorerActions CreateActions(IHostTaskService tasks) =>
        new(null!, null!, null!, null!, null!, tasks);

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