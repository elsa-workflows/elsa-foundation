using Elsa.Activities.Design.Core.Models;

namespace Elsa3.Activities.Design.Import.Contracts;

/// <summary>
/// Publishes one imported reusable activity version from its draft through the host's regular publish path (spec 246
/// R-1). A replacement contract with no default: the <c>Elsa3ImportActivitiesPublishing</c> feature implements it over
/// the Workflows Publishing activity publisher, so the import itself never references Publishing. Without an
/// implementation, applying an import that selects a reusable version is refused before anything is written.
/// </summary>
public interface IReusableActivityImportPublisher
{
    /// <summary>
    /// Preflights and publishes the draft. A refusal is returned as an outcome, never thrown; infrastructure failures
    /// propagate.
    /// </summary>
    ValueTask<ReusableActivityImportPublishOutcome> PublishAsync(
        ReusableActivityImportPublishRequest request,
        CancellationToken cancellationToken = default);
}

/// <param name="Version">The SemVer to publish, <c>{n}.0.0</c> for Elsa 3 version n.</param>
/// <param name="RequestedVersionId">The plan's deterministic version id, which wrappers and callers already reference.</param>
/// <param name="IdempotencyKey">Deterministic per receipt and version, so a resumed apply never publishes twice.</param>
/// <param name="TenantId">The import's tenant; the publisher must resolve the same one.</param>
public sealed record ReusableActivityImportPublishRequest(
    string DraftId,
    long DraftRevision,
    string? ExpectedDefinitionHeadVersionId,
    string Version,
    string RequestedVersionId,
    string IdempotencyKey,
    string? TenantId);

public sealed record ReusableActivityImportPublishOutcome(
    bool Published,
    string? ErrorCode,
    IReadOnlyList<ActivityDiagnostic> Diagnostics)
{
    public static ReusableActivityImportPublishOutcome Applied { get; } = new(true, null, []);

    public static ReusableActivityImportPublishOutcome Refused(string errorCode, IReadOnlyList<ActivityDiagnostic> diagnostics) =>
        new(false, errorCode, diagnostics);
}
