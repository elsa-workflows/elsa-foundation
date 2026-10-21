using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design.Api;
using Elsa.Etl.Design.Api.Models;
using Elsa.Etl.Design.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Etl.Tests;

public sealed partial class EtlDesignApiHttpTests
{
    private static readonly string[] ConnectorAuthorPermissions =
    [
        EtlDesignPermissions.ReadDefinitions,
        EtlDesignPermissions.ManageDefinitions,
        EtlDesignPermissions.PublishDefinitions,
        EtlDesignPermissions.ReadBindings
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Connector_api_accepts_discriminator_orders_and_preserves_omitted_budget_defaults(bool kindLast)
    {
        var state = new ConnectorBindingState();
        await using var host = await ConnectorHostAsync(state);
        var pipeline = EtlV2TestPipelines.Create();
        var createPipelineJson = PipelineJson(pipeline, kindLast);
        AssertRetainedBudgetsOmitted(createPipelineJson);

        using var create = await host.SendRawAsync(HttpMethod.Post, "/etl/v2/definitions",
            RawConnectorCreateBody("orders", createPipelineJson),
            permissions: [EtlDesignPermissions.ManageDefinitions]);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var (revision, createdPipeline) = await ReadConnectorDraftPipelineAsync(create);
        Assert.Equal(1, revision);
        AssertConnectorPipeline(createdPipeline, pipeline);

        using var validate = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/validate",
            new EtlValidateRequest("orders", revision), permissions: [EtlDesignPermissions.ManageDefinitions]);
        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
        var validation = await ReadAsync<EtlValidationView>(validate);
        Assert.NotNull(validation.ContentHash);
        Assert.Empty(validation.Diagnostics);

        using var publish = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/publish",
            new EtlPublishRequest("orders", revision, validation.DefinitionVersion, validation.ContentHash!),
            permissions: [EtlDesignPermissions.PublishDefinitions]);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        var updatedPipeline = pipeline with { Http = pipeline.Http with { MaxPageRecords = 9_999 } };
        var updatePipelineJson = PipelineJson(updatedPipeline, kindLast);
        AssertRetainedBudgetsOmitted(updatePipelineJson);
        var updateBody = new JsonObject
        {
            ["expectedRevision"] = revision,
            ["pipeline"] = updatePipelineJson
        };
        using var update = await host.SendRawAsync(HttpMethod.Put, "/etl/v2/definitions/orders/draft",
            updateBody.ToJsonString(), permissions: [EtlDesignPermissions.ManageDefinitions]);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var (updatedRevision, persistedPipeline) = await ReadConnectorDraftPipelineAsync(update);
        Assert.Equal(2, updatedRevision);
        AssertConnectorPipeline(persistedPipeline, updatedPipeline);

        using var validateUpdated = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/validate",
            new EtlValidateRequest("orders", updatedRevision), permissions: [EtlDesignPermissions.ManageDefinitions]);
        Assert.Equal(HttpStatusCode.OK, validateUpdated.StatusCode);
        var updatedValidation = await ReadAsync<EtlValidationView>(validateUpdated);
        Assert.NotNull(updatedValidation.ContentHash);
        Assert.Empty(updatedValidation.Diagnostics);
        using var publishUpdated = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/publish",
            new EtlPublishRequest("orders", updatedRevision, updatedValidation.DefinitionVersion, updatedValidation.ContentHash!),
            permissions: [EtlDesignPermissions.PublishDefinitions]);
        Assert.Equal(HttpStatusCode.OK, publishUpdated.StatusCode);
    }

    [Fact]
    public async Task Connector_api_preserves_explicit_zero_budgets_for_validation()
    {
        var state = new ConnectorBindingState();
        await using var host = await ConnectorHostAsync(state);
        var pipeline = EtlV2TestPipelines.Create();
        var explicitZeroPipeline = pipeline with
        {
            Limits = pipeline.Limits with { MaxRetainedOutputBytes = 0 },
            Http = pipeline.Http with { MaxRetainedMetadataBytes = 0 }
        };
        var zeroPipelineJson = PipelineJson(explicitZeroPipeline, kindLast: false);
        Assert.Equal(0, zeroPipelineJson["limits"]!["maxRetainedOutputBytes"]!.GetValue<long>());
        Assert.Equal(0, zeroPipelineJson["http"]!["maxRetainedMetadataBytes"]!.GetValue<long>());
        using var createExplicitZero = await host.SendRawAsync(HttpMethod.Post, "/etl/v2/definitions",
            RawConnectorCreateBody("explicit-zero", zeroPipelineJson),
            permissions: [EtlDesignPermissions.ManageDefinitions]);
        Assert.Equal(HttpStatusCode.Created, createExplicitZero.StatusCode);
        var (zeroRevision, zeroPipeline) = await ReadConnectorDraftPipelineAsync(createExplicitZero);
        Assert.Equal(0, zeroPipeline.Limits.MaxRetainedOutputBytes);
        Assert.Equal(0, zeroPipeline.Http.MaxRetainedMetadataBytes);
        using var validateExplicitZero = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/explicit-zero/validate",
            new EtlValidateRequest("explicit-zero", zeroRevision), permissions: [EtlDesignPermissions.ManageDefinitions]);
        Assert.Equal(HttpStatusCode.OK, validateExplicitZero.StatusCode);
        var zeroValidation = await ReadAsync<EtlValidationView>(validateExplicitZero);
        Assert.Null(zeroValidation.ContentHash);
        Assert.Contains(zeroValidation.Diagnostics, diagnostic => diagnostic.Code == "limits.invalid");
        Assert.Contains(zeroValidation.Diagnostics, diagnostic => diagnostic.Code == "http.limits.invalid");
    }

    [Fact]
    public async Task Connector_api_rejects_unknown_pipeline_members()
    {
        var state = new ConnectorBindingState();
        await using var host = await ConnectorHostAsync(state);
        var unknownPropertyPipeline = PipelineJson(EtlV2TestPipelines.Create(), kindLast: true);
        unknownPropertyPipeline["futureOption"] = true;
        using var unknownProperty = await host.SendRawAsync(HttpMethod.Post, "/etl/v2/definitions",
            RawConnectorCreateBody("unknown-property", unknownPropertyPipeline),
            permissions: [EtlDesignPermissions.ManageDefinitions]);
        Assert.Equal(HttpStatusCode.BadRequest, unknownProperty.StatusCode);
        Assert.Equal("etl.request-invalid", (await ReadAsync<EtlApiProblem>(unknownProperty)).Code);
    }

    private static JsonObject PipelineJson(EtlPipelineV2 pipeline, bool kindLast)
    {
        var json = JsonNode.Parse(EtlArtifactV2Json.Write(pipeline).GetRawText())!.AsObject();
        var source = json["source"]!.AsObject();
        var routes = json["routes"]!.AsObject();
        if (!kindLast)
        {
            Assert.Equal("kind", source.First().Key);
            Assert.Equal("kind", source["pagination"]!.AsObject().First().Key);
            Assert.Equal("kind", routes["main"]!.AsObject().First().Key);
            Assert.Equal("kind", routes["errors"]!.AsObject().First().Key);
            return json;
        }

        MoveKindToEnd(source["pagination"]!.AsObject());
        MoveKindToEnd(source);
        MoveKindToEnd(routes["main"]!.AsObject());
        MoveKindToEnd(routes["errors"]!.AsObject());
        Assert.Equal("kind", source.Last().Key);
        Assert.Equal("kind", source["pagination"]!.AsObject().Last().Key);
        Assert.Equal("kind", routes["main"]!.AsObject().Last().Key);
        Assert.Equal("kind", routes["errors"]!.AsObject().Last().Key);
        return json;
    }

    private static void AssertRetainedBudgetsOmitted(JsonObject pipeline)
    {
        Assert.False(pipeline["limits"]!.AsObject().ContainsKey("maxRetainedOutputBytes"));
        Assert.False(pipeline["http"]!.AsObject().ContainsKey("maxRetainedMetadataBytes"));
    }

    private static void MoveKindToEnd(JsonObject json)
    {
        var kind = json["kind"]?.DeepClone() ?? throw new InvalidOperationException("A polymorphic declaration must have a kind.");
        json.Remove("kind");
        json.Add("kind", kind);
    }

    private static string RawConnectorCreateBody(string id, JsonObject pipeline) =>
        new JsonObject { ["id"] = id, ["pipeline"] = pipeline }.ToJsonString();

    private static async Task<(long Revision, EtlPipelineV2 Pipeline)> ReadConnectorDraftPipelineAsync(HttpResponseMessage response)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        return (root.GetProperty("revision").GetInt64(), EtlArtifactV2Json.Read(root.GetProperty("pipeline")));
    }

    private static void AssertConnectorPipeline(EtlPipelineV2 actual, EtlPipelineV2 expected)
    {
        Assert.IsType<EtlHttpJsonSource>(actual.Source);
        Assert.IsType<EtlTokenPagination>(Assert.IsType<EtlHttpJsonSource>(actual.Source).Pagination);
        Assert.IsType<EtlHttpDestination>(actual.Routes.Main);
        Assert.IsType<EtlHttpDestination>(actual.Routes.Errors);
        Assert.Equal(EtlConnectorLimits.DefaultMaxRetainedOutputBytes, actual.Limits.MaxRetainedOutputBytes);
        Assert.Equal(EtlHttpPolicy.DefaultMaxRetainedMetadataBytes, actual.Http.MaxRetainedMetadataBytes);
        Assert.Equal(EtlArtifactV2Json.Write(expected).GetRawText(), EtlArtifactV2Json.Write(actual).GetRawText());
    }

    [Fact]
    public async Task Connector_catalog_is_scoped_and_exposes_only_safe_alias_role_and_kind()
    {
        var state = new ConnectorBindingState();
        await using var host = await ConnectorHostAsync(state);

        var pages = new List<EtlConnectorBindingSummary>();
        string? cursor = null;
        do
        {
            var path = cursor is null
                ? "/etl/v2/bindings?limit=2"
                : $"/etl/v2/bindings?limit=2&cursor={Uri.EscapeDataString(cursor)}";
            using var response = await host.SendAsync(HttpMethod.Get, path, permissions: [EtlDesignPermissions.ReadBindings]);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await ReadAsync<EtlPage<EtlConnectorBindingSummary>>(response);
            pages.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.Equal(new[] { "errors", "lookup", "output", "source" }, pages.Select(item => item.Alias));
        Assert.Equal(EtlConnectorKind.Http, Assert.Single(pages, item => item.Alias == "source").Kind);
        Assert.Equal(EtlConnectorKind.Csv, Assert.Single(pages, item => item.Alias == "lookup").Kind);
        Assert.Equal(EtlConnectorKind.Http, Assert.Single(pages, item => item.Alias == "output").Kind);
        var serialized = JsonSerializer.Serialize(pages, EtlDesignApiTestHost.JsonOptions);
        Assert.DoesNotContain("https://", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("revision", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("handle", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", serialized, StringComparison.OrdinalIgnoreCase);

        using var otherTenant = await host.SendAsync(HttpMethod.Get, "/etl/v2/bindings", tenant: "tenant-b", permissions: [EtlDesignPermissions.ReadBindings]);
        Assert.Equal("foreign-tenant", Assert.Single((await ReadAsync<EtlPage<EtlConnectorBindingSummary>>(otherTenant)).Items).Alias);
        using var otherPartition = await host.SendAsync(HttpMethod.Get, "/etl/v2/bindings", partition: "partition-b", permissions: [EtlDesignPermissions.ReadBindings]);
        Assert.Equal("foreign-partition", Assert.Single((await ReadAsync<EtlPage<EtlConnectorBindingSummary>>(otherPartition)).Items).Alias);

        using var forbidden = await host.SendAsync(HttpMethod.Get, "/etl/v2/bindings");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Connector_publication_rechecks_approval_and_exact_replay_does_not_resolve_again()
    {
        var state = new ConnectorBindingState();
        await using var host = await ConnectorHostAsync(state);
        using var create = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions",
            new EtlConnectorCreateDefinitionRequest("orders", EtlV2TestPipelines.Create()),
            permissions: [EtlDesignPermissions.ManageDefinitions]);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var draft = await ReadAsync<EtlConnectorDraftView>(create);

        using var validate = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/validate",
            new EtlValidateRequest("orders", draft.Revision), permissions: [EtlDesignPermissions.ManageDefinitions]);
        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
        var validation = await ReadAsync<EtlValidationView>(validate);
        Assert.NotNull(validation.ContentHash);
        Assert.Empty(validation.Diagnostics);
        var publishRequest = new EtlPublishRequest("orders", validation.Revision, validation.DefinitionVersion, validation.ContentHash!);

        var callsBeforeForbidden = state.ResolutionCalls;
        using var forbiddenPublish = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/publish", publishRequest,
            permissions: [EtlDesignPermissions.ManageDefinitions]);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenPublish.StatusCode);
        Assert.Equal(callsBeforeForbidden, state.ResolutionCalls);

        state.Approved = false;
        using var refused = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/publish", publishRequest,
            permissions: [EtlDesignPermissions.PublishDefinitions]);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("etl.validation-failed", (await ReadAsync<EtlApiProblem>(refused)).Code);
        var resolverCallsBeforePublish = state.ResolutionCalls;

        state.Approved = true;
        using var published = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/publish", publishRequest,
            permissions: [EtlDesignPermissions.PublishDefinitions]);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var publication = await ReadAsync<EtlPublicationView>(published);
        Assert.Equal("published", publication.Status);
        Assert.Equal(resolverCallsBeforePublish + 1, state.ResolutionCalls);

        var editedPipeline = EtlV2TestPipelines.Create() with
        {
            Http = EtlV2TestPipelines.Create().Http with { MaxPageRecords = 9_999 }
        };
        using var update = await host.SendAsync(HttpMethod.Put, "/etl/v2/definitions/orders/draft",
            new EtlConnectorUpdateDraftRequest("orders", validation.Revision, editedPipeline),
            permissions: [EtlDesignPermissions.ManageDefinitions]);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(validation.Revision + 1, (await ReadAsync<EtlConnectorDraftView>(update)).Revision);

        state.Approved = false;
        var resolverCallsBeforeReplay = state.ResolutionCalls;
        using var replay = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/publish", publishRequest,
            permissions: [EtlDesignPermissions.PublishDefinitions]);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await ReadAsync<EtlPublicationView>(replay);
        Assert.Equal("replay", replayed.Status);
        Assert.Equal(publication.Version, replayed.Version);
        Assert.Equal(resolverCallsBeforeReplay, state.ResolutionCalls);
    }

    [Fact]
    public async Task Missing_v1_version_history_is_not_found_without_the_optional_v2_store()
    {
        await using var host = await EtlDesignApiTestHost.StartAsync(configureServices: services =>
            services.RemoveAll<IEtlDefinitionStoreV2>());

        using var response = await host.SendAsync(HttpMethod.Get, "/etl/definitions/missing/versions",
            permissions: [EtlDesignPermissions.ReadDefinitions]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("etl.definition-not-found", (await ReadAsync<EtlApiProblem>(response)).Code);
    }

    [Fact]
    public async Task Connector_api_returns_safe_refusal_for_null_tenant_and_unavailable_optional_resolver()
    {
        var state = new ConnectorBindingState();
        await using (var host = await ConnectorHostAsync(state))
        {
            using var create = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions",
                new EtlConnectorCreateDefinitionRequest("unscoped", EtlV2TestPipelines.Create()), tenant: null,
                permissions: [EtlDesignPermissions.ManageDefinitions]);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);

            using var validate = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/unscoped/validate",
                new EtlValidateRequest("unscoped", 1), tenant: null, permissions: [EtlDesignPermissions.ManageDefinitions]);
            Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
            var result = await ReadAsync<EtlValidationView>(validate);
            Assert.Null(result.ContentHash);
            Assert.Equal("binding.alias.unavailable", Assert.Single(result.Diagnostics).Code);
            Assert.Null(state.LastResolvedScope?.TenantId);
        }

        await using (var host = await EtlDesignApiTestHost.StartAsync(configureServices: services =>
        {
            var state = new ConnectorBindingState();
            services.AddSingleton(state);
            services.AddScoped<IEtlConnectorBindingCatalog, TestConnectorBindingCatalog>();
        }))
        {
            using var create = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions",
                new EtlConnectorCreateDefinitionRequest("orders", EtlV2TestPipelines.Create()),
                permissions: [EtlDesignPermissions.ManageDefinitions]);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);

            using var validate = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/orders/validate",
                new EtlValidateRequest("orders", 1), permissions: [EtlDesignPermissions.ManageDefinitions]);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, validate.StatusCode);
            Assert.Equal("etl.provider-unavailable", (await ReadAsync<EtlApiProblem>(validate)).Code);
        }
    }

    [Fact]
    public async Task Versioned_lists_keep_mixed_v1_v2_history_on_the_matching_api_surface()
    {
        var state = new ConnectorBindingState();
        await using var host = await ConnectorHostAsync(state);
        using var createV1 = await host.SendAsync(HttpMethod.Post, "/etl/definitions",
            new EtlCreateDefinitionRequest("mixed", EtlTestPipelines.Create()), permissions: [EtlDesignPermissions.ManageDefinitions]);
        Assert.Equal(HttpStatusCode.Created, createV1.StatusCode);
        using var validateV1 = await host.SendAsync(HttpMethod.Post, "/etl/definitions/mixed/validate",
            new EtlValidateRequest("mixed", 1), permissions: [EtlDesignPermissions.ManageDefinitions]);
        var validationV1 = await ReadAsync<EtlValidationView>(validateV1);
        using var publishV1 = await host.SendAsync(HttpMethod.Post, "/etl/definitions/mixed/publish",
            new EtlPublishRequest("mixed", 1, 1, validationV1.ContentHash!), permissions: [EtlDesignPermissions.PublishDefinitions]);
        Assert.Equal(HttpStatusCode.OK, publishV1.StatusCode);

        using var updateV2 = await host.SendAsync(HttpMethod.Put, "/etl/v2/definitions/mixed/draft",
            new EtlConnectorUpdateDraftRequest("mixed", 1, EtlV2TestPipelines.Create()), permissions: [EtlDesignPermissions.ManageDefinitions]);
        Assert.Equal(HttpStatusCode.OK, updateV2.StatusCode);
        var v2Draft = await ReadAsync<EtlConnectorDraftView>(updateV2);
        using var validateV2 = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/mixed/validate",
            new EtlValidateRequest("mixed", v2Draft.Revision), permissions: [EtlDesignPermissions.ManageDefinitions]);
        var validationV2 = await ReadAsync<EtlValidationView>(validateV2);
        using var publishV2 = await host.SendAsync(HttpMethod.Post, "/etl/v2/definitions/mixed/publish",
            new EtlPublishRequest("mixed", v2Draft.Revision, 2, validationV2.ContentHash!), permissions: [EtlDesignPermissions.PublishDefinitions]);
        Assert.Equal(HttpStatusCode.OK, publishV2.StatusCode);

        using var v1Definitions = await host.SendAsync(HttpMethod.Get, "/etl/definitions?limit=1", permissions: [EtlDesignPermissions.ReadDefinitions]);
        Assert.DoesNotContain((await ReadAsync<EtlPage<EtlDefinitionSummaryView>>(v1Definitions)).Items,
            item => item.DefinitionId == "mixed");
        using var v2Definitions = await host.SendAsync(HttpMethod.Get, "/etl/v2/definitions?limit=1", permissions: [EtlDesignPermissions.ReadDefinitions]);
        Assert.Contains((await ReadAsync<EtlPage<EtlDefinitionSummaryView>>(v2Definitions)).Items,
            item => item.DefinitionId == "mixed");

        using var v1Versions = await host.SendAsync(HttpMethod.Get, "/etl/definitions/mixed/versions?limit=1", permissions: [EtlDesignPermissions.ReadDefinitions]);
        Assert.Equal(new[] { 1 }, (await ReadAsync<EtlPage<EtlPublishedVersionView>>(v1Versions)).Items.Select(item => item.DefinitionVersion));
        using var v2VersionsFirst = await host.SendAsync(HttpMethod.Get, "/etl/v2/definitions/mixed/versions?limit=1", permissions: [EtlDesignPermissions.ReadDefinitions]);
        var firstV2Page = await ReadAsync<EtlPage<EtlPublishedVersionView>>(v2VersionsFirst);
        Assert.Empty(firstV2Page.Items);
        Assert.NotNull(firstV2Page.NextCursor);
        using var v2VersionsSecond = await host.SendAsync(HttpMethod.Get,
            $"/etl/v2/definitions/mixed/versions?limit=1&cursor={Uri.EscapeDataString(firstV2Page.NextCursor!)}",
            permissions: [EtlDesignPermissions.ReadDefinitions]);
        Assert.Equal(new[] { 2 }, (await ReadAsync<EtlPage<EtlPublishedVersionView>>(v2VersionsSecond)).Items.Select(item => item.DefinitionVersion));

        using var v1Version = await host.SendAsync(HttpMethod.Get, "/etl/definitions/mixed/versions/1", permissions: [EtlDesignPermissions.ReadDefinitions]);
        Assert.Equal(HttpStatusCode.OK, v1Version.StatusCode);
        using var wrongFormatVersion = await host.SendAsync(HttpMethod.Get, "/etl/definitions/mixed/versions/2", permissions: [EtlDesignPermissions.ReadDefinitions]);
        Assert.Equal(HttpStatusCode.NotFound, wrongFormatVersion.StatusCode);
        using var v2Version = await host.SendAsync(HttpMethod.Get, "/etl/v2/definitions/mixed/versions/2", permissions: [EtlDesignPermissions.ReadDefinitions]);
        Assert.Equal(HttpStatusCode.OK, v2Version.StatusCode);
        using var wrongV2FormatVersion = await host.SendAsync(HttpMethod.Get, "/etl/v2/definitions/mixed/versions/1", permissions: [EtlDesignPermissions.ReadDefinitions]);
        Assert.Equal(HttpStatusCode.NotFound, wrongV2FormatVersion.StatusCode);
    }

    private static Task<EtlDesignApiTestHost> ConnectorHostAsync(ConnectorBindingState state) =>
        EtlDesignApiTestHost.StartAsync(configureServices: services =>
        {
            services.AddSingleton(state);
            services.AddScoped<IEtlConnectorBindingCatalog, TestConnectorBindingCatalog>();
            services.AddScoped<IEtlConnectorBindingResolver, TestConnectorBindingResolver>();
        });

    private sealed class ConnectorBindingState
    {
        public bool Approved { get; set; } = true;
        public int ResolutionCalls { get; set; }
        public EtlScope? LastResolvedScope { get; set; }
    }

    private sealed class TestConnectorBindingCatalog : IEtlConnectorBindingCatalog
    {
        private static readonly (EtlScope Scope, EtlConnectorBindingSummary Binding)[] Bindings =
        [
            (new("tenant-a", "partition-a"), new("errors", "ErrorOutput", EtlConnectorKind.Http)),
            (new("tenant-a", "partition-a"), new("lookup", "Lookup", EtlConnectorKind.Csv)),
            (new("tenant-a", "partition-a"), new("output", "MainOutput", EtlConnectorKind.Http)),
            (new("tenant-a", "partition-a"), new("source", "Source", EtlConnectorKind.Http)),
            (new("tenant-b", "partition-a"), new("foreign-tenant", "Source", EtlConnectorKind.Http)),
            (new("tenant-a", "partition-b"), new("foreign-partition", "Source", EtlConnectorKind.Http))
        ];

        public IReadOnlyList<EtlConnectorBindingSummary> ListBindings(EtlScope scope, string? afterAlias, string? afterRole, int take)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(take, 1000);
            if ((afterAlias is null) != (afterRole is null))
                throw new ArgumentException("A binding page boundary requires both alias and role.");
            return Bindings.Where(row => row.Scope == scope).Select(row => row.Binding)
                .Where(item => afterAlias is null || StringComparer.Ordinal.Compare(item.Alias, afterAlias) > 0 ||
                    StringComparer.Ordinal.Equals(item.Alias, afterAlias) && StringComparer.Ordinal.Compare(item.Role, afterRole) > 0)
                .OrderBy(item => item.Alias, StringComparer.Ordinal).ThenBy(item => item.Role, StringComparer.Ordinal)
                .Take(take).ToArray();
        }
    }

    private sealed class TestConnectorBindingResolver(ConnectorBindingState state) : IEtlConnectorBindingResolver
    {
        public ValueTask<EtlConnectorBindingResolution> ResolveAsync(EtlScope scope, EtlPipelineV2 pipeline,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.ResolutionCalls++;
            state.LastResolvedScope = scope;
            if (!state.Approved || scope.TenantId is null)
                return ValueTask.FromResult(new EtlConnectorBindingResolution(null, "etl.binding-unavailable"));

            var source = new EtlConnectorBindingRevision(EtlConnectorKind.Http,
                new EtlBindingRevision("source", "http-source-v1", new string('a', 64), null));
            var main = new EtlConnectorBindingRevision(EtlConnectorKind.Http,
                new EtlBindingRevision("output", "http-output-v1", new string('b', 64), null));
            var errors = new EtlConnectorBindingRevision(EtlConnectorKind.Http,
                new EtlBindingRevision("errors", "http-errors-v1", new string('c', 64), null));
            var bindings = new EtlConnectorRunBindings(source, null, main, errors, ["shard-1"]);
            return ValueTask.FromResult(new EtlConnectorBindingResolution(bindings, null));
        }
    }
}
