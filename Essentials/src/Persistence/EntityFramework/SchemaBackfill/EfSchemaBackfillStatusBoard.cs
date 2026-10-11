using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>
/// Each family's backfill status as this worker last saw it (spec 186, FR-021), and the one place a blocker is logged:
/// when it first appears or changes, since every round meets a persistent blocker again. The module's gate reads it
/// through <see cref="IEfSchemaBackfillStatusSource"/>.
/// </summary>
internal sealed class EfSchemaBackfillStatusBoard(string module, ILogger logger) : IEfSchemaBackfillStatusSource
{
    private readonly object _lock = new();
    private readonly Dictionary<string, EfSchemaBackfillStatus> _statuses = new(StringComparer.Ordinal);

    public EfSchemaBackfillStatus StatusOf(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        lock (_lock)
            return _statuses.GetValueOrDefault(family) ?? EfSchemaBackfillStatus.Idle(family);
    }

    public void Update(string family, Func<EfSchemaBackfillStatus, EfSchemaBackfillStatus> change)
    {
        lock (_lock)
            _statuses[family] = change(_statuses.GetValueOrDefault(family) ?? EfSchemaBackfillStatus.Idle(family));
    }

    /// <summary>Records that <paramref name="blockers"/> keep <paramref name="family"/> from completion, logging each one this worker had not reported.</summary>
    public void Blocked(string family, IReadOnlyList<EfSchemaBackfillBlocker> blockers)
    {
        var reported = StatusOf(family).Blockers.Select(blocker => blocker.Detail).ToHashSet(StringComparer.Ordinal);
        foreach (var blocker in blockers.Where(blocker => !reported.Contains(blocker.Detail)))
            logger.Log(
                blocker.Kind == EfSchemaBackfillBlockerKind.ContentAddressed ? LogLevel.Information : LogLevel.Error,
                "Schema family {Family} of EF module {Module} cannot be recorded complete: {Blocker}", family, module, blocker.Detail);
        Update(family, status => status with { State = EfSchemaBackfillState.Blocked, Blockers = blockers, SettleWaitingFor = [], Detail = null });
    }
}
