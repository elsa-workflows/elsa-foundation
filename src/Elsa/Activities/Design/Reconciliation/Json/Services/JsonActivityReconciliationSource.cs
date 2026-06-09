using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Activities.Design.Reconciliation.Json.Contracts;
using Elsa.Activities.Design.Reconciliation.Json.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Reconciliation.Json.Services;

/// <summary>
/// An <see cref="IActivityReconciliationSource"/> that contributes activity-version rows read from one
/// or more JSON files on disk (§2.6.1). The reconciler resolves this from DI alongside every other
/// source and calls <see cref="Read"/>; files are read lazily per call so a re-run picks up edits.
/// </summary>
/// <remarks>
/// The either/or shape of <see cref="JsonReconciliationOptions"/> (a single <c>FilePath</c> or an
/// ordered <c>Files</c> list) and the required <c>SourceId</c> are validated by
/// <see cref="JsonActivityReconciliationFeature"/> at registration, so this source can assume a valid
/// configuration and simply read whichever was supplied.
/// </remarks>
public sealed class JsonActivityReconciliationSource(
    IJsonActivityCatalogReader reader,
    IOptions<JsonReconciliationOptions> options) : IActivityReconciliationSource
{
    private readonly JsonReconciliationOptions _options = options.Value;

    public string SourceId => _options.SourceId;

    public string SourceKind => "Json";

    public ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken)
    {
        var result = new List<ActivityVersionReconciliationModel>();

        foreach (var file in EffectiveFiles())
            result.AddRange(reader.Read(file.FilePath, cancellationToken));

        return new ValueTask<IEnumerable<ActivityVersionReconciliationModel>>(result);
    }

    /// <summary>
    /// The ordered set of files to read: the explicit <see cref="JsonReconciliationOptions.Files"/> when
    /// present, otherwise the single <see cref="JsonReconciliationOptions.FilePath"/> shorthand.
    /// </summary>
    private IEnumerable<JsonActivityReconciliationFileOption> EffectiveFiles()
    {
        if (_options.Files.Any())
            return _options.Files.OrderBy(f => f.Order);

        if (!string.IsNullOrWhiteSpace(_options.FilePath))
            return [new JsonActivityReconciliationFileOption(0, _options.FilePath)];

        return [];
    }
}
