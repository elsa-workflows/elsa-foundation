using System.Collections.Concurrent;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;

namespace Elsa.Activities.Design.Reconciliation.Services;

/// <summary>Default <see cref="IActivityReconciliationDiagnosticStore"/>: this node's latest refusals, lost on restart and rebuilt by the next pass.</summary>
public sealed class InMemoryActivityReconciliationDiagnosticStore : IActivityReconciliationDiagnosticStore
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<ActivityDiagnostic>> _entries = new(StringComparer.Ordinal);

    public void Record(string definitionVersionId, IReadOnlyList<ActivityDiagnostic> diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionVersionId);
        ArgumentNullException.ThrowIfNull(diagnostics);
        _entries[definitionVersionId] = diagnostics.ToArray();
    }

    public void Clear(string definitionVersionId) => _entries.TryRemove(definitionVersionId, out _);

    public IReadOnlyList<ActivityDiagnostic> Find(string definitionVersionId) =>
        _entries.TryGetValue(definitionVersionId, out var diagnostics) ? diagnostics : [];

    public IReadOnlyDictionary<string, IReadOnlyList<ActivityDiagnostic>> List() =>
        new Dictionary<string, IReadOnlyList<ActivityDiagnostic>>(_entries, StringComparer.Ordinal);
}
