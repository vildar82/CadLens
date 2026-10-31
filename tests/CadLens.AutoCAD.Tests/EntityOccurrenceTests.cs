using System.Collections.Immutable;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using CadLens.Common;
using CadLens.Common.AutoCAD;
using Xunit;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

/// <summary>
/// Exercises occurrence transforms and failure-safe temporary graphics with native API doubles.
/// </summary>
[Collection("AutoCAD")]
public sealed class EntityOccurrenceTests
{
    /// <summary>
    /// Repeated insertions own separate world-space clones; clearing never changes drawing visibility.
    /// </summary>
    [Fact]
    public void RepeatedOccurrencesCreateIndependentGraphicsAndClearIdempotently()
    {
        using var fixture = new Fixture();
        var second = fixture.AddInsertion(200);
        var count = fixture.Graphics.ApplyOccurrences(fixture.Database, [fixture.Target, second], fixture.Inventory);
        Entity[] clones = [.. fixture.Manager.Registered];

        Assert.Equal(2, count);
        Assert.Equal([100d, 200d], clones.Select(entity => entity.AppliedTransform.X).OrderBy(value => value));
        Assert.All(clones, entity => Assert.Equal(fixture.Layer, entity.LayerId));
        Assert.All(clones, entity => Assert.Equal((byte) 210, entity.Color.Red));
        Assert.Equal(Matrix3d.Identity, fixture.Source.AppliedTransform);
        Assert.True(fixture.Root.Visible);
        Assert.True(fixture.Graphics.IsApplicable(fixture.Root));
        Assert.True(fixture.Graphics.CustomFilter);
        Assert.True(fixture.Database.TransactionManager.TopTransaction.CommitCount > 0);

        fixture.Graphics.Clear(false);
        fixture.Graphics.Clear(false);

        Assert.Empty(fixture.Manager.Registered);
        Assert.All(clones, entity => Assert.True(entity.IsDisposed));
        Assert.False(fixture.Source.IsDisposed);
        Assert.False(fixture.Graphics.IsApplicable(fixture.Root));
        Assert.DoesNotContain(fixture.Graphics, DrawableOverrule.Registered);
        Assert.All(
            fixture.Database.TransactionManager.TopTransaction.OpenRequests,
            request => Assert.Equal(OpenMode.ForRead, request.Mode));
    }

    /// <summary>
    /// A failure after one native registration clears even partially registered graphics and the hiding rule.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialRegistrationFailureClearsTheEntireEffect(bool throws)
    {
        using var fixture = new Fixture();
        var second = fixture.AddInsertion(200);
        var attempted = new List<Entity>();
        fixture.Manager.OnAdd = (entity, count) =>
        {
            attempted.Add(entity);

            return count == 1 || (!throws ? false : throw new InvalidOperationException("native add failure"));
        };

        Assert.Throws<InvalidOperationException>(() =>
            fixture.Graphics.ApplyOccurrences(fixture.Database, [fixture.Target, second], fixture.Inventory));

        Assert.Equal(2, attempted.Count);
        Assert.All(attempted, entity => Assert.True(entity.IsDisposed));
        Assert.Empty(fixture.Manager.Registered);
        Assert.False(fixture.Graphics.IsApplicable(fixture.Root));
        Assert.DoesNotContain(fixture.Graphics, DrawableOverrule.Registered);
    }

    /// <summary>
    /// Picking suspends both the hiding rule and clones, then restores the same owned clones once.
    /// </summary>
    [Fact]
    public void SuspensionRestoresTransientOwnershipOnce()
    {
        using var fixture = new Fixture();
        fixture.Apply();
        var clone = Assert.Single(fixture.Manager.Registered);
        var restore = fixture.Graphics.Suspend();

        Assert.Empty(fixture.Manager.Registered);
        Assert.False(fixture.Graphics.IsApplicable(fixture.Root));
        Assert.False(clone.IsDisposed);

        restore();
        restore();

        Assert.Same(clone, Assert.Single(fixture.Manager.Registered));
        Assert.Equal(2, fixture.Manager.AddCount);
        Assert.True(fixture.Graphics.IsApplicable(fixture.Root));
    }

