using System.Text;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

/// <summary>
/// <c>GET publishing/workflows/{versionId}/executable-export</c> — the portable closure for one Published workflow
/// definition version, returned inline as a download (FR-B-010a).
/// </summary>
/// <remarks>
/// <para>
/// <b>The GET route binds to the <c>download</c> target only.</b> GET is a safe method; receipt-producing targets
/// (a folder writer, a blob push) are external side effects that crawlers, retries and caches may repeat. There is
/// no target selector in v1 — a side-effecting target arrives with its own POST command surface carrying an
/// explicit idempotency contract.
/// </para>
/// <para>
/// <b>Producing the closure and deciding where it goes stay separate.</b> The factory is destination-agnostic and
/// the target owns encoding, so this endpoint is only the HTTP binding: it selects the safe target, maps the
/// factory's exception taxonomy onto status codes, and names the file.
/// </para>
/// </remarks>
internal sealed class ExportWorkflowExecutableClosureEndpoint(
    IWorkflowArtifactClosureFactory closureFactory,
    IEnumerable<IWorkflowArtifactExportTarget> exportTargets,
    ILogger<ExportWorkflowExecutableClosureEndpoint> logger)
    : ElsaEndpoint<ExportWorkflowExecutableClosure>
{
    /// <summary>Longest a single interpolated filename segment may be before it is truncated.</summary>
    private const int MaximumFileNameSegmentLength = 96;

    public override void Configure()
    {
        Get(RouteConstants.WorkflowExecutableExport);
        // Deliberately not a new permission. Executable content is already readable under this family
        // (GetWorkflowExecutableInputSourcesEndpoint serves it under the same constant), so export differs by
        // bundling the transitive closure into one response — convenience of retrieval, not a capability boundary.
        // If the executable-inspection endpoints are ever tightened, this must be tightened with them.
        ConfigurePermissions(PermissionNames.WorkflowPublishingRead);
    }

    public override async Task HandleAsync(ExportWorkflowExecutableClosure request, CancellationToken cancellationToken)
    {
        // The route constraint is `.+`, so a blank-but-present segment ("%20") reaches here. It names no version,
        // which is the 404 case rather than a raw ArgumentException out of the factory.
        if (string.IsNullOrWhiteSpace(request.VersionId))
        {
            ThrowError("Cannot export a workflow definition version: no version was named.", StatusCodes.Status404NotFound);
            return;
        }

        var target = exportTargets.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.TargetId, DownloadWorkflowArtifactExportTarget.Id));
        if (target is null)
        {
            logger.LogError(
                "No export target named '{TargetId}' is registered; the publishing API feature must contribute it.",
                DownloadWorkflowArtifactExportTarget.Id);
            ThrowError("Workflow artifact export is not available on this engine.", StatusCodes.Status500InternalServerError);
            return;
        }

        WorkflowArtifactClosure closure;
        WorkflowArtifactExportDelivery delivery;
        try
        {
            closure = await closureFactory.CreateAsync(request.VersionId, cancellationToken);
            delivery = await DeliverAsync(target, closure, request.VersionId, cancellationToken);
        }
        catch (WorkflowArtifactClosureSourceNotFoundException exception)
        {
            ThrowError(exception.Message, StatusCodes.Status404NotFound);
            return;
        }
        catch (WorkflowArtifactClosureNotPublishedException exception)
        {
            // Distinct from "unknown": the version exists, it was simply never published, and FR-B-011 forbids
            // exporting the expiring TestRun snapshot it does have.
            ThrowError(exception.Message, StatusCodes.Status409Conflict);
            return;
        }
        catch (IncompleteWorkflowArtifactClosureException exception)
        {
            // Every unresolved id is named as its own error entry, so a client renders "these are missing" without
            // parsing the summary sentence.
            foreach (var artifactId in exception.MissingArtifactIds)
                AddError($"Dependency artifact '{artifactId}' is missing from the executable store.");

            ThrowError(exception.Message, StatusCodes.Status409Conflict);
            return;
        }
        catch (WorkflowArtifactClosureCycleException exception)
        {
            // Store corruption, not a client mistake: no content-addressed compiler can form a back edge.
            logger.LogError(exception, "Corrupt executable dependency graph while exporting '{VersionId}'.", request.VersionId);
            ThrowError(exception.Message, StatusCodes.Status500InternalServerError);
            return;
        }
        catch (WorkflowArtifactClosureException exception)
        {
            // The storage member of the family, plus any future one: an answer about the engine, never the caller.
            // §2.23.5 already wrapped the provider's own exception, so the inner detail stays in the log.
            logger.LogError(exception, "Workflow artifact export failed for '{VersionId}'.", request.VersionId);
            ThrowError(exception.Message, StatusCodes.Status500InternalServerError);
            return;
        }

        if (delivery.Kind != WorkflowArtifactExportDeliveryKind.InlinePayload || delivery.Payload is not { } payload)
        {
            // A receipt here would mean a side-effecting target answered a safe method, which is the one thing
            // this route exists to prevent.
            logger.LogError(
                "Export target '{TargetId}' returned a '{Kind}' delivery; this route serves inline payloads only.",
                delivery.TargetId,
                delivery.Kind);
            ThrowError("Workflow artifact export produced an unexpected delivery.", StatusCodes.Status500InternalServerError);
            return;
        }

        // No FastEndpoints byte-download precedent exists in the repo, so the header is written explicitly. The
        // safe-name rules below reduce both interpolated segments to [A-Za-z0-9._-], which is why quoting the
        // value here cannot be escaped out of.
        HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{CreateFileName(closure)}\"";
        await Send.StringAsync(
            Encoding.UTF8.GetString(payload.Span),
            StatusCodes.Status200OK,
            "application/json",
            cancellationToken);
    }

    /// <summary>
    /// Runs the delivery, wrapping the codec's <c>JsonException</c> per §2.23.5 — this boundary owns the version id
    /// the codec deliberately does not, which is exactly why the codec leaves the wrap to its caller.
    /// </summary>
    private static async Task<WorkflowArtifactExportDelivery> DeliverAsync(
        IWorkflowArtifactExportTarget target,
        WorkflowArtifactClosure closure,
        string versionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await target.DeliverAsync(closure, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or WorkflowArtifactClosureException))
        {
            throw new WorkflowArtifactClosureStorageException(
                versionId,
                $"encode the exported closure envelope through target '{target.TargetId}'",
                exception);
        }
    }

    /// <summary>
    /// The download name shared with elsa-foundation-studio#493: <c>{definitionId}-{artifactVersion}-closure.json</c>.
    /// </summary>
    /// <remarks>
    /// Both interpolated segments come from stored artifact identity and land in a response header this endpoint
    /// writes by hand, so both are reduced to a conservative filename alphabet first. A definition id carrying a
    /// quote, a path separator or a newline would otherwise be echoed straight onto the wire.
    /// </remarks>
    internal static string CreateFileName(WorkflowArtifactClosure closure)
    {
        ArgumentNullException.ThrowIfNull(closure);

        var root = closure.Artifacts.FirstOrDefault(artifact =>
            StringComparer.Ordinal.Equals(artifact.Identity.ArtifactId, closure.RootArtifactId));
        var definitionId = SafeNameSegment(root?.Identity.DefinitionId, "workflow");
        var artifactVersion = SafeNameSegment(root?.Identity.ArtifactVersion, "unversioned");
        return $"{definitionId}-{artifactVersion}-closure.json";
    }

    private static string SafeNameSegment(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var safe = char.IsAsciiLetterOrDigit(character) || character is '.' or '_' ? character : '-';

            // Collapse runs of substituted characters so a segment of separators cannot pad the name out.
            if (safe == '-' && builder.Length > 0 && builder[^1] == '-')
                continue;

            builder.Append(safe);
        }

        // Leading/trailing dots would produce a hidden file or a relative-path lookalike; leading/trailing dashes
        // would double up against the literal separators in the template.
        var sanitized = builder.ToString().Trim('-', '.');
        if (sanitized.Length > MaximumFileNameSegmentLength)
            sanitized = sanitized[..MaximumFileNameSegmentLength].TrimEnd('-', '.');

        return sanitized.Length == 0 ? fallback : sanitized;
    }
}
