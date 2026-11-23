using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Diagnostics;
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
