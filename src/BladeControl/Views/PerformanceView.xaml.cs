using System.Windows;
using System.Windows.Controls;
using BladeControl.ViewModels;

namespace BladeControl.Views;

public partial class PerformanceView : UserControl
{
    public PerformanceView() => InitializeComponent();

    // The limit goes to the laptop once the slider is let go, not at every step of a drag.
    private void ChargeSlider_Commit(object sender, RoutedEventArgs e) =>
        (DataContext as PerformanceViewModel)?.CommitChargeLimit();
}
