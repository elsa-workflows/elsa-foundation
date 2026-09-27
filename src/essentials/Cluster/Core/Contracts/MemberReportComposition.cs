using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Core.Contracts;

/// <summary>How a provider reads its report sources, so every provider composes a report the same way (FR-014).</summary>
public static class MemberReportComposition
{
    /// <summary>
    /// Returns the one source registered for a section, or <see langword="null"/> when there is none. A provider calls
    /// this when it is constructed, so two sources for one section refuse the provider at startup and name both.
    /// </summary>
    public static IMemberReportSource<TSection>? SingleSource<TSection>(IEnumerable<IMemberReportSource<TSection>> sources)
        where TSection : MemberReportSection
    {
        ArgumentNullException.ThrowIfNull(sources);
        var registered = sources.ToArray();
        return registered.Length switch
        {
            0 => null,
            1 => registered[0],
            _ => throw new ClusterMembershipConfigurationException(
                $"More than one source is registered for the {typeof(TSection).Name} member report section: " +
                $"{string.Join(", ", registered.Select(source => source.GetType().FullName))}. Exactly one source may produce a section.")
        };
    }

    /// <summary>Reads every section from its source at this moment.</summary>
    public static async ValueTask<MemberReport> ComposeAsync(
        IMemberReportSource<ReadabilitySection>? readability,
        CancellationToken cancellationToken = default) =>
        new(readability is null ? null : await readability.ReadAsync(cancellationToken));
}
