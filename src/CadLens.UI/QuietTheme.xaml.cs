using System.Windows;
using JetBrains.Annotations;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace CadLens.UI;

/// <summary>Loads window-scoped theme dependencies before the custom XAML styles.</summary>
[UsedImplicitly]
public partial class QuietTheme : ResourceDictionary
{
    /// <summary>Creates the theme with direct references that the plugin host can resolve.</summary>
    public QuietTheme()
    {
        MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
        MergedDictionaries.Add(new ControlsDictionary());
        InitializeComponent();
    }
}