namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Shell HTTP settings contributed by the feature that owns workflow HTTP endpoints.</summary>
public sealed class WorkflowHttpEndpointSettings(string basePath, TimeSpan syncRequestTimeout, long maxRequestBodyBytes)
{
    public string BasePath { get; } = basePath;
    public TimeSpan SyncRequestTimeout { get; } = syncRequestTimeout;
    public long MaxRequestBodyBytes { get; } = maxRequestBodyBytes;
}
