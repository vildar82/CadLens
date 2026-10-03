using JetBrains.Annotations;

namespace CadLens.UI;

/// <summary>Loads local control styles and a standalone dark palette.</summary>
[UsedImplicitly]
public partial class QuietTheme
{
    /// <summary>Creates the local theme without application-wide resource or theme changes.</summary>
    public QuietTheme()
    {
        InitializeComponent();
        AppearancePalette.Apply(this, "Dark", "Quiet", "Mint", false);
    }
}