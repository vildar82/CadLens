using Autodesk.AutoCAD.Runtime;
using JetBrains.Annotations;

namespace CadLens.AutoCAD;

/// <summary>Owns plugin lifetime separately from the CADLENS command and each panel session.</summary>
[UsedImplicitly]
public sealed class CadLensApplication : IExtensionApplication
{
    private static ExplorerOwner? _owner;

    /// <inheritdoc />
    public void Initialize() => _owner = new ExplorerOwner();

    /// <inheritdoc />
    public void Terminate()
    {
        _owner?.Terminate();
        _owner = null;
    }

    internal static void OpenPanel() => _owner?.Open();
}
