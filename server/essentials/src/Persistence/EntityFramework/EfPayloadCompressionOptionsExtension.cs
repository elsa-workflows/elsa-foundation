using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Carries the payload encoding a host chose for one module context. It rides on
/// <see cref="DbContextOptions"/> because a derived context is constructed with nothing else: the context reads it
/// back in <c>OnModelCreating</c> through <see cref="Find(DbContext)"/> and applies it to its payload columns.
/// </summary>
/// <remarks>
/// <para>
/// EF caches one model per internal service provider, and that provider is cached by the hash every extension
/// contributes. The encoding is baked into a value converter on the model, so two contexts of the same type
/// configured with different codecs have to disagree here, or the second would silently reuse the first one's model
/// and write the first one's encoding.
/// </para>
/// <para>
/// Riding on the options rather than on a model cache key is also what keeps pooling correct:
/// <see cref="EfModuleBinding.AddContext{TContext}"/> selects <c>AddDbContextPool</c> when a module sets
/// <c>Pooling</c>, and a pool is keyed by options.
/// </para>
/// </remarks>
public sealed class EfPayloadCompressionOptionsExtension(EfPayloadCompressionOptions options) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? info;

    public EfPayloadCompressionOptions Options { get; } = options;

    public DbContextOptionsExtensionInfo Info => info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options) => Options.Validate();

    /// <summary>The encoding this context was bound to, or plaintext when the host configured none.</summary>
    public static EfPayloadCompressionOptions Find(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Find(context.GetService<IDbContextOptions>());
    }

    /// <summary>The encoding these options were bound to, or plaintext when the host configured none.</summary>
    public static EfPayloadCompressionOptions Find(IDbContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.FindExtension<EfPayloadCompressionOptionsExtension>()?.Options ?? EfPayloadCompressionOptions.Plaintext;
    }

    private sealed class ExtensionInfo(EfPayloadCompressionOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        private new EfPayloadCompressionOptionsExtension Extension => (EfPayloadCompressionOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => $"PayloadCodec={Extension.Options.Codec}/{Extension.Options.MinimumLength} ";

        public override int GetServiceProviderHashCode() =>
            HashCode.Combine(Extension.Options.Codec, Extension.Options.MinimumLength);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo candidate &&
            candidate.Extension.Options.Codec == Extension.Options.Codec &&
            candidate.Extension.Options.MinimumLength == Extension.Options.MinimumLength;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Elsa:" + nameof(EfPayloadCompressionOptionsExtension)] = LogFragment;
    }
}

public static class EfPayloadCompressionOptionsBuilderExtensions
{
    /// <summary>
    /// Binds one module context to a payload encoding.
    /// </summary>
    /// <remarks>
    /// Deliberately not reachable from configuration yet. The decoder has to ship, and be deployed, before any host
    /// can enable an encoder: a database written with compression on cannot be read by a build that predates the
    /// decoder. Wiring this to a module feature option and a host-wide configuration key is the next unit of work.
    /// </remarks>
    public static DbContextOptionsBuilder UseElsaPayloadCompression(
        this DbContextOptionsBuilder builder,
        EfPayloadCompressionOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new EfPayloadCompressionOptionsExtension(options));
        return builder;
    }

    /// <inheritdoc cref="UseElsaPayloadCompression(DbContextOptionsBuilder,EfPayloadCompressionOptions)"/>
    public static DbContextOptionsBuilder<TContext> UseElsaPayloadCompression<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        EfPayloadCompressionOptions options)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseElsaPayloadCompression((DbContextOptionsBuilder)builder, options);
}
