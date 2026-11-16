using System.IO.Compression;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The stored form of a payload column, and the only supported way a module encodes one.
/// <para>
/// A compressed value is marked <b>in band</b>, inside the value: <c>elsaz1.&lt;codec&gt;.&lt;base64&gt;</c>.
/// Nothing else in the row says how the value was written, which is what lets compressed and uncompressed rows
/// coexist in one column with no schema change and no migration. A value that does not carry
/// <see cref="FramePrefix"/> is plaintext, which is what every row written before this shipped already is.
/// </para>
/// <para>
/// <b>The payload is UTF-16 code units, not UTF-8.</b> <see cref="System.Text.Encoding"/> substitutes U+FFFD for a
/// lone surrogate, so a UTF-8 round trip is not lossless for every .NET string. This uses the same raw code-unit
/// representation as <see cref="EfRelationalIdentity.Encode"/>, for the same reason: the runtime deliberately
/// preserves lone surrogates through persistence
/// (<c>Elsa.Workflows.Runtime…Stores.RuntimeArtifactJson.LosslessUtf16StringConverter</c>), and a codec that
/// quietly replaced them would defeat that.
/// </para>
/// </summary>
public static class EfPayloadCodec
{
    /// <summary>
    /// What marks a stored value as a frame. Versioned, so a later format can coexist rather than having to be
    /// distinguished by guessing.
    /// </summary>
    public const string FramePrefix = "elsaz1.";

    /// <summary>
    /// The identity codec. It exists so encoding stays total: a plaintext that itself begins with
    /// <see cref="FramePrefix"/> is framed with this rather than stored raw, where a reader would mistake it for a
    /// frame. Payload columns are not all JSON documents — <c>WorkflowTriggerBindingProjectionStateEntity.ContentJson</c>
    /// stores a hex fingerprint — so "a document can never start with that" is not an argument available here.
    /// </summary>
    private const string RawCodecName = "raw";

    private const string GZipCodecName = "gz";
    private const char Separator = '.';

    /// <summary>True when <paramref name="stored"/> is a frame rather than plaintext.</summary>
    public static bool IsFrame(string? stored) =>
        stored is not null && stored.StartsWith(FramePrefix, StringComparison.Ordinal);

    /// <summary>
    /// The stored form of <paramref name="value"/>.
    /// <para>
    /// Returns plaintext whenever the frame would not be <b>shorter</b> than the plaintext, so base64's expansion can
    /// never make a row larger. <paramref name="minimumLength"/> short-circuits that comparison for values too small
    /// to be worth attempting; it is a cost control, not a correctness rule, and any value of it produces correct
    /// output.
    /// </para>
    /// </summary>
    public static string? Encode(string? value, EfPayloadCompression codec, int minimumLength)
    {
        if (value is null)
            return null;
        // Checked first and unconditionally: this is what keeps Decode's two cases unambiguous, so it applies even
        // when the module writes plaintext.
        if (IsFrame(value))
            return Frame(RawCodecName, EncodeUtf16CodeUnits(value));
        if (codec != EfPayloadCompression.GZip || value.Length < minimumLength)
            return value;
        var framed = Frame(GZipCodecName, Compress(EncodeUtf16CodeUnits(value)));
        return framed.Length < value.Length ? framed : value;
    }

    /// <summary>
    /// The plaintext behind <paramref name="stored"/>, which is <paramref name="stored"/> itself when it is not a frame.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The value carries <see cref="FramePrefix"/> but is not a frame this build can read. Failing closed is
    /// deliberate: Elsa 3's equivalent path logs a warning and substitutes a default state, which loses the row's
    /// contents silently.
    /// </exception>
    public static string? Decode(string? stored)
    {
        if (stored is null || !IsFrame(stored))
            return stored;
        var parts = stored.Split(Separator, 3);
        // Base64's alphabet contains no '.', so three parts is exact rather than a lower bound.
        if (parts.Length != 3)
            throw new InvalidDataException("A persisted payload carries the Elsa frame marker but is not a complete frame.");
        var name = parts[1];
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"A persisted payload frame with codec '{name}' does not contain valid base64.", exception);
        }

        return name switch
        {
            RawCodecName => DecodeUtf16CodeUnits(bytes),
            GZipCodecName => DecodeUtf16CodeUnits(Decompress(bytes)),
            _ => throw new InvalidDataException(
                $"A persisted payload names codec '{name}', which this build cannot read. A database written by a " +
                "newer build cannot be read by an older one; deploy the build that introduced the codec.")
        };
    }

    private static string Frame(string codecName, byte[] bytes) =>
        string.Concat(FramePrefix, codecName, Separator.ToString(), Convert.ToBase64String(bytes));

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        // Disposed before ToArray: GZipStream writes its trailer on dispose, and reading the buffer first yields a
        // truncated member that inflates to nothing.
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes, 0, bytes.Length);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException("A persisted payload frame is not readable GZip data.", exception);
        }
    }

    private static byte[] EncodeUtf16CodeUnits(string value)
    {
        var bytes = new byte[checked(value.Length * sizeof(char))];
        for (var index = 0; index < value.Length; index++)
        {
            bytes[index * sizeof(char)] = (byte)value[index];
            bytes[index * sizeof(char) + 1] = (byte)(value[index] >> 8);
        }

        return bytes;
    }

    private static string DecodeUtf16CodeUnits(byte[] bytes)
    {
        if (bytes.Length % sizeof(char) != 0)
            throw new InvalidDataException("A persisted payload frame does not contain complete UTF-16 code units.");
        var chars = new char[bytes.Length / sizeof(char)];
        for (var index = 0; index < chars.Length; index++)
            chars[index] = (char)(bytes[index * sizeof(char)] | bytes[index * sizeof(char) + 1] << 8);
        return new string(chars);
    }
}
