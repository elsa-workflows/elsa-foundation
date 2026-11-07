using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Extensions;
using Elsa.Connections.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Jobs.Core;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Jobs.Secrets.Tests;

public sealed class JobConnectionsControls
{
    [Fact]
    public async Task Actual_resolver_and_shared_worker_keep_colliding_aliases_in_the_explicit_job_tenant_after_restart()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-jobs-connections-").FullName;
        try
        {
            JobAcceptance alpha; JobAcceptance beta;
            await using (var producer = await ConnectionJobs.StartAsync(directory))
            {
                await ConnectionJobs.SeedAsync(producer);
                alpha = await ConnectionJobs.SubmitAsync(producer, SecretJobHost.Alpha, "alpha");
                beta = await ConnectionJobs.SubmitAsync(producer, SecretJobHost.Beta, "beta");
                var retained = (await producer.ReadAsync(SecretJobHost.Alpha, alpha.JobId))!.Request!.Input.Value;
                Assert.Equal(ConnectionJobs.Alias, retained.GetProperty("connectionName").GetString());
                Assert.Equal(SecretJobHost.Reference.Name, retained.GetProperty("credential").GetProperty("name").GetString());
                ConnectionJobs.NoCanaries(retained.GetRawText());
            }
            var control = new ConnectionJobControl { RequireConcurrentTenants = true };
            await using var worker = await ConnectionJobs.StartAsync(directory, worker: true, control: control);
            await control.BothTenants.Task.WaitAsync(TimeSpan.FromSeconds(30));
            ConnectionJobs.Useful(await worker.WaitAsync(SecretJobHost.Alpha, alpha.JobId));
            ConnectionJobs.Useful(await worker.WaitAsync(SecretJobHost.Beta, beta.JobId));
            for (var index = 0; index < 4; index++)
            {
                var tenant = index % 2 == 0 ? SecretJobHost.Beta : SecretJobHost.Alpha;
                var accepted = await ConnectionJobs.SubmitAsync(worker, tenant, "alternate-" + index);
                ConnectionJobs.Useful(await worker.WaitAsync(tenant, accepted.JobId));
            }
            Assert.Equal(6, control.Observations.Count);
            Assert.All(control.Observations, item =>
            {
                Assert.Equal(item.JobTenant, item.ConnectionTenant);
                Assert.Equal(item.JobTenant == SecretJobHost.Alpha ? SecretJobHost.AlphaValue : SecretJobHost.BetaValue, item.Value);
                Assert.Equal(ConnectionJobs.Alias, item.Name); Assert.True(item.Version > 0); Assert.False(string.IsNullOrEmpty(item.IncarnationId));
            });
            Assert.Equal(6, control.Observations.Select(item => item.InvocationScope).Distinct().Count());
            await ConnectionJobs.AssertRetainedSafeAsync(worker, SecretJobHost.Alpha, alpha.JobId);
            await ConnectionJobs.AssertRetainedSafeAsync(worker, SecretJobHost.Beta, beta.JobId);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Retry_after_real_host_restart_resolves_current_tenant_connection_and_rotated_secret_in_a_new_scope()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-jobs-connection-retry-").FullName;
        try
        {
            JobAcceptance accepted;
            await using (var producer = await ConnectionJobs.StartAsync(directory))
            {
                await ConnectionJobs.SeedAsync(producer);
                accepted = await ConnectionJobs.SubmitAsync(producer, SecretJobHost.Alpha, "retry", "retry");
            }
            var first = new ConnectionJobControl();
            await using (var worker = await ConnectionJobs.StartAsync(directory, worker: true, control: first))
            {
                await first.FirstRetry.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await worker.UseAsync(async services =>
                {
                    var paused = await services.GetRequiredService<IJobCommands>().PauseAsync(new(
                        new(SecretJobHost.Partition, SecretJobHost.Alpha), new(JobGateKind.Queue, "reports"),
                        "connection-retry-restart", new("pause-connection-retry", "test", "Hold retry across restart")));
                    Assert.Equal(JobCommandStatus.Applied, paused.Status); return true;
                });
                first.ContinueRetry.TrySetResult();
                await ConnectionJobs.UntilAsync(async () => (await worker.UseAsync(async services => await services.GetRequiredService<IJobQueries>()
                    .ListPhysicalReceiptsAsync(new(SecretJobHost.Partition, SecretJobHost.Alpha), accepted.JobId, new(25, null)))).Items.Count == 1);
                var pending = (await worker.ReadAsync(SecretJobHost.Alpha, accepted.JobId))!;
                Assert.Null(pending.Result); Assert.Null(pending.ActiveAttempt);
                Assert.Single(first.Observations);
                await ConnectionJobs.AssertRetainedSafeAsync(worker, SecretJobHost.Alpha, accepted.JobId);
            }
            await using (var producer = await ConnectionJobs.StartAsync(directory))
            {
                await producer.UseAsync(async services =>
                {
                    await services.GetRequiredService<ISecretManager>().RotateAsync(SecretJobHost.Alpha, SecretJobHost.Reference.Name,
                        new() { Value = ConnectionJobs.Rotated });
                    await services.GetRequiredService<IConnectionManager>().UpdateAsync(SecretJobHost.Alpha, ConnectionJobs.Alias,
                        new() { DisplayName = "Updated between attempts" }); return true;
                });
            }
            var second = new ConnectionJobControl();
            await using (var successor = await ConnectionJobs.StartAsync(directory, worker: true, control: second))
            {
                await successor.UseAsync(async services =>
                {
                    var scope = new JobScope(SecretJobHost.Partition, SecretJobHost.Alpha); var gate = new JobGateKey(JobGateKind.Queue, "reports");
                    var hold = Assert.Single((await services.GetRequiredService<IJobQueries>().ListHoldsAsync(scope, gate, new(25, null))).Items);
                    var released = await services.GetRequiredService<IJobCommands>().ReleaseHoldAsync(new(scope, gate, hold.HoldId, hold.Revision,
                        new("release-connection-retry", "test", "Resume after resolver restart")));
                    Assert.Equal(JobCommandStatus.Applied, released.Status); return true;
                });
                ConnectionJobs.Useful(await successor.WaitAsync(SecretJobHost.Alpha, accepted.JobId));
                var before = Assert.Single(first.Observations); var after = Assert.Single(second.Observations);
                Assert.Equal(1, before.Attempt); Assert.Equal(2, after.Attempt);
                Assert.Equal(SecretJobHost.AlphaValue, before.Value); Assert.Equal(ConnectionJobs.Rotated, after.Value);
                Assert.Equal(SecretJobHost.Alpha, after.ConnectionTenant); Assert.NotEqual(before.InvocationScope, after.InvocationScope);
                Assert.Equal(before.IncarnationId, after.IncarnationId); Assert.True(after.Version > before.Version);
                await ConnectionJobs.AssertRetainedSafeAsync(successor, SecretJobHost.Alpha, accepted.JobId, attempts: 2);
                var beta = await ConnectionJobs.SubmitAsync(successor, SecretJobHost.Beta, "beta-after-retry");
                ConnectionJobs.Useful(await successor.WaitAsync(SecretJobHost.Beta, beta.JobId));
                Assert.Equal(SecretJobHost.BetaValue, second.Observations.Last().Value);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("missing-connection")]
    [InlineData("revoked-connection")]
    [InlineData("missing-secret")]
    [InlineData("revoked-secret")]
    public async Task Real_missing_or_revoked_binding_never_falls_back_to_the_other_tenants_alias(string kind)
    {
        await using var producer = await ConnectionJobs.StartAsync(); await ConnectionJobs.SeedAsync(producer);
        var tenant = kind == "missing-connection" ? SecretJobHost.Missing : SecretJobHost.Beta;
        await producer.UseAsync(async services =>
        {
            if (kind == "revoked-connection") await services.GetRequiredService<IConnectionManager>().RevokeAsync(tenant, ConnectionJobs.Alias);
            if (kind == "missing-secret") Assert.True(await services.GetRequiredService<ISecretManager>().DeleteAsync(tenant, SecretJobHost.Reference.Name));
            if (kind == "revoked-secret") await services.GetRequiredService<ISecretManager>().RevokeAsync(tenant, SecretJobHost.Reference.Name);
            return true;
        });
        var accepted = await ConnectionJobs.SubmitAsync(producer, tenant, kind);
        var control = new ConnectionJobControl();
        await using var worker = await ConnectionJobs.StartAsync(producer.StorageDirectory, worker: true, control: control);
        var selected = await worker.WaitAsync(tenant, accepted.JobId);
        Assert.Equal(JobOutcome.Failed, selected.Result!.Outcome); Assert.Null(selected.Result.Payload);
        Assert.Equal("jobs.data-policy.refused", selected.Result.Diagnostic!.Code); Assert.Empty(control.Observations);
        var failure = Assert.Single(control.Failures);
        Assert.Equal(tenant, failure.Tenant); Assert.Equal(accepted.JobId, failure.JobId); Assert.Equal(1, failure.Attempt);
        Assert.Equal(ConnectionFaultCodes.Unavailable, failure.Code);
        Assert.Equal(kind.EndsWith("connection", StringComparison.Ordinal) ? "connection" : "credential", failure.Stage);
        await ConnectionJobs.AssertRetainedSafeAsync(worker, tenant, accepted.JobId);
    }

    [Theory]
    [InlineData("result-secret")]
    [InlineData("progress-message")]
    [InlineData("progress-data")]
    [InlineData("exception")]
    public async Task Declared_credential_and_actual_connection_value_never_enter_job_outputs_progress_or_framework_diagnostics(string mode)
    {
        await using var producer = await ConnectionJobs.StartAsync(); await ConnectionJobs.SeedAsync(producer);
        var accepted = await ConnectionJobs.SubmitAsync(producer, SecretJobHost.Alpha, mode, mode);
        await using var worker = await ConnectionJobs.StartAsync(producer.StorageDirectory, worker: true);
        var terminal = await worker.WaitAsync(SecretJobHost.Alpha, accepted.JobId);
        Assert.Equal(JobOutcome.Failed, terminal.Result!.Outcome); Assert.Null(terminal.Result.Payload); Assert.Null(terminal.Progress);
        Assert.Equal(mode == "exception" ? "jobs.handler.failed" : "jobs.data-policy.refused", terminal.Result.Diagnostic!.Code);
        await ConnectionJobs.AssertRetainedSafeAsync(worker, SecretJobHost.Alpha, accepted.JobId, mode == "exception" ? 2 : 1);
    }
}

internal static class ConnectionJobs
{
    internal const string Alias = "jobs.shared-report";
    internal const string Rotated = "ROTATED_CONNECTION_JOB_CANARY_5896";
    internal static readonly JobHandlerSupport Support = new(new("connection.report", "1"), new("connection.report.input", 1),
        new("connection.report.result", 1), "connection-report-controls-v1");

    internal static Task<SecretJobHost> StartAsync(string? directory = null, bool worker = false, ConnectionJobControl? control = null) =>
        SecretJobHost.StartAsync(directory, worker: worker, configureStorage: (services, storageDirectory) =>
        {
            services.AddConnections();
            services.AddConnectionsEntityFrameworkCore(new() { Provider = "Sqlite",
                ConnectionString = "Data Source=" + Path.Combine(storageDirectory, "connections.db") + ";Pooling=False" });
            services.AddSingleton(control ?? new ConnectionJobControl());
            if (worker) services.AddJobHandler<ConnectionJobHandler, ConnectionJobInput, JsonElement>(Support);
        }, additionalDeclarations: options => options.Declarations.Add(new(Support.Handler, Support.InputSchema,
            [new("/credential", [SecretJobHost.Reference])])));

    internal static async Task SeedAsync(SecretJobHost host)
    {
        foreach (var tenant in new[] { SecretJobHost.Alpha, SecretJobHost.Beta })
        {
            await host.SeedAsync(tenant, tenant == SecretJobHost.Alpha ? SecretJobHost.AlphaValue : SecretJobHost.BetaValue);
            await host.UseAsync(async services => await services.GetRequiredService<IConnectionManager>().CreateAsync(tenant, new()
            {
                Name = Alias, TypeName = ConnectionTypeNames.Http, AuthScheme = HttpAuthSchemes.Bearer,
                Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "example.invalid" },
                SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
                { [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = SecretJobHost.Reference.Name } }
            }));
        }
    }

    internal static Task<JobAcceptance> SubmitAsync(SecretJobHost host, string tenant, string key, string mode = "safe") => host.UseAsync(async services =>
        await services.GetRequiredService<IJobClient>().SubmitAsync(new(SecretJobHost.Partition, tenant), new(key,
            new(Support.Handler, new(Support.InputSchema, JsonSerializer.SerializeToElement(new ConnectionJobInput(SecretJobHost.Reference, Alias, [12.5m, 7.5m], mode), SecretJobHost.Json)),
                Support.ResultSchema, "reports", new(new(2, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), 1, 0, true),
                    TimeSpan.FromSeconds(30), TimeSpan.FromDays(30)), null, null))));

