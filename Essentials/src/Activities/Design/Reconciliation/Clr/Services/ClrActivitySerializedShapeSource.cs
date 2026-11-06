using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Reconciliation.Clr.Contracts;
using Elsa.Activities.Design.Reconciliation.Clr.Options;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Serialization;
using Elsa.Expressions.Core.Contracts;
using Elsa.Primitives.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Reconciliation.Clr.Services;

/// <summary>Binds admitted contracts to configured metadata, without consulting runtime alias authority.</summary>
internal sealed class ClrActivitySerializedShapeSource(
    IOptions<ClrReconciliationOptions> options,
    IClrAssemblyScanner scanner,
    IVariableTypeDescriptorCatalog types,
    IActivityContractCapabilityCatalog capabilities,
    ILogger<ClrActivitySerializedShapeSource> logger,
    IActivityDefinitionHasher? hasher = null) : IActivitySerializedShapeSource
{
    private readonly ClrReconciliationOptions _options = options.Value;
    private static readonly JsonSerializerOptions DescriptorOptions = new(JsonSerializerDefaults.Web);

    public ValueTask<ActivitySerializedShape?> DescribeResultAsync(IActivityDefinitionVersion admittedVersion,
        string projectionKey, ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default)
    {
        Validate(memberPath, limits, cancellationToken);
        ArgumentNullException.ThrowIfNull(admittedVersion);
        if (admittedVersion.ProviderKey != ClrAssemblyScanner.ProviderKey || admittedVersion.SourceKind != "CLR" ||
            admittedVersion.SourceId != _options.ResolvedSourceId)
            return ValueTask.FromResult<ActivitySerializedShape?>(null);
        var revision = ResultRevision(admittedVersion, projectionKey);
        if (admittedVersion.ProviderSchemaVersion != ClrAssemblyScanner.SchemaVersion ||
            admittedVersion.ConsumerKey != ClrAssemblyScanner.ProviderKey || admittedVersion.ConsumerSchemaVersion != "1")
            return Answer(Unavailable(revision, "activity.shape.contract-schema-unavailable"));
        try
        {
            if (hasher is null || string.IsNullOrWhiteSpace(admittedVersion.Hash) || !Directory.Exists(_options.ResolvedFolderPath))
                return Answer(Unavailable(revision, "activity.shape.source-unavailable"));
            if (!StringComparer.Ordinal.Equals(hasher.Hash(admittedVersion.Definition, admittedVersion), admittedVersion.Hash))
                return Answer(Unavailable(revision, "activity.shape.source-contract-mismatch"));
            var descriptor = admittedVersion.DescriptorPayload.Deserialize<ClrActivityDescriptor>(DescriptorOptions);
            if (descriptor is null || descriptor.TypeAlias != admittedVersion.Definition.ActivityTypeKey)
                return Answer(Unavailable(revision, "activity.shape.descriptor-mismatch"));
            var current = scanner.Scan(_options.ResolvedFolderPath).Where(x => x.ActivityTypeKey == descriptor.TypeAlias).ToArray();
            if (current.Length != 1 || current[0].Version != admittedVersion.Version)
                return Answer(Unavailable(revision, "activity.shape.source-version-unavailable"));
            var model = current[0];
            if (model.Descriptor is not ClrActivityDescriptor currentDescriptor || currentDescriptor.TypeAlias != descriptor.TypeAlias)
                return Answer(Unavailable(revision, "activity.shape.descriptor-mismatch"));
            var candidate = new ActivityDefinitionVersionModel(admittedVersion.Id, model.Version, admittedVersion.DefinitionId,
                model.ProviderKey, model.ProviderSchemaVersion, model.ConsumerKey, model.ConsumerSchemaVersion,
                admittedVersion.DescriptorPayload, "CLR", _options.ResolvedSourceId,
                admittedVersion.Definition, model.Inputs, model.Outputs, model.DesignFacets, model.ExecutionType, string.Empty);
            if (!StringComparer.Ordinal.Equals(hasher.Hash(admittedVersion.Definition, candidate), admittedVersion.Hash))
                return Answer(Unavailable(revision, "activity.shape.source-contract-mismatch"));

            var dlls = Directory.EnumerateFiles(_options.ResolvedFolderPath, "*.dll", SearchOption.TopDirectoryOnly).ToArray();
            using var context = new MetadataLoadContext(new PathAssemblyResolver(ClrMetadataResolver.BuildResolverPaths(dlls, logger)));
            var matchingTypes = new List<Type>();
            foreach (var dll in dlls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assembly assembly;
                try { assembly = context.LoadFromAssemblyPath(dll); }
                catch (BadImageFormatException) { continue; }
                if (assembly.GetType(descriptor.TypeAlias, throwOnError: false, ignoreCase: false) is { } type)
                    matchingTypes.Add(type);
            }
            if (matchingTypes.Count != 1)
                return Answer(Unavailable(revision, "activity.shape.metadata-type-unavailable"));
            var activityType = matchingTypes[0];
            using var activityAsset = File.OpenRead(activityType.Assembly.Location);
            var metadataSeed = Convert.ToHexString(SHA256.HashData(activityAsset)).ToLowerInvariant();
            var results = activityType.GetInterfaces().Where(i => i.IsGenericType &&
                i.GetGenericTypeDefinition().FullName == typeof(IActivityResult<>).FullName)
                .Select(i => i.GetGenericArguments()[0]).Distinct().ToArray();
            if (results.Length == 0)
                return Answer(Unavailable(revision, "activity.shape.no-typed-result"));
            if (results.Length != 1)
                return Answer(Unavailable(revision, "activity.shape.result-contract-ambiguous"));
            var resultType = results[0];
            var rootType = TypeReferenceFactory.FromClrType(resultType, TypeAliasConvention.CanonicalAlias);
            var prefix = ImmutableArray<string>.Empty;
            OutputDefinition? output = null;
            Type? projectionType = null;
            NullabilityInfo? projectionNullability = null;
            bool? projectionIsNullable = null;
            if (projectionKey != "$result")
            {
                var outputs = admittedVersion.Outputs.Where(x => x.ReferenceKey == projectionKey).ToArray();
                if (outputs.Length != 1)
                    return Answer(Unavailable(revision, "activity.shape.projection-not-declared"));
                output = outputs[0];
                var property = resultType.GetProperty(output.Name, BindingFlags.Public | BindingFlags.Instance);
                var attribute = property is null ? null : ReflectionOnlyAttributes.FindAttributeUpPropertyChain(property,
                    "Elsa.Activities.Runtime.Core.Attributes.OutputAttribute");
                if (property is null || attribute is null)
                    return Answer(Unavailable(revision, "activity.shape.projection-mismatch"));
                // Preserve the existing producer projection path contract; only the new member suffix is literal.
                var path = attribute.NamedArguments.FirstOrDefault(x => x.MemberName == "Path").TypedValue.Value as string ??
                    JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                if (path != "$")
                {
                    var whole = new ClrSerializedShapeReader(revision, new(1, 4096), cancellationToken, metadataSeed).Read(resultType, rootType, [], false);
                    prefix = whole.Members.Any(x => x.SerializedName == path) ? [path] :
                        path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToImmutableArray();
                }
                var projectionReader = new ClrSerializedShapeReader(revision, new(0, 1), cancellationToken, metadataSeed);
                var projected = projectionReader.Read(resultType, rootType, prefix, false);
                if (projected.ReasonCode == "activity.shape.member-not-declared" || projected.Type is not null && projected.Type != output.Type)
                    return Answer(Unavailable(revision, "activity.shape.projection-mismatch"));
                if (projectionReader.SelectedType is null)
                    return Answer(projected with
                    {
                        Type = memberPath.IsEmpty ? output.Type : null,
                        Presence = output.IsRequired
                            ? memberPath.IsEmpty ? ActivitySerializedPresence.Required : ActivitySerializedPresence.Unknown
                            : ActivitySerializedPresence.Optional,
                        IsNullable = output.IsRequired && memberPath.IsEmpty ? false : null
                    });
                projectionType = projectionReader.SelectedType;
                projectionNullability = projectionReader.SelectedNullability;
                projectionIsNullable = projected.IsNullable;
                metadataSeed += ":" + projected.MetadataRevision;
            }
            // Member selection starts at the already completed projection envelope. A required producer
            // projection cannot be null on success, even if its original CLR property was nullable.
            var selected = output is null
                ? new ClrSerializedShapeReader(revision, limits, cancellationToken, metadataSeed).Read(resultType, rootType, memberPath, false)
                : new ClrSerializedShapeReader(revision, limits, cancellationToken, metadataSeed).Read(projectionType!, output.Type, memberPath,
                    output.IsRequired ? false : projectionIsNullable,
                    output.IsRequired ? ActivitySerializedPresence.Required : ActivitySerializedPresence.Optional,
                    output.SourceRepresentation, projectionNullability);
            return Answer(selected);
        }
        catch (Exception exception) when (MetadataFailure(exception))
        {
            return Answer(Unavailable(revision, "activity.shape.metadata-unavailable"));
        }
    }

    public ValueTask<ActivitySerializedShape?> DescribeDeclaredTypeAsync(TypeReference declaredType, string ownerContractIdentity,
        ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default)
    {
        Validate(memberPath, limits, cancellationToken);
        ArgumentNullException.ThrowIfNull(declaredType);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerContractIdentity);
        var admitted = capabilities.Types.Where(x => x.Alias == declaredType.Alias && x.SupportedCollectionKinds.Contains(declaredType.CollectionKind)).ToArray();
        var descriptors = types.GetDescriptors().Where(x => x.Alias == declaredType.Alias && x.SupportedCollectionKinds.Contains(declaredType.CollectionKind)).ToArray();
        if (admitted.Length != 1 || descriptors.Length != 1)
            return Answer(Unknown(null, ownerContractIdentity, "activity.shape.type-not-admitted"));
        // Type admission is not serialization authority. Request seeding uses default JSON options
        // for CLR values and preserves caller JsonElement keys; variables can retain JSON from other
        // writers. The alias cannot establish literal member names under the activity-value profile.
        return Answer(Unknown(memberPath.IsEmpty ? declaredType : null, ownerContractIdentity,
            "activity.shape.serialization-profile-unbound"));
    }

    private static ActivitySerializedShape Unavailable(string revision, string reason) =>
        new(ActivitySerializedShapeState.Unavailable, ActivitySerializedValueKind.Unknown, null, revision, ActivityValueJsonProfile.Revision, string.Empty)
        { ReasonCode = reason };
    private static ActivitySerializedShape Unknown(TypeReference? type, string revision, string reason) =>
        new(ActivitySerializedShapeState.Unknown, ActivitySerializedValueKind.Unknown, type, revision, string.Empty, string.Empty)
        { ReasonCode = reason };
    private static ValueTask<ActivitySerializedShape?> Answer(ActivitySerializedShape shape) => ValueTask.FromResult<ActivitySerializedShape?>(shape);
    private static bool MetadataFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or BadImageFormatException or
        TypeLoadException or ReflectionTypeLoadException or NotSupportedException or InvalidOperationException or ArgumentException or JsonException or
        AmbiguousMatchException or FormatException or OverflowException or System.Security.SecurityException;

    private static void Validate(ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(limits);
        if (memberPath.IsDefault || memberPath.Any(x => x is null) || memberPath.Length > 128)
            throw new ArgumentException("A literal member path must contain at most 128 non-null segments.", nameof(memberPath));
        if (limits.MaximumDepth is < 0 or > 32 || limits.MaximumMembers is < 0 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(limits), "Metadata response limits must be within the supported bounds.");
    }

    private static string ResultRevision(IActivityDefinitionVersion version, string projectionKey)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { version.Id, version.Version, version.Hash, version.ProviderKey,
            version.ProviderSchemaVersion, version.ConsumerKey, version.ConsumerSchemaVersion, version.DescriptorPayload,
            version.SourceKind, version.SourceId, ProjectionKey = projectionKey }, DescriptorOptions);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
