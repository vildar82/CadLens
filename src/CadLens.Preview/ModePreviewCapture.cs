using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CadLens.UI;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CadLens.Preview;

internal static class ModePreviewCapture
{
    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        if (Environment.GetCommandLineArgs().Contains("--focus-unavailable"))
        {
            await CaptureFocusUnavailableAsync(directory);
            return;
        }

        var checks = new List<string>();
        await CheckModesAsync(checks);
        await CheckCancellationAsync(checks);

        foreach (var useActionStrip in new[] { false, true })
        {
            var variant = useActionStrip ? "action-strip" : "action-rows";

            foreach (var scenario in new[] { "Compact", "Layer list", "Layer", "Type", "Object", "Empty", "Error", "Slow" })
            {
                var session = new ModePreviewSession(useActionStrip, scenario);

                try
                {
                    session.Show();

                    if (scenario != "Slow")
                        await session.Startup;

                    foreach (var width in scenario == "Compact" ? new[] { 300 } : new[] { 300, 370 })
                    {
                        session.Window.Width = width;
                        session.Window.Height = scenario == "Compact" ? 52 : width == 300 ? 450 : 660;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        var name = $"{variant}-{scenario.ToLowerInvariant().Replace(' ', '-')}-{width}.png";
                        SaveWindow(session, Path.Combine(directory, name));

                        if (useActionStrip && width == 300 && scenario is "Layer list" or "Object")
                        {
                            var view = (LayersView)session.Explorer.ActiveView!;
                            var control = (IInputElement)view.FindName(scenario == "Layer list" ? "AutoSelect" : "SelectAction");
                            Keyboard.Focus(control);
                            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            SaveWindow(session, Path.Combine(directory, name.Replace(".png", "-keyboard-focus.png")));
                            Keyboard.ClearFocus();

                            if (scenario == "Object")
                            {
                                var details = (ScrollViewer)view.FindName("ObjectDetails");
                                details.ScrollToEnd();
                                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                                SaveWindow(session, Path.Combine(directory, name.Replace(".png", "-scrolled.png")));
                                details.ScrollToHome();
                            }
                        }
                    }
                }
                finally
                {
                    await session.CloseAsync();
                }
            }
        }

