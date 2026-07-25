using System.Text.Json;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Models;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

public sealed class OpenIddictGroundworkCodecTests
{
    [Theory]
    [InlineData("Application")]
    [InlineData("Authorization")]
    [InlineData("Scope")]
    [InlineData("Token")]
    public void Every_record_kind_has_a_versioned_codec_contract(string recordKind)
    {
        var policy = recordKind switch
        {
            "Application" => OpenIddictGroundworkJson.CreateApplicationPolicy(),
            "Authorization" => OpenIddictGroundworkJson.CreateAuthorizationPolicy(),
            "Scope" => OpenIddictGroundworkJson.CreateScopePolicy(),
            "Token" => OpenIddictGroundworkJson.CreateTokenPolicy(),
            _ => throw new ArgumentOutOfRangeException(nameof(recordKind))
        };

        Assert.Equal(1, policy.MinimumReadableVersion);
        Assert.Equal(1, policy.CurrentVersion);
    }

    [Fact]
    public void Codec_contract_exposes_current_minimum_readable_and_upcast_policies()
    {
        Assert.Equal(4, OpenIddictGroundworkJson.Policies.Count);
        Assert.Empty(OpenIddictGroundworkJson.Upcasters);
    }

    [Fact]
    public void Canonical_codec_uses_manifest_compatible_camel_case()
    {
        var content = OpenIddictGroundworkJson.CreateCodec().Serialize(
            OpenIddictGroundworkJson.TokenDocumentKind,
            new { Subject = "subject-a", ReferenceId = "reference-a" });

        using var document = JsonDocument.Parse(content.ContentJson);
        Assert.Equal("subject-a", document.RootElement.GetProperty("subject").GetString());
        Assert.Equal("reference-a", document.RootElement.GetProperty("referenceId").GetString());
        Assert.False(document.RootElement.TryGetProperty("Subject", out _));
    }

    [Fact]
    public void Four_canonical_record_kinds_round_trip_every_descriptor_group()
    {
        using var property = JsonDocument.Parse("{\"enabled\":true}");
        var application = new OpenIddictGroundworkApplication
        {
            Id = "application-a",
            ClientId = "client-a",
            RedirectUris = ["https://one.example/callback"],
            PostLogoutRedirectUris = ["https://one.example/logout"],
            Permissions = ["endpoint:token"],
            Requirements = ["pkce"],
            DisplayNames = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["en-US"] = "Application A" },
            Properties = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal) { ["feature"] = property.RootElement.Clone() },
            Settings = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["mode"] = "strict" }
        };
        var authorization = new OpenIddictGroundworkAuthorization
        {
            Id = "authorization-a",
            ApplicationId = application.Id,
            CreationDate = DateTimeOffset.UnixEpoch,
            Scopes = ["api", "profile"],
            Status = "valid",
            Subject = "subject-a",
            Type = "permanent"
        };
        var scope = new OpenIddictGroundworkScope
        {
            Id = "scope-a",
            Name = "api",
            Resources = ["resource-a"],
            Descriptions = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["en-US"] = "API scope" }
        };
        var token = new OpenIddictGroundworkToken
        {
            Id = "token-a",
            ApplicationId = application.Id,
            AuthorizationId = authorization.Id,
            CreationDate = DateTimeOffset.UnixEpoch,
            ExpirationDate = DateTimeOffset.UnixEpoch.AddHours(1),
            Payload = "payload-a",
            ReferenceId = "reference-a",
            Status = "valid",
            Subject = "subject-a",
            Type = "refresh_token"
        };

        AssertRecordRoundTrip(application, record =>
        {
            Assert.Equal("client-a", record.ClientId);
            Assert.Equal("https://one.example/callback", Assert.Single(record.RedirectUris));
            Assert.Equal("Application A", record.DisplayNames["en-US"]);
            Assert.True(record.Properties["feature"].GetProperty("enabled").GetBoolean());
        });
        AssertRecordRoundTrip(authorization, record =>
        {
            Assert.Equal(application.Id, record.ApplicationId);
            Assert.Equal(new[] { "api", "profile" }, record.Scopes);
        });
        AssertRecordRoundTrip(scope, record =>
        {
            Assert.Equal("api", record.Name);
            Assert.Equal("resource-a", Assert.Single(record.Resources));
        });
        AssertRecordRoundTrip(token, record =>
        {
            Assert.Equal(authorization.Id, record.AuthorizationId);
            Assert.Equal("reference-a", record.ReferenceId);
            Assert.Equal("refresh_token", record.Type);
        });
    }

    [Fact]
    public void Corrupt_future_wrong_kind_and_identity_mismatch_fail_with_the_adapter_serialization_outcome()
    {
        var future = Envelope(OpenIddictGroundworkJson.TokenDocumentKind, "token-a", "v2", "{}", 1);
        var corrupt = Envelope(OpenIddictGroundworkJson.TokenDocumentKind, "token-a", "v1", "{", 1);
        var wrongKind = Envelope(OpenIddictGroundworkJson.ScopeDocumentKind, "token-a", "v1", "{}", 1);
        var wrongIdentity = Envelope(
            OpenIddictGroundworkJson.TokenDocumentKind,
            "token-a",
            "v1",
            "{\"id\":\"token-b\",\"concurrencyToken\":\"opaque\"}",
            1);

        Assert.IsType<global::Groundwork.Documents.Serialization.DocumentSchemaVersionException>(
            Assert.Throws<OpenIddictGroundworkSerializationException>(() =>
                OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkToken>(future)).InnerException);
        Assert.NotNull(Assert.Throws<OpenIddictGroundworkSerializationException>(() =>
            OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkToken>(corrupt)).InnerException);
        Assert.Null(Assert.Throws<OpenIddictGroundworkSerializationException>(() =>
            OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkToken>(wrongKind)).InnerException);
        Assert.Null(Assert.Throws<OpenIddictGroundworkSerializationException>(() =>
            OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkToken>(wrongIdentity)).InnerException);
    }

    private static void AssertRecordRoundTrip<TRecord>(TRecord record, Action<TRecord> assert)
        where TRecord : OpenIddictGroundworkRecord
    {
        var request = OpenIddictGroundworkRecordSerializer.CreateSaveRequest(record, expectedVersion: 4);
        Assert.Equal("v1", request.SchemaVersion);
        Assert.Equal(record.Id, request.Id);
        Assert.Equal(4, request.ExpectedVersion);
        Assert.DoesNotContain("persistenceVersion", request.ContentJson, StringComparison.Ordinal);

        var restored = OpenIddictGroundworkRecordSerializer.Deserialize<TRecord>(Envelope(
            request.DocumentKind,
            request.Id,
            request.SchemaVersion,
            request.ContentJson,
            5));

        Assert.Equal(5, restored.PersistenceVersion);
        Assert.Equal(record.ConcurrencyToken, restored.ConcurrencyToken);
        assert(restored);
    }

    private static DocumentEnvelope Envelope(
        string kind,
        string id,
        string schemaVersion,
        string content,
        long version) => new(
        kind,
        id,
        schemaVersion,
        version,
        content,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);
}
