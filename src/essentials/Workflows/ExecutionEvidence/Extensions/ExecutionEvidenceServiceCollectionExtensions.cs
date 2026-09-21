using Elsa.Workflows.ExecutionEvidence.Contracts;
using Elsa.Workflows.ExecutionEvidence.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.ExecutionEvidence.Extensions;

public static class ExecutionEvidenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the process-local evidence store and the checkpoint enricher that feeds it. The enricher joins the
    /// existing <see cref="IRuntimeCheckpointCommitEnricher"/> set, which the committer resolves with
    /// <c>GetServices</c>, so this composes additively and in no particular order with whatever else is registered.
    /// </summary>
    public static IServiceCollection AddExecutionEvidence(
        this IServiceCollection services,
        Action<ExecutionEvidenceOptions>? configure = null)
    {
        var options = new ExecutionEvidenceOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IExecutionEvidenceStore>(_ => new InMemoryExecutionEvidenceStore(options));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, ExecutionEvidenceCheckpointEnricher>());
        return services;
    }
}
