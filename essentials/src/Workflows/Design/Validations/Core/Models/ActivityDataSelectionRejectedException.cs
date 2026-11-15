using System.Collections.Immutable;

namespace Elsa.Workflows.Design.Validations.Core.Models;

/// <summary>Static source-read findings that prevent executable admission.</summary>
public sealed class ActivityDataSelectionRejectedException(IEnumerable<ValidationError> findings)
    : Exception("One or more activity data references are invalid.")
{
    public ImmutableArray<ValidationError> Findings { get; } =
        (findings ?? throw new ArgumentNullException(nameof(findings))).ToImmutableArray();

    public string? ResourceKind { get; init; }
    public string? ResourceId { get; init; }
    public string? ProviderKey { get; init; }
}
