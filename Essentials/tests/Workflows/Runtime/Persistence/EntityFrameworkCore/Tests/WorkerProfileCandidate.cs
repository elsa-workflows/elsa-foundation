using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Elsa.Cli.Tests;
using Elsa.Modularity.Planning.Bridge;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Produces the worker actor's source and candidate through the same CLI workflow an operator uses.</summary>
internal sealed record WorkerProfileCandidate(
    string SourceDirectory,
    string CandidateDirectory,
    string AuthoredPath,
    string ShellId,
    string Environment,
    string Audience,
    string[] FeatureIds,
    (string CatalogId, string CatalogVersion, string CatalogDigest, string ProfileId, string ProfileVersion, string ProfileDigest) Identity,
    IReadOnlyDictionary<string, string> ConsumedFileHashes,
    string[] Findings)
{
    public const string DefaultShellId = "worker-oidc-runtime";
    public const string CandidateEnvironment = "Development";
    public const string DefaultProfileId = "worker-http";
    public const string DefaultProfileVersion = "1";
    public const string ControlAudience = "worker-api-candidate";
    private const string ExpectedCatalogDigest = "6563d77f116b7aefb2a67425f28b72cab736297d4e46df964bf9fb507cf91c3c";
    private const string ExpectedProfileDigest = "e46f8092771171ad63b0ca81bf307f5ad7ad7a9ca4929dd311bfac6af3cf8fa1";

    private static readonly string[] s_workerFeatures =
    [
        "ActivitiesControlFlow",
        "ActivitiesPrimitives",
        "ActivitiesRuntime",
        "ActivitiesSequence",
        "ApiCapabilities",
        "Events",
        "Expressions",
        "FileSystemDistributedLocking",
        "FoundationIdentityAbstractions",
        "FoundationIdentityOidc",
        "IdentityIamEntityFrameworkCore",
        "Mediator",
        "Primitives",
        "Serialization",
        "Tasks",
        "WorkflowsRuntimeApi",
        "WorkflowsRuntimeEntityFrameworkCore",
        "WorkflowsRuntimeResumption",
        "WorkflowsRuntimeTriggers"
    ];

    private static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string CatalogId => Identity.CatalogId;
    public string CatalogVersion => Identity.CatalogVersion;
    public string CatalogDigest => Identity.CatalogDigest;
    public string ProfileId => Identity.ProfileId;
    public string ProfileVersion => Identity.ProfileVersion;
    public string ProfileDigest => Identity.ProfileDigest;

    public static Task<WorkerProfileCandidate> CreatePrimaryAsync(
        string root,
        string authority,
        string runtimeDatabasePath,
        string iamDatabasePath,
        string locksDirectory) => CreateAsync(
            root, authority, runtimeDatabasePath, iamDatabasePath, locksDirectory, WorkerOidcHostFixture.Audience, sourceCompositionPath: null,
            removeControlFlow: false);

    public Task<WorkerProfileCandidate> CreateControlAsync(
        string root,
        string authority,
        string runtimeDatabasePath,
        string iamDatabasePath,
        string locksDirectory) => CreateAsync(
            root, authority, runtimeDatabasePath, iamDatabasePath, locksDirectory, ControlAudience,
            AuthoredPath, removeControlFlow: true);

    public static string[] PrimaryFeatureIds => [.. s_workerFeatures.Order(StringComparer.Ordinal)];

    private static async Task<WorkerProfileCandidate> CreateAsync(
        string root,
        string authority,
        string runtimeDatabasePath,
        string iamDatabasePath,
        string locksDirectory,
        string audience,
        string? sourceCompositionPath,
        bool removeControlFlow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);
        var stem = removeControlFlow ? "worker-control" : "worker-primary";
        var authoredPath = Path.Join(root, $"{stem}.authored.json");
        var acceptedPath = Path.Join(root, $"{stem}.accepted.json");
        var sourceDirectory = Path.Join(root, $"{stem}-source");
        var candidateDirectory = Path.Join(root, $"{stem}-candidate");
        Directory.CreateDirectory(sourceDirectory);

        if (sourceCompositionPath is null)
        {
            var initialized = DotnetElsa.Run("composition", "init", "--profile", $"{DefaultProfileId}@{DefaultProfileVersion}", "--output", authoredPath);
            Assert.Equal(0, initialized.ExitCode);
        }
        else
        {
            var authored = JsonNode.Parse(await File.ReadAllTextAsync(sourceCompositionPath))!.AsObject();
            var initialIdentity = ReadIdentity(authored);
            var initialAcceptedIds = Strings(authored["accepted"]!["featureIds"]!.AsArray());
            Assert.Equal(PrimaryFeatureIds, initialAcceptedIds);
            Assert.Equal(DefaultProfileId, initialIdentity.ProfileId);
            Assert.Equal(DefaultProfileVersion, initialIdentity.ProfileVersion);
            authored["remove"] = new JsonArray(JsonValue.Create("ActivitiesControlFlow"));
            AssertPinnedIdentity(JsonNode.Parse(await File.ReadAllTextAsync(sourceCompositionPath))!.AsObject(), authored);
            await File.WriteAllTextAsync(authoredPath, authored.ToJsonString(s_json));
        }

        // The authored portable layer stays empty. OIDC, IAM, Runtime and locking values belong to the
        // source host files and must survive the generator as local configuration.
        var authoredNode = JsonNode.Parse(await File.ReadAllTextAsync(authoredPath))!.AsObject();
        authoredNode["settings"] = new JsonObject();
        authoredNode["resources"] = null;
        await File.WriteAllTextAsync(authoredPath, authoredNode.ToJsonString(s_json));
        Assert.Null(authoredNode["resources"]);

        var expected = removeControlFlow
            ? s_workerFeatures.Where(id => id != "ActivitiesControlFlow").Order(StringComparer.Ordinal).ToArray()
            : PrimaryFeatureIds;
        var plan = DotnetElsa.Run("composition", "plan", "--composition", authoredPath, "--format", "json");
        Assert.Equal(0, plan.ExitCode);
        using var planDocument = JsonDocument.Parse(plan.Output);
        var planRoot = planDocument.RootElement;
        Assert.Equal(expected, ReadIds(planRoot.GetProperty("candidate").GetProperty("featureIds")));
        Assert.Equal(removeControlFlow ? PrimaryFeatureIds : expected,
            ReadIds(planRoot.GetProperty("accepted").GetProperty("featureIds")));
        var findings = planRoot.GetProperty("findings").EnumerateArray()
            .Select(item => item.GetProperty("code").GetString()!)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Contains("inventory-unverified", findings);
        Assert.Contains("persistence-unverified", findings);
        Assert.Empty(authoredNode["settings"]!.AsObject());

        var accepted = await PseudoTerminalCli.RunElsaAsync(
            "Type accept to write the accepted composition: ", "accept",
            ["composition", "accept", "--composition", authoredPath, "--output", acceptedPath]);
        AssertInteractiveSuccess(accepted);
        var acceptedNode = JsonNode.Parse(await File.ReadAllTextAsync(acceptedPath))!.AsObject();
        Assert.Equal(expected, Strings(acceptedNode["accepted"]!["featureIds"]!.AsArray()));
        AssertPinnedIdentity(authoredNode, acceptedNode);
        Assert.Empty(acceptedNode["settings"]!.AsObject());
        Assert.Null(acceptedNode["resources"]);

        WriteSourceFiles(sourceDirectory, authority, audience, runtimeDatabasePath, iamDatabasePath, locksDirectory);
        var sourceFiles = ReadSourceFiles(sourceDirectory);

        Assert.False(Directory.Exists(candidateDirectory));
        var generated = await PseudoTerminalCli.RunElsaAsync(
            "Type generate to write the candidate: ", "generate",
            ["composition", "generate", "--host-dir", sourceDirectory, "--shell", DefaultShellId,
                "--environment", CandidateEnvironment, "--composition", acceptedPath, "--output-dir", candidateDirectory]);
        AssertInteractiveSuccess(generated);
        AssertSourceUnchanged(sourceFiles, sourceDirectory);
        Assert.True(Directory.Exists(candidateDirectory));

        var baseShellBytes = await File.ReadAllBytesAsync(Path.Join(candidateDirectory, "shells.json"));
        var overlayShellBytes = await File.ReadAllBytesAsync(Path.Join(candidateDirectory, $"shells.{CandidateEnvironment}.json"));
        var readback = CshellsSourceReader.Read(
            Encoding.UTF8.GetString(baseShellBytes), Encoding.UTF8.GetString(overlayShellBytes), DefaultShellId);
        Assert.Equal(expected, readback.EnabledFeatureIds);
        if (removeControlFlow)
            Assert.Equal(new[] { "ActivitiesControlFlow" }, readback.DisabledFeatureIds);
        else
            Assert.Empty(readback.DisabledFeatureIds);
        AssertSourceSettings(readback, authority, audience);
        AssertCandidateAppsettings(candidateDirectory, runtimeDatabasePath, iamDatabasePath);

        var acceptedPlan = DotnetElsa.Run("composition", "plan", "--composition", acceptedPath, "--format", "json");
        Assert.Equal(0, acceptedPlan.ExitCode);
        using var acceptedPlanDocument = JsonDocument.Parse(acceptedPlan.Output);
        Assert.Equal(expected, ReadIds(acceptedPlanDocument.RootElement.GetProperty("candidate").GetProperty("featureIds")));
        Assert.Equal(expected, ReadIds(acceptedPlanDocument.RootElement.GetProperty("accepted").GetProperty("featureIds")));
        var identity = ReadIdentity(authoredNode);
        var acceptedIdentity = ReadIdentity(acceptedNode);
        Assert.Equal("elsa-foundation", identity.CatalogId);
        Assert.Equal("3", identity.CatalogVersion);
        Assert.Equal(ExpectedCatalogDigest, identity.CatalogDigest);
        Assert.Equal(DefaultProfileId, identity.ProfileId);
        Assert.Equal(DefaultProfileVersion, identity.ProfileVersion);
        Assert.Equal(ExpectedProfileDigest, identity.ProfileDigest);
        Assert.Equal(identity, acceptedIdentity);
        var plannedCatalog = acceptedPlanDocument.RootElement.GetProperty("catalog");
        Assert.Equal(identity.CatalogId, plannedCatalog.GetProperty("id").GetString());
        Assert.Equal(identity.CatalogVersion, plannedCatalog.GetProperty("version").GetString());
        Assert.Equal(identity.CatalogDigest, plannedCatalog.GetProperty("digest").GetString());

        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in SelectedCandidateFiles())
        {
            var bytes = await File.ReadAllBytesAsync(Path.Join(candidateDirectory, name));
            hashes.Add(name, Convert.ToHexString(SHA256.HashData(bytes)));
        }

        return new WorkerProfileCandidate(
            sourceDirectory, candidateDirectory, authoredPath, DefaultShellId, CandidateEnvironment, audience, expected,
            acceptedIdentity, hashes, findings);
    }

    public static string[] SelectedCandidateFiles() =>
        ["appsettings.json", $"appsettings.{CandidateEnvironment}.json", "shells.json", $"shells.{CandidateEnvironment}.json"];

    public static string HashAudience(string audience) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(audience)));

    private static void AssertInteractiveSuccess(PseudoTerminalCliRun run)
    {
        var code = Regex.Match(run.Output + run.Error, @"error \[([a-z0-9-]{1,96})\]").Groups[1].Value;
        Assert.True(run.ExitCode == 0 && run.ResponseSent && !run.TimedOut,
            $"Interactive CLI exit={run.ExitCode}, refusalCode={code}, responseSent={run.ResponseSent}, timedOut={run.TimedOut}.");
    }

    private static void WriteSourceFiles(
        string directory,
        string authority,
        string audience,
        string runtimeDatabasePath,
        string iamDatabasePath,
        string locksDirectory)
    {
        var features = new JsonObject();
        foreach (var feature in s_workerFeatures)
            features[feature] = new JsonObject();
        features["FoundationIdentityOidc"] = new JsonObject
        {
            ["Authority"] = authority,
            ["Audience"] = audience,
            ["ProviderId"] = WorkerOidcHostFixture.ProviderId,
            ["TenantId"] = WorkerOidcHostFixture.TenantId,
            ["NormalizeBearerClaims"] = true,
            ["RequireHttpsMetadata"] = false,
            ["IsDefault"] = true
        };
        features["IdentityIamEntityFrameworkCore"] = new JsonObject
        {
            ["Provider"] = "Sqlite",
            ["ConnectionName"] = "Iam"
        };
        features["WorkflowsRuntimeEntityFrameworkCore"] = new JsonObject
        {
            ["RecoveryContinuationSigningKey"] = "worker-oidc-runtime-recovery-signing-key-32-bytes",
            ["HierarchyCursorSigningKey"] = "worker-oidc-runtime-hierarchy-signing-key-32-bytes"
        };
        features["FileSystemDistributedLocking"] = new JsonObject { ["LocksFolderPath"] = locksDirectory };

        var baseShell = new JsonObject
        {
            ["CShells"] = new JsonObject
            {
                ["Shells"] = new JsonObject
                {
                    [DefaultShellId] = new JsonObject
                    {
                        ["Features"] = features,
                        ["Configuration"] = new JsonObject { ["WebRouting"] = new JsonObject { ["Path"] = "" } }
                    }
                }
            }
        };
        var shellOverlay = new JsonObject
        {
            ["CShells"] = new JsonObject
            {
                ["Shells"] = new JsonObject
                {
                    [DefaultShellId] = new JsonObject
                    {
                        ["Configuration"] = new JsonObject { ["WorkerProfileCandidate"] = new JsonObject { ["Layer"] = "environment" } }
                    }
                }
            }
        };
        var appsettings = new JsonObject
        {
            ["Elsa"] = new JsonObject
            {
                ["Persistence"] = new JsonObject
                {
                    ["DefaultResource"] = "primary",
                    ["Resources"] = new JsonObject
                    {
                        ["primary"] = new JsonObject { ["Provider"] = "Sqlite", ["ConnectionName"] = "WorkerRuntime" }
                    }
                }
            },
            ["ConnectionStrings"] = new JsonObject
            {
                ["WorkerRuntime"] = $"Data Source={runtimeDatabasePath};Pooling=False",
                ["Iam"] = $"Data Source={iamDatabasePath};Pooling=False"
            }
        };
        var appsettingsOverlay = new JsonObject { ["WorkerProfileCandidate"] = new JsonObject { ["Layer"] = "environment" } };

        File.WriteAllText(Path.Join(directory, "shells.json"), baseShell.ToJsonString(s_json));
        File.WriteAllText(Path.Join(directory, $"shells.{CandidateEnvironment}.json"), shellOverlay.ToJsonString(s_json));
        File.WriteAllText(Path.Join(directory, "appsettings.json"), appsettings.ToJsonString(s_json));
        File.WriteAllText(Path.Join(directory, $"appsettings.{CandidateEnvironment}.json"), appsettingsOverlay.ToJsonString(s_json));
    }

    private static string[] ReadIds(JsonElement value) => [.. value.EnumerateArray().Select(item => item.GetString()!)];

    private static string[] Strings(JsonArray values) => [.. values.Select(item => item!.GetValue<string>()).Order(StringComparer.Ordinal)];

    private static (string CatalogId, string CatalogVersion, string CatalogDigest, string ProfileId, string ProfileVersion, string ProfileDigest)
        ReadIdentity(JsonObject composition)
    {
        var catalog = composition["catalog"]!.AsObject();
        var profile = composition["profile"]!.AsObject();
        return (catalog["id"]!.GetValue<string>(), catalog["version"]!.GetValue<string>(), catalog["digest"]!.GetValue<string>(),
            profile["id"]!.GetValue<string>(), profile["version"]!.GetValue<string>(), profile["digest"]!.GetValue<string>());
    }

    private static void AssertPinnedIdentity(JsonObject authored, JsonObject accepted)
    {
        Assert.Equal(ReadIdentity(authored), ReadIdentity(accepted));
    }

    private static void AssertSourceSettings(CshellsSource source, string authority, string audience)
    {
        AssertSetting(source, "FoundationIdentityOidc", "/Authority", JsonValueKind.String, authority);
        AssertSetting(source, "FoundationIdentityOidc", "/Audience", JsonValueKind.String, audience);
        AssertSetting(source, "FoundationIdentityOidc", "/ProviderId", JsonValueKind.String, WorkerOidcHostFixture.ProviderId);
        AssertSetting(source, "FoundationIdentityOidc", "/TenantId", JsonValueKind.String, WorkerOidcHostFixture.TenantId);
        AssertSetting(source, "FoundationIdentityOidc", "/NormalizeBearerClaims", JsonValueKind.True);
        AssertSetting(source, "FoundationIdentityOidc", "/RequireHttpsMetadata", JsonValueKind.False);
        AssertSetting(source, "FoundationIdentityOidc", "/IsDefault", JsonValueKind.True);
        Assert.DoesNotContain(source.SettingLeaves, leaf => leaf.FeatureId == "FoundationIdentityOidc" && leaf.Pointer == "/ClientId");
        AssertSetting(source, "IdentityIamEntityFrameworkCore", "/Provider", JsonValueKind.String, "Sqlite");
        AssertSetting(source, "IdentityIamEntityFrameworkCore", "/ConnectionName", JsonValueKind.String, "Iam");
        AssertSetting(source, "WorkflowsRuntimeEntityFrameworkCore", "/RecoveryContinuationSigningKey", JsonValueKind.String);
        AssertSetting(source, "FileSystemDistributedLocking", "/LocksFolderPath", JsonValueKind.String);
    }

    private static void AssertCandidateAppsettings(string directory, string runtimeDatabasePath, string iamDatabasePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Join(directory, "appsettings.json")));
        var persistence = document.RootElement.GetProperty("Elsa").GetProperty("Persistence");
        Assert.Equal("primary", persistence.GetProperty("DefaultResource").GetString());
        Assert.Equal("Sqlite", persistence.GetProperty("Resources").GetProperty("primary").GetProperty("Provider").GetString());
        Assert.Equal("WorkerRuntime", persistence.GetProperty("Resources").GetProperty("primary").GetProperty("ConnectionName").GetString());
        var connections = document.RootElement.GetProperty("ConnectionStrings");
        Assert.Equal($"Data Source={runtimeDatabasePath};Pooling=False", connections.GetProperty("WorkerRuntime").GetString());
        Assert.Equal($"Data Source={iamDatabasePath};Pooling=False", connections.GetProperty("Iam").GetString());
        Assert.NotEqual(connections.GetProperty("WorkerRuntime").GetString(), connections.GetProperty("Iam").GetString());
    }

    private static void AssertSetting(CshellsSource source, string feature, string pointer, JsonValueKind kind, string? expectedString = null)
    {
        var value = Assert.Single(source.SettingLeaves, leaf => leaf.FeatureId == feature && leaf.Pointer == pointer).Value;
        Assert.Equal(kind, value.ValueKind);
        if (expectedString is not null)
            Assert.Equal(expectedString, value.GetString());
    }

    private static IReadOnlyDictionary<string, byte[]> ReadSourceFiles(string directory) =>
        SelectedCandidateFiles().ToDictionary(name => name, name => File.ReadAllBytes(Path.Join(directory, name)), StringComparer.Ordinal);

    private static void AssertSourceUnchanged(IReadOnlyDictionary<string, byte[]> before, string directory)
    {
        foreach (var (name, bytes) in before)
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Join(directory, name))), $"Source file {name} changed during generation.");
    }
}
