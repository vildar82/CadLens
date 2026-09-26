using Autodesk.AutoCAD.Runtime;
using JetBrains.Annotations;

namespace CadLens.AutoCAD;

/// <summary>
/// Opens or activates the modeless CAD Lens panel.
/// </summary>
[UsedImplicitly]
public sealed class CadLensCommand
{
    private const string CommandName = "CADLENS";

    /// <summary>
    /// Opens or activates the single panel for this plugin session.
    /// </summary>
    [UsedImplicitly]
    [CommandMethod(CommandName, CommandFlags.NoUndoMarker | CommandFlags.UsePickSet)]
    public void Execute()
    {
        CadLensApplication.OpenPanel();
    }
}
