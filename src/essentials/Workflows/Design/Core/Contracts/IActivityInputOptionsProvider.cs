using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// Contributes context-dependent allowable values for activity inputs at design time.
/// Register implementations as enumerable services; keys are stable and case-sensitive.
/// </summary>
public interface IActivityInputOptionsProvider
{
    string Key { get; }

    ValueTask<IReadOnlyList<ActivityInputOption>> GetOptionsAsync(
        ActivityInputOptionsContext context,
        CancellationToken cancellationToken = default);
}
