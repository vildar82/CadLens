using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Runtime;
using JetBrains.Annotations;

namespace CadLens.AutoCAD;

/// <summary>
/// Command used to verify that CAD Lens is loaded in AutoCAD.
/// </summary>
[UsedImplicitly]
public sealed class CadLensCommand
{
    private const string CommandName = "CADLENS";
    private const string Greeting = "\nHello! CAD Lens is ready.";

    /// <summary>
    /// Writes a greeting to the active drawing's command line.
    /// </summary>
    [UsedImplicitly]
    [CommandMethod(CommandName, CommandFlags.NoUndoMarker)]
    public void Execute()
    {
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(Greeting);
    }
}