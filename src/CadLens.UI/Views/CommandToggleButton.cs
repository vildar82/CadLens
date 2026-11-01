using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;

namespace CadLens.UI;

/// <summary>Toggle whose UI Automation action runs its command like a click, unlike the WPF default.</summary>
public sealed class CommandToggleButton : ToggleButton
{
    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new CommandTogglePeer(this);

    private sealed class CommandTogglePeer(CommandToggleButton owner) : ToggleButtonAutomationPeer(owner), IToggleProvider
    {
        void IToggleProvider.Toggle()
        {
            if (!IsEnabled())
                throw new ElementNotEnabledException();

            owner.OnClick();
        }
    }
}
