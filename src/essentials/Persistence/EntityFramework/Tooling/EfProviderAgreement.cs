using CShells.Features;
using System.Reflection;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>
/// One shell feature class as the provider-agreement check sees it: its CShells feature name, the module(s)
/// its <see cref="UsesEfModuleAttribute"/> declarations map it to, and whether it declares a
/// <c>Provider</c> setting of its own.
/// </summary>
/// <remarks>
/// <see cref="DeclaresProvider"/> is the distinction FR-036 and FR-037 turn on, and the two rules look
/// alike without being alike: a feature that declares the setting and leaves it unset is compared, as
/// <c>Sqlite</c>; a feature that declares no such setting at all is skipped entirely, because the provider
/// of the contexts it reads is decided by whichever features register those contexts' migrations.
/// </remarks>
public sealed record EfFeatureModuleUsage(string Feature, Type FeatureType, IReadOnlyList<string> Modules, bool DeclaresProvider);

/// <summary>One enabled feature whose configured provider disagrees with the requested one.</summary>
public sealed record EfProviderDisagreement(string Shell, string Feature, IReadOnlyList<string> Modules, string? Configured)
{
    /// <summary>
    /// The offender as a refusal detail line: the feature by name, the selected module(s) it backs, and the
    /// provider it is configured for — including when that provider is the <c>Sqlite</c> default an unset
    /// setting resolves to, which is stated as such rather than shown as a blank.
    /// </summary>
    public override string ToString() =>
        $"'{Feature}' in shell '{Shell}' backs {string.Join(", ", Modules.Select(module => $"'{module}'"))} " +
        $"and is configured for '{Configured ?? EfProviderAgreement.UnsetProvider}'" +
        (Configured is null ? " (its Provider setting is unset, which is its own default)." : ".");
}

/// <summary>
/// The per-feature provider-agreement check (spec 171 FR-035–FR-037, ADR 0076 D4): every shell feature
/// that is enabled, maps through <see cref="UsesEfModuleAttribute"/> to a selected module, and declares a
/// <c>Provider</c> setting of its own must be configured for the provider the command asked for.
/// </summary>
/// <remarks>
/// <para>
/// Per feature, never per module. <c>Workflows.Runtime</c> alone is backed by eight features that each
/// carry their own <c>Provider</c> while registering against the same base context, so a check that took
/// any one feature's provider for the module's would let a correctly configured feature mask a wrongly
/// configured one — and the run would then produce SQL for a database the masked feature refuses at
/// startup. Each feature is compared on its own, so at most one of two disagreeing features of one module
/// can equal the requested provider and the other is always reported.
/// </para>
/// <para>
/// Nothing here instantiates a feature: the feature's own default for an unset setting is taken from
/// FR-037's rule (<c>Sqlite</c>) rather than by constructing the class, which would run module code purely
/// to read a default.
/// </para>
/// </remarks>
public static class EfProviderAgreement
{
    /// <summary>What an unset <c>Provider</c> setting resolves to on a feature that declares one (FR-037).</summary>
    public const string UnsetProvider = "Sqlite";

    private const string ProviderSettingName = "Provider";

