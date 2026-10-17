namespace CadLens.Common;

/// <summary>Detached progress for a cancellable drawing operation.</summary>
/// <param name="Stage">Current operation step.</param>
/// <param name="Completed">Items processed in the current step.</param>
/// <param name="Total">Known item count, or zero when no useful fraction is available.</param>
public sealed record OperationProgress(OperationStage Stage, int Completed = 0, int Total = 0);

/// <summary>Steps displayed while exploring and visualizing drawing objects.</summary>
public enum OperationStage
{
    /// <summary>Preparing the operation.</summary>
    Preparing,
    /// <summary>Reading drawing objects.</summary>
    Reading,
    /// <summary>Building the detached explorer groups.</summary>
    Building,
    /// <summary>Selecting drawing objects.</summary>
    Selecting,
    /// <summary>Resolving isolation targets.</summary>
    Isolating,
    /// <summary>Creating temporary occurrence graphics.</summary>
    CreatingGraphics,
    /// <summary>Displaying temporary occurrence graphics.</summary>
    DisplayingGraphics,
    /// <summary>Regenerating the drawing view.</summary>
    Regenerating,
    /// <summary>Removing temporary isolation.</summary>
    Clearing,
    /// <summary>Collecting bounds and fitting the view.</summary>
    Focusing,
    /// <summary>Checking one collected object.</summary>
    Checking,
    /// <summary>Waiting for native object selection.</summary>
    WaitingForSelection
}