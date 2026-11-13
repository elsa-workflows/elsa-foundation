using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Publishing.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>Refuses ambiguous candidate publication policy before startup reconcilers can publish.</summary>
[Order(-100)]
internal sealed class ValidateActivityPublishingReplacementContractsStartupTask(IServiceCollection services) : IStartupTask
{
    internal static readonly Type[] Contracts =
    [
        typeof(IActivityTemplateCompiler),
        typeof(IExecutableStructureNormalizer),
        typeof(IActivityPublicationCandidateEvaluator),
        typeof(IActivityPublicationReadiness),
        typeof(IActivityCatalogProjection),
        typeof(IActivityPublicationVersionPolicy)
    ];

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var invalid = Contracts.Select(type => (Type: type, Count: services.Count(x => x.ServiceType == type)))
            .Where(x => x.Count != 1)
            .Select(x => $"{x.Type.Name} ({x.Count} registrations)").Order(StringComparer.Ordinal).ToArray();
        if (invalid.Length != 0)
            throw new InvalidOperationException("Activity publishing replacement contracts require exactly one implementation: " + string.Join(", ", invalid) + ". Register a replacement before the feature defaults or use services.Replace(...).");
        return Task.CompletedTask;
    }
}
