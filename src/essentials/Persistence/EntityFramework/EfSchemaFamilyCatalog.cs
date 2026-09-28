using System.Reflection;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The one place that reads <see cref="EfSchemaFamilyAttribute"/> declarations off a set of assemblies (spec 180,
/// FR-001). Every family is discovered the same way, first-party and third-party alike.
/// </summary>
/// <remarks>
/// Declarations are matched by the attribute's full type name and read as metadata (<see cref="CustomAttributeData"/>),
/// never constructed. A package that Nuplane loads into a load context of its own can carry its own copy of this
/// assembly, and so its own copy of the attribute type. Matching by type identity would skip exactly that package's
/// families without a word, and a family missing from a host's readability report lets a version finalize that the host
/// cannot read (spec 183, FR-020).
/// </remarks>
public static class EfSchemaFamilyCatalog
{
    private static readonly string FamilyAttributeName = typeof(EfSchemaFamilyAttribute).FullName!;
    private static readonly string ModuleAttributeName = typeof(EfModuleAttribute).FullName!;
    private static readonly string UpcasterAttributeName = typeof(EfSchemaUpcasterAttribute).FullName!;
    private static readonly string UpcasterInterfaceName = typeof(IEfSchemaUpcaster).FullName!;
    private static readonly string ContentAttributeName = typeof(EfSchemaContentAttribute).FullName!;
    private static readonly string IntegrityAttributeName = typeof(EfSchemaIntegrityAttribute).FullName!;

    /// <summary>
    /// Enumerates every <see cref="EfSchemaFamilyAttribute"/> declared on <paramref name="assemblies"/>, one
    /// <see cref="EfSchemaFamilyDescriptor"/> per declaration. Refuses discovery, naming the assembly, when a declaration
    /// has no name or no current version, when its module is not one the same assembly declares with
    /// <see cref="EfModuleAttribute"/>, when a family declared shared - owned by no single EF module, so
    /// <see cref="EfSchemaFamilyDescriptor.Module"/> reads <see langword="null"/> - sits in an assembly that declares an
    /// <see cref="EfModuleAttribute"/> of its own, or when one assembly declares a family twice. The same family
    /// declared by two assemblies, such as two generations of one package, is two descriptors: combining them is the
    /// caller's decision. Each descriptor carries the content and integrity columns its assembly declares for it
    /// (<see cref="EfSchemaContentAttribute"/>, <see cref="EfSchemaIntegrityAttribute"/>); discovery is refused, naming
    /// the assembly, when such a declaration names a family the assembly does not declare, names no type or column, gives
    /// an integrity column no reason, or declares one column twice.
    /// </summary>
    /// <remarks>
    /// A fault in a family's upcaster chain (spec 180, FR-005) does not refuse discovery: it is reported on the
    /// descriptor's <see cref="EfSchemaFamilyDescriptor.Defects"/>, and its
    /// <see cref="EfSchemaFamilyDescriptor.ReadableVersions"/> never credits a version the fault leaves unreachable. The
    /// build and the family's registration at startup refuse it instead, so one family's broken chain cannot take every
    /// other family out of a host's readability report.
    /// </remarks>
    public static IReadOnlyList<EfSchemaFamilyDescriptor> Discover(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return assemblies.Distinct().SelectMany(Describe).ToArray();
    }

    private static EfSchemaFamilyDescriptor[] Describe(Assembly assembly)
    {
        var attributes = assembly.GetCustomAttributesData();
        var declarations = attributes.Where(attribute => Is(attribute, FamilyAttributeName)).ToArray();
        if (declarations.Length == 0)
            return [];

        var modules = attributes
            .Where(attribute => Is(attribute, ModuleAttributeName))
            .Select(attribute => Argument(attribute, 0))
            .OfType<string>()
            .ToArray();
        var families = declarations.Select(declaration => Describe(assembly, declaration, modules)).ToArray();

        var duplicate = families
            .GroupBy(family => family.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"{assembly.GetName().Name} declares schema family '{duplicate.Key}' more than once.");

        var columns = Columns(assembly, attributes, families);
        return families
            .Select(family => family with
            {
                ContentColumns = columns.Where(column => column.Family == family.Name && column.Reason is null)
                    .Select(column => new EfSchemaColumn(column.Entity, column.Name)).ToArray(),
                IntegrityColumns = columns.Where(column => column.Family == family.Name && column.Reason is not null)
                    .Select(column => new EfSchemaIntegrityColumn(column.Entity, column.Name, column.Reason!)).ToArray()
            })
            .ToArray();
    }

    /// <summary>
    /// Every content and integrity column <paramref name="assembly"/> declares, one entry per column: its family, the type
    /// mapped to its table, its name, and for an integrity column the reason, <see langword="null"/> for content.
    /// </summary>
    private static (string Family, Type Entity, string Name, string? Reason)[] Columns(
        Assembly assembly,
        IList<CustomAttributeData> attributes,
        EfSchemaFamilyDescriptor[] families)
    {
        var name = assembly.GetName().Name;
        var content = attributes.Where(attribute => Is(attribute, ContentAttributeName))
            .SelectMany(attribute => Names(attribute).Select(column => (Declaration: attribute, Column: column, Reason: (string?)null)));
        var integrity = attributes.Where(attribute => Is(attribute, IntegrityAttributeName))
            .Select(attribute => (Declaration: attribute, Column: Argument(attribute, 2), Reason: (string?)(Argument(attribute, 3) ?? "")));
        var columns = content.Concat(integrity)
            .Select(column => (Family: Argument(column.Declaration, 0), Entity: column.Declaration.ConstructorArguments.Count > 1 ? column.Declaration.ConstructorArguments[1].Value as Type : null, column.Column, column.Reason))
            .ToArray();

        foreach (var column in columns)
        {
            var declaration = $"{name} declares a {(column.Reason is null ? "content" : "integrity")} column '{column.Entity?.Name}.{column.Column}' of family '{column.Family}'";
            if (column.Family is null || !families.Any(family => StringComparer.Ordinal.Equals(family.Name, column.Family)))
                throw new InvalidOperationException($"{declaration}, which that assembly does not declare with [EfSchemaFamily].");
            if (column.Entity is null || string.IsNullOrWhiteSpace(column.Column))
                throw new InvalidOperationException($"{declaration} with no type or no column name.");
            if (column.Reason is not null && string.IsNullOrWhiteSpace(column.Reason))
                throw new InvalidOperationException($"{declaration} with no reason; an integrity column records why it is compared as stored bytes.");
        }

        var twice = columns
            .GroupBy(column => (column.Entity, column.Column))
            .FirstOrDefault(group => group.Count() > 1);
        if (twice is not null)
            throw new InvalidOperationException(
                $"{name} declares column '{twice.Key.Entity!.Name}.{twice.Key.Column}' {twice.Count()} times; a column is content or integrity of one family, once.");

        return columns.Select(column => (column.Family!, column.Entity!, column.Column!, column.Reason)).ToArray();
    }

    /// <summary>The columns a content declaration names: its <c>params</c> argument, read as metadata.</summary>
    private static IEnumerable<string?> Names(CustomAttributeData declaration) =>
        declaration.ConstructorArguments.Count > 2 && declaration.ConstructorArguments[2].Value is IEnumerable<CustomAttributeTypedArgument> names
            ? names.Select(column => column.Value as string).DefaultIfEmpty(null)
            : [null];

    private static EfSchemaFamilyDescriptor Describe(Assembly assembly, CustomAttributeData declaration, string[] modules)
    {
        var name = Argument(declaration, 0);
        // The two-argument constructor (name, currentVersion) declares a family shared by no single EF module; the
        // three-argument one (name, module, currentVersion) names its owner explicitly.
        var shared = declaration.ConstructorArguments.Count < 3;
        var module = shared ? null : Argument(declaration, 1);
        var currentVersion = Argument(declaration, shared ? 1 : 2);

        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"{assembly.GetName().Name} declares an [EfSchemaFamily] with no name.");
        if (string.IsNullOrWhiteSpace(currentVersion))
            throw new InvalidOperationException($"{assembly.GetName().Name} declares [EfSchemaFamily(\"{name}\")] with no current version.");

        if (shared)
        {
            if (modules.Length > 0)
                throw new InvalidOperationException(
                    $"{assembly.GetName().Name} declares [EfSchemaFamily(\"{name}\")] shared, with no single owning EF module, but also declares " +
                    $"{string.Join(", ", modules.Select(declared => $"'{declared}'"))}. An assembly that owns an [EfModule] names it as the family's " +
                    "owner instead of declaring the family shared.");

            return new EfSchemaFamilyDescriptor(name, null, currentVersion, assembly) { Upcasters = Upcasters(declaration) };
        }

        var owner = modules.FirstOrDefault(declared => StringComparer.OrdinalIgnoreCase.Equals(declared, module));
        if (owner is null)
            throw new InvalidOperationException(
                $"{assembly.GetName().Name} declares [EfSchemaFamily(\"{name}\")] owned by EF module '{module}', which that assembly does not declare. " +
                $"A family belongs to an [EfModule] of its own assembly; this one declares {(modules.Length == 0 ? "none" : string.Join(", ", modules.Select(declared => $"'{declared}'")))}.");

        return new EfSchemaFamilyDescriptor(name, owner, currentVersion, assembly) { Upcasters = Upcasters(declaration) };
    }

    /// <summary>The declaration's <see cref="EfSchemaFamilyAttribute.Upcasters"/>, each read as metadata.</summary>
    private static EfSchemaUpcasterDescriptor[] Upcasters(CustomAttributeData declaration) =>
        declaration.NamedArguments
            .Where(argument => argument.MemberName == nameof(EfSchemaFamilyAttribute.Upcasters))
            .Select(argument => argument.TypedValue.Value)
            .OfType<IEnumerable<CustomAttributeTypedArgument>>()
            .SelectMany(types => types)
            .Select(type => type.Value as Type)
            .Select(DescribeUpcaster)
            .ToArray();

    /// <summary>One chain entry, read from <paramref name="type"/>'s metadata exactly as a declaration's entries are.</summary>
    internal static EfSchemaUpcasterDescriptor DescribeUpcaster(Type? type)
    {
        if (type is null)
            return new EfSchemaUpcasterDescriptor(typeof(void), null, null, "the chain lists no type.");

        var versions = type.GetCustomAttributesData().Where(attribute => Is(attribute, UpcasterAttributeName)).ToArray();
        var from = versions.Length == 1 ? Argument(versions[0], 0) : null;
        var to = versions.Length == 1 ? Argument(versions[0], 1) : null;
        return new EfSchemaUpcasterDescriptor(type, from, to, Refusal(type));
    }

    /// <summary>
    /// Why <paramref name="type"/> cannot be constructed and called as an upcaster (spec 180, FR-003), read without
    /// constructing it; interfaces are matched by full name for the same reason attributes are.
    /// </summary>
    private static string? Refusal(Type type)
    {
        if (!type.IsClass || type.IsAbstract)
            return "an upcaster is a concrete class.";
        if (type.ContainsGenericParameters)
            return "an upcaster is not an open generic type.";
        if (!type.GetInterfaces().Any(contract => string.Equals(contract.FullName, UpcasterInterfaceName, StringComparison.Ordinal)))
            return $"an upcaster implements {UpcasterInterfaceName}.";
        if (type.GetConstructor(Type.EmptyTypes) is not { IsPublic: true })
            return "an upcaster has a public parameterless constructor, since the persistence worker has no container to inject from.";
        return null;
    }

    private static bool Is(CustomAttributeData attribute, string fullName) =>
        string.Equals(attribute.AttributeType.FullName, fullName, StringComparison.Ordinal);

    private static string? Argument(CustomAttributeData attribute, int index) =>
        attribute.ConstructorArguments.Count > index ? attribute.ConstructorArguments[index].Value as string : null;
}
