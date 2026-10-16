using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Shutdown.Ui
{
    /// <summary>Decorative icon text, hidden from screen readers (the owning control carries the name).</summary>
    public sealed class Glyph : TextBlock
    {
        protected override AutomationPeer OnCreateAutomationPeer() => null;
    }
}