    internal static void Useful(JobSnapshot snapshot)
    {
        Assert.Equal(JobOutcome.Succeeded, snapshot.Result!.Outcome);
        Assert.Equal(20m, snapshot.Result.Payload!.Value.GetProperty("Total").GetDecimal());
        Assert.Equal(2, snapshot.Result.Payload.Value.GetProperty("LineCount").GetInt32());
    }

    internal static async Task AssertRetainedSafeAsync(SecretJobHost host, string tenant, string id, int attempts = 1)
    {
        await host.UseAsync(async services =>
        {
            var scope = new JobScope(SecretJobHost.Partition, tenant); var queries = services.GetRequiredService<IJobQueries>();
            var snapshot = await queries.FindAsync(scope, id);
            var history = await queries.ListAttemptsAsync(scope, id, new(25, null)); var receipts = await queries.ListPhysicalReceiptsAsync(scope, id, new(25, null));
            Assert.Equal(attempts, history.Items.Count); Assert.Equal(attempts, receipts.Items.Count);
            var jobs = await queries.ListJobsAsync(scope, new(new(25, null), null, null, null)); Assert.Null(jobs.ContinuationToken);
            NoCanaries(JsonSerializer.Serialize(new { snapshot, history, receipts, jobs,
                connections = await services.GetRequiredService<IConnectionManager>().ListAsync(tenant) })); return true;
        });
        Assert.All(host.Logs.Entries, NoCanaries);
    }

