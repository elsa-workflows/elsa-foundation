using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Persistence.Groundwork.Targets;

namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>
/// The per-target deployment plan a host computed, in the form a separate process can read.
/// <para>
/// Since targets exist, a host may bind each persistence lane to its own physical store, and every target
/// admits only the storage units of the lanes bound to it. <c>Groundwork.Tool</c> runs in its own process
/// and activates a deployment schema source parameterlessly, so it cannot see those bindings: they only
/// exist after feature composition. Left to itself the tool applies the union of every lane to every
/// database.
/// </para>
/// <para>
/// This descriptor is the host exporting what it already computed, rather than the tool re-deriving it.
/// The alternative would put the binding rule in two implementations, and when they drift the tool
/// provisions a schema the runtime does not expect. That is a quiet failure at deploy time, which is the
/// shape the target registry exists to remove.
/// </para>
/// </summary>
public sealed class GroundworkTargetDeploymentDescriptor
{
    /// <summary>
    /// The descriptor format's own version. A reader that does not recognise the value refuses rather than
    /// guessing at the shape, because a misread descriptor provisions the wrong schema silently.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Creates a descriptor, validating it as if it had just been read back.</summary>
    public static GroundworkTargetDeploymentDescriptor Create(
        string deploymentSchemaSource,
        IReadOnlyCollection<GroundworkTargetDeploymentEntry> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var descriptor = new GroundworkTargetDeploymentDescriptor
        {
            SchemaVersion = CurrentSchemaVersion,
            DeploymentSchemaSource = deploymentSchemaSource,
            Targets = targets.OrderBy(target => target.Name, StringComparer.Ordinal).ToArray()
        };
        descriptor.Validate();
        return descriptor;
    }

    /// <summary>
    /// How a type is written down in a descriptor: full name and simple assembly name, without version or
    /// public key. A descriptor is read by whatever build is being deployed, so pinning it to the exact
    /// assembly version that exported it would make every rebuild look like a mismatch.
    /// </summary>
    public static string NameOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return $"{type.FullName}, {type.Assembly.GetName().Name}";
    }

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// The assembly-qualified whole-host deployment schema source this plan was narrowed from. The reader
    /// re-activates it to recover the host naming policy, and to check that the descriptor still describes
    /// the same set of lanes the host composes.
    /// </summary>
    public string DeploymentSchemaSource { get; init; } = string.Empty;

    public IReadOnlyList<GroundworkTargetDeploymentEntry> Targets { get; init; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>Writes this descriptor to <paramref name="path"/>, creating the directory if needed.</summary>
    public void WriteTo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, ToJson());
    }

    /// <summary>
    /// Reads a descriptor, refusing anything it cannot interpret exactly. There is deliberately no lenient
    /// path: a descriptor that cannot be read is not the same as one that says "apply everything".
    /// </summary>
    public static GroundworkTargetDeploymentDescriptor FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        GroundworkTargetDeploymentDescriptor? descriptor;
        try
        {
            descriptor = JsonSerializer.Deserialize<GroundworkTargetDeploymentDescriptor>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new GroundworkTargetDeploymentDescriptorException(
                "The Groundwork target deployment descriptor is not valid JSON.", exception);
        }

        if (descriptor is null)
            throw new GroundworkTargetDeploymentDescriptorException("The Groundwork target deployment descriptor is empty.");

        descriptor.Validate();
        return descriptor;
    }

    /// <summary>The entry for <paramref name="targetName"/>, refusing when the host never declared it.</summary>
    public GroundworkTargetDeploymentEntry RequireTarget(string? targetName)
    {
        var target = GroundworkTargetNames.Normalize(targetName);
        return Targets.FirstOrDefault(entry => string.Equals(entry.Name, target, StringComparison.Ordinal))
               ?? throw new GroundworkTargetDeploymentDescriptorException(
                   $"The Groundwork target deployment descriptor does not describe target '{target}'. " +
                   $"It describes [{string.Join(", ", Targets.Select(entry => entry.Name))}]. " +
                   "Apply the schema for a target the host actually declares, or re-export the descriptor.");
    }

    private void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new GroundworkTargetDeploymentDescriptorException(
                $"The Groundwork target deployment descriptor declares format version {SchemaVersion}, and this " +
                $"build reads version {CurrentSchemaVersion}. Re-export it from the host you are deploying.");
        }

        if (string.IsNullOrWhiteSpace(DeploymentSchemaSource))
            throw new GroundworkTargetDeploymentDescriptorException("The descriptor does not name a deployment schema source.");

        if (Targets is not { Count: > 0 })
            throw new GroundworkTargetDeploymentDescriptorException("The descriptor describes no targets.");

        var duplicate = Targets
            .GroupBy(target => target.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new GroundworkTargetDeploymentDescriptorException($"Target '{duplicate.Key}' is described more than once.");

        foreach (var target in Targets)
            target.Validate();
    }
}

/// <summary>One target's share of a host's deployment plan.</summary>
public sealed class GroundworkTargetDeploymentEntry
{
    public static GroundworkTargetDeploymentEntry Create(
        string name,
        string manifestIdentity,
        IReadOnlyCollection<string> manifestSources)
    {
        ArgumentNullException.ThrowIfNull(manifestSources);
        var entry = new GroundworkTargetDeploymentEntry
        {
            Name = GroundworkTargetNames.Normalize(name),
            ManifestIdentity = manifestIdentity,
            ManifestSources = manifestSources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
        };
        entry.Validate();
        return entry;
    }

    /// <summary>The operator-chosen target name, canonicalized.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The composed manifest identity the runtime derives for this target. Groundwork keys its persisted
    /// schema state on (manifest identity, provider name), so this is the value that decides which state row
    /// the tool is about to write. It is checked against a freshly computed one before anything is applied.
    /// </summary>
    public string ManifestIdentity { get; init; } = string.Empty;

    /// <summary>The assembly-qualified manifest source types bound to this target.</summary>
    public IReadOnlyList<string> ManifestSources { get; init; } = [];

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new GroundworkTargetDeploymentDescriptorException("A described target must have a name.");

        if (string.IsNullOrWhiteSpace(ManifestIdentity))
            throw new GroundworkTargetDeploymentDescriptorException($"Target '{Name}' does not record a manifest identity.");

        if (ManifestSources is not { Count: > 0 })
        {
            throw new GroundworkTargetDeploymentDescriptorException(
                $"Target '{Name}' records no manifest sources. A target with nothing bound to it has no schema to " +
                "apply, and the host refuses to compose it, so the descriptor should not describe it either.");
        }

        if (ManifestSources.Any(string.IsNullOrWhiteSpace))
            throw new GroundworkTargetDeploymentDescriptorException($"Target '{Name}' records a blank manifest source.");
    }
}

/// <summary>
/// Raised when a deployment descriptor is absent, unreadable, or describes something other than what is
/// about to be applied. It is deliberately never recoverable into "apply the host-wide union": a stale
/// descriptor and a missing one are both refusals, because the alternative provisions a schema the runtime
/// did not ask for and says nothing.
/// </summary>
public sealed class GroundworkTargetDeploymentDescriptorException : Exception
{
    public GroundworkTargetDeploymentDescriptorException(string message) : base(message)
    {
    }

    public GroundworkTargetDeploymentDescriptorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
