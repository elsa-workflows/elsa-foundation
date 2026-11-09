using System.Security.Cryptography;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Rules.Tests;

public sealed class RuleSourceBuildIdentitySourceTests
{
    [Fact]
    public async Task The_source_build_pin_names_the_three_loaded_rules_assemblies_with_source_build_provenance()
    {
        var source = new RuleSourceBuildIdentitySource(NullLogger<RuleSourceBuildIdentitySource>.Instance);
        var pin = await source.ReadAsync();

        Assert.NotNull(pin);
        Assert.Equal(1, pin.PinSchemaVersion);
        Assert.Equal(RuleLanguage.Id, pin.LanguageId);
        var ids = pin.Packages.Select(package => package.PackageId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(["source-build:Elsa.Rules.Core", "source-build:Elsa.Rules.Persistence.Core", "source-build:Elsa.Rules.Runtime"], ids);
        foreach (var package in pin.Packages)
        {
            var asset = Assert.Single(package.SelectedAssets);
            Assert.StartsWith("source-build/", asset.PackageRelativeAssetPath, StringComparison.Ordinal);
            // Both hashes are of the assembly file this process loaded, so a changed build changes the pin; the content-hash
            // slot carries the file's SHA-512 in the canonical Base64 the pin shape requires, not a NuGet archive hash.
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => !assembly.IsDynamic && assembly.GetName().FullName == asset.AssemblyFullName);
            var bytes = await File.ReadAllBytesAsync(loaded.Location);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), asset.AssetSha256);
            Assert.Equal(Convert.ToBase64String(SHA512.HashData(bytes)), package.NuGetContentHash);
        }
    }

    [Fact]
    public async Task The_source_build_pin_can_never_equal_a_packaged_pin_for_the_same_assemblies()
    {
        var sourceBuild = (await new RuleSourceBuildIdentitySource(NullLogger<RuleSourceBuildIdentitySource>.Instance).ReadAsync())!;
        // A packaged receipt pins the same assemblies under their NuGet ids; the provenance marker keeps the hashes apart.
        var packaged = new RuleSemanticPin(1, sourceBuild.Packages.Select(package => new RulePackagePin(
            package.PackageId["source-build:".Length..], package.NuGetVersion, package.NuGetContentHash,
            package.SelectedAssets)).ToArray(), sourceBuild.LanguageId, sourceBuild.LanguageVersion, sourceBuild.ReceiptSchemaVersion, sourceBuild.StateSchemaVersion);

        Assert.NotEqual(RuleRecordCodec.PinHash(packaged), RuleRecordCodec.PinHash(sourceBuild));
        // The load-bearing guarantee: the markers only a source-build pin carries, and a receipt-shaped pin never does
        // (the receipt reader resolves package ids against deps.json, where a "source-build:" id cannot exist).
        Assert.All(sourceBuild.Packages, package => Assert.StartsWith(RuleSourceBuildIdentitySource.ProvenancePrefix, package.PackageId, StringComparison.Ordinal));
        var receiptShaped = RuleStoreTestFixture.NewInvocation().ExpectedImplementation;
        Assert.All(receiptShaped.Packages, package =>
        {
            Assert.DoesNotContain(':', package.PackageId);
            Assert.All(package.SelectedAssets, asset => Assert.False(asset.PackageRelativeAssetPath.StartsWith("source-build/", StringComparison.Ordinal)));
        });
    }

    [Fact]
    public async Task The_pin_is_snapshotted_once_and_the_warning_is_logged_once()
    {
        var logger = new RecordingLogger();
        var source = new RuleSourceBuildIdentitySource(logger);
        var first = (await source.ReadAsync())!;
        var second = (await source.ReadAsync())!;

        Assert.Same(first, second);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("source-build identity", warning.Message, StringComparison.Ordinal);
        Assert.Contains(RuleRecordCodec.PinHash(first), warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_invocation_prepared_under_the_source_build_pin_is_admissible_by_the_same_build()
    {
        var source = new RuleSourceBuildIdentitySource(NullLogger<RuleSourceBuildIdentitySource>.Instance);
        var prepared = await new RuleInvocationFactory(source).PrepareAsync(RuleStoreTestFixture.NewInvocation().Identity,
            SeedProgram(), [new RuleFact("seed")], maxRounds: 4);
        Assert.True(prepared.Disposition == RuleInvocationPreparationDisposition.Prepared, prepared.Disposition + ": " + prepared.Diagnostic?.Code);

        var validation = await new RuleAdmissionValidator(source).ValidateAsync(prepared.Invocation!);
        Assert.Equal(RuleAdmissionValidationDisposition.Ready, validation.Disposition);
    }

    [Fact]
    public async Task A_packaged_pin_is_unavailable_under_a_source_build_host_and_vice_versa()
    {
        var sourceBuild = new RuleSourceBuildIdentitySource(NullLogger<RuleSourceBuildIdentitySource>.Instance);
        var packagedPin = RuleStoreTestFixture.NewInvocation().ExpectedImplementation;
        var packagedHost = new RuleAdmissionValidator(new FixedIdentitySource(packagedPin));

        var underPackaged = await new RuleInvocationFactory(new FixedIdentitySource(packagedPin)).PrepareAsync(
            RuleStoreTestFixture.NewInvocation().Identity, SeedProgram(), [new RuleFact("seed")], maxRounds: 4);
        var underSourceBuild = await new RuleInvocationFactory(sourceBuild).PrepareAsync(
            RuleStoreTestFixture.NewInvocation().Identity, SeedProgram(), [new RuleFact("seed")], maxRounds: 4);

        var packagedOnSourceBuild = await new RuleAdmissionValidator(sourceBuild).ValidateAsync(underPackaged.Invocation!);
        var sourceBuildOnPackaged = await packagedHost.ValidateAsync(underSourceBuild.Invocation!);
        Assert.Equal(RuleAdmissionValidationDisposition.Unavailable, packagedOnSourceBuild.Disposition);
        Assert.Equal("rules.implementation-unavailable", packagedOnSourceBuild.Diagnostic!.Code);
        Assert.Equal(RuleAdmissionValidationDisposition.Unavailable, sourceBuildOnPackaged.Disposition);
    }

    [Fact]
    public void The_feature_registers_the_deployment_reader_by_default_and_the_source_build_reader_only_when_allowed()
    {
        var byDefault = new ServiceCollection();
        new RulesRuntimeFeature().ConfigureServices(byDefault);
        using var defaultProvider = byDefault.BuildServiceProvider();
        Assert.IsType<RuleDeploymentIdentitySource>(defaultProvider.GetRequiredService<IRuleImplementationIdentitySource>());

        var allowed = new ServiceCollection();
        new RulesRuntimeFeature { AllowSourceBuildIdentity = true }.ConfigureServices(allowed);
        using var allowedProvider = allowed.BuildServiceProvider();
        Assert.IsType<RuleSourceBuildIdentitySource>(allowedProvider.GetRequiredService<IRuleImplementationIdentitySource>());

        var replaced = new ServiceCollection();
        var host = new FixedIdentitySource(null);
        replaced.AddSingleton<IRuleImplementationIdentitySource>(host);
        new RulesRuntimeFeature { AllowSourceBuildIdentity = true }.ConfigureServices(replaced);
        using var replacedProvider = replaced.BuildServiceProvider();
        Assert.Same(host, replacedProvider.GetRequiredService<IRuleImplementationIdentitySource>());
    }

    [Theory]
    [InlineData("/deploy/elsa.rules.deployment-receipt.json", "")]
    [InlineData("", "/deploy/host.deps.json")]
    public void The_feature_refuses_a_source_build_identity_next_to_deployment_evidence(string receiptPath, string depsPath)
    {
        var feature = new RulesRuntimeFeature { AllowSourceBuildIdentity = true, DeploymentReceiptPath = receiptPath, HostDepsPath = depsPath };
        var error = Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));
        Assert.Contains("AllowSourceBuildIdentity", error.Message, StringComparison.Ordinal);
    }

    private static RuleProgram SeedProgram() => new(1, RuleLanguage.Id, RuleLanguage.Version,
        [new RuleFact("derived"), new RuleFact("seed")], [new RuleDefinition("derive", [new RuleFact("seed")], new RuleFact("derived"))]);

    private sealed class FixedIdentitySource(RuleSemanticPin? pin) : IRuleImplementationIdentitySource
    {
        public ValueTask<RuleSemanticPin?> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(pin);
    }

    private sealed class RecordingLogger : ILogger<RuleSourceBuildIdentitySource>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
