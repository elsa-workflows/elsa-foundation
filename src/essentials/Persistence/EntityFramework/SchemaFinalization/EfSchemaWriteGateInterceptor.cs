using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// Holds every write of an EF module's context to the write version its finalization gate keeps (spec 181, FR-009 and
/// FR-012; spec 180, FR-013, FR-015 and FR-016). Every module context carries the one <see cref="Instance"/>, added by
/// <see cref="EfModuleBinding.Apply"/>, so no store can save a row without passing it.
/// </summary>
/// <remarks>
/// <para>
/// For each row the save would insert, update or delete in a stamped table of the module, against the family the
/// table belongs to (<see cref="EfSchemaModuleFamilies.FamilyOf"/>):
/// </para>
/// <list type="bullet">
/// <item>A family whose writes the gate refuses (FR-012) refuses the save, deletes included.</item>
/// <item>A row read at a version later than the write version makes the gate re-read the record first, on the saving
/// context, and the save is refused if the record does not confirm that version (spec 180, FR-015).</item>
/// <item>An inserted or updated row must carry exactly the write version. A later one is a version that is not
/// finalized, and is refused with the write refusal naming it (spec 180, FR-013 and FR-016), never restamped: the row's
/// content is in the format the store wrote, and only the store can write an older one.</item>
/// </list>
/// <para>
/// A context whose module no gate has admitted in its container, as in a test or a tool, can only write a family that
/// has one version, the one every reader reads; a family with a longer chain is refused rather than written at a
/// version nothing has confirmed is finalized.
/// </para>
/// <para>
/// A write that bypasses <c>SaveChanges</c>, such as <c>ExecuteUpdate</c>, does not pass through here. No first-party
/// store writes a stamp that way.
/// </para>
/// </remarks>
public sealed class EfSchemaWriteGateInterceptor : SaveChangesInterceptor
{
    private EfSchemaWriteGateInterceptor()
    {
    }

    /// <summary>The one instance every module context carries.</summary>
    public static EfSchemaWriteGateInterceptor Instance { get; } = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
            CheckAsync(context, synchronous: true, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            await CheckAsync(context, synchronous: false, cancellationToken);
        return result;
    }

    /// <summary>
    /// Adds <see cref="Instance"/> to <paramref name="builder"/> unless its options already carry it.
    /// </summary>
    public static DbContextOptionsBuilder EnsureAdded(DbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors?.Contains(Instance) != true)
            builder.AddInterceptors(Instance);
        return builder;
    }

    /// <summary>Checks every pending write of <paramref name="context"/>; completes synchronously when <paramref name="synchronous"/>.</summary>
    internal static async ValueTask CheckAsync(DbContext context, bool synchronous, CancellationToken cancellationToken)
    {
        if (EfSchemaModuleFamilies.ForContext(context.GetType()) is not { } families)
            return;
        if (context.ChangeTracker.AutoDetectChangesEnabled)
            context.ChangeTracker.DetectChanges();

        var writes = context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(entry => EfSchemaModuleFamilies.IsStamped(entry.Metadata))
            .ToArray();
        if (writes.Length == 0)
            return;

        var gate = FindGate(context, families.Module);
        // One write version per family for the whole save, so every row of a family this save writes carries the same
        // stamp (spec 180, FR-017).
        var versions = new Dictionary<string, EfSchemaFamilyWriteState>(StringComparer.Ordinal);
        foreach (var entry in writes)
        {
            var chain = families.FamilyOf(entry.Metadata.ClrType)
                        ?? throw new InvalidOperationException(
                            $"EF module '{families.Module}' writes '{entry.Metadata.ClrType.Name}', a stamped table no single schema family of the " +
                            "module claims. Name it in the Entities of its family's [EfSchemaFamily] declaration.");
            if (!versions.TryGetValue(chain.Family, out var state))
                versions[chain.Family] = state = StateOf(gate, chain, families.Module);
            if (state.WritesRefused)
                throw new EfSchemaFamilyWritesRefusedException(chain.Family, state.WriteVersion, state.UnreadableFinalizedVersion!, state.ReadableVersions);
            if (entry.State is EntityState.Deleted)
                continue;

            var stamp = entry.Property(EfSchemaVersion.ColumnName);
            if (entry.State is EntityState.Modified && stamp.OriginalValue is string stored && Later(chain, stored, state.WriteVersion))
            {
                // Spec 180, FR-015: a row at a later version exists only once that version is finalized, so the record
                // is read again before the row is written, and the save is refused if it does not confirm it.
                state = versions[chain.Family] = gate is null
                    ? state
                    : await gate.ConfirmAsync(context, chain, synchronous, cancellationToken);
                if (state.WritesRefused)
                    throw new EfSchemaFamilyWritesRefusedException(chain.Family, state.WriteVersion, state.UnreadableFinalizedVersion!, state.ReadableVersions);
                if (Later(chain, stored, state.WriteVersion))
                    throw new EfSchemaWriteRefusedException(chain.Family, state.WriteVersion, stored);
            }

            var written = stamp.CurrentValue as string;
            if (!StringComparer.Ordinal.Equals(written, state.WriteVersion))
                throw new EfSchemaWriteRefusedException(chain.Family, state.WriteVersion, written ?? "(no stamp)");
        }
    }

    /// <summary>
    /// What <paramref name="chain"/> may write: the gate's state once it has admitted the module, and otherwise the
    /// family's only version, when it has just one.
    /// </summary>
    private static EfSchemaFamilyWriteState StateOf(EfSchemaModuleGate? gate, EfSchemaChain chain, string module)
    {
        if (gate?.StateOf(chain.Family) is { } state)
            return state;
        if (chain.ReadableVersions.Count == 1)
            return new EfSchemaFamilyWriteState(chain.CurrentVersion, null, chain.ReadableVersions);
        throw new EfSchemaWriteRefusedException(chain.Family, chain.ReadableVersions[0], chain.CurrentVersion);
    }

    /// <summary>True when <paramref name="stamp"/> comes after <paramref name="writeVersion"/> along the chain, or is not in it.</summary>
    private static bool Later(EfSchemaChain chain, string stamp, string writeVersion)
    {
        var readable = chain.ReadableVersions;
        var at = SchemaVersionChain.PositionOf(readable, stamp);
        return at < 0 || at > SchemaVersionChain.PositionOf(readable, writeVersion);
    }

    private static EfSchemaModuleGate? FindGate(DbContext context, string module) =>
        context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.Find(module);
}