        checks.Add("PASS: both variants rendered at 300x450 and 370x660; compact at 300x52.");
        checks.Add("LIMIT: simulated targets only; no AutoCAD graphics, native selection, DPI or drag validation.");
        await File.WriteAllLinesAsync(Path.Combine(directory, "checks.txt"), checks);
    }

    private static async Task CaptureFocusUnavailableAsync(string directory)
    {
        const string status = "Focus is unavailable in a locked or unavailable layout viewport. CAD objects selected. Selection highlighted. Hidden objects remain hidden.";
        var session = new ModePreviewSession(true, "Object");

        try
        {
            session.Show();
            await session.Startup;
            session.Window.Width = 300;
            session.Window.Height = 450;
            typeof(LayersViewModel).GetProperty(nameof(LayersViewModel.Status))!.SetValue(session.Model, status);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var view = (LayersView)session.Explorer.ActiveView!;
            var footer = (Border)((Grid)view.Content).Children[2];
            var statusText = ((StackPanel)footer.Child).Children.OfType<TextBlock>().Single();
            Require(statusText.Text == status && Equals(statusText.ToolTip, status), "full Focus failure status and tooltip binding");
            SaveWindow(session, Path.Combine(directory, "action-strip-focus-unavailable-300.png"));
            await File.WriteAllTextAsync(Path.Combine(directory, "focus-unavailable-check.txt"), $"PASS: the status and tooltip both contain the complete message.\nVisual fixture only; no AutoCAD invocation.\n{status}\n");
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    private static async Task CheckModesAsync(List<string> checks)
    {
        var session = new ModePreviewSession(false, "Layer list");

        try
        {
            session.Show();
            await session.Startup;
            var model = session.Model;
            var layers = model;
            var actions = session.Actions;
            Require(!model.IsAutoFocus && !model.IsAutoSelect && model.IsAutoHighlight, "default modes");
            Require(model.ToggleAutoFocusCommand.CanExecute(null) && model.ToggleAutoSelectCommand.CanExecute(null) && model.ToggleAutoHighlightCommand.CanExecute(null), "root auto controls");
            Require(!model.FocusCommand.CanExecute(null) && !model.SelectCommand.CanExecute(null) && !model.HighlightCommand.CanExecute(null), "root has no action target");
            await model.ToggleAutoHighlightCommand.ExecuteAsync(null);
            await model.ToggleAutoSelectCommand.ExecuteAsync(null);

            for (var depth = 0; depth < 3; depth++)
            {
                await layers.EnterCommand.ExecuteAsync(layers.Items[0]);
                Require(actions.SelectedTargets.SequenceEqual(layers.Current!.Objects), "auto selection at layer/type/object");
                Require(actions.CameraTargets.IsEmpty && actions.HighlightedTargets.IsEmpty, "selection-only navigation");
            }

            await layers.NextCommand.ExecuteAsync(null);
            Require(actions.SelectedTargets.SequenceEqual(layers.Current!.Objects), "auto selection on Next");
            await layers.BackCommand.ExecuteAsync(null);
            Require(actions.SelectedTargets.SequenceEqual(layers.Current!.Objects), "auto selection on Back");
            checks.Add("PASS: Auto select configured at root applies to layer, type, object, Next and Back without camera/highlight changes.");

            await model.FocusCommand.ExecuteAsync(null);
            var camera = actions.CameraTargets;
            Require(actions.SelectedTargets.SequenceEqual(layers.Current.Objects) && actions.HighlightedTargets.IsEmpty, "Focus independence");
            await model.HighlightCommand.ExecuteAsync(null);
            Require(actions.CameraTargets.SequenceEqual(camera), "Highlight independence");
            await model.ResetCommand.ExecuteAsync(null);
            Require(!model.IsAutoFocus && !model.IsAutoSelect && !model.IsAutoHighlight, "Reset turns off Auto modes");
            Require(actions.SelectedTargets.IsEmpty && actions.HighlightedTargets.IsEmpty && actions.CameraTargets.SequenceEqual(camera), "Reset clears only selection/highlight");
            await layers.EnterCommand.ExecuteAsync(layers.Items[0]);
            Require(actions.SelectedTargets.IsEmpty && actions.HighlightedTargets.IsEmpty && actions.CameraTargets.SequenceEqual(camera), "Auto modes stay off after Reset");
            checks.Add("PASS: manual Focus and Highlight are independent; Reset clears selection/highlight, turns off Auto modes, and keeps the camera.");

            await model.ToggleAutoSelectCommand.ExecuteAsync(null);
            await model.ToggleAutoFocusCommand.ExecuteAsync(null);
            await (model.FocusCommand.ExecutionTask ?? Task.CompletedTask);
            await model.ToggleAutoHighlightCommand.ExecuteAsync(null);
            Require(actions.CameraTargets.SequenceEqual(layers.Current.Objects) && actions.HighlightedTargets.SequenceEqual(layers.Current.Objects), "auto enable applies immediately");
            camera = actions.CameraTargets;
            await model.ResetCommand.ExecuteAsync(null);
            Require(!model.IsAutoFocus && !model.IsAutoSelect && !model.IsAutoHighlight, "Reset turns off all enabled modes");
            await layers.NextCommand.ExecuteAsync(null);
            Require(actions.CameraTargets.SequenceEqual(camera) && actions.SelectedTargets.IsEmpty && actions.HighlightedTargets.IsEmpty, "all modes stay off after Reset");
            await model.ToggleAutoSelectCommand.ExecuteAsync(null);
            await model.ToggleAutoSelectCommand.ExecuteAsync(null);
            Require(actions.SelectedTargets.IsEmpty, "Auto select off clears selection");
            camera = actions.CameraTargets;
            await layers.RootCommand.ExecuteAsync(null);
            Require(actions.SelectedTargets.IsEmpty && actions.HighlightedTargets.IsEmpty && actions.CameraTargets.SequenceEqual(camera), "root clears effects without moving camera");
            checks.Add("PASS: enabling Auto applies immediately, turning Auto select off clears selection, and root clears selection/highlight without moving camera.");

            await layers.EnterCommand.ExecuteAsync(layers.Items[0]);
            await model.SelectCommand.ExecuteAsync(null);
            var selection = actions.SelectedTargets;
            await layers.EnterCommand.ExecuteAsync(layers.Items[0]);
            await model.FocusCommand.ExecuteAsync(null);
            Require(actions.SelectedTargets.SequenceEqual(selection) && actions.HighlightedTargets.IsEmpty, "manual Focus preserves a different selection");
            camera = actions.CameraTargets;
            await model.HighlightCommand.ExecuteAsync(null);
            Require(actions.SelectedTargets.SequenceEqual(selection) && actions.CameraTargets.SequenceEqual(camera), "manual Highlight preserves selection/camera");
            var highlight = actions.HighlightedTargets;
            await model.SelectCommand.ExecuteAsync(null);
            Require(actions.SelectedTargets.SequenceEqual(layers.Current!.Objects) && actions.CameraTargets.SequenceEqual(camera) && actions.HighlightedTargets.SequenceEqual(highlight), "manual Select preserves camera/highlight");
            checks.Add("PASS: each manual action changes only its own target state, including Focus/Highlight while a different target remains selected.");

            await session.Explorer.ToggleLensCommand.ExecuteAsync(session.Explorer.Lenses[0]);
            Require(!layers.IsLensActive && actions.SelectedTargets.IsEmpty && actions.HighlightedTargets.IsEmpty, "compact cleanup");
            Require(session.Window.Height == 52, "real compact height");
            checks.Add("PASS: collapse clears effects and uses the production 52px compact window.");
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    private static async Task CheckCancellationAsync(List<string> checks)
    {
        var slow = new ModePreviewSession(false, "Slow");
        slow.Show();
        Require(slow.Model.IsBusy && !slow.Model.ResetCommand.CanExecute(null), "Reset unavailable during busy work");
        await slow.CloseAsync();
        Require(!slow.Window.IsVisible && !slow.Model.IsBusy && slow.Actions.SelectedTargets.IsEmpty && slow.Actions.HighlightedTargets.IsEmpty, "close while activation is busy");

        var old = new ModePreviewSession(false, "Layer");
        old.Show();
        await old.Startup;
        var pending = old.Model.FocusCommand.ExecuteAsync(null);
        Require(old.Model.IsBusy, "Focus pending before switch");
        await old.CloseAsync();
        await pending;
        Require(old.Actions.CameraTargets.IsEmpty && old.Actions.HighlightedTargets.IsEmpty, "canceled Focus has no late effects");

        var next = new ModePreviewSession(true, "Layer");

        try
        {
            next.Show();
            await next.Startup;
            Require(next.Model.Current?.Id == old.Model.Current?.Id, "same scenario after switch");
            Require(!next.Model.IsAutoFocus && !next.Model.IsAutoSelect && next.Model.IsAutoHighlight && next.Actions.CameraTargets.IsEmpty && next.Actions.SelectedTargets.IsEmpty, "fresh mode state after switch");
            var collapse = next.Explorer.ToggleLensCommand.ExecuteAsync(next.Explorer.Lenses[0]);
            next.Window.Close();
            await next.CloseAsync();
            await collapse;
            Require(!next.Window.IsVisible && next.Actions.HighlightedTargets.IsEmpty, "window close during shell cleanup");
        }
        finally
        {
            await next.CloseAsync();
        }

        checks.Add("PASS: close during slow activation, Focus and shell cleanup cancels work; new variant starts the same scenario with fresh modes.");
    }

    private static void SaveWindow(ModePreviewSession session, string path)
    {
        session.Window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)session.Window.ActualWidth, (int)session.Window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(session.Window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException($"Preview check failed: {description}.");
    }
}