    /// <summary>
    /// Clearing or leaving a suspended drawing context disposes its clones and invalidates restoration.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void StaleSuspensionCannotRestoreGraphics(int change)
    {
        using var fixture = new Fixture();
        fixture.Apply();
        var clone = Assert.Single(fixture.Manager.Registered);
        var restore = fixture.Graphics.Suspend();

        switch (change)
        {
            case 0:
                fixture.Graphics.Clear(false);
                break;
            case 1:
                Application.DocumentManager.MdiActiveDocument = new Document();
                break;
            default:
                fixture.Database.CurrentSpaceId = fixture.Database.Add(new BlockTableRecord());
                break;
        }

        restore();

        Assert.True(clone.IsDisposed);
        Assert.Empty(fixture.Manager.Registered);
        Assert.DoesNotContain(fixture.Graphics, DrawableOverrule.Registered);
    }

    /// <summary>
    /// Failures while restoring nested isolation leave the original insertion visible.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedRestoreClearsNestedIsolation(bool failRedraw)
    {
        using var fixture = new Fixture();
        fixture.Apply();
        var clone = Assert.Single(fixture.Manager.Registered);
        var restore = fixture.Graphics.Suspend();

        if (failRedraw)
            fixture.Document.Drawing.OnRegen = () => throw new InvalidOperationException("redraw failure");
        else
            fixture.Manager.OnAdd = (_, _) => false;

        Assert.Throws<InvalidOperationException>(restore);

        Assert.True(clone.IsDisposed);
        Assert.Empty(fixture.Manager.Registered);
        Assert.False(fixture.Graphics.IsApplicable(fixture.Root));
        Assert.DoesNotContain(fixture.Graphics, DrawableOverrule.Registered);
    }

    /// <summary>
    /// A rejected native erase retains the drawable for retry while immediately releasing hidden originals.
    /// </summary>
    [Fact]
    public void FailedRemovalRetainsGraphicsForRetryAndNeverKeepsOriginalsHidden()
    {
        using var fixture = new Fixture();
        fixture.Apply();
        var clone = Assert.Single(fixture.Manager.Registered);
        fixture.Manager.OnErase = _ => false;

        Assert.Throws<InvalidOperationException>(() => fixture.Graphics.Clear(false));
        Assert.Throws<InvalidOperationException>(fixture.Apply);

        Assert.False(clone.IsDisposed);
        Assert.Same(clone, Assert.Single(fixture.Manager.Registered));
        Assert.False(fixture.Graphics.IsApplicable(fixture.Root));
        Assert.DoesNotContain(fixture.Graphics, DrawableOverrule.Registered);

        fixture.Manager.OnErase = null;
        fixture.Graphics.Clear(false);

        Assert.True(clone.IsDisposed);
        Assert.Empty(fixture.Manager.Registered);
    }

    /// <summary>
    /// Invisible entities, invisible attributes, and hidden ancestors cannot be resurrected by previews.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void HiddenOccurrencesNeverCreateVisibleClones(int hidden)
    {
        using var fixture = new Fixture(
            (definition, zero, root) => hidden switch
            {
                2 => new AttributeReference {OwnerId = root, LayerId = zero, Invisible = true},
                3 => new AttributeDefinition {OwnerId = definition, LayerId = zero, Constant = true, Invisible = true},
                _ => new Line {OwnerId = definition, LayerId = zero, Visible = hidden != 4}
            },
            hiddenRoot: hidden == 0,
            hiddenLayer: hidden == 1);

        if (hidden == 5)
            fixture.Document.Editor.CurrentViewportObjectId = fixture.Database.Add(
                new Viewport {FrozenLayers = [fixture.Layer]});

        fixture.Apply();

        Assert.Empty(fixture.Manager.Registered);
        Assert.Null(fixture.Source.LastTransformedCopy);
    }

