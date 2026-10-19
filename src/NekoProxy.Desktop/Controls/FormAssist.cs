using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
namespace NekoProxy.Desktop.Controls;

// View-only focus/scroll behavior. Models expose validation text; no remote work
// or validation rules run in the view. Field names are stable and untranslated.
public sealed class FormAssist : AvaloniaObject
{
    public static readonly AttachedProperty<string?> FieldProperty=AvaloniaProperty.RegisterAttached<FormAssist,Control,string?>("Field");
    public static readonly AttachedProperty<string?> MessageProperty=AvaloniaProperty.RegisterAttached<FormAssist,Control,string?>("Message");
    public static readonly AttachedProperty<int> StepProperty=AvaloniaProperty.RegisterAttached<FormAssist,Control,int>("Step");
    public static readonly AttachedProperty<bool> RevealProperty=AvaloniaProperty.RegisterAttached<FormAssist,Control,bool>("Reveal");
    public static readonly AttachedProperty<object?> SelectionProperty=AvaloniaProperty.RegisterAttached<FormAssist,ListBox,object?>("Selection");
    public static object? GetSelection(ListBox c)=>c.GetValue(SelectionProperty);
    public static void SetSelection(ListBox c,object? value)=>c.SetValue(SelectionProperty,value);
    public static bool GetReveal(Control c)=>c.GetValue(RevealProperty);
    public static void SetReveal(Control c,bool value)=>c.SetValue(RevealProperty,value);
    public static string? GetField(Control c)=>c.GetValue(FieldProperty);
    public static void SetField(Control c,string? value)=>c.SetValue(FieldProperty,value);
    public static string? GetMessage(Control c)=>c.GetValue(MessageProperty);
    public static void SetMessage(Control c,string? value)=>c.SetValue(MessageProperty,value);
    public static int GetStep(Control c)=>c.GetValue(StepProperty);
    public static void SetStep(Control c,int value)=>c.SetValue(StepProperty,value);
    static FormAssist()
    {
        // A selector clears its visual selection while ItemsSource is replaced.
        // Restore after that update even when the VM retains the same node.
        SelectionProperty.Changed.AddClassHandler<ListBox>((list,_)=>RestoreSelection(list));
        ItemsControl.ItemsSourceProperty.Changed.AddClassHandler<ListBox>((list,_)=>RestoreSelection(list));
        RevealProperty.Changed.AddClassHandler<Control>((scope,e)=>{if(e.NewValue is true)Dispatcher.UIThread.Post(()=>scope.BringIntoView(),DispatcherPriority.Loaded);});
        MessageProperty.Changed.AddClassHandler<Control>((scope,e)=>{if(e.NewValue is string {Length:>0} text)Dispatcher.UIThread.Post(()=>FocusError(scope,text),DispatcherPriority.Loaded);});
        StepProperty.Changed.AddClassHandler<Control>((scope,_)=>Dispatcher.UIThread.Post(()=>
        { foreach(var scroll in scope.GetVisualDescendants().OfType<ScrollViewer>().Where(s=>s.Name=="WizardScroll"))scroll.Offset=default; },DispatcherPriority.Loaded));
    }
    private static void RestoreSelection(ListBox list)=>Dispatcher.UIThread.Post(()=>
    {
        var desired=GetSelection(list);
        if(desired is not null&&list.Items.Contains(desired)&&!Equals(list.SelectedItem,desired))list.SelectedItem=desired;
    },DispatcherPriority.Loaded);
    public static string? ErrorField(string text)
    {
        // A successful preflight also mentions confirmation, but must not
        // scroll the new page away from its heading as if it were an error.
        if(text=="检查已完成，请确认部署内容。")return null;
        foreach(var (word,field) in new[]{("保留备份","BackupExisting"),("确认部署内容","DeploymentConfirmed"),("公钥","RealityPublicKey"),("Short ID","ShortId"),("REALITY 私钥","RealityPrivateKey"),("服务器名称","Name"),("节点名称","Name"),("IPv4","Host"),("IPv6","Ipv6Address"),("服务器域名","Host"),("SSH 端口","SshPort"),("SSH 用户","Username"),("私钥文件","PrivateKeyPath"),("先连接","Connect"),("REALITY 目标","CheckTargets"),("监听端口","Port"),("代理端口","ProxyPort"),("SNI","Sni"),("证书域名","Sni"),("域名授权","DomainAuthorized"),("授权","DomainAuthorized"),("邮箱","AcmeEmail"),("证书路径","CertificatePath"),("证书和私钥","CertificatePath"),("密钥","Credential"),("认证资料","Credential"),("UUID","Credential")})
            if(text.Contains(word,StringComparison.Ordinal))return field;
        return null;
    }
    private static void FocusError(Control scope,string text)
    {
        if(!scope.IsEffectivelyVisible||ErrorField(text) is not {} field)return;
        var controls=scope.GetLogicalDescendants().OfType<Control>().Concat(scope.GetVisualDescendants().OfType<Control>()).Distinct();
        var target=controls.FirstOrDefault(c=>GetField(c)==field&&c.IsVisible);
        if(target is null)return;
        foreach(var expander in target.GetLogicalAncestors().OfType<Expander>())expander.IsExpanded=true;
        Dispatcher.UIThread.Post(()=>{target.BringIntoView();target.Focus();},DispatcherPriority.Loaded);
    }
}
