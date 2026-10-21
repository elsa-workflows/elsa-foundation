using System.Net;
using System.Text.Json;
using CShells.Features;
using Elsa.Api.Capabilities;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Api;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Persistence.EntityFramework;
using Elsa.Tasks;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed class EtlRuntimeApiHttpTests
{
    private static readonly string[] ReadPermissions = [EtlRuntimePermissions.ReadRuns];
    private static readonly string[] CancelPermissions = [EtlRuntimePermissions.CancelRuns];

    [Fact]
    public async Task Runtime_run_paging_preserves_encoded_identity_order_across_small_and_large_pages()
    {
        await using var host = await EtlDesignApiTestHost.StartRuntimeAsync(maximumPageSize: 2);
        var ids = new[] { "run-\u00ff", "run-\u0100" };
        foreach (var id in ids)
            await SeedRunningAsync(host, id, new EtlScope("tenant-a", "partition-a"));

        var expectedOrder = ids.OrderBy(EfRelationalIdentity.Encode, StringComparer.Ordinal).ToArray();
        Assert.False(ids.Order(StringComparer.Ordinal).SequenceEqual(expectedOrder));

        var pagedOrder = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            Assert.True(++pages <= ids.Length + 1, "Run pagination did not terminate within the seeded row bound.");
            var path = cursor is null
                ? "/etl/runs?limit=1"
                : $"/etl/runs?limit=1&cursor={Uri.EscapeDataString(cursor)}";
            using var response = await host.SendAsync(HttpMethod.Get, path, permissions: ReadPermissions);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = await ReadJsonAsync(response);
            pagedOrder.AddRange(document.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("runId").GetString()!));
            cursor = OptionalString(document.RootElement, "nextCursor");
        } while (cursor is not null);

        Assert.Equal(expectedOrder, pagedOrder);

        using var aggregate = await host.SendAsync(HttpMethod.Get, "/etl/runs?limit=200", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, aggregate.StatusCode);
        using var aggregateDocument = await ReadJsonAsync(aggregate);
        Assert.Equal(expectedOrder, aggregateDocument.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("runId").GetString()).ToArray());
        Assert.Null(OptionalString(aggregateDocument.RootElement, "nextCursor"));
    }

    [Fact]
    public async Task Runtime_only_http_api_pages_scoped_run_and_partition_views_without_exposing_runtime_identity()
    {
        await using var host = await EtlDesignApiTestHost.StartRuntimeAsync(maximumPageSize: 2);
        Assert.NotNull(host.Host.Services.GetRequiredService<IEtlRuntimeStore>());
        Assert.Null(host.Host.Services.GetService<IEtlArtifactCompiler>());
        Assert.Null(host.Host.Services.GetService<IEtlDefinitionStore>());
        Assert.Null(host.Host.Services.GetService<EtlRuntimeWorker>());
        Assert.Null(host.Host.Services.GetService<IBackgroundTask>());
        Assert.Null(host.Host.Services.GetService<Elsa.Etl.Runtime.Preparation.EtlFilePreparer>());

        foreach (var id in new[] { "run-alpha", "run-bravo", "run-charlie" })
            await SeedRunningAsync(host, id, new EtlScope("tenant-a", "partition-a"));
        await SeedRunningAsync(host, "run-foreign-tenant", new EtlScope("tenant-b", "partition-a"));
        await SeedRunningAsync(host, "run-foreign-partition", new EtlScope("tenant-a", "partition-b"));
        await ClaimAndReserveFirstBatchAsync(host, CreateAdmission("run-alpha", new EtlScope("tenant-a", "partition-a")));

        using var firstRuns = await host.SendAsync(HttpMethod.Get, "/etl/runs?limit=1", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, firstRuns.StatusCode);
        using var firstRunDocument = await ReadJsonAsync(firstRuns);
        var firstRunItems = firstRunDocument.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Single(firstRunItems);
        var runCursor = RequiredString(firstRunDocument.RootElement, "nextCursor");

        var listedRuns = new List<string>();
        string? cursor = null;
        var pageCount = 0;
        do
        {
            Assert.True(++pageCount <= 4, "Run pagination did not terminate within the seeded row bound.");
            var path = cursor is null
                ? "/etl/runs?limit=1"
                : $"/etl/runs?limit=1&cursor={Uri.EscapeDataString(cursor)}";
            using var response = await host.SendAsync(HttpMethod.Get, path, permissions: ReadPermissions);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = await ReadJsonAsync(response);
            var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
            foreach (var item in items)
                AssertSafeRunView(item);
            listedRuns.AddRange(items.Select(item => item.GetProperty("runId").GetString()!));
            cursor = OptionalString(document.RootElement, "nextCursor");
        } while (cursor is not null);
        Assert.Equal(new[] { "run-alpha", "run-bravo", "run-charlie" }, listedRuns.Order(StringComparer.Ordinal));

        using var largeRunPage = await host.SendAsync(HttpMethod.Get, "/etl/runs?limit=200", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, largeRunPage.StatusCode);
        using var largeRunPageDocument = await ReadJsonAsync(largeRunPage);
        Assert.Equal(new[] { "run-alpha", "run-bravo", "run-charlie" }, largeRunPageDocument.RootElement.GetProperty("items")
            .EnumerateArray().Select(item => item.GetProperty("runId").GetString()).ToArray());
        Assert.Null(OptionalString(largeRunPageDocument.RootElement, "nextCursor"));

        using var changedLimit = await host.SendAsync(HttpMethod.Get,
            $"/etl/runs?limit=2&cursor={Uri.EscapeDataString(runCursor)}", permissions: ReadPermissions);
        await AssertProblemAsync(changedLimit, HttpStatusCode.BadRequest, "etl.cursor-invalid");
        using var crossTenantCursor = await host.SendAsync(HttpMethod.Get,
            $"/etl/runs?limit=1&cursor={Uri.EscapeDataString(runCursor)}", tenant: "tenant-b", permissions: ReadPermissions);
        await AssertProblemAsync(crossTenantCursor, HttpStatusCode.BadRequest, "etl.cursor-invalid");
        using var crossPartitionCursor = await host.SendAsync(HttpMethod.Get,
            $"/etl/runs?limit=1&cursor={Uri.EscapeDataString(runCursor)}", partition: "partition-b", permissions: ReadPermissions);
        await AssertProblemAsync(crossPartitionCursor, HttpStatusCode.BadRequest, "etl.cursor-invalid");
        using var tamperedCursor = await host.SendAsync(HttpMethod.Get,
            $"/etl/runs?limit=1&cursor={Uri.EscapeDataString(Tamper(runCursor))}", permissions: ReadPermissions);
        await AssertProblemAsync(tamperedCursor, HttpStatusCode.BadRequest, "etl.cursor-invalid");

        using var detail = await host.SendAsync(HttpMethod.Get, "/etl/runs/run-alpha", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailDocument = await ReadJsonAsync(detail);
        Assert.Equal("run-alpha", detailDocument.RootElement.GetProperty("runId").GetString());
        Assert.Equal("running", detailDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal(4L, detailDocument.RootElement.GetProperty("sourceRecordCount").GetInt64());
        Assert.Equal("source", detailDocument.RootElement.GetProperty("bindingAliases").GetProperty("source").GetString());
        AssertSafeRunView(detailDocument.RootElement);

        using var firstPartitions = await host.SendAsync(HttpMethod.Get, "/etl/runs/run-alpha/partitions?limit=1", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, firstPartitions.StatusCode);
        using var firstPartitionDocument = await ReadJsonAsync(firstPartitions);
        var firstPartition = Assert.Single(firstPartitionDocument.RootElement.GetProperty("items").EnumerateArray());
        AssertSafeRunView(firstPartition);
        Assert.Equal(0, firstPartition.GetProperty("partitionOrdinal").GetInt32());
        Assert.True(firstPartition.GetProperty("isClaimed").GetBoolean());
        Assert.True(firstPartition.GetProperty("claimFence").GetInt64() > 0);
        Assert.True(firstPartition.GetProperty("claimExpiresAt").ValueKind != JsonValueKind.Null);
        var owedBatch = firstPartition.GetProperty("owedBatch");
        Assert.Equal(32, owedBatch.GetProperty("inputBytes").GetInt64());
        var partitionCursor = RequiredString(firstPartitionDocument.RootElement, "nextCursor");

        using var secondPartitions = await host.SendAsync(HttpMethod.Get,
            $"/etl/runs/run-alpha/partitions?limit=1&cursor={Uri.EscapeDataString(partitionCursor)}", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, secondPartitions.StatusCode);
        using var secondPartitionDocument = await ReadJsonAsync(secondPartitions);
        var secondPartition = Assert.Single(secondPartitionDocument.RootElement.GetProperty("items").EnumerateArray());
        AssertSafeRunView(secondPartition);
        Assert.Equal(1, secondPartition.GetProperty("partitionOrdinal").GetInt32());
        Assert.False(secondPartition.GetProperty("isClaimed").GetBoolean());
        Assert.Null(OptionalString(secondPartitionDocument.RootElement, "nextCursor"));

        using var largePartitionPage = await host.SendAsync(HttpMethod.Get,
            "/etl/runs/run-alpha/partitions?limit=200", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, largePartitionPage.StatusCode);
        using var largePartitionPageDocument = await ReadJsonAsync(largePartitionPage);
        Assert.Equal(new[] { 0, 1 }, largePartitionPageDocument.RootElement.GetProperty("items")
            .EnumerateArray().Select(item => item.GetProperty("partitionOrdinal").GetInt32()).ToArray());
        Assert.Null(OptionalString(largePartitionPageDocument.RootElement, "nextCursor"));

        using var wrongRunCursor = await host.SendAsync(HttpMethod.Get,
            $"/etl/runs/run-bravo/partitions?limit=1&cursor={Uri.EscapeDataString(partitionCursor)}", permissions: ReadPermissions);
        await AssertProblemAsync(wrongRunCursor, HttpStatusCode.BadRequest, "etl.cursor-invalid");
        using var wrongPartitionScope = await host.SendAsync(HttpMethod.Get,
            $"/etl/runs/run-alpha/partitions?limit=1&cursor={Uri.EscapeDataString(partitionCursor)}",
            tenant: "tenant-a", partition: "partition-b", permissions: ReadPermissions);
        await AssertProblemAsync(wrongPartitionScope, HttpStatusCode.BadRequest, "etl.cursor-invalid");
        using var wrongPageSize = await host.SendAsync(HttpMethod.Get,
            $"/etl/runs/run-alpha/partitions?limit=2&cursor={Uri.EscapeDataString(partitionCursor)}", permissions: ReadPermissions);
        await AssertProblemAsync(wrongPageSize, HttpStatusCode.BadRequest, "etl.cursor-invalid");
        using var tamperedPartitionCursor = await host.SendAsync(HttpMethod.Get,
            $"/etl/runs/run-alpha/partitions?limit=1&cursor={Uri.EscapeDataString(Tamper(partitionCursor))}", permissions: ReadPermissions);
        await AssertProblemAsync(tamperedPartitionCursor, HttpStatusCode.BadRequest, "etl.cursor-invalid");

        foreach (var (tenant, partition) in new[] { ("tenant-b", "partition-a"), ("tenant-a", "partition-b") })
        {
            using var hidden = await host.SendAsync(HttpMethod.Get, "/etl/runs/run-alpha", tenant: tenant, partition: partition, permissions: ReadPermissions);
            await AssertProblemAsync(hidden, HttpStatusCode.NotFound, "etl.run-not-found");
            var body = await hidden.Content.ReadAsStringAsync();
            Assert.DoesNotContain("run-alpha", body, StringComparison.Ordinal);
            Assert.DoesNotContain("tenant-a", body, StringComparison.Ordinal);
            Assert.DoesNotContain("partition-a", body, StringComparison.Ordinal);
        }

        using var missing = await host.SendAsync(HttpMethod.Get, "/etl/runs/not-found", permissions: ReadPermissions);
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "etl.run-not-found");

        using var tenantForeignList = await host.SendAsync(HttpMethod.Get, "/etl/runs", tenant: "tenant-b", permissions: ReadPermissions);
        using var tenantForeignDocument = await ReadJsonAsync(tenantForeignList);
        Assert.Equal(new[] { "run-foreign-tenant" }, tenantForeignDocument.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("runId").GetString()).ToArray());
        using var partitionForeignList = await host.SendAsync(HttpMethod.Get, "/etl/runs", partition: "partition-b", permissions: ReadPermissions);
        using var partitionForeignDocument = await ReadJsonAsync(partitionForeignList);
        Assert.Equal(new[] { "run-foreign-partition" }, partitionForeignDocument.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("runId").GetString()).ToArray());
    }

    [Fact]
    public void Runtime_api_and_ef_store_shell_closure_does_not_activate_etl_execution_tasks_or_design()
    {
        var assemblies = new[]
        {
            typeof(EtlRuntimeApiFeature).Assembly,
            typeof(EtlRuntimeEntityFrameworkCoreFeature).Assembly,
            typeof(EtlRuntimeFeature).Assembly,
            typeof(EtlDesignFeature).Assembly,
            typeof(EtlDesignEntityFrameworkCoreFeature).Assembly,
            typeof(ApiCapabilitiesFeature).Assembly,
            typeof(TasksFeature).Assembly
        }.Distinct();
        var features = FeatureDiscovery.DiscoverFeatures(assemblies)
            .ToDictionary(feature => feature.Id, StringComparer.OrdinalIgnoreCase);

        var closure = new FeatureDependencyResolver().GetOrderedFeatures(
            ["EtlRuntimeApi", "EtlRuntimeEntityFrameworkCore"], features);

        Assert.Contains("EtlRuntimeApi", closure, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("EtlRuntimeEntityFrameworkCore", closure, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("EtlRuntime", closure, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tasks", closure, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("EtlDesign", closure, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("EtlDesignEntityFrameworkCore", closure, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runtime_http_cancel_requires_its_permission_uses_persisted_invocation_and_preserves_terminal_success()
    {
        await using var host = await EtlDesignApiTestHost.StartRuntimeAsync();
        var invocation = await SeedRunningAsync(host, "run-cancel", new EtlScope("tenant-a", "partition-a"));
        var before = await FindRunAsync(host, invocation.Scope, invocation.RunId);
        Assert.Equal(invocation.ResultAddress, before.Invocation.ResultAddress);

        foreach (var (tenant, partition) in new[] { ("tenant-b", "partition-a"), ("tenant-a", "partition-b") })
        {
            using var crossScopeCancel = await host.SendAsync(HttpMethod.Post, $"/etl/runs/{invocation.RunId}/cancel",
                tenant: tenant, partition: partition, permissions: CancelPermissions);
            await AssertProblemAsync(crossScopeCancel, HttpStatusCode.NotFound, "etl.run-not-found");
            Assert.Null(await FindRunOrNullAsync(host, new EtlScope(tenant, partition), invocation.RunId));
        }
        Assert.Equal(EtlRunStatus.Running, (await FindRunAsync(host, invocation.Scope, invocation.RunId)).Status);

        using var denied = await host.SendAsync(HttpMethod.Post, $"/etl/runs/{invocation.RunId}/cancel",
            new { scope = new { tenantId = "tenant-b", persistencePartition = "partition-b" }, invocation = new { runId = "spoofed" } },
            permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(EtlRunStatus.Running, (await FindRunAsync(host, invocation.Scope, invocation.RunId)).Status);

        var oversizedBody = "{\"spoof\":\"" + new string('x', 5_000) + "\"}";
        using var oversizedKnownLength = await host.SendRawAsync(HttpMethod.Post, $"/etl/runs/{invocation.RunId}/cancel",
            oversizedBody, permissions: CancelPermissions);
        await AssertProblemAsync(oversizedKnownLength, HttpStatusCode.RequestEntityTooLarge, "etl.request-too-large");
        Assert.Equal(EtlRunStatus.Running, (await FindRunAsync(host, invocation.Scope, invocation.RunId)).Status);

        using var oversizedChunked = await host.SendRawAsync(HttpMethod.Post, $"/etl/runs/{invocation.RunId}/cancel",
            oversizedBody, permissions: CancelPermissions, chunked: true);
        await AssertProblemAsync(oversizedChunked, HttpStatusCode.RequestEntityTooLarge, "etl.request-too-large");
        Assert.Equal(EtlRunStatus.Running, (await FindRunAsync(host, invocation.Scope, invocation.RunId)).Status);

        using var canceled = await host.SendAsync(HttpMethod.Post, $"/etl/runs/{invocation.RunId}/cancel",
            new { scope = new { tenantId = "tenant-b", persistencePartition = "partition-b" }, invocation = new { runId = "spoofed" } },
            permissions: CancelPermissions);
        Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);
        using var canceledDocument = await ReadJsonAsync(canceled);
        Assert.True(canceledDocument.RootElement.GetProperty("cancellationAccepted").GetBoolean());
        Assert.Equal("cancelled", canceledDocument.RootElement.GetProperty("terminalResult").GetProperty("outcome").GetString());
        AssertSafeRunView(canceledDocument.RootElement);

        using var replay = await host.SendAsync(HttpMethod.Post, $"/etl/runs/{invocation.RunId}/cancel", permissions: CancelPermissions);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var replayDocument = await ReadJsonAsync(replay);
        Assert.True(replayDocument.RootElement.GetProperty("cancellationAccepted").GetBoolean());
        Assert.Equal("cancelled", replayDocument.RootElement.GetProperty("terminalResult").GetProperty("outcome").GetString());
        Assert.Equal(canceledDocument.RootElement.GetProperty("terminalResult").GetRawText(),
            replayDocument.RootElement.GetProperty("terminalResult").GetRawText());

        var after = await FindRunAsync(host, invocation.Scope, invocation.RunId);
        Assert.Equal(invocation.ResultAddress, after.Invocation.ResultAddress);
        Assert.Equal(EtlRunStatus.Cancelled, after.Status);

        using var missingCancel = await host.SendAsync(HttpMethod.Post, "/etl/runs/not-found/cancel", permissions: CancelPermissions);
        await AssertProblemAsync(missingCancel, HttpStatusCode.NotFound, "etl.run-not-found");
        Assert.Null(await FindRunOrNullAsync(host, invocation.Scope, "not-found"));

        var successfulInvocation = await SeedSuccessfulAsync(host, "run-success", new EtlScope("tenant-a", "partition-a"));
        using var completed = await host.SendAsync(HttpMethod.Post, $"/etl/runs/{successfulInvocation.RunId}/cancel", permissions: CancelPermissions);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        using var completedDocument = await ReadJsonAsync(completed);
        Assert.False(completedDocument.RootElement.GetProperty("cancellationAccepted").GetBoolean());
        Assert.Equal("succeeded", completedDocument.RootElement.GetProperty("terminalResult").GetProperty("outcome").GetString());
        Assert.Equal(EtlRunStatus.Succeeded, (await FindRunAsync(host, successfulInvocation.Scope, successfulInvocation.RunId)).Status);

        var businessErrorInvocation = await SeedBusinessErrorAsync(host, "run-business-error", new EtlScope("tenant-a", "partition-a"));
        using var businessErrorDetail = await host.SendAsync(HttpMethod.Get, $"/etl/runs/{businessErrorInvocation.RunId}", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, businessErrorDetail.StatusCode);
        using var businessErrorDetailDocument = await ReadJsonAsync(businessErrorDetail);
        Assert.Equal("businessError", businessErrorDetailDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal("businessError", businessErrorDetailDocument.RootElement.GetProperty("terminalResult").GetProperty("outcome").GetString());
        using var businessErrorCancel = await host.SendAsync(HttpMethod.Post,
            $"/etl/runs/{businessErrorInvocation.RunId}/cancel", permissions: CancelPermissions);
        Assert.Equal(HttpStatusCode.OK, businessErrorCancel.StatusCode);
        using var businessErrorCancelDocument = await ReadJsonAsync(businessErrorCancel);
        Assert.False(businessErrorCancelDocument.RootElement.GetProperty("cancellationAccepted").GetBoolean());
        Assert.Equal(businessErrorDetailDocument.RootElement.GetProperty("terminalResult").GetRawText(),
            businessErrorCancelDocument.RootElement.GetProperty("terminalResult").GetRawText());
        var businessErrorRun = await FindRunAsync(host, businessErrorInvocation.Scope, businessErrorInvocation.RunId);
        Assert.Equal(EtlRunStatus.BusinessError, businessErrorRun.Status);
        Assert.Equal(EtlTerminalOutcome.BusinessError, businessErrorRun.TerminalResult?.Outcome);
    }

    [Fact]
    public async Task Runtime_http_requires_normalized_identity_and_scoped_persistence_and_reports_missing_provider_distinct_from_empty()
    {
        await using (var host = await EtlDesignApiTestHost.StartRuntimeAsync())
        {
            using var empty = await host.SendAsync(HttpMethod.Get, "/etl/runs", permissions: ReadPermissions);
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            using var emptyDocument = await ReadJsonAsync(empty);
            Assert.Empty(emptyDocument.RootElement.GetProperty("items").EnumerateArray());

            foreach (var invalidContext in new[]
                     {
                         new { Normalized = false, DuplicateTenant = false, PersistenceContext = "scoped" },
                         new { Normalized = true, DuplicateTenant = true, PersistenceContext = "scoped" },
                         new { Normalized = true, DuplicateTenant = false, PersistenceContext = "global" },
                         new { Normalized = true, DuplicateTenant = false, PersistenceContext = "privileged-global" }
                     })
            {
                using var denied = await host.SendAsync(HttpMethod.Get, "/etl/runs", permissions: ReadPermissions,
                    normalized: invalidContext.Normalized, duplicateTenant: invalidContext.DuplicateTenant,
                    persistenceContext: invalidContext.PersistenceContext);
                if (!invalidContext.Normalized)
                    Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
                else
                    await AssertProblemAsync(denied, HttpStatusCode.Forbidden, "etl.scope-unavailable");
            }

            using var unauthorized = await host.SendAsync(HttpMethod.Get, "/etl/runs", authenticated: false);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        }

        await using (var unavailableHost = await EtlDesignApiTestHost.StartRuntimeAsync(registerRuntimePersistence: false))
        {
            using var unavailable = await unavailableHost.SendAsync(HttpMethod.Get, "/etl/runs", permissions: ReadPermissions);
            await AssertProblemAsync(unavailable, HttpStatusCode.ServiceUnavailable, "etl.provider-unavailable");
        }
    }

    [Fact]
    public async Task Runtime_detail_retains_distinct_input_identities_before_and_after_preparation()
    {
        await using var host = await EtlDesignApiTestHost.StartRuntimeAsync();
        var scope = new EtlScope("tenant-a", "partition-a");
        var admission = CreateAdmission("input-identities", scope);
        var lookupHash = new string('b', 64);
        admission = admission with { Bindings = admission.Bindings with
        {
            Source = admission.Bindings.Source with { Revision = "source-v7" },
            Lookup = admission.Bindings.Lookup with { Revision = "lookup-v3", ExpectedSha256 = lookupHash }
        } };
        await using var services = host.Host.Services.CreateAsyncScope();
        var store = services.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitAsync(admission)).Status);

        using (var response = await host.SendAsync(HttpMethod.Get, "/etl/runs/input-identities", permissions: ReadPermissions))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = await ReadJsonAsync(response);
            AssertInputIdentity(document.RootElement.GetProperty("sourceInput"), "source-v7", EtlRuntimeFixtures.Hash, null);
            AssertInputIdentity(document.RootElement.GetProperty("lookupInput"), "lookup-v3", lookupHash, null);
            AssertSafeRunView(document.RootElement);
        }

        var lease = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(scope, "prepare-identities"));
        var prepared = EtlRuntimeFixtures.Prepared();
        prepared = prepared with { Lookup = prepared.Lookup with { ContentHash = lookupHash } };
        Assert.Equal(EtlStoreStatus.Applied, await store.CommitPreparationAsync(lease, prepared));
        using var completed = await host.SendAsync(HttpMethod.Get, "/etl/runs/input-identities", permissions: ReadPermissions);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        using var completedDocument = await ReadJsonAsync(completed);
        AssertInputIdentity(completedDocument.RootElement.GetProperty("sourceInput"), "source-v7", EtlRuntimeFixtures.Hash, 128);
        AssertInputIdentity(completedDocument.RootElement.GetProperty("lookupInput"), "lookup-v3", lookupHash, 64);
        AssertSafeRunView(completedDocument.RootElement);

        // Even useful identity metadata remains unavailable across the tenant boundary.
        using var foreign = await host.SendAsync(HttpMethod.Get, "/etl/runs/input-identities", tenant: "tenant-b", permissions: ReadPermissions);
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "etl.run-not-found");
    }

    private static void AssertInputIdentity(JsonElement input, string revision, string hash, long? length)
    {
        Assert.Equal(revision, input.GetProperty("bindingRevision").GetString());
        Assert.Equal(hash, input.GetProperty("expectedSha256").GetString());
        Assert.Equal(length.HasValue ? hash : null, input.GetProperty("snapshotSha256").GetString());
        var bytes = input.GetProperty("snapshotByteLength");
        Assert.Equal(length, bytes.ValueKind == JsonValueKind.Null ? (long?)null : bytes.GetInt64());
    }

    private static async Task<EtlInvocation> SeedRunningAsync(EtlDesignApiTestHost host, string runId, EtlScope scope)
    {
        var admission = CreateAdmission(runId, scope);
        await using var services = host.Host.Services.CreateAsyncScope();
        var store = services.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitAsync(admission)).Status);
        var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(scope, $"seed-{runId}"));
        Assert.Equal(EtlStoreStatus.Applied, await store.CommitPreparationAsync(preparation, EtlRuntimeFixtures.Prepared(4)));
        return admission.Invocation;
    }

    private static async Task<EtlInvocation> SeedSuccessfulAsync(EtlDesignApiTestHost host, string runId, EtlScope scope)
    {
        var admission = CreateAdmission(runId, scope);
        await using var services = host.Host.Services.CreateAsyncScope();
        var store = services.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitAsync(admission)).Status);
        var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(scope, $"seed-{runId}"));
        Assert.Equal(EtlStoreStatus.Applied, await store.CommitPreparationAsync(preparation, EtlRuntimeFixtures.Prepared(0)));
        var completed = Assert.IsType<EtlRunView>(await store.FindRunAsync(scope, runId));
        Assert.Equal(EtlRunStatus.Succeeded, completed.Status);
        return admission.Invocation;
    }

    private static async Task<EtlInvocation> SeedBusinessErrorAsync(EtlDesignApiTestHost host, string runId, EtlScope scope)
    {
        var admission = CreateAdmission(runId, scope);
        await using var services = host.Host.Services.CreateAsyncScope();
        var store = services.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        Assert.Equal(EtlStoreStatus.Applied, (await store.RejectAdmissionAsync(admission.Invocation,
            new EtlRunDiagnostic("source.schema-invalid", RecordOrdinal: 0, Field: "id"))).Status);
        var run = Assert.IsType<EtlRunView>(await store.FindRunAsync(scope, runId));
        Assert.Equal(EtlRunStatus.BusinessError, run.Status);
        return admission.Invocation;
    }

    private static async Task ClaimAndReserveFirstBatchAsync(EtlDesignApiTestHost host, EtlRunAdmission admission)
    {
        await using var services = host.Host.Services.CreateAsyncScope();
        var store = services.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        var run = Assert.IsType<EtlRunView>(await store.FindRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        var prepared = Assert.IsType<EtlPreparedRun>(run.Prepared);
        var lease = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "http-api-test-worker"));
        var start = lease.StartOrdinal;
        var end = start + 1;
        const long inputBytes = 32;
        var inputHash = new string('a', 64);
        var batchId = EtlRuntimeIdentity.BatchId(admission, prepared, lease.PartitionOrdinal, start, end, inputBytes, inputHash);
        var batch = new EtlBatchDescriptor(batchId, start, end, inputBytes, inputHash);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveBatchAsync(lease, batch)).Status);
    }

    private static EtlRunAdmission CreateAdmission(string runId, EtlScope scope)
    {
        var admission = EtlRuntimeFixtures.Admission(runId, scope);
        var address = admission.Invocation.ResultAddress with
        {
            WorkflowExecutionId = $"workflow-{runId}",
            ActivityExecutionId = $"activity-{runId}",
            NodeId = $"node-{runId}",
            BookmarkId = $"bookmark-{runId}",
            RegistrationId = $"registration-{runId}",
            ResumeTargetId = $"target-{runId}"
        };
        return admission with { Invocation = admission.Invocation with { ResultAddress = address } };
    }

    private static async Task<EtlRunView> FindRunAsync(EtlDesignApiTestHost host, EtlScope scope, string runId)
    {
        await using var services = host.Host.Services.CreateAsyncScope();
        return Assert.IsType<EtlRunView>(await services.ServiceProvider.GetRequiredService<IEtlRuntimeStore>().FindRunAsync(scope, runId));
    }

    private static async Task<EtlRunView?> FindRunOrNullAsync(EtlDesignApiTestHost host, EtlScope scope, string runId)
    {
        await using var services = host.Host.Services.CreateAsyncScope();
        return await services.ServiceProvider.GetRequiredService<IEtlRuntimeStore>().FindRunAsync(scope, runId);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw new InvalidOperationException($"Expected '{name}' to be a string.");

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static string Tamper(string cursor)
    {
        var chars = cursor.ToCharArray();
        var index = chars.Length / 2;
        chars[index] = chars[index] == 'A' ? 'B' : 'A';
        return new string(chars);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal((int)status, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
    }

    private static void AssertSafeRunView(JsonElement element)
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "address", "resultAddress", "bookmarkId", "registrationId", "resumeTargetId", "handle", "path",
            "key", "contentHash", "batchId", "inputHash", "payloadHash", "payload", "artifact"
        };
        AssertNoForbiddenProperties(element, forbidden);
    }

    private static void AssertNoForbiddenProperties(JsonElement element, HashSet<string> forbidden)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert.DoesNotContain(property.Name, forbidden);
                AssertNoForbiddenProperties(property.Value, forbidden);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                AssertNoForbiddenProperties(child, forbidden);
        }
    }
}
