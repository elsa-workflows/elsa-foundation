using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>Rejects ambiguous query visibility before a shell starts accepting requests.</summary>
internal sealed class LibraryVisibilityStartupTask(IServiceCollection services) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(services);
        return Task.CompletedTask;
    }

    internal static void Validate(IServiceCollection services)
    {
        if (services.Count(descriptor => descriptor.ServiceType == typeof(IWorkflowLibraryVisibilityPolicy)) > 1)
            throw new InvalidOperationException("IWorkflowLibraryVisibilityPolicy has multiple registrations; select one explicit workflow library visibility policy.");
    }
}
