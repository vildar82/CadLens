namespace CadLens.Lenses;

/// <summary>Root organization of direct objects in the active drawing space.</summary>
public enum DrawingGrouping
{
    /// <summary>Layer, then runtime type, then individual object.</summary>
    Layers,

    /// <summary>Runtime type across layers, then individual object.</summary>
    ObjectTypes
}