    /// <summary>
    /// Unsupported custom types, clipping, and layout viewports fail without modifying the drawing.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnsupportedPreviewsFailWithoutHidingOriginals(int unsupported)
    {
        using var fixture = new Fixture(
            (definition, zero, _) => unsupported == 0
                ? new CustomText {OwnerId = definition, LayerId = zero}
                : new Line {OwnerId = definition, LayerId = zero},
            clipped: unsupported == 1);
        fixture.Database.TileMode = unsupported != 2;

        Assert.Throws<NotSupportedException>(fixture.Apply);

        Assert.Null(fixture.Source.LastTransformedCopy);
        Assert.Empty(fixture.Manager.Registered);
        Assert.False(fixture.Graphics.IsApplicable(fixture.Root));
        Assert.True(fixture.Root.Visible);
    }

    /// <summary>
    /// Owned polyline vertices are never transformed through a shallow header clone.
    /// </summary>
    [Fact]
    public void LegacyPolylineUsesIndependentExplodedGeometry()
    {
        var part = new Line();
        using var fixture = new Fixture((definition, zero, _) => new Polyline2d
        {
            OwnerId = definition,
            LayerId = zero,
            TransformedCopy = _ => throw new InvalidOperationException("Unsafe header clone"),
            OnExplode = parts => parts.Items.Add(part)
        });

        fixture.Apply();

        Assert.Null(fixture.Source.LastTransformedCopy);
        Assert.Equal(Matrix3d.Identity, fixture.Source.AppliedTransform);
        Assert.True(part.IsDisposed);
        Assert.Equal(100, Assert.Single(fixture.Manager.Registered).AppliedTransform.X);
    }

    /// <summary>
    /// Dimensions own a generated block, so previews use exploded geometry instead of a shallow clone.
    /// </summary>
    [Fact]
    public void DimensionUsesIndependentExplodedGeometry()
    {
        var part = new Solid();
        using var fixture = new Fixture((definition, zero, _) => new AlignedDimension
        {
            OwnerId = definition,
            LayerId = zero,
            TransformedCopy = _ => throw new InvalidOperationException("Unsafe header clone"),
            OnExplode = parts => parts.Items.Add(part)
        });

        fixture.Apply();

        Assert.True(part.IsDisposed);
        Assert.Equal(100, Assert.Single(fixture.Manager.Registered).AppliedTransform.X);
    }

    /// <summary>
    /// Points, 2D solids and 3D faces own no children, so they preview through an ordinary transformed copy.
    /// </summary>
    /// <param name="kind">Simple entity kind found inside a block.</param>
    [Theory]
    [InlineData("point")]
    [InlineData("solid")]
    [InlineData("face")]
    public void SimpleEntitiesPreviewInsideBlocks(string kind)
    {
        using var fixture = new Fixture((definition, zero, _) => kind switch
        {
            "point" => new DBPoint {OwnerId = definition, LayerId = zero},
            "solid" => new Solid {OwnerId = definition, LayerId = zero},
            _ => new Face {OwnerId = definition, LayerId = zero}
        });

        fixture.Apply();

        Assert.Equal(fixture.Source.GetType(), Assert.Single(fixture.Manager.Registered).GetType());
    }

    /// <summary>
    /// A rotated insertion bounds all eight transformed corners, not only the original minimum and maximum.
    /// </summary>
    [Fact]
    public void BoundsCoverEveryCornerOfRotatedInsertion()
    {
        using var fixture = new Fixture();
        var definition = fixture.Database.Add(new BlockTableRecord());
        var rotated = fixture.Database.Add(
            new BlockReference
            {
                OwnerId = fixture.Root.BlockTableRecord,
                LayerId = fixture.Layer,
                BlockTableRecord = definition,
                BlockTransform = Matrix3d.RotationAboutZ(Math.PI / 4)
            });
        var leaf = fixture.Database.Add(
            new Line
            {
                OwnerId = definition,
                LayerId = fixture.Layer,
                GeometricExtents = new Extents3d(new Point3d(0, 0, 0), new Point3d(2, 1, 0))
            });
        var target = new EntityId(leaf, new EntityId(rotated, fixture.Target.Container));

        var bounds = EntityOccurrence.Resolve(fixture.Database, target)!.ReadBounds()!.Value;

        var halfDiagonal = Math.Sqrt(0.5);
        Assert.Equal(100 - halfDiagonal, bounds.MinPoint.X, 6);
        Assert.Equal(100 + 2 * halfDiagonal, bounds.MaxPoint.X, 6);
        Assert.Equal(0, bounds.MinPoint.Y, 6);
        Assert.Equal(3 * halfDiagonal, bounds.MaxPoint.Y, 6);
    }

    /// <summary>
    /// A block's bounds ignore members on frozen layers and hidden dynamic states, as Zoom Extents does.
    /// </summary>
    [Fact]
    public async Task FocusIgnoresUndisplayedBlockMembers()
    {
        using var fixture = new Fixture();
        var frozen = fixture.Database.Add(new LayerTableRecord {Name = "Frozen", IsFrozen = true});
        var far = new Extents3d(new Point3d(-400, -400, 0), new Point3d(400, 400, 0));
        var shown = fixture.Database.Add(
            new Line {GeometricExtents = new Extents3d(new Point3d(0, 0, 0), new Point3d(2, 5, 0))});
        var onFrozenLayer = fixture.Database.Add(new Arc {LayerId = frozen, GeometricExtents = far});
        var hiddenState = fixture.Database.Add(new Line {Visible = false, GeometricExtents = far});
        var definition = fixture.Database.Add(new BlockTableRecord(shown, onFrozenLayer, hiddenState));
        var block = fixture.Database.Add(
            new BlockReference
            {
                OwnerId = fixture.Database.CurrentSpaceId,
                LayerId = fixture.Layer,
                BlockTableRecord = definition,
                BlockTransform = Matrix3d.Displacement(new Vector3d(50, 0, 0)),
                GeometricExtents = far
            });

        await FocusAsync(new EntityId(block));

        Assert.Equal(new Point3d(50, 0, 0), fixture.Document.Editor.ZoomBounds!.Value.MinPoint);
        Assert.Equal(new Point3d(52, 5, 0), fixture.Document.Editor.ZoomBounds.Value.MaxPoint);
    }

    /// <summary>
    /// An empty box at the origin cannot stretch Focus, while a real point keeps its position.
    /// </summary>
    [Fact]
    public async Task FocusSkipsEmptyBoundsExceptPoints()
    {
        using var fixture = new Fixture();
        var space = fixture.Database.CurrentSpaceId;
        var origin = new Extents3d(Point3d.Origin, Point3d.Origin);
        var empty = fixture.Database.Add(new Line {OwnerId = space, GeometricExtents = origin});
        var location = new Point3d(100, 50, 0);
        var point = fixture.Database.Add(
            new DBPoint {OwnerId = space, GeometricExtents = new Extents3d(location, location)});

        await FocusAsync(new EntityId(empty), new EntityId(point));

        Assert.Equal(location, fixture.Document.Editor.ZoomBounds!.Value.MinPoint);
        Assert.Equal(location, fixture.Document.Editor.ZoomBounds.Value.MaxPoint);
    }

    /// <summary>
    /// Objects reporting more than they draw stop at the drawing extents; targets outside them keep their bounds.
    /// </summary>
    /// <param name="overlaps">Whether the drawing extents overlap the target.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FocusClipsToDrawingExtents(bool overlaps)
    {
        using var fixture = new Fixture();
        var surface = fixture.Database.Add(
            new Line
            {
                OwnerId = fixture.Database.CurrentSpaceId,
                GeometricExtents = new Extents3d(new Point3d(-1000, -1000, 0), new Point3d(1000, 1000, 0))
            });
        var offset = overlaps ? 0 : 5000;
        fixture.Database.Extmin = new Point3d(offset, offset, 0);
        fixture.Database.Extmax = new Point3d(offset + 10, offset + 20, 0);

        await FocusAsync(new EntityId(surface));

        var bounds = fixture.Document.Editor.ZoomBounds!.Value;
        Assert.Equal(overlaps ? new Point3d(0, 0, 0) : new Point3d(-1000, -1000, 0), bounds.MinPoint);
        Assert.Equal(overlaps ? new Point3d(10, 20, 0) : new Point3d(1000, 1000, 0), bounds.MaxPoint);
    }

    /// <summary>
    /// Nested inserts accumulate transforms, while attached attributes already use the insertion's parent coordinates.
    /// </summary>
    [Fact]
    public void BoundsValidateOccurrencePathsAndAttributeCoordinates()
    {
        using var fixture = new Fixture();
        var nestedDefinition = fixture.Database.Add(new BlockTableRecord());
        var nested = fixture.Database.Add(
            new BlockReference
            {
                OwnerId = fixture.Root.BlockTableRecord,
                LayerId = fixture.Layer,
                BlockTableRecord = nestedDefinition,
                BlockTransform = Matrix3d.Scaling(2, Point3d.Origin)
            });
        var leaf = fixture.Database.Add(
            new Line
            {
                OwnerId = nestedDefinition,
                LayerId = fixture.Layer,
                GeometricExtents = new Extents3d(new Point3d(1, 2, 0), new Point3d(2, 3, 0))
            });
        var attribute = fixture.Database.Add(
            new AttributeReference
            {
                OwnerId = nested,
                LayerId = fixture.Layer,
                GeometricExtents = new Extents3d(new Point3d(5, 0, 0), new Point3d(6, 1, 0))
            });
        var container = new EntityId(nested, fixture.Target.Container);
        var leafOccurrence = EntityOccurrence.Resolve(fixture.Database, new EntityId(leaf, container))!;
        var attributeOccurrence = EntityOccurrence.Resolve(fixture.Database, new EntityId(attribute, container))!;
        var leafBounds = leafOccurrence.ReadBounds()!.Value;
        var attributeBounds = attributeOccurrence.ReadBounds()!.Value;

        Assert.Equal(new Point3d(102, 4, 0), leafBounds.MinPoint);
        Assert.Equal(new Point3d(104, 6, 0), leafBounds.MaxPoint);
        Assert.Equal(new Point3d(105, 0, 0), attributeBounds.MinPoint);
        Assert.Null(EntityOccurrence.Resolve(fixture.Database, new EntityId(leaf, fixture.Target.Container)));
    }

    /// <summary>
    /// Selection maps to unique root inserts; isolation preserves paths and Focus uses the inner bounds.
    /// </summary>
    [Fact]
    public async Task VisualizationMapsNativeActionsToOccurrences()
    {
        using var fixture = new Fixture();
        var isolation = new CapturedIsolation();
        var service = new AutoCadObjectVisualizationService(new ImmediateTasks(), isolation);
        ImmutableArray<IPlacedObjectId> targets = [fixture.Target, fixture.Target];

        Assert.IsType<HostResult<bool>.Success>(await service.SelectAsync(targets, CancellationToken.None));
        Assert.Equal([fixture.Root.ObjectId], fixture.Document.Editor.Selection);
        Assert.IsType<HostResult<bool>.Success>(await service.IsolateAsync(targets, CancellationToken.None));
        Assert.Equal([fixture.Target, fixture.Target], isolation.Targets);
        Assert.IsType<HostResult<bool>.Success>(await service.FocusAsync(targets, CancellationToken.None));
        Assert.Equal(new Point3d(100, 0, 0), fixture.Document.Editor.ZoomBounds!.Value.MinPoint);
        Assert.Equal(new Point3d(101, 1, 0), fixture.Document.Editor.ZoomBounds.Value.MaxPoint);
    }

    /// <summary>
    /// Cancelling native cloning, display or regeneration removes partial graphics and hiding rules.
    /// </summary>
    /// <param name="boundary">Native boundary at which cancellation is requested.</param>
    [Theory]
    [InlineData("creating")]
    [InlineData("displaying")]
    [InlineData("regenerating")]
    public void CancellationRollsBackPartialOccurrenceGraphics(string boundary)
    {
        using var cancellation = new CancellationTokenSource();
        Action cancel = cancellation.Cancel;
        var clones = new List<Entity>();
        using var fixture = new Fixture((definition, layer, _) => new Line
        {
            OwnerId = definition,
            LayerId = layer,
            TransformedCopy = _ =>
            {
                var clone = new Line();
                clones.Add(clone);

                if (boundary == "creating" && clones.Count == 2)
                    cancel();

                return clone;
            }
        });
        var second = fixture.AddInsertion(200);

        switch (boundary)
        {
            case "displaying":
                fixture.Manager.OnAdd = (_, _) =>
                {
                    cancel();
                    return true;
                };
                break;
            case "regenerating":
                fixture.Document.Drawing.OnRegen = cancel;
                break;
        }

        Assert.ThrowsAny<OperationCanceledException>(() => fixture.Graphics.ApplyOccurrences(
            fixture.Database,
            [fixture.Target, second],
            fixture.Inventory,
            cancellationToken: cancellation.Token));

        Assert.Equal(2, clones.Count);
        Assert.All(clones, clone => Assert.True(clone.IsDisposed));
        Assert.Empty(fixture.Manager.Registered);
        Assert.False(fixture.Graphics.IsApplicable(fixture.Root));
        Assert.DoesNotContain(fixture.Graphics, DrawableOverrule.Registered);
        Assert.False(fixture.Source.IsDisposed);
    }

    /// <summary>
    /// Cancellation immediately after native explode still releases every temporary exploded object.
    /// </summary>
    [Fact]
    public void CancellationDisposesExplodedParts()
    {
        using var cancellation = new CancellationTokenSource();
        Action cancel = cancellation.Cancel;
        var first = new Line();
        var second = new Line();
        using var fixture = new Fixture((definition, layer, _) => new Polyline2d
        {
            OwnerId = definition,
            LayerId = layer,
            OnExplode = parts =>
            {
                parts.Items.Add(first);
                parts.Items.Add(second);
                cancel();
            }
        });

        Assert.ThrowsAny<OperationCanceledException>(() => fixture.Graphics.ApplyOccurrences(
            fixture.Database,
            [fixture.Target],
            fixture.Inventory,
            cancellationToken: cancellation.Token));

        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.Empty(fixture.Manager.Registered);
        Assert.DoesNotContain(fixture.Graphics, DrawableOverrule.Registered);
    }

    /// <summary>
    /// Native removal failure during cancellation keeps the displayed wrapper alive until cleanup succeeds.
    /// </summary>
    [Fact]
    public void CancelledDisplayRetainsFailedRemovalForRetry()
    {
        using var cancellation = new CancellationTokenSource();
        Action cancel = cancellation.Cancel;
        using var fixture = new Fixture();
        fixture.Manager.OnAdd = (_, _) =>
        {
            cancel();
            return true;
        };
        fixture.Manager.OnErase = _ => false;

        Assert.ThrowsAny<OperationCanceledException>(() => fixture.Graphics.ApplyOccurrences(
            fixture.Database,
            [fixture.Target],
            fixture.Inventory,
            cancellationToken: cancellation.Token));

        var clone = Assert.Single(fixture.Manager.Registered);
        Assert.False(clone.IsDisposed);
        Assert.False(fixture.Graphics.IsApplicable(fixture.Root));
        Assert.DoesNotContain(fixture.Graphics, DrawableOverrule.Registered);

        fixture.Manager.OnErase = null;
        fixture.Graphics.Clear(false);

        Assert.True(clone.IsDisposed);
        Assert.Empty(fixture.Manager.Registered);
    }

    /// <summary>
    /// Large occurrence sets report bounded stage updates instead of one notification per native object.
    /// </summary>
    [Fact]
    public void OccurrenceProgressIsThrottledAndReportsNativeStages()
    {
        using var fixture = new Fixture();
        var targets = new List<EntityId> {fixture.Target};

        for (var index = 1; index < 130; index++)
            targets.Add(fixture.AddInsertion(index * 10));

        var updates = new List<OperationProgress>();
        var count = fixture.Graphics.ApplyOccurrences(
            fixture.Database,
            [.. targets],
            fixture.Inventory,
            progress: new CapturedProgress(updates.Add));

        Assert.Equal(130, count);
        Assert.Equal([0, 64, 128, 130], updates.Where(update =>
            update.Stage == OperationStage.CreatingGraphics).Select(update => update.Completed));
        Assert.Equal([0, 64, 128, 130], updates.Where(update =>
            update.Stage == OperationStage.DisplayingGraphics).Select(update => update.Completed));
        Assert.Contains(updates, update => update.Stage == OperationStage.Isolating);
        Assert.Contains(updates, update => update.Stage == OperationStage.Regenerating);
    }

    /// <summary>
    /// Production selection, focus and isolation forward their native stage progress to the same observer.
    /// </summary>
    [Fact]
    public async Task VisualizationReportsSelectionFocusAndIsolationStages()
    {
        using var fixture = new Fixture();
        fixture.Database.Objects[fixture.Database.CurrentSpaceId.Value] =
            new BlockTableRecord(fixture.Inventory) {ObjectId = fixture.Database.CurrentSpaceId};
        var tasks = new ImmediateTasks();
        var service = new AutoCadObjectVisualizationService(
            tasks,
            new EntityIsolationActions(tasks, fixture.Graphics));
        var updates = new List<OperationProgress>();
        var progress = new CapturedProgress(updates.Add);

        var selection = await service.SelectAsync([fixture.Target], CancellationToken.None, progress);
        Assert.IsType<HostResult<bool>.Success>(selection);
        Assert.Contains(updates, update => update.Stage == OperationStage.Selecting);
        updates.Clear();

        var focused = await service.FocusAsync([fixture.Target], CancellationToken.None, progress);
        Assert.IsType<HostResult<bool>.Success>(focused);
        Assert.Contains(updates, update => update.Stage == OperationStage.Reading);
        Assert.Contains(updates, update => update.Stage == OperationStage.Focusing);
        updates.Clear();

        var isolated = await service.IsolateAsync([fixture.Target], CancellationToken.None, progress);
        Assert.IsType<HostResult<bool>.Success>(isolated);
        Assert.Contains(updates, update => update.Stage == OperationStage.Isolating);
        Assert.Contains(updates, update => update.Stage == OperationStage.CreatingGraphics);
        Assert.Contains(updates, update => update.Stage == OperationStage.DisplayingGraphics);
        Assert.Contains(updates, update => update.Stage == OperationStage.Regenerating);
    }

