using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Design.Reconciliation.Json.Contracts;
using Elsa.Activities.Design.Reconciliation.Json.Exceptions;
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
/// configuration and simply read whichever was supplied. An entry whose input declares <c>isCredential</c> is refused
/// (spec 188, research R5): in phase 0 the credential declaration is reserved for
/// <c>[ActivityInput(IsCredential = true)]</c> on a CLR activity, which CLR reconciliation checks can be bound to a
/// secret reference, and a JSON entry has no such check. The refusal is made here rather than in the replaceable <see cref="IJsonActivityCatalogReader"/>, so a
/// replacement reader cannot skip it.
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
        {
            var models = reader.Read(file.FilePath, cancellationToken);
            RefuseCredentialDeclarations(file.FilePath, models);
            result.AddRange(models);
        }

        return new ValueTask<IEnumerable<ActivityVersionReconciliationModel>>(result);
    }

    private static void RefuseCredentialDeclarations(string filePath, IEnumerable<ActivityVersionReconciliationModel> models)
    {
        foreach (var model in models)
        {
            var credential = (model.Inputs ?? []).FirstOrDefault(input => input.IsCredential == true);
            if (credential is not null)
            {
                throw new InvalidActivityCatalogJsonException(
                    filePath,
                    $"input '{credential.ReferenceKey}' of activity '{model.ActivityTypeKey}' declares isCredential, which a JSON activity catalog cannot declare. Declare a credential input with [ActivityInput(IsCredential = true)] on a CLR activity.");
            }
        }
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
