using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Core.Extensions;

/// <summary>Registers trusted scoped workflow-alteration handler contributions.</summary>
public static class WorkflowAlterationServiceCollectionExtensions
{
    /// <summary>
    /// Adds one exact schema handler. Built-in names are reserved for Runtime-owned handlers; custom kinds must be
    /// dotted/namespaced so persisted envelopes never depend on a deployment-local CLR type name.
    /// </summary>
    public static IServiceCollection AddWorkflowAlterationHandler<THandler>(this IServiceCollection services, WorkflowAlterationDescriptor descriptor)
        where THandler : class, IWorkflowAlterationPreflightHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateCustomDescriptor(descriptor);

        var contribution = new WorkflowAlterationHandlerContribution(descriptor, typeof(THandler));
        var sameDescriptor = services
            .Where(item => item.ServiceType == typeof(WorkflowAlterationHandlerContribution))
            .Select(item => item.ImplementationInstance)
            .OfType<WorkflowAlterationHandlerContribution>()
            .FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Descriptor.Kind, descriptor.Kind) && item.Descriptor.SchemaVersion == descriptor.SchemaVersion);
        if (sameDescriptor is not null)
        {
            if (sameDescriptor.HandlerType == typeof(THandler) && sameDescriptor.Descriptor == descriptor)
                return services;
            throw new InvalidOperationException($"Workflow alteration '{descriptor.Kind}/{descriptor.SchemaVersion}' is already registered by '{sameDescriptor.HandlerType.FullName}'.");
        }

        services.TryAddScoped<THandler>();
        services.AddSingleton(contribution);
        return services;
    }

    private static void ValidateCustomDescriptor(WorkflowAlterationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Kind is "CancelWorkflow" or "ModifyVariable" or "ScheduleActivity" or "RescheduleActivity" or "Migrate")
            throw new ArgumentException($"'{descriptor.Kind}' is reserved for a Runtime-owned alteration.", nameof(descriptor));
        if (!descriptor.Kind.Contains('.', StringComparison.Ordinal) || descriptor.Kind.Split('.').Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Custom workflow alteration kinds must be dotted and namespaced.", nameof(descriptor));
    }
}
