using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using CadLens.Lenses;
using Xunit;

namespace CadLens.AutoCAD;

/// <summary>Exercises insertion attribute capture in the production reader against native API doubles.</summary>
public sealed class BlockAttributeReaderTests
{
    /// <summary>Repeated and dynamic insertions keep independent values, effective names, and structural counts.</summary>
    [Fact]
    public void CapturesPerInsertionValuesAndRefreshesWithoutChangingTargets()
    {
        var database = new Database();
        var definition = database.Add(new BlockTableRecord(database.Add(new Line())) {Name = "Equipment"});
        var dynamicDefinition = database.Add(new BlockTableRecord {Name = "Equipment"});
        var firstAttribute = new AttributeReference {Tag = "MARK", TextString = "P-01"};
        var secondAttribute = new AttributeReference {Tag = "MARK", TextString = "P-02"};
        var first = AddBlock(database, definition, firstAttribute);
        var second = new BlockReference
        {
            BlockTableRecord = definition,
            DynamicBlockTableRecord = dynamicDefinition,
            IsDynamicBlock = true
        };
        database.Add(second);
        second.AttributeCollection.Add(database.Add(secondAttribute));
        var reader = new AutoCadEntitySnapshotReader(CancellationToken.None);
        var firstSnapshot = reader.Read(first);
        var secondSnapshot = reader.Read(second);
        firstAttribute.TextString = "Updated";
        var refreshed = reader.Read(first);

        Assert.Equal(new BlockAttributeSnapshot("MARK", "P-01"), Assert.Single(firstSnapshot.BlockAttributes));
        Assert.Equal(new BlockAttributeSnapshot("MARK", "P-02"), Assert.Single(secondSnapshot.BlockAttributes));
        Assert.Equal(new BlockAttributeSnapshot("MARK", "Updated"), Assert.Single(refreshed.BlockAttributes));
        Assert.Equal(firstSnapshot.Id, refreshed.Id);
        Assert.NotEqual(firstSnapshot.Id, secondSnapshot.Id);
        Assert.Equal(new DrawingBooleanValue(true), secondSnapshot.Properties![DrawingPropertyId.Dynamic]);
        Assert.Equal(new DrawingTextValue("Equipment"), secondSnapshot.Properties[DrawingPropertyId.BlockName]);
        Assert.Equal(new DrawingNumberValue(1, DrawingUnit.Count), secondSnapshot.Properties[DrawingPropertyId.DefinitionEntities]);
        Assert.Equal(new DrawingNumberValue(1, DrawingUnit.Count), secondSnapshot.Properties[DrawingPropertyId.Attributes]);
        Assert.All(database.TransactionManager.TopTransaction.OpenRequests, request => Assert.Equal(OpenMode.ForRead, request.Mode));
    }

    /// <summary>Blank, failed value, failed tag, and unreadable attributes do not collapse into the same state.</summary>
    [Fact]
    public void PreservesPartialAttributesAndBlankValues()
    {
        var database = new Database();
        var block = AddBlock(database, default, new AttributeReference {Tag = "BLANK", TextString = ""});
        block.AttributeCollection.Add(database.Add(new AttributeReference {Tag = "MISSING", TextError = ErrorStatus.NotApplicable}));
        block.AttributeCollection.Add(database.Add(new AttributeReference {TagError = ErrorStatus.NotApplicable, TextString = "Read value"}));
        block.AttributeCollection.Add(default);

        var attributes = new AutoCadEntitySnapshotReader(CancellationToken.None).Read(block).BlockAttributes;

        Assert.Equal<BlockAttributeSnapshot>(
            [
                new BlockAttributeSnapshot("BLANK", ""),
                new BlockAttributeSnapshot("MISSING", null),
                new BlockAttributeSnapshot(null, "Read value"),
                new BlockAttributeSnapshot(null, null)
            ],
            attributes);
    }

    /// <summary>Multiline text uses complete plain MText contents and disposes the returned native wrapper.</summary>
    [Fact]
    public void CapturesCompleteMultilineText()
    {
        var database = new Database();
        var text = new MText {Text = "First line\n" + new string('Ж', 500) + "\nLast line"};
        var block = AddBlock(database, default, new AttributeReference
        {
            Tag = "DESCRIPTION",
            TextString = "Single-line representation",
            IsMTextAttribute = true,
            MTextAttribute = text
        });

        var attribute = Assert.Single(new AutoCadEntitySnapshotReader(CancellationToken.None).Read(block).BlockAttributes);

        Assert.Equal(text.Text, attribute.Value);
        Assert.True(text.IsDisposed);
    }

    /// <summary>A successful empty collection differs from an unavailable collection.</summary>
    [Fact]
    public void DistinguishesEmptyAndUnavailableCollections()
    {
        var database = new Database();
        var empty = new BlockReference();
        var unavailable = new BlockReference {AttributesError = ErrorStatus.NotApplicable};
        database.Add(empty);
        database.Add(unavailable);
        var reader = new AutoCadEntitySnapshotReader(CancellationToken.None);

        Assert.True(reader.Read(empty).BlockAttributes.IsEmpty);
        Assert.True(reader.Read(unavailable).BlockAttributes.IsDefault);
    }

    private static BlockReference AddBlock(Database database, ObjectId definition, AttributeReference attribute)
    {
        var block = new BlockReference {BlockTableRecord = definition};
        database.Add(block);
        block.AttributeCollection.Add(database.Add(attribute));

        return block;
    }
}