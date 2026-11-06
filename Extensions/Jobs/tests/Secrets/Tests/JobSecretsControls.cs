using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Services;
using Elsa.Secrets.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Jobs.Secrets.Tests;

public sealed class JobSecretsControls
{
    [Theory]
    [InlineData("\"PLAINTEXT_SECRET_CANARY\"")]
    [InlineData("null")]
    [InlineData("{\"name\":\"not.allowed\",\"typeName\":\"text\",\"scope\":\"reports\"}")]
    [InlineData("{\"name\":\"Jobs.Shared\",\"typeName\":\"text\",\"scope\":\"reports\",\"tenantId\":\"tenant-beta\"}")]
    [InlineData("{\"name\":42}")]
    public async Task Producer_only_declarations_refuse_plaintext_malformed_and_undeclared_bindings_without_resolving(string credential)
    {
        await using var host = await SecretJobHost.StartAsync(installSecrets: false);
        using var json = JsonDocument.Parse("{\"credential\":" + credential + ",\"protected\":null,\"mode\":\"safe\"}");
        var error = await Assert.ThrowsAsync<JobDataPolicyException>(() => host.SubmitAsync(SecretJobHost.Alpha, "invalid", json.RootElement));
        Assert.Equal("The Jobs data policy refused this operation.", error.Message); Assert.Null(error.InnerException);
        Assert.DoesNotContain("CANARY", error.ToString());
        var page = await host.UseAsync(async services => await services.GetRequiredService<IJobQueries>().ListJobsAsync(new(SecretJobHost.Partition, SecretJobHost.Alpha), new(new(25, null), null, null, null)));
        Assert.Empty(page.Items); Assert.Empty(await host.UseAsync(services => Task.FromResult(services.GetServices<ISecretValueResolver>().ToArray())));
    }

    [Fact]
    public async Task Declared_inputs_reject_undeclared_reference_objects_and_ambiguous_duplicate_members_but_unrelated_business_objects_remain_plain()
    {
        await using var host = await SecretJobHost.StartAsync(installSecrets: false);
        var reference = JsonSerializer.Serialize(SecretJobHost.Reference, SecretJobHost.Json);
        foreach (var value in new[] { "{\"credential\":" + reference + ",\"protected\":null,\"other\":" + reference + "}",
                     "{\"credential\":" + reference + ",\"Credential\":\"PLAIN\",\"protected\":null}" })
        {
            using var json = JsonDocument.Parse(value);
            await Assert.ThrowsAsync<JobDataPolicyException>(() => host.SubmitAsync(SecretJobHost.Alpha, Guid.NewGuid().ToString("N"), json.RootElement));
        }
        var plain = SecretJobHost.Request(new { name = "Ordinary business name" }) with { Handler = new("plain.business", "1") };
        var accepted = await host.UseAsync(async services => await services.GetRequiredService<IJobClient>().SubmitAsync(new(SecretJobHost.Partition, SecretJobHost.Alpha), new("plain", plain)));
        Assert.NotNull(await host.ReadAsync(SecretJobHost.Alpha, accepted.JobId));
        await host.UseAsync(async services => { await Assert.ThrowsAsync<JobDataPolicyException>(async () => await services.GetRequiredService<IJobSecrets>().ResolveAsync(SecretJobHost.Reference)); return true; });
    }

