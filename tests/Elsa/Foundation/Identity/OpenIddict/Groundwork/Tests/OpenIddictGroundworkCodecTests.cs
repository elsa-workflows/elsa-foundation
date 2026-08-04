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
        Assert.Equal(5, OpenIddictGroundworkJson.Policies.Count);
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
            ApplicationType = "web",
            ClientId = "client-a",
            ClientSecret = "secret-a",
            ClientType = "confidential",
            ConsentType = "explicit",
            DisplayName = "Application A",
            RedirectUris = ["https://one.example/callback"],
            PostLogoutRedirectUris = ["https://one.example/logout"],
            JsonWebKeySet = "{\"keys\":[]}",
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
            Type = "permanent",
            Properties = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal) { ["feature"] = property.RootElement.Clone() }
        };
        var scope = new OpenIddictGroundworkScope
        {
            Id = "scope-a",
            Description = "API scope",
            DisplayName = "API",
            Name = "api",
            Resources = ["resource-a"],
            Descriptions = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["en-US"] = "API scope" },
            DisplayNames = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["en-US"] = "API" },
            Properties = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal) { ["feature"] = property.RootElement.Clone() }
        };
        var token = new OpenIddictGroundworkToken
        {
            Id = "token-a",
            ApplicationId = application.Id,
            AuthorizationId = authorization.Id,
            CreationDate = DateTimeOffset.UnixEpoch,
            ExpirationDate = DateTimeOffset.UnixEpoch.AddHours(1),
            Payload = "payload-a",
            Properties = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal) { ["feature"] = property.RootElement.Clone() },
            RedemptionDate = DateTimeOffset.UnixEpoch.AddMinutes(30),
            ReferenceId = "reference-a",
            Status = "valid",
            Subject = "subject-a",
            Type = "refresh_token"
        };

        AssertRecordRoundTrip(application, record =>
        {
            Assert.Equal("web", record.ApplicationType);
            Assert.Equal("client-a", record.ClientId);
            Assert.Equal("secret-a", record.ClientSecret);
            Assert.Equal("confidential", record.ClientType);
            Assert.Equal("explicit", record.ConsentType);
            Assert.Equal("Application A", record.DisplayName);
            Assert.Equal("https://one.example/callback", Assert.Single(record.RedirectUris));
            Assert.Equal("https://one.example/logout", Assert.Single(record.PostLogoutRedirectUris));
            Assert.Equal("{\"keys\":[]}", record.JsonWebKeySet);
            Assert.Equal("endpoint:token", Assert.Single(record.Permissions));
            Assert.Equal("pkce", Assert.Single(record.Requirements));
            Assert.Equal("Application A", record.DisplayNames["en-US"]);
            Assert.True(record.Properties["feature"].GetProperty("enabled").GetBoolean());
            Assert.Equal("strict", record.Settings["mode"]);
        });
        AssertRecordRoundTrip(authorization, record =>
        {
            Assert.Equal(application.Id, record.ApplicationId);
            Assert.Equal(DateTimeOffset.UnixEpoch, record.CreationDate);
            Assert.Equal(new[] { "api", "profile" }, record.Scopes);
            Assert.Equal("valid", record.Status);
            Assert.Equal("subject-a", record.Subject);
            Assert.Equal("permanent", record.Type);
            Assert.True(record.Properties["feature"].GetProperty("enabled").GetBoolean());
        });
        AssertRecordRoundTrip(scope, record =>
        {
            Assert.Equal("API scope", record.Description);
            Assert.Equal("API scope", record.Descriptions["en-US"]);
            Assert.Equal("API", record.DisplayName);
            Assert.Equal("API", record.DisplayNames["en-US"]);
            Assert.Equal("api", record.Name);
            Assert.Equal("resource-a", Assert.Single(record.Resources));
            Assert.True(record.Properties["feature"].GetProperty("enabled").GetBoolean());
        });
        AssertRecordRoundTrip(token, record =>
        {
            Assert.Equal(application.Id, record.ApplicationId);
            Assert.Equal(authorization.Id, record.AuthorizationId);
            Assert.Equal(DateTimeOffset.UnixEpoch, record.CreationDate);
            Assert.Equal(DateTimeOffset.UnixEpoch.AddHours(1), record.ExpirationDate);
            Assert.Equal("payload-a", record.Payload);
            Assert.True(record.Properties["feature"].GetProperty("enabled").GetBoolean());
            Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(30), record.RedemptionDate);
            Assert.Equal("reference-a", record.ReferenceId);
            Assert.Equal("valid", record.Status);
            Assert.Equal("subject-a", record.Subject);
            Assert.Equal("refresh_token", record.Type);
        });
    }

    [Theory]
    [InlineData("Application")]
    [InlineData("Authorization")]
    [InlineData("Scope")]
    [InlineData("Token")]
    public void Every_record_kind_rejects_corrupt_future_wrong_kind_and_identity_mismatch(string recordKind)
    {
        switch (recordKind)
        {
            case "Application":
                AssertRejectedPayloads<OpenIddictGroundworkApplication>(
                    OpenIddictGroundworkJson.ApplicationDocumentKind,
                    OpenIddictGroundworkJson.AuthorizationDocumentKind);
                break;
            case "Authorization":
                AssertRejectedPayloads<OpenIddictGroundworkAuthorization>(
                    OpenIddictGroundworkJson.AuthorizationDocumentKind,
                    OpenIddictGroundworkJson.ScopeDocumentKind);
                break;
            case "Scope":
                AssertRejectedPayloads<OpenIddictGroundworkScope>(
                    OpenIddictGroundworkJson.ScopeDocumentKind,
                    OpenIddictGroundworkJson.TokenDocumentKind);
                break;
            case "Token":
                AssertRejectedPayloads<OpenIddictGroundworkToken>(
                    OpenIddictGroundworkJson.TokenDocumentKind,
                    OpenIddictGroundworkJson.ApplicationDocumentKind);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(recordKind));
        }
    }

    private static void AssertRejectedPayloads<TRecord>(string documentKind, string wrongDocumentKind)
        where TRecord : OpenIddictGroundworkRecord
    {
        var future = Envelope(documentKind, "record-a", "v2", "{}", 1);
        var corrupt = Envelope(documentKind, "record-a", "v1", "{", 1);
        var wrongKind = Envelope(wrongDocumentKind, "record-a", "v1", "{}", 1);
        var wrongIdentity = Envelope(
            documentKind,
            "record-a",
            "v1",
            "{\"id\":\"record-b\",\"concurrencyToken\":\"opaque\"}",
            1);
        var missingConcurrencyToken = Envelope(documentKind, "record-a", "v1", "{\"id\":\"record-a\"}", 1);
        var nullConcurrencyToken = Envelope(
            documentKind,
            "record-a",
            "v1",
            "{\"id\":\"record-a\",\"concurrencyToken\":null}",
            1);
        var blankConcurrencyToken = Envelope(
            documentKind,
            "record-a",
            "v1",
            "{\"id\":\"record-a\",\"concurrencyToken\":\"   \"}",
            1);

        Assert.IsType<global::Groundwork.Documents.Serialization.DocumentSchemaVersionException>(
            Assert.Throws<OpenIddictGroundworkSerializationException>(() =>
                OpenIddictGroundworkRecordSerializer.Deserialize<TRecord>(future)).InnerException);
        Assert.NotNull(Assert.Throws<OpenIddictGroundworkSerializationException>(() =>
            OpenIddictGroundworkRecordSerializer.Deserialize<TRecord>(corrupt)).InnerException);
        Assert.Null(Assert.Throws<OpenIddictGroundworkSerializationException>(() =>
            OpenIddictGroundworkRecordSerializer.Deserialize<TRecord>(wrongKind)).InnerException);
        Assert.Null(Assert.Throws<OpenIddictGroundworkSerializationException>(() =>
            OpenIddictGroundworkRecordSerializer.Deserialize<TRecord>(wrongIdentity)).InnerException);
        Assert.NotNull(Assert.Throws<OpenIddictGroundworkSerializationException>(() =>
            OpenIddictGroundworkRecordSerializer.Deserialize<TRecord>(missingConcurrencyToken)).InnerException);
        Assert.Null(Assert.Throws<OpenIddictGroundworkSerializationException>(() =>
            OpenIddictGroundworkRecordSerializer.Deserialize<TRecord>(nullConcurrencyToken)).InnerException);
        Assert.Null(Assert.Throws<OpenIddictGroundworkSerializationException>(() =>
            OpenIddictGroundworkRecordSerializer.Deserialize<TRecord>(blankConcurrencyToken)).InnerException);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Save_rejects_a_missing_or_blank_concurrency_token(string? concurrencyToken)
    {
        var record = new OpenIddictGroundworkApplication
        {
            Id = "application-a",
            ConcurrencyToken = concurrencyToken!
        };

        Assert.Throws<OpenIddictGroundworkSerializationException>(() =>
            OpenIddictGroundworkRecordSerializer.CreateSaveRequest(record));
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
