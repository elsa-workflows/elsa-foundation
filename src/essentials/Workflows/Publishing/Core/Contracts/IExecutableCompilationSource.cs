using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>
/// Returns immutable compile-time claims without mutating the executable tree.
/// </summary>
public interface IExecutableCompilationSource
{
    ValueTask<ExecutableCompilationContribution> GetContributionAsync(
        ExecutableCompilationContext context,
        CancellationToken cancellationToken = default);
}
