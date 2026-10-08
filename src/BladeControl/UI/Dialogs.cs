using System.Windows;

namespace BladeControl.UI;

/// <summary>Questions and notices, over the main window.</summary>
internal static class Dialogs
{
    public static MessageBoxResult Ask(
        string message,
        string title,
        MessageBoxButton buttons = MessageBoxButton.YesNo,
        MessageBoxImage image = MessageBoxImage.Question,
        MessageBoxResult defaultResult = MessageBoxResult.No) =>
        Application.Current.MainWindow is { IsVisible: true } owner
            ? MessageBox.Show(owner, message, title, buttons, image, defaultResult)
            : MessageBox.Show(message, title, buttons, image, defaultResult);

    public static void Tell(string message, string title) =>
        Ask(message, title, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);
}
