using System.Windows;

namespace CadLens.UI;

/// <summary>The locally themed modeless drawing explorer.</summary>
public partial class ExplorerWindow
{
    /// <summary>Creates the panel without changing application-wide WPF resources.</summary>
    /// <param name="viewModel">Constructor-injected explorer commands.</param>
    public ExplorerWindow(ExplorerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        MaxWidth = SystemParameters.WorkArea.Width;
        MaxHeight = SystemParameters.WorkArea.Height;
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}