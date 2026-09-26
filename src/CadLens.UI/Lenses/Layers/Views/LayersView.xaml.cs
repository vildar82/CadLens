using System.Windows.Controls;

namespace CadLens.UI;

/// <summary>The Layers lens owns its exploration layout and bindings.</summary>
public partial class LayersView : UserControl
{
    /// <summary>Creates the Layers content with its own view model.</summary>
    /// <param name="viewModel">Layers-specific commands and presentation.</param>
    public LayersView(LayersViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}