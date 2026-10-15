using BladeControl.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BladeControl.ViewModels;

/// <summary>
/// A line of a card with a button at its right: its title, a line under it
/// that says what it found or what it did, and the button, which gives way to
/// a loader while its job runs. When the job ends the loader shows a check, or
/// a cross if the job called <see cref="Fail"/> or threw, for a moment before
/// the button comes back; a job the user called off (<see cref="Skip"/>) shows
/// neither (see the ActionRow template).
/// </summary>
internal sealed partial class ActionRow : ObservableObject
{
    private static readonly TimeSpan ResultShown = TimeSpan.FromMilliseconds(1400);

    private readonly Func<Task> _run;
    private bool _failed;
    private bool _skipped;

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

    /// <summary>What the loader shows: running, or how the job ended.</summary>
    [ObservableProperty]
    private LoaderStatus _status;

    /// <summary>For the job: it did not do what was asked, so the loader ends in a cross.</summary>
    public void Fail() => _failed = true;

    /// <summary>For the job: the user called it off, so the button comes back without a mark.</summary>
    public void Skip() => _skipped = true;

    private async Task RunAsync()
    {
        _failed = false;
        _skipped = false;
        Status = LoaderStatus.Loading;
        IsBusy = true;

        try
        {
            await _run();
        }
        catch
        {
            _failed = true;
            throw;
        }
        finally
        {
            if (!_skipped)
            {
                Status = _failed ? LoaderStatus.Error : LoaderStatus.Success;
                await Task.Delay(ResultShown);
            }

            IsBusy = false;
            Status = LoaderStatus.Loading;
        }
    }
}
