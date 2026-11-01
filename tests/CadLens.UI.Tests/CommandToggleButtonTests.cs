using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using CommunityToolkit.Mvvm.Input;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Checks that assistive technology reaches the same commands as the mouse.</summary>
public sealed class CommandToggleButtonTests
{
    /// <summary>The UI Automation toggle action runs the command once.</summary>
    [Fact]
    public void AutomationToggleExecutesCommand()
    {
        WpfTest.Run(() =>
        {
            var executions = 0;
            var button = new CommandToggleButton {Command = new RelayCommand(() => executions++)};

            GetToggle(button).Toggle();

            Assert.Equal(1, executions);
        });
    }

    /// <summary>A disabled toggle refuses the action, as UI Automation requires.</summary>
    [Fact]
    public void AutomationToggleRejectsDisabledButton()
    {
        WpfTest.Run(() =>
        {
            var executions = 0;
            var button = new CommandToggleButton
            {
                Command = new RelayCommand(() => executions++),
                IsEnabled = false
            };

            Assert.Throws<ElementNotEnabledException>(() => GetToggle(button).Toggle());
            Assert.Equal(0, executions);
        });
    }

    private static IToggleProvider GetToggle(CommandToggleButton button)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(button);
        return Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
    }
}
