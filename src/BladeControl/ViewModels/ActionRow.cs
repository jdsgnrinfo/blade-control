using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BladeControl.ViewModels;

/// <summary>
/// A line of a card with a button at its right: its title, a line under it
/// that says what it found or what it did, and the button, which turns into a
/// "working" state while its job runs (see the ActionRow template).
/// </summary>
internal sealed partial class ActionRow : ObservableObject
{
    private readonly Func<Task> _run;

    public ActionRow(string title, string buttonText, Func<Task> run, string hint = "")
    {
        Title = title;
        _buttonText = buttonText;
        _run = run;
        _hint = hint;
        RunCommand = new AsyncRelayCommand(RunAsync, () => CanRun && !IsBusy);
    }

    public string Title { get; }

    [ObservableProperty]
    private string _buttonText;

    public IAsyncRelayCommand RunCommand { get; }

    [ObservableProperty]
    private string _hint;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _canRun = true;

    private async Task RunAsync()
    {
        IsBusy = true;

        try
        {
            await _run();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
