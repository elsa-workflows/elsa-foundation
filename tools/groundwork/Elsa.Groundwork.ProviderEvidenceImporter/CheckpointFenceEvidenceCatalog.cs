namespace Elsa.Groundwork.Tools;

/// <summary>
/// Closed contract shared by checkpoint/fence publication import and architecture validation.
/// </summary>
public static class CheckpointFenceEvidenceCatalog
{
    public static IReadOnlyList<string> Providers { get; } =
        ["sqlite", "sqlserver", "postgresql", "mongodb"];

    public static IReadOnlyDictionary<string, string> ExpectedTopologies { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sqlite"] = "file-backed-distinct-connections",
            ["sqlserver"] = "real-sqlserver-container",
            ["postgresql"] = "real-postgresql-container",
            ["mongodb"] = "transaction-capable-replica-set"
        };

    public static IReadOnlyDictionary<string, string> SourceScenarios { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime-checkpoint-commit|concurrencySemantic:atomic-stale-fence-rejection"] = "runtime-execution-ownership-fencing",
            ["runtime-checkpoint-commit|concurrencySemantic:create-only-idempotency-marker"] = "runtime-checkpoint-idempotency",
            ["runtime-checkpoint-commit|failureWindow:before-provider-decision"] = "runtime-checkpoint-failure-recovery",
            ["runtime-checkpoint-commit|failureWindow:during-provider-decision"] = "runtime-checkpoint-failure-recovery",
            ["runtime-checkpoint-commit|failureWindow:after-durable-decision-before-caller-acknowledgement"] = "runtime-checkpoint-failure-recovery",
            ["runtime-checkpoint-commit|restartScenario:dispose-and-reopen-same-database"] = "runtime-checkpoint-bundle-process-restart",
            ["runtime-checkpoint-commit|restartScenario:process-restart-same-database"] = "runtime-checkpoint-bundle-process-restart",
            ["runtime-execution-liveness|concurrencySemantic:strictly-increasing-fence"] = "runtime-execution-ownership-fencing",
            ["runtime-post-commit-outbox|concurrencySemantic:checkpoint-bundle-write"] = "runtime-checkpoint-idempotency"
        };

    public static int ProviderOrder(string provider)
    {
        for (var index = 0; index < Providers.Count; index++)
        {
            if (string.Equals(Providers[index], provider, StringComparison.Ordinal))
                return index;
        }

        return int.MaxValue;
    }
}
