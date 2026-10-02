using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Modularity.Planning.Catalog;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>Disposable source and supplied-input files for candidate inspection against a real host closure.</summary>
internal sealed class CandidateInspectionFixture : IDisposable
{
    public const string ResourceProbeFeatureId = "ResourceProbe";
    public const string ResourceProbeModuleId = "Acme.ResourceProbe";
    public const string StructuredLogsFeatureId = "DiagnosticsStructuredLogs";
    public const string StructuredLogsEfFeatureId = "DiagnosticsStructuredLogsEntityFrameworkCore";
    public const string OpenTelemetryFeatureId = "DiagnosticsOpenTelemetry";
    public const string OpenTelemetryEfFeatureId = "DiagnosticsOpenTelemetryEntityFrameworkCore";
    public const string PrivateCanary = "candidate-local-value-canary-2177";
    public const string EnvironmentPolicy = "workbench-json-explicit-environment-v1";
    public const string EnvironmentInvocationId = "11111111111111111111111111111111";
    public const string EnvironmentCaptureId = "22222222222222222222222222222222";
    public const string PublicEnvironmentResource = "EnvironmentResource";
    public const string PublicEnvironmentConnection = "DeclaredConnection";
    public const string OverlayEnvironmentResource = "OverlayResource";
    public const string OverlayEnvironmentConnection = "OverlayConnection";
    public const string PrivateEnvironmentCanary = "candidate-environment-private-canary-2292";
    public const string SafePrivateEnvironmentCanary = "PrivateEnvironmentValue2292";
    public const string WorkbenchModularityApiFeatureId = "ModularityApi";
    public const string WorkbenchRuntimeFaultStackTraceFeatureId = "RuntimeFaultStackTrace";

