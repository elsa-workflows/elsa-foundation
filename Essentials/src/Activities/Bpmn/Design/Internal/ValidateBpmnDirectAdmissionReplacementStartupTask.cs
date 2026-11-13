using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Rejects competing owners of the optional Direct assessment before draft validation runs.</summary>
[Order(-100)]
internal sealed class ValidateBpmnDirectAdmissionReplacementStartupTask(IServiceCollection services) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = services.Count(descriptor => descriptor.ServiceType == typeof(IBpmnDocumentDirectAdmission));
        if (count > 1)
            throw new InvalidOperationException($"IBpmnDocumentDirectAdmission is an optional replacement contract and requires at most one implementation ({count} registrations). Register a replacement before the feature default or use services.Replace(...).");
        return Task.CompletedTask;
    }
}
