using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using CadLens.Lenses;
using Xunit;

namespace CadLens.AutoCAD;

/// <summary>Exercises insertion attribute capture in the production reader against native API doubles.</summary>
public sealed class BlockAttributeReaderTests
{
    /// <summary>
    /// Keeps origins distinct in one dictionary and marks duplicate named values unavailable.
    /// </summary>
    [Fact]
    public void UnifiedPropertiesKeepSourcesAndDuplicateValuesDistinct()
    {
        var database = new Database();
        var block = new BlockReference {IsDynamicBlock = true};
        database.Add(block);
        block.AttributeCollection.Add(database.Add(new AttributeReference {Tag = "Color", TextString = "A"}));
        block.AttributeCollection.Add(database.Add(new AttributeReference {Tag = "Color", TextString = "B"}));
        block.AttributeCollection.Add(database.Add(new AttributeReference {Tag = "Width", TextString = "001"}));
        var dynamicProperties = block.DynamicBlockReferencePropertyCollection.Properties;
        dynamicProperties.Add(new DynamicBlockReferenceProperty {PropertyName = "Color", Value = 3d});
        dynamicProperties.Add(new DynamicBlockReferenceProperty {PropertyName = "Width", Value = 1d});
        dynamicProperties.Add(new DynamicBlockReferenceProperty {PropertyName = "Width", Value = 2d});

        var properties = new AutoCadEntitySnapshotReader(CancellationToken.None).Read(block).Properties!;

        Assert.IsType<DrawingColorValue>(properties[DrawingPropertyId.Color]);
        Assert.Null(properties[DrawingPropertyKey.ForAttribute("Color")]);
        Assert.Equal(new DrawingNumberValue(3, DrawingUnit.Scale), properties[DrawingPropertyKey.ForDynamicBlock("Color")]);
        Assert.Equal(new DrawingTextValue("001"), properties[DrawingPropertyKey.ForAttribute("Width")]);
        Assert.Null(properties[DrawingPropertyKey.ForDynamicBlock("Width")]);
    }

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

        Assert.Equal(new DrawingTextValue("P-01"), firstSnapshot.Properties![DrawingPropertyKey.ForAttribute("MARK")]);
        Assert.Equal(new DrawingTextValue("P-02"), secondSnapshot.Properties![DrawingPropertyKey.ForAttribute("MARK")]);
        Assert.Equal(new DrawingTextValue("Updated"), refreshed.Properties![DrawingPropertyKey.ForAttribute("MARK")]);
        Assert.Equal(firstSnapshot.Id, refreshed.Id);
        Assert.NotEqual(firstSnapshot.Id, secondSnapshot.Id);
        Assert.Equal(new DrawingBooleanValue(true), secondSnapshot.Properties![DrawingPropertyId.Dynamic]);
        Assert.Equal(new DrawingTextValue("Equipment"), secondSnapshot.Properties[DrawingPropertyId.BlockName]);
        Assert.Equal(new DrawingNumberValue(1, DrawingUnit.Count), secondSnapshot.Properties[DrawingPropertyId.DefinitionEntities]);
        Assert.Equal(new DrawingNumberValue(1, DrawingUnit.Count), secondSnapshot.Properties[DrawingPropertyId.Attributes]);
        Assert.All(database.TransactionManager.TopTransaction.OpenRequests, request => Assert.Equal(OpenMode.ForRead, request.Mode));
    }

    /// <summary>Blank text remains distinct from unavailable values; unnamed attributes have no dictionary key.</summary>
    [Fact]
    public void PreservesPartialAttributesAndBlankValues()
    {
        var database = new Database();
        var block = AddBlock(database, default, new AttributeReference {Tag = "BLANK", TextString = ""});
        block.AttributeCollection.Add(database.Add(new AttributeReference {Tag = "MISSING", TextError = ErrorStatus.NotApplicable}));
        block.AttributeCollection.Add(database.Add(new AttributeReference {TagError = ErrorStatus.NotApplicable, TextString = "Read value"}));
        block.AttributeCollection.Add(default);

        var attributes = new AutoCadEntitySnapshotReader(CancellationToken.None).Read(block).Properties!
            .Where(pair => pair.Key.Source == DrawingPropertySource.Attribute).ToList();

        Assert.Equal(2, attributes.Count);
        Assert.Equal(new DrawingTextValue(""), attributes.Single(pair => pair.Key.Name == "BLANK").Value);
        Assert.Null(attributes.Single(pair => pair.Key.Name == "MISSING").Value);
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

        var attribute = new AutoCadEntitySnapshotReader(CancellationToken.None).Read(block).Properties![DrawingPropertyKey.ForAttribute("DESCRIPTION")];

        Assert.Equal(new DrawingTextValue(text.Text), attribute);
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

        Assert.Equal(new DrawingNumberValue(0, DrawingUnit.Count), reader.Read(empty).Properties![DrawingPropertyId.Attributes]);
        Assert.Null(reader.Read(unavailable).Properties![DrawingPropertyId.Attributes]);
    }

    private static BlockReference AddBlock(Database database, ObjectId definition, AttributeReference attribute)
    {
        var block = new BlockReference {BlockTableRecord = definition};
        database.Add(block);
        block.AttributeCollection.Add(database.Add(attribute));

        return block;
    }
}