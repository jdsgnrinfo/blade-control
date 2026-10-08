using System.Windows;
using System.Windows.Controls;
using BladeControl.ViewModels;

namespace BladeControl.Views;

public partial class LightingView : UserControl
{
    public LightingView() => InitializeComponent();

    // A drag only sends; letting go reads the laptop back once.
    private void KeyboardBrightness_Commit(object? sender, EventArgs e) =>
        (DataContext as LightingViewModel)?.CommitKeyboardBrightness();
}
