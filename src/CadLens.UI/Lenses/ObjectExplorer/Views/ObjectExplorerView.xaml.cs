namespace CadLens.UI;

/// <summary>The drawing lenses share their exploration layout and bindings.</summary>
public partial class ObjectExplorerView
{
    /// <summary>Creates lens content with its own view model.</summary>
    /// <param name="viewModel">Lens commands and presentation.</param>
    public ObjectExplorerView(ObjectExplorerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void DisplayPropertyClicked(object _, System.Windows.RoutedEventArgs __) =>
        DisplayPropertyPopup.IsOpen = false;
}