using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Sqlite;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Renders a storage manifest's physical surface into a canonical, diff-friendly form and compares
/// it against a committed golden file. The golden pins both the resolved physical schema (via
/// Groundwork's own route fingerprints) and the declared logical indexes and bounded queries, so any
/// manifest refactor that changes what providers would materialize fails deterministically.
/// Set ELSA_UPDATE_GOLDENS=1 to rewrite the golden files instead of asserting.
/// </summary>
public static class GroundworkManifestGoldenVerifier
{
    private static readonly ProviderIdentity FingerprintProvider = new("groundwork-sqlite", "1.0.0");

    public static void Verify(StorageManifest manifest, params string[] goldenPathSegments)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (goldenPathSegments.Length == 0)
            throw new ArgumentException("A repo-relative golden path is required.", nameof(goldenPathSegments));

        var goldenPath = Path.Combine([FindRepoRoot(), .. goldenPathSegments]);
        var rendered = Render(manifest);

        if (Environment.GetEnvironmentVariable("ELSA_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, rendered);
            return;
        }

        if (!File.Exists(goldenPath))
        {
            throw new InvalidOperationException(
                $"Golden file '{goldenPath}' does not exist. Run the suite once with ELSA_UPDATE_GOLDENS=1 to capture it.");
        }

        var expected = File.ReadAllText(goldenPath);
        if (expected == rendered)
            return;

        throw new InvalidOperationException(
            $"Manifest '{manifest.Identity.Value}' no longer matches its golden physical surface at '{goldenPath}'. " +
            $"First difference: {DescribeFirstDifference(expected, rendered)} " +
            "If the change is intentional, regenerate with ELSA_UPDATE_GOLDENS=1 and review the diff.");
    }

    // Identity-policy comparison-algorithm stamps embed a hash of the runtime's Unicode casefold
    // table, which varies with the host's ICU version (macOS vs Linux). The digest below normalizes
    // those stamps out so goldens are platform-stable; everything else in Groundwork's canonical
    // definition serialization is pinned verbatim.
    private static readonly Regex PlatformDependentAlgorithmHash = new(
        "(?<=-v[0-9]+-)[0-9a-f]{64}", RegexOptions.Compiled);

