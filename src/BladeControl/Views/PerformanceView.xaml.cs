using System.Windows;
using System.Windows.Controls;
using BladeControl.Core.Models;
using BladeControl.ViewModels;

namespace BladeControl.Views;

public partial class PerformanceView : UserControl
{
    public PerformanceView() => InitializeComponent();

    // A processor setting goes to Windows once its slider is let go; the slider's Tag names it.
    private void CpuSlider_Commit(object? sender, EventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name } && Enum.TryParse<CpuSetting>(name, out var setting))
            (DataContext as PerformanceViewModel)?.CommitCpu(setting);
    }

    // The limit goes to the laptop once the slider is let go, not at every step of a drag.
    private void ChargeSlider_Commit(object? sender, EventArgs e) =>
        (DataContext as PerformanceViewModel)?.CommitChargeLimit();
}
