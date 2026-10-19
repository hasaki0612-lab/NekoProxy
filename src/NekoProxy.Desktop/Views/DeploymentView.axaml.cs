using Avalonia;
using Avalonia.Controls;
namespace NekoProxy.Desktop.Views;
public partial class DeploymentView : UserControl
{
    public DeploymentView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => {
            var wide = Bounds.Width >= 850;
            BodyGrid.ColumnDefinitions = new(wide ? "4*,2*" : "*");
            Grid.SetColumn(ContextPanel, wide ? 1 : 0); Grid.SetRow(ContextPanel, wide ? 0 : 1);
            ContextPanel.Margin = wide ? new Thickness(28, 0, 0, 0) : new Thickness(0, 24, 0, 0);
        };
    }
}
