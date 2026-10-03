namespace CadLens.UI.Tests;

/// <summary>An unrelated lens layout used to verify the shell's module boundary.</summary>
public partial class CounterLensView
{
    internal CounterLensView(CounterViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}