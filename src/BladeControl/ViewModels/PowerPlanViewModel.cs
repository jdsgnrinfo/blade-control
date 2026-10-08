using System.ComponentModel;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Localization;
using BladeControl.Core.Platform;
using BladeControl.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BladeControl.ViewModels;

/// <summary>
/// Windows' power plan, as in Razer Helper: the plans on this PC, the one in
/// use (picking another makes it active), and a button to add the Razer Blade
/// plan while it is not there. None of it needs administrator rights.
/// </summary>
internal sealed partial class PowerPlanViewModel : ObservableObject
{
    private readonly IPowerPlans _plans = new WindowsPowerPlans();
    private IReadOnlyList<PowerPlan> _choices = [];
    private bool _showing;

    [ObservableProperty]
    private IReadOnlyList<string> _planNames = [];

    private int _planIndex = -1;

    /// <summary>The plan in use; picking another makes Windows use it.</summary>
    public int PlanIndex
    {
        get => _planIndex;
        set
        {
            if (!SetProperty(ref _planIndex, value) || _showing || _choices.ElementAtOrDefault(value) is not { } plan)
                return;

            Activate(plan);
        }
    }

    /// <summary>The line under the title: what the plan is, or what went wrong.</summary>
    [ObservableProperty]
    private string _hint = L.T("The one Windows uses.");

    [ObservableProperty]
    private bool _hintIsError;

    /// <summary>The Razer Blade plan is not on this PC yet.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _canInstall;

    /// <summary>Reads the plans again: another program, or Windows' settings, may have changed them.</summary>
    public void Refresh()
    {
        IReadOnlyList<PowerPlan> plans;
        Guid active;

        try
        {
            plans = _plans.List();
            active = _plans.Active();
        }
        catch (Win32Exception exception)
        {
            AppLog.Error("Could not read the power plans.", exception);
            return;
        }

        _showing = true;

        if (!plans.SequenceEqual(_choices))
        {
            _choices = plans;
            PlanNames = [.. plans.Select(plan => plan.Name)];
        }

        _planIndex = _choices.ToList().FindIndex(plan => plan.Id == active);
        OnPropertyChanged(nameof(PlanIndex));
        _showing = false;

        CanInstall = plans.All(plan => plan.Id != RazerBladePlan.Id);
    }

    private void Activate(PowerPlan plan)
    {
        try
        {
            _plans.Activate(plan.Id);
            AppLog.Info($"Power plan changed to {plan.Name}.");
            Show(L.T("The one Windows uses."), isError: false);
        }
        catch (Win32Exception exception)
        {
            AppLog.Error("Could not change the power plan.", exception);
            Show(L.T("Windows did not change the plan."), isError: true);
            Refresh();
        }
    }

    // Adds our plan (or brings it up to date) off the window's thread, and makes it active.
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task Install()
    {
        CanInstall = false;
        Show(L.T("Installing..."), isError: false);
        var spanish = L.Current == AppLanguage.Spanish;

        try
        {
            var refused = await Task.Run(() => RazerBladePlan.Install(_plans, spanish));
            AppLog.Info(refused.Count == 0
                ? "Razer Blade power plan installed and made active."
                : $"Razer Blade power plan installed; this PC refused {string.Join(", ", refused)}.");
            Show(L.T("Razer Blade plan installed and in use."), isError: false);
        }
        catch (Win32Exception exception)
        {
            AppLog.Error("Could not install the Razer Blade power plan.", exception);
            Show(L.T("Could not install the Razer Blade plan."), isError: true);
        }

        Refresh();
    }

    private void Show(string hint, bool isError)
    {
        Hint = hint;
        HintIsError = isError;
    }
}
