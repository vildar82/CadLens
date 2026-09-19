namespace CadLens.Lenses;

/// <summary>Read-only layer status, including the active viewport's freeze state.</summary>
/// <param name="Id">Opaque layer identity.</param>
/// <param name="Name">Layer name.</param>
/// <param name="IsOff">Global switched-off state.</param>
/// <param name="IsFrozen">Global frozen state.</param>
/// <param name="IsViewportFrozen">Frozen in the currently active layout viewport.</param>
/// <param name="IsLocked">Locked status, which does not affect inclusion.</param>
public sealed record LayerSnapshot(
    string Id,
    string Name,
    bool IsOff,
    bool IsFrozen,
    bool IsViewportFrozen,
    bool IsLocked);