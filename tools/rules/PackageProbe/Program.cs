using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Elsa.Persistence.EntityFramework;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Runtime;
using Elsa.Tasks.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NuGet.Packaging;
using NuGet.Versioning;

internal static partial class Program
{
    private const string ReceiptFileName = "elsa.rules.deployment-receipt.json";
    private const string PublishStateEnvironmentVariable = "ELSA_RULES_PUBLISH_STATE_COMMIT";
    private const long MaxArchiveBytes = 1_073_741_824;
    private const long MaxAssetBytes = 256L * 1024 * 1024;
    private const long MaxTotalAssetBytes = 512L * 1024 * 1024;
    private const int MaxDepsBytes = 32 * 1024 * 1024;
    private const int MaxRuntimeConfigBytes = 1 * 1024 * 1024;
    private const int MaxNuspecBytes = 1 * 1024 * 1024;
    private const int MaxTextBytes = 4 * 1024;
    private const int MaxJsonDepth = 64;
    private const int MaxJsonNodes = 1_000_000;
    private const int MaxFeedEntries = 10_000;
    private const int MaxSelectedAssets = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: PackageProbe <receipt|direct|unavailable|incompatible> <arguments...> <output-json>");
            return 2;
        }

        var command = args[0];
        var outputPath = args[^1];
        try
        {
            object result = command switch
            {
                "process-boundary" when args.Length == 4 => await RunProcessBoundaryAsync(args[1], args[2]),
                "process-recover" when args.Length == 3 => await RunProcessRecoverAsync(args[1]),
                "process-cancel" when args.Length == 3 => await RunProcessCancelAsync(args[1]),
                "receipt" when args.Length == 3 => await CreateReceiptAsync(args[1]),
                "direct" when args.Length == 3 => await RunDirectAsync(args[1]),
                "unavailable" when args.Length == 3 => await RunUnavailableAsync(args[1]),
                "incompatible" when args.Length == 3 => await RunIncompatibleAsync(args[1]),
                _ => throw new ProbeAssertionException("Invalid command or argument count.")
            };

            WriteJson(outputPath, result);
            Console.WriteLine($"{command}: passed; result={Path.GetFullPath(outputPath)}");
            return 0;
        }
        catch (Exception exception)
        {
            var error = exception is ProbeAssertionException
                ? exception.Message
                : $"{exception.GetType().Name}: {exception.Message}";
            try
            {
                WriteJson(outputPath, new { command, passed = false, error });
            }
            catch (Exception writeException)
            {
                Console.Error.WriteLine($"Could not write result JSON: {writeException.GetType().Name}");
            }

            Console.Error.WriteLine($"{command}: failed; {error}");
            return 1;
        }
    }

    private static async Task<object> CreateReceiptAsync(string feedDirectory)
    {
        Require(Path.IsPathFullyQualified(feedDirectory), "Feed directory must be an absolute path.");
        var feedRoot = Path.GetFullPath(feedDirectory);
        Require(Directory.Exists(feedRoot), "Package feed does not exist.");

        var entryAssembly = Assembly.GetEntryAssembly() ?? throw new ProbeAssertionException("Entry assembly is unavailable.");
        Require(!string.IsNullOrWhiteSpace(entryAssembly.Location), "The consumer must be file-based.");
        var hostRoot = ResolvePhysicalPath(AppContext.BaseDirectory);
        var depsPath = Path.ChangeExtension(entryAssembly.Location, ".deps.json");
        Require(File.Exists(depsPath), "The active consumer deps.json is missing.");
        var depsBytes = ReadBoundedFile(depsPath, MaxDepsBytes);
        using var depsDocument = JsonDocument.Parse(depsBytes, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
        Require(HasBoundedJsonShape(depsDocument.RootElement), "The active deps.json exceeds bounded JSON limits.");
        var graph = ReadDependencyGraph(depsDocument.RootElement);

        var packageAssemblies = new (string PackageId, Assembly Assembly)[]
        {
            ("Elsa.Rules.Core", typeof(RuleProgram).Assembly),
            ("Elsa.Rules.Runtime", typeof(RulesRuntimeFeature).Assembly),
            ("Elsa.Rules.Persistence.Core", typeof(IRuleRunStore).Assembly)
        };
        var requiredPackages = new List<ReceiptPackage>(packageAssemblies.Length);
        long totalAssetBytes = 0;
        string? sourceCommit = null;

        foreach (var (packageId, assembly) in packageAssemblies.OrderBy(item => item.PackageId, StringComparer.Ordinal))
        {
            Require(AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default,
                $"Required assembly for {packageId} is not loaded in the default context.");
            var location = assembly.Location;
            Require(!string.IsNullOrWhiteSpace(location), $"Required assembly for {packageId} has no file location.");
            var physicalAssembly = ResolvePhysicalPath(location);
            Require(IsUnderRoot(physicalAssembly, hostRoot), $"Required assembly for {packageId} is outside the published host root.");

            var dependency = graph.FindRequiredPackage(packageId);
            var assemblyFileName = Path.GetFileName(physicalAssembly);
            var candidatePaths = dependency.RuntimeAssetPaths
                .Where(path => StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(path), assemblyFileName))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Require(candidatePaths.Length == 1, $"The active deps target does not select one unambiguous runtime asset for {packageId}.");
            var assetPath = candidatePaths[0];
            var loadedFullName = assembly.GetName().FullName;
            Require(!string.IsNullOrWhiteSpace(loadedFullName), $"Required assembly for {packageId} has no full name.");
            var diskFullName = AssemblyName.GetAssemblyName(physicalAssembly).FullName;
            Require(StringComparer.Ordinal.Equals(loadedFullName, diskFullName), $"Loaded and on-disk assembly identities differ for {packageId}.");

            var feedPackage = FindFeedPackage(feedRoot, dependency.PackageId, dependency.Version);
            var cachePackage = GetCachedPackagePath(dependency.PackageId, dependency.Version);
            var feedPackageContentHash = GetNuGetContentHash(feedPackage.Path, dependency.PackageId, dependency.Version);
            var cachePackageContentHash = GetNuGetContentHash(cachePackage, dependency.PackageId, dependency.Version);
            var cacheMarkerPath = cachePackage + ".sha512";
            var cacheMarkerHash = NormalizeNuGetHash(ReadBoundedText(cacheMarkerPath, MaxTextBytes));
            Require(StringComparer.Ordinal.Equals(feedPackageContentHash, dependency.NuGetContentHash),
                $"Packed NuGet content hash differs from active deps.json for {packageId}.");
            Require(StringComparer.Ordinal.Equals(cachePackageContentHash, dependency.NuGetContentHash),
                $"Restored cache archive content hash differs from active deps.json for {packageId}.");
            Require(StringComparer.Ordinal.Equals(cacheMarkerHash, dependency.NuGetContentHash),
                $"Restored cache hash marker differs from active deps.json for {packageId}.");
            Require(StringComparer.Ordinal.Equals(feedPackageContentHash, cachePackageContentHash),
                $"Feed and restored package archives differ for {packageId}.");

            var nuspecCommit = ReadRepositoryCommit(feedPackage.Path);
            if (sourceCommit is null)
                sourceCommit = nuspecCommit;
            else
                Require(StringComparer.Ordinal.Equals(sourceCommit, nuspecCommit), "Rules packages have inconsistent source commits.");

            var packageAssetHash = await HashArchiveEntryAsync(feedPackage.Path, assetPath);
            var deployedAssetHash = await HashFileAsync(physicalAssembly, MaxAssetBytes, totalAssetBytes);
            totalAssetBytes = checked(totalAssetBytes + new FileInfo(physicalAssembly).Length);
            Require(totalAssetBytes <= MaxTotalAssetBytes, "Selected semantic assets exceed the total byte bound.");
            Require(StringComparer.Ordinal.Equals(packageAssetHash, deployedAssetHash),
                $"Selected deployed asset differs from the matching package asset for {packageId}.");

            var archiveHash = await HashArchiveAsync(feedPackage.Path);
            requiredPackages.Add(new ReceiptPackage(
                dependency.PackageId,
                dependency.Version,
                feedPackageContentHash,
                [new ReceiptAsset(assetPath, loadedFullName, deployedAssetHash)],
                new PackageProvenance(feedPackage.Path, cachePackage, archiveHash.Base64, archiveHash.Hex,
                    await HashArchiveAsync(cachePackage), [assetPath], nuspecCommit)));
        }

        Require(sourceCommit is not null && IsCommit(sourceCommit), "Packed Rules nuspecs do not provide a full immutable source commit.");
        var publishStateCommit = Environment.GetEnvironmentVariable(PublishStateEnvironmentVariable);
        Require(publishStateCommit is not null && IsCommit(publishStateCommit),
            $"{PublishStateEnvironmentVariable} must contain a full immutable commit SHA.");
        var runtimeProfile = ReadRuntimeProfile(entryAssembly);
        var launchSdkVersion = GetSdkVersion();
        var consumerBuildSdkVersion = Environment.GetEnvironmentVariable("ELSA_RULES_CONSUMER_BUILD_SDK");
        Require(!string.IsNullOrWhiteSpace(consumerBuildSdkVersion), "The coordinator must capture the consumer build SDK.");
        var packages = requiredPackages.OrderBy(package => package.Id, StringComparer.Ordinal)
            .Select(package => new
            {
                id = package.Id,
                version = package.Version,
                contentHash = package.ContentHash,
                assets = package.Assets.OrderBy(asset => asset.Path, StringComparer.Ordinal)
                    .Select(asset => new { path = asset.Path, assembly = asset.Assembly, sha256 = asset.Sha256 }).ToArray()
            }).ToArray();
        var receipt = new
        {
            receiptSchemaVersion = 1,
            sourceCommit,
            publishStateCommit,
            pin = new
            {
                pinSchemaVersion = 1,
                packages,
                languageId = RuleLanguage.Id,
                languageVersion = RuleLanguage.Version,
                receiptSchemaVersion = 1,
                stateSchemaVersion = 1
            },
            provenance = new
            {
                supportedSchemas = new
                {
                    artifactFormatVersion = RuleLanguage.FormatVersion,
                    invocationSchemaVersion = 1,
                    resultSchemaVersion = 1
                },
                dependencyResolution = new
                {
                    runtimeTarget = graph.RuntimeTarget,
                    resolvedLibraries = graph.ResolvedLibraries,
                    packages = requiredPackages.OrderBy(package => package.Id, StringComparer.Ordinal)
                        .Select(package => new
                        {
                            id = package.Id,
                            version = package.Version,
                            type = "package",
                            nuGetContentHash = package.ContentHash,
                            selectedRuntimeAssets = package.Provenance.SelectedRuntimeAssets,
                            feedArchive = package.Provenance.FeedArchive,
                            restoredCacheArchive = package.Provenance.CacheArchive,
                            rawArchiveSha512Base64 = package.Provenance.RawArchiveSha512Base64,
                            rawArchiveSha512Hex = package.Provenance.RawArchiveSha512Hex,
                            restoredArchiveSha512Base64 = package.Provenance.CacheArchiveSha512Base64,
                            restoredArchiveSha512Hex = package.Provenance.CacheArchiveSha512Hex,
                            packageSourceCommit = package.Provenance.SourceCommit
                        }).ToArray()
                },
                receiptGenerationRuntime = runtimeProfile,
                consumerBuildSdkVersion,
                launchSdkVersion,
                generatedUtc = DateTimeOffset.UtcNow
            }
        };

        var json = JsonSerializer.Serialize(receipt, JsonOptions) + "\n";
        var receiptPath = Path.Combine(hostRoot, ReceiptFileName);
        WriteTextAtomically(receiptPath, json);
        return receipt;
    }

    private static async Task<object> RunDirectAsync(string databasePath)
    {
        Require(Path.IsPathFullyQualified(databasePath), "Database path must be absolute.");
        var host = BuildHost(databasePath);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await host.StartAsync(timeout.Token);
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var factory = services.GetRequiredService<IRuleInvocationFactory>();
            var store = services.GetRequiredService<IRuleRunStore>();
            var reader = services.GetRequiredService<IRuleRunReader>();
            var identitySource = services.GetRequiredService<IRuleImplementationIdentitySource>();
            var trustedPin = await identitySource.ReadAsync(timeout.Token);
            Require(trustedPin is not null, "Direct consumer could not validate its deployed Rules identity receipt.");

            await using var inspection = new SqliteConnection(SqliteConnectionString(databasePath));
            await inspection.OpenAsync(timeout.Token);
            await using var versionCommand = inspection.CreateCommand();
            versionCommand.CommandText = "select sqlite_version()";
            var engineVersion = (string?)await versionCommand.ExecuteScalarAsync(timeout.Token);
            Require(!string.IsNullOrWhiteSpace(engineVersion), "The direct database must report its live SQLite version.");
            var program = Witness();
            var cases = new (string Name, int? RequestedRounds, int ExpectedRounds, RuleRunStatus ExpectedStatus)[]
            {
                ("default-budget", null, 7, RuleRunStatus.Succeeded),
                ("explicit-budget", 4, 4, RuleRunStatus.Succeeded),
                ("budget-fault", 3, 3, RuleRunStatus.Faulted)
            };
            var preparedRuns = new List<(string Name, RuleInvocationEnvelope Invocation, RuleRunStatus ExpectedStatus)>();
            foreach (var (name, requestedRounds, expectedRounds, expectedStatus) in cases)
            {
                var identity = NewIdentity(name);
                var prepared = await factory.PrepareAsync(identity, program, Facts("seed"), requestedRounds, cancellationToken: timeout.Token);
                Require(prepared.Disposition == RuleInvocationPreparationDisposition.Prepared && prepared.Invocation is not null,
                    $"{name} invocation did not prepare: {prepared.Disposition}.");
                Require(prepared.Invocation.MaxRounds == expectedRounds,
                    $"{name} invocation froze {prepared.Invocation.MaxRounds} rounds instead of {expectedRounds}.");
                Require(prepared.Invocation.InvocationSchemaVersion == 1 &&
                        prepared.Invocation.Artifact.FormatVersion == RuleLanguage.FormatVersion,
                    "Prepared invocation does not match the declared receipt schemas.");
                var admission = await store.AdmitAsync(new RuleRunAdmission(prepared.Invocation), timeout.Token);
                Require(admission.Disposition == RuleAdmissionDisposition.Admitted,
                    $"{name} invocation admission returned {admission.Disposition}.");
                preparedRuns.Add((name, prepared.Invocation, expectedStatus));
            }

            var observedRuns = new List<object>(preparedRuns.Count);
            foreach (var (name, invocation, expectedStatus) in preparedRuns)
            {
                var view = await WaitForTerminalAsync(reader, invocation.Identity, timeout.Token);
                var terminal = await reader.ReadTerminalAsync(invocation.Identity, timeout.Token);
                Require(terminal.Disposition == RuleTerminalReadDisposition.Found && terminal.Result is not null,
                    $"{name} did not produce one readable terminal result.");
                Require(view.Status == expectedStatus, $"{name} ended as {view.Status} instead of {expectedStatus}.");
                Require(terminal.Result.Kind == (expectedStatus == RuleRunStatus.Succeeded ? RuleTerminalKind.Succeeded : RuleTerminalKind.Faulted),
                    $"{name} terminal kind does not match its expected status.");
                Require(view.CommittedRounds == (name == "budget-fault" ? 3 : 4),
                    $"{name} committed an unexpected number of rounds.");

                Require(terminal.Result.ResultSchemaVersion == 1, "Terminal result schema differs from the deployment receipt.");
                var terminalFacts = FactAtoms(terminal.Result.ResultFacts);
                Require(terminalFacts.SequenceEqual(ExpectedFinalFacts, StringComparer.Ordinal),
                    $"{name} returned an unexpected terminal fact set.");
                if (name == "budget-fault")
                {
                    Require(terminal.Result.Diagnostic?.Code == "rules.round-budget-exhausted",
                        "Budget fault did not use the stable rules.round-budget-exhausted diagnostic.");
                }
                else
                {
                    Require(terminal.Result.Diagnostic is null, $"{name} unexpectedly carried a terminal diagnostic.");
                }

                var progress = await reader.ReadProgressAsync(invocation.Identity, null, RuleLimits.MaxPageEntries, timeout.Token);
                Require(progress.Disposition == RuleProgressReadDisposition.Page && progress.Page is not null,
                    $"{name} progress history was unavailable.");
                Require(progress.Page.Next is null, $"{name} unexpectedly exceeded one bounded history page.");
                var historyFacts = progress.Page.Items.Where(item => item.Kind == RuleProgressItemKind.Fact)
                    .Select(item => item.Fact!.Atom).OrderBy(atom => atom, StringComparer.Ordinal).ToArray();
                Require(historyFacts.SequenceEqual(ExpectedFinalFacts, StringComparer.Ordinal),
                    $"{name} fact history does not match its terminal facts.");
                var rounds = progress.Page.Items.Where(item => item.Kind == RuleProgressItemKind.Round)
                    .Select(item => item.Round!).OrderBy(round => round.Ordinal).ToArray();
                var expectedCommittedRounds = name == "budget-fault" ? 3 : 4;
                Require(rounds.Length == expectedCommittedRounds &&
                        rounds.Select(round => round.Ordinal).SequenceEqual(Enumerable.Range(0, expectedCommittedRounds)),
                    $"{name} does not have the expected contiguous committed round history.");
                Require(rounds.All(round => round.ConsumedModelRevision is not null &&
                    !string.IsNullOrWhiteSpace(round.AcceptedAttemptId) && !string.IsNullOrWhiteSpace(round.AcceptedWorkerId)),
                    $"{name} round history is missing accepted and consumed attempt evidence.");

                var replay = await store.AdmitAsync(new RuleRunAdmission(invocation), timeout.Token);
                Require(replay.Disposition == RuleAdmissionDisposition.Replay,
                    $"{name} exact immutable admission replay returned {replay.Disposition}.");
                observedRuns.Add(new
                {
                    name,
                    invocationId = invocation.Identity.InvocationId,
                    invocationSchemaVersion = invocation.InvocationSchemaVersion,
                    artifactFormatVersion = invocation.Artifact.FormatVersion,
                    resultSchemaVersion = terminal.Result.ResultSchemaVersion,
                    requestedRounds = name == "default-budget" ? (int?)null : invocation.MaxRounds,
                    resolvedRounds = invocation.MaxRounds,
                    status = view.Status.ToString(),
                    committedRounds = view.CommittedRounds,
                    terminalKind = terminal.Result.Kind.ToString(),
                    terminalFacts,
                    diagnostic = terminal.Result.Diagnostic?.Code,
                    historyFacts,
                    rounds = rounds.Select(round => new
                    {
                        ordinal = round.Ordinal,
                        acceptedAttemptId = round.AcceptedAttemptId,
                        acceptedWorkerId = round.AcceptedWorkerId,
                        acceptedModelRevision = round.AcceptedModelRevision,
                        consumedModelRevision = round.ConsumedModelRevision
                    }).ToArray(),
                    exactAdmissionReplay = replay.Disposition.ToString()
                });
            }

            await host.StopAsync(timeout.Token);
            return new
            {
                command = "direct",
                passed = true,
                executionRuntime = ReadRuntimeProfile(Assembly.GetEntryAssembly()!),
                databaseEngine = "SQLite",
                databaseEngineVersion = engineVersion,
                identityPinHash = RuleRecordCodec.PinHash(trustedPin),
                databasePath = Path.GetFullPath(databasePath),
                cases = observedRuns,
                packageReferenceOnly = true
            };
        }
        finally
        {
            host.Dispose();
        }
    }

    private static async Task<object> RunUnavailableAsync(string databasePath)
    {
        Require(Path.IsPathFullyQualified(databasePath), "Database path must be absolute.");
        var host = BuildHost(databasePath);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await host.StartAsync(timeout.Token);
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var identitySource = services.GetRequiredService<IRuleImplementationIdentitySource>();
            var installedPin = await identitySource.ReadAsync(timeout.Token);
            Require(installedPin is null, "Expected deployed implementation identity to be unavailable.");

            var identity = NewIdentity("unavailable-receipt");
            var preparation = await services.GetRequiredService<IRuleInvocationFactory>()
                .PrepareAsync(identity, Witness(), Facts("seed"), cancellationToken: timeout.Token);
            Require(preparation.Disposition == RuleInvocationPreparationDisposition.Unavailable && preparation.Invocation is null,
                $"Missing or mismatched receipt did not make preparation unavailable: {preparation.Disposition}.");
            var lookup = await services.GetRequiredService<IRuleRunReader>().FindAsync(identity, timeout.Token);
            Require(lookup.Disposition == RuleRunReadDisposition.NotFound,
                $"Unavailable preparation created or exposed durable state: {lookup.Disposition}.");
            await host.StopAsync(timeout.Token);
            return new
            {
                command = "unavailable",
                passed = true,
                identityAvailable = false,
                preparation = preparation.Disposition.ToString(),
                durableLookup = lookup.Disposition.ToString(),
                databasePath = Path.GetFullPath(databasePath)
            };
        }
        finally
        {
            host.Dispose();
        }
    }

    private static async Task<object> RunIncompatibleAsync(string databasePath)
    {
        Require(Path.IsPathFullyQualified(databasePath), "Database path must be absolute.");
        var host = BuildHost(databasePath);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await host.StartAsync(timeout.Token);
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var identitySource = services.GetRequiredService<IRuleImplementationIdentitySource>();
            var installedPin = await identitySource.ReadAsync(timeout.Token);
            Require(installedPin is not null, "Incompatible-pin control requires a valid deployed identity receipt.");
            var preparation = await services.GetRequiredService<IRuleInvocationFactory>()
                .PrepareAsync(NewIdentity("fabricated-incompatible-pin"), Witness(), Facts("seed"), cancellationToken: timeout.Token);
            Require(preparation.Disposition == RuleInvocationPreparationDisposition.Prepared && preparation.Invocation is not null,
                "Actual installed pin did not prepare a valid baseline invocation.");

            var changedPin = ReplaceFirstPackageVersion(installedPin);
            var baseline = preparation.Invocation;
            var incompatible = new RuleInvocationEnvelope(baseline.Identity, baseline.Artifact, baseline.InitialFacts,
                baseline.MaxRounds, changedPin, baseline.InvocationSchemaVersion, baseline.NativeDestination);
            var admission = await services.GetRequiredService<IRuleRunStore>()
                .AdmitAsync(new RuleRunAdmission(incompatible), timeout.Token);
            Require(admission.Disposition == RuleAdmissionDisposition.Unavailable,
                $"Fabricated incompatible pin was not rejected as unavailable: {admission.Disposition}.");
            var lookup = await services.GetRequiredService<IRuleRunReader>().FindAsync(baseline.Identity, timeout.Token);
            Require(lookup.Disposition == RuleRunReadDisposition.NotFound,
                $"Incompatible pin admission created or exposed durable state: {lookup.Disposition}.");
            await host.StopAsync(timeout.Token);
            return new
            {
                command = "incompatible",
                passed = true,
                fabricatedIncompatibility = true,
                versionBUpgradeProof = false,
                changedPackageId = installedPin.Packages[0].PackageId,
                installedVersion = installedPin.Packages[0].NuGetVersion,
                changedVersion = changedPin.Packages[0].NuGetVersion,
                admission = admission.Disposition.ToString(),
                durableLookup = lookup.Disposition.ToString(),
                databasePath = Path.GetFullPath(databasePath)
            };
        }
        finally
        {
            host.Dispose();
        }
    }

    private static IHost BuildHost(string databasePath)
    {
        var hostRoot = ResolvePhysicalPath(AppContext.BaseDirectory);
        var entryAssembly = Assembly.GetEntryAssembly() ?? throw new ProbeAssertionException("Entry assembly is unavailable.");
        var depsPath = Path.ChangeExtension(entryAssembly.Location, ".deps.json");
        var builder = Host.CreateApplicationBuilder();
        new RulesRuntimeFeature
        {
            HostRoot = hostRoot,
            HostDepsPath = depsPath,
            DeploymentReceiptPath = Path.Combine(hostRoot, ReceiptFileName),
            PollIntervalMilliseconds = 10
        }.ConfigureServices(builder.Services);
        new RulesRuntimeEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = SqliteConnectionString(databasePath),
            Pooling = false
        }.ConfigureServices(builder.Services);
        builder.Services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.AutoMigrate);
        return builder.Build();
    }

    private static async Task<RuleRunView> WaitForTerminalAsync(IRuleRunReader reader, RuleRunIdentity identity,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await reader.FindAsync(identity, cancellationToken);
            Require(read.Disposition == RuleRunReadDisposition.Found && read.View is not null,
                $"Admitted run {identity.InvocationId} was not readable: {read.Disposition}.");
            if (read.View.Status != RuleRunStatus.Running)
                return read.View;
            await Task.Delay(20, cancellationToken);
        }
    }

    private static RuleSemanticPin ReplaceFirstPackageVersion(RuleSemanticPin source)
    {
        var changedPackageId = source.Packages[0].PackageId;
        var packages = source.Packages.Select((package, index) => index == 0
            ? new RulePackagePin(package.PackageId, "999999.0.0-incompatible", package.NuGetContentHash, package.SelectedAssets)
            : package).ToArray();
        Require(!StringComparer.Ordinal.Equals(source.Packages[0].NuGetVersion, packages[0].NuGetVersion),
            $"Could not change the pin version for {changedPackageId}.");
        return new RuleSemanticPin(source.PinSchemaVersion, packages, source.LanguageId, source.LanguageVersion,
            source.ReceiptSchemaVersion, source.StateSchemaVersion);
    }

    private static string SqliteConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath), Pooling = false }.ToString();

    private static RuleRunIdentity NewIdentity(string label)
    {
        var id = Guid.NewGuid().ToString("N");
        return new(new RuleScope(null, "packaged-rules-probe"), $"{label}-{id}");
    }

    private static RuleProgram Witness() => new(
        RuleLanguage.FormatVersion,
        RuleLanguage.Id,
        RuleLanguage.Version,
        Facts("final", "joined", "left", "never-derived", "right", "seed", "unreachable"),
        [
            Rule("left", ["seed"], "left"),
            Rule("right", ["seed"], "right"),
            Rule("join", ["left", "right"], "joined"),
            Rule("final", ["joined"], "final"),
            Rule("unreachable", ["unreachable"], "never-derived")
        ]);

    private static RuleDefinition Rule(string id, string[] premises, string conclusion) =>
        new(id, Facts(premises), new RuleFact(conclusion));

    private static RuleFact[] Facts(params string[] facts) => facts.Select(fact => new RuleFact(fact)).ToArray();

    private static string[] FactAtoms(IEnumerable<RuleFact> facts) =>
        facts.Select(fact => fact.Atom).OrderBy(atom => atom, StringComparer.Ordinal).ToArray();

    private static readonly string[] ExpectedFinalFacts = ["final", "joined", "left", "right", "seed"];

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
            throw new ProbeAssertionException(message);
    }

    private static DependencyGraph ReadDependencyGraph(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("runtimeTarget", out var runtimeTarget) ||
            runtimeTarget.ValueKind != JsonValueKind.Object)
            throw new ProbeAssertionException("The active deps.json runtimeTarget is missing or malformed.");
        var targetName = RequiredString(runtimeTarget, "name");
        if (!root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object ||
            !targets.TryGetProperty(targetName, out var activeTarget) || activeTarget.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
            throw new ProbeAssertionException("The active deps.json target is missing or malformed.");

        var librariesByName = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries.EnumerateObject())
        {
            Require(librariesByName.Count < MaxFeedEntries, "The deps.json library count exceeds its bound.");
            Require(librariesByName.TryAdd(library.Name, library.Value), "The deps.json contains duplicate library identities.");
        }

        var resolvedLibraries = new List<ResolvedLibrary>(librariesByName.Count);
        foreach (var (libraryName, metadata) in librariesByName.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!TrySplitLibraryName(libraryName, out var resolvedId, out var resolvedVersion))
                continue;
            Require(metadata.ValueKind == JsonValueKind.Object, "A deps.json library metadata entry is malformed.");
            resolvedLibraries.Add(new ResolvedLibrary(resolvedId, NormalizeVersion(resolvedVersion),
                RequiredString(metadata, "type"), ReadOptionalNuGetHash(metadata)));
        }

        var targetSeparator = targetName.LastIndexOf('/');
        var targetRid = targetSeparator < 0 ? null : targetName[(targetSeparator + 1)..];
        var packages = new Dictionary<string, DependencyPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var targetLibrary in activeTarget.EnumerateObject())
        {
            if (!TrySplitLibraryName(targetLibrary.Name, out var packageId, out var version) ||
                !RequiredPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                continue;
            Require(!packages.ContainsKey(packageId), $"The active deps target contains ambiguous versions of {packageId}.");
            Require(librariesByName.TryGetValue(targetLibrary.Name, out var libraryMetadata) &&
                    libraryMetadata.ValueKind == JsonValueKind.Object,
                $"The active deps library metadata is missing for {packageId}.");
            var type = RequiredString(libraryMetadata, "type");
            Require(StringComparer.OrdinalIgnoreCase.Equals(type, "package"),
                $"Required library {packageId} is a project or non-package dependency.");
            var normalizedVersion = NormalizeVersion(version);
            var nugetContentHash = ReadRequiredNuGetHash(libraryMetadata, packageId);
            var runtimeAssetPaths = ReadRuntimeAssets(targetLibrary.Value, targetRid);
            Require(runtimeAssetPaths.Count > 0, $"The active deps target selects no runtime asset for {packageId}.");
            packages.Add(packageId, new DependencyPackage(packageId, normalizedVersion, nugetContentHash, runtimeAssetPaths));
        }

        foreach (var packageId in RequiredPackageIds)
            Require(packages.ContainsKey(packageId), $"Required package {packageId} is absent from the active deps target.");
        return new DependencyGraph(targetName, packages, resolvedLibraries);
    }

    private static readonly string[] RequiredPackageIds =
        ["Elsa.Rules.Core", "Elsa.Rules.Runtime", "Elsa.Rules.Persistence.Core"];

    private static HashSet<string> ReadRuntimeAssets(JsonElement targetLibrary, string? targetRid)
    {
        Require(targetLibrary.ValueKind == JsonValueKind.Object, "A package target entry is malformed.");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var assetCount = 0;
        if (targetLibrary.TryGetProperty("runtime", out var runtime))
        {
            Require(runtime.ValueKind == JsonValueKind.Object, "A package runtime asset map is malformed.");
            foreach (var asset in runtime.EnumerateObject())
            {
                Require(++assetCount <= MaxSelectedAssets, "The package runtime asset count exceeds its bound.");
                Require(IsCanonicalPackagePath(asset.Name), "The deps.json contains an unsafe runtime asset path.");
                if (StringComparer.Ordinal.Equals(Path.GetFileName(asset.Name), "_._"))
                    continue;
                if (asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    paths.Add(asset.Name);
            }
        }

        if (targetLibrary.TryGetProperty("runtimeTargets", out var runtimeTargets))
        {
            Require(runtimeTargets.ValueKind == JsonValueKind.Object, "A package runtimeTargets map is malformed.");
            foreach (var asset in runtimeTargets.EnumerateObject())
            {
                Require(++assetCount <= MaxSelectedAssets, "The package runtime target count exceeds its bound.");
                Require(asset.Value.ValueKind == JsonValueKind.Object, "A deps runtimeTargets record is malformed.");
                var assetType = OptionalString(asset.Value, "assetType");
                var rid = OptionalString(asset.Value, "rid");
                if (!StringComparer.OrdinalIgnoreCase.Equals(assetType, "runtime") ||
                    (rid is not null && !StringComparer.OrdinalIgnoreCase.Equals(rid, targetRid)))
                    continue;
                Require(IsCanonicalPackagePath(asset.Name), "The deps.json contains an unsafe RID runtime asset path.");
                if (StringComparer.Ordinal.Equals(Path.GetFileName(asset.Name), "_._"))
                    continue;
                if (asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    paths.Add(asset.Name);
            }
        }

        return paths;
    }

    private static string ReadRequiredNuGetHash(JsonElement metadata, string packageId)
    {
        var hash = ReadOptionalNuGetHash(metadata);
        Require(hash is not null, $"The active deps.json has no NuGet sha512 for {packageId}.");
        return hash!;
    }

    private static string? ReadOptionalNuGetHash(JsonElement metadata)
    {
        if (!metadata.TryGetProperty("sha512", out var hashElement))
            return null;
        Require(hashElement.ValueKind == JsonValueKind.String, "A deps.json sha512 value is malformed.");
        var value = hashElement.GetString()!;
        return value.Length == 0 ? null : NormalizeNuGetHash(value);
    }

    private static string NormalizeNuGetHash(string value)
    {
        var encoded = value.Trim();
        if (encoded.StartsWith("sha512-", StringComparison.Ordinal))
            encoded = encoded["sha512-".Length..];
        Require(encoded.Length == 88, "A NuGet sha512 content hash has an invalid length.");
        Span<byte> bytes = stackalloc byte[64];
        Require(Convert.TryFromBase64String(encoded, bytes, out var written) && written == bytes.Length &&
                StringComparer.Ordinal.Equals(Convert.ToBase64String(bytes), encoded),
            "A NuGet sha512 content hash is not canonical Base64.");
        return encoded;
    }

    private static FeedPackage FindFeedPackage(string feedRoot, string packageId, string version)
    {
        var candidates = Directory.EnumerateFiles(feedRoot, "*.nupkg", SearchOption.TopDirectoryOnly)
            .Take(MaxFeedEntries + 1).ToArray();
        Require(candidates.Length <= MaxFeedEntries, "The package feed entry count exceeds its bound.");
        var matches = new List<string>();
        foreach (var candidate in candidates)
        {
            var info = new FileInfo(candidate);
            Require(info.Length <= MaxArchiveBytes, "A package feed archive exceeds its byte bound.");
            using var stream = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read,
                81_920, FileOptions.SequentialScan);
            using var reader = new PackageArchiveReader(stream);
            var identity = reader.GetIdentity();
            if (StringComparer.OrdinalIgnoreCase.Equals(identity.Id, packageId) &&
                StringComparer.Ordinal.Equals(identity.Version.ToNormalizedString(), version))
                matches.Add(Path.GetFullPath(candidate));
            Require(matches.Count <= 1, $"The package feed contains duplicate {packageId}/{version} archives.");
        }

        Require(matches.Count == 1, $"The package feed does not contain exactly one {packageId}/{version} archive.");
        return new FeedPackage(matches[0]);
    }

    private static string GetCachedPackagePath(string packageId, string version)
    {
        var packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Require(!string.IsNullOrWhiteSpace(packagesRoot) && Path.IsPathFullyQualified(packagesRoot),
            "NUGET_PACKAGES must point at the isolated restore cache.");
        var id = packageId.ToLowerInvariant();
        var normalizedVersion = NormalizeVersion(version).ToLowerInvariant();
        var path = Path.Combine(Path.GetFullPath(packagesRoot), id, normalizedVersion, $"{id}.{normalizedVersion}.nupkg");
        Require(File.Exists(path), $"Restored package archive is missing from the NuGet cache for {packageId}/{version}.");
        return path;
    }

    private static string GetNuGetContentHash(string archivePath, string expectedPackageId, string expectedVersion)
    {
        var info = new FileInfo(archivePath);
        Require(info.Length <= MaxArchiveBytes, "A package archive exceeds its byte bound.");
        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81_920, FileOptions.SequentialScan);
        using var reader = new PackageArchiveReader(stream);
        var identity = reader.GetIdentity();
        Require(StringComparer.OrdinalIgnoreCase.Equals(identity.Id, expectedPackageId) &&
                StringComparer.Ordinal.Equals(identity.Version.ToNormalizedString(), expectedVersion),
            "A package cache or feed archive has the wrong NuGet identity.");
        return NormalizeNuGetHash(reader.GetContentHash(CancellationToken.None));
    }

    private static string ReadRepositoryCommit(string archivePath)
    {
        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var nuspecs = archive.Entries.Where(entry => !entry.FullName.Contains('/') &&
                entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        Require(nuspecs.Length == 1 && nuspecs[0].Length <= MaxNuspecBytes,
            "A package archive must have one bounded root nuspec.");
        using var nuspecStream = nuspecs[0].Open();
        using var xmlReader = XmlReader.Create(nuspecStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxNuspecBytes,
            IgnoreComments = true,
            IgnoreWhitespace = true
        });
        var document = XDocument.Load(xmlReader, LoadOptions.None);
        var metadata = document.Root?.Elements().SingleOrDefault(element => element.Name.LocalName == "metadata");
        var repository = metadata?.Elements().SingleOrDefault(element => element.Name.LocalName == "repository");
        var commit = repository?.Attribute("commit")?.Value;
        Require(commit is not null && IsCommit(commit), "A Rules package nuspec has no full immutable repository commit.");
        return commit;
    }

    private static async Task<string> HashArchiveEntryAsync(string archivePath, string entryPath)
    {
        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.Where(entry => StringComparer.Ordinal.Equals(entry.FullName, entryPath))
            .Take(2).ToArray();
        Require(entries.Length == 1 && entries[0].Length <= MaxAssetBytes,
            "The selected package asset is missing, duplicated, or exceeds its byte bound.");
        await using var asset = entries[0].Open();
        return await HashStreamAsync(asset, MaxAssetBytes);
    }

    private static async Task<string> HashFileAsync(string path, long maxBytes, long existingTotalBytes)
    {
        var info = new FileInfo(path);
        Require(info.Length <= maxBytes && existingTotalBytes <= MaxTotalAssetBytes - info.Length,
            "Selected deployment assets exceed their byte bounds.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await HashStreamAsync(stream, maxBytes);
    }

    private static async Task<string> HashStreamAsync(Stream stream, long maxBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81_920];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
                break;
            total = checked(total + read);
            Require(total <= maxBytes, "A selected asset exceeds its byte bound.");
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task<(string Base64, string Hex)> HashArchiveAsync(string path)
    {
        var info = new FileInfo(path);
        Require(info.Length <= MaxArchiveBytes, "A package archive exceeds its byte bound.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        var buffer = new byte[81_920];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
                break;
            total = checked(total + read);
            Require(total <= MaxArchiveBytes, "A package archive exceeds its byte bound.");
            hash.AppendData(buffer, 0, read);
        }

        var digest = hash.GetHashAndReset();
        return (Convert.ToBase64String(digest), Convert.ToHexString(digest).ToLowerInvariant());
    }

    private static RuntimeProvenance ReadRuntimeProfile(Assembly entryAssembly)
    {
        var runtimeConfigPath = Path.ChangeExtension(entryAssembly.Location, ".runtimeconfig.json");
        var bytes = ReadBoundedFile(runtimeConfigPath, MaxRuntimeConfigBytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
        Require(HasBoundedJsonShape(document.RootElement), "The runtimeconfig exceeds bounded JSON limits.");
        Require(document.RootElement.TryGetProperty("runtimeOptions", out var runtimeOptions) &&
                runtimeOptions.ValueKind == JsonValueKind.Object,
            "The consumer runtimeconfig is malformed.");
        var frameworkDependent = runtimeOptions.TryGetProperty("framework", out _) || runtimeOptions.TryGetProperty("frameworks", out _);
        Require(frameworkDependent && !runtimeOptions.TryGetProperty("includedFrameworks", out _),
            "The consumer must use a framework-dependent file-based runtime profile.");
        return new RuntimeProvenance(
            entryAssembly.Location,
            Environment.ProcessPath,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.RuntimeIdentifier,
            AppContext.TargetFrameworkName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.MachineName,
            Environment.ProcessId,
            "framework-dependent-file-based");
    }

    private static string GetSdkVersion()
    {
        try
        {
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--version");
            using var process = Process.Start(start);
            if (process is null || !process.WaitForExit(5_000))
            {
                process?.Kill(entireProcessTree: true);
                throw new ProbeAssertionException("Could not capture the installed .NET SDK version.");
            }
            var version = process.StandardOutput.ReadToEnd().Trim();
            Require(process.ExitCode == 0 && version.Length is > 0 and <= 128,
                "Could not capture the installed .NET SDK version.");
            return version;
        }
        catch (ProbeAssertionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new ProbeAssertionException("Could not capture the installed .NET SDK version.");
        }
    }

    private static byte[] ReadBoundedFile(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81_920, FileOptions.SequentialScan);
        Require(stream.Length <= maxBytes, "A JSON or metadata file exceeds its byte bound.");
        using var buffer = new MemoryStream((int)stream.Length);
        var chunk = new byte[81_920];
        while (true)
        {
            var read = stream.Read(chunk, 0, chunk.Length);
            if (read == 0)
                break;
            Require(buffer.Length + read <= maxBytes, "A JSON or metadata file exceeds its byte bound.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string ReadBoundedText(string path, int maxBytes)
    {
        var bytes = ReadBoundedFile(path, maxBytes);
        return StrictUtf8.GetString(bytes).Trim();
    }

    private static void WriteJson(string path, object value) =>
        WriteTextAtomically(path, JsonSerializer.Serialize(value, JsonOptions) + "\n");

    private static void WriteTextAtomically(string path, string value)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new ProbeAssertionException("Output path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, value, new UTF8Encoding(false, true));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string NormalizeVersion(string value)
    {
        Require(NuGetVersion.TryParse(value, out var version), "A deps/package version is invalid.");
        return version.ToNormalizedString();
    }

    private static bool TrySplitLibraryName(string value, out string packageId, out string version)
    {
        var separator = value.LastIndexOf('/');
        if (separator <= 0 || separator == value.Length - 1)
        {
            packageId = string.Empty;
            version = string.Empty;
            return false;
        }
        packageId = value[..separator];
        version = value[(separator + 1)..];
        return true;
    }

    private static bool IsCanonicalPackagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > RuleLimits.MaxIdentifierUtf8Bytes ||
            path.Contains('\\') || path.StartsWith("/", StringComparison.Ordinal) || path.Contains(':') ||
            !StringComparer.Ordinal.Equals(path, path.Trim()))
            return false;
        try
        {
            if (StrictUtf8.GetByteCount(path) > RuleLimits.MaxIdentifierUtf8Bytes)
                return false;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
        return path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static string ResolvePhysicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var volumeRoot = Path.GetPathRoot(fullPath) ?? throw new ProbeAssertionException("A host path has no volume root.");
        var segments = fullPath[volumeRoot.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var resolved = volumeRoot;
        for (var index = 0; index < segments.Length; index++)
        {
            resolved = Path.Combine(resolved, segments[index]);
            FileSystemInfo entry = index == segments.Length - 1 && File.Exists(resolved)
                ? new FileInfo(resolved)
                : new DirectoryInfo(resolved);
            resolved = entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? resolved;
        }
        return Path.GetFullPath(resolved);
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        var prefix = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, comparison) || StringComparerFrom(comparison).Equals(fullPath, fullRoot);
    }

    private static StringComparer StringComparerFrom(StringComparison comparison) =>
        comparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsCommit(string? value) => value is { Length: 40 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string RequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String ||
            value.GetString() is not { Length: > 0 } text)
            throw new ProbeAssertionException($"A required deps.json string property '{property}' is missing.");
        return text;
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool HasBoundedJsonShape(JsonElement root)
    {
        var nodes = 0;
        return Visit(root, ref nodes);

        static bool Visit(JsonElement element, ref int nodes)
        {
            if (++nodes > MaxJsonNodes)
                return false;
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                    if (!names.Add(property.Name) || !Visit(property.Value, ref nodes))
                        return false;
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    if (!Visit(item, ref nodes))
                        return false;
            }
            return true;
        }
    }

    private sealed record FeedPackage(string Path);
    private sealed record DependencyPackage(string PackageId, string Version, string NuGetContentHash, HashSet<string> RuntimeAssetPaths);
    private sealed record ResolvedLibrary(string Id, string Version, string Type, string? NuGetContentHash);
    private sealed record DependencyGraph(string RuntimeTarget, Dictionary<string, DependencyPackage> Packages,
        IReadOnlyList<ResolvedLibrary> ResolvedLibraries)
    {
        public DependencyPackage FindRequiredPackage(string packageId) => Packages.TryGetValue(packageId, out var package)
            ? package
            : throw new ProbeAssertionException($"Required package {packageId} is absent from the active deps target.");
    }
    private sealed record ReceiptAsset(string Path, string Assembly, string Sha256);
    private sealed record PackageProvenance(string FeedArchive, string CacheArchive, string RawArchiveSha512Base64,
        string RawArchiveSha512Hex, (string Base64, string Hex) CacheArchiveSha512, string[] SelectedRuntimeAssets,
        string SourceCommit)
    {
        public string CacheArchiveSha512Base64 => CacheArchiveSha512.Base64;
        public string CacheArchiveSha512Hex => CacheArchiveSha512.Hex;
    }
    private sealed record ReceiptPackage(string Id, string Version, string ContentHash, ReceiptAsset[] Assets,
        PackageProvenance Provenance);
    private sealed record RuntimeProvenance(string EntryAssembly, string? ProcessPath, string FrameworkDescription,
        string RuntimeIdentifier, string? TargetFramework, string OSDescription, string ProcessArchitecture,
        string MachineName, int ProcessId, string DeploymentProfile);

    private sealed class ProbeAssertionException(string message) : Exception(message);
}
