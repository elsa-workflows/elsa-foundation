using Microsoft.AspNetCore.Http;

namespace Elsa.Api.AspNetCore;

/// <summary>Shell HTTP settings and timeout response policy contributed by the workflow endpoint feature.</summary>
public sealed class WorkflowHttpEndpointSettings(string basePath, TimeSpan syncRequestTimeout, long maxRequestBodyBytes,
    Func<HttpContext, Exception, CancellationToken, ValueTask> handleTimeoutAsync)
{
    public string BasePath { get; } = basePath;
    public TimeSpan SyncRequestTimeout { get; } = syncRequestTimeout;
    public long MaxRequestBodyBytes { get; } = maxRequestBodyBytes;
    public Func<HttpContext, Exception, CancellationToken, ValueTask> HandleTimeoutAsync { get; } = handleTimeoutAsync;
}
