namespace CadLens.UI;

/// <summary>The drawing lenses share their exploration layout and bindings.</summary>
public partial class ObjectExplorerView
{
    /// <summary>Creates lens content with its own view model.</summary>
    /// <param name="viewModel">Lens commands and presentation.</param>
    public ObjectExplorerView(ObjectExplorerViewModel viewModel)
    {
        InitializeComponent();
#if !NET47
        System.Windows.Automation.AutomationProperties.SetLiveSetting(
            StatusText,
            System.Windows.Automation.AutomationLiveSetting.Polite);
#endif
        DataContext = viewModel;
    }

    private void DisplayPropertyClicked(object _, System.Windows.RoutedEventArgs __) =>
        DisplayPropertyPopup.IsOpen = false;
}