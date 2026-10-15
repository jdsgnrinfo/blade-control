using System.Buffers.Binary;
using System.Text;

namespace BladeControl.Core.Services;

/// <summary>
/// Writes a small ICC display profile (version 2.1) that carries one of the
/// app's color profiles as its video card gamma table ("vcgt"): the curve
/// Windows loads into the graphics card at sign-in and whenever the screen's
/// profile changes, so the colors stay without the app. Its colorimetry is
/// sRGB's, what Windows assumes for a screen without a profile, so color-aware
/// apps see the screen as they did.
/// </summary>
internal static class IccProfile
{
    // sRGB's primaries, adapted to the D50 the profile connection space uses, and its D65 white.
    private static readonly double[] Red = [0.4361, 0.2225, 0.0139];
    private static readonly double[] Green = [0.3851, 0.7169, 0.0971];
    private static readonly double[] Blue = [0.1431, 0.0606, 0.7141];
    private static readonly double[] White = [0.9505, 1.0, 1.0891];
    private static readonly double[] D50 = [0.9642, 1.0, 0.8249];

    /// <summary>The profile's bytes: <paramref name="ramp"/> is 768 values, red, green then blue, 256 each.</summary>
    public static byte[] Build(string description, ushort[] ramp, DateTime created)
    {
        if (ramp.Length != 3 * ColorProfiles.Levels)
            throw new ArgumentException("The ramp needs 256 values for each of red, green and blue.", nameof(ramp));

        var gamma = Curve();
        var tags = new List<(string Signature, byte[] Data)>
        {
            ("desc", Description(description)),
            ("cprt", Text("No copyright, use freely.")),
            ("wtpt", Xyz(White)),
            ("rXYZ", Xyz(Red)),
            ("gXYZ", Xyz(Green)),
            ("bXYZ", Xyz(Blue)),
            ("rTRC", gamma),
            ("gTRC", gamma),
            ("bTRC", gamma),
            ("vcgt", VideoCardGamma(ramp))
        };

        // The header, the tag count and table, then each tag's data on a four-byte boundary.
        var tableEnd = 128 + 4 + 12 * tags.Count;
        var offsets = new int[tags.Count];
        var position = tableEnd;

        for (var index = 0; index < tags.Count; index++)
        {
            offsets[index] = position;
            position = Align(position + tags[index].Data.Length);
        }

        var profile = new byte[position];
        var span = profile.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span[0..], (uint)profile.Length);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], 0x02100000);
        Ascii(span[12..], "mntr");
        Ascii(span[16..], "RGB ");
        Ascii(span[20..], "XYZ ");
        BinaryPrimitives.WriteUInt16BigEndian(span[24..], (ushort)created.Year);
        BinaryPrimitives.WriteUInt16BigEndian(span[26..], (ushort)created.Month);
        BinaryPrimitives.WriteUInt16BigEndian(span[28..], (ushort)created.Day);
        BinaryPrimitives.WriteUInt16BigEndian(span[30..], (ushort)created.Hour);
        BinaryPrimitives.WriteUInt16BigEndian(span[32..], (ushort)created.Minute);
        BinaryPrimitives.WriteUInt16BigEndian(span[34..], (ushort)created.Second);
        Ascii(span[36..], "acsp");
        Ascii(span[40..], "MSFT");
        WriteXyz(span[68..], D50);

        BinaryPrimitives.WriteUInt32BigEndian(span[128..], (uint)tags.Count);

        for (var index = 0; index < tags.Count; index++)
        {
            var entry = span[(132 + 12 * index)..];
            Ascii(entry, tags[index].Signature);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)offsets[index]);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)tags[index].Data.Length);
            tags[index].Data.CopyTo(span[offsets[index]..]);
        }

        return profile;
    }

    /// <summary>The gamma table a profile carries, or null when it has none: to read back what was written.</summary>
    public static ushort[]? ReadVideoCardGamma(byte[] profile)
    {
        var span = profile.AsSpan();
        var count = BinaryPrimitives.ReadUInt32BigEndian(span[128..]);

        for (var index = 0; index < count; index++)
        {
            var entry = span[(132 + 12 * index)..];

            if (Encoding.ASCII.GetString(entry[..4]) != "vcgt")
                continue;

            var data = span[(int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..])..];
            var channels = BinaryPrimitives.ReadUInt16BigEndian(data[12..]);
            var entries = BinaryPrimitives.ReadUInt16BigEndian(data[14..]);
            var ramp = new ushort[channels * entries];

            for (var value = 0; value < ramp.Length; value++)
                ramp[value] = BinaryPrimitives.ReadUInt16BigEndian(data[(18 + 2 * value)..]);

            return ramp;
        }

        return null;
    }

    // textDescriptionType: the ASCII name, then empty Unicode and ScriptCode parts.
    private static byte[] Description(string text)
    {
        var ascii = Encoding.ASCII.GetBytes(text + '\0');
        var data = new byte[12 + ascii.Length + 4 + 4 + 2 + 1 + 67];
        Ascii(data, "desc");
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)ascii.Length);
        ascii.CopyTo(data, 12);
        return data;
    }

    private static byte[] Text(string text)
    {
        var ascii = Encoding.ASCII.GetBytes(text + '\0');
        var data = new byte[8 + ascii.Length];
        Ascii(data, "text");
        ascii.CopyTo(data, 8);
        return data;
    }

    private static byte[] Xyz(double[] xyz)
    {
        var data = new byte[20];
        Ascii(data, "XYZ ");
        WriteXyz(data.AsSpan(8), xyz);
        return data;
    }

    // A plain 2.2 gamma, the usual for a screen.
    private static byte[] Curve()
    {
        var data = new byte[14];
        Ascii(data, "curv");
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12), 0x0233);
        return data;
    }

    // Apple's vcgt as a table: three channels of 256 two-byte entries.
    private static byte[] VideoCardGamma(ushort[] ramp)
    {
        var data = new byte[18 + 2 * ramp.Length];
        Ascii(data, "vcgt");
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12), 3);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(14), ColorProfiles.Levels);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), 2);

        for (var index = 0; index < ramp.Length; index++)
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(18 + 2 * index), ramp[index]);

        return data;
    }

    private static void WriteXyz(Span<byte> target, double[] xyz)
    {
        for (var index = 0; index < 3; index++)
            BinaryPrimitives.WriteInt32BigEndian(target[(4 * index)..], (int)Math.Round(xyz[index] * 65536));
    }

    private static void Ascii(Span<byte> target, string text) => Encoding.ASCII.GetBytes(text).CopyTo(target);

    private static int Align(int position) => (position + 3) & ~3;
}
