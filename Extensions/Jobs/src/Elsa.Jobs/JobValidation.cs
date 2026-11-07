using System.Text.Json;
using Elsa.Jobs.Core;

namespace Elsa.Jobs;

internal static class JobValidation
{
    internal static void Identity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
            throw new JobException("jobs.identity.invalid", "A Jobs identity must contain 1 through 256 non-control characters.");
    }

    internal static void Scope(JobScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Identity(scope.PersistencePartition);
        if (scope.TenantId is not null) Identity(scope.TenantId);
    }

    internal static void Schema(JobSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        Identity(schema.Alias);
        if (schema.Version < 1) throw new JobException("jobs.schema.invalid", "A Jobs schema requires a positive version.");
    }

    internal static void Support(JobHandlerSupport support)
    {
        ArgumentNullException.ThrowIfNull(support);
        ArgumentNullException.ThrowIfNull(support.Handler);
        Identity(support.Handler.JobType);
        Identity(support.Handler.Version);
        Identity(support.ImplementationIdentity);
        Schema(support.InputSchema);
        Schema(support.ResultSchema);
    }

    internal static void Payload(JobPayload payload, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Schema(payload.Schema);
        if (payload.Value.ValueKind == JsonValueKind.Undefined || JobPayloadLimits.GetByteCount(payload) > maximumBytes)
            throw new JobException("jobs.payload.invalid", "The complete Jobs schema/value envelope is undefined or exceeds the configured byte bound.");
    }

    internal static void Request(JobRequest request, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Handler);
        Identity(request.Handler.JobType);
        Identity(request.Handler.Version);
        Identity(request.Queue);
        Payload(request.Input, maximumBytes);
        Schema(request.ResultSchema);
        ArgumentNullException.ThrowIfNull(request.Policy);
        ArgumentNullException.ThrowIfNull(request.Policy.Retry);
        var retry = request.Policy.Retry;
        if (retry.MaximumAttempts is < 1 or > 1000 || retry.InitialBackoff < TimeSpan.Zero ||
            retry.MaximumBackoff < retry.InitialBackoff || retry.MaximumBackoff > TimeSpan.FromDays(365) ||
            !double.IsFinite(retry.BackoffMultiplier) || retry.BackoffMultiplier < 1 ||
            !double.IsFinite(retry.JitterFraction) || retry.JitterFraction is < 0 or > 1 ||
            request.Policy.AttemptTimeout <= TimeSpan.Zero || request.Policy.AttemptTimeout > TimeSpan.FromDays(365) ||
            request.Policy.MinimumKeyRecognition <= TimeSpan.Zero || request.Policy.MinimumKeyRecognition > TimeSpan.FromDays(3650))
            throw new JobException("jobs.policy.invalid", "The Jobs execution policy exceeds its supported bounds.");
    }

    internal static void Progress(JobProgress progress, JobRuntimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (progress.Fraction is { } fraction && (!double.IsFinite(fraction) || fraction is < 0 or > 1) ||
            progress.Message?.Length > 1024)
            throw new JobException("jobs.progress.invalid", "The Jobs progress fraction or message exceeds its supported bounds.");
        if (progress.Data is { } data) Payload(data, settings.MaximumProgressBytes);
        int byteCount;
        try { byteCount = JsonSerializer.SerializeToUtf8Bytes(progress).Length; }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new JobException("jobs.progress.invalid", "The Jobs progress cannot be represented by its supported durable JSON format.", error);
        }
        if (byteCount > settings.MaximumProgressBytes)
            throw new JobException("jobs.progress.invalid", "The Jobs progress exceeds the configured byte bound.");
    }
}
