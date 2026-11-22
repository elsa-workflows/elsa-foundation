using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Models;

/// <summary>
/// The declared inputs of one document, indexed by reference key. An <c>Input</c> or <c>InputMember</c> read names an
/// input by its reference key, and it names one only when exactly one declared input has that key. The data selection
/// analysis and publication's lowering of those reads both ask this one index, built once per document.
/// </summary>
public sealed class DeclaredInputIndex
{
    private readonly Dictionary<string, InputDefinition[]> _byReferenceKey;

    public DeclaredInputIndex(IEnumerable<InputDefinition> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        _byReferenceKey = inputs
            .Where(input => !string.IsNullOrEmpty(input.ReferenceKey))
            .GroupBy(input => input.ReferenceKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
    }

    /// <summary>The one declared input with <paramref name="referenceKey"/>; false when there is none or more than one.</summary>
    public bool TryFind(string referenceKey, out InputDefinition input)
    {
        if (_byReferenceKey.TryGetValue(referenceKey, out var matches) && matches is [var single])
        {
            input = single;
            return true;
        }

        input = null!;
        return false;
    }
}
