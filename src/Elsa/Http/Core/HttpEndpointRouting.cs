namespace Elsa.Http.Core;

/// <summary>
/// The shared identity vocabulary of workflow HTTP endpoint routing (spec 089 B). Lives in the lower HTTP
/// contracts package because two co-equal modules speak it: <c>Elsa.Activities.Http</c> writes these values
/// when the trigger provider describes an endpoint's stimuli, and <c>Elsa.Workflows.Runtime.Http</c> reads
/// them from trigger-binding metadata to maintain the per-shell route table. Neither module references the
/// other, so the vocabulary sits below both.
/// </summary>
public static class HttpEndpointRouting
{
    /// <summary>The stimulus type shared by every HTTP endpoint trigger binding and bookmark.</summary>
    public const string StimulusType = "HttpEndpoint";

    /// <summary>Trigger-binding metadata key carrying the endpoint's normalized route template (e.g. <c>orders/{id}</c>).</summary>
    public const string TemplateMetadataKey = "http:template";

    /// <summary>Trigger-binding metadata key carrying the lowercased HTTP method of this binding (one binding per method).</summary>
    public const string MethodMetadataKey = "http:method";
}
