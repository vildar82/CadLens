using System.Windows.Controls;
using CadLens.UI;

namespace CadLens.Preview;

/// <summary>Keeps the alternative action rows available for comparison.</summary>
public partial class ModeLayersView : UserControl
{
    internal ModeLayersView(LayersViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        ActionPanel.Content = new ModeActionRows();
    }
}