    /// <summary>
    /// Cancelling during target or bounds reading prevents the final native selection or view change.
    /// </summary>
    /// <param name="focus">Whether to cancel Focus rather than selection.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationPreventsFinalNativeVisualization(bool focus)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        Action cancel = cancellation.Cancel;
        var service = new AutoCadObjectVisualizationService(new ImmediateTasks(), new CapturedIsolation());
        var progress = new CapturedProgress(update =>
        {
            if (update.Completed > 0 && update.Stage == (focus ? OperationStage.Reading : OperationStage.Selecting))
                cancel();
        });
        var request = focus
            ? service.FocusAsync([fixture.Target], cancellation.Token, progress)
            : service.SelectAsync([fixture.Target], cancellation.Token, progress);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Null(fixture.Document.Editor.ZoomBounds);
        Assert.Empty(fixture.Document.Editor.Selection);
    }

    private sealed class CapturedProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }

    private sealed class CustomText : DBText;

    private sealed class ImmediateTasks : IHostTaskService
    {
        public Task<HostResult<T>> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<HostResult<T>>(new HostResult<T>.Success(action()));
        }

        public Task StopAsync() => Task.CompletedTask;
    }

    private sealed class CapturedIsolation : IEntityIsolationActions
    {
        internal EntityId[] Targets { get; private set; } = [];

        public Task<HostResult<int>> IsolateAsync(
            EntityId[] objects,
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Targets = objects;
            return Task.FromResult<HostResult<int>>(new HostResult<int>.Success(objects.Length));
        }

        public Task<HostResult<bool>> ClearAsync(
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
        }
    }

    private static async Task FocusAsync(params EntityId[] targets)
    {
        var service = new AutoCadObjectVisualizationService(new ImmediateTasks(), new CapturedIsolation());
        var result = await service.FocusAsync([.. targets], CancellationToken.None);

        Assert.IsType<HostResult<bool>.Success>(result);
    }

    private sealed class Fixture : IDisposable
    {
        internal Fixture(
            Func<ObjectId, ObjectId, ObjectId, Entity>? sourceFactory = null,
            bool hiddenRoot = false,
            bool hiddenLayer = false,
            bool clipped = false)
        {
            Application.DocumentManager = new DocumentCollection();
            Document = Application.DocumentManager.MdiActiveDocument!;
            Database.CurrentSpaceId = Database.Add(new BlockTableRecord());
            var zero = Database.Add(new LayerTableRecord {Name = "0"});
            Layer = Database.Add(
                new LayerTableRecord
                {
                    Name = "Members",
                    IsOff = hiddenLayer,
                    Color = new Color {IsByLayer = false, IsByColor = true, Red = 210}
                });
            var definition = Database.Add(new BlockTableRecord());
            Root = new BlockReference
            {
                OwnerId = Database.CurrentSpaceId,
                LayerId = Layer,
                BlockTableRecord = definition,
                BlockTransform = Matrix3d.Displacement(new Vector3d(100, 0, 0)),
                Visible = !hiddenRoot,
                ExtensionDictionary = clipped ? Database.Add(new DBDictionary("ACAD_FILTER")) : default
            };
            Database.Add(Root);
            Source = sourceFactory?.Invoke(definition, zero, Root.ObjectId) ?? new Line
            {
                OwnerId = definition,
                LayerId = zero,
                Color = new Color {IsByLayer = false, IsByBlock = true}
            };
            Target = new EntityId(Database.Add(Source), new EntityId(Root.ObjectId));
            Inventory = [Root.ObjectId];
            TransientManager.CurrentTransientManager = Manager;
        }

        internal Document Document { get; }
        internal Database Database => Document.Database;
        internal EntityIsolationService Graphics { get; } = new();
        internal TransientManager Manager { get; } = new();
        internal BlockReference Root { get; }
        internal Entity Source { get; }
        internal ObjectId Layer { get; }
        internal EntityId Target { get; }
        internal ObjectId[] Inventory { get; private set; }

        internal EntityId AddInsertion(double x)
        {
            var id = Database.Add(
                new BlockReference
                {
                    OwnerId = Database.CurrentSpaceId,
                    LayerId = Layer,
                    BlockTableRecord = Root.BlockTableRecord,
                    BlockTransform = Matrix3d.Displacement(new Vector3d(x, 0, 0))
                });
            Inventory = [.. Inventory, id];
            return new EntityId(Source.ObjectId, new EntityId(id));
        }

        internal void Apply() => Graphics.ApplyOccurrences(Database, [Target], Inventory);

        public void Dispose()
        {
            Document.Drawing.OnRegen = null;
            Manager.OnErase = null;
            Graphics.Clear(false);
        }
    }
}