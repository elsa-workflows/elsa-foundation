using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Carries the database schema a host chose for one module context. It rides on <see cref="DbContextOptions"/>
/// because a derived context is constructed with nothing else: the context reads it back in
/// <c>OnModelCreating</c> through <see cref="Find(DbContext)"/> and applies <c>HasDefaultSchema</c>.
/// </summary>
/// <remarks>
/// EF caches one model per internal service provider, and that provider is cached by the hash every extension
/// contributes. Two contexts of the same type bound to different schemas therefore have to disagree here, or the
/// second would silently reuse the first one's model and write to the first one's schema.
/// </remarks>
public sealed class EfSchemaOptionsExtension(string schema) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? info;

    public string Schema { get; } = schema;

    public DbContextOptionsExtensionInfo Info => info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    /// <summary>The schema this context was bound to, or null when the host configured none.</summary>
    public static string? Find(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Find(context.GetService<IDbContextOptions>());
    }

    /// <summary>The schema these options were bound to, or null when the host configured none.</summary>
    public static string? Find(IDbContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.FindExtension<EfSchemaOptionsExtension>()?.Schema;
    }

    private sealed class ExtensionInfo(EfSchemaOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        private new EfSchemaOptionsExtension Extension => (EfSchemaOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => $"DefaultSchema={Extension.Schema} ";

        public override int GetServiceProviderHashCode() => Extension.Schema.GetHashCode(StringComparison.Ordinal);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo candidate && string.Equals(candidate.Extension.Schema, Extension.Schema, StringComparison.Ordinal);

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Elsa:" + nameof(EfSchemaOptionsExtension)] = Extension.Schema;
    }
}
