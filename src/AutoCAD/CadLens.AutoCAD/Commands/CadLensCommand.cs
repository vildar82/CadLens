using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Runtime;
using JetBrains.Annotations;

namespace CadLens.AutoCAD;

/// <summary>
/// Команда для проверки загрузки CAD Lens в AutoCAD.
/// </summary>
[UsedImplicitly]
public sealed class CadLensCommand
{
    private const string CommandName = "CADLENS";
    private const string Greeting = "\nПривет! CAD Lens готов к работе.";

    /// <summary>
    /// Выводит приветствие в командную строку активного чертежа.
    /// </summary>
    [UsedImplicitly]
    [CommandMethod(CommandName, CommandFlags.NoUndoMarker)]
    public void Execute()
    {
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(Greeting);
    }
}