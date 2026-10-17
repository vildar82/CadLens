namespace CadLens.Lenses;

/// <summary>An action supported by a presentation node.</summary>
public enum LensAction
{
    /// <summary>Explicitly fit usable target bounds in the active view.</summary>
    Focus,

    /// <summary>
    /// Opens the recursive placed contents of a local block insertion.
    /// </summary>
    BrowseContents
}