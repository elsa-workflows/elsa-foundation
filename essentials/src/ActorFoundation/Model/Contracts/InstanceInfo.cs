using System.Text.Json.Serialization;
using Kernel.Foundation.Contracts;

namespace Kernel.Model.Contracts;

/// <summary>The workflow identity and model data visible to an execution model.</summary>
/// <param name="Workflow">The workflow the instance runs: on a publishing host, the published artifact.</param>
public sealed record InstanceInfo(ActorRef Ref, string Tenant, string Model, string Workflow, ActorRef? Parent)
{
    public string Tenant { get; } = Tenant;

    /// <summary>The run's logical definition when the start records one separately from the artifact.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Definition { get; init; }

    /// <summary>The publishing host's optional exact executable identity for this run.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutableSelection { get; init; }

    /// <summary>The exact isolated authoring scope when the workflow host admitted a Test Run.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TestScopeId { get; init; }

    /// <summary>The authenticated subject recorded by the start boundary, in its ordinary application tenant.</summary>
    [JsonIgnore]
    public WorkflowExecutionSubject? Subject { get; init; }
}

/// <summary>Authenticated execution attribution for model operations that must recheck a current grant.</summary>
public sealed record WorkflowExecutionSubject(string Tenant, string SubjectId);