    internal static void NoCanaries(string text)
    {
        Assert.DoesNotContain(SecretJobHost.AlphaValue, text); Assert.DoesNotContain(SecretJobHost.BetaValue, text); Assert.DoesNotContain(Rotated, text);
    }

    internal static async Task UntilAsync(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await predicate()) await Task.Delay(25, timeout.Token);
    }
}

internal sealed record ConnectionJobInput(SecretReference Credential, string ConnectionName, decimal[] Amounts, string Mode);
internal sealed record ConnectionJobObservation(string JobTenant, string ConnectionTenant, string JobId, int Attempt,
    string Name, int Version, string? IncarnationId, string Value, Guid InvocationScope);
internal sealed record ConnectionJobFailure(string Tenant, string JobId, int Attempt, string Stage, string Code);
internal sealed class ConnectionJobControl
{
    internal readonly ConcurrentQueue<ConnectionJobObservation> Observations = new();
    internal readonly ConcurrentQueue<ConnectionJobFailure> Failures = new();
    private readonly ConcurrentDictionary<string, byte> _tenants = new(StringComparer.Ordinal);
    internal bool RequireConcurrentTenants;
    internal readonly TaskCompletionSource BothTenants = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource FirstRetry = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource ContinueRetry = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal async Task ObserveAsync(ConnectionJobObservation observation, string mode, CancellationToken cancellationToken)
    {
        Observations.Enqueue(observation);
        if (RequireConcurrentTenants)
        {
            _tenants.TryAdd(observation.JobTenant, 0); if (_tenants.Count == 2) BothTenants.TrySetResult();
            await BothTenants.Task.WaitAsync(cancellationToken);
        }
        if (mode == "retry" && observation.Attempt == 1)
        { FirstRetry.TrySetResult(); await ContinueRetry.Task.WaitAsync(cancellationToken); }
    }
}

