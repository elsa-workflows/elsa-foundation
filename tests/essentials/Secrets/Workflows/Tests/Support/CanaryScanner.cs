using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Elsa.Secrets.Workflows.Tests.Support;

/// <summary>One byte form a value can take on a surface, named so a hit says how the value was stored.</summary>
public sealed record CanaryForm(string Name, byte[] Bytes);

/// <summary>One occurrence of a value's <see cref="Form"/> in <see cref="Location"/> (a file, or a named piece of text).</summary>
public sealed record CanaryHit(string Location, string Form, long Offset);

/// <summary>
/// The canary's encoded search (spec 188, T080, research R10). A value can reach a surface in a form a raw search does
/// not see: the EF runtime stores write every CLR string inside <c>ContentJson</c> as Base64 of its UTF-16LE code
/// units, so a leaked fault message in the runtime database is invisible to a search for the value's text. The scanner
/// therefore searches for every form of <see cref="FormsOf"/>: the raw UTF-8 and UTF-16LE bytes, the value as the
/// default and the relaxed JSON encoders escape it, and the Base64 of its UTF-8 and of its UTF-16LE bytes at each of
/// the three byte alignments the value can start at within a longer encoded string.
/// </summary>
/// <remarks>
/// A Base64 encoding of a longer byte string that contains the value encodes the value's bytes in characters that
/// depend only on those bytes, except at its two ends: the first characters of the value's first group mix in the bytes
/// before it, and the characters of its last, incomplete group mix in the bytes after it. Each alignment's form is the
/// encoding of the value's bytes behind 0, 1 or 2 placeholder bytes, with exactly those boundary characters dropped, so
/// it occurs in every Base64 string that encodes the value at that alignment, whatever surrounds it.
/// </remarks>
public static class CanaryScanner
{
    private static readonly JsonSerializerOptions RelaxedJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Every form of <paramref name="value"/> the scanner searches for, each byte form once.</summary>
    public static IReadOnlyList<CanaryForm> FormsOf(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        var utf8 = Encoding.UTF8.GetBytes(value);
        var utf16 = Encoding.Unicode.GetBytes(value);
        var forms = new List<CanaryForm>
        {
            new("utf8", utf8),
            new("utf16le", utf16),
            new("json", Encoding.UTF8.GetBytes(JsonEscaped(value, JsonSerializerOptions.Default))),
            new("json-relaxed", Encoding.UTF8.GetBytes(JsonEscaped(value, RelaxedJson)))
        };
        for (var alignment = 0; alignment < 3; alignment++)
        {
            forms.Add(new($"base64-utf8-{alignment}", Encoding.ASCII.GetBytes(AlignedBase64(utf8, alignment))));
            forms.Add(new($"base64-utf16le-{alignment}", Encoding.ASCII.GetBytes(AlignedBase64(utf16, alignment))));
        }

        return forms
            .Where(form => form.Bytes.Length > 0)
            .DistinctBy(form => Convert.ToHexString(form.Bytes))
            .ToArray();
    }

    /// <summary>Every occurrence of every form of <paramref name="value"/> in <paramref name="content"/>.</summary>
    public static IReadOnlyList<CanaryHit> Find(ReadOnlySpan<byte> content, string value, string location)
    {
        var hits = new List<CanaryHit>();
        foreach (var form in FormsOf(value))
        {
            var start = 0;
            int index;
            while ((index = content[start..].IndexOf(form.Bytes)) >= 0)
            {
                hits.Add(new(location, form.Name, start + index));
                start += index + 1;
            }
        }

        return hits;
    }

    /// <summary>Every occurrence of every form of <paramref name="value"/> in <paramref name="text"/>, read as UTF-8.</summary>
    public static IReadOnlyList<CanaryHit> Find(string text, string value, string location) =>
        Find(Encoding.UTF8.GetBytes(text), value, location);

    /// <summary>
    /// Every occurrence of every form of <paramref name="value"/> in the files under <paramref name="directory"/>, its
    /// subdirectories included: a database's <c>-wal</c> and <c>-shm</c> files are scanned like the database itself. A
    /// file another process holds open is read with sharing, as it stands. Refuses a directory with no file in it, so a
    /// scan of the wrong place cannot pass as a clean one.
    /// </summary>
    public static IReadOnlyList<CanaryHit> ScanDirectory(string directory, string value) =>
        ReadDirectory(directory).SelectMany(file => Find(file.Content, value, file.Path)).ToArray();

    /// <summary>
    /// The content of every file under <paramref name="directory"/>, its subdirectories included. SQLite deletes a
    /// database's <c>-wal</c> and <c>-shm</c> files when its last connection closes, after moving their pages into the
    /// database file, and can hold a file locked for a moment while it writes; when a file vanishes or is locked between
    /// listing and reading, the whole directory is read again, so the result never misses pages that moved into a file
    /// already read. Refuses a directory with no file in it.
    /// </summary>
    public static IReadOnlyList<(string Path, byte[] Content)> ReadDirectory(string directory)
    {
        for (var attempt = 1; ; attempt++)
        {
            var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToArray();
            if (files.Length == 0)
                throw new InvalidOperationException($"The canary scanner found no file to scan under '{directory}'.");
            try
            {
                return files.Select(file => (file, ReadShared(file))).ToArray();
            }
            catch (IOException) when (attempt < 20)
            {
                // A journal file closed, or a file was locked, between listing and reading: read the directory again.
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>The bytes of <paramref name="path"/>, read while another process may hold it open for writing.</summary>
    public static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string JsonEscaped(string value, JsonSerializerOptions options)
    {
        var quoted = JsonSerializer.Serialize(value, options);
        return quoted[1..^1];
    }

    private static string AlignedBase64(byte[] bytes, int alignment)
    {
        var encoded = Convert.ToBase64String([.. new byte[alignment], .. bytes]);
        // The characters that mix in the placeholder bytes, and those of the incomplete last group, are dropped.
        var skip = alignment switch { 0 => 0, 1 => 2, _ => 3 };
        var end = (alignment + bytes.Length) / 3 * 4;
        return end > skip ? encoded[skip..end] : string.Empty;
    }
}
