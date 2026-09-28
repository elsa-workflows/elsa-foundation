using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Core.Contracts;

/// <summary>
/// Produces one section of this host's member report (FR-014). Exactly one source may be registered per section:
/// a provider refuses to start when it finds two. A provider reads its sources each time it publishes, so a source
/// reports what is true at that moment.
/// </summary>
/// <remarks>
/// Register sources on the host container, beside the membership provider, so that every shell of the process
/// publishes the same report. <see cref="MemberReportComposition"/> is how a provider reads them.
/// </remarks>
public interface IMemberReportSource<TSection> where TSection : MemberReportSection
{
    ValueTask<TSection> ReadAsync(CancellationToken cancellationToken = default);
}
