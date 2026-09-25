using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Modularity.Planning.Bridge;

/// <summary>Selected file identities for one local import invocation.</summary>
public sealed record SourceSelection(string ShellId, string Environment, string ShellOverlayFileName, string? AppsettingsOverlayFileName);

/// <summary>Private source bytes and change tokens that never enter an authored composition.</summary>
public sealed class SourceSnapshot
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);
    private readonly ImmutableDictionary<string, ImmutableArray<byte>> _files;
    private readonly ImmutableDictionary<string, ImmutableArray<byte>> _digests;

    private SourceSnapshot(SourceSelection selection, ImmutableDictionary<string, ImmutableArray<byte>> files)
    {
        Selection = selection;
        _files = files;
        _digests = files.ToImmutableDictionary(
            item => item.Key,
            item => SHA256.HashData(item.Value.AsSpan()).ToImmutableArray(),
            StringComparer.OrdinalIgnoreCase);
        FileNames = files.Keys.Order(StringComparer.Ordinal).ToImmutableArray();
    }

    public SourceSelection Selection { get; }

    public ImmutableArray<string> FileNames { get; }

    public static SourceSnapshot Freeze(SourceSelection selection, IEnumerable<KeyValuePair<string, byte[]>> files)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(files);
        var builder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<byte>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, content) in files)
        {
            if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || content is null)
                throw new ArgumentException("Source file identities must be safe names with content.", nameof(files));
            if (!builder.TryAdd(name, content.ToImmutableArray()))
                throw new ArgumentException("Source file identities must be unique.", nameof(files));
        }

        return new SourceSnapshot(selection, builder.ToImmutable());
    }

    public string ReadText(string fileName) => s_utf8.GetString(_files[fileName].AsSpan());

    public byte[] CopyBytes(string fileName) => _files[fileName].ToArray();

    public bool ContentMatches(string fileName, ReadOnlySpan<byte> current) =>
        _digests.TryGetValue(fileName, out var digest) &&
        SHA256.HashData(current).AsSpan().SequenceEqual(digest.AsSpan());
}
