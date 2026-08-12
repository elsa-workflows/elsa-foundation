using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Core.Manifests;

namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>
/// The deployment schema for exactly one Groundwork target, recovered from a host-exported descriptor.
/// <para>
/// One assembly deploys as many schemas as the host has targets, and which one to apply is an operator's
/// choice per invocation rather than a property of this type. So the tool passes the descriptor and the
/// target name as manifest options: <c>--manifest-option descriptor=…</c> and
/// <c>--manifest-option target=…</c>.
/// </para>
/// <para>
/// Every disagreement between the descriptor and what is actually loaded is a refusal. A missing, stale, or
/// mismatched descriptor never degrades into applying the host-wide union: over-provisioning silently is
/// the failure this exists to remove, so it must not be the fallback.
/// </para>
/// </summary>
public sealed class GroundworkTargetDeploymentSchema :
    GroundworkDeploymentSchemaManifestSource,
    IConfigurablePhysicalSchemaManifestSource
{
    /// <summary>Manifest option naming the descriptor the host exported.</summary>
    public const string DescriptorOption = "descriptor";

    /// <summary>
    /// Manifest option naming the target whose schema is being applied. Absent means
    /// <see cref="GroundworkTargetNames.Default"/>, which is the name a single-database host uses, so
    /// omitting it stays correct for the deployments that never split anything.
    /// </summary>
    public const string TargetOption = "target";

    private Lazy<ResolvedPlan> plan;

    public GroundworkTargetDeploymentSchema()
        : this(descriptorPath: null, targetName: null)
    {
    }

    /// <summary>Creates the source from an explicit path and target, which is what the tests drive.</summary>
    public GroundworkTargetDeploymentSchema(string? descriptorPath, string? targetName) =>
        plan = CreatePlan(descriptorPath, targetName);

    public void Configure(IReadOnlyDictionary<string, string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var unknown = options.Keys
            .Where(key => key is not (DescriptorOption or TargetOption))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new GroundworkTargetDeploymentDescriptorException(
                $"Unknown manifest option(s) [{string.Join(", ", unknown)}]. This source accepts " +
                $"'{DescriptorOption}' and '{TargetOption}'.");
        }

        plan = CreatePlan(
            options.GetValueOrDefault(DescriptorOption),
            options.GetValueOrDefault(TargetOption));
    }

    /// <summary>The target this source was narrowed to.</summary>
    public string TargetName => plan.Value.TargetName;

    protected override IReadOnlyCollection<Type> ManifestSourceTypes => plan.Value.ManifestSourceTypes;

    protected override GroundworkStorageNamingPolicyOptions CreateStorageNamingPolicy() => plan.Value.NamingPolicy;

    private protected override StorageManifestIdentity ManifestIdentity => plan.Value.ManifestIdentity;

    private static Lazy<ResolvedPlan> CreatePlan(string? descriptorPath, string? targetName) =>
        new(() => Resolve(descriptorPath, targetName), LazyThreadSafetyMode.ExecutionAndPublication);

    private static ResolvedPlan Resolve(string? descriptorPath, string? targetName)
    {
        var target = GroundworkTargetNames.Normalize(targetName);
        if (string.IsNullOrWhiteSpace(descriptorPath))
        {
            throw new GroundworkTargetDeploymentDescriptorException(
                $"No Groundwork target deployment descriptor was given. Pass '--manifest-option " +
                $"{DescriptorOption}=<path>' naming the descriptor the host exported. Without it the target's " +
                "schema cannot be told from the rest of the host's, and applying the whole host to one target " +
                "is exactly what this refuses to do.");
        }

        if (!File.Exists(descriptorPath))
        {
            throw new GroundworkTargetDeploymentDescriptorException(
                $"The Groundwork target deployment descriptor '{descriptorPath}' does not exist. Export it from " +
                "the host you are deploying, and name it with '--manifest-option " + DescriptorOption + "=<path>'.");
        }

        var descriptor = GroundworkTargetDeploymentDescriptor.FromJson(File.ReadAllText(descriptorPath));
        var entry = descriptor.RequireTarget(target);

        // The identity decides which Groundwork schema-state row is about to be written. A descriptor that
        // disagrees with what this build derives is describing a different deployment, so nothing is applied.
        var derivedIdentity = GroundworkStorageCompositionDescriptor.IdentityFor(target);
        if (!string.Equals(entry.ManifestIdentity, derivedIdentity.Value, StringComparison.Ordinal))
        {
            throw new GroundworkTargetDeploymentDescriptorException(
                $"The descriptor records manifest identity '{entry.ManifestIdentity}' for target '{target}', but " +
                $"this build derives '{derivedIdentity.Value}'. Applying it would write another deployment's " +
                "schema-state row. Re-export the descriptor from the host you are deploying.");
        }

        var hostSource = ActivateHostSource(descriptor.DeploymentSchemaSource);
        var hostTypes = hostSource.GetManifestSourceTypes();
        var described = descriptor.Targets
            .SelectMany(item => item.ManifestSources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Freshness, made checkable rather than conventional: the descriptor must account for every lane the
        // host composes, and name no lane it does not. Either way round the descriptor predates the host.
        var hostNames = hostTypes.Select(GroundworkTargetDeploymentDescriptor.NameOf).ToHashSet(StringComparer.Ordinal);
        if (!hostNames.SetEquals(described))
        {
            var missing = hostNames.Except(described, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var unknown = described.Except(hostNames, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            throw new GroundworkTargetDeploymentDescriptorException(
                $"The Groundwork target deployment descriptor is stale for deployment schema source " +
                $"'{descriptor.DeploymentSchemaSource}'. " +
                (missing.Length > 0 ? $"It does not place [{string.Join(", ", missing)}]. " : string.Empty) +
                (unknown.Length > 0 ? $"It places [{string.Join(", ", unknown)}], which the host does not compose. " : string.Empty) +
                "Re-export it from the host you are deploying.");
        }

        var manifestSourceTypes = entry.ManifestSources.Select(ResolveManifestSourceType).ToArray();
        return new ResolvedPlan(
            target,
            derivedIdentity,
            Array.AsReadOnly(manifestSourceTypes),
            hostSource.GetStorageNamingPolicy());
    }

    private static GroundworkDeploymentSchemaManifestSource ActivateHostSource(string assemblyQualifiedName)
    {
        var type = ResolveType(assemblyQualifiedName)
                   ?? throw new GroundworkTargetDeploymentDescriptorException(
                       $"The descriptor's deployment schema source '{assemblyQualifiedName}' could not be loaded. " +
                       "The descriptor was exported from a different build than the one being applied.");

        if (!typeof(GroundworkDeploymentSchemaManifestSource).IsAssignableFrom(type))
        {
            throw new GroundworkTargetDeploymentDescriptorException(
                $"The descriptor's deployment schema source '{assemblyQualifiedName}' is not a " +
                $"{nameof(GroundworkDeploymentSchemaManifestSource)}.");
        }

        return (GroundworkDeploymentSchemaManifestSource)(Activator.CreateInstance(type)
            ?? throw new GroundworkTargetDeploymentDescriptorException(
                $"The descriptor's deployment schema source '{assemblyQualifiedName}' could not be activated."));
    }

    private static Type ResolveManifestSourceType(string assemblyQualifiedName) =>
        ResolveType(assemblyQualifiedName)
        ?? throw new GroundworkTargetDeploymentDescriptorException(
            $"The descriptor names manifest source '{assemblyQualifiedName}', which this build cannot load. " +
            "The descriptor was exported from a different build than the one being applied.");

    /// <summary>
    /// Resolves a type by assembly-qualified name, falling back to the assemblies already loaded. The
    /// fallback matters because the tool loads one assembly explicitly and reaches the rest by probing.
    /// </summary>
    private static Type? ResolveType(string assemblyQualifiedName)
    {
        var type = Type.GetType(assemblyQualifiedName, throwOnError: false);
        if (type is not null)
            return type;

        var separator = assemblyQualifiedName.IndexOf(',');
        var fullName = separator < 0 ? assemblyQualifiedName : assemblyQualifiedName[..separator].Trim();
        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(fullName, throwOnError: false))
            .FirstOrDefault(candidate => candidate is not null);
    }

    private sealed record ResolvedPlan(
        string TargetName,
        StorageManifestIdentity ManifestIdentity,
        IReadOnlyCollection<Type> ManifestSourceTypes,
        GroundworkStorageNamingPolicyOptions NamingPolicy);
}
