using System.Windows;

namespace CadLens.UI;

/// <summary>A locally themed modeless shell used to verify host integration first.</summary>
public partial class VerificationWindow
{
    /// <summary>Creates the panel without changing application-wide WPF resources.</summary>
    /// <param name="viewModel">Constructor-injected check commands.</param>
    public VerificationWindow(VerificationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        MaxWidth = SystemParameters.WorkArea.Width;
        MaxHeight = SystemParameters.WorkArea.Height;
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}