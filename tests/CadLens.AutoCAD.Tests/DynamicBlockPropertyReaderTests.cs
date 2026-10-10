using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using CadLens.Lenses;
using Xunit;

namespace CadLens.AutoCAD;

/// <summary>Dynamic insertion values remain detached, typed, and independent of definition contents.</summary>
public sealed class DynamicBlockPropertyReaderTests
{
    /// <summary>Each insertion supplies its current values while identity, attributes, and structural metrics stay unchanged.</summary>
    [Fact]
    public void ReadsPerInsertionValuesAndRefreshesWithoutTraversingNestedBlocks()
    {
        var database = new Database();
        var nested = AddBlock(database, new DynamicBlockReferenceProperty {PropertyName = "Nested", Value = 9d});
        var definition = database.Add(new BlockTableRecord(nested.ObjectId) {Name = "Door"});
        var firstValue = new DynamicBlockReferenceProperty {PropertyName = "Width", Value = 1d};
        var first = AddBlock(database, firstValue, definition);
        var second = AddBlock(
            database,
            new DynamicBlockReferenceProperty {PropertyName = "Width", Value = 2d},
            definition);
        first.AttributeCollection.Add(database.Add(new AttributeReference {Tag = "MARK", TextString = "A"}));
        var reader = new AutoCadEntitySnapshotReader(CancellationToken.None);
        var original = reader.Read(first);
        var other = reader.Read(second);
        firstValue.Value = 3d;
        var refreshed = reader.Read(first);

        var width = DrawingPropertyKey.ForDynamicBlock("Width");
        Assert.Equal(new DrawingNumberValue(1, DrawingUnit.Scale), original.Properties![width]);
        Assert.Equal(new DrawingNumberValue(2, DrawingUnit.Scale), other.Properties![width]);
        Assert.Equal(new DrawingNumberValue(3, DrawingUnit.Scale), refreshed.Properties![width]);
        Assert.Equal(original.Id, refreshed.Id);
        Assert.NotEqual(original.Id, other.Id);
        Assert.Equal(new DrawingTextValue("Door"), original.Properties![DrawingPropertyId.BlockName]);
        Assert.Equal(
            new DrawingNumberValue(1, DrawingUnit.Count),
            original.Properties[DrawingPropertyId.DefinitionEntities]);
        Assert.Equal(new DrawingTextValue("A"), original.Properties[DrawingPropertyKey.ForAttribute("MARK")]);
        Assert.Equal(0, Assert.Single(nested.DynamicBlockReferencePropertyCollection.Properties).ValueReadCount);
        Assert.All(
            database.TransactionManager.TopTransaction.OpenRequests,
            request => Assert.Equal(OpenMode.ForRead, request.Mode));
        Assert.True(first.DynamicBlockReferencePropertyCollection.IsDisposed);
        Assert.True(second.DynamicBlockReferencePropertyCollection.IsDisposed);
    }

    /// <summary>Native units and primitive types control values; names, numeric strings, and readonly values stay intact.</summary>
    [Fact]
    public void PreservesNativeTypesUnitsNamesAndReadonlyProperties()
    {
        (object Native, DynamicBlockReferencePropertyUnitsType Units, DrawingValue Expected)[] cases =
        [
            (12.3456789, DynamicBlockReferencePropertyUnitsType.Distance,
                new DrawingNumberValue(12.3456789, DrawingUnit.Distance)),
            (Math.PI / 7, DynamicBlockReferencePropertyUnitsType.Angular,
                new DrawingNumberValue(Math.PI / 7, DrawingUnit.Angle)),
            (12.5, DynamicBlockReferencePropertyUnitsType.Area, new DrawingNumberValue(12.5, DrawingUnit.Area)),
            (0d, DynamicBlockReferencePropertyUnitsType.NoUnits, new DrawingNumberValue(0, DrawingUnit.Scale)),
            (1.25f, DynamicBlockReferencePropertyUnitsType.NoUnits, new DrawingNumberValue(1.25, DrawingUnit.Scale)),
            ((short) 1, DynamicBlockReferencePropertyUnitsType.NoUnits, new DrawingNumberValue(1, DrawingUnit.Scale)),
            (int.MaxValue, DynamicBlockReferencePropertyUnitsType.NoUnits,
                new DrawingNumberValue(int.MaxValue, DrawingUnit.Scale)),
            (true, DynamicBlockReferencePropertyUnitsType.NoUnits, new DrawingBooleanValue(true)),
            ("001.250", DynamicBlockReferencePropertyUnitsType.NoUnits, new DrawingTextValue("001.250")),
            ("", DynamicBlockReferencePropertyUnitsType.NoUnits, new DrawingTextValue(""))
        ];

        foreach (var (value, units, expected) in cases)
        {
            var property = new DynamicBlockReferenceProperty
            {
                PropertyName = "  Длина / A  ",
                Value = value,
                UnitsType = units,
                ReadOnly = true
            };
            var block = AddBlock(new Database(), property);
            var snapshot = new AutoCadEntitySnapshotReader(CancellationToken.None).Read(block);

            Assert.Equal(expected, snapshot.Properties![DrawingPropertyKey.ForDynamicBlock("  Длина / A  ")]);
            Assert.True(property.ReadOnly);
            Assert.Equal(1, property.ValueReadCount);
        }
    }

    /// <summary>Unsupported payloads and nonfinite numbers remain unavailable without lossy conversion or string parsing.</summary>
    [Fact]
    public void UnsupportedValuesAndUnitsRemainUnavailable()
    {
        object?[] values =
        [
            null, new(), decimal.MaxValue, long.MaxValue, ulong.MaxValue, double.NaN, double.PositiveInfinity,
            float.NegativeInfinity
        ];

        foreach (var value in values)
        {
            var block = AddBlock(
                new Database(),
                new DynamicBlockReferenceProperty {PropertyName = "Value", Value = value});
            Assert.Null(new AutoCadEntitySnapshotReader(CancellationToken.None).Read(block)
                .Properties![DrawingPropertyKey.ForDynamicBlock("Value")]);
        }

        var unknownUnits = AddBlock(
            new Database(),
            new DynamicBlockReferenceProperty
            {
                PropertyName = "Future units",
                Value = 1d,
                UnitsType = (DynamicBlockReferencePropertyUnitsType) 999
            });
        Assert.Null(new AutoCadEntitySnapshotReader(CancellationToken.None).Read(unknownUnits)
            .Properties![DrawingPropertyKey.ForDynamicBlock("Future units")]);
    }

    /// <summary>Read errors preserve other property names and values, and string or boolean values do not require units.</summary>
    [Fact]
    public void KeepsPartiallyReadablePropertiesAfterNativeErrors()
    {
        var block = AddBlock(new Database(), new DynamicBlockReferenceProperty {PropertyName = "", Value = ""});
        var properties = block.DynamicBlockReferencePropertyCollection.Properties;
        properties.Add(
            new DynamicBlockReferenceProperty
                {PropertyName = "Unavailable", ValueError = ErrorStatus.GeneralModelingFailure});
        properties.Add(new DynamicBlockReferenceProperty {NameError = ErrorStatus.NotApplicable, Value = "Readable"});
        properties.Add(
            new DynamicBlockReferenceProperty
                {PropertyName = "Units", Value = 1d, UnitsError = ErrorStatus.InvalidInput});
        properties.Add(
            new DynamicBlockReferenceProperty
                {PropertyName = "Text", Value = "0", UnitsError = ErrorStatus.InvalidInput});
        properties.Add(
            new DynamicBlockReferenceProperty
                {PropertyName = "Bool", Value = false, UnitsError = ErrorStatus.InvalidInput});

        var values = new AutoCadEntitySnapshotReader(CancellationToken.None).Read(block).Properties!
            .Where(pair => pair.Key.Source == DrawingPropertySource.DynamicBlock).ToList();

        Assert.Equal(4, values.Count);
        Assert.Null(values.Single(pair => pair.Key.Name == "Unavailable").Value);
        Assert.Null(values.Single(pair => pair.Key.Name == "Units").Value);
        Assert.Equal(new DrawingTextValue("0"), values.Single(pair => pair.Key.Name == "Text").Value);
        Assert.Equal(new DrawingBooleanValue(false), values.Single(pair => pair.Key.Name == "Bool").Value);
        Assert.True(block.DynamicBlockReferencePropertyCollection.IsDisposed);
    }

    /// <summary>Ordinary, empty, and unavailable collections publish no dynamic keys; native wrappers are released.</summary>
    [Fact]
    public void DistinguishesEmptyCollectionsFromNativeFailures()
    {
        var database = new Database();
        var ordinary = new BlockReference {DynamicPropertiesError = ErrorStatus.GeneralModelingFailure};
        var empty = new BlockReference {IsDynamicBlock = true};
        var unavailable = new BlockReference
            {IsDynamicBlock = true, DynamicPropertiesError = ErrorStatus.GeneralModelingFailure};
        var enumeration = new DynamicBlockReferencePropertyCollection {EnumerationError = ErrorStatus.InvalidInput};
        var failedEnumeration = new BlockReference
            {IsDynamicBlock = true, DynamicBlockReferencePropertyCollection = enumeration};
        foreach (var block in new[] {ordinary, empty, unavailable, failedEnumeration})
            database.Add(block);

        var reader = new AutoCadEntitySnapshotReader(CancellationToken.None);
        foreach (var block in new[] {ordinary, empty, unavailable, failedEnumeration})
            Assert.DoesNotContain(reader.Read(block).Properties!.Keys, key => key.Source == DrawingPropertySource.DynamicBlock);
        Assert.True(enumeration.IsDisposed);
        Assert.True(empty.DynamicBlockReferencePropertyCollection.IsDisposed);
    }

    /// <summary>Cancellation stops between property reads and still releases the native collection.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationStopsPropertyCaptureAndDisposesCollection(bool hasNextProperty)
    {
        using var cancellation = new CancellationTokenSource();
        var block = AddBlock(
            new Database(),
            new DynamicBlockReferenceProperty
            {
                PropertyName = "First",
                Value = 1d,
                ValueReading = cancellation.Cancel
            });
        var unread = new DynamicBlockReferenceProperty {PropertyName = "Next", Value = 2d};

        if (hasNextProperty)
            block.DynamicBlockReferencePropertyCollection.Properties.Add(unread);

        Assert.Throws<OperationCanceledException>(() =>
            new AutoCadEntitySnapshotReader(cancellation.Token).Read(block));
        Assert.Equal(0, unread.ValueReadCount);
        Assert.True(block.DynamicBlockReferencePropertyCollection.IsDisposed);
    }

    /// <summary>Native error recovery must not swallow managed cancellation thrown during a getter.</summary>
    [Fact]
    public void ManagedCancellationInGetterPropagates()
    {
        var block = AddBlock(
            new Database(),
            new DynamicBlockReferenceProperty
            {
                PropertyName = "Cancelled",
                ValueReading = () => throw new OperationCanceledException()
            });

        Assert.Throws<OperationCanceledException>(() =>
            new AutoCadEntitySnapshotReader(CancellationToken.None).Read(block));
        Assert.True(block.DynamicBlockReferencePropertyCollection.IsDisposed);
    }

    private static BlockReference AddBlock(
        Database database,
        DynamicBlockReferenceProperty property,
        ObjectId definition = default)
    {
        var block = new BlockReference {IsDynamicBlock = true, BlockTableRecord = definition};
        block.DynamicBlockReferencePropertyCollection.Properties.Add(property);
        database.Add(block);

        return block;
    }
}