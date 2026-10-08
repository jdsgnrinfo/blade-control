using System.Windows;
using BladeControl.Core.Localization;
using BladeControl.Core.Services;

namespace BladeControl.Views;

/// <summary>Asks for a name to save the equalizer's curve under; the app's own presets' names are taken.</summary>
public partial class PresetNameWindow : Window
{
    public PresetNameWindow(string suggested)
    {
        InitializeComponent();
        NameBox.Text = suggested;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };

        // Dragging the card moves it.
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not System.Windows.Controls.TextBox)
                DragMove();
        };
    }

    /// <summary>The name given, trimmed; set once Save closes it.</summary>
    public string PresetName { get; private set; } = string.Empty;

    /// <summary>Asks over the main window; null when it was cancelled.</summary>
    public static string? Ask(string suggested)
    {
        var window = new PresetNameWindow(suggested) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.PresetName : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();

        if (name.Length == 0)
        {
            Problem.Text = L.T("Give the preset a name.");
            return;
        }

        if (EqPreset.IsBuiltIn(name))
        {
            Problem.Text = L.T("That name belongs to one of the app's presets. Try another.");
            return;
        }

        PresetName = name;
        DialogResult = true;
    }
}