internal sealed class ConnectionJobHandler(IConnectionResolver resolver, IJobSecrets secrets, ConnectionJobControl control) : IJobHandler<ConnectionJobInput, JsonElement>
{
    private readonly Guid _invocationScope = Guid.NewGuid();
    public async ValueTask<JsonElement> ExecuteAsync(ConnectionJobInput input, JobExecutionContext context, CancellationToken cancellationToken)
    {
        if (input.ConnectionName != ConnectionJobs.Alias || string.IsNullOrWhiteSpace(context.Scope.TenantId)) throw new JobDataPolicyException();
        var stage = "connection";
        try
        {
            // This is the real registered resolver, not an ambient workflow/application tenant accessor.
            await using var connection = await resolver.ResolveAsync(new()
            { TenantId = context.Scope.TenantId, Name = input.ConnectionName, RequiredType = ConnectionTypeNames.Http }, cancellationToken);
            stage = "credential";
            var sensitive = await connection.GetSensitiveAsync(HttpConnectionSecretFields.Token, cancellationToken);
            var value = sensitive.Reveal();
            // The host declares the underlying secret too. This existing facade registers its literal
            // with the Jobs output/progress policy; IConnectionResolver alone is not that policy.
            var declared = await secrets.ResolveAsync(input.Credential, cancellationToken);
            if (connection.TenantId != context.Scope.TenantId || value != declared || sensitive.ToString() != SensitiveValue.Redacted)
                throw new JobDataPolicyException();
            await control.ObserveAsync(new(context.Scope.TenantId, connection.TenantId, context.JobId, context.AttemptNumber,
                connection.Name, connection.Version, connection.IncarnationId, value, _invocationScope), input.Mode, cancellationToken);
            if (input.Mode == "exception" || input.Mode == "retry" && context.AttemptNumber == 1) throw new InvalidOperationException(value);
            if (input.Mode == "progress-message") await context.Progress.ReportAsync(new(.5, value, null), cancellationToken);
            if (input.Mode == "progress-data") await context.Progress.ReportAsync(new(.5, "Calculating report", new(new("report.progress", 1), JsonSerializer.SerializeToElement(new { Token = value }))), cancellationToken);
            return input.Mode == "result-secret" ? JsonSerializer.SerializeToElement(new { Token = value })
                : JsonSerializer.SerializeToElement(new { Total = input.Amounts.Sum(), LineCount = input.Amounts.Length });
        }
        catch (ConnectionUnavailableException error)
        {
            control.Failures.Enqueue(new(context.Scope.TenantId, context.JobId, context.AttemptNumber, stage, error.Code));
            throw new JobDataPolicyException();
        }
    }
}
