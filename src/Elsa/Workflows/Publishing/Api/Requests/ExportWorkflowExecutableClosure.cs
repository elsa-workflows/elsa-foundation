namespace Elsa.Workflows.Publishing.Api.Requests;

/// <summary>
/// FR-B-010a: export the portable executable closure for one Published workflow definition version.
/// </summary>
/// <remarks>
/// Route-bound only. There is deliberately no target selector: the GET route serves the <c>download</c> target
/// alone, because GET is a safe method and receipt-producing targets are external side effects a crawler, a retry
/// or a cache may repeat. A side-effecting target arrives with its own POST command surface and its own
/// idempotency contract.
/// </remarks>
/// <param name="VersionId">The workflow definition version whose closure is exported.</param>
public sealed record ExportWorkflowExecutableClosure(string VersionId);