    public static string Render(StorageManifest manifest)
    {
        // Compile validates the manifest and certifies executable routes exactly as production does.
        PhysicalSchemaTargetCompiler.Compile(
            manifest,
            FingerprintProvider,
            SqliteGroundworkCapabilities.PhysicalNames);

        var resolution = PhysicalStorageResolver.Resolve(
            manifest, PhysicalNamePolicy.Identity, SqliteGroundworkCapabilities.PhysicalNames);
        var serializedDefinitions = resolution.Definitions.ToDictionary(
            definition => definition.Resolved.StorageUnit.Value,
            definition => PlatformDependentAlgorithmHash.Replace(
                PhysicalStorageDefinitionSerializer.Serialize(definition), "platform-dependent"),
            StringComparer.Ordinal);

        // Set ELSA_DUMP_DEFINITIONS=<dir> to write each unit's serialized definition for diffing
        // when a digest changes and the declared sections of the golden do not explain it.
        if (Environment.GetEnvironmentVariable("ELSA_DUMP_DEFINITIONS") is { Length: > 0 } dumpDirectory)
        {
            Directory.CreateDirectory(dumpDirectory);
            foreach (var (unitIdentity, serialized) in serializedDefinitions)
                File.WriteAllText(Path.Combine(dumpDirectory, $"{manifest.Identity.Value}.{unitIdentity}.json"), serialized);
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("manifest", manifest.Identity.Value);
            writer.WriteString("owner", manifest.Owner.Value);
            writer.WriteString("version", manifest.Version.Value);

            writer.WritePropertyName("sharedDocumentStorages");
            writer.WriteStartArray();
            foreach (var storage in manifest.SharedDocumentStorages.OrderBy(x => x.Binding.Value, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("binding", storage.Binding.Value);
                writer.WriteString("featureDefaultLogicalName", storage.FeatureDefaultLogicalName);
                writer.WriteString("canonicalJsonColumn", storage.Envelope.CanonicalJsonColumn);
                writer.WriteNumber("schemaVersion", storage.SchemaVersion);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("storageUnits");
            writer.WriteStartArray();
            foreach (var unit in manifest.StorageUnits.OrderBy(x => x.Identity.Value, StringComparer.Ordinal))
                WriteUnit(writer, unit, serializedDefinitions[unit.Identity.Value]);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }

    private static void WriteUnit(Utf8JsonWriter writer, StorageUnit unit, string serializedDefinition)
    {
        var storage = unit.PhysicalStorage
            ?? throw new InvalidOperationException($"Storage unit '{unit.Identity.Value}' has no physical storage to pin.");

        writer.WriteStartObject();
        writer.WriteString("identity", unit.Identity.Value);
        writer.WriteString(
            "definitionDigest",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serializedDefinition))).ToLowerInvariant());
        writer.WriteString("provisioningMode", storage.ProvisioningMode.ToString());
        writer.WriteString("policy", storage.Policy switch
        {
            PhysicalStoragePolicy.ExplicitPolicy explicitPolicy => $"Explicit({explicitPolicy.Definition.Form})",
            PhysicalStoragePolicy.DefaultPolicy defaultPolicy => $"Default({defaultPolicy.SharedStorage?.Value ?? "none"})",
            _ => storage.Policy.GetType().Name
        });

        writer.WritePropertyName("logicalIndexes");
        writer.WriteStartArray();
        foreach (var index in storage.LogicalIndexes)
        {
            writer.WriteStartObject();
            writer.WriteString("identity", index.Identity);
            writer.WritePropertyName("fields");
            writer.WriteStartArray();
            foreach (var field in index.Fields)
            {
                writer.WriteStartObject();
                writer.WriteString("path", field.Path);
                if (field.ValueKind is { } fieldKind)
                    writer.WriteString("valueKind", fieldKind.ToString());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("valueKind", index.ValueKind.ToString());
            writer.WriteBoolean("isUnique", index.IsUnique);
            writer.WriteString("missingValueBehavior", index.MissingValueBehavior.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WritePropertyName("boundedQueries");
        writer.WriteStartArray();
        foreach (var query in storage.BoundedQueries)
            WriteQuery(writer, query);
        writer.WriteEndArray();

        WriteNames(writer, "nameOverrides", storage.NameOverrides.Select(x => $"{x.ObjectKind}:{x.FeatureDefaultLogicalName}:{x.LogicalName}"));
        WriteNames(writer, "boundedMutations", storage.BoundedMutations.Select(x => x.Identity));
        writer.WriteEndObject();
    }

    private static void WriteQuery(Utf8JsonWriter writer, BoundedQueryDeclaration query)
    {
        writer.WriteStartObject();
        writer.WriteString("identity", query.Identity);
        writer.WriteString("indexIdentity", query.IndexIdentity);
        WriteNames(writer, "operations", query.Operations.Select(x => x.ToString()));
        writer.WriteString("sortSupport", query.SortSupport.ToString());
        writer.WriteString("pagingSupport", query.PagingSupport.ToString());
        writer.WriteString("executionClass", query.ExecutionClass.ToString());
        writer.WriteBoolean("supportsDisjunction", query.SupportsDisjunction);
        writer.WriteBoolean("supportsTotalCount", query.SupportsTotalCount);
        writer.WriteString("predicateBindingMode", query.PredicateBindingMode.ToString());

        writer.WritePropertyName("sortFields");
        writer.WriteStartArray();
        foreach (var field in query.SortFields)
        {
            writer.WriteStartObject();
            writer.WriteString("path", field.Path);
            writer.WriteString("direction", field.Direction.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WritePropertyName("predicateFields");
        writer.WriteStartArray();
        foreach (var field in query.PredicateFields)
        {
            writer.WriteStartObject();
            writer.WriteString("path", field.Path);
            WriteNames(writer, "operations", field.Operations.Select(x => x.ToString()));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WritePropertyName("residualPredicateFields");
        writer.WriteStartArray();
        foreach (var field in query.ResidualPredicateFields)
        {
            writer.WriteStartObject();
            writer.WriteString("path", field.Path);
            writer.WriteString("valueKind", field.ValueKind.ToString());
            WriteNames(writer, "operations", field.Operations.Select(x => x.ToString()));
            writer.WriteBoolean("isRequired", field.IsRequired);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        WriteNames(writer, "resultOperations", query.ResultOperations.Select(x => x.ToString()));
        if (query.LatestPerKeyPath is { } latestPerKeyPath)
            writer.WriteString("latestPerKeyPath", latestPerKeyPath);
        writer.WriteEndObject();
    }

    private static void WriteNames(Utf8JsonWriter writer, string propertyName, IEnumerable<string> names)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var name in names.Order(StringComparer.Ordinal))
            writer.WriteStringValue(name);
        writer.WriteEndArray();
    }

    private static string DescribeFirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            var expectedLine = i < expectedLines.Length ? expectedLines[i] : "<end of file>";
            var actualLine = i < actualLines.Length ? actualLines[i] : "<end of file>";
            if (expectedLine != actualLine)
                return $"line {i + 1}: expected '{expectedLine.Trim()}', actual '{actualLine.Trim()}'.";
        }

        return "identical content with different encoding.";
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test base directory.");
    }
}
