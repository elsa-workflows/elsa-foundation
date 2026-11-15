using CShells.Lifecycle;
using Microsoft.Extensions.Configuration;

namespace Elsa.Persistence.EntityFramework.ResourceResolution;

/// <summary>Reads only persistence selection and legacy target presence from one composed shell.</summary>
internal static class PersistenceConfigurationAdapter
{
    internal sealed record ReadResult(
        PersistenceResolutionInput Input,
        IReadOnlyList<string> UnresolvedCodes,
        IReadOnlySet<string> UnsupportedResourceDefinitions);

    public static ReadResult Read(
        ShellSettingsPreparationContext context,
        IConfiguration rootConfiguration,
        IReadOnlyList<EnrolledPersistenceParticipant> participants,
        IReadOnlyCollection<string>? knownEnrollmentIds = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rootConfiguration);
        ArgumentNullException.ThrowIfNull(participants);

        var rootSource = new PersistenceSourceProvenance("root", "configuration", false, true);
        var shellSource = new PersistenceSourceProvenance("shell-composed", "configuration", false, true);
        var rawSource = new PersistenceSourceProvenance("shell-authored", "configuration", false, true);
        var root = rootConfiguration.GetSection("Elsa:Persistence");
        var rawShell = rootConfiguration.GetSection($"CShells:Shells:{context.ShellId.Name}");
        var rawPersistence = rawShell.GetSection("Configuration:Elsa:Persistence");

        var resources = new Dictionary<string, PersistenceResourceDefinition>(StringComparer.OrdinalIgnoreCase);
        var unsupportedDefinitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in root.GetSection("Resources").GetChildren())
        {
            if (resource.GetChildren().Any(field =>
                    !StringComparer.OrdinalIgnoreCase.Equals(field.Key, "Provider") &&
                    !StringComparer.OrdinalIgnoreCase.Equals(field.Key, "ConnectionName")))
                unsupportedDefinitions.Add(resource.Key);
            resources[resource.Key] = new PersistenceResourceDefinition(
                resource.Key,
                ReadReference(resource, "Provider", rootSource),
                ReadReference(resource, "ConnectionName", rootSource),
                rootSource);
        }

        var shellDefault = ReadComposedOrRaw(
            context.ConfigurationData,
            "Elsa:Persistence:DefaultResource",
            rawPersistence,
            "DefaultResource",
            shellSource,
            rawSource);
        var bindings = new Dictionary<string, PersistenceAuthoredValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in context.ConfigurationData)
        {
            const string prefix = "Elsa:Persistence:Bindings:";
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var featureId = key[prefix.Length..];
            if (featureId.Length != 0)
                bindings[featureId] = FromReferenceScalar(value, shellSource);
        }
        foreach (var binding in rawPersistence.GetSection("Bindings").GetChildren())
            bindings.TryAdd(binding.Key, ReadReference(rawPersistence.GetSection("Bindings"), binding.Key, rawSource));

        var resetIds = context.FeatureSettingResetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enabledIds = context.EnabledFeatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var disabledIds = context.DisabledFeatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var legacy = new Dictionary<string, PersistenceLegacyTargetPresence>(StringComparer.OrdinalIgnoreCase);
        foreach (var participant in participants)
        {
            var featureId = participant.FeatureId;
            var rawFeature = FindRawFeature(rawShell.GetSection("Features"), featureId);
            var provider = ReadLegacyField("Provider");
            var connectionName = ReadLegacyField("ConnectionName");
            var connectionString = ReadLegacyField("ConnectionString");
            legacy[featureId] = new PersistenceLegacyTargetPresence(
                featureId,
                enabledIds.Contains(featureId) && !disabledIds.Contains(featureId),
                resetIds.Contains(featureId),
                provider,
                connectionName,
                connectionString);

            PersistenceLegacyFieldPresence ReadLegacyField(string field)
            {
                if (TryGet(context.ConfigurationData, $"{featureId}:{field}", out var finalValue))
                    return new PersistenceLegacyFieldPresence(FromScalar(finalValue, shellSource).Presence, shellSource, true);

                if (resetIds.Contains(featureId))
                    return new PersistenceLegacyFieldPresence(PersistencePresence.Absent, shellSource);

                // CShells flattens non-null feature settings but drops null leaves. The raw view
                // restores only that lost presence; it cannot replace the final composed map.
                var authored = ReadValue(rawFeature, field, rawSource);
                if (authored.Presence == PersistencePresence.Null)
                    return new PersistenceLegacyFieldPresence(PersistencePresence.Null, rawSource);
                var wrapped = ReadValue(rawFeature.GetSection("Settings"), field, rawSource);
                return new PersistenceLegacyFieldPresence(
                    wrapped.Presence == PersistencePresence.Null ? PersistencePresence.Null : PersistencePresence.Absent,
                    rawSource);
            }
        }

        // A binding to an enrolled but inactive feature is preserved without becoming active
        // or being misreported as an unknown consumer.
        foreach (var knownId in knownEnrollmentIds ?? [])
        {
            if (legacy.ContainsKey(knownId))
                continue;
            var absent = new PersistenceLegacyFieldPresence(PersistencePresence.Absent, shellSource);
            legacy[knownId] = new PersistenceLegacyTargetPresence(
                knownId, false, false, absent, absent, absent);
        }

        var unresolved = new List<string>();
        if (root.GetSection("Bindings").GetChildren().Any() ||
            rawPersistence.GetSection("Resources").GetChildren().Any())
            unresolved.Add("resource-scope-unsupported");

        var sourceContext = new PersistenceConfigurationContext(
            PersistenceContextMode.Runtime,
            context.ShellId.Name,
            null,
            [rootSource, shellSource, rawSource],
            false,
            false);
        return new ReadResult(
            new PersistenceResolutionInput(
                new PersistenceResourceCatalog(resources, ReadReference(root, "DefaultResource", rootSource)),
                new PersistenceShellSelection(context.ShellId.Name, shellDefault, bindings),
                participants,
                legacy,
                sourceContext),
            unresolved,
            unsupportedDefinitions);
    }

    private static IConfigurationSection FindRawFeature(IConfigurationSection features, string featureId)
    {
        foreach (var entry in features.GetChildren())
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(entry.Key, featureId) ||
                StringComparer.OrdinalIgnoreCase.Equals(entry.GetSection("Name").Value, featureId))
                return entry;
        }
        return features.GetSection(featureId);
    }

    private static PersistenceAuthoredValue ReadComposedOrRaw(
        IReadOnlyDictionary<string, string?> composed,
        string composedKey,
        IConfigurationSection raw,
        string rawKey,
        PersistenceSourceProvenance composedSource,
        PersistenceSourceProvenance rawSource) =>
        TryGet(composed, composedKey, out var value)
            ? FromReferenceScalar(value, composedSource)
            : ReadReference(raw, rawKey, rawSource);

    private static PersistenceAuthoredValue ReadReference(
        IConfigurationSection parent,
        string key,
        PersistenceSourceProvenance source)
    {
        var value = ReadValue(parent, key, source);
        return value.Presence == PersistencePresence.Value
            ? FromReferenceScalar(value.Value, source)
            : value;
    }

    private static PersistenceAuthoredValue ReadValue(
        IConfigurationSection parent,
        string key,
        PersistenceSourceProvenance source)
    {
        var child = parent.GetChildren().FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Key, key));
        if (child is null)
            return new PersistenceAuthoredValue(PersistencePresence.Absent, null, source);
        if (child.GetChildren().Any())
            return new PersistenceAuthoredValue(PersistencePresence.WrongType, null, source);
        return FromScalar(child.Value, source);
    }

    private static PersistenceAuthoredValue FromScalar(string? value, PersistenceSourceProvenance source) =>
        new(value is null ? PersistencePresence.Null : string.IsNullOrWhiteSpace(value) ? PersistencePresence.Blank : PersistencePresence.Value,
            value, source);

    // IConfiguration has only string? leaves. It cannot distinguish JSON false/0 from
    // the strings "false"/"0" after provider composition. The first-slice contract reserves
    // those ambiguous spellings and uses identifier-like reference names instead.
    private static PersistenceAuthoredValue FromReferenceScalar(string? value, PersistenceSourceProvenance source)
    {
        var scalar = FromScalar(value, source);
        return scalar.Presence == PersistencePresence.Value && !IsReferenceName(value!)
            ? new PersistenceAuthoredValue(PersistencePresence.WrongType, null, source)
            : scalar;
    }

    private static bool IsReferenceName(string value) =>
        value.Length > 0 &&
        (char.IsLetter(value[0]) || value[0] == '_') &&
        !value.Equals("true", StringComparison.OrdinalIgnoreCase) &&
        !value.Equals("false", StringComparison.OrdinalIgnoreCase) &&
        !value.Equals("null", StringComparison.OrdinalIgnoreCase) &&
        value.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.');

    private static bool TryGet(IReadOnlyDictionary<string, string?> values, string key, out string? value)
    {
        if (values.TryGetValue(key, out value))
            return true;
        foreach (var pair in values)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(pair.Key, key))
                continue;
            value = pair.Value;
            return true;
        }
        value = null;
        return false;
    }
}
