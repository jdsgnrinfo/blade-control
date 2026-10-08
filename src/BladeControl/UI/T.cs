using System.Text;
using System.Windows.Markup;
using BladeControl.Core.Localization;

namespace BladeControl.UI;

/// <summary>
/// A text on screen, in the interface's language: <c>{ui:T 'Power mode'}</c>.
/// The English text is the key, as with <see cref="L.T"/> in code.
/// <see cref="Caps"/> writes it as the concept's titles are: in capitals,
/// spread out (WPF has no letter spacing, so thin spaces stand in for it).
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

    public bool Caps { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var text = L.T(Text);
        return Caps ? Spread(text) : text;
    }

    /// <summary>"Power mode" becomes "P O W E R  M O D E" with hair spaces.</summary>
    public static string Spread(string text)
    {
        const char hairSpace = ' ';
        var upper = text.ToUpper(L.Current == AppLanguage.Spanish
            ? System.Globalization.CultureInfo.GetCultureInfo("es")
            : System.Globalization.CultureInfo.InvariantCulture);
        var builder = new StringBuilder(upper.Length * 2);

        foreach (var character in upper)
        {
            if (builder.Length > 0)
                builder.Append(hairSpace);

            builder.Append(character);
        }

        return builder.ToString();
    }
}
