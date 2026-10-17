namespace CadLens.Lenses;

/// <summary>
/// Reports completed top-level objects, including their nested contents, and the number of processed objects.
/// </summary>
/// <param name="CompletedRoots">Top-level objects whose complete contents have been processed.</param>
/// <param name="TotalRoots">Total top-level objects; zero while preparing the read.</param>
/// <param name="ProcessedObjects">Objects processed so far, including block contents.</param>
public sealed record DrawingReadProgress(int CompletedRoots, int TotalRoots, int ProcessedObjects);