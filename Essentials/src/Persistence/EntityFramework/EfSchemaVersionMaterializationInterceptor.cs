using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Stamps the rows of an <see cref="IEfSchemaVersionedContext"/> when they are written, and checks the stamp while EF
/// materializes one of them, before EF reads any other column of it.
/// </summary>
/// <remarks>
/// <para>
/// For a module whose context maps domain types directly and keeps the stamp in a shadow
/// <see cref="PropertyName"/> property. EF runs such a module's value converters, JSON deserializers among them, as it
/// materializes a row, so no store code runs early enough to check the stamp before the content is deserialized.
/// <see cref="CreatingInstance"/> does: EF calls it before it constructs the instance, and reads a property's value
/// only when asked for one, so the stamp is the only column read at that point. Every materialization goes through
/// it, tracked or not, whichever store or module issued the query. A projection that selects columns without
/// materializing the entity does not, so a reader that projects row content must select and check the stamp itself.
/// </para>
/// <para>
/// A module whose stores map their own row types checks the stamp in store code instead, through
/// <see cref="EfSchemaVersion"/> directly. Its context declares no family, and the interceptor leaves its rows to those
/// checks, so the one <see cref="Instance"/> can sit on any context.
/// </para>
/// <para>
/// The finalization tables every module maps (<see cref="EfSchemaFinalization"/>) belong to a family of their own even
/// in a context that declares one, so the interceptor neither checks nor stamps them; their store does both.
/// </para>
/// <para>
/// It accepts the family's current version alone (<see cref="EfSchemaVersion.EnsureCurrent"/>): a value converter has
/// deserialized the content before any upcaster could run, so a predecessor's row would be read as if this build had
/// written it. Such a family declares no upcasters (see <see cref="IEfSchemaVersionedContext"/>).
/// </para>
/// </remarks>
public sealed class EfSchemaVersionMaterializationInterceptor : IMaterializationInterceptor
{
    private EfSchemaVersionMaterializationInterceptor()
    {
    }

    /// <summary>The stamp column every table of an interceptor-checked family carries.</summary>
    public const string PropertyName = EfSchemaVersion.ColumnName;

    /// <summary>
    /// The one instance every schema-versioned context registers. A materialization interceptor is a singleton inside
    /// EF, and EF builds a separate internal service provider for every distinct set of them, so an instance per module
    /// would give every module's contexts their own.
    /// </summary>
    public static EfSchemaVersionMaterializationInterceptor Instance { get; } = new();

    public InterceptionResult<object> CreatingInstance(MaterializationInterceptionData data, InterceptionResult<object> result)
    {
        if (data.Context is IEfSchemaVersionedContext context && !EfSchemaFinalization.Maps(data.EntityType.ClrType))
            EfSchemaVersion.EnsureCurrent(context.SchemaChain, data.GetPropertyValue<string?>(PropertyName));
        return result;
    }

    /// <summary>
    /// Stamps every row <paramref name="context"/> is about to insert or update with its family's current version, apart
    /// from the finalization rows, which carry their own. A context calls this from its <c>SaveChanges</c> overrides, so
    /// no store can write a row without the stamp.
    /// </summary>
    public static void StampWrites(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var version = context is IEfSchemaVersionedContext versioned
            ? versioned.SchemaChain.CurrentVersion
            : throw new InvalidOperationException(
                $"{context.GetType().Name} stamps its writes but does not declare its schema family through {nameof(IEfSchemaVersionedContext)}.");
        foreach (var entry in context.ChangeTracker.Entries()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
                     .Where(entry => !EfSchemaFinalization.Maps(entry.Metadata.ClrType)))
            entry.Property(PropertyName).CurrentValue = version;
    }

    /// <summary>
    /// Adds <see cref="Instance"/> to <paramref name="builder"/> unless the options already carry it.
    /// </summary>
    /// <remarks>
    /// A context calls this from <c>OnConfiguring</c>, so every instance checks its rows however its options were built.
    /// A pooled context's options are frozen before <c>OnConfiguring</c> runs, and EF refuses a change to them, so a
    /// module's registration adds the interceptor up front and this call then finds it already present.
    /// </remarks>
    public static void EnsureAdded(DbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors?.Contains(Instance) != true)
            builder.AddInterceptors(Instance);
    }
}
