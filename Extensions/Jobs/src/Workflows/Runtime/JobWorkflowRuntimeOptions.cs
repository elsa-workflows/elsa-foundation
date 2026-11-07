using Elsa.Jobs.Core;

namespace Elsa.Jobs.Workflows.Runtime;

/// <summary>Trusted native-adapter host settings; the delivery loop has no handler slots or execution authority.</summary>
public sealed class JobWorkflowRuntimeOptions
{
    public bool EnableResultDelivery { get; set; }
    public IList<JobScope> DeliveryScopes { get; } = new List<JobScope>();
    public int DeliveryPageSize { get; set; } = 32;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

internal sealed record JobWorkflowRuntimeSettings(bool EnableResultDelivery, IReadOnlyList<JobScope> Scopes,
    int PageSize, TimeSpan PollInterval, TimeSpan OperationTimeout)
{
    internal static JobWorkflowRuntimeSettings Capture(JobWorkflowRuntimeOptions options)
    {
        if (options.DeliveryPageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(options.DeliveryPageSize));
        if (options.PollInterval < TimeSpan.FromMilliseconds(10) || options.PollInterval > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(options.PollInterval));
        if (options.OperationTimeout < TimeSpan.FromMilliseconds(100) || options.OperationTimeout > TimeSpan.FromMinutes(10)) throw new ArgumentOutOfRangeException(nameof(options.OperationTimeout));
        var scopes = options.DeliveryScopes.ToArray();
        foreach (var scope in scopes) Services.JobsNativeBoundary.Scope(scope);
        if (scopes.Distinct().Count() != scopes.Length || options.EnableResultDelivery && scopes.Length == 0)
            throw new ArgumentException("Jobs result delivery requires distinct explicit trusted scopes.", nameof(options));
        return new(options.EnableResultDelivery, Array.AsReadOnly(scopes), options.DeliveryPageSize, options.PollInterval, options.OperationTimeout);
    }
}
