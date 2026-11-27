using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Diagnostics;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;

namespace Elsa3.Activities.Design.Import.Publishing.Services;

/// <summary>
/// Publishes an imported reusable version the way Studio does: preflight for a review token, then the reviewed
/// publish, naming the plan's version id so wrappers and callers keep resolving to it (spec 246 R-1, R-2).
/// </summary>
internal sealed class PublisherReusableActivityImportPublisher(
    IActivityDefinitionPublisher publisher,
    IActivityPublishingAuthorizationContext authorization) : IReusableActivityImportPublisher
{
    private const string TenantDenied = "activity.tenant.reference-denied";

    public async ValueTask<ReusableActivityImportPublishOutcome> PublishAsync(
        ReusableActivityImportPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The publisher resolves its tenant from the caller; it must be the tenant the import committed in.
        if (!StringComparer.Ordinal.Equals(authorization.TenantId, request.TenantId))
            return ReusableActivityImportPublishOutcome.Refused(TenantDenied, [
                new(TenantDenied, ActivityDiagnosticSeverity.Error,
                    "The publisher resolves another tenant than the import committed in.",
                    new("ActivityDraft", request.DraftId, null, Revision: request.DraftRevision),
                    Remediation: "The caller's tenant claim and the host's persistence scope must name the same tenant; check how the host resolves the tenant.")
            ]);

        try
        {
            var preflight = await publisher.PreflightAsync(
                new(request.DraftId, request.DraftRevision, request.ExpectedDefinitionHeadVersionId) { Version = request.Version },
                cancellationToken);
            if (!preflight.IsPublishable)
                return ReusableActivityImportPublishOutcome.Refused(FirstErrorCode(preflight.Diagnostics), preflight.Diagnostics);

            var receipt = await publisher.PublishReviewedAsync(
                new(request.DraftId, request.DraftRevision, request.ExpectedDefinitionHeadVersionId, request.Version,
                    preflight.ReviewToken, request.IdempotencyKey, request.RequestedVersionId),
                cancellationToken);
            return receipt.Status == ActivityPublicationReceiptStatus.Applied
                ? ReusableActivityImportPublishOutcome.Applied
                : ReusableActivityImportPublishOutcome.Refused(Specific(receipt.ErrorCode, receipt.Diagnostics), receipt.Diagnostics);
        }
        catch (ActivityPublicationRejectedException rejection)
        {
            return ReusableActivityImportPublishOutcome.Refused(Specific(rejection.ErrorCode, rejection.Diagnostics), rejection.Diagnostics);
        }
        catch (Exception exception) when (Find<ActivityDataSelectionRejectedException>(exception) is { } rejection)
        {
            // The graph provider refused the imported manifest with located findings (#5850). They are what Studio's
            // publish endpoint reports for the same refusal, so the import reports them too, not "failed unexpectedly".
            var diagnostics = rejection.Findings.Select(finding => FromFinding(finding, rejection.ProviderKey, request)).ToArray();
            return ReusableActivityImportPublishOutcome.Refused(FirstErrorCode(diagnostics), diagnostics);
        }
        catch (Exception exception) when (Find<SchemaWriteRefusedException>(exception) is { } refusal)
        {
            // The store refused a write to a schema family: the value needs a version this host may not write yet, the family
            // is finalized at one this host cannot read, or the operation needs a dormant one (#5972). Studio's publish
            // endpoint answers it with its code, family and both versions; so does the import, not "failed unexpectedly".
            return ReusableActivityImportPublishOutcome.Refused(refusal.Code, [FromSchemaRefusal(refusal, request)]);
        }
    }

    /// <summary>
    /// The exception itself, or the one a compiler or store wrapped it in, following every exception an
    /// <see cref="AggregateException"/> holds, not only its first.
    /// </summary>
    private static T? Find<T>(Exception exception) where T : Exception => exception switch
    {
        T match => match,
        AggregateException aggregate => aggregate.InnerExceptions.Select(Find<T>).FirstOrDefault(found => found is not null),
        { InnerException: { } inner } => Find<T>(inner),
        _ => null
    };

    /// <summary>
    /// The refusal as one diagnostic carrying what <c>SchemaWriteRefusalProblem</c> gives every API: the code, the family and
    /// both versions, and for a dormancy refusal its feature and reason. Its message names the family and versions and,
    /// since the cause differs by refusal, what this host needs before it can write the family.
    /// </summary>
    private static ActivityDiagnostic FromSchemaRefusal(SchemaWriteRefusedException refusal, ReusableActivityImportPublishRequest request)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["family"] = refusal.Family,
            ["writeVersion"] = refusal.WriteVersion,
            ["requiredVersion"] = refusal.RequiredVersion
        };
        if (refusal is SchemaDormancyRefusedException dormancy)
        {
            if (dormancy.FeatureId is { } feature)
                metadata["feature"] = feature;
            metadata["reason"] = dormancy.Reason;
        }

        return new(
            refusal.Code,
            ActivityDiagnosticSeverity.Error,
            refusal.Message,
            new("ActivityDraft", request.DraftId, null, Revision: request.DraftRevision),
            Remediation: $"Nothing was published for this version; the imported rows are kept. This host refused to write schema " +
                         $"family '{refusal.Family}'; the message says why and what the host needs. Once that is resolved, upload " +
                         "the same export again and apply it under a new idempotency key; only the versions that are not yet " +
                         "published publish.",
            Metadata: metadata);
    }

    private static ActivityDiagnostic FromFinding(ValidationError finding, string? providerKey, ReusableActivityImportPublishRequest request)
    {
        // A finding's path is a JSON pointer into the provider manifest (/variables/0/initialValue) or an authored node
        // path ({NodeId}/inputs/{ReferenceKey}), as the publish endpoint locates it.
        const string inputMarker = "/inputs/";
        var input = finding.Path.LastIndexOf(inputMarker, StringComparison.Ordinal);
        var location = finding.Path.StartsWith('/')
            ? new ActivityDiagnosticLocation(ProviderKey: providerKey, JsonPointer: finding.Path)
            : new ActivityDiagnosticLocation(
                ProviderKey: providerKey,
                ReferenceKey: input >= 0 ? finding.Path[(input + inputMarker.Length)..] : null,
                NodeOrigin: [new ActivityNodeOrigin("AuthoredNode", input >= 0 ? finding.Path[..input] : finding.Path)]);
        return new(
            finding.Type,
            StringComparer.Ordinal.Equals(finding.Severity, ValidationSeverities.Warning) ? ActivityDiagnosticSeverity.Warning : ActivityDiagnosticSeverity.Error,
            finding.Message,
            new("ActivityDraft", request.DraftId, null, Revision: request.DraftRevision),
            location,
            "Elsa 4 refused the activity graph this import converted from the Elsa 3 workflow. The conversion should have refused it " +
            "with guidance or converted it to a graph Elsa 4 accepts, so report this finding with the Elsa 3 export.",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["authoredPath"] = finding.Path });
    }

    /// <summary>
    /// The publisher's code, or, when that is only the generic "publication invalid", the first error diagnostic's code
    /// that says why (such as <c>activity.version.bump-insufficient</c>), as a refused preflight reports it.
    /// </summary>
    private static string Specific(string? errorCode, IReadOnlyList<ActivityDiagnostic> diagnostics) =>
        errorCode is null || StringComparer.Ordinal.Equals(errorCode, ActivityErrorCodes.PublicationInvalid)
            ? FirstErrorCode(diagnostics)
            : errorCode;

    private static string FirstErrorCode(IReadOnlyList<ActivityDiagnostic> diagnostics) =>
        diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == ActivityDiagnosticSeverity.Error)?.Code
        ?? ActivityErrorCodes.PublicationInvalid;
}
