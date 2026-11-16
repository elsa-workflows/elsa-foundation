using System.Text.Json;

namespace Elsa.Modularity.Core.Models;

public sealed record FeatureCatalogResponse(
    string Revision,
    IReadOnlyList<FeatureCatalogItem> Features);

public sealed record FeatureCatalogItem(
    string Id,
    string DisplayName,
    string? Description,
    IReadOnlyList<string> Categories,
    string SourceKind,
    string? PackageId,
    string? PackageVersion,
    bool Enabled,
    JsonElement Configuration,
    bool Advanced,
    bool Experimental,
    string? ManifestPath,
    string? ManifestHash,
    string? ReadError,
    IReadOnlyList<FeatureSettingDescriptor> Settings,
    IReadOnlyList<FeatureDependency> Dependencies)
{
    /// <summary>
    /// Whether the runtime catalog supplied a live descriptor for this feature, independently of
    /// <see cref="Enabled"/>. An enabled feature without a descriptor is requested but skipped by
    /// lenient shell composition. Manifest metadata alone does not establish runtime availability.
    /// This does not assert successful activation or schema readability; see <see cref="Availability"/>.
    /// </summary>
    public bool RuntimeAvailable { get; init; }

    /// <summary>
    /// Whether an enabled feature is available or dormant, and why, or <see langword="null"/> for a
    /// feature that is not enabled: dormancy is reported only for enabled features. Distinct from <see cref="ReadError"/>,
    /// which means a manifest could not be read.
    /// </summary>
    public FeatureAvailability? Availability { get; init; }
}

public sealed record FeatureDependency(string Id, bool Optional);

/// <summary>
/// An enabled feature's availability: <see cref="AvailableStatus"/>, or
/// <see cref="DormantStatus"/> with one reason per unmet dormancy requirement. A dormant feature is composed and running,
/// and listed as enabled; only its operations that need data a newer schema version holds are refused until then.
/// </summary>
public sealed record FeatureAvailability(string Status, IReadOnlyList<FeatureAvailabilityReason> Reasons)
{
    public const string AvailableStatus = "available";
    public const string DormantStatus = "dormant";

    public static FeatureAvailability Available { get; } = new(AvailableStatus, []);

    public bool IsDormant => Status == DormantStatus;
}

/// <summary>
/// Why a dormant feature is not available yet, for one unmet requirement: the schema family and version it needs, the
/// kind of wait (for instance <c>WaitingForHosts</c>, <c>Held</c> or <c>WaitingForCompleteness</c>), and the reason as an
/// operator reads it, which may name the hosts that cannot read the version yet.
/// </summary>
public sealed record FeatureAvailabilityReason(string Family, string Version, string Kind, string Reason);

public sealed class FeatureCatalogItemBuilder
{
    private static readonly JsonElement s_emptyObject = CreateEmptyObject();
    private static JsonElement CreateEmptyObject() { using var d = JsonDocument.Parse("{}"); return d.RootElement.Clone(); }

    public string Id { get; init; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public IReadOnlyList<string> Categories { get; set; } = [];
    public string SourceKind { get; set; } = FeatureSourceKinds.Shell;
    public string? PackageId { get; set; }
    public string? PackageVersion { get; set; }
    public bool Enabled { get; set; }
    public JsonElement Configuration { get; set; } = s_emptyObject;
    public bool Advanced { get; set; }
    public bool Experimental { get; set; }
    public string? ManifestPath { get; set; }
    public string? ManifestHash { get; set; }
    public string? ReadError { get; set; }
    public IReadOnlyList<FeatureSettingDescriptor> Settings { get; set; } = [];
    public IReadOnlyList<FeatureDependency> Dependencies { get; set; } = [];

    // Set by the runtime contributor when a live descriptor exists for this feature, so the manifest
    // contributor can distinguish "runtime authoritatively resolved zero dependencies" from "no runtime
    // info available" instead of treating an empty list as an unknown sentinel. Not part of ToItem().
    public bool DependenciesResolved { get; set; }

    /// <inheritdoc cref="FeatureCatalogItem.RuntimeAvailable" />
    public bool RuntimeAvailable { get; set; }

    /// <summary>
    /// The feature's class, set by the runtime contributor when a live descriptor exists, so a later contributor can read
    /// what the class declares, such as its dormancy requirements. Not part of <see cref="ToItem"/>.
    /// </summary>
    public Type? FeatureType { get; set; }

    /// <summary>The enabled feature's availability; see <see cref="FeatureCatalogItem.Availability"/>.</summary>
    public FeatureAvailability? Availability { get; set; }

    public FeatureCatalogItem ToItem() =>
        new(
            Id,
            string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName!,
            Description,
            Categories,
            SourceKind,
            PackageId,
            PackageVersion,
            Enabled,
            Configuration.Clone(),
            Advanced,
            Experimental,
            ManifestPath,
            ManifestHash,
            ReadError,
            Settings,
            Dependencies)
        {
            RuntimeAvailable = RuntimeAvailable,
            // Dormancy is reported only for enabled features.
            Availability = Enabled ? Availability : null
        };
}
