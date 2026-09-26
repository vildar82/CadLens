using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CadLens.UI;

namespace CadLens.Preview;

internal sealed class ModePreviewWindow : Window
{
    private readonly ComboBox _variant = new() { ItemsSource = new[] { "Action rows", "Action strip" }, SelectedIndex = 0 };
    private readonly ComboBox _scenario = new() { ItemsSource = new[] { "Compact", "Layer list", "Layer", "Type", "Object", "Empty", "Error", "Slow" }, SelectedIndex = 1 };
    private readonly TextBlock _evidence = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button _restart = new() { Content = "Restart scenario", Margin = new Thickness(0, 12, 0, 0) };
    private ModePreviewSession? _session;
    private bool _switching;
    private bool _closing;
    private bool _allowClose;

    public ModePreviewWindow()
    {
        Title = "CAD Lens - mode comparison";
        Width = 350;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Left = 60;
        Top = 80;
        Background = new SolidColorBrush(Color.FromRgb(16, 23, 30));
        Foreground = Brushes.WhiteSmoke;
        FontFamily = new FontFamily("Segoe UI");
        Resources.MergedDictionaries.Add(new QuietTheme());

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Navigation modes", FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Treatment", Margin = new Thickness(0, 16, 0, 4) });
        panel.Children.Add(_variant);
        panel.Children.Add(new TextBlock { Text = "Scenario", Margin = new Thickness(0, 12, 0, 4) });
        panel.Children.Add(_scenario);
        panel.Children.Add(_restart);
        panel.Children.Add(new TextBlock
        {
            Text = "Simulated drawing state",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 20, 0, 0)
        });
        panel.Children.Add(_evidence);
        panel.Children.Add(new TextBlock
        {
            Text = "Focus changes camera only. Select changes selection only. Highlight adds temporary emphasis.\n\nReset turns off Auto modes and keeps the camera. It is available after pending work finishes.\n\nSwitching starts the same scenario afresh. No AutoCAD connection.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            Margin = new Thickness(0, 16, 0, 0)
        });
        Content = panel;
        Loaded += async (_, _) => await SwitchAsync();
        _variant.SelectionChanged += async (_, _) => await SwitchAsync();
        _scenario.SelectionChanged += async (_, _) => await SwitchAsync();
        _restart.Click += async (_, _) => await SwitchAsync();
        Closing += OnClosing;
    }

    private async Task SwitchAsync()
    {
        if (_switching || _closing)
            return;

        _switching = true;
        SetSelectorsEnabled(false);

        try
        {
            if (_session is not null)
                await _session.CloseAsync();

            if (_closing)
                return;

            _session = new ModePreviewSession(_variant.SelectedIndex == 1, (string)_scenario.SelectedItem);
            _evidence.SetBinding(TextBlock.TextProperty, new Binding(nameof(ModePreviewActions.Evidence)) { Source = _session.Actions });
            _session.Show();
        }
        finally
        {
            _switching = false;
            SetSelectorsEnabled(true);
        }

        if (_session is not null)
            await _session.Startup;
    }

    private void SetSelectorsEnabled(bool enabled)
    {
        _variant.IsEnabled = enabled;
        _scenario.IsEnabled = enabled;
        _restart.IsEnabled = enabled;
    }

    private async void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_allowClose)
            return;

        args.Cancel = true;
        _closing = true;

        if (_session is not null)
            await _session.CloseAsync();

        await Dispatcher.Yield(DispatcherPriority.Background);
        _allowClose = true;
        Close();
    }
}
