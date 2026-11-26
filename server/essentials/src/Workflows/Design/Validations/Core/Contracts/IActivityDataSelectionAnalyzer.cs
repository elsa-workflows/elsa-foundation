using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Contracts;

/// <summary>One scoped authority for authored data-source discovery and static read admission.</summary>
/// <remarks>
/// The resource owner authorizes and supplies the actual canonical candidate. This service neither
/// reads HTTP authority nor persists, evaluates expressions, executes producers or creates drafts.
/// A null target validates the complete candidate without building a browser response. Missing or
/// duplicate analyzer authority is a composition error, never permission to skip admission.
/// </remarks>
public interface IActivityDataSelectionAnalyzer
{
    ValueTask<ActivityDataSelectionAnalysis> AnalyzeAsync(
        ActivityDataSelectionDocument document,
        ActivityDataSelectionTarget? target = null,
        ActivityDataSelectionQuery? query = null,
        CancellationToken cancellationToken = default);
}
