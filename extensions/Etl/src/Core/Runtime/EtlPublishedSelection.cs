using System.Globalization;

namespace Elsa.Etl.Core.Runtime;

/// <summary>A literal workflow selection that pins one published ETL definition version and its full content hash.</summary>
/// <remarks>
/// The token is an authored literal input, so the workflow executable's existing input-binding hash commits to this
/// identity without changing the generic workflow executable format. The corresponding bounded artifact envelope is
/// stored separately in ETL-owned executable-node metadata and must match this selection at runtime.
/// </remarks>
public sealed record EtlPublishedSelection
{
    private const string TokenPrefix = "etl1:";
    private const int MaximumDefinitionIdLength = 256;
    private const int ContentHashLength = 64;
    private static readonly int MaximumTokenLength = TokenPrefix.Length + MaximumDefinitionIdLength + 1 + 10 + 1 + ContentHashLength;

    /// <summary>Creates an exact published artifact selection.</summary>
    /// <exception cref="ArgumentException">An identity component is outside the canonical v1 token format.</exception>
    public EtlPublishedSelection(string DefinitionId, int DefinitionVersion, string ContentHash)
    {
        if (!IsValidDefinitionId(DefinitionId))
            throw new ArgumentException("The ETL definition identifier is invalid.", nameof(DefinitionId));
        if (DefinitionVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(DefinitionVersion), "The ETL definition version must be positive.");
        if (!IsLowercaseSha256(ContentHash))
            throw new ArgumentException("The ETL content hash must be a lowercase SHA-256 value.", nameof(ContentHash));

        this.DefinitionId = DefinitionId;
        this.DefinitionVersion = DefinitionVersion;
        this.ContentHash = ContentHash;
    }

    /// <summary>The selected published definition identifier.</summary>
    public string DefinitionId { get; }

    /// <summary>The selected positive published definition version.</summary>
    public int DefinitionVersion { get; }

    /// <summary>The selected full lowercase SHA-256 content hash.</summary>
    public string ContentHash { get; }

    /// <summary>Returns the canonical, bounded ASCII v1 selection token.</summary>
    public string ToToken() =>
        $"{TokenPrefix}{DefinitionId}:{DefinitionVersion.ToString(CultureInfo.InvariantCulture)}:{ContentHash}";

    /// <summary>Parses one canonical token, rejecting alternate spellings and unsupported versions.</summary>
    /// <exception cref="FormatException">The token is malformed, noncanonical, or outside the v1 bounds.</exception>
    public static EtlPublishedSelection Parse(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Length > MaximumTokenLength || !token.StartsWith(TokenPrefix, StringComparison.Ordinal))
            throw InvalidToken();

        var definitionSeparator = token.IndexOf(':', TokenPrefix.Length);
        var versionSeparator = definitionSeparator < 0 ? -1 : token.IndexOf(':', definitionSeparator + 1);
        if (definitionSeparator <= TokenPrefix.Length || versionSeparator < 0 ||
            token.IndexOf(':', versionSeparator + 1) >= 0)
            throw InvalidToken();

        var definitionId = token.AsSpan(TokenPrefix.Length, definitionSeparator - TokenPrefix.Length);
        var versionText = token.AsSpan(definitionSeparator + 1, versionSeparator - definitionSeparator - 1);
        var hash = token.AsSpan(versionSeparator + 1);
        if (!IsValidDefinitionId(definitionId) ||
            !int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            version <= 0 || !IsLowercaseSha256(hash))
            throw InvalidToken();

        var selection = new EtlPublishedSelection(definitionId.ToString(), version, hash.ToString());
        if (!StringComparer.Ordinal.Equals(selection.ToToken(), token))
            throw InvalidToken();
        return selection;
    }

    private static bool IsValidDefinitionId(string? value) =>
        value is not null && IsValidDefinitionId(value.AsSpan());

    private static bool IsValidDefinitionId(ReadOnlySpan<char> value)
    {
        if (value.Length is 0 or > MaximumDefinitionIdLength || !IsAsciiAlphaNumeric(value[0]))
            return false;

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (!IsAsciiAlphaNumeric(character) && character is not ('.' or '_' or '-'))
                return false;
        }

        return true;
    }

    private static bool IsLowercaseSha256(string? value) =>
        value is not null && IsLowercaseSha256(value.AsSpan());

    private static bool IsLowercaseSha256(ReadOnlySpan<char> value)
    {
        if (value.Length != ContentHashLength)
            return false;
        foreach (var character in value)
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        return true;
    }

    private static bool IsAsciiAlphaNumeric(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    private static FormatException InvalidToken() =>
        new("The ETL published selection token is invalid or noncanonical.");
}
