using System.Windows.Controls;
using System.Windows.Input;
using BladeControl.ViewModels;

namespace BladeControl.Views;

public partial class AppsView : UserControl
{
    public AppsView() => InitializeComponent();

    // A click on the app already picked closes its settings again.
    private void AppItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { IsSelected: true } && DataContext is AppsViewModel apps)
        {
            apps.Selected = null;
            e.Handled = true;
        }
    }
}
