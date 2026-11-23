using Microsoft.Extensions.Configuration;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// How an operator turns payload compression on, and the only supported way a module reads that choice.
/// <para>
/// The module's own key wins, then the host-wide <see cref="ConfigurationKey"/>, then no compression at all, which
/// is what every deployment has today. Narrower-wins mirrors <see cref="EfSchema.Resolve"/> deliberately: an operator
/// who has learned how <c>Schema</c> resolves already knows how this one does.
/// </para>
/// <para>
/// Configuration only, with no C# option on each module's feature, unlike <c>Schema</c> and <c>Pooling</c>. Those
/// exist in code because a module can be moved onto its own database or pool as part of composing a host; the codec
/// is a deployment choice about stored bytes and nothing in composition depends on it. Resolving it from the owner
/// name also keeps it out of the Runtime module's shared-context agreement, where every participating feature would
/// otherwise have to carry and agree on a setting none of them acts on.
/// </para>
/// </summary>
/// <remarks>
/// Per-module rather than one switch, because the modules differ in kind: diagnostics rows are append-mostly and read
/// rarely, runtime execution state is read on every resume, and design documents back an interactive UI. The
/// host-wide key is a fallback for operators who do not want to make that distinction, and it is safe precisely
/// because the excluded columns are excluded <b>by column</b> in <see cref="EfPayloadColumns"/> rather than by
/// module: a module that owns an excluded column also owns ordinary payload columns, and no switch can reach the
/// excluded ones.
/// </remarks>
public static class EfPayloadCompressionSettings
{
    /// <summary>
    /// The host-wide default: <c>Elsa:Persistence:EntityFramework:PayloadCompression</c>, or the environment
    /// variable <c>Elsa__Persistence__EntityFramework__PayloadCompression</c>.
    /// </summary>
    public const string ConfigurationKey = "Elsa:Persistence:EntityFramework:PayloadCompression";

    /// <summary>Companion to <see cref="ConfigurationKey"/> for <see cref="EfPayloadCompressionOptions.MinimumLength"/>.</summary>
    public const string MinimumLengthConfigurationKey = "Elsa:Persistence:EntityFramework:PayloadCompressionMinimumLength";

    /// <summary>
    /// One module's own key: <c>Elsa:Persistence:EntityFramework:&lt;owner&gt;:PayloadCompression</c>, where the owner
    /// is the name the module binds with (<c>Runtime</c>, <c>OpenTelemetry</c>, <c>WorkflowsDesign</c>, …) and is the
    /// same name its migrations-history table and its errors carry.
    /// </summary>
    public static string ModuleConfigurationKey(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        return $"Elsa:Persistence:EntityFramework:{owner}:PayloadCompression";
    }

    /// <summary>
    /// The module's own key, then <see cref="ConfigurationKey"/>, then none.
    /// </summary>
    /// <returns>
    /// <c>null</c> when the result is plaintext with the default threshold, so a host that configures nothing binds
    /// no options extension at all and its contexts stay byte-identical to what they were before this shipped.
    /// </returns>
    public static EfPayloadCompressionOptions? Resolve(IServiceProvider services, string owner)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var configuration = (IConfiguration?)services.GetService(typeof(IConfiguration));
        var codec = configuration?[ModuleConfigurationKey(owner)];
        if (string.IsNullOrWhiteSpace(codec))
            codec = configuration?[ConfigurationKey];
        var resolved = Parse(owner, codec);
        // The threshold only means anything while a codec is active, so no codec binds nothing at all rather than
        // an extension that splits the model cache without changing a single stored byte.
        if (resolved == EfPayloadCompression.None)
            return null;
        return new() { Codec = resolved, MinimumLength = MinimumLength(owner, configuration) };
    }

    /// <summary>
    /// The codec named by <paramref name="codec"/>, case-insensitively, or <see cref="EfPayloadCompression.None"/>
    /// for a blank one.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A name no codec answers to. Refused rather than silently ignored: a typo that fell back to no compression
    /// would look exactly like the setting working, and the operator would have no way to tell.
    /// </exception>
    public static EfPayloadCompression Parse(string owner, string? codec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (string.IsNullOrWhiteSpace(codec))
            return EfPayloadCompression.None;
        if (Enum.TryParse<EfPayloadCompression>(codec.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            return parsed;
        throw new InvalidOperationException(
            $"{owner} EF cannot use payload codec '{codec}'. Use one of: " +
            string.Join(", ", Enum.GetNames<EfPayloadCompression>()) + $", or leave it and '{ConfigurationKey}' unset.");
    }

    private static int MinimumLength(string owner, IConfiguration? configuration)
    {
        var configured = configuration?[MinimumLengthConfigurationKey];
        if (string.IsNullOrWhiteSpace(configured))
            return EfPayloadCompressionOptions.DefaultMinimumLength;
        if (!int.TryParse(configured.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            throw new InvalidOperationException(
                $"{owner} EF cannot use payload compression minimum length '{configured}'. " +
                $"Use a non-negative whole number, or leave '{MinimumLengthConfigurationKey}' unset.");
        }

        return parsed;
    }
}
