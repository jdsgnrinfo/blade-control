using System.Windows.Markup;
using BladeControl.Core.Localization;

namespace BladeControl.UI;

/// <summary>
/// A text on screen, in the interface's language: <c>{ui:T 'Power mode'}</c>.
/// The English text is the key, as with <see cref="L.T"/> in code. Texts are
/// written as sentences, with only their first letter in capitals.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class T : MarkupExtension
{
    public T()
    {
    }

    public T(string text) => Text = text;

    [ConstructorArgument("text")]
    public string Text { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Text);
}
