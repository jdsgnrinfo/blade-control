using System.Text;

namespace BladeControl.Core.Hardware;

/// <summary>
/// What a screen says about itself in its EDID, the block every display hands
/// the computer: its maker's three-letter code, its model, its size, its
/// native resolution and, from EDID 1.4, how many bits each color has.
/// </summary>
internal sealed record Edid(
    string MakerCode,
    int ProductCode,
    string? Model,
    int? WidthMillimeters,
    int? HeightMillimeters,
    int? NativeWidth,
    int? NativeHeight,
    int? BitsPerColor)
{
    private const int BlockSize = 128;
    private const int DescriptorsStart = 54;
    private const int DescriptorSize = 18;
    private const int DescriptorCount = 4;

    private static readonly byte[] Header = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    /// <summary>The diagonal in inches, from the size in millimeters, or null without it.</summary>
    public double? DiagonalInches =>
        WidthMillimeters is { } width && HeightMillimeters is { } height
            ? Math.Sqrt(width * (double)width + height * (double)height) / 25.4
            : null;

    /// <summary>Reads an EDID's base block, or null when it is not one.</summary>
    public static Edid? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < BlockSize || !data[..Header.Length].SequenceEqual(Header))
            return null;

        // Three letters of five bits each, 1 for A, big-endian.
        var maker = (data[8] << 8) | data[9];
        var makerCode = new string([Letter(maker >> 10), Letter(maker >> 5), Letter(maker)]);
        var productCode = data[10] | (data[11] << 8);

        // The size in centimeters, used only when the timing gives none in millimeters.
        int? width = data[21] > 0 ? data[21] * 10 : null;
        int? height = data[22] > 0 ? data[22] * 10 : null;
        int? nativeWidth = null, nativeHeight = null;
        string? name = null, text = null;

        for (var index = 0; index < DescriptorCount; index++)
        {
            var descriptor = data.Slice(DescriptorsStart + index * DescriptorSize, DescriptorSize);

            // A detailed timing (its pixel clock is not zero); the first is the native mode.
            if (descriptor[0] != 0 || descriptor[1] != 0)
            {
                if (nativeWidth is null)
                {
                    nativeWidth = descriptor[2] | ((descriptor[4] & 0xF0) << 4);
                    nativeHeight = descriptor[5] | ((descriptor[7] & 0xF0) << 4);

                    var widthMm = descriptor[12] | ((descriptor[14] & 0xF0) << 4);
                    var heightMm = descriptor[13] | ((descriptor[14] & 0x0F) << 8);

                    if (widthMm > 0 && heightMm > 0)
                        (width, height) = (widthMm, heightMm);
                }

                continue;
            }

            // A text: the screen's name (0xFC), or a line of free text (0xFE), where panels give their part number.
            switch (descriptor[3])
            {
                case 0xFC:
                    name ??= Text(descriptor[5..]);
                    break;
                case 0xFE:
                    text = Text(descriptor[5..]) is { Length: > 0 } line ? line : text;
                    break;
            }
        }

        // From EDID 1.4, a digital input's bits per color: 1 is 6, 2 is 8, up to 6 for 16.
        int? bits = null;

        if (data[18] == 1 && data[19] >= 4 && (data[20] & 0x80) != 0 && ((data[20] >> 4) & 0x07) is var depth and >= 1 and <= 6)
            bits = 4 + 2 * depth;

        return new Edid(makerCode, productCode, NonEmpty(name) ?? NonEmpty(text), width, height, nativeWidth, nativeHeight, bits);
    }

    private static char Letter(int bits) => (char)('A' - 1 + (bits & 0x1F));

    // Printable ASCII up to the line feed that ends it, trimmed.
    private static string Text(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder();

        foreach (var value in bytes)
        {
            if (value == 0x0A)
                break;

            if (value is >= 0x20 and < 0x7F)
                builder.Append((char)value);
        }

        return builder.ToString().Trim();
    }

    private static string? NonEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
