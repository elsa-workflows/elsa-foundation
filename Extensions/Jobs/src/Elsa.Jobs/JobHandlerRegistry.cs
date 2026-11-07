using System.Text.Json;
using Elsa.Jobs.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Jobs;

internal sealed class JobHandlerRegistry
{
    private readonly IReadOnlyList<JobHandlerRegistration> _registrations;

    public JobHandlerRegistry(IEnumerable<JobHandlerRegistration> registrations)
    {
        var entries = registrations.ToArray();
        foreach (var entry in entries) JobValidation.Support(entry.Support);
        if (entries.GroupBy(entry => entry.Support.Handler).Any(group => group.Count() != 1))
            throw new JobException("jobs.handler.duplicate", "A Jobs handler identity has multiple registered contributions.");
        _registrations = Array.AsReadOnly(entries);
        Support = Array.AsReadOnly(entries.Select(entry => entry.Support).ToArray());
    }

    internal IReadOnlyList<JobHandlerSupport> Support { get; }

    internal JobHandlerRegistration Resolve(JobRequest request, string implementationIdentity) =>
        _registrations.SingleOrDefault(entry => entry.Support.Handler == request.Handler &&
            entry.Support.InputSchema == request.Input.Schema && entry.Support.ResultSchema == request.ResultSchema &&
            StringComparer.Ordinal.Equals(entry.Support.ImplementationIdentity, implementationIdentity))
        ?? throw new JobException("jobs.handler.unavailable", "The exact pinned handler and schemas are unavailable on this worker.");
}

internal abstract class JobHandlerRegistration(JobHandlerSupport support)
{
    internal JobHandlerSupport Support { get; } = support;
    internal abstract ValueTask<JobPayload> InvokeAsync(IServiceProvider services, JobPayload input,
        JobExecutionContext context, JobRuntimeSettings settings, CancellationToken cancellationToken);
}

internal sealed class JobHandlerRegistration<THandler, TInput, TResult>(JobHandlerSupport support) : JobHandlerRegistration(support)
    where THandler : class, IJobHandler<TInput, TResult>
{
    internal override async ValueTask<JobPayload> InvokeAsync(IServiceProvider services, JobPayload input,
        JobExecutionContext context, JobRuntimeSettings settings, CancellationToken cancellationToken)
    {
        TInput? value;
        try
        {
            value = input.Value.Deserialize<TInput>(settings.JsonSerializerOptions);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new JobPayloadException("jobs.input.invalid", "The retained Jobs input cannot be read by its pinned handler.", error);
        }
        var handler = services.GetRequiredService<THandler>();
        var result = await handler.ExecuteAsync(value!, context, cancellationToken);
        try
        {
            var payload = new JobPayload(Support.ResultSchema, JsonSerializer.SerializeToElement(result, settings.JsonSerializerOptions));
            JobValidation.Payload(payload, settings.MaximumPayloadBytes);
            return payload;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or JobException)
        {
            throw new JobPayloadException("jobs.result.invalid", "The Jobs result cannot be retained within its declared schema and bounds.", error);
        }
    }
}

internal sealed class JobPayloadException(string code, string message, Exception innerException) : JobException(code, message, innerException);
