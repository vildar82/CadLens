using System.Collections;
using Autodesk.AutoCAD.DatabaseServices;

// ReSharper disable MemberCanBeMadeStatic.Global, MemberCanBeMadeStatic.Local -- Native instance APIs.

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
        internal ObjectId LinetypeObjectId { get; init; }
        internal LineWeight LineWeight { get; init; } = LineWeight.ByLineWeightDefault;
        internal Colors.Transparency Transparency { get; init; } = new();
        internal string Name { get; init; } = "Layer";
        internal bool IsOff { get; init; }
        internal bool IsFrozen { get; init; }
        internal bool IsLocked { get; init; }
    }

    internal sealed class Layout : DBObject
    {
        internal string LayoutName { get; init; } = "Model";
    }

    internal sealed class Viewport : Entity
    {
        internal bool Locked { get; init; }
        internal ObjectId[] FrozenLayers { get; init; } = [];
        internal IEnumerable GetFrozenLayers() => FrozenLayers;
    }
}

namespace CadLens.Common.AutoCAD
{
    internal static class InventoryExtensions
    {
        internal static int SelectObjects(this Autodesk.AutoCAD.ApplicationServices.Editor editor, ObjectId[] objects)
        {
            editor.Selection = objects;
            return objects.Length;
        }
    }
}
