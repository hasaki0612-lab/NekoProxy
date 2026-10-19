using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Data;

namespace NekoProxy.Desktop.Controls;

// Keep the existing TextBox template and keyboard editing. PasswordChar alone
// only masks pixels: the stock value/text automation providers can reveal Text.
public sealed class SecretTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);
    public SecretTextBox()
    {
        PasswordChar = '●';
        Bind(AutomationProperties.NameProperty,new Binding("[part2.protectedInput]"){Source=Localization.LocalizationHub.Current,Mode=BindingMode.OneWay});
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new SecretPeer(this);

    private sealed class SecretPeer(SecretTextBox owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
        // Expose the accessible label and focus, never Value/Text patterns or
        // TextPresenter descendants. No clear-text property-change events.
        protected override object? GetProviderCore(Type providerType) => null;
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => null;
    }
}
