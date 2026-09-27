using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Testing;

/// <summary>A readability source whose entries a test sets, for fixtures to register on each member.</summary>
public sealed class ConformanceReadabilitySource(IEnumerable<ReadabilityEntry> entries) : IMemberReportSource<ReadabilitySection>
{
    private ReadabilitySection _section = new(entries);

    public void Set(IEnumerable<ReadabilityEntry> entries) => Volatile.Write(ref _section, new ReadabilitySection(entries));

    public ValueTask<ReadabilitySection> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Volatile.Read(ref _section));
}
