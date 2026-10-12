namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Capability-limited staging surface supplied while an incident-resolution action executes.</summary>
public abstract class IncidentResolutionActionContext
{
    /// <summary>Keeps the target incident blocking.</summary>
    public abstract void KeepIncidentBlocking();

    /// <summary>Makes the target incident open while preserving the containing workflow lifecycle.</summary>
    public abstract void OpenIncident();

    /// <summary>Resolves the target incident with the action's durable classification.</summary>
    public abstract void ResolveIncident();

    /// <summary>Requests that the containing workflow be faulted.</summary>
    public abstract void RequestWorkflowFault();

    /// <summary>Adds one bounded, safe outcome metadata pair.</summary>
    public abstract void AddMetadata(string key, string value);

    /// <summary>Requests one explicitly registered, strategy-safe post-commit intent.</summary>
    public abstract void AddPostCommitIntent(IncidentStrategySafePostCommitIntent intent);
}
