using System.Collections;
using Autodesk.AutoCAD.DatabaseServices;

// ReSharper disable MemberCanBeMadeStatic.Global -- Native API doubles must preserve instance member signatures.

namespace Autodesk.AutoCAD.EditorInput
{
    internal enum PromptStatus { OK, Error, Cancel }
    internal sealed class PromptSelectionOptions
    {
        internal string MessageForAdding { get; init; } = "";
        internal string MessageForRemoval { get; init; } = "";
    }
    internal sealed class SelectionSet(ObjectId[] objects)
    {
        internal ObjectId[] GetObjectIds() => [.. objects];
    }

    internal sealed class PromptSelectionResult
    {
        internal PromptSelectionResult(ObjectId[] objects) : this(objects.Length == 0 ? PromptStatus.Error : PromptStatus.OK, objects) { }

        internal PromptSelectionResult(PromptStatus status, params ObjectId[] objects)
        {
            Status = status;
            Value = new SelectionSet(objects);
        }

        internal PromptStatus Status { get; }
        internal SelectionSet Value { get; }
    }
}

namespace Autodesk.AutoCAD.DatabaseServices
{
    internal sealed class LayerTable(params ObjectId[] ids) : SymbolTable(ids);
    internal sealed class LayerTableRecord : DBObject
    {
        internal Colors.Color Color { get; init; } = new();
        internal string Name => "Layer";
        internal bool IsOff => false;
        internal bool IsFrozen => false;
        internal bool IsLocked => false;
    }

    internal sealed class Layout : DBObject
    {
        internal string LayoutName => "Model";
    }

    internal sealed class Viewport : Entity
    {
        internal IEnumerable GetFrozenLayers() => Array.Empty<ObjectId>();
    }
}

namespace CadLens.Common.AutoCAD
{
    internal static class InventoryExtensions
    {
        internal static BlockTableRecord GetActiveSpace(this Database database) =>
            database.CurrentSpaceId.GetObject<BlockTableRecord>()!;

        internal static void SelectObjects(this Autodesk.AutoCAD.ApplicationServices.Editor editor, ObjectId[] objects) =>
            editor.Selection = objects;
    }
}
