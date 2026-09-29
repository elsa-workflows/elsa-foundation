using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore.Metadata;
using Elsa.Persistence.Schema;

namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// The schema families one EF module owns, as its assembly declares them (spec 180, FR-001), and which of them each of
/// its stamped tables belongs to. The finalization gate finalizes these families, refuses the module when one of them
/// is unreadable, and checks every write against the family its table belongs to.
/// </summary>
/// <remarks>
/// A family shared by no single module, such as the finalization record's own, is not the module's: its rows are
/// written by the finalization store alone, which stamps them itself.
/// </remarks>
public sealed class EfSchemaModuleFamilies
{
    private static readonly ConcurrentDictionary<Type, EfSchemaModuleFamilies?> ByContextType = new();
    private readonly ConcurrentDictionary<Type, EfSchemaChain?> _byEntityType = new();
    private readonly IReadOnlyList<EfSchemaFamilyDescriptor> _declarations;

    private EfSchemaModuleFamilies(string module, IReadOnlyList<EfSchemaFamilyDescriptor> declarations, IReadOnlyList<EfSchemaChain> chains)
    {
        Module = module;
        _declarations = declarations;
        Chains = chains;
    }

    /// <summary>The EF module's canonical name, as its <see cref="EfModuleAttribute"/> spells it.</summary>
    public string Module { get; }

    /// <summary>The chain of every family the module owns, in declaration order.</summary>
    public IReadOnlyList<EfSchemaChain> Chains { get; }

    /// <summary>
    /// The families of the EF module whose base context <paramref name="contextType"/> is or derives from, or
    /// <see langword="null"/> for a context no <see cref="EfModuleAttribute"/> names, such as a test's own context.
    /// </summary>
    public static EfSchemaModuleFamilies? ForContext(Type contextType)
    {
        ArgumentNullException.ThrowIfNull(contextType);
        return ByContextType.GetOrAdd(contextType, static type =>
        {
            for (var candidate = type; candidate is not null && candidate != typeof(object); candidate = candidate.BaseType)
            {
                var module = EfModuleCatalog.Discover([candidate.Assembly]).FirstOrDefault(descriptor => descriptor.ContextType == candidate);
                if (module is not null)
                    return For(module.Name, module.Assembly);
            }

            return null;
        });
    }

    /// <summary>The families <paramref name="assembly"/> declares for <paramref name="module"/>.</summary>
    public static EfSchemaModuleFamilies For(string module, System.Reflection.Assembly assembly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentNullException.ThrowIfNull(assembly);
        var declarations = EfSchemaFamilyCatalog.Discover([assembly])
            .Where(family => family.Module is not null && StringComparer.OrdinalIgnoreCase.Equals(family.Module, module))
            .ToArray();
        // The families' own cached chains, the same instances every store of the module checks and upcasts through.
        return new EfSchemaModuleFamilies(module, declarations, declarations.Select(declaration => EfSchemaChain.Of(declaration.Assembly, declaration.Name)).ToArray());
    }

    /// <summary>
    /// The families <paramref name="declarations"/> describe, as a module that no assembly declares owns them: for a
    /// test, or a tool, that builds its declarations itself. Each chain is built from its declaration, uncached.
    /// </summary>
    public static EfSchemaModuleFamilies FromDeclarations(string module, IEnumerable<EfSchemaFamilyDescriptor> declarations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentNullException.ThrowIfNull(declarations);
        var declared = declarations.ToArray();
        return new EfSchemaModuleFamilies(module, declared, declared.Select(EfSchemaChain.For).ToArray());
    }

    /// <summary>The declaration of <paramref name="family"/>, one of <see cref="Chains"/>: its content-addressed tables and its rewriter among it.</summary>
    /// <exception cref="ArgumentException">The module owns no such family.</exception>
    public EfSchemaFamilyDescriptor DeclarationOf(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        return _declarations.FirstOrDefault(declaration => StringComparer.Ordinal.Equals(declaration.Name, family))
               ?? throw new ArgumentException($"EF module '{Module}' owns no schema family '{family}'.", nameof(family));
    }

    /// <summary>
    /// The family the rows of <paramref name="entityType"/> belong to: the module's only family, or the one whose
    /// <see cref="EfSchemaFamilyAttribute.Entities"/> names the type. <see langword="null"/> when none or several do,
    /// which a write refuses and a guard test fails the build on.
    /// </summary>
    public EfSchemaChain? FamilyOf(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return _byEntityType.GetOrAdd(entityType, type =>
        {
            if (Chains.Count == 1)
                return Chains[0];
            var owners = _declarations
                .Select((declaration, index) => (declaration, index))
                .Where(item => item.declaration.Entities.Any(entity => entity.IsAssignableFrom(type)))
                .ToArray();
            return owners.Length == 1 ? Chains[owners[0].index] : null;
        });
    }

    /// <summary>Whether rows of <paramref name="entityType"/> carry a schema-version stamp this gate checks.</summary>
    public static bool IsStamped(IReadOnlyEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return !entityType.IsOwned() &&
               !EfSchemaFinalization.Maps(entityType.ClrType) &&
               entityType.FindProperty(EfSchemaVersion.ColumnName) is not null;
    }
}
