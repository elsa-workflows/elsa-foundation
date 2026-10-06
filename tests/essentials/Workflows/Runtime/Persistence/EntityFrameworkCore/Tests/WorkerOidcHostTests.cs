using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Elsa.Api.Capabilities.Authorization;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Api.Authorization;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class WorkerOidcHostTests
{
    private const string ExecutePath = "/runtime/workflows/executables/{0}/execute";
    private const string ExecutePermission = WorkflowRuntimePermissions.WorkflowRuntimeExecute;

    [UnixPtyFact]
    public async Task Real_shell_activation_refuses_a_scope_that_disagrees_with_the_configured_tenant()
    {
        await using var fixture = await WorkerOidcHostFixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.StartHostAsync(persistenceScope: "different-static-tenant"));
        Assert.Contains("host-start-failed, activate,", exception.Message, StringComparison.Ordinal);
        // A refused child must be reaped; the same fixture can then activate its ordinary configured scope.
        var valid = await fixture.StartHostAsync();
        AssertWorkerComposition(valid.Ready.Data, fixture.PrimaryCandidate);
        await AssertNoMappingReadOrRuntimeEffectAsync(valid);
        await AssertNoUserOrExternalIdentityRowsAsync(valid);
    }

    [UnixPtyFact]
    public async Task Real_bearer_actor_executes_resumes_revokes_and_reloads_across_a_child_process_restart()
    {
        await using var fixture = await WorkerOidcHostFixture.CreateAsync();
        Assert.NotEqual(Path.GetFullPath(fixture.IamDatabasePath), Path.GetFullPath(fixture.RuntimeDatabasePath));
        var candidate = fixture.PrimaryCandidate;
        var first = await fixture.StartHostAsync(candidate);
        var ready = first.Ready.Data;

        AssertWorkerComposition(ready, candidate);
        var executable = await first.ControlAsync("seed-executable", new { prefix = "worker-oidc" });
        var artifactId = executable.GetProperty("artifactId").GetString()!;
        Assert.Equal("worker-oidc-event-artifact", artifactId);

        // These colliding grants must remain invisible to the configured tenant/provider pair.
        await SaveRuleAsync(first, "other-provider", WorkerOidcHostFixture.TenantId, "provider-other", ExecutePermission);
        await SaveRuleAsync(first, "other-tenant", "worker-tenant-other", WorkerOidcHostFixture.ProviderId, ExecutePermission);
        await SaveRuleAsync(first, "other-both", "worker-tenant-other", "provider-other", ExecutePermission);

        var token = fixture.CreateToken();
        foreach (var probe in new[] { "mismatch", "global", "privileged", "across" })
        {
            await ResetMappingReadsAsync(first);
            using var response = await PostExecuteAsync(first, artifactId, token, probe);
            await AssertNoMappingReadOrRuntimeEffectAsync(first, probe, response.StatusCode, RequestOperationId(response));
            await AssertNoUserOrExternalIdentityRowsAsync(first);
        }

        string?[] invalidTokens =
        [
            null,
            fixture.TamperSignature(token),
            fixture.CreateToken(issuer: "http://127.0.0.1:1/wrong-issuer"),
            fixture.CreateToken(audience: "different-api"),
            fixture.CreateToken(notBefore: DateTimeOffset.UtcNow.AddHours(-2), expires: DateTimeOffset.UtcNow.AddHours(-1)),
            fixture.CreateToken(notBefore: DateTimeOffset.UtcNow.AddHours(1), expires: DateTimeOffset.UtcNow.AddHours(2))
        ];

        foreach (var invalidToken in invalidTokens)
        {
            await ResetMappingReadsAsync(first);
            using var response = await PostExecuteAsync(first, artifactId, invalidToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            await AssertNoMappingReadOrRuntimeEffectAsync(first, actualStatusCode: response.StatusCode,
                operationId: RequestOperationId(response));
            await AssertNoUserOrExternalIdentityRowsAsync(first);
        }

        var forgedToken = fixture.CreateToken(claims:
        [
            new Claim("elsa.identity.normalized", "v1"),
            new Claim("elsa.identity.tenant_id", "worker-tenant-other"),
            new Claim("elsa.identity.provider", "provider-other"),
            new Claim("elsa.identity.permission", ExecutePermission),
            new Claim("elsa.identity.role", "forged-administrator")
        ]);
        await ResetMappingReadsAsync(first);
        using (var response = await PostExecuteAsync(first, artifactId, forgedToken))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, await MappingReadCountAsync(first));
        await AssertNoRuntimeRowsAsync(first);
        await AssertNoUserOrExternalIdentityRowsAsync(first);

        // A normal, valid token with no mapping also remains authenticated but has no permission.
        await ResetMappingReadsAsync(first);
        using (var response = await PostExecuteAsync(first, artifactId, token))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, await MappingReadCountAsync(first));
        await AssertNoRuntimeRowsAsync(first);

        await SaveRuleAsync(
            first,
            "worker-execute",
            WorkerOidcHostFixture.TenantId,
            WorkerOidcHostFixture.ProviderId,
            ExecutePermission);
        await ResetMappingReadsAsync(first);

        using var executeResponse = await PostExecuteAsync(first, artifactId, token);
        Assert.Equal(HttpStatusCode.OK, executeResponse.StatusCode);
        await AssertSingleHttpMappingReadAttributedToResponseAsync(first, executeResponse, "ordinary");
        var executeResult = await ReadJsonAsync(executeResponse);
        Assert.Equal("Accepted", executeResult.GetProperty("commandDispatchStatus").GetString());
        var executionId = executeResult.GetProperty("workflowExecutionId").GetString()!;

        var suspended = await SnapshotAsync(first, executionId);
        // The checkpoint retains a running workflow while its sole event activity waits on a durable bookmark.
        Assert.Equal("Running", suspended.GetProperty("workflowStatus").GetString());
        Assert.Equal(1, suspended.GetProperty("bookmarkRows").GetInt32());
        Assert.Equal("Suspended", Assert.Single(suspended.GetProperty("activityStatuses").EnumerateArray()).GetString());
        var bookmark = Assert.Single(suspended.GetProperty("bookmarks").EnumerateArray());

        using (var stimulusRequest = new HttpRequestMessage(HttpMethod.Post, "/runtime/workflows/stimuli")
        {
            Content = JsonContent.Create(new
            {
                stimulusType = bookmark.GetProperty("stimulusType").GetString(),
                stimulusHash = bookmark.GetProperty("stimulusHash").GetString(),
                input = JsonSerializer.SerializeToElement(new EventReceived("worker-oidc-ready")),
                mode = "ResumeOnly"
            })
        })
        {
            stimulusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var stimulusResponse = await first.Client.SendAsync(stimulusRequest);
            Assert.Equal(HttpStatusCode.OK, stimulusResponse.StatusCode);
            var stimulusResult = await ReadJsonAsync(stimulusResponse);
            Assert.Equal(0, stimulusResult.GetProperty("startedCount").GetInt32());
            Assert.Equal(1, stimulusResult.GetProperty("resumedCount").GetInt32());
            Assert.Equal(executionId, Assert.Single(stimulusResult.GetProperty("resumes").EnumerateArray())
                .GetProperty("workflowExecutionId").GetString());
        }

        var completed = await SnapshotAsync(first, executionId);
        Assert.Equal("Completed", completed.GetProperty("workflowStatus").GetString());
        Assert.Equal(0, completed.GetProperty("bookmarkRows").GetInt32());
        Assert.Contains("Completed", completed.GetProperty("activityStatuses").EnumerateArray()
            .Select(value => value.GetString()));
        await AssertNoUserOrExternalIdentityRowsAsync(first);

        // Persist the exact rule with no grants; the same still-valid bearer token must lose access immediately.
        await SaveRuleAsync(
            first,
            "worker-execute",
            WorkerOidcHostFixture.TenantId,
            WorkerOidcHostFixture.ProviderId,
            grants: []);
        var revokedRules = await first.ControlAsync("list-rules", new
        {
            tenantId = WorkerOidcHostFixture.TenantId,
            provider = WorkerOidcHostFixture.ProviderId
        });
        var revokedRule = Assert.Single(revokedRules.GetProperty("rules").EnumerateArray());
        Assert.Empty(revokedRule.GetProperty("grantPermissions").EnumerateArray());

        var stateRowsBeforeRevokedCall = completed.GetProperty("workflowExecutionStateRows").GetInt32();
        await ResetMappingReadsAsync(first);
        using (var response = await PostExecuteAsync(first, artifactId, token))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, await MappingReadCountAsync(first));
        var afterRevokedCall = await SnapshotAsync(first, executionId);
        Assert.Equal(stateRowsBeforeRevokedCall, afterRevokedCall.GetProperty("workflowExecutionStateRows").GetInt32());
        await AssertNoUserOrExternalIdentityRowsAsync(first);

        var firstIdentity = (first.ProcessId, first.ProcessStartUtcTicks);
        var firstArtifactSha256 = first.ArtifactSha256;
        await first.StopAsync();
        Assert.True(first.HasExited);
        Assert.Equal(0, first.ExitCode);

        // The issuer/key and exact token above stay in this parent process while the actor host is a new OS process.
        var second = await fixture.StartHostAsync(candidate);
        var secondIdentity = (second.ProcessId, second.ProcessStartUtcTicks);
        Assert.NotEqual(firstIdentity, secondIdentity);
        Assert.Equal(firstArtifactSha256, second.ArtifactSha256);
        AssertWorkerComposition(second.Ready.Data, candidate);

        var reloaded = await SnapshotAsync(second, executionId);
        Assert.Equal("Completed", reloaded.GetProperty("workflowStatus").GetString());
        Assert.Equal(stateRowsBeforeRevokedCall, reloaded.GetProperty("workflowExecutionStateRows").GetInt32());
        await AssertNoUserOrExternalIdentityRowsAsync(second);

        var reloadedRules = await second.ControlAsync("list-rules", new
        {
            tenantId = WorkerOidcHostFixture.TenantId,
            provider = WorkerOidcHostFixture.ProviderId
        });
        Assert.Empty(Assert.Single(reloadedRules.GetProperty("rules").EnumerateArray())
            .GetProperty("grantPermissions").EnumerateArray());

        await ResetMappingReadsAsync(second);
        using (var response = await PostExecuteAsync(second, artifactId, token))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, await MappingReadCountAsync(second));
        var afterRestartDeniedCall = await SnapshotAsync(second, executionId);
        Assert.Equal(stateRowsBeforeRevokedCall, afterRestartDeniedCall.GetProperty("workflowExecutionStateRows").GetInt32());
        await AssertNoUserOrExternalIdentityRowsAsync(second);
        await AssertResetSpanningControlReadAsync(second);
    }

    [UnixPtyFact]
    public async Task Edited_candidate_controls_feature_removal_and_capabilities_audience()
    {
        await using var fixture = await WorkerOidcHostFixture.CreateAsync();
        var candidate = await fixture.CreateControlCandidateAsync();
        var primary = fixture.PrimaryCandidate;
        Assert.Equal(primary.CatalogId, candidate.CatalogId);
        Assert.Equal(primary.CatalogVersion, candidate.CatalogVersion);
        Assert.Equal(primary.CatalogDigest, candidate.CatalogDigest);
        Assert.Equal(primary.ProfileId, candidate.ProfileId);
        Assert.Equal(primary.ProfileVersion, candidate.ProfileVersion);
        Assert.Equal(primary.ProfileDigest, candidate.ProfileDigest);
        Assert.Equal(18, candidate.FeatureIds.Length);
        Assert.DoesNotContain("ActivitiesControlFlow", candidate.FeatureIds);
        Assert.Contains("Events", candidate.FeatureIds);
        Assert.Contains("inventory-unverified", candidate.Findings);
        Assert.Contains("persistence-unverified", candidate.Findings);
        Assert.Contains("candidate-re-resolution", candidate.Findings);
        Assert.Equal(WorkerProfileCandidate.ControlAudience, candidate.Audience);

        var host = await fixture.StartHostAsync(candidate);
        // Let the paired real requests detect a stale Audience before checking the options receipt.
        AssertWorkerComposition(host.Ready.Data, candidate, deferAudienceCheck: true);
        await SaveRuleAsync(
            host,
            "worker-capabilities-read",
            WorkerOidcHostFixture.TenantId,
            WorkerOidcHostFixture.ProviderId,
            ApiCapabilitiesPermissions.Read);

        using var oldAudienceRequest = new HttpRequestMessage(HttpMethod.Get, "/capabilities");
        oldAudienceRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.CreateToken(audience: WorkerOidcHostFixture.Audience));
        await ResetMappingReadsAsync(host);
        using (var response = await host.Client.SendAsync(oldAudienceRequest))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            await AssertNoMappingReadOrRuntimeEffectAsync(host, actualStatusCode: response.StatusCode,
                operationId: RequestOperationId(response));
        }
        await AssertNoUserOrExternalIdentityRowsAsync(host);

        using var candidateAudienceRequest = new HttpRequestMessage(HttpMethod.Get, "/capabilities");
        candidateAudienceRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.CreateToken(audience: candidate.Audience));
        await ResetMappingReadsAsync(host);
        using (var response = await host.Client.SendAsync(candidateAudienceRequest))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await MappingReadCountAsync(host));
        await AssertNoRuntimeRowsAsync(host);
        await AssertNoUserOrExternalIdentityRowsAsync(host);
        Assert.Equal(WorkerProfileCandidate.HashAudience(candidate.Audience),
            host.Ready.Data.GetProperty("oidcAudienceSha256").GetString());
    }

    private static void AssertWorkerComposition(
        JsonElement ready,
        WorkerProfileCandidate candidate,
        bool deferAudienceCheck = false)
    {
        Assert.Equal(candidate.ShellId, ready.GetProperty("shell").GetString());
        Assert.Equal(candidate.Environment, ready.GetProperty("environment").GetString());
        Assert.True(ready.GetProperty("candidateAppsettingsOverlayLoaded").GetBoolean());
        Assert.True(ready.GetProperty("candidateShellOverlayLoaded").GetBoolean());
        var consumedHashes = ready.GetProperty("candidateFileHashes");
        Assert.Equal(candidate.ConsumedFileHashes.Keys.Order(StringComparer.Ordinal),
            consumedHashes.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        foreach (var (name, hash) in candidate.ConsumedFileHashes)
            Assert.Equal(hash, consumedHashes.GetProperty(name).GetString());
        Assert.Equal(WorkerOidcHostFixture.TenantId, ready.GetProperty("tenantId").GetString());
        Assert.Equal(WorkerOidcHostFixture.ProviderId, ready.GetProperty("providerId").GetString());
        if (!deferAudienceCheck)
            Assert.Equal(WorkerProfileCandidate.HashAudience(candidate.Audience), ready.GetProperty("oidcAudienceSha256").GetString());
        Assert.True(ready.GetProperty("oidcAuthorityConfigured").GetBoolean());
        Assert.False(ready.GetProperty("oidcClientIdConfigured").GetBoolean());
        Assert.False(ready.GetProperty("oidcRequireHttpsMetadata").GetBoolean());
        Assert.True(ready.GetProperty("normalizationEnabled").GetBoolean());
        Assert.True(ready.GetProperty("audienceConfigured").GetBoolean());
        Assert.Equal(WorkerOidcHostFixture.JwtBearerScheme, ready.GetProperty("jwtBearerScheme").GetString());
        Assert.Equal(WorkerOidcHostFixture.NormalizedAuthenticationType, ready.GetProperty("normalizedAuthenticationType").GetString());
        Assert.True(ready.GetProperty("normalizedTypeEnrolled").GetBoolean());
        Assert.False(ready.GetProperty("rawSchemeEnrolled").GetBoolean());
        Assert.Equal(WorkerOidcHostFixture.JwtBearerScheme, ready.GetProperty("defaultAuthenticateScheme").GetString());
        Assert.Equal(WorkerOidcHostFixture.JwtBearerScheme, ready.GetProperty("defaultChallengeScheme").GetString());
        Assert.Equal("Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerHandler", ready.GetProperty("bearerHandlerType").GetString());
        Assert.Equal("Elsa.Foundation.Identity.Oidc.OidcBearerNormalizationEvents", ready.GetProperty("bearerEventsType").GetString());
        Assert.False(ready.GetProperty("interactiveOidcSchemePresent").GetBoolean());
        Assert.Equal(candidate.FeatureIds, ready.GetProperty("enabledFeatures").EnumerateArray()
            .Select(value => value.GetString()!)
            .OrderBy(value => value, StringComparer.Ordinal));

        var routes = ready.GetProperty("routes").EnumerateArray()
            .Select(value => value.GetString()!.TrimStart('/'))
            .ToArray();
        Assert.Contains("runtime/workflows/executables/{artifactId}/execute", routes);
        Assert.Contains("runtime/workflows/stimuli", routes);
        Assert.Contains("capabilities", routes);
        Assert.DoesNotContain(routes, route => route.StartsWith("design/", StringComparison.Ordinal));
        Assert.DoesNotContain(routes, route => route.StartsWith("publishing/", StringComparison.Ordinal));

        Assert.Equal("Sqlite", ready.GetProperty("runtimeProvider").GetString());
        Assert.Equal("WorkerRuntime", ready.GetProperty("runtimeConnectionName").GetString());
        Assert.Contains("Sqlite", ready.GetProperty("runtimeDatabaseProvider").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(ready.GetProperty("runtimeUsesExpectedDatabase").GetBoolean());
        Assert.True(ready.GetProperty("runtimeMigrationsApplied").GetBoolean());
        Assert.False(ready.GetProperty("runtimeMigrationsPending").GetBoolean());
        Assert.Equal("Sqlite", ready.GetProperty("iamProvider").GetString());
        Assert.Equal("Iam", ready.GetProperty("iamConnectionName").GetString());
        Assert.Contains("Sqlite", ready.GetProperty("iamDatabaseProvider").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(ready.GetProperty("iamUsesExpectedDatabase").GetBoolean());
        Assert.True(ready.GetProperty("iamMigrationsApplied").GetBoolean());
        Assert.False(ready.GetProperty("iamMigrationsPending").GetBoolean());
        Assert.True(ready.GetProperty("databasesAreDistinct").GetBoolean());
        Assert.EndsWith("EfClaimMappingStore", ready.GetProperty("mappingStoreType").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("DefaultClaimsNormalizer", ready.GetProperty("normalizerType").GetString(), StringComparison.Ordinal);
        Assert.Equal(WorkerOidcHostFixture.TenantId, ready.GetProperty("persistenceScope").GetString());
        Assert.Equal("Ordinary", ready.GetProperty("persistenceAccessPolicy").GetString());
        Assert.False(ready.GetProperty("persistenceAcrossScopes").GetBoolean());
        Assert.True(ready.GetProperty("usesConfiguredTenant").GetBoolean());
    }

    private static async Task SaveRuleAsync(
        WorkerOidcHostProcess host,
        string id,
        string tenantId,
        string provider,
        params string[] grants) =>
        _ = await host.ControlAsync("save-rule", new
        {
            id,
            tenantId,
            provider,
            matchClaimType = ClaimTypes.NameIdentifier,
            matchValue = "worker-actor",
            grantRoles = Array.Empty<string>(),
            grantPermissions = grants,
            order = 0,
            stopOnMatch = true
        });

    private static async Task<HttpResponseMessage> PostExecuteAsync(
        WorkerOidcHostProcess host,
        string artifactId,
        string? token,
        string? persistenceProbe = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, string.Format(ExecutePath, artifactId))
        {
            Content = JsonContent.Create(new { })
        };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (persistenceProbe is not null)
            request.Headers.Add("X-Worker-Persistence-Probe", persistenceProbe);
        return await host.Client.SendAsync(request);
    }

    private static async Task<JsonElement> SnapshotAsync(WorkerOidcHostProcess host, string? executionId = null) =>
        await host.ControlAsync("snapshot", new { executionId });

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static async Task ResetMappingReadsAsync(WorkerOidcHostProcess host) =>
        _ = await host.ControlAsync("reset-mapping-reads");

    private static async Task<int> MappingReadCountAsync(WorkerOidcHostProcess host) =>
        (await SnapshotAsync(host)).GetProperty("mappingReadCount").GetInt32();

    private static string RequestOperationId(HttpResponseMessage response)
    {
        var operationId = Assert.Single(response.Headers.GetValues(WorkerOidcHostFixture.RequestOperationIdHeader));
        Assert.Matches("^op-[1-9][0-9]*$", operationId);
        return operationId;
    }

    private static async Task AssertSingleHttpMappingReadAttributedToResponseAsync(
        WorkerOidcHostProcess host,
        HttpResponseMessage response,
        string expectedCategory)
    {
        var snapshot = await SnapshotAsync(host);
        var read = Assert.Single(snapshot.GetProperty("mappingReadObservations").EnumerateArray());
        Assert.Equal(1, snapshot.GetProperty("mappingReadCount").GetInt64());
        Assert.Equal(RequestOperationId(response), read.GetProperty("operationId").GetString());
        Assert.Equal("http-request", read.GetProperty("operationKind").GetString());
        Assert.Equal("direct-http-context", read.GetProperty("operationSource").GetString());
        Assert.Equal(expectedCategory, read.GetProperty("contextCategory").GetString());
        Assert.True(read.GetProperty("directHttpContextPresent").GetBoolean());
        Assert.Equal(RequestOperationId(response), read.GetProperty("directHttpOperationId").GetString());
        Assert.Equal(RequestOperationId(response), read.GetProperty("flowedOperationId").GetString());
        Assert.Equal("http-request", read.GetProperty("flowedOperationKind").GetString());
        Assert.Equal(read.GetProperty("operationEpoch").GetInt64(), read.GetProperty("queryEpoch").GetInt64());
        Assert.False(snapshot.GetProperty("observationRecordsTruncated").GetBoolean());
    }

    private static async Task AssertResetSpanningControlReadAsync(WorkerOidcHostProcess host)
    {
        await ResetMappingReadsAsync(host);
        var beforeResetSpanningControl = await SnapshotAsync(host);
        var resetSpanningControl = await host.ControlWithReceiptAsync("list-rules", new
        {
            tenantId = WorkerOidcHostFixture.TenantId,
            provider = WorkerOidcHostFixture.ProviderId
        }, resetEpochAfterOperationEntry: true);
        var afterResetSpanningControl = await SnapshotAsync(host);
        var resetSpanningRead = Assert.Single(afterResetSpanningControl.GetProperty("mappingReadObservations").EnumerateArray());
        Assert.Matches("^op-[1-9][0-9]*$", resetSpanningControl.OperationId);
        Assert.Equal("fixture-control", resetSpanningControl.OperationKind);
        Assert.Equal("list-rules", resetSpanningControl.OperationCategory);
        Assert.Equal("flowed-control-operation", resetSpanningRead.GetProperty("operationSource").GetString());
        Assert.False(resetSpanningRead.GetProperty("directHttpContextPresent").GetBoolean());
        Assert.Equal(resetSpanningControl.OperationId, resetSpanningRead.GetProperty("flowedOperationId").GetString());
        Assert.Equal("fixture-control", resetSpanningRead.GetProperty("flowedOperationKind").GetString());
        Assert.Equal(resetSpanningControl.OperationId, resetSpanningRead.GetProperty("operationId").GetString());
        Assert.Equal(resetSpanningControl.OperationEpoch, resetSpanningRead.GetProperty("operationEpoch").GetInt64());
        Assert.Equal(resetSpanningControl.OperationEpoch + 1, resetSpanningRead.GetProperty("queryEpoch").GetInt64());
        Assert.Equal(resetSpanningRead.GetProperty("queryEpoch").GetInt64(),
            afterResetSpanningControl.GetProperty("mappingReadEpoch").GetInt64());
        Assert.Equal(1, afterResetSpanningControl.GetProperty("mappingReadCount").GetInt64());
        Assert.Equal(beforeResetSpanningControl.GetProperty("lifetimeMappingReadCount").GetInt64() + 1,
            afterResetSpanningControl.GetProperty("lifetimeMappingReadCount").GetInt64());
        Assert.False(afterResetSpanningControl.GetProperty("observationRecordsTruncated").GetBoolean());
    }

    private static async Task AssertNoMappingReadOrRuntimeEffectAsync(
        WorkerOidcHostProcess host,
        string? expectedProbe = null,
        HttpStatusCode? actualStatusCode = null,
        string? operationId = null)
    {
        var snapshot = await SnapshotAsync(host);
        var accessObservations = snapshot.GetProperty("persistenceAccessObservations").EnumerateArray().ToArray();
        var mappingReadObservations = snapshot.GetProperty("mappingReadObservations").EnumerateArray().ToArray();
        var accessSummary = accessObservations.Select(FormatOperationObservation);
        var mappingReadSummary = mappingReadObservations.Select(FormatOperationObservation);
        var diagnostic = $"Probe '{expectedProbe ?? "none"}', response '{actualStatusCode?.ToString() ?? "not asserted"}', " +
                         $"operation '{operationId ?? "none"}', epoch {snapshot.GetProperty("mappingReadEpoch").GetInt64()}, " +
                         $"lifetime SELECT count {snapshot.GetProperty("lifetimeMappingReadCount").GetInt64()}, " +
                         $"records truncated {snapshot.GetProperty("observationRecordsTruncated").GetBoolean()}, " +
                         $"access observations [{string.Join(", ", accessSummary)}], " +
                         $"mapping-read observations [{string.Join(", ", mappingReadSummary)}].";

        if (expectedProbe is not null)
            Assert.True(accessObservations.Any(observation =>
                    observation.GetProperty("persistenceAccessCategory").GetString() == expectedProbe &&
                    (observation.GetProperty("directHttpOperationId").GetString() == operationId ||
                     observation.GetProperty("flowedOperationId").GetString() == operationId)),
                $"Expected the allowlisted persistence context and independent HTTP operation to be observed together. {diagnostic}");
        if (actualStatusCode is not null)
            Assert.True(actualStatusCode == HttpStatusCode.Unauthorized, $"Expected an unauthorized response. {diagnostic}");

        foreach (var property in new[] { "mappingReadCount", "workflowExecutionStateRows", "activityExecutionStateRows", "bookmarkRows" })
        {
            var count = snapshot.GetProperty(property).GetInt32();
            Assert.True(count == 0, $"Expected zero {property}, observed {count}. {diagnostic}");
        }
        Assert.Equal(WorkerOidcHostFixture.TenantId, snapshot.GetProperty("persistenceScope").GetString());
        Assert.Equal("Ordinary", snapshot.GetProperty("persistenceAccessPolicy").GetString());
        Assert.False(snapshot.GetProperty("persistenceAcrossScopes").GetBoolean());
    }

    private static string FormatOperationObservation(JsonElement observation)
    {
        var endEpoch = observation.TryGetProperty("queryEpoch", out var queryEpoch)
            ? queryEpoch.GetInt64()
            : observation.GetProperty("observationEpoch").GetInt64();
        var startEpoch = observation.GetProperty("operationEpoch").ValueKind == JsonValueKind.Number
            ? observation.GetProperty("operationEpoch").GetInt64().ToString()
            : "none";
        var persistenceAccessCategory = observation.TryGetProperty("persistenceAccessCategory", out var category)
            ? $" selected={category.GetString()}"
            : string.Empty;
        var directHttpContext = observation.GetProperty("directHttpContextPresent").GetBoolean()
            ? "present"
            : "absent";
        return $"{observation.GetProperty("operationId").GetString()} " +
               $"{observation.GetProperty("operationSource").GetString()}/" +
               $"{observation.GetProperty("operationKind").GetString()}:" +
               $"{observation.GetProperty("contextCategory").GetString()}{persistenceAccessCategory} " +
               $"epoch {startEpoch}->{endEpoch} " +
               $"direct-http {directHttpContext}/{observation.GetProperty("directHttpOperationId").GetString() ?? "none"} " +
               $"flowed {observation.GetProperty("flowedOperationId").GetString() ?? "none"}/" +
               $"{observation.GetProperty("flowedOperationKind").GetString() ?? "none"}:" +
               $"{observation.GetProperty("flowedContextCategory").GetString() ?? "none"}";
    }

    private static async Task AssertNoRuntimeRowsAsync(WorkerOidcHostProcess host)
    {
        var snapshot = await SnapshotAsync(host);
        Assert.Equal(0, snapshot.GetProperty("workflowExecutionStateRows").GetInt32());
        Assert.Equal(0, snapshot.GetProperty("activityExecutionStateRows").GetInt32());
        Assert.Equal(0, snapshot.GetProperty("bookmarkRows").GetInt32());
    }

    private static async Task AssertNoUserOrExternalIdentityRowsAsync(WorkerOidcHostProcess host)
    {
        var snapshot = await SnapshotAsync(host);
        Assert.Equal(0, snapshot.GetProperty("userCount").GetInt32());
        Assert.Equal(0, snapshot.GetProperty("externalIdentityCount").GetInt32());
    }
}

internal sealed class UnixPtyFactAttribute : FactAttribute
{
    public UnixPtyFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "The generated-candidate actor requires the shared Unix PTY helper; a Windows skip is not acceptance evidence.";
    }
}
