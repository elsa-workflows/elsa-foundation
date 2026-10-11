using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Testing;

/// <summary>A runnability source whose entries a test sets, for fixtures to register on each member (spec 184, FR-010).</summary>
public sealed class ConformanceRunnabilitySource(IEnumerable<RunnabilityEntry> entries) : IMemberReportSource<RunnabilitySection>
{
    private RunnabilitySection _section = new(entries);

    public void Set(IEnumerable<RunnabilityEntry> entries) => Volatile.Write(ref _section, new RunnabilitySection(entries));

    public ValueTask<RunnabilitySection> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Volatile.Read(ref _section));
}
