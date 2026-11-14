using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>Uses the route manifest's equivalence rule without changing persisted endpoint stimulus hashes.</summary>
internal sealed class HttpIngressReservationPolicy : IIngressReservationPolicy
{
    public string StimulusType => HttpEndpointRouting.StimulusType;
    public string TriggerTypeKey => "http";

    public bool Conflicts(RuntimeIngressReservationDescriptor first, RuntimeIngressReservationDescriptor second)
    {
        var left = Read(first); var right = Read(second);
        return left.Route == right.Route && (left.Method == "*" || right.Method == "*" || left.Method == right.Method);
    }

    private static TriggerOperationException Unavailable(string message, Exception? cause = null) =>
        new("unavailable", message, innerException: cause ?? new InvalidDataException(message));

    private static (string Route, string Method) Read(RuntimeIngressReservationDescriptor claim)
    {
        if (claim.StimulusType != HttpEndpointRouting.StimulusType ||
            !claim.Metadata.TryGetValue(HttpEndpointRouting.TemplateMetadataKey, out var route) || string.IsNullOrWhiteSpace(route))
            throw Unavailable("An HTTP reservation has no valid original route metadata.");
        try { _ = RoutePatternFactory.Parse(route); }
        catch (RoutePatternException exception) { throw Unavailable("An HTTP reservation has invalid route syntax.", exception); }
        var method = "*"; // Missing method metadata is the existing legacy wildcard shape.
        if (claim.Metadata.TryGetValue(HttpEndpointRouting.MethodMetadataKey, out var value))
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(character => !char.IsAsciiLetterOrDigit(character) &&
                    !"!#$%&'*+-.^_`|~".Contains(character)))
                throw Unavailable("An HTTP reservation has invalid method metadata.");
            method = value.ToUpperInvariant();
        }
        return (RouteTemplateIdentity.Canonicalize(route), method);
    }
}