    public static byte[] EnvironmentDocument(params (string Key, string Value)[] entries) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            entries = entries.Select(entry => new { key = entry.Key, value = entry.Value })
        });

    /// <summary>Produces a valid empty document at an exact raw UTF-8 size, using legal trailing whitespace.</summary>
    public static byte[] EnvironmentDocumentOfSize(int byteCount)
    {
        var document = EnvironmentDocument();
        ArgumentOutOfRangeException.ThrowIfLessThan(byteCount, document.Length);
        var bytes = new byte[byteCount];
        document.CopyTo(bytes, 0);
        bytes.AsSpan(document.Length).Fill((byte)' ');
        return bytes;
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly TempDirectory _directory = new("elsa-candidate-inspection-");
    private readonly Dictionary<string, byte[]> _initialInputBytes = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, byte[]> _sourceBytes;
    private string? _workbenchHostDirectory;

    public CandidateInspectionFixture()
    {
        HostAssemblyDirectory = DotnetElsa.Host("ResourceAwareLiveHost");
        SourceDirectory = _directory.File("source");
        InputDirectory = _directory.File("inputs");
        Directory.CreateDirectory(SourceDirectory);
        Directory.CreateDirectory(InputDirectory);

        File.WriteAllText(Path.Join(SourceDirectory, "shells.json"), """
            {"CShells":{"Shells":{"default":{"Name":"default","Features":{"ResourceProbe":{}}}}}}
            """);
        File.WriteAllText(Path.Join(SourceDirectory, "shells.Production.json"), """
            {"CShells":{"Shells":{"default":{"Configuration":{"Elsa":{"Persistence":{"Bindings":{"ResourceProbe":"primary"}}}}}}}}
            """);
        File.WriteAllText(Path.Join(SourceDirectory, "appsettings.json"), """
            {"ConnectionStrings":{"Probe":"Data Source=:memory:"},"Elsa":{"Persistence":{"DefaultResource":"primary","Resources":{"primary":{"Provider":"Sqlite","ConnectionName":"Probe"}}}}}
            """);
        File.WriteAllText(Path.Join(SourceDirectory, "appsettings.Production.json"), "{}");
        _sourceBytes = Directory.GetFiles(SourceDirectory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
    }

    /// <summary>The compiled host closure passed to the CLI's --host option; source files live separately.</summary>
    public string HostAssemblyDirectory { get; }

    /// <summary>
    /// A disposable copy of the actual Workbench build output, with only runtime-owned Nuplane state omitted.
    /// Keeping this installation separate prevents a prior host run's package state from changing the closure
    /// observed by the acceptance journey while preserving the real compiled assembly and dependency layout.
    /// </summary>
    public string WorkbenchHostDirectory => _workbenchHostDirectory ??
        throw new InvalidOperationException("UseWorkbenchFiles must prepare the actual Workbench installation first.");

    public string SourceDirectory { get; }

    public string InputDirectory { get; }

    public string CandidateOutputDirectory => _directory.File("generated-candidate");

    public string ShellId => "default";

    public string Environment => "Production";

    public string DatabasePath => Path.Join(SourceDirectory, "must-not-create.db");
    public string ContextMarkerPath => _directory.File("context-constructed.txt");
    public string ActionMarkerPath => _directory.File("action-constructed.txt");
    public string SettingReviewPath => InputPath("setting-review.json");
    public IReadOnlyDictionary<string, string> SentinelEnvironment => new Dictionary<string, string>
    {
        ["ELSA_RESOURCE_PROBE_CONTEXT_MARKER"] = ContextMarkerPath,
        ["ELSA_RESOURCE_PROBE_ACTION_MARKER"] = ActionMarkerPath
    };

    public void UseDiagnosticsSource(bool includeOpenTelemetryEf = true)
    {
        var shells = JsonNode.Parse(File.ReadAllText(Path.Join(SourceDirectory, "shells.json")))!;
        var features = shells["CShells"]!["Shells"]![ShellId]!["Features"]!.AsObject();
        features[OpenTelemetryFeatureId] = new JsonObject();
        if (includeOpenTelemetryEf)
            features[OpenTelemetryEfFeatureId] = new JsonObject();
        File.WriteAllText(Path.Join(SourceDirectory, "shells.json"), shells.ToJsonString());
        var appsettings = JsonNode.Parse(File.ReadAllText(Path.Join(SourceDirectory, "appsettings.json")))!;
        appsettings["ProbeDefaults"] = new JsonObject { ["EnableDiagnostics"] = true };
        appsettings["ConnectionStrings"]!["Probe"] = $"Data Source={DatabasePath};Password={PrivateCanary}";
        appsettings["UnknownLocal"] = new JsonObject { ["Nested"] = PrivateCanary };
        File.WriteAllText(Path.Join(SourceDirectory, "appsettings.json"), appsettings.ToJsonString());
        _sourceBytes = Directory.GetFiles(SourceDirectory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
    }

    /// <summary>
    /// Copies the real Workbench composition into this fixture and trims its shell to the small reviewed set
    /// needed by the public environment-input proof. The actual host closure remains the built Workbench.
    /// </summary>
    public void UseWorkbenchFiles()
    {
        foreach (var file in Directory.GetFiles(SourceDirectory))
            File.Delete(file);

        foreach (var name in new[] { "shells.json", "shells.Production.json", "appsettings.json", "appsettings.Production.json" })
            File.Copy(Path.Join(DotnetElsa.WorkbenchSource(), name), Path.Join(SourceDirectory, name));

        var workbenchHostPath = _directory.File("workbench-host");
        _workbenchHostDirectory = Path.Join(ResolveExistingPath(Path.GetDirectoryName(workbenchHostPath)!),
            Path.GetFileName(workbenchHostPath));
        var actualWorkbenchDirectory = DotnetElsa.Workbench();
        CopyActualWorkbenchClosure(actualWorkbenchDirectory, _workbenchHostDirectory);
        foreach (var fileName in new[] { "Elsa.Workbench.dll", "Elsa.Workbench.deps.json", "Elsa.Workbench.runtimeconfig.json" })
            Assert.Equal(File.ReadAllBytes(Path.Join(actualWorkbenchDirectory, fileName)),
                File.ReadAllBytes(Path.Join(_workbenchHostDirectory, fileName)));

        var shells = JsonNode.Parse(File.ReadAllText(Path.Join(SourceDirectory, "shells.json")))!.AsObject();
        var shell = shells["CShells"]!["Shells"]![ShellId]!.AsObject();
        shell["Features"] = new JsonObject
        {
            [WorkbenchModularityApiFeatureId] = new JsonObject(),
            [WorkbenchRuntimeFaultStackTraceFeatureId] = new JsonObject(),
            [StructuredLogsFeatureId] = new JsonObject(),
            [StructuredLogsEfFeatureId] = new JsonObject()
        };
        File.WriteAllText(Path.Join(SourceDirectory, "shells.json"), shells.ToJsonString());

        var productionShells = JsonNode.Parse(File.ReadAllText(Path.Join(SourceDirectory, "shells.Production.json")))!.AsObject();
        productionShells["CShells"]!["Shells"]![ShellId]!["Features"] = new JsonObject();
        File.WriteAllText(Path.Join(SourceDirectory, "shells.Production.json"), productionShells.ToJsonString());

        var appsettings = JsonNode.Parse(File.ReadAllText(Path.Join(SourceDirectory, "appsettings.json")))!.AsObject();
        appsettings["ConnectionStrings"]!["DeclaredConnection"] =
            $"Data Source={DatabasePath};Password={PrivateEnvironmentCanary}";
        appsettings["ConnectionStrings"]![OverlayEnvironmentConnection] = "";
        appsettings["Elsa"]!["Persistence"]!["DefaultResource"] = PublicEnvironmentResource;
        appsettings["Elsa"]!["Persistence"]!["Resources"] = new JsonObject
        {
            [PublicEnvironmentResource] = new JsonObject
            {
                ["Provider"] = "Sqlite",
                ["ConnectionName"] = PublicEnvironmentConnection
            },
            [OverlayEnvironmentResource] = new JsonObject
            {
                ["Provider"] = "Sqlite",
                ["ConnectionName"] = OverlayEnvironmentConnection
            }
        };
        File.WriteAllText(Path.Join(SourceDirectory, "appsettings.json"), appsettings.ToJsonString());
        _sourceBytes = Directory.GetFiles(SourceDirectory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
    }

    private static void CopyActualWorkbenchClosure(string source, string destination)
    {
        if (IsReparsePoint(source))
            throw new InvalidOperationException($"The actual Workbench output is a reparse point: {source}");

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (string.Equals(Path.GetFileName(file), ".nuplane", StringComparison.Ordinal))
                continue;
            if (IsReparsePoint(file))
                throw new InvalidOperationException($"The actual Workbench output contains a reparse point: {file}");
            File.Copy(file, Path.Join(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            if (string.Equals(Path.GetFileName(directory), ".nuplane", StringComparison.Ordinal))
                continue;
            if (IsReparsePoint(directory))
                throw new InvalidOperationException($"The actual Workbench output contains a reparse point: {directory}");
            CopyActualWorkbenchClosure(directory, Path.Join(destination, Path.GetFileName(directory)));
        }

        static bool IsReparsePoint(string path) =>
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static string ResolveExistingPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        foreach (var component in fullPath[root.Length..].Split(Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Join(current, component);
            var directory = new DirectoryInfo(candidate);
            current = (directory.Attributes & FileAttributes.ReparsePoint) != 0
                ? directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ??
                  throw new InvalidOperationException("The fixture temporary directory has an unresolved reparse point.")
                : candidate;
        }

        return current;
    }

    public void WriteBundledCatalog() => WriteInput("catalog.json", JsonSerializer.Serialize(FoundationSelectionCatalog.Load(), Json));

    public string WriteWorkspaceStart()
    {
        var (path, definition) = WriteWorkspaceProfile("profile.json", "candidate-local", "1",
            [OpenTelemetryFeatureId, OpenTelemetryEfFeatureId, ResourceProbeFeatureId],
            "Reviewed workspace starting selection");
        var catalog = FoundationSelectionCatalog.Load();
        var authored = new AuthoredComposition("1", new CatalogPin(catalog.Id, catalog.Version, catalog.Digest),
            new DefinitionReference("workspace", "profile", definition.Id, definition.Version, definition.Digest),
            [], [], [], new AcceptedSelection(catalog.Digest, [], []), null, null);
        // This input is intentionally edited next; only frozen inspection inputs are tracked.
        File.WriteAllText(InputPath("authored.json"), JsonSerializer.Serialize(authored, Json));
        return path;
    }

    public (string Path, SelectionDefinition Definition) WriteWorkspaceProfile(string name, string id, string version,
        IEnumerable<string> members, string rationale = "Reviewed workspace profile fixture")
    {
        var draft = new SelectionDefinition("profile", id, version, new string('0', 64), [.. members],
            rationale, "Local candidate", "Local source-backed selection", []);
        var definition = draft with { Digest = SelectionDigest.ComputeDefinitionDigest(draft) };
        var document = JsonSerializer.SerializeToNode(definition, Json)!.AsObject();
        document["schemaVersion"] = "1";
        var path = InputPath(name);
        WriteInput(name, document.ToJsonString());
        return (path, definition);
    }

    public void AddReviewedInspectionSetting()
    {
        var source = JsonNode.Parse(File.ReadAllText(Path.Join(SourceDirectory, "shells.json")))!;
        source["CShells"]!["Shells"]![ShellId]!["Features"]![ResourceProbeFeatureId]!["InspectionLabel"] = "source-label";
        File.WriteAllText(Path.Join(SourceDirectory, "shells.json"), source.ToJsonString());
        _sourceBytes = Directory.GetFiles(SourceDirectory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        WriteInput("setting-review.json", $$"""
            {"schemaVersion":"1","fields":[{"featureId":"{{ResourceProbeFeatureId}}","pointer":"/InspectionLabel","type":"string","portable":true}]}
            """);
    }

    public void EditReviewedInspectionSetting()
    {
        var authored = JsonNode.Parse(File.ReadAllText(InputPath("authored.json")))!;
        authored["settings"] = new JsonObject
        {
            [ResourceProbeFeatureId] = new JsonObject { ["InspectionLabel"] = "reviewed-label" }
        };
        File.WriteAllText(InputPath("authored.json"), authored.ToJsonString());
    }

    public void EditDiagnosticSelection()
    {
        var authored = JsonNode.Parse(File.ReadAllText(InputPath("authored.json")))!;
        authored["add"]!.AsArray().Add(StructuredLogsFeatureId);
        authored["add"]!.AsArray().Add(StructuredLogsEfFeatureId);
        authored["remove"]!.AsArray().Add(OpenTelemetryEfFeatureId);
        File.WriteAllText(InputPath("authored.json"), authored.ToJsonString());
    }

    public void TrackAcceptedInput(string? path = null)
    {
        var acceptedPath = path ?? InputPath("accepted.json");
        acceptedPath = Path.GetFullPath(acceptedPath);
        _initialInputBytes[acceptedPath] = File.ReadAllBytes(acceptedPath);
    }

    public string[] InspectionArguments(string? format = null, IReadOnlyList<string>? workspaceProfiles = null, bool trust = true,
        int? timeoutSeconds = null, string? settingReviewPath = null, string? hostDirectory = null,
        IReadOnlyList<string>? packageRoots = null, string? environmentInputPath = null, string? compositionPath = null)
    {
        if (timeoutSeconds is < 1 or > 300)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Inspection timeout must be between 1 and 300 seconds.");
        var arguments = new List<string>
        {
            "composition", "inspect", "--host", hostDirectory ?? HostAssemblyDirectory, "--host-dir", SourceDirectory,
            "--shell", ShellId, "--environment", Environment, "--composition", compositionPath ?? InputPath("accepted.json"),
        };
        if (format is not null)
            arguments.AddRange(["--format", format]);
        if (workspaceProfiles is { Count: > 0 })
            foreach (var profile in workspaceProfiles)
                arguments.AddRange(["--workspace-profile", profile]);
        else
            arguments.AddRange(["--catalog", InputPath("catalog.json")]);
        if (settingReviewPath is not null)
            arguments.AddRange(["--setting-review", settingReviewPath]);
        if (environmentInputPath is not null)
            arguments.AddRange(["--environment-input", environmentInputPath]);
        foreach (var packageRoot in packageRoots ?? [])
            arguments.AddRange(["--packages", packageRoot]);
        if (timeoutSeconds is { } seconds)
            arguments.AddRange(["--timeout-seconds", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (trust)
            arguments.Add("--trust-host-code");
        return [.. arguments];
    }

    public Task<PseudoTerminalCliRun> RunGenerateInteractiveAsync(string workspaceProfilePath, string settingReviewPath)
    {
        var arguments = new List<string>
        {
            "composition", "generate", "--host-dir", SourceDirectory, "--shell", ShellId,
            "--environment", Environment, "--catalog", InputPath("catalog.json"),
            "--composition", InputPath("accepted.json"), "--setting-review", settingReviewPath,
            "--workspace-profile", workspaceProfilePath, "--output-dir", CandidateOutputDirectory
        };
        return PseudoTerminalCli.RunElsaAsync(
            "Type generate to write the candidate: ", "generate", arguments);
    }

    public string InputPath(string name)
    {
        if (Path.GetFileName(name) != name)
            throw new ArgumentException("An input fixture name must be a file name.", nameof(name));
        return Path.Join(InputDirectory, name);
    }

    public void WriteInput(string name, string contents)
    {
        var path = InputPath(name);
        File.WriteAllText(path, contents);
        _initialInputBytes[Path.GetFullPath(path)] = File.ReadAllBytes(path);
    }

    public string WriteEnvironmentInput(params (string Key, string Value)[] entries) =>
        WriteEnvironmentInput("environment-input.json", entries);

    public string WriteEnvironmentInput(string name, params (string Key, string Value)[] entries)
    {
        var path = InputPath(name);
        File.WriteAllBytes(path, EnvironmentDocument(entries));
        _initialInputBytes[Path.GetFullPath(path)] = File.ReadAllBytes(path);
        return path;
    }

    /// <summary>Captures all supported source files, including siblings that must remain unchanged.</summary>
    public CompositionFileSource CaptureSource() => CompositionFileSource.Open(SourceDirectory, ShellId, Environment);

    /// <summary>Asserts that authored/catalog/profile fixture inputs were not changed by an inspection journey.</summary>
    public void AssertInputsUnchanged()
    {
        foreach (var (path, expected) in _initialInputBytes)
            Assert.True(File.Exists(path) && expected.AsSpan().SequenceEqual(File.ReadAllBytes(path)),
                "A supplied candidate-inspection input changed during the test journey.");
    }

    public void AssertSourcesUnchanged()
    {
        Assert.Equal(_sourceBytes.Keys.Order(), Directory.GetFiles(SourceDirectory).Select(Path.GetFileName).Order());
        foreach (var (name, bytes) in _sourceBytes)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Join(SourceDirectory, name)));
        Assert.False(File.Exists(DatabasePath));
        Assert.False(File.Exists(ContextMarkerPath));
        Assert.False(File.Exists(ActionMarkerPath));
    }

    public void Dispose() => _directory.Dispose();
}
