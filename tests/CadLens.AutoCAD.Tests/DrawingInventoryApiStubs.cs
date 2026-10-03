using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using Autodesk.AutoCAD.DatabaseServices;
using CadLens.Lenses;

namespace Autodesk.AutoCAD.EditorInput
{
    internal enum PromptStatus { OK, Error }
    internal sealed class SelectionSet(ObjectId[] objects)
    {
        internal ObjectId[] GetObjectIds() => [.. objects];
    }

    internal sealed class PromptSelectionResult(ObjectId[] objects)
    {
        internal PromptStatus Status => objects.Length == 0 ? PromptStatus.Error : PromptStatus.OK;
        internal SelectionSet Value { get; } = new(objects);
    }
}

namespace Autodesk.AutoCAD.DatabaseServices
{
    internal sealed class LayerTable(params ObjectId[] ids) : SymbolTable(ids);
    [SuppressMessage("ReSharper", "MemberCanBeMadeStatic.Global", Justification = "Matches the native instance API.")]
    internal sealed class LayerTableRecord : DBObject
    {
        internal string Name => "Layer";
        internal bool IsOff => false;
        internal bool IsFrozen => false;
        internal bool IsLocked => false;
    }

    [SuppressMessage("ReSharper", "MemberCanBeMadeStatic.Global", Justification = "Matches the native instance API.")]
    internal sealed class Layout : DBObject
    {
        internal string LayoutName => "Model";
    }

    [SuppressMessage("ReSharper", "MemberCanBeMadeStatic.Global", Justification = "Matches the native instance API.")]
    internal sealed class Viewport : Entity
    {
        internal IEnumerable GetFrozenLayers() => Array.Empty<ObjectId>();
    }
}

namespace Common.AutoCAD
{
    internal static class InventoryExtensions
    {
        internal static BlockTableRecord GetActiveSpace(this Database database) =>
            database.CurrentSpaceId.GetObject<BlockTableRecord>()!;

        internal static void SelectObjects(this Autodesk.AutoCAD.ApplicationServices.Editor editor, ObjectId[] objects) =>
            editor.Selection = objects;
    }
}

namespace CadLens.AutoCAD
{
    // Inventory tests replace property extraction only; scope and native ID validation run production code.
    internal sealed class AutoCadEntitySnapshotReader(CancellationToken cancellationToken)
    {
        internal EntitySnapshot Read(Entity entity)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return new EntitySnapshot(
                new Common.AutoCAD.EntityId(entity.ObjectId),
                new Common.AutoCAD.LayerId(entity.LayerId),
                "AcDbLine",
                ImmutableDictionary<DrawingPropertyId, DrawingValue?>.Empty);
        }
    }
}