    /// <summary>
    /// Every feature class in <paramref name="assemblies"/> carrying at least one
    /// <see cref="UsesEfModuleAttribute"/>, keyed by the CShells feature name a shell enables it under.
    /// </summary>
    /// <remarks>
    /// Only assemblies that reference the assembly declaring <see cref="UsesEfModuleAttribute"/> are opened:
    /// nothing else can carry the attribute, and a host's closure is large enough that scanning every type
    /// in it would cost for no possible result. A feature that cannot be loaded is skipped rather than
    /// failing discovery — the host's own closure decides what loads, and a type this build cannot see is
    /// not a type it can compare.
    /// </remarks>
    public static IReadOnlyList<EfFeatureModuleUsage> Discover(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var declaring = typeof(UsesEfModuleAttribute).Assembly.GetName().Name;

        return
        [
            .. assemblies
                .Distinct()
                .Where(assembly => !assembly.IsDynamic && References(assembly, declaring))
                .SelectMany(Types)
                .Select(Describe)
                .Where(usage => usage.Modules.Count > 0)
                .OrderBy(usage => usage.Feature, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// Every enabled feature that disagrees with <paramref name="provider"/>, in shell-then-feature order.
    /// An enabled feature no <see cref="UsesEfModuleAttribute"/> maps to a selected module is ignored, and
    /// so is one that declares no <c>Provider</c> setting of its own.
    /// </summary>
    public static IReadOnlyList<EfProviderDisagreement> Check(
        IEnumerable<(string Shell, string Feature, string? Provider)> enabled,
        IReadOnlyList<EfFeatureModuleUsage> usages,
        IEnumerable<string> selectedModules,
        string provider)
    {
        ArgumentNullException.ThrowIfNull(enabled);
        ArgumentNullException.ThrowIfNull(usages);
        ArgumentNullException.ThrowIfNull(selectedModules);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        var selected = new HashSet<string>(selectedModules, StringComparer.OrdinalIgnoreCase);
        var requested = EfRelationalProviderBinding.Normalize(provider);

        return
        [
            .. enabled
                .SelectMany(
                    entry => usages.Where(usage =>
                        usage.DeclaresProvider && string.Equals(usage.Feature, entry.Feature, StringComparison.OrdinalIgnoreCase)),
                    (entry, usage) => (entry, Modules: usage.Modules.Where(selected.Contains).Order(StringComparer.Ordinal).ToArray()))
                .Where(candidate => candidate.Modules.Length > 0 && !Agrees(candidate.entry.Provider, requested))
                .Select(candidate => new EfProviderDisagreement(
                    candidate.entry.Shell,
                    candidate.entry.Feature,
                    candidate.Modules,
                    Configured(candidate.entry.Provider)))
                .OrderBy(offender => offender.Shell, StringComparer.Ordinal)
                .ThenBy(offender => offender.Feature, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// The CShells feature name a shell enables <paramref name="featureType"/> under: its
    /// <see cref="ShellFeatureAttribute.Name"/>, or — exactly as CShells' own discovery derives it — the
    /// class name with a <c>ShellFeature</c> or <c>Feature</c> suffix stripped.
    /// </summary>
    public static string FeatureName(Type featureType)
    {
        ArgumentNullException.ThrowIfNull(featureType);
        if (featureType.GetCustomAttribute<ShellFeatureAttribute>(inherit: false)?.Name is { Length: > 0 } declared)
            return declared;

        foreach (var suffix in new[] { "ShellFeature", "Feature" })
            if (featureType.Name.EndsWith(suffix, StringComparison.Ordinal) && featureType.Name.Length > suffix.Length)
                return featureType.Name[..^suffix.Length];

        return featureType.Name;
    }

    /// <summary>
    /// Whether <paramref name="featureType"/> declares a <c>Provider</c> setting of its own — a readable,
    /// writable public instance property, which is what CShells' binder sets from shell configuration.
    /// Matched case-insensitively because configuration keys are.
    /// </summary>
    public static bool DeclaresProviderSetting(Type featureType)
    {
        ArgumentNullException.ThrowIfNull(featureType);
        var property = featureType.GetProperty(
            ProviderSettingName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy);
        return property is { CanRead: true, CanWrite: true };
    }

    private static EfFeatureModuleUsage Describe(Type featureType) => new(
        FeatureName(featureType),
        featureType,
        [
            .. featureType.GetCustomAttributes<UsesEfModuleAttribute>(inherit: false)
                .Select(attribute => attribute.Module)
                .Where(module => !string.IsNullOrWhiteSpace(module))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
        ],
        DeclaresProviderSetting(featureType));

    private static bool Agrees(string? configured, string requested)
    {
        var effective = string.IsNullOrWhiteSpace(configured) ? UnsetProvider : configured;
        return string.Equals(EfRelationalProviderBinding.Normalize(effective), requested, StringComparison.Ordinal);
    }

    /// <summary>The value to report, or <c>null</c> when the setting was left unset and defaulted.</summary>
    private static string? Configured(string? configured) => string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();

    private static bool References(Assembly assembly, string? declaring) =>
        assembly.GetName().Name == declaring ||
        assembly.GetReferencedAssemblies().Any(reference => reference.Name == declaring);

    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException failure)
        {
            return failure.Types.OfType<Type>();
        }
        catch (Exception failure) when (failure is FileNotFoundException or TypeLoadException)
        {
            return [];
        }
    }
}
