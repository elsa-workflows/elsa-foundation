using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryRuntimeCheckpointWriter : IRuntimeCheckpointWriter
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, RuntimeCheckpointWriteRecord> _writes = new(StringComparer.Ordinal);

    public ValueTask WriteAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _writes.TryAdd(commit.CommitId, new RuntimeCheckpointWriteRecord(commit, decision));
        }

        return ValueTask.CompletedTask;
    }

    public IReadOnlyCollection<RuntimeCheckpointWriteRecord> ListWrites()
    {
        lock (_syncRoot)
        {
            return _writes.Values.ToArray();
        }
    }
}

public sealed record RuntimeCheckpointWriteRecord(
    RuntimeCheckpointCommit Commit,
    RuntimeCheckpointPersistenceDecision Decision);
