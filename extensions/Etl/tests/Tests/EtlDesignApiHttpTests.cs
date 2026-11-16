using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design.Api;
using Elsa.Etl.Design.Api.Models;
using Elsa.Etl.Runtime.Bindings;

namespace Elsa.Etl.Tests;

public sealed partial class EtlDesignApiHttpTests
{
    private static readonly string[] AuthorPermissions =
    [
        EtlDesignPermissions.ReadDefinitions,
        EtlDesignPermissions.ManageDefinitions,
        EtlDesignPermissions.PublishDefinitions,
        EtlDesignPermissions.ReadBindings
    ];

    private static readonly string[] DefinitionRead = [EtlDesignPermissions.ReadDefinitions];

    [Fact]
    public async Task Http_authoring_flow_compiles_publishes_and_replays_the_validated_version_after_a_later_draft_edit()
    {
        await using var host = await EtlDesignApiTestHost.StartAsync();

        using var createResponse = await host.SendAsync(HttpMethod.Post, "/etl/definitions", new EtlCreateDefinitionRequest("orders", EtlTestPipelines.Create()), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadAsync<EtlDraftView>(createResponse);
        Assert.Equal("orders", created.DefinitionId);
        Assert.Equal(1, created.Revision);
        Assert.Equal(0, created.LastPublishedVersion);

        using var getResponse = await host.SendAsync(HttpMethod.Get, "/etl/definitions/orders", permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var read = await ReadAsync<EtlDraftView>(getResponse);
        Assert.Equal(created.Pipeline.Mapping, read.Pipeline.Mapping);

        // The route ID wins over a JSON body ID; the API DTO intentionally ignores the body copy.
        using var updateResponse = await host.SendAsync(HttpMethod.Put, "/etl/definitions/orders/draft",
            new { id = "body-spoof", expectedRevision = created.Revision, pipeline = EtlTestPipelines.Create("name-v1") },
            permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await ReadAsync<EtlDraftView>(updateResponse);
        Assert.Equal("orders", updated.DefinitionId);
        Assert.Equal(2, updated.Revision);
        using var bodyId = await host.SendAsync(HttpMethod.Get, "/etl/definitions/body-spoof", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.NotFound, bodyId.StatusCode);

        using var validateResponse = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/validate",
            new { id = "body-spoof", expectedRevision = updated.Revision }, permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);
        var validationV1 = await ReadAsync<EtlValidationView>(validateResponse);
        Assert.Equal(updated.Revision, validationV1.Revision);
        Assert.Equal(1, validationV1.DefinitionVersion);
        Assert.NotNull(validationV1.ContentHash);
        Assert.Matches("^[0-9a-f]{64}$", validationV1.ContentHash);
        Assert.Empty(validationV1.Diagnostics);

        using var publishV1Response = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish",
            new { id = "body-spoof", expectedRevision = validationV1.Revision, definitionVersion = validationV1.DefinitionVersion, contentHash = validationV1.ContentHash },
            permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, publishV1Response.StatusCode);
        var publishedV1 = await ReadAsync<EtlPublicationView>(publishV1Response);
        Assert.Equal("published", publishedV1.Status);
        Assert.Equal(1, publishedV1.Version.DefinitionVersion);
        Assert.Equal(validationV1.ContentHash, publishedV1.Version.ContentHash);
        Assert.False(string.IsNullOrWhiteSpace(publishedV1.Version.Selection));

        using var publishedArtifactResponse = await host.SendAsync(HttpMethod.Get, "/etl/definitions/orders/versions/1", permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, publishedArtifactResponse.StatusCode);
        var publishedArtifact = await ReadAsync<EtlPublishedArtifactView>(publishedArtifactResponse);
        Assert.Equal(validationV1.ContentHash, publishedArtifact.Version.ContentHash);
        Assert.Equal(EtlTestPipelines.Create("name-v1").Mapping, publishedArtifact.Pipeline.Mapping);

        using var readCurrentResponse = await host.SendAsync(HttpMethod.Get, "/etl/definitions/orders", permissions: AuthorPermissions);
        var current = await ReadAsync<EtlDraftView>(readCurrentResponse);
        using var updateV2Response = await host.SendAsync(HttpMethod.Put, "/etl/definitions/orders/draft",
            new EtlUpdateDraftRequest("orders", current.Revision, EtlTestPipelines.Create("name-v2")), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, updateV2Response.StatusCode);
        var updatedV2 = await ReadAsync<EtlDraftView>(updateV2Response);
        using var validateV2Response = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/validate",
            new EtlValidateRequest("orders", updatedV2.Revision), permissions: AuthorPermissions);
        var validationV2 = await ReadAsync<EtlValidationView>(validateV2Response);
        Assert.Equal(2, validationV2.DefinitionVersion);
        Assert.NotEqual(validationV1.ContentHash, validationV2.ContentHash);

        using var publishV2Response = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish",
            new EtlPublishRequest("orders", validationV2.Revision, validationV2.DefinitionVersion, validationV2.ContentHash!),
            permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, publishV2Response.StatusCode);
        Assert.Equal("published", (await ReadAsync<EtlPublicationView>(publishV2Response)).Status);

        // Retrying the exact validation tuple for v1 must replay v1, even though v2 is now current.
        using var retryV1Response = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish",
            new EtlPublishRequest("orders", validationV1.Revision, validationV1.DefinitionVersion, validationV1.ContentHash!),
            permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, retryV1Response.StatusCode);
        var retryV1 = await ReadAsync<EtlPublicationView>(retryV1Response);
        Assert.Equal("replay", retryV1.Status);
        Assert.Equal(1, retryV1.Version.DefinitionVersion);
        Assert.Equal(validationV1.ContentHash, retryV1.Version.ContentHash);
    }

    [Fact]
    public async Task Publish_rejects_a_hash_mismatch_and_stale_unpublished_validation_without_creating_a_version()
    {
        await using var host = await EtlDesignApiTestHost.StartAsync();
        await CreateDefinitionAsync(host, "orders");
        var validation = await ValidateAsync(host, "orders", expectedRevision: 1);

        using var badHashResponse = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish",
            new EtlPublishRequest("orders", validation.Revision, validation.DefinitionVersion, new string('0', 64)), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.Conflict, badHashResponse.StatusCode);
        AssertSafeProblem(await ReadAsync<EtlApiProblem>(badHashResponse), HttpStatusCode.Conflict);

        using var missingV1 = await host.SendAsync(HttpMethod.Get, "/etl/definitions/orders/versions/1", permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.NotFound, missingV1.StatusCode);

        using var staleResponse = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish",
            new EtlPublishRequest("orders", validation.Revision + 1, validation.DefinitionVersion, validation.ContentHash!), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        AssertSafeProblem(await ReadAsync<EtlApiProblem>(staleResponse), HttpStatusCode.Conflict);

        using var publishResponse = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish",
            new EtlPublishRequest("orders", validation.Revision, validation.DefinitionVersion, validation.ContentHash!), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        using var conflictingReplay = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish",
            new EtlPublishRequest("orders", validation.Revision, validation.DefinitionVersion, new string('f', 64)), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.Conflict, conflictingReplay.StatusCode);
        AssertSafeProblem(await ReadAsync<EtlApiProblem>(conflictingReplay), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Concurrent_identical_publish_requests_converge_to_published_and_replay()
    {
        await using var host = await EtlDesignApiTestHost.StartAsync();
        await CreateDefinitionAsync(host, "orders");
        var validation = await ValidateAsync(host, "orders", expectedRevision: 1);
        var tuple = new EtlPublishRequest("orders", validation.Revision, validation.DefinitionVersion, validation.ContentHash!);

        var responses = await Task.WhenAll(
            host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish", tuple, permissions: AuthorPermissions),
            host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish", tuple, permissions: AuthorPermissions));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var views = await Task.WhenAll(responses.Select(ReadAsync<EtlPublicationView>));
            Assert.Equal(new[] { "published", "replay" }, views.Select(view => view.Status).OrderBy(value => value, StringComparer.Ordinal));
            Assert.All(views, view =>
            {
                Assert.Equal(1, view.Version.DefinitionVersion);
                Assert.Equal(validation.ContentHash, view.Version.ContentHash);
            });
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task Permission_and_trusted_scope_are_enforced_before_data_is_created_or_disclosed()
    {
        await using var host = await EtlDesignApiTestHost.StartAsync();
        var body = new
        {
            id = "orders",
            tenantId = "tenant-b",
            persistencePartition = "partition-b",
            pipeline = EtlTestPipelines.Create()
        };

        using var anonymous = await host.SendAsync(HttpMethod.Post, "/etl/definitions", body, authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var denied = await host.SendAsync(HttpMethod.Post, "/etl/definitions", body, permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var unnormalized = await host.SendAsync(HttpMethod.Post, "/etl/definitions",
            new EtlCreateDefinitionRequest("unnormalized", EtlTestPipelines.Create()),
            permissions: AuthorPermissions, normalized: false);
        // Foundation Identity challenges a principal without its trusted normalization marker.
        Assert.Equal(HttpStatusCode.Unauthorized, unnormalized.StatusCode);

        using var ambiguousTenant = await host.SendAsync(HttpMethod.Post, "/etl/definitions",
            new EtlCreateDefinitionRequest("ambiguous-tenant", EtlTestPipelines.Create()),
            permissions: AuthorPermissions, duplicateTenant: true);
        Assert.Equal(HttpStatusCode.Forbidden, ambiguousTenant.StatusCode);

        foreach (var persistenceContext in new[] { "global", "privileged-global" })
        {
            using var unscoped = await host.SendAsync(HttpMethod.Post, "/etl/definitions",
                new EtlCreateDefinitionRequest($"{persistenceContext}-scope", EtlTestPipelines.Create()),
                permissions: AuthorPermissions, persistenceContext: persistenceContext);
            Assert.Equal(HttpStatusCode.Forbidden, unscoped.StatusCode);
        }

        using var empty = await host.SendAsync(HttpMethod.Get, "/etl/definitions", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Empty((await ReadAsync<EtlPage<EtlDefinitionSummaryView>>(empty)).Items);

        // Tenant and partition are not accepted request fields. An attempted body override is rejected
        // before any definition is created; scope comes from the normalized identity and host binding.
        using var authorityInjection = await host.SendAsync(HttpMethod.Post, "/etl/definitions", body, permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.BadRequest, authorityInjection.StatusCode);
        using var stillEmpty = await host.SendAsync(HttpMethod.Get, "/etl/definitions", permissions: DefinitionRead);
        Assert.Empty((await ReadAsync<EtlPage<EtlDefinitionSummaryView>>(stillEmpty)).Items);

        using var create = await host.SendAsync(HttpMethod.Post, "/etl/definitions",
            new EtlCreateDefinitionRequest("orders", EtlTestPipelines.Create()), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal("orders", (await ReadAsync<EtlDraftView>(create)).DefinitionId);

        foreach (var (tenant, partition) in new[] { ("tenant-b", "partition-a"), ("tenant-a", "partition-b") })
        {
            using var foreign = await host.SendAsync(HttpMethod.Get, "/etl/definitions/orders", tenant: tenant, partition: partition, permissions: DefinitionRead);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            var bodyText = await foreign.Content.ReadAsStringAsync();
            Assert.DoesNotContain("orders", bodyText, StringComparison.Ordinal);
            Assert.DoesNotContain("tenant-a", bodyText, StringComparison.Ordinal);
            Assert.DoesNotContain("partition-a", bodyText, StringComparison.Ordinal);
        }

        using var sameScope = await host.SendAsync(HttpMethod.Get, "/etl/definitions/orders", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.OK, sameScope.StatusCode);

        using var unnormalizedRead = await host.SendAsync(HttpMethod.Get, "/etl/definitions/orders",
            permissions: AuthorPermissions, normalized: false);
        Assert.Equal(HttpStatusCode.Unauthorized, unnormalizedRead.StatusCode);
        using var ambiguousTenantRead = await host.SendAsync(HttpMethod.Get, "/etl/definitions/orders",
            permissions: AuthorPermissions, duplicateTenant: true);
        Assert.Equal(HttpStatusCode.Forbidden, ambiguousTenantRead.StatusCode);
        foreach (var persistenceContext in new[] { "global", "privileged-global" })
        {
            using var unscopedRead = await host.SendAsync(HttpMethod.Get, "/etl/definitions/orders",
                permissions: AuthorPermissions, persistenceContext: persistenceContext);
            Assert.Equal(HttpStatusCode.Forbidden, unscopedRead.StatusCode);
        }
    }

    [Fact]
    public async Task Missing_design_store_or_catalog_returns_provider_unavailable_instead_of_an_empty_result()
    {
        await using var host = await EtlDesignApiTestHost.StartAsync(registerDesignPersistence: false);

        using var list = await host.SendAsync(HttpMethod.Get, "/etl/definitions", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, list.StatusCode);
        AssertSafeProblem(await ReadAsync<EtlApiProblem>(list), HttpStatusCode.ServiceUnavailable);

        using var create = await host.SendAsync(HttpMethod.Post, "/etl/definitions",
            new EtlCreateDefinitionRequest("orders", EtlTestPipelines.Create()), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
        AssertSafeProblem(await ReadAsync<EtlApiProblem>(create), HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Definition_cursor_is_signed_and_bound_to_scope_and_limit()
    {
        await using var host = await EtlDesignApiTestHost.StartAsync();
        foreach (var id in new[] { "alpha", "bravo", "charlie" })
            await CreateDefinitionAsync(host, id);

        using var firstResponse = await host.SendAsync(HttpMethod.Get, "/etl/definitions?limit=1", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = await ReadAsync<EtlPage<EtlDefinitionSummaryView>>(firstResponse);
        Assert.Equal("alpha", Assert.Single(first.Items).DefinitionId);
        Assert.False(string.IsNullOrWhiteSpace(first.NextCursor));
        var cursor = first.NextCursor!;

        using var nextResponse = await host.SendAsync(HttpMethod.Get, $"/etl/definitions?limit=1&cursor={Uri.EscapeDataString(cursor)}", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);
        Assert.Equal("bravo", Assert.Single((await ReadAsync<EtlPage<EtlDefinitionSummaryView>>(nextResponse)).Items).DefinitionId);

        using var changedLimit = await host.SendAsync(HttpMethod.Get, $"/etl/definitions?limit=2&cursor={Uri.EscapeDataString(cursor)}", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.BadRequest, changedLimit.StatusCode);
        AssertSafeProblem(await ReadAsync<EtlApiProblem>(changedLimit), HttpStatusCode.BadRequest);

        using var crossTenant = await host.SendAsync(HttpMethod.Get, $"/etl/definitions?limit=1&cursor={Uri.EscapeDataString(cursor)}",
            tenant: "tenant-b", partition: "partition-a", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.BadRequest, crossTenant.StatusCode);

        using var crossPartition = await host.SendAsync(HttpMethod.Get, $"/etl/definitions?limit=1&cursor={Uri.EscapeDataString(cursor)}",
            tenant: "tenant-a", partition: "partition-b", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.BadRequest, crossPartition.StatusCode);

        using var tampered = await host.SendAsync(HttpMethod.Get,
            $"/etl/definitions?limit=1&cursor={Uri.EscapeDataString(Tamper(cursor))}", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
    }

    [Fact]
    public async Task Version_cursor_keeps_its_initial_high_water_mark_when_a_new_publication_arrives()
    {
        await using var host = await EtlDesignApiTestHost.StartAsync();
        await CreateDefinitionAsync(host, "orders");
        await PublishVersionAsync(host, "orders", expectedRevision: 1);
        await UpdateAndPublishVersionAsync(host, "orders", EtlTestPipelines.Create("name-v2"));

        using var firstResponse = await host.SendAsync(HttpMethod.Get, "/etl/definitions/orders/versions?limit=1", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = await ReadAsync<EtlPage<EtlPublishedVersionView>>(firstResponse);
        Assert.Equal(1, Assert.Single(first.Items).DefinitionVersion);
        Assert.False(string.IsNullOrWhiteSpace(first.NextCursor));
        var cursor = first.NextCursor!;

        // This publication arrives after the first page fixed its throughVersion ceiling at 2.
        await UpdateAndPublishVersionAsync(host, "orders", EtlTestPipelines.Create("name-v3"));

        using var nextResponse = await host.SendAsync(HttpMethod.Get,
            $"/etl/definitions/orders/versions?limit=1&cursor={Uri.EscapeDataString(cursor)}", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);
        var second = await ReadAsync<EtlPage<EtlPublishedVersionView>>(nextResponse);
        Assert.Equal(2, Assert.Single(second.Items).DefinitionVersion);
        Assert.Null(second.NextCursor);

        using var changedLimit = await host.SendAsync(HttpMethod.Get,
            $"/etl/definitions/orders/versions?limit=2&cursor={Uri.EscapeDataString(cursor)}", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.BadRequest, changedLimit.StatusCode);
        using var crossScope = await host.SendAsync(HttpMethod.Get,
            $"/etl/definitions/orders/versions?limit=1&cursor={Uri.EscapeDataString(cursor)}", tenant: "tenant-b", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.BadRequest, crossScope.StatusCode);
        using var tampered = await host.SendAsync(HttpMethod.Get,
            $"/etl/definitions/orders/versions?limit=1&cursor={Uri.EscapeDataString(Tamper(cursor))}", permissions: DefinitionRead);
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);

        using var freshPage = await host.SendAsync(HttpMethod.Get, "/etl/definitions/orders/versions?limit=3", permissions: DefinitionRead);
        var allVersions = await ReadAsync<EtlPage<EtlPublishedVersionView>>(freshPage);
        Assert.Equal(new[] { 1, 2, 3 }, allVersions.Items.Select(item => item.DefinitionVersion));
    }

    [Fact]
    public async Task Binding_catalog_returns_only_safe_scoped_alias_roles_and_distinguishes_missing_from_empty()
    {
        await using (var host = await EtlDesignApiTestHost.StartAsync())
        {
            var pages = new List<EtlBindingSummary>();
            string? cursor = null;
            do
            {
                var path = cursor is null
                    ? "/etl/bindings?limit=1"
                    : $"/etl/bindings?limit=1&cursor={Uri.EscapeDataString(cursor)}";
                using var response = await host.SendAsync(HttpMethod.Get, path, permissions: [EtlDesignPermissions.ReadBindings]);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var page = await ReadAsync<EtlPage<EtlBindingSummary>>(response);
                pages.AddRange(page.Items);
                cursor = page.NextCursor;
            } while (cursor is not null);

            Assert.Equal(new[] { "errors", "lookup", "output", "source", "source-extra" }, pages.Select(item => item.Alias));
            Assert.Contains(pages, item => item.Role == "Source");
            Assert.Contains(pages, item => item.Role == "Lookup");
            Assert.Contains(pages, item => item.Role == "MainOutput");
            Assert.Contains(pages, item => item.Role == "ErrorOutput");
            var serialized = JsonSerializer.Serialize(pages, EtlDesignApiTestHost.JsonOptions);
            Assert.DoesNotContain("source.csv", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(new string('a', 64), serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(new string('b', 64), serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("revision", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("handle", serialized, StringComparison.OrdinalIgnoreCase);
        }

        await using (var emptyHost = await EtlDesignApiTestHost.StartAsync(bindings: Array.Empty<HostFileBinding>()))
        {
            using var emptyResponse = await emptyHost.SendAsync(HttpMethod.Get, "/etl/bindings", permissions: [EtlDesignPermissions.ReadBindings]);
            Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
            Assert.Empty((await ReadAsync<EtlPage<EtlBindingSummary>>(emptyResponse)).Items);
        }

        await using (var unavailableHost = await EtlDesignApiTestHost.StartAsync(registerBindingCatalog: false))
        {
            using var unavailableResponse = await unavailableHost.SendAsync(HttpMethod.Get, "/etl/bindings", permissions: [EtlDesignPermissions.ReadBindings]);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailableResponse.StatusCode);
            AssertSafeProblem(await ReadAsync<EtlApiProblem>(unavailableResponse), HttpStatusCode.ServiceUnavailable);
        }
    }

    [Fact]
    public async Task Malformed_json_is_rejected_without_creating_a_definition()
    {
        await using var host = await EtlDesignApiTestHost.StartAsync();
        using var malformed = await host.SendRawAsync(HttpMethod.Post, "/etl/definitions", "{", permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        var oversizedBody = "{\"padding\":\"" + new string('x', 270 * 1024) + "\"}";
        using var oversized = await host.SendRawAsync(HttpMethod.Post, "/etl/definitions", oversizedBody, permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        AssertSafeProblem(await ReadAsync<EtlApiProblem>(oversized), HttpStatusCode.RequestEntityTooLarge);

        using var oversizedChunked = await host.SendRawAsync(HttpMethod.Post, "/etl/definitions", oversizedBody,
            permissions: AuthorPermissions, chunked: true);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedChunked.StatusCode);
        AssertSafeProblem(await ReadAsync<EtlApiProblem>(oversizedChunked), HttpStatusCode.RequestEntityTooLarge);

        // Neither known-length nor chunked oversized bodies may reach the definition store.
        using var list = await host.SendAsync(HttpMethod.Get, "/etl/definitions", permissions: DefinitionRead);
        Assert.Empty((await ReadAsync<EtlPage<EtlDefinitionSummaryView>>(list)).Items);
    }

    private static async Task CreateDefinitionAsync(EtlDesignApiTestHost host, string id)
    {
        using var response = await host.SendAsync(HttpMethod.Post, "/etl/definitions",
            new EtlCreateDefinitionRequest(id, EtlTestPipelines.Create()), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<EtlValidationView> ValidateAsync(EtlDesignApiTestHost host, string id, long expectedRevision)
    {
        using var response = await host.SendAsync(HttpMethod.Post, $"/etl/definitions/{Uri.EscapeDataString(id)}/validate",
            new EtlValidateRequest(id, expectedRevision), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var validation = await ReadAsync<EtlValidationView>(response);
        Assert.Empty(validation.Diagnostics);
        Assert.NotNull(validation.ContentHash);
        return validation;
    }

    private static async Task PublishVersionAsync(EtlDesignApiTestHost host, string id, long expectedRevision)
    {
        using var validateResponse = await host.SendAsync(HttpMethod.Post, $"/etl/definitions/{Uri.EscapeDataString(id)}/validate",
            new EtlValidateRequest(id, expectedRevision), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);
        var validation = await ReadAsync<EtlValidationView>(validateResponse);
        Assert.Empty(validation.Diagnostics);
        Assert.NotNull(validation.ContentHash);

        using var publishResponse = await host.SendAsync(HttpMethod.Post, $"/etl/definitions/{Uri.EscapeDataString(id)}/publish",
            new EtlPublishRequest(id, validation.Revision, validation.DefinitionVersion, validation.ContentHash!), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        Assert.Equal("published", (await ReadAsync<EtlPublicationView>(publishResponse)).Status);
    }

    private static async Task UpdateAndPublishVersionAsync(EtlDesignApiTestHost host, string id, EtlPipeline pipeline)
    {
        using var getResponse = await host.SendAsync(HttpMethod.Get, $"/etl/definitions/{Uri.EscapeDataString(id)}", permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var draft = await ReadAsync<EtlDraftView>(getResponse);
        using var updateResponse = await host.SendAsync(HttpMethod.Put, $"/etl/definitions/{Uri.EscapeDataString(id)}/draft",
            new EtlUpdateDraftRequest(id, draft.Revision, pipeline), permissions: AuthorPermissions);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await ReadAsync<EtlDraftView>(updateResponse);
        await PublishVersionAsync(host, id, updated.Revision);
    }

    private static string Tamper(string cursor)
    {
        var chars = cursor.ToCharArray();
        var index = chars.Length / 2;
        chars[index] = chars[index] == 'A' ? 'B' : 'A';
        return new string(chars);
    }

    private static void AssertSafeProblem(EtlApiProblem problem, HttpStatusCode status)
    {
        Assert.Equal((int)status, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Code));
        Assert.DoesNotContain("tenant", problem.Code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("partition", problem.Code, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>(EtlDesignApiTestHost.JsonOptions));
}
