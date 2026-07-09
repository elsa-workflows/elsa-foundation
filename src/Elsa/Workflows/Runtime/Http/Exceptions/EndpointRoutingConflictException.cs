using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Http.Exceptions;

/// <summary>
/// Thrown at publish time when two distinct workflow definitions claim the same HTTP endpoint
/// <c>(template, method)</c> pair (spec 089 follow-up, issue #592 item 2). Raised by
/// <c>HttpEndpointRoutingUniquenessValidator</c> on the trigger indexer's pre-write validation seam
/// (<c>IWorkflowTriggerIndexValidator</c>), so the <em>second</em> publish of a conflicting endpoint fails
/// with the durable index untouched — rather than the collision persisting and only appearing as a
/// request-time 409.
/// </summary>
public sealed class EndpointRoutingConflictException(string endpoint)
    : Exception($"More than one workflow definition claims the HTTP endpoint '{endpoint}'. A (template, method) pair must be unique across definitions.")
{
    /// <summary>The conflicting endpoint, formatted as <c>METHOD template</c> (e.g. <c>GET orders/{id}</c>).</summary>
    public string Endpoint { get; } = endpoint;

    /// <summary>Builds the exception for a conflicting binding, describing its endpoint from the routing metadata.</summary>
    public static EndpointRoutingConflictException ForBinding(WorkflowTriggerBinding binding) => new(DescribeEndpoint(binding));

    /// <summary>Formats a binding's endpoint as <c>METHOD template</c> from its routing metadata (unknowns called out).</summary>
    public static string DescribeEndpoint(WorkflowTriggerBinding binding)
    {
        var template = binding.Metadata.GetValueOrDefault(HttpEndpointRouting.TemplateMetadataKey, "(unknown template)");
        var method = binding.Metadata.GetValueOrDefault(HttpEndpointRouting.MethodMetadataKey, "(unknown method)");
        return $"{method.ToUpperInvariant()} {template}";
    }
}
