using Elsa.Activities.Design.Reconciliation.Clr.Contracts;
using Elsa.Activities.Design.Reconciliation.Clr.Options;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Reconciliation.Clr.Services;

/// <summary>
/// An <see cref="IActivityReconciliationSource"/> that contributes activity-version rows read from
/// CLR assemblies on disk (§2.6.1). The reconciler resolves this from DI alongside every other
/// source and calls <see cref="Read"/>; the scan is driven lazily per call so a re-run picks up
/// folder changes.
/// </summary>
public sealed class ClrActivityReconciliationSource(
    IClrAssemblyScanner scanner,
    IOptions<ClrReconciliationOptions> options) : IActivityReconciliationSource
{
    private readonly ClrReconciliationOptions _options = options.Value;

    public string SourceId => _options.ResolvedSourceId;

    public string SourceKind => "CLR";

    public ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IEnumerable<ActivityVersionReconciliationModel>>(scanner.Scan(_options.FolderPath));
}