    [Fact]
    public async Task One_real_shared_worker_resolves_persisted_references_for_concurrent_then_alternating_tenants_after_restart()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-job-secrets-restart-").FullName;
        try
        {
            JobAcceptance alpha; JobAcceptance beta;
            await using (var producer = await SecretJobHost.StartAsync(directory))
            {
                await producer.SeedAsync(SecretJobHost.Alpha, SecretJobHost.AlphaValue); await producer.SeedAsync(SecretJobHost.Beta, SecretJobHost.BetaValue);
                alpha = await producer.SubmitAsync(SecretJobHost.Alpha, "alpha"); beta = await producer.SubmitAsync(SecretJobHost.Beta, "beta");
                var retained = (await producer.ReadAsync(SecretJobHost.Alpha, alpha.JobId))!.Request!.Input.Value.GetRawText();
                Assert.Contains("Jobs.Shared", retained); Assert.DoesNotContain(SecretJobHost.AlphaValue, retained); Assert.DoesNotContain(SecretJobHost.BetaValue, retained);
            }
            var control = new SecretHandlerControl { RequireConcurrentTenants = true };
            await using var worker = await SecretJobHost.StartAsync(directory, worker: true, control: control);
            await control.BothTenants.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(JobOutcome.Succeeded, (await worker.WaitAsync(SecretJobHost.Alpha, alpha.JobId)).Result!.Outcome);
            Assert.Equal(JobOutcome.Succeeded, (await worker.WaitAsync(SecretJobHost.Beta, beta.JobId)).Result!.Outcome);
            for (var index = 0; index < 4; index++)
            {
                var tenant = index % 2 == 0 ? SecretJobHost.Beta : SecretJobHost.Alpha;
                var accepted = await worker.SubmitAsync(tenant, "alternating-" + index); await worker.WaitAsync(tenant, accepted.JobId);
            }
            Assert.Equal(6, control.Observations.Count);
            Assert.All(control.Observations, item => { Assert.Equal(item.Tenant == SecretJobHost.Alpha ? SecretJobHost.AlphaValue : SecretJobHost.BetaValue, item.Value); Assert.Equal(SecretJobHost.Support.Handler, item.Handler); });
            Assert.All(worker.Logs.Entries, text => { Assert.DoesNotContain(SecretJobHost.AlphaValue, text); Assert.DoesNotContain(SecretJobHost.BetaValue, text); });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("scope-denied")]
    [InlineData("revoked")]
    public async Task Missing_or_denied_default_secret_resolution_is_nonretryable_and_never_falls_back_to_another_tenant(string kind)
    {
        await using var host = await SecretJobHost.StartAsync(worker: true);
        await host.SeedAsync(SecretJobHost.Alpha, SecretJobHost.AlphaValue);
        var tenant = kind == "missing" ? SecretJobHost.Missing : SecretJobHost.Beta;
        if (kind != "missing") await host.SeedAsync(tenant, SecretJobHost.BetaValue, kind == "scope-denied" ? "different-scope" : "reports");
        if (kind == "revoked") await host.UseAsync(async services => await services.GetRequiredService<ISecretManager>().RevokeAsync(tenant, SecretJobHost.Reference.Name));
        var accepted = await host.SubmitAsync(tenant, kind); var result = await host.WaitAsync(tenant, accepted.JobId);
        await AssertSafeFailure(host, tenant, result); Assert.Empty(host.Control.Observations);
    }

    [Theory]
    [InlineData("result-secret")]
    [InlineData("result-protected")]
    [InlineData("progress-message")]
    [InlineData("progress-data")]
    [InlineData("progress-protected")]
    public async Task Literal_secret_and_protected_input_disclosure_never_enters_retained_results_progress_receipts_or_framework_logs(string mode)
    {
        await using var host = await SecretJobHost.StartAsync(worker: true); await host.SeedAsync(SecretJobHost.Alpha, SecretJobHost.AlphaValue);
        var accepted = await host.SubmitAsync(SecretJobHost.Alpha, mode, new SecretJobInput(SecretJobHost.Reference, SecretJobHost.ProtectedValue, mode));
        await AssertSafeFailure(host, SecretJobHost.Alpha, await host.WaitAsync(SecretJobHost.Alpha, accepted.JobId));
    }

