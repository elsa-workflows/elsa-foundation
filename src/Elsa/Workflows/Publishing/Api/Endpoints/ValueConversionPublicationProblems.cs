using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Services;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

/// <summary>
/// Maps a <see cref="ValueConversionPublicationException"/> raised during workflow publish/preflight into the
/// same RFC 7807 <c>application/problem+json</c> shape used for activity publication. The stable diagnostic
/// <see cref="Code"/> is <c>VF-COER-001</c>; the structured binding context (node id, reference key, source and
/// target contracts, representation, mode, profile, reason code) is carried on the single diagnostic so clients
/// never have to reparse it from the message string.
/// </summary>
internal static class ValueConversionPublicationProblems
{
    public const string Code = "VF-COER-001";

    /// <summary>
    /// Walks the exception chain looking for a conversion rejection. Publish and preflight surface these wrapped
    /// in a <c>WorkflowExecutableCompilationException</c>, so the inner-exception chain must be inspected.
    /// </summary>
    public static bool TryFind(Exception exception, out ValueConversionPublicationException conversion)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ValueConversionPublicationException match)
            {
                conversion = match;
                return true;
            }
        }

        conversion = null!;
        return false;
    }

    public static ActivityPublishingProblemDetails Create(
        ValueConversionPublicationException exception,
        HttpContext context,
        string? workflowVersionId)
    {
        var binding = exception.Binding;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["reasonCode"] = exception.ReasonCode.ToString(),
            ["mode"] = exception.Mode.ToString(),
            ["targetType"] = ValueConversionPublicationException.DescribeContract(exception.TargetType)
        };

        if (exception.SourceType is { } sourceType)
            metadata["sourceType"] = ValueConversionPublicationException.DescribeContract(sourceType);
        if (exception.SourceRepresentation is { } sourceRepresentation)
            metadata["sourceRepresentation"] = sourceRepresentation.ToString();
        if (exception.Profile is { } profile)
        {
            metadata["profileId"] = profile.Id;
            metadata["profileVersion"] = profile.Version;
        }

        if (binding is not null)
        {
            metadata["nodeId"] = binding.NodeId;
            metadata["referenceKey"] = binding.ReferenceKey;
            metadata["bindingKind"] = binding.Kind.ToString();
        }

        if (workflowVersionId is not null)
            metadata["workflowVersionId"] = workflowVersionId;

        var subject = new ActivityDiagnosticSubject(
            Kind: binding is null ? "ValueConversion" : binding.Kind.ToString(),
            Id: binding?.NodeId ?? workflowVersionId ?? string.Empty,
            VersionId: workflowVersionId);

        var location = binding is null
            ? null
            : new ActivityDiagnosticLocation(ReferenceKey: binding.ReferenceKey);

        var diagnostic = new ActivityPublishingDiagnosticView(
            Code,
            ActivityDiagnosticSeverity.Error.ToString(),
            exception.Message,
            subject,
            location,
            exception.Reason,
            metadata);

        return new ActivityPublishingProblemDetails(
            $"https://elsa.dev/problems/{Code}",
            "Workflow publication conversion was rejected",
            StatusCodes.Status400BadRequest,
            exception.Message,
            context.Request.Path.Value ?? string.Empty,
            Code,
            context.TraceIdentifier,
            [diagnostic]);
    }

    /// <summary>Writes the problem payload, reusing the shared activity-publishing serializer and content type.</summary>
    public static Task WriteAsync(
        HttpResponse response,
        ActivityPublishingProblemDetails problem,
        CancellationToken cancellationToken) =>
        ActivityPublishingProblems.WriteAsync(response, problem, cancellationToken);
}
