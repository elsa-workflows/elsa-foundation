using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Tests.Support;

internal sealed record EtlV1CompatibilityFile(string Path, long ByteLength, string Sha256);

internal sealed record EtlV1CompatibilityManifest(
    int FixtureFormatVersion,
    string ProducerSourceBaseline,
    string CaptureBaseCommit,
    string CaptureHarnessState,
    IReadOnlyList<EtlV1CompatibilityFile> CaptureHarnessFiles,
    string DefinitionId,
    int DefinitionVersion,
    string EtlArtifactId,
    string EtlContentHash,
    string WorkflowArtifactId,
    string WorkflowArtifactHash,
    string WorkflowDefinitionVersionId,
    string? TenantId,
    string PersistencePartition,
    string RuntimeRunId,
    int PartitionOrdinal,
    long CapturedFence,
    DateTimeOffset CapturedLeaseExpiresAt,
    EtlRunBindings Bindings,
    EtlPreparedRun Prepared,
    EtlBatchDescriptor ReservedBatch,
    EtlOutputEffect MainEffect,
    long AcceptedRecordsBeforeAcceptance,
    long OutstandingRecordsBeforeAcceptance,
    long OutstandingBytesBeforeAcceptance,
    IReadOnlyList<EtlV1CompatibilityFile> Files);

/// <summary>Reads the retained producer-era V1 payloads without invoking current fixture producers.</summary>
internal sealed class EtlV1CompatibilityFixture
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private EtlV1CompatibilityFixture(string rootDirectory, EtlV1CompatibilityManifest manifest)
    {
        RootDirectory = rootDirectory;
        Manifest = manifest;
    }

    public string RootDirectory { get; }
    public EtlV1CompatibilityManifest Manifest { get; }
    public string RuntimeDirectory => Path.GetDirectoryName(FilePath("runtime/runtime.db"))!;
    public string SnapshotRoot => Path.Combine(RuntimeDirectory, "snapshots");
    public string OutputRoot => Path.Combine(RuntimeDirectory, "output");

    public static EtlV1CompatibilityFixture Open()
    {
        var root = Path.Combine(FindRepositoryRoot(), "Extensions", "Etl", "tests", "Shared", "Fixtures", "V1", "data");
        var manifestPath = Path.Combine(root, "manifest.json");
        var manifest = JsonSerializer.Deserialize<EtlV1CompatibilityManifest>(File.ReadAllBytes(manifestPath), WebJson)
            ?? throw new InvalidDataException("The retained ETL V1 compatibility manifest is empty.");
        if (manifest.FixtureFormatVersion != 1)
            throw new InvalidDataException("The retained ETL V1 compatibility fixture version is unsupported.");
        return new EtlV1CompatibilityFixture(root, manifest);
    }

    public string FilePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is ".." or "."))
            throw new InvalidDataException("A compatibility fixture path is not relative and bounded.");
        var path = Path.GetFullPath(Path.Combine(RootDirectory, relativePath));
        if (!path.StartsWith(Path.GetFullPath(RootDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("A compatibility fixture path escaped its root.");
        return path;
    }

    public EtlArtifact ReadArtifact() =>
        JsonSerializer.Deserialize<EtlArtifact>(File.ReadAllBytes(FilePath("etl-artifact.json")), WebJson)
        ?? throw new InvalidDataException("The retained ETL V1 artifact is empty.");

    public EtlBatchAcceptance ReadAcceptance() =>
        JsonSerializer.Deserialize<EtlBatchAcceptance>(File.ReadAllBytes(FilePath("runtime/acceptance.json")), WebJson)
        ?? throw new InvalidDataException("The retained ETL V1 acceptance receipt is empty.");

    public void VerifyFiles()
    {
        foreach (var item in Manifest.Files)
        {
            var path = FilePath(item.Path);
            var bytes = File.ReadAllBytes(path);
            if (bytes.LongLength != item.ByteLength ||
                !StringComparer.Ordinal.Equals(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), item.Sha256))
                throw new InvalidDataException($"The retained ETL V1 fixture file '{item.Path}' does not match its manifest.");
        }
    }

    public void CopyRuntimeTo(string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (Directory.Exists(destinationDirectory) && Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
            throw new IOException("The ETL V1 runtime fixture destination must be empty.");

        Directory.CreateDirectory(destinationDirectory);
        File.Copy(FilePath("runtime/runtime.db"), Path.Combine(destinationDirectory, "runtime.db"));
        CopyDirectory(Path.Combine(RuntimeDirectory, "snapshots"), Path.Combine(destinationDirectory, "snapshots"));
        CopyDirectory(Path.Combine(RuntimeDirectory, "output"), Path.Combine(destinationDirectory, "output"));
    }

    public static IReadOnlyList<EtlV1CompatibilityFile> DescribeFiles(string rootDirectory) =>
        Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !StringComparer.Ordinal.Equals(Path.GetFileName(path), "manifest.json"))
            .Select(path =>
            {
                var bytes = File.ReadAllBytes(path);
                return new EtlV1CompatibilityFile(
                    Path.GetRelativePath(rootDirectory, path).Replace(Path.DirectorySeparatorChar, '/'),
                    bytes.LongLength,
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            })
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<EtlV1CompatibilityFile> DescribeFiles(string rootDirectory, IEnumerable<string> relativePaths) =>
        relativePaths.Select(relativePath =>
        {
            var path = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
            var bytes = File.ReadAllBytes(path);
            return new EtlV1CompatibilityFile(
                relativePath.Replace(Path.DirectorySeparatorChar, '/'),
                bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }).OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();

    public static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
            return;

        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
    }

    public static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        throw new InvalidOperationException("Could not locate the Elsa repository root for the ETL V1 fixture.");
    }
}