    [Fact]
    public async Task Retry_uses_a_fresh_invocation_scope_and_current_secret_version_while_exception_text_stays_private()
    {
        var control = new SecretHandlerControl(); await using var host = await SecretJobHost.StartAsync(worker: true, control: control);
        await host.SeedAsync(SecretJobHost.Alpha, SecretJobHost.AlphaValue);
        var accepted = await host.SubmitAsync(SecretJobHost.Alpha, "retry", new SecretJobInput(SecretJobHost.Reference, null, "retry"));
        await control.FirstRetry.Task.WaitAsync(TimeSpan.FromSeconds(30));
        const string rotated = "ROTATED_ALPHA_JOB_SECRET_CANARY_1840";
        await host.UseAsync(async services => await services.GetRequiredService<ISecretManager>().RotateAsync(SecretJobHost.Alpha, SecretJobHost.Reference.Name, new() { Value = rotated }));
        control.ContinueRetry.TrySetResult(); var terminal = await host.WaitAsync(SecretJobHost.Alpha, accepted.JobId);
        Assert.Equal(JobOutcome.Succeeded, terminal.Result!.Outcome);
        var values = control.Observations.Where(item => item.JobId == accepted.JobId).OrderBy(item => item.Attempt).ToArray();
        Assert.Equal(2, values.Length); Assert.Equal(SecretJobHost.AlphaValue, values[0].Value); Assert.Equal(rotated, values[1].Value);
        var attempts = await host.UseAsync(async services => await services.GetRequiredService<IJobQueries>().ListAttemptsAsync(new(SecretJobHost.Partition, SecretJobHost.Alpha), accepted.JobId, new(25, null)));
        Assert.Equal(2, attempts.Items.Count); Assert.All(attempts.Items, item => Assert.DoesNotContain("CANARY", JsonSerializer.Serialize(item)));
        Assert.DoesNotContain("CANARY", JsonSerializer.Serialize(terminal.Result)); Assert.All(host.Logs.Entries, text => Assert.DoesNotContain("CANARY", text));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Typed_store_unavailability_retries_and_recovers_through_the_actual_default_resolver_without_retaining_provider_text(bool injectTypedResolverFailure)
    {
        var outage = new UnavailableOnce();
        await using var host = await SecretJobHost.StartAsync(worker: true, configure: services =>
        {
            if (injectTypedResolverFailure)
            {
                services.AddScoped<DefaultSecretValueResolver>();
                services.AddScoped<ISecretValueResolver>(provider => new UnavailableOnceResolver(provider.GetRequiredService<DefaultSecretValueResolver>(), outage));
            }
            else
            {
                // Fault injection stays below the real default resolver and uses the actual encrypted
                // store for writes/recovery. Its IOException must become typed StoreUnavailable.
                var registration = services.Single(item => item.ServiceType == typeof(ISecretStore) && item.ImplementationType == typeof(EncryptedSecretStore));
                services.Remove(registration); services.AddSingleton<EncryptedSecretStore>();
                services.AddSingleton<ISecretStore>(provider => new UnavailableOnceStore(provider.GetRequiredService<EncryptedSecretStore>(), outage));
            }
        });
        await host.SeedAsync(SecretJobHost.Alpha, SecretJobHost.AlphaValue);
        var accepted = await host.SubmitAsync(SecretJobHost.Alpha, "transient-outage");
        var terminal = await host.WaitAsync(SecretJobHost.Alpha, accepted.JobId);
        Assert.Equal(JobOutcome.Succeeded, terminal.Result!.Outcome);
        var observation = Assert.Single(host.Control.Observations); Assert.Equal(2, observation.Attempt); Assert.Equal(SecretJobHost.AlphaValue, observation.Value);
        Assert.Equal(new[] { SecretJobHost.Alpha, SecretJobHost.Alpha }, outage.Tenants.ToArray());
        var evidence = await host.UseAsync(async services =>
        {
            var queries = services.GetRequiredService<IJobQueries>(); var scope = new JobScope(SecretJobHost.Partition, SecretJobHost.Alpha);
            var attempts = await queries.ListAttemptsAsync(scope, accepted.JobId, new(25, null));
            var receipts = await queries.ListPhysicalReceiptsAsync(scope, accepted.JobId, new(25, null));
            Assert.Equal(2, attempts.Items.Count); Assert.Equal(2, receipts.Items.Count);
            var first = Assert.Single(receipts.Items, receipt => receipt.AttemptId == attempts.Items.Single(attempt => attempt.Lease.AttemptNumber == 1).Lease.AttemptId);
            Assert.True(first.Retryable); Assert.Null(first.Payload); Assert.Equal(JobAttemptOutcome.Failed, first.Outcome);
            Assert.Equal("jobs.data-policy.unavailable", first.Diagnostic!.Code);
            Assert.Equal("The Jobs data policy is temporarily unavailable.", first.Diagnostic.Summary);
            return JsonSerializer.Serialize(new { terminal.Result, terminal.Progress, attempts, receipts });
        });
        Assert.DoesNotContain("CANARY", evidence); Assert.All(host.Logs.Entries, text => Assert.DoesNotContain("CANARY", text));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task Optional_reference_declaration_preserves_supported_null_and_empty_inputs(string json)
    {
        await using var host = await SecretJobHost.StartAsync(worker: true); using var value = JsonDocument.Parse(json);
        var accepted = await host.SubmitAsync(SecretJobHost.Alpha, "null-empty", value.RootElement, nullHandler: true);
        var result = (await host.WaitAsync(SecretJobHost.Alpha, accepted.JobId)).Result!;
        Assert.Equal(JobOutcome.Succeeded, result.Outcome); Assert.True(result.Payload!.Value.GetProperty(json == "null" ? "NullInput" : "EmptyObject").GetBoolean());
    }

    [Theory]
    [InlineData("wrong-tenant")]
    [InlineData("wrong-name")]
    [InlineData("wrong-type")]
    [InlineData("wrong-scope")]
    [InlineData("failure-error")]
    [InlineData("throws")]
    public async Task Adverse_resolver_metadata_failures_and_exceptions_cannot_cross_the_bound_tenant_or_leak_provider_text(string kind)
    {
        var replacement = new AdverseResolver(kind);
        await using var host = await SecretJobHost.StartAsync(worker: true, configure: services => services.AddScoped<ISecretValueResolver>(_ => replacement));
        var accepted = await host.SubmitAsync(SecretJobHost.Alpha, kind); await AssertSafeFailure(host, SecretJobHost.Alpha, await host.WaitAsync(SecretJobHost.Alpha, accepted.JobId));
        Assert.Equal(SecretJobHost.Alpha, Assert.Single(replacement.Tenants));
    }

    private static async Task AssertSafeFailure(SecretJobHost host, string tenant, JobSnapshot snapshot)
    {
        Assert.Equal(JobOutcome.Failed, snapshot.Result!.Outcome); Assert.Null(snapshot.Result.Payload); Assert.Null(snapshot.Progress);
        Assert.Equal("jobs.data-policy.refused", snapshot.Result.Diagnostic!.Code);
        Assert.Equal("The Jobs data policy refused this operation.", snapshot.Result.Diagnostic.Summary);
        var evidence = await host.UseAsync(async services =>
        {
            var queries = services.GetRequiredService<IJobQueries>(); var scope = new JobScope(SecretJobHost.Partition, tenant);
            var attempts = await queries.ListAttemptsAsync(scope, snapshot.JobId, new(25, null)); var receipts = await queries.ListPhysicalReceiptsAsync(scope, snapshot.JobId, new(25, null));
            Assert.Single(attempts.Items); Assert.All(receipts.Items, receipt => { Assert.False(receipt.Retryable); Assert.Null(receipt.Payload); });
            return JsonSerializer.Serialize(new { snapshot.Result, snapshot.Progress, attempts, receipts });
        });
        Assert.DoesNotContain("CANARY", evidence); Assert.All(host.Logs.Entries, text => Assert.DoesNotContain("CANARY", text));
    }
    private sealed class AdverseResolver(string kind) : ISecretValueResolver
    {
        internal readonly List<string> Tenants = [];
        public ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default)
        {
            Tenants.Add(tenantId);
            if (kind == "throws") throw new InvalidOperationException("PROVIDER_EXCEPTION_CANARY");
            if (kind == "failure-error") return ValueTask.FromResult(ResolvedSecret.Failure(SecretResolutionFailureCode.Unauthorized, "PROVIDER_ERROR_CANARY"));
            return ValueTask.FromResult(ResolvedSecret.Success("FOREIGN_VALUE_CANARY", new() { TenantId = kind == "wrong-tenant" ? SecretJobHost.Beta : tenantId,
                Name = kind == "wrong-name" ? "another.secret" : reference.Name, TypeName = kind == "wrong-type" ? "rsa-key" : reference.TypeName!, Scope = kind == "wrong-scope" ? "another-scope" : reference.Scope, Status = SecretStatus.Active }));
        }
    }
    private sealed class UnavailableOnce
    {
        private int _calls;
        internal readonly System.Collections.Concurrent.ConcurrentQueue<string> Tenants = new();
        internal bool Fail(string tenant) { Tenants.Enqueue(tenant); return Interlocked.Increment(ref _calls) == 1; }
    }
    private sealed class UnavailableOnceResolver(ISecretValueResolver inner, UnavailableOnce outage) : ISecretValueResolver
    {
        public ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return outage.Fail(tenantId)
                ? ValueTask.FromResult(ResolvedSecret.Failure(SecretResolutionFailureCode.StoreUnavailable, "PROVIDER_UNAVAILABLE_ENDPOINT_CANARY"))
                : inner.ResolveAsync(tenantId, reference, cancellationToken);
        }
    }
    private sealed class UnavailableOnceStore(ISecretStore inner, UnavailableOnce outage) : ISecretStore
    {
        public SecretStoreDescriptor Descriptor => inner.Descriptor;
        public ValueTask<SecretPayload> WriteAsync(SecretWriteContext context, CancellationToken cancellationToken = default) => inner.WriteAsync(context, cancellationToken);
        public ValueTask<SecretPayload?> ReadAsync(SecretReadContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (outage.Fail(context.Secret.TenantId)) throw new IOException("SECRET_STORE_CONNECTION_ENDPOINT_CANARY");
            return inner.ReadAsync(context, cancellationToken);
        }
        public ValueTask DeleteAsync(SecretDeleteContext context, CancellationToken cancellationToken = default) => inner.DeleteAsync(context, cancellationToken);
        public ValueTask<SecretTestResult> TestAsync(SecretTestContext context, CancellationToken cancellationToken = default) => inner.TestAsync(context, cancellationToken);
    }
}
