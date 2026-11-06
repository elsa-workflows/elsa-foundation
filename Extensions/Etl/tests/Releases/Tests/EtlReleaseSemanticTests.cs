using Elsa.Etl.Core;
using Elsa.Etl.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Core;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Xunit;

namespace Elsa.Etl.Releases.Tests;

public sealed class EtlReleaseSemanticTests
{
    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 3)]
    public async Task Real_owner_codec_and_shared_orchestrator_verify_complete_ETL_catalog_root(int format, int resourceCount)
    {
        using var provider = EtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var codec = scope.ServiceProvider.GetRequiredService<IEtlReleaseArtifactCodec>();
        var artifact = format == 1 ? EtlReleaseFixture.Artifact(EtlReleaseFixture.V1()) : EtlReleaseFixture.Artifact(EtlReleaseFixture.V2());
        var encoded = codec.Encode(EtlReleaseFixture.Release(artifact));
        var manifest = EtlReleaseFixture.Manifest(encoded, artifact);
        var bundle = EtlReleaseFixture.Bundle(scope.ServiceProvider.GetRequiredService<IReleaseBundleCodec>(), manifest, encoded);
        var result = await scope.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>().VerifyAsync(bundle);
        Assert.Equal(ReleaseSemanticStatus.Verified, result.Status);
        Assert.Equal(resourceCount, result.Resources.Count);
        Assert.Empty(result.Edges);
        Assert.Empty(result.Diagnostics);
        Assert.Single(result.Resources, x => x.Kind == "etl.binding.jsonl.error-output");
        Assert.Contains(result.Prerequisites, x => x.Kind == "runtime-capacity" && x.Identity == "etl.capacity-input-bytes");
    }

    [Fact]
    public async Task Several_ETL_versions_keep_distinct_resource_and_prerequisite_keys()
    {
        using var provider = EtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var codec = scope.ServiceProvider.GetRequiredService<IEtlReleaseArtifactCodec>();
        var first = EtlReleaseFixture.Artifact(EtlReleaseFixture.V1(), 37);
        var second = EtlReleaseFixture.Artifact(EtlReleaseFixture.V1(), 38);
        var firstBytes = codec.Encode(EtlReleaseFixture.Release(first));
        var secondBytes = codec.Encode(EtlReleaseFixture.Release(second));
        var manifest = EtlReleaseFixture.Manifest(firstBytes, first);
        var another = EtlReleaseFixture.Manifest(secondBytes, second);
        foreach (var field in new[] { "roots", "members", "prerequisites", "resourceRequirements" })
            foreach (var item in another[field]!.AsArray()) manifest[field]!.AsArray().Add(item!.DeepClone());
        var blobs = new Dictionary<string, ReadOnlyMemory<byte>>
        {
            [firstBytes.Descriptor.PayloadDigest] = firstBytes.GetPayloadBytes(),
            [secondBytes.Descriptor.PayloadDigest] = secondBytes.GetPayloadBytes()
        };
        var bundle = scope.ServiceProvider.GetRequiredService<IReleaseBundleCodec>().Validate(Encoding.UTF8.GetBytes(manifest.ToJsonString()), blobs);
        var result = await scope.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>().VerifyAsync(bundle);
        Assert.Equal(ReleaseSemanticStatus.Verified, result.Status);
        Assert.Equal(8, result.Resources.Count);
        Assert.Equal(22, result.Prerequisites.Count);
        Assert.Equal(result.Prerequisites.Count, result.Prerequisites.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("activation")]
    [InlineData("missing-resource")]
    [InlineData("invented-resource")]
    [InlineData("changed-alias")]
    [InlineData("changed-origin")]
    [InlineData("missing-prerequisite")]
    [InlineData("changed-capacity")]
    [InlineData("semver-version")]
    [InlineData("integer-version")]
    [InlineData("codec-format")]
    public async Task Declared_roles_resources_and_exact_integer_owner_identity_cannot_replace_actual_facts(string change)
    {
        using var provider = EtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var artifact = EtlReleaseFixture.Artifact(EtlReleaseFixture.V1());
        var encoded = scope.ServiceProvider.GetRequiredService<IEtlReleaseArtifactCodec>().Encode(EtlReleaseFixture.Release(artifact));
        var manifest = EtlReleaseFixture.Manifest(encoded, artifact);
        switch (change)
        {
            case "activation": manifest["roots"]![0]!["action"] = "activate-serving"; break;
            case "missing-resource": manifest["resourceRequirements"]!.AsArray().RemoveAt(0); break;
            case "invented-resource":
                var extra = manifest["resourceRequirements"]![0]!.DeepClone(); extra["key"]!["reference"] = "invented";
                manifest["resourceRequirements"]!.AsArray().Add(extra); break;
            case "changed-alias": manifest["resourceRequirements"]![0]!["key"]!["reference"] = "another"; break;
            case "changed-origin": manifest["resourceRequirements"]![0]!["key"]!["origin"]![0] = "other-scope"; break;
            case "missing-prerequisite": manifest["prerequisites"]!.AsArray().RemoveAt(0); break;
            case "changed-capacity": manifest["prerequisites"]![3]!["constraint"] = "[9999999999999999,)"; break;
            case "semver-version": manifest["members"]![0]!["ownerVersion"]!["scheme"] = "semver"; manifest["members"]![0]!["ownerVersion"]!["value"] = "37.0.0"; break;
            case "integer-version": manifest["members"]![0]!["ownerVersion"]!["value"] = 38; break;
            case "codec-format": manifest["members"]![0]!["codecVersion"] = 2; break;
        }
        var result = await scope.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>().VerifyAsync(
            EtlReleaseFixture.Bundle(scope.ServiceProvider.GetRequiredService<IReleaseBundleCodec>(), manifest, encoded));
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task Unknown_ETL_codec_version_is_unsupported_even_when_its_bytes_are_transport_valid()
    {
        using var provider = EtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var artifact = EtlReleaseFixture.Artifact(EtlReleaseFixture.V1());
        var encoded = scope.ServiceProvider.GetRequiredService<IEtlReleaseArtifactCodec>().Encode(EtlReleaseFixture.Release(artifact));
        var manifest = EtlReleaseFixture.Manifest(encoded, artifact); manifest["members"]![0]!["codecVersion"] = 99;
        var result = await scope.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>().VerifyAsync(
            EtlReleaseFixture.Bundle(scope.ServiceProvider.GetRequiredService<IReleaseBundleCodec>(), manifest, encoded));
        Assert.Equal(ReleaseSemanticStatus.Unsupported, result.Status);
    }

    [Theory]
    [InlineData("csv", "csv")]
    [InlineData("jsonl", "jsonl")]
    [InlineData("sql", "sql")]
    [InlineData("rabbitmq", "rabbitmq")]
    public async Task Every_supported_connector_family_has_explicit_scoped_resource_and_capability_facts(string sourceKind, string destinationKind)
    {
        using var provider = EtlReleaseFixture.Services(); using var scope = provider.CreateScope();
        var pipeline = EtlReleaseFixture.V2();
        var sourceColumns = new[] { new EtlColumn("id", EtlColumnType.Int64, true), new EtlColumn("name", EtlColumnType.String, false) };
        pipeline = pipeline with
        {
            Source = sourceKind switch
            {
                "csv" => new EtlCsvSource("source", sourceColumns),
                "jsonl" => new EtlJsonlSource("source", [new("id", "/id", EtlColumnType.Int64, true), new("name", "/name", EtlColumnType.String, false)]),
                "sql" => new EtlSqlSource("source", "orders", sourceColumns, ["id"]),
                _ => new EtlRabbitMqSource("source", "orders", [new("id", "/id", EtlColumnType.Int64, true), new("name", "/name", EtlColumnType.String, false)], 10)
            },
            Routes = new(destinationKind switch
            {
                "csv" => new EtlCsvDestination("output"), "jsonl" => new EtlJsonlDestination("output"),
                "sql" => new EtlSqlDestination("output", "orders", ["id"]), _ => new EtlRabbitMqDestination("output", "orders", "orders.accepted")
            }, new EtlJsonlDestination("errors")),
            Mapping = [new("id", EtlFieldOrigin.Source, "id"), new("name", EtlFieldOrigin.Source, "name")]
        };
        var artifact = EtlReleaseFixture.Artifact(pipeline);
        var encoded = scope.ServiceProvider.GetRequiredService<IEtlReleaseArtifactCodec>().Encode(EtlReleaseFixture.Release(artifact));
        var member = new ReleaseContentMember(encoded.Descriptor, encoded.OwnerVersion);
        var verifier = Assert.Single(scope.ServiceProvider.GetServices<IReleaseArtifactSemanticVerifier>());
        var facts = await verifier.VerifyAsync(member, encoded.GetPayloadBytes(), new CapturedContent(member, encoded.GetPayloadBytes()));
        Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
        Assert.Contains(facts.Resources, x => x.Origin.SequenceEqual(new[] { "source" }) && x.Reference == "source" && x.Kind == "etl.binding." + sourceKind + ".source");
        Assert.Contains(facts.Resources, x => x.Origin.SequenceEqual(new[] { "routes", "main" }) && x.Reference == "output" && x.Kind == "etl.binding." + destinationKind + ".main-output");
        Assert.Contains(facts.Prerequisites, x => x.Identity == "etl.connector." + sourceKind);
        Assert.Equal(3, facts.Resources.Count);
        Assert.Equal(ReleaseRootAction.MakeAvailable, Assert.Single(facts.RootCapabilities).Action);
    }

    [Fact]
    public void Optional_verification_adds_no_worker_and_detects_duplicate_codec_replacements()
    {
        var services = new ServiceCollection(); var feature = new EtlReleaseVerificationFeature();
        feature.ConfigureServices(services); feature.PostConfigureServices(services);
        Assert.DoesNotContain(services, x => x.ServiceType.FullName == "Microsoft.Extensions.Hosting.IHostedService");
        services.AddScoped<IEtlReleaseArtifactCodec, EtlReleaseArtifactCodec>();
        Assert.Throws<InvalidOperationException>(() => feature.PostConfigureServices(services));
    }

    private sealed class CapturedContent(ReleaseContentMember member, byte[] payload) : IReleaseContentReader
    {
        public IReadOnlyList<ReleaseContentMember> Members { get; } = Array.AsReadOnly(new[] { member });
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => key == member.Key ? member : throw new KeyNotFoundException();
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) => GetMember(key) == member ? (byte[])payload.Clone() : throw new KeyNotFoundException();
    }
}
