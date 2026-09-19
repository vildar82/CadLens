using Autodesk.AutoCAD.DatabaseServices;
using Common.AutoCAD;
using Xunit;

namespace CadLens.AutoCAD;

/// <summary>Checks the default opening mode and filtering of unusable identifiers.</summary>
public sealed class DatabaseObjectExtensionsTests
{
    /// <summary>The optional flag changes only the transaction's access mode.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpensTypedObjectInActiveTransaction(bool forWrite)
    {
        var database = new Database();
        var entity = new Entity();
        var id = database.Add(entity);

        Assert.Same(entity, id.GetObject<Entity>(forWrite));
        Assert.Equal(
            (forWrite ? OpenMode.ForWrite : OpenMode.ForRead, false, true),
            Assert.Single(database.TransactionManager.TopTransaction.OpenRequests));
    }

    /// <summary>Invalid and erased identifiers never reach the native transaction.</summary>
    [Fact]
    public void SkipsErasedAndInvalidObjects()
    {
        var database = new Database();
        var erased = database.Add(new Entity { IsErased = true });

        Assert.Null(erased.GetObject<Entity>());
        Assert.Null(default(ObjectId).GetObject<Entity>());
        Assert.Empty(database.TransactionManager.TopTransaction.OpenRequests);
    }

    /// <summary>Table and block enumeration return only live objects of the requested type.</summary>
    [Fact]
    public void EnumeratesOnlyMatchingLiveObjects()
    {
        var database = new Database();
        var entity = new Entity();
        var live = database.Add(entity);
        var erased = database.Add(new Entity { IsErased = true });
        var otherType = database.Add(new DBObject());
        var table = new SymbolTable(live, erased, otherType);
        var space = new BlockTableRecord(live, erased, otherType);

        Assert.Same(entity, Assert.Single(table.GetObjects<Entity>()));
        Assert.Same(entity, Assert.Single(space.GetObjects<Entity>()));
    }
}