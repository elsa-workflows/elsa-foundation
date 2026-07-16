namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Mutable contribution boundary used while selected Groundwork feature declarations are gathered.
/// Once frozen, declarations are exposed as an immutable, deterministically ordered collection and
/// no further contribution is admitted.
/// </summary>
public sealed class GroundworkStorageCompositionContext
{
    private readonly List<GroundworkStorageManifestDeclaration> declarations = [];
    private readonly IReadOnlyList<GroundworkStorageManifestDeclaration> mutableView;
    private IReadOnlyList<GroundworkStorageManifestDeclaration>? frozenDeclarations;

    public GroundworkStorageCompositionContext() => mutableView = declarations.AsReadOnly();

    /// <summary>Gets the contributed declarations in insertion order until frozen, then ordinal feature order.</summary>
    public IReadOnlyList<GroundworkStorageManifestDeclaration> Declarations =>
        frozenDeclarations ?? mutableView;

    public bool IsFrozen => frozenDeclarations is not null;

    /// <summary>Adds one feature declaration while enforcing unique feature ownership.</summary>
    public void Add(GroundworkStorageManifestDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (IsFrozen)
            throw new InvalidOperationException("The Groundwork storage composition context is frozen.");

        var existing = declarations.SingleOrDefault(candidate =>
            string.Equals(candidate.FeatureIdentity, declaration.FeatureIdentity, StringComparison.Ordinal));
        if (existing is not null)
        {
            throw new GroundworkStorageCompositionException(
                $"Groundwork feature identity '{declaration.FeatureIdentity}' has duplicate owners. " +
                $"Existing declaration: {Describe(existing)}; incoming declaration: {Describe(declaration)}.");
        }

        declarations.Add(declaration);
    }

    /// <summary>Freezes the contribution set in deterministic ordinal feature order.</summary>
    public void Freeze()
    {
        if (IsFrozen)
            return;

        frozenDeclarations = Array.AsReadOnly(declarations
            .OrderBy(declaration => declaration.FeatureIdentity, StringComparer.Ordinal)
            .ToArray());
    }

    private static string Describe(GroundworkStorageManifestDeclaration declaration)
    {
        var storageUnits = declaration.Manifest.StorageUnits
            .Select(unit => unit.Identity.Value)
            .OrderBy(identity => identity, StringComparer.Ordinal);
        return $"feature '{declaration.FeatureIdentity}', manifest '{declaration.Manifest.Identity.Value}', " +
               $"owner '{declaration.Manifest.Owner.Value}', storage units [{string.Join(", ", storageUnits)}]";
    }
}

/// <summary>Raised when selected Groundwork storage declarations cannot form one composition.</summary>
public sealed class GroundworkStorageCompositionException(string message) : InvalidOperationException(message);
