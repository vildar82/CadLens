using System.Collections.Immutable;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
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
    /// <summary>
    /// Automatic reads stop above the limit; explicit full and selected reads remain available.
    /// </summary>
    /// <param name="limit">Automatic loading threshold.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AutomaticReadHonorsThreshold(int limit)
    {
        var document = CreateDocument();
        var selected = AddEntity(document.Database);
        AddEntity(document.Database);
        var tasks = new HostTasks();
        var source = new AutoCadDrawingInventorySource(tasks);
        var automatic = source.ReadAsync(null, CancellationToken.None, limit);
        await tasks.Execute(automatic);

        if (limit < 2)
            Assert.IsType<HostResult<DrawingInventory>.Unavailable>(await automatic);
        else
            Assert.Equal(2, Assert.IsType<HostResult<DrawingInventory>.Success>(await automatic).Value.Entities.Length);

        var explicitRead = source.ReadAsync(null, CancellationToken.None);
        await tasks.Execute(explicitRead);
        Assert.Equal(2, Assert.IsType<HostResult<DrawingInventory>.Success>(await explicitRead).Value.Entities.Length);

        var selection = source.ReadAsync([new EntityId(selected)], CancellationToken.None, 0);
        await tasks.Execute(selection);
        Assert.Single(Assert.IsType<HostResult<DrawingInventory>.Success>(await selection).Value.Entities);
    }

    /// <summary>Preselection remains detached after CAD selection changes or clears.</summary>
    [Fact]
    public async Task PreselectionIsDetachedFromLaterEditorChanges()
    {
        var document = CreateDocument();
        var entity = AddEntity(document.Database);
        document.Editor.Selection = [entity];
        var actions = new ObjectExplorerActions(null!, null!, null!, null!, null!, null!);
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
        await tasks.Execute(request);
        var inventory = Assert.IsType<HostResult<DrawingInventory>.Success>(await request).Value;

        Assert.Equal(selected, Assert.IsType<EntityId>(Assert.Single(inventory.Entities).Id).NativeId);

        var all = source.ReadAsync(null, CancellationToken.None);
        await tasks.Execute(all);
        Assert.Equal(2, Assert.IsType<HostResult<DrawingInventory>.Success>(await all).Value.Entities.Length);
        var empty = source.ReadAsync([], CancellationToken.None);
        await tasks.Execute(empty);
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

        await tasks.Execute(request);
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
        await tasks.Execute(read);
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

    /// <summary>Opt-in flattening preserves independent nested placements and effective layers.</summary>
    [Fact]
    public async Task BlockContentsKeepEveryOccurrenceAndInheritLayerZero()
    {
        var document = CreateDocument();
        var database = document.Database;
        var zero = AddLayer(database, "0");
        var outerLayer = AddLayer(database, "Outer");
        var ownLayer = AddLayer(database, "Own");
        var definition = database.Add(new BlockTableRecord());
        var inherited = database.Add(new Line
        {
            OwnerId = definition, LayerId = zero, Length = 3,
            Color = new Autodesk.AutoCAD.Colors.Color {IsByLayer = false, IsByBlock = true}
        });
        var own = database.Add(new Line {OwnerId = definition, LayerId = ownLayer, Length = 5});
        SetContents(database, definition, inherited, own);
        var outerDefinition = database.Add(new BlockTableRecord());
        var nested = database.Add(new BlockReference
        {
            OwnerId = outerDefinition, LayerId = zero, BlockTableRecord = definition,
            Color = new Autodesk.AutoCAD.Colors.Color {IsByLayer = false, IsByBlock = true}
        });
        SetContents(database, outerDefinition, nested);
        var first = AddRoot(database, new BlockReference
        {
            OwnerId = database.CurrentSpaceId, LayerId = outerLayer, BlockTableRecord = outerDefinition,
            Color = new Autodesk.AutoCAD.Colors.Color {IsByLayer = false, IsByAci = true, ColorIndex = 1}
        });
        var second = AddRoot(database, new BlockReference
        {
            OwnerId = database.CurrentSpaceId, LayerId = ownLayer, BlockTableRecord = outerDefinition
        });

        var original = await ReadInventory(false);
        var flattened = await ReadInventory(true);

        Assert.Equal([new EntityId(first), new EntityId(second)], original.Entities.Select(entity => entity.Id));
        Assert.All(original.Entities, entity => Assert.True(entity.CanBrowseContents));
        Assert.All(flattened.Entities, entity => Assert.False(entity.CanBrowseContents));
        Assert.Equal(4, flattened.Entities.Length);
        Assert.Equal(
            [outerLayer, ownLayer, ownLayer, ownLayer],
            flattened.Entities.Select(entity => Assert.IsType<LayerId>(entity.LayerId).NativeId));
        var firstLeaf = new EntityId(inherited, new EntityId(nested, new EntityId(first)));
        var secondLeaf = new EntityId(inherited, new EntityId(nested, new EntityId(second)));
        Assert.Equal(firstLeaf, flattened.Entities[0].Id);
        Assert.Equal(secondLeaf, flattened.Entities[2].Id);
        Assert.Equal(0xFF0000, flattened.Entities[0].DisplayColor);
        Assert.Equal(
            [outerLayer, outerLayer],
            flattened.Entities[0].ContainerLayers.Cast<LayerId>().Select(layer => layer.NativeId));
        Assert.NotEqual(firstLeaf, secondLeaf);
        Assert.Equal($"{first.Handle}/{nested.Handle}/{inherited.Handle}", firstLeaf.DisplayId);
        Assert.Equal(first, firstLeaf.RootId);

        var selected = await ReadInventory(true, [new EntityId(first)]);

        Assert.Equal(
            [firstLeaf, new EntityId(own, firstLeaf.Container)],
            selected.Entities.Select(entity => entity.Id));
    }

    /// <summary>Selected inserts expand only inside their captured roots and ignore automatic loading limits.</summary>
    [Fact]
    public async Task SelectedBlockContentsUseCapturedRootsAndSkipErasedMembers()
    {
        var document = CreateDocument();
        var database = document.Database;
        var definition = database.Add(new BlockTableRecord());
        var live = database.Add(new Line {OwnerId = definition});
        var erased = database.Add(new Line {OwnerId = definition, IsErased = true});
        SetContents(database, definition, live, erased, default);
        var selected = AddRoot(database, new BlockReference
        {
            OwnerId = database.CurrentSpaceId, BlockTableRecord = definition
        });
        AddRoot(database, new BlockReference {OwnerId = database.CurrentSpaceId, BlockTableRecord = definition});

        var inventory = await ReadInventory(true, [new EntityId(selected), new EntityId(selected)], 0);

        Assert.Equal(new EntityId(live, new EntityId(selected)), Assert.Single(inventory.Entities).Id);
    }

    /// <summary>Dynamic inserts enumerate their evaluated record rather than the authoring definition.</summary>
    [Fact]
    public async Task DynamicBlocksReadTheirEvaluatedDefinition()
    {
        var document = CreateDocument();
        var database = document.Database;
        var original = database.Add(new BlockTableRecord());
        var evaluated = database.Add(new BlockTableRecord());
        SetContents(database, original, database.Add(new Line {OwnerId = original}));
        var visible = database.Add(new Circle {OwnerId = evaluated});
        var hidden = database.Add(new Line {OwnerId = evaluated, Visible = false});
        var nestedDefinition = database.Add(new BlockTableRecord());
        SetContents(database, nestedDefinition, database.Add(new Line {OwnerId = nestedDefinition}));
        var hiddenNested = database.Add(new BlockReference
        {
            OwnerId = evaluated, BlockTableRecord = nestedDefinition, Visible = false
        });
        SetContents(database, evaluated, visible, hidden, hiddenNested);
        var block = AddRoot(database, new BlockReference
        {
            OwnerId = database.CurrentSpaceId,
            IsDynamicBlock = true,
            DynamicBlockTableRecord = original,
            BlockTableRecord = evaluated
        });
        AddRoot(database, new BlockReference
        {
            OwnerId = database.CurrentSpaceId, BlockTableRecord = evaluated, Visible = false
        });

        var inventory = await ReadInventory(true);

        Assert.Equal(new EntityId(visible, new EntityId(block)), Assert.Single(inventory.Entities).Id);
        Assert.Equal(2, (await ReadInventory(false)).Entities.Length);
    }

    /// <summary>A recursive definition terminates locally without suppressing the next placement.</summary>
    [Fact]
    public async Task RecursiveDefinitionsDoNotSuppressOtherOccurrences()
    {
        var document = CreateDocument();
        var database = document.Database;
        var definition = database.Add(new BlockTableRecord());
        var recursive = database.Add(new BlockReference {OwnerId = definition, BlockTableRecord = definition});
        var leaf = database.Add(new Line {OwnerId = definition});
        SetContents(database, definition, recursive, leaf);
        var first = AddRoot(database, new BlockReference
        {
            OwnerId = database.CurrentSpaceId, BlockTableRecord = definition
        });
        var second = AddRoot(database, new BlockReference
        {
            OwnerId = database.CurrentSpaceId, BlockTableRecord = definition
        });

        var inventory = await ReadInventory(true);

        Assert.All(inventory.Entities, entity => Assert.False(entity.CanBrowseContents));
        Assert.Equal(
            [new EntityId(recursive, new EntityId(first)), new EntityId(leaf, new EntityId(first)),
                new EntityId(recursive, new EntityId(second)), new EntityId(leaf, new EntityId(second))],
            inventory.Entities.Select(entity => entity.Id));
    }

    /// <summary>External references and insertion arrays retain their atomic representation.</summary>
    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public async Task UnsupportedInsertionsRemainAtomic(bool external, bool overlay, bool array, bool clipped)
    {
        var document = CreateDocument();
        var database = document.Database;
        var definition = database.Add(new BlockTableRecord());
        var leaf = database.Add(new Line {OwnerId = definition});
        database.Objects[definition.Value] = new BlockTableRecord(leaf)
        {
            ObjectId = definition, IsFromExternalReference = external, IsFromOverlayReference = overlay
        };
        var dictionary = clipped ? database.Add(new DBDictionary("ACAD_FILTER")) : default;
        var block = array
            ? new MInsertBlock {OwnerId = database.CurrentSpaceId, BlockTableRecord = definition}
            : new BlockReference
            {
                OwnerId = database.CurrentSpaceId, BlockTableRecord = definition, ExtensionDictionary = dictionary
            };
        var root = AddRoot(database, block);

        var inventory = await ReadInventory(true);

        Assert.Equal(new EntityId(root), Assert.Single(inventory.Entities).Id);
        Assert.False(Assert.Single(inventory.Entities).CanBrowseContents);
        Assert.False(Assert.Single((await ReadInventory(false)).Entities).CanBrowseContents);
    }

    /// <summary>Automatic expansion stops during traversal; explicit refresh can still read the full block.</summary>
    [Fact]
    public async Task AutomaticLimitStopsInsideOneLargeBlock()
    {
        var document = CreateDocument();
        var database = document.Database;
        var definition = database.Add(new BlockTableRecord());
        var measured = 0;
        var children = Enumerable.Range(0, 5).Select(_ => database.Add(new Line
        {
            OwnerId = definition, ReadArea = () => ++measured
        })).ToArray();
        SetContents(database, definition, children);
        AddRoot(database, new BlockReference {OwnerId = database.CurrentSpaceId, BlockTableRecord = definition});
        var tasks = new HostTasks();
        var request = new AutoCadDrawingInventorySource(tasks).ReadAsync(null, CancellationToken.None, 2, true);
        await tasks.Execute(request);

        Assert.IsType<HostResult<DrawingInventory>.Unavailable>(await request);
        Assert.Equal(0, measured);
        Assert.Equal(5, (await ReadInventory(true)).Entities.Length);
    }

    /// <summary>Cancellation interrupts block expansion before another definition member is measured.</summary>
    [Fact]
    public async Task CancellationStopsNestedExpansion()
    {
        var database = CreateDocument().Database;
        var definition = database.Add(new BlockTableRecord());
        using var cancellation = new CancellationTokenSource();
        var measured = 0;
        var first = database.Add(new Line
        {
            OwnerId = definition,
            ReadArea = () =>
            {
                measured++;
                // ReSharper disable once AccessToDisposedClosure -- The awaited read finishes before source disposal.
                cancellation.Cancel();
                return 1;
            }
        });
        var second = database.Add(new Line {OwnerId = definition, ReadArea = () => ++measured});
        SetContents(database, definition, first, second);
        AddRoot(database, new BlockReference {OwnerId = database.CurrentSpaceId, BlockTableRecord = definition});
        var tasks = new HostTasks();
        var request = new AutoCadDrawingInventorySource(tasks).ReadAsync(null, cancellation.Token, null, true);

        await tasks.Execute(request);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Equal(1, measured);
    }

    /// <summary>Placed measurements use composed transforms and release their temporary native copies.</summary>
    [Fact]
    public async Task NestedMeasurementsUseComposedTransformsAndDisposeCopies()
    {
        var document = CreateDocument();
        var database = document.Database;
        var definition = database.Add(new BlockTableRecord());
        Matrix3d? observed = null;
        var line = new Line
        {
            OwnerId = definition,
            Length = 2,
            TransformedCopy = transform =>
            {
                observed = transform;
                return new Line {Length = 12, ReadArea = () => 36};
            }
        };
        SetContents(database, definition, database.Add(line));
        var parent = database.Add(new BlockTableRecord());
        SetContents(database, parent, database.Add(new BlockReference
        {
            OwnerId = parent, BlockTableRecord = definition,
            BlockTransform = Matrix3d.Scaling(3, Point3d.Origin)
        }));
        AddRoot(database, new BlockReference
        {
            OwnerId = database.CurrentSpaceId, BlockTableRecord = parent,
            BlockTransform = Matrix3d.Scaling(2, Point3d.Origin)
        });

        var snapshot = Assert.Single((await ReadInventory(true)).Entities);

        Assert.Equal(Matrix3d.Scaling(6, Point3d.Origin), observed);
        Assert.Equal(new DrawingNumberValue(12, DrawingUnit.Distance), snapshot.Properties![DrawingPropertyId.Length]);
        Assert.Equal(new DrawingNumberValue(36, DrawingUnit.Area), snapshot.Properties[DrawingPropertyId.Area]);
        Assert.True(line.LastTransformedCopy!.IsDisposed);
        Assert.Equal(2, line.Length);
    }

    /// <summary>Unsafe complex clones and rejected transforms retain rows without invented placed dimensions.</summary>
    [Fact]
    public void UnavailableTransformsRetainRowsAndCountFacts()
    {
        var database = CreateDocument().Database;
        var first = database.Add(new Vertex2d());
        var second = database.Add(new Vertex2d());
        var complex = new Polyline2d(first, second)
        {
            Length = 5,
            ReadArea = () => 4,
            TransformedCopy = _ => throw new InvalidOperationException("Owned vertices must never be cloned.")
        };
        var rejected = new Line
        {
            Length = 5,
            TransformedCopy = _ => throw new Autodesk.AutoCAD.Runtime.Exception(
                Autodesk.AutoCAD.Runtime.ErrorStatus.InvalidInput)
        };
        var reader = new AutoCadEntitySnapshotReader(CancellationToken.None);
        var transform = Matrix3d.Scaling(2, Point3d.Origin);

        var complexSnapshot = reader.ReadOccurrence(complex, new EntityId(first), default, transform);
        var rejectedSnapshot = reader.ReadOccurrence(rejected, new EntityId(second), default, transform);

        Assert.Equal(
            new DrawingNumberValue(2, DrawingUnit.Count),
            complexSnapshot.Properties![DrawingPropertyId.Vertices]);
        Assert.Null(complexSnapshot.Properties[DrawingPropertyId.Length]);
        Assert.Null(complexSnapshot.Properties[DrawingPropertyId.Area]);
        Assert.Null(rejectedSnapshot.Properties![DrawingPropertyId.Length]);
        Assert.Equal(new EntityId(second), rejectedSnapshot.Id);
    }

    /// <summary>An unexpected native failure on one object keeps its row without failing the read.</summary>
    [Fact]
    public void UnexpectedNativeFailureKeepsRowWithoutProperties()
    {
        var database = CreateDocument().Database;
        var id = database.Add(new DBText {TextError = Autodesk.AutoCAD.Runtime.ErrorStatus.GeneralModelingFailure});
        var reader = new AutoCadEntitySnapshotReader(CancellationToken.None);

        var snapshot = reader.Read((Entity) database.Objects[id.Value]);

        Assert.Equal(new EntityId(id), snapshot.Id);
        Assert.Equal("AcDbDBText", snapshot.TypeKey);
        Assert.Null(snapshot.Properties);
    }

    /// <summary>Elliptical arcs measure only their own span, including a start parameter above zero.</summary>
    [Fact]
    public void EllipseLengthUsesTheCurveSpan()
    {
        var reader = new AutoCadEntitySnapshotReader(CancellationToken.None);

        var snapshot = reader.Read(new Ellipse {Length = 10, StartParam = 0.5, EndParam = 2});

        Assert.Equal(DrawingPropertyId.Length, snapshot.PrimaryMetric);
        Assert.Equal(new DrawingNumberValue(15, DrawingUnit.Distance), snapshot.Properties![DrawingPropertyId.Length]);
    }

    /// <summary>A curve with no measurable length keeps its appearance properties and an unavailable length.</summary>
    [Fact]
    public void UnmeasurableCurveKeepsAppearance()
    {
        var reader = new AutoCadEntitySnapshotReader(CancellationToken.None);

        var snapshot = reader.Read(new UnmeasurableCurve());

        Assert.Equal(DrawingPropertyId.Length, snapshot.PrimaryMetric);
        Assert.Null(snapshot.Properties![DrawingPropertyId.Length]);
        Assert.Contains(DrawingPropertyId.Color, snapshot.Properties.Keys.Select(key => key.BuiltIn));
    }

    /// <summary>Proxy objects group by their original class and report the application that created them.</summary>
    [Fact]
    public void ProxyUsesOriginalClassAndApplication()
    {
        var reader = new AutoCadEntitySnapshotReader(CancellationToken.None);

        var snapshot = reader.Read(new ProxyEntity {OriginalClassName = "AecDbWall", ApplicationDescription = "ACA"});

        Assert.Equal("AecDbWall", snapshot.TypeKey);
        Assert.Equal(new DrawingTextValue("ACA"), snapshot.Properties![DrawingPropertyId.ProxyApplication]);
    }

    /// <summary>A proxy without a recorded class keeps the native proxy type.</summary>
    [Fact]
    public void ProxyWithoutOriginalClassKeepsNativeType()
    {
        var reader = new AutoCadEntitySnapshotReader(CancellationToken.None);

        Assert.Equal("AcDbProxyEntity", reader.Read(new ProxyEntity()).TypeKey);
    }

    /// <summary>Custom types publish their name, and a failing getter only makes that value unavailable.</summary>
    [Fact]
    public void CustomObjectNamesAreReadAndFailuresStayLocal()
    {
        var reader = new AutoCadEntitySnapshotReader(CancellationToken.None);

        var named = reader.Read(new NamedObject {Name = "Road axis"});
        var failing = reader.Read(new NamedObject {NameFails = true});

        Assert.Equal(new DrawingTextValue("Road axis"), named.Properties![DrawingPropertyId.Name]);
        Assert.Null(failing.Properties![DrawingPropertyId.Name]);
        Assert.Contains(DrawingPropertyId.Color, failing.Properties.Keys.Select(key => key.BuiltIn));
    }

    private sealed class UnmeasurableCurve : Curve
    {
        internal override double GetDistanceAtParameter(double parameter) =>
            throw new Autodesk.AutoCAD.Runtime.Exception(Autodesk.AutoCAD.Runtime.ErrorStatus.GeneralModelingFailure);
    }

    private sealed class NamedObject : Entity
    {
        internal bool NameFails { get; init; }

        public string Name
        {
            get => NameFails ? throw new InvalidOperationException("Getter failure") : field;
            init;
        } = "";
    }

    /// <summary>Flattened attributes show actual values and constant definitions, without placeholders.</summary>
    [Fact]
    public async Task AttributesUseActualValuesAndTheirOwnerCoordinateSpace()
    {
        var database = CreateDocument().Database;
        var definition = database.Add(new BlockTableRecord());
        var placeholder = database.Add(new AttributeDefinition {OwnerId = definition, TextString = "Template"});
        var constant = database.Add(new AttributeDefinition
        {
            OwnerId = definition, TextString = "Constant", Constant = true
        });
        var invisibleConstant = database.Add(new AttributeDefinition
        {
            OwnerId = definition, Constant = true, Invisible = true
        });
        SetContents(database, definition, placeholder, constant, invisibleConstant);
        var block = new BlockReference
        {
            OwnerId = database.CurrentSpaceId, BlockTableRecord = definition,
            BlockTransform = Matrix3d.Scaling(2, Point3d.Origin)
        };
        var root = AddRoot(database, block);
        var attribute = database.Add(new AttributeReference
        {
            OwnerId = root, TextString = "Actual", Height = 4,
            TransformedCopy = _ => throw new InvalidOperationException("Root attributes already use world coordinates.")
        });
        block.AttributeCollection.Add(attribute);
        block.AttributeCollection.Add(database.Add(new AttributeReference {OwnerId = root, Invisible = true}));

        var inventory = await ReadInventory(true);

        Assert.Equal(2, inventory.Entities.Length);
        Assert.DoesNotContain(inventory.Entities, entity => Assert.IsType<EntityId>(entity.Id).NativeId == placeholder);
        var actual = Assert.Single(
            inventory.Entities,
            entity => Assert.IsType<EntityId>(entity.Id).NativeId == attribute);
        Assert.Equal(new EntityId(attribute, new EntityId(root)), actual.Id);
        Assert.Equal(new DrawingTextValue("Actual"), actual.Properties![DrawingPropertyId.Text]);
        Assert.Equal(new DrawingNumberValue(4, DrawingUnit.Distance), actual.Properties[DrawingPropertyId.TextHeight]);
    }

    /// <summary>
    /// Repeated translated placements share definition enumeration and measured facts, retaining IDs.
    /// </summary>
    /// <param name="scale">Shared linear transform of both translated insertions.</param>
    /// <param name="expectedCopies">Native geometry copies needed for both placements.</param>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public async Task ReusesDefinitionMembersAndMeasurementsAcrossTranslatedPlacements(double scale, int expectedCopies)
    {
        var database = CreateDocument().Database;
        var definition = database.Add(new BlockTableRecord());
        var measured = 0;
        var copies = 0;
        var child = database.Add(
            new Line
            {
                OwnerId = definition, Length = 7, ReadArea = () => ++measured,
                TransformedCopy = _ =>
                {
                    copies++;
                    return new Line {Length = 7 * scale, ReadArea = () => ++measured};
                }
            });
        SetContents(database, definition, child);
        var first = AddRoot(
            database,
            new BlockReference
            {
                OwnerId = database.CurrentSpaceId, BlockTableRecord = definition,
                BlockTransform = Matrix3d.Displacement(new Vector3d(10, 0, 0)) * Matrix3d.Scaling(scale, Point3d.Origin)
            });
        var second = AddRoot(
            database,
            new BlockReference
            {
                OwnerId = database.CurrentSpaceId, BlockTableRecord = definition,
                BlockTransform = Matrix3d.Displacement(new Vector3d(20, 0, 0)) * Matrix3d.Scaling(scale, Point3d.Origin)
            });

        var inventory = await ReadInventory(true);

        Assert.Equal(1, measured);
        Assert.Equal(expectedCopies, copies);
        Assert.Equal(1, ((BlockTableRecord) database.Objects[definition.Value]).EnumerationCount);
        Assert.Equal(1, ((BlockTableRecord) database.Objects[database.CurrentSpaceId.Value]).EnumerationCount);
        Assert.Equal(
            [new EntityId(child, new EntityId(first)), new EntityId(child, new EntityId(second))],
            inventory.Entities.Select(entity => entity.Id));
        Assert.All(
            inventory.Entities,
            entity =>
                Assert.Equal(
                    new DrawingNumberValue(7 * scale, DrawingUnit.Distance),
                    entity.Properties![DrawingPropertyId.Length]));
    }

    /// <summary>A single large insertion reports nested work before its top-level object completes.</summary>
    [Fact]
    public async Task ReportsIntermediateProgressWithinOneLargeBlock()
    {
        var database = CreateDocument().Database;
        var definition = database.Add(new BlockTableRecord());
        SetContents(
            database,
            definition,
            [.. Enumerable.Range(0, 400).Select(_ => database.Add(new Line {OwnerId = definition}))]);
        AddRoot(database, new BlockReference {OwnerId = database.CurrentSpaceId, BlockTableRecord = definition});
        var updates = new List<DrawingReadProgress>();
        var tasks = new HostTasks();
        var request = new AutoCadDrawingInventorySource(tasks).ReadAsync(
            null,
            CancellationToken.None,
            null,
            true,
            new ReadProgress(updates.Add));

        await tasks.Execute(request);
        var inventory = Assert.IsType<HostResult<DrawingInventory>.Success>(await request).Value;

        Assert.Equal(400, inventory.Entities.Length);
        Assert.Contains(updates, update => update is {CompletedRoots: 0, ProcessedObjects: > 0 and < 401});
        Assert.Equal(new DrawingReadProgress(1, 1, 401), updates.Last());
    }

    /// <summary>
    /// Document and space changes invalidate a partial read before another batch is processed.
    /// </summary>
    /// <param name="change">Context change to make between batches.</param>
    [Theory]
    [InlineData("document")]
    [InlineData("space")]
    public async Task RejectsContextChangesBetweenBatches(string change)
    {
        var document = CreateDocument();
        var database = document.Database;
        var definition = database.Add(new BlockTableRecord());
        SetContents(
            database,
            definition,
            [.. Enumerable.Range(0, 400).Select(_ => database.Add(new Line {OwnerId = definition}))]);
        AddRoot(database, new BlockReference {OwnerId = database.CurrentSpaceId, BlockTableRecord = definition});
        var changed = false;
        var tasks = new HostTasks();
        var request = new AutoCadDrawingInventorySource(tasks).ReadAsync(
            null,
            CancellationToken.None,
            null,
            true,
            new ReadProgress(update =>
            {
                if (changed || update.ProcessedObjects == 0)
                    return;

                changed = true;

                switch (change)
                {
                    case "document": Application.DocumentManager.MdiActiveDocument = new Document(); break;
                    case "space": database.CurrentSpaceId = database.Add(new BlockTableRecord()); break;
                }
            }));

        await tasks.Execute(request);

        Assert.True(changed);
        Assert.IsType<HostResult<DrawingInventory>.Unavailable>(await request);
    }

    /// <summary>
    /// Native notifications during reading do not block automatic lens loading or repeated explicit refreshes.
    /// </summary>
    /// <param name="grouping">Lens grouping to load through the production provider.</param>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task NativeNotificationsDoNotBlockInitialLoadOrRepeatedRefresh(DrawingGrouping grouping)
    {
        var database = CreateDocument().Database;
        var layer = database.Objects.Values.OfType<LayerTableRecord>().Single().ObjectId;
        var getterNotifications = 0;
        var batchNotifications = 0;
        var entities = Enumerable.Range(0, 400).Select(_ => database.Add(new Line
        {
            OwnerId = database.CurrentSpaceId,
            LayerId = layer,
            ReadArea = () =>
            {
                database.ModifyObject();
                database.AppendObject();
                database.EraseObject();
                getterNotifications++;
                return 1;
            }
        }));
        SetContents(database, database.CurrentSpaceId, [.. entities]);
        var tasks = new HostTasks();
        var provider = new DrawingLensProvider(new AutoCadDrawingInventorySource(tasks));
        var progress = new ReadProgress(update =>
        {
            if (update.ProcessedObjects is <= 0 or >= 400)
                return;

            database.ModifyObject();
            database.AppendObject();
            database.EraseObject();
            batchNotifications++;
        });

        for (var read = 0; read < 3; read++)
        {
            var request = provider.LoadAsync(
                grouping,
                [],
                null,
                CancellationToken.None,
                maximumObjects: read == 0 ? 10_000 : null,
                progress: progress);
            await tasks.Execute(request);
            var presentation = Assert.IsType<HostResult<LensPresentation>.Success>(await request).Value;

            var group = Assert.Single(presentation.Groups);
            Assert.Equal(400, group.Objects.Length);
            Assert.Equal(400, presentation.Inventory!.Entities.Length);
        }

        Assert.Equal(1_200, getterNotifications);
        Assert.True(batchNotifications >= 3);
    }

    /// <summary>Cancellation between nested batches stops the read.</summary>
    [Fact]
    public async Task CancellationBetweenBatchesStopsRead()
    {
        var database = CreateDocument().Database;
        var definition = database.Add(new BlockTableRecord());
        SetContents(
            database,
            definition,
            [.. Enumerable.Range(0, 400).Select(_ => database.Add(new Line {OwnerId = definition}))]);
        AddRoot(database, new BlockReference {OwnerId = database.CurrentSpaceId, BlockTableRecord = definition});
        using var cancellation = new CancellationTokenSource();
        var tasks = new HostTasks();
        var request = new AutoCadDrawingInventorySource(tasks).ReadAsync(
            null,
            cancellation.Token,
            null,
            true,
            new ReadProgress(update =>
            {
                if (update.ProcessedObjects > 0)
                {
                    // ReSharper disable once AccessToDisposedClosure -- The read finishes before source disposal.
                    cancellation.Cancel();
                }
            }));

        await tasks.Execute(request);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    /// <summary>Cancellation is preserved when the host cancels before delivering the prepared result.</summary>
    [Fact]
    public async Task CancellationBeforePreparedResultIsDeliveredIsPreserved()
    {
        var database = CreateDocument().Database;
        AddEntity(database);
        using var cancellation = new CancellationTokenSource();
        var tasks = new HostTasks {BeforeCompletion = cancellation.Cancel};
        var request = new AutoCadDrawingInventorySource(tasks).ReadAsync(null, cancellation.Token);

        await tasks.Execute(request);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    private sealed class ReadProgress(Action<DrawingReadProgress> report) : IProgress<DrawingReadProgress>
    {
        public void Report(DrawingReadProgress value) => report(value);
    }

    private static async Task<DrawingInventory> ReadInventory(
        bool includeBlockContents,
        ImmutableArray<IPlacedObjectId>? selection = null,
        int? maximumObjects = null)
    {
        var tasks = new HostTasks();
        var read = new AutoCadDrawingInventorySource(tasks).ReadAsync(
            selection,
            CancellationToken.None,
            maximumObjects,
            includeBlockContents);
        await tasks.Execute(read);

        return Assert.IsType<HostResult<DrawingInventory>.Success>(await read).Value;
    }

    private static ObjectId AddLayer(Database database, string name)
    {
        var id = database.Add(new LayerTableRecord {Name = name});
        database.Objects[database.LayerTableId.Value] = new LayerTable(
            [.. database.Objects.Values.OfType<LayerTableRecord>().Select(layer => layer.ObjectId)]);

        return id;
    }

    private static ObjectId AddRoot(Database database, Entity entity)
    {
        var root = database.Add(entity);
        SetContents(
            database,
            database.CurrentSpaceId,
            [.. database.Objects.Values.OfType<Entity>()
                .Where(item => item.OwnerId == database.CurrentSpaceId).Select(item => item.ObjectId)]);

        return root;
    }

    private static void SetContents(Database database, ObjectId definition, params ObjectId[] children) =>
        database.Objects[definition.Value] = new BlockTableRecord(children) {ObjectId = definition};

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
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _pending = new();
        internal Action? BeforeCompletion { get; init; }

        public Task<HostResult<T>> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<HostResult<T>>();
            _pending.Enqueue(() =>
            {
                try
                {
                    var value = action();
                    BeforeCompletion?.Invoke();
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.SetResult(new HostResult<T>.Success(value));
                }
                catch (OperationCanceledException)
                {
#if NETFRAMEWORK
                    completion.SetCanceled();
#else
                    completion.SetCanceled(cancellationToken);
#endif
                }
                catch (Exception exception)
                {
                    completion.SetResult(new HostResult<T>.Unavailable(exception.Message));
                }
            });

            return completion.Task;
        }

        public Task StopAsync() => Task.CompletedTask;

        internal async Task Execute(Task request)
        {
            var timeout = DateTime.UtcNow.AddSeconds(10);

            while (!request.IsCompleted)
            {
                if (_pending.TryDequeue(out var next))
                    next();
                else
                    await Task.Delay(1);

                if (DateTime.UtcNow > timeout)
                    throw new TimeoutException("The inventory did not finish its queued batches.");
            }
        }
    }
}
