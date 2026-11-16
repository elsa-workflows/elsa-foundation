using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Api.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

/// <summary>Preserves located source-admission findings through compiler exception wrappers.</summary>
internal static class ActivityDataSelectionPublicationProblems
{
    internal static bool TryFind(Exception exception, out ActivityDataSelectionRejectedException refusal)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is ActivityDataSelectionRejectedException found)
            {
                refusal = found;
                return true;
            }
        refusal = null!;
        return false;
    }

    internal static ActivityPublishingProblemDetails Create(ActivityDataSelectionRejectedException refusal, HttpContext context) => new(
        "https://elsa.dev/problems/activity-data.invalid",
        "Activity data references were rejected",
        StatusCodes.Status400BadRequest,
        refusal.Message,
        context.Request.Path.Value ?? string.Empty,
        "activity-data.invalid",
        context.TraceIdentifier,
        refusal.Findings.Select(finding => new ActivityPublishingDiagnosticView(
            finding.Type, finding.Severity, finding.Message,
            new ActivityDiagnosticSubject(refusal.ResourceKind ?? "ActivityData", refusal.ResourceId ?? string.Empty),
            Location(finding.Path, refusal.ProviderKey), null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["authoredPath"] = finding.Path })
            { AuthoredPath = finding.Path }).ToArray());

    internal static ActivityDiagnosticLocation Location(string path, string? providerKey)
    {
        const string inputMarker = "/inputs/";
        var input = path.LastIndexOf(inputMarker, StringComparison.Ordinal);
        var nodeId = input >= 0 ? path[..input] : path;
        var key = input >= 0 ? path[(input + inputMarker.Length)..] : null;
        return new(ProviderKey: providerKey, ReferenceKey: key,
            NodeOrigin: [new ActivityNodeOrigin("AuthoredNode", nodeId)]);
    }
}
