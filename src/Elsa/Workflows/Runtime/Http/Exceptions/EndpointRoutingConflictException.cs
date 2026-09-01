using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Http.Exceptions;

/// <summary>
/// Thrown at publish time when two authoritative publication slots claim the same HTTP endpoint
/// <c>(template, method)</c> pair (spec 089 follow-up, issue #592 item 2). Raised by
/// <c>HttpEndpointRoutingUniquenessValidator</c> on the trigger indexer's pre-write validation seam
/// (<c>IWorkflowTriggerIndexValidator</c>), so the <em>second</em> publish of a conflicting endpoint fails
/// with the durable index untouched — rather than the collision persisting and only appearing as a
/// request-time 409.
/// </summary>
public sealed class EndpointRoutingConflictException(
    string endpoint,
    string? candidatePublicationId = null,
    string? candidateSlotId = null,
    string? conflictingPublicationId = null,
    string? conflictingSlotId = null)
    : Exception(BuildMessage(endpoint, candidatePublicationId, candidateSlotId, conflictingPublicationId, conflictingSlotId))
{
    /// <summary>The conflicting endpoint, formatted as <c>METHOD template</c> (e.g. <c>GET orders/{id}</c>).</summary>
    public string Endpoint { get; } = endpoint;
    public string? CandidatePublicationId { get; } = candidatePublicationId;
    public string? CandidateSlotId { get; } = candidateSlotId;
    public string? ConflictingPublicationId { get; } = conflictingPublicationId;
    public string? ConflictingSlotId { get; } = conflictingSlotId;

    /// <summary>Builds the exception with both candidate and authoritative claimant diagnostics.</summary>
    public static EndpointRoutingConflictException ForBindings(
        WorkflowTriggerBinding candidate,
        WorkflowTriggerBinding conflicting) =>
        new(
            DescribeEndpoint(candidate),
            candidate.ActivationId,
            candidate.SlotId,
            conflicting.ActivationId,
            conflicting.SlotId);

    /// <summary>Formats a binding's endpoint as <c>METHOD template</c> from its routing metadata (unknowns called out).</summary>
    public static string DescribeEndpoint(WorkflowTriggerBinding binding)
    {
        var template = binding.Metadata.GetValueOrDefault(HttpEndpointRouting.TemplateMetadataKey, "(unknown template)");
        var method = binding.Metadata.GetValueOrDefault(HttpEndpointRouting.MethodMetadataKey, "(unknown method)");
        return $"{method.ToUpperInvariant()} {template}";
    }

    private static string BuildMessage(
        string endpoint,
        string? candidatePublicationId,
        string? candidateSlotId,
        string? conflictingPublicationId,
        string? conflictingSlotId)
    {
        var message = $"More than one authoritative publication claims the HTTP endpoint '{endpoint}'. A (template, method) pair is Exclusive.";
        if (candidatePublicationId is null && conflictingPublicationId is null)
            return message;

        return message +
               $" Candidate publication '{candidatePublicationId ?? "(legacy)"}' in slot '{candidateSlotId ?? "(legacy)"}'" +
               $" conflicts with publication '{conflictingPublicationId ?? "(legacy)"}' in slot '{conflictingSlotId ?? "(legacy)"}'.";
    }
}
