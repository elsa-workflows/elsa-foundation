namespace Elsa.Serialization.Core;

/// <summary>Runtime null contracts for activated type aliases, independently dispatched from design picker metadata.</summary>
/// <remarks>Replacement contract: exactly one implementation is active per host; replace its existing registration.</remarks>
public interface IRuntimeTypeNullability
{
    /// <summary>Finds the activated scalar type's null contract. Unknown aliases return false.</summary>
    bool TryGetSupportsNull(string alias, out bool supportsNull);
}
