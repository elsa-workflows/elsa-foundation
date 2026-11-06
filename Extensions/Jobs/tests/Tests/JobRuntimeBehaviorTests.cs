using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Jobs.Tests;

public class JobRuntimeBehaviorTests
{
    private static readonly JobScope Scope = new("jobs-tests", "tenant-one");
    private static readonly JobHandlerSupport Support = new(new("reports.generate", "1"), new("report", 1), new("report-result", 1), "deployment-a");
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Producer_validates_policy_and_rejects_forged_association_before_admission()
    {
        var transport = new ScriptedJobTransport();
        await using var provider = Producer(transport);
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IJobClient>();
        var valid = Request();
        var policy = valid with { Policy = valid.Policy with { Retry = valid.Policy.Retry with { JitterFraction = double.NaN } } };
        var invalid = await Assert.ThrowsAsync<JobException>(async () => await client.SubmitAsync(Scope, new("policy", policy)));
        Assert.Equal("jobs.policy.invalid", invalid.Code);
        var associated = valid with
        {
            Association = new("native", "orders", "1", "workflow-one", "invoke-one", "artifact-one", "hash-one", JobAssociationMode.Awaited, null)
        };
        var untrusted = await Assert.ThrowsAsync<JobException>(async () => await client.SubmitAsync(Scope, new("association", associated)));
        Assert.Equal("jobs.association.untrusted", untrusted.Code);
        Assert.Empty(transport.Admissions);
    }

    [Fact]
    public async Task Producer_enforces_payload_byte_bound_before_admission()
    {
        var transport = new ScriptedJobTransport();
        await using var provider = Producer(transport, options => options.MaximumPayloadBytes = 32);
        await using var scope = provider.CreateAsyncScope();
        var request = Request() with { Input = new(Support.InputSchema, JsonSerializer.SerializeToElement(new string('x', 33))) };
        var error = await Assert.ThrowsAsync<JobException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IJobClient>().SubmitAsync(Scope, new("too-large", request)));
        Assert.Equal("jobs.payload.invalid", error.Code);
        Assert.Empty(transport.Admissions);
    }

    [Fact]
    public async Task Default_payload_envelope_accepts_a_300_kib_input_before_durable_admission()
    {
        var acceptance = new JobAcceptance("large-input", Now, Now.AddDays(30), 1);
        var transport = new ScriptedJobTransport
        {
            Admission = (_, _) => ValueTask.FromResult(new JobAdmissionResult(JobCommandStatus.Applied, acceptance))
        };
        await using var provider = Producer(transport);
        await using var scope = provider.CreateAsyncScope();
        var request = Request() with { Input = new(Support.InputSchema, JsonSerializer.SerializeToElement(new string('x', 300 * 1024))) };
        Assert.Same(acceptance, await scope.ServiceProvider.GetRequiredService<IJobClient>().SubmitAsync(Scope, new("large-input", request)));
        Assert.Single(transport.Admissions);
    }

    [Fact]
    public async Task Payload_envelope_over_one_mib_is_refused_even_when_its_raw_value_is_below_the_limit()
    {
        var empty = new JobPayload(Support.InputSchema, JsonSerializer.SerializeToElement(string.Empty));
        var length = JobPayloadLimits.MaximumBytes - JobPayloadLimits.GetByteCount(empty) + 1;
        var payload = new JobPayload(Support.InputSchema, JsonSerializer.SerializeToElement(new string('x', length)));
        Assert.True(Encoding.UTF8.GetByteCount(payload.Value.GetRawText()) < JobPayloadLimits.MaximumBytes);
        var transport = new ScriptedJobTransport();
        await using var provider = Producer(transport);
        await using var scope = provider.CreateAsyncScope();
        var error = await Assert.ThrowsAsync<JobException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IJobClient>().SubmitAsync(Scope, new("oversized-envelope", Request() with { Input = payload })));
        Assert.Equal("jobs.payload.invalid", error.Code);
        Assert.Empty(transport.Admissions);
    }

    [Fact]
    public async Task Deep_producer_json_is_refused_with_safe_domain_error_before_admission()
    {
        var transport = new ScriptedJobTransport();
        await using var provider = Producer(transport);
        await using var scope = provider.CreateAsyncScope();
        var request = Request() with { Input = DeepPayload(Support.InputSchema, 80) };
        var error = await Assert.ThrowsAsync<JobException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IJobClient>().SubmitAsync(Scope, new("deep-input", request)));
        Assert.Equal("jobs.payload.invalid", error.Code);
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
        Assert.DoesNotContain("deep-secret-canary", error.Message);
        Assert.Empty(transport.Admissions);
    }

    [Fact]
    public void Host_payload_configuration_can_lower_but_cannot_exceed_the_shared_one_mib_envelope_limit()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddElsaJobs(options => options.MaximumPayloadBytes = JobPayloadLimits.MaximumBytes + 1));
        var lower = new ServiceCollection();
        lower.AddElsaJobs(options => options.MaximumPayloadBytes = 32_768);
        Assert.Equal(32_768, lower.Single(item => item.ServiceType == typeof(JobRuntimeSettings)).ImplementationInstance is JobRuntimeSettings settings
            ? settings.MaximumPayloadBytes : -1);
    }

    [Fact]
    public void Host_progress_configuration_can_lower_but_cannot_exceed_the_shared_sixteen_kib_envelope_limit()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddElsaJobs(options => options.MaximumProgressBytes = JobPayloadLimits.MaximumProgressBytes + 1));
        var lower = new ServiceCollection();
        lower.AddElsaJobs(options => options.MaximumProgressBytes = 8192);
        Assert.Equal(8192, lower.Single(item => item.ServiceType == typeof(JobRuntimeSettings)).ImplementationInstance is JobRuntimeSettings settings
            ? settings.MaximumProgressBytes : -1);
    }

    [Fact]
    public async Task Progress_accepts_the_shared_exact_envelope_boundary_and_refuses_one_more_byte_before_persistence()
    {
        var schema = new JobSchema("report-progress", 1);
        var empty = new JobProgress(.5, "Safe progress", new(schema, JsonSerializer.SerializeToElement(string.Empty)));
        var length = JobPayloadLimits.MaximumProgressBytes - JsonSerializer.SerializeToUtf8Bytes(empty).Length;
        var exact = empty with { Data = new(schema, JsonSerializer.SerializeToElement(new string('x', length))) };
        var oversized = empty with { Data = new(schema, JsonSerializer.SerializeToElement(new string('x', length + 1))) };
        Assert.Equal(JobPayloadLimits.MaximumProgressBytes, JsonSerializer.SerializeToUtf8Bytes(exact).Length);
        var control = new HandlerControl(async (input, context, cancellationToken) =>
        {
            await context.Progress.ReportAsync(exact, cancellationToken);
            var error = await Assert.ThrowsAsync<JobException>(async () => await context.Progress.ReportAsync(oversized, cancellationToken));
            Assert.Equal("jobs.progress.invalid", error.Code);
            return new ReportResult(input.Value);
        });
        await using var runtime = Runtime(control);
        runtime.Transport.Progress = (_, _, _) => ValueTask.FromResult(new JobTransition(JobCommandStatus.Applied, null, null, null));
        await runtime.Worker.StartAsync(CancellationToken.None);
        var receipt = await Wait(runtime.Transport.Completed.Task);
        Assert.Equal(JobAttemptOutcome.Succeeded, receipt.Outcome);
        Assert.Same(exact, Assert.Single(runtime.Transport.ProgressReports));
        await runtime.Worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Deep_progress_json_has_a_safe_domain_cause_and_never_reaches_persistence()
    {
        var progress = new JobProgress(.5, "Safe progress", DeepPayload(new("progress", 1), 80));
        var control = new HandlerControl(async (input, context, cancellationToken) =>
        {
            var error = await Assert.ThrowsAsync<JobException>(async () => await context.Progress.ReportAsync(progress, cancellationToken));
            Assert.Equal("jobs.payload.invalid", error.Code);
            Assert.IsAssignableFrom<JsonException>(error.InnerException);
            Assert.DoesNotContain("deep-secret-canary", error.Message);
            return new ReportResult(input.Value);
        });
        await using var runtime = Runtime(control);
        await runtime.Worker.StartAsync(CancellationToken.None);
        var receipt = await Wait(runtime.Transport.Completed.Task);
        Assert.Equal(JobAttemptOutcome.Succeeded, receipt.Outcome);
        Assert.Empty(runtime.Transport.ProgressReports);
        await runtime.Worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Producer_maps_replay_to_original_acceptance_and_conflict_to_domain_exception()
    {
        var accepted = new JobAcceptance("original-job", Now, Now.AddDays(30), 7);
        var transport = new ScriptedJobTransport
        {
            Admission = (admission, _) => ValueTask.FromResult(new JobAdmissionResult(
                admission.IdempotencyKey == "replay" ? JobCommandStatus.Replayed : JobCommandStatus.Conflict,
                admission.IdempotencyKey == "replay" ? accepted : null))
        };
        await using var provider = Producer(transport);
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IJobClient>();
        Assert.Same(accepted, await client.SubmitAsync(Scope, new("replay", Request())));
        var conflict = await Assert.ThrowsAsync<JobSubmissionConflictException>(async () =>
            await client.SubmitAsync(Scope, new("conflict", Request())));
        Assert.Equal("conflict", conflict.IdempotencyKey);
        Assert.All(transport.Admissions, admission =>
        {
            Assert.Equal(Scope, admission.Scope);
            Assert.Equal(JobRequestDigest.Compute(admission.Request), admission.RequestSha256);
            Assert.Null(admission.Origin);
        });
    }

    [Fact]
    public void Duplicate_handler_identity_is_rejected_even_when_the_schema_or_deployment_differs()
    {
        var services = new ServiceCollection();
        services.AddJobHandler<ControlledHandler, ReportInput, ReportResult>(Support);
        var conflict = Support with { InputSchema = new("report", 2), ImplementationIdentity = "deployment-b" };
        var error = Assert.Throws<JobException>(() => services.AddJobHandler<ControlledHandler, ReportInput, ReportResult>(conflict));
        Assert.Equal("jobs.handler.duplicate", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Startup_diagnoses_missing_or_duplicate_authorities_instead_of_selecting_the_last_provider(int commandProviders)
    {
        var services = new ServiceCollection();
        for (var index = 0; index < commandProviders; index++)
            services.AddSingleton<IJobCommands>(new ScriptedJobTransport());
        services.AddSingleton<IJobQueries>(new ScriptedJobTransport());
        services.AddElsaJobs();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var error = await Assert.ThrowsAsync<JobException>(() => provider.GetRequiredService<JobsConfigurationValidator>().StartAsync(CancellationToken.None));
        Assert.Equal("jobs.authority.invalid", error.Code);
    }

    [Fact]
    public async Task Plain_host_and_shell_startup_share_validation_and_both_reject_a_missing_provider()
    {
        var services = new ServiceCollection();
        services.AddElsaJobs();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        var startup = Assert.Single(provider.GetServices<IStartupTask>());
        Assert.Same(hosted, startup);
        var plainFailure = await Assert.ThrowsAsync<JobException>(() => hosted.StartAsync(CancellationToken.None));
        var shellFailure = await Assert.ThrowsAsync<JobException>(() => startup.ExecuteAsync(CancellationToken.None));
        Assert.Equal("jobs.authority.invalid", plainFailure.Code);
        Assert.Equal(plainFailure.Code, shellFailure.Code);
    }

    [Fact]
    public async Task Registry_invokes_typed_handler_and_requires_exact_schema_and_deployment_support()
    {
        var control = new HandlerControl((input, _, _) => ValueTask.FromResult(new ReportResult(input.Value + 1)));
        var services = new ServiceCollection();
        services.AddScoped(_ => new ControlledHandler(control));
        services.AddJobHandler<ControlledHandler, ReportInput, ReportResult>(Support);
        services.AddElsaJobs();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var registry = provider.GetRequiredService<JobHandlerRegistry>();
        var registration = registry.Resolve(Request(), "deployment-a");
        Assert.Equal(Support, Assert.Single(registry.Support));
        Assert.Equal("jobs.handler.unavailable", Assert.Throws<JobException>(() => registry.Resolve(Request(), "deployment-b")).Code);
        Assert.Throws<JobException>(() => registry.Resolve(Request() with { Input = new(new("report", 2), Request().Input.Value) }, "deployment-a"));
        Assert.Throws<JobException>(() => registry.Resolve(Request() with { ResultSchema = new("report-result", 2) }, "deployment-a"));
        await using var scope = provider.CreateAsyncScope();
        var payload = await registration.InvokeAsync(scope.ServiceProvider, Request().Input, Context(),
            provider.GetRequiredService<JobRuntimeSettings>(), CancellationToken.None);
        Assert.Equal(Support.ResultSchema, payload.Schema);
        Assert.Equal(42, payload.Value.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task Typed_input_serialization_boundary_preserves_original_failure_as_inner_exception()
    {
        var control = new HandlerControl((input, _, _) => ValueTask.FromResult(new ReportResult(input.Value)));
        var services = new ServiceCollection();
        services.AddScoped(_ => new ControlledHandler(control));
        services.AddJobHandler<ControlledHandler, ReportInput, ReportResult>(Support);
        services.AddElsaJobs();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var registration = provider.GetRequiredService<JobHandlerRegistry>().Resolve(Request(), "deployment-a");
        var invalid = new JobPayload(Support.InputSchema, JsonSerializer.SerializeToElement("invalid-input-canary"));
        var error = await Assert.ThrowsAsync<JobPayloadException>(async () =>
            await registration.InvokeAsync(scope.ServiceProvider, invalid, Context(), provider.GetRequiredService<JobRuntimeSettings>(), CancellationToken.None));
        Assert.Equal("jobs.input.invalid", error.Code);
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
    }

    [Fact]
    public async Task Result_serialization_boundary_preserves_inner_cause_and_worker_diagnostic_does_not_disclose_it()
    {
        var control = new HandlerControl((input, _, _) => ValueTask.FromResult(new ReportResult(input.Value)));
        await using var runtime = Runtime(control, options => options.MaximumPayloadBytes = 1);
        // Invoke the registered typed serializer directly: the worker also rejects an oversized stored
        // request, which would be a different boundary from result serialization.
        await using var scope = runtime.Provider.CreateAsyncScope();
        var registration = runtime.Provider.GetRequiredService<JobHandlerRegistry>().Resolve(Request(), "deployment-a");
        var error = await Assert.ThrowsAsync<JobPayloadException>(async () =>
            await registration.InvokeAsync(scope.ServiceProvider, Request().Input, Context(),
                runtime.Provider.GetRequiredService<JobRuntimeSettings>(), CancellationToken.None));
        Assert.Equal("jobs.result.invalid", error.Code);
        Assert.IsType<JobException>(error.InnerException);
        Assert.Equal("jobs.payload.invalid", ((JobException)error.InnerException!).Code);

        var secretFailure = new HandlerControl((_, _, _) => throw new InvalidOperationException("exception-secret-canary"));
        await using var failed = Runtime(secretFailure);
        await failed.Worker.StartAsync(CancellationToken.None);
        var receipt = await Wait(failed.Transport.Completed.Task);
        Assert.Equal("jobs.handler.failed", receipt.Diagnostic!.Code);
        Assert.DoesNotContain("exception-secret-canary", JsonSerializer.Serialize(receipt));
        await failed.Worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Result_envelope_over_one_mib_becomes_a_small_nonretryable_failure_receipt()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var empty = new JobPayload(Support.ResultSchema, JsonSerializer.SerializeToElement(new ReportResult(41, string.Empty), json));
        var length = JobPayloadLimits.MaximumBytes - JobPayloadLimits.GetByteCount(empty) + 1;
        var value = new ReportResult(41, new string('x', length));
        var oversized = new JobPayload(Support.ResultSchema, JsonSerializer.SerializeToElement(value, json));
        Assert.Equal(JobPayloadLimits.MaximumBytes + 1, JobPayloadLimits.GetByteCount(oversized));
        Assert.True(Encoding.UTF8.GetByteCount(oversized.Value.GetRawText()) < JobPayloadLimits.MaximumBytes);
        var control = new HandlerControl((_, _, _) => ValueTask.FromResult(value));
        await using var runtime = Runtime(control);
        await runtime.Worker.StartAsync(CancellationToken.None);
        var receipt = await Wait(runtime.Transport.Completed.Task);
        Assert.Equal(JobAttemptOutcome.Failed, receipt.Outcome);
        Assert.False(receipt.Retryable);
        Assert.Null(receipt.Payload);
        Assert.Equal("jobs.result.invalid", receipt.Diagnostic!.Code);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(receipt).Length < 4096);
        Assert.Single(runtime.Transport.Completions);
        await runtime.Worker.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public async Task Result_payload_depth_reserves_headroom_for_its_actual_completion_envelope(int arrays, bool supported)
    {
        var value = new ReportResult(41, null, DeepPayload(Support.ResultSchema, arrays).Value);
        var control = new HandlerControl((_, _, _) => ValueTask.FromResult(value));
        await using var runtime = Runtime(control);
        await runtime.Worker.StartAsync(CancellationToken.None);
        var receipt = await Wait(runtime.Transport.Completed.Task);
        if (supported)
        {
            Assert.Equal(JobAttemptOutcome.Succeeded, receipt.Outcome);
            Assert.NotNull(receipt.Payload);
        }
        else
        {
            Assert.Equal(JobAttemptOutcome.Failed, receipt.Outcome);
            Assert.False(receipt.Retryable);
            Assert.Null(receipt.Payload);
            Assert.Equal("jobs.result.invalid", receipt.Diagnostic!.Code);
        }
        Assert.NotEmpty(JsonSerializer.SerializeToUtf8Bytes(receipt));
        await runtime.Worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Handler_idempotency_identity_covers_partition_tenant_and_job_but_not_attempt()
    {
        async Task<string> Key(JobScope scope, int attempt)
        {
            var entered = NewSignal<JobExecutionContext>();
            var control = new HandlerControl((input, context, _) =>
            {
                entered.TrySetResult(context);
                return ValueTask.FromResult(new ReportResult(input.Value));
            });
            await using var runtime = Runtime(control, jobScope: scope, attemptNumber: attempt);
            await runtime.Worker.StartAsync(CancellationToken.None);
            var context = await Wait(entered.Task);
            await Wait(runtime.Transport.Completed.Task);
            await runtime.Worker.StopAsync(CancellationToken.None);
            return context.IdempotencyKey;
        }
        var first = await Key(Scope, 1);
        Assert.Equal(first, await Key(Scope, 2));
        Assert.NotEqual(first, await Key(Scope with { TenantId = "tenant-two" }, 1));
        Assert.NotEqual(first, await Key(Scope with { PersistencePartition = "other-partition" }, 1));
        Assert.NotEqual(await Key(Scope with { TenantId = null }, 1), await Key(Scope with { TenantId = "null" }, 1));
        Assert.StartsWith("elsa.jobs.invocation.v1:", first);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Unavailable_or_unreadable_retained_request_never_enters_handler_or_selects_a_fabricated_failure(bool readableRequest, bool diagnostic)
    {
        var entered = 0;
        var returnedToPolling = NewSignal<bool>();
        var control = new HandlerControl((input, _, _) =>
        {
            Interlocked.Increment(ref entered);
            return ValueTask.FromResult(new ReportResult(input.Value));
        });
        await using var runtime = Runtime(control);
        var originalFind = runtime.Transport.Find!;
        runtime.Transport.Find = async (scope, jobId, cancellationToken) =>
        {
            var snapshot = await originalFind(scope, jobId, cancellationToken)
                ?? throw new InvalidOperationException("The test script requires a retained snapshot.");
            return snapshot with
            {
                Request = readableRequest ? snapshot.Request : null,
                UnavailableDiagnostic = diagnostic ? new("jobs.request.unavailable", "The retained request is unavailable.") : null
            };
        };
        var originalClaim = runtime.Transport.Claim!;
        runtime.Transport.Claim = async (request, cancellationToken) =>
        {
            var response = await originalClaim(request, cancellationToken);
            if (response.Status == JobCommandStatus.NoEligibleWork) returnedToPolling.TrySetResult(true);
            return response;
        };
        await runtime.Worker.StartAsync(CancellationToken.None);
        await Wait(returnedToPolling.Task);
        Assert.Equal(0, Volatile.Read(ref entered));
        Assert.Empty(runtime.Transport.Renewals);
        Assert.Empty(runtime.Transport.Completions);
        await runtime.Worker.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("digest")]
    [InlineData("lease")]
    public async Task Snapshot_identity_mismatch_preserves_work_without_entering_handler_or_fabricating_a_failure(string mismatch)
    {
        var entered = 0;
        var returnedToPolling = NewSignal<bool>();
        var control = new HandlerControl((input, _, _) =>
        {
            Interlocked.Increment(ref entered);
            return ValueTask.FromResult(new ReportResult(input.Value));
        });
        await using var runtime = Runtime(control);
        var originalFind = runtime.Transport.Find!;
        runtime.Transport.Find = async (scope, jobId, cancellationToken) =>
        {
            var snapshot = await originalFind(scope, jobId, cancellationToken)
                ?? throw new InvalidOperationException("The test requires a retained snapshot.");
            return mismatch switch
            {
                "scope" => snapshot with { Scope = scope with { TenantId = "another-tenant" } },
                "digest" => snapshot with { RequestSha256 = "another-request-digest" },
                _ => snapshot with { ActiveAttempt = snapshot.ActiveAttempt! with { Revision = 99 } }
            };
        };
        var originalClaim = runtime.Transport.Claim!;
        runtime.Transport.Claim = async (request, cancellationToken) =>
        {
            var response = await originalClaim(request, cancellationToken);
            if (response.Status == JobCommandStatus.NoEligibleWork) returnedToPolling.TrySetResult(true);
            return response;
        };
        await runtime.Worker.StartAsync(CancellationToken.None);
        await Wait(returnedToPolling.Task);
        Assert.Equal(0, Volatile.Read(ref entered));
        Assert.Empty(runtime.Transport.Renewals);
        Assert.Empty(runtime.Transport.Completions);
        await runtime.Worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Deep_retained_request_refuses_physical_entry_without_fabricating_a_handler_failure()
    {
        var entered = 0;
        var returnedToPolling = NewSignal<bool>();
        var control = new HandlerControl((input, _, _) =>
        {
            Interlocked.Increment(ref entered);
            return ValueTask.FromResult(new ReportResult(input.Value));
        });
        await using var runtime = Runtime(control);
        var originalFind = runtime.Transport.Find!;
        runtime.Transport.Find = async (scope, jobId, cancellationToken) =>
        {
            var snapshot = await originalFind(scope, jobId, cancellationToken)
                ?? throw new InvalidOperationException("The test requires a retained snapshot.");
            return snapshot with { Request = Request() with { Input = DeepPayload(Support.InputSchema, 80) }, RequestSha256 = "unreadable-request-digest" };
        };
        var originalClaim = runtime.Transport.Claim!;
        runtime.Transport.Claim = async (request, cancellationToken) =>
        {
            var response = await originalClaim(request, cancellationToken);
            if (response.Status == JobCommandStatus.NoEligibleWork) returnedToPolling.TrySetResult(true);
            return response;
        };
        await runtime.Worker.StartAsync(CancellationToken.None);
        await Wait(returnedToPolling.Task);
        Assert.Equal(0, Volatile.Read(ref entered));
        Assert.Empty(runtime.Transport.Renewals);
        Assert.Empty(runtime.Transport.Completions);
        await runtime.Worker.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uncertain_claim_replays_the_same_pull_identity_and_enters_the_physical_handler_once(bool explicitUnavailable)
    {
        var pollInstalled = NewSignal<bool>();
        var entered = 0;
        var starts = 0;
        var control = new HandlerControl((input, _, _) =>
        {
            Interlocked.Increment(ref entered);
            return ValueTask.FromResult(new ReportResult(input.Value));
        });
        await using var runtime = Runtime(control, observeTimer: due =>
        {
            if (due == TimeSpan.FromMilliseconds(250)) pollInstalled.TrySetResult(true);
        });
        var originalClaim = runtime.Transport.Claim!;
        var calls = 0;
        JobClaimRequest? firstRequest = null;
        JobAttemptLease? committedLease = null;
        runtime.Transport.Claim = async (request, cancellationToken) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                firstRequest = request;
                committedLease = (await originalClaim(request, cancellationToken)).Lease;
                Assert.False(string.IsNullOrWhiteSpace(request.RequestId));
                if (explicitUnavailable) return new(JobCommandStatus.Unavailable, null);
                throw new IOException("test-lost-claim-acknowledgement");
            }
            if (call == 2)
            {
                Assert.Same(firstRequest, request);
                Assert.Equal(firstRequest!.RequestId, request.RequestId);
                return new(JobCommandStatus.Replayed, committedLease);
            }
            return await originalClaim(request, cancellationToken);
        };
        var originalStart = runtime.Transport.Start!;
        runtime.Transport.Start = (lease, cancellationToken) =>
        {
            Interlocked.Increment(ref starts);
            return originalStart(lease, cancellationToken);
        };
        await runtime.Worker.StartAsync(CancellationToken.None);
        await Wait(pollInstalled.Task);
        runtime.Clock.Advance(TimeSpan.FromMilliseconds(250));
        await Wait(runtime.Transport.Completed.Task);
        Assert.Equal(1, Volatile.Read(ref entered));
        Assert.Equal(1, Volatile.Read(ref starts));
        Assert.Equal(1, Assert.Single(runtime.Transport.Completions).Fence);
        Assert.Same(runtime.Transport.Claims.ElementAt(0), runtime.Transport.Claims.ElementAt(1));
        await runtime.Worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Lost_renewal_acknowledgement_replays_the_original_token_before_entering_the_handler()
    {
        var retryInstalled = NewSignal<bool>();
        var entered = 0;
        var control = new HandlerControl((input, _, _) =>
        {
            Interlocked.Increment(ref entered);
            return ValueTask.FromResult(new ReportResult(input.Value));
        });
        await using var runtime = Runtime(control, observeTimer: due =>
        {
            if (due == TimeSpan.FromMilliseconds(250)) retryInstalled.TrySetResult(true);
        });
        JobAttemptLease? originalToken = null;
        JobAttemptLease? renewedToken = null;
        var extensionCount = 0;
        runtime.Transport.Renewal = (lease, _, _) =>
        {
            if (runtime.Transport.Renewals.Count == 1)
            {
                originalToken = lease;
                renewedToken = lease with { Revision = lease.Revision + 1, ExpiresAt = lease.ExpiresAt.AddSeconds(1) };
                Interlocked.Increment(ref extensionCount);
                throw new IOException("test-lost-renewal-acknowledgement");
            }
            Assert.Same(originalToken, lease);
            return ValueTask.FromResult(new JobRenewal(JobCommandStatus.Replayed, renewedToken, false, false));
        };
        var originalCompletion = runtime.Transport.Completion!;
        runtime.Transport.Completion = (lease, receipt, cancellationToken) =>
        {
            Assert.Same(renewedToken, lease);
            return originalCompletion(lease, receipt, cancellationToken);
        };
        await runtime.Worker.StartAsync(CancellationToken.None);
        await Wait(retryInstalled.Task);
        runtime.Clock.Advance(TimeSpan.FromMilliseconds(250));
        await Wait(runtime.Transport.Completed.Task);
        Assert.Equal(1, Volatile.Read(ref entered));
        Assert.Equal(1, Volatile.Read(ref extensionCount));
        Assert.Equal(2, runtime.Transport.Renewals.Count);
        Assert.Same(runtime.Transport.Renewals.ElementAt(0), runtime.Transport.Renewals.ElementAt(1));
        await runtime.Worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Unconfirmed_renewal_cancels_cooperatively_and_retains_the_physical_slot_until_exit_with_only_late_evidence()
    {
        var entered = NewSignal<bool>();
        var retryInstalled = NewSignal<bool>();
        var signalled = NewSignal<bool>();
        var release = NewSignal<bool>();
        var control = new HandlerControl(async (_, _, cancellationToken) =>
        {
            using var registration = cancellationToken.Register(() => signalled.TrySetResult(true));
            entered.TrySetResult(true);
            await release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return new ReportResult(0);
        });
        await using var runtime = Runtime(control, observeTimer: due =>
        {
            if (due == TimeSpan.FromMilliseconds(250)) retryInstalled.TrySetResult(true);
        });
        runtime.Transport.Renewal = (lease, _, _) => ValueTask.FromResult(runtime.Transport.Renewals.Count == 1
            ? new JobRenewal(JobCommandStatus.Applied, lease with { Revision = lease.Revision + 1 }, false, false)
            : new JobRenewal(JobCommandStatus.Unavailable, null, false, false));
        runtime.Transport.PhysicalReceipt = (_, _, _, _) => ValueTask.FromResult(new JobTransition(JobCommandStatus.Applied, null, null, null));
        try
        {
            await runtime.Worker.StartAsync(CancellationToken.None);
            await Wait(entered.Task);
            runtime.Clock.Advance(TimeSpan.FromSeconds(1));
            await Wait(retryInstalled.Task);
            runtime.Clock.Advance(TimeSpan.FromMilliseconds(250));
            await Wait(signalled.Task);
            var stopping = runtime.Worker.StopAsync(CancellationToken.None);
            Assert.False(stopping.IsCompleted);
            Assert.Single(runtime.Transport.Claims);
            Assert.Empty(runtime.Transport.Completions);
            Assert.Equal(3, runtime.Transport.Renewals.Count);
            Assert.Same(runtime.Transport.Renewals.ElementAt(1), runtime.Transport.Renewals.ElementAt(2));
            release.TrySetResult(true);
            var receipt = await Wait(runtime.Transport.ReceiptRecorded.Task);
            Assert.Equal(JobAttemptOutcome.Abandoned, receipt.Outcome);
            Assert.Empty(runtime.Transport.Completions);
            await Wait(stopping);
        }
        finally { release.TrySetResult(true); }
    }

    [Fact]
    public async Task Grace_expiry_requests_cooperative_abort_without_authorizing_job_cancellation()
    {
        var entered = NewSignal<bool>();
        var control = new HandlerControl(async (_, _, cancellationToken) =>
        {
            entered.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new ReportResult(0);
        });
        await using var runtime = Runtime(control);
        using var activation = new CancellationTokenSource();
        await runtime.Worker.StartAsync(activation.Token);
        await Wait(entered.Task);
        activation.Cancel();
        Assert.False(runtime.Transport.Completed.Task.IsCompleted);
        var stopping = runtime.Worker.StopAsync(CancellationToken.None);
        var receipt = await Wait(runtime.Transport.Completed.Task);
        Assert.Equal(JobAttemptOutcome.Abandoned, receipt.Outcome);
        Assert.True(receipt.Retryable);
        Assert.Equal("jobs.worker.stopped", receipt.Diagnostic!.Code);
        await Wait(stopping);
    }

    [Theory]
    [InlineData(true, JobAttemptOutcome.TimedOut)]
    [InlineData(false, JobAttemptOutcome.Cancelled)]
    public async Task Provider_stop_signal_keeps_the_physical_slot_until_the_handler_actually_exits(bool timeout, JobAttemptOutcome expected)
    {
        var entered = NewSignal<bool>();
        var signalled = NewSignal<bool>();
        var release = NewSignal<bool>();
        var control = new HandlerControl(async (_, _, cancellationToken) =>
        {
            using var registration = cancellationToken.Register(() => signalled.TrySetResult(true));
            entered.TrySetResult(true);
            await release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return new ReportResult(0);
        });
        await using var runtime = Runtime(control);
        runtime.Transport.Renewal = (lease, _, _) =>
        {
            var reached = runtime.Transport.Renewals.Count >= 2;
            return ValueTask.FromResult(new JobRenewal(JobCommandStatus.Applied, lease with { Revision = lease.Revision + 1 },
                reached && !timeout, reached && timeout));
        };
        try
        {
            await runtime.Worker.StartAsync(CancellationToken.None);
            await Wait(entered.Task);
            runtime.Clock.Advance(timeout ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(1));
            await Wait(signalled.Task);
            var stopping = runtime.Worker.StopAsync(CancellationToken.None);
            Assert.False(stopping.IsCompleted);
            Assert.Empty(runtime.Transport.Completions);
            Assert.Single(runtime.Transport.Claims);
            release.TrySetResult(true);
            var receipt = await Wait(runtime.Transport.Completed.Task);
            Assert.Equal(expected, receipt.Outcome);
            await Wait(stopping);
        }
        finally { release.TrySetResult(true); }
    }

    private static JobRequest Request() => new(Support.Handler,
        new(Support.InputSchema, JsonSerializer.SerializeToElement(new { value = 41 })), Support.ResultSchema, "default",
        new(new(3, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), 2, 0, true), TimeSpan.FromSeconds(3), TimeSpan.FromDays(30)), null, null);

    private static JobExecutionContext Context() => new(Scope, "job-one", "attempt-one", 1, "stable-key", new UnusedProgress());

    private static JobPayload DeepPayload(JobSchema schema, int arrays)
    {
        using var document = JsonDocument.Parse(new string('[', arrays) + "\"deep-secret-canary\"" + new string(']', arrays),
            new JsonDocumentOptions { MaxDepth = 128 });
        return new(schema, document.RootElement);
    }

    private static ServiceProvider Producer(ScriptedJobTransport transport, Action<JobsOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJobCommands>(transport);
        services.AddSingleton<IJobQueries>(transport);
        services.AddElsaJobs(configure);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static RuntimeHarness Runtime(HandlerControl control, Action<JobsOptions>? configure = null,
        JobScope? jobScope = null, int attemptNumber = 1, Action<TimeSpan>? observeTimer = null)
    {
        var selectedScope = jobScope ?? Scope;
        var clock = new FakeTimeProvider(Now);
        var transport = new ScriptedJobTransport();
        var claimNumber = 0;
        JobAttemptLease? selectedLease = null;
        transport.Claim = (request, _) =>
        {
            if (Interlocked.Increment(ref claimNumber) != 1)
                return ValueTask.FromResult(new JobClaimResult(JobCommandStatus.NoEligibleWork, null));
            selectedLease = new(selectedScope, "same-job", "attempt-" + attemptNumber, attemptNumber, request.Owner, 1, 1,
                Now, Now.AddSeconds(9), Now.AddSeconds(3), "deployment-a");
            Assert.Equal(selectedScope, request.Scope);
            Assert.Equal(Support, Assert.Single(request.Handlers));
            return ValueTask.FromResult(new JobClaimResult(JobCommandStatus.Applied, selectedLease));
        };
        transport.Find = (scope, jobId, _) => ValueTask.FromResult<JobSnapshot?>(new(scope, jobId, JobRequestDigest.Compute(Request()),
            Request(), JobState.AttemptActive, 1, Now, Now, selectedLease, null, null, false, null));
        transport.Renewal = (lease, _, _) => ValueTask.FromResult(new JobRenewal(JobCommandStatus.Applied,
            lease with { Revision = lease.Revision + 1 }, false, false));
        transport.Start = (_, _) => ValueTask.FromResult(new JobTransition(JobCommandStatus.Applied, null, null, null));
        transport.Completion = (_, _, _) => ValueTask.FromResult(new JobTransition(JobCommandStatus.Applied, null, null, null));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(observeTimer is null ? clock : new ObservingTimeProvider(clock, observeTimer));
        services.AddSingleton<IJobCommands>(transport);
        services.AddSingleton<IJobQueries>(transport);
        services.AddScoped(_ => new ControlledHandler(control));
        services.AddJobHandler<ControlledHandler, ReportInput, ReportResult>(Support);
        services.AddElsaJobs(options =>
        {
            options.EnableWorker = true;
            options.MaterializeSchedules = false;
            options.WorkerScopes.Add(selectedScope);
            options.MaximumConcurrentAttempts = 1;
            options.LeaseDuration = TimeSpan.FromSeconds(9);
            options.RenewalInterval = TimeSpan.FromSeconds(1);
            options.ShutdownGracePeriod = TimeSpan.Zero;
            configure?.Invoke(options);
        });
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return new(provider, provider.GetRequiredService<JobsWorker>(), transport, clock);
    }

    private static TaskCompletionSource<T> NewSignal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task<T> Wait<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(10));
    private static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10));

    private sealed record ReportInput(int Value);
    private sealed record ReportResult(int Value, string? Text = null, JsonElement? Data = null);
    private sealed record HandlerControl(Func<ReportInput, JobExecutionContext, CancellationToken, ValueTask<ReportResult>> Execute);
    private sealed class ControlledHandler(HandlerControl control) : IJobHandler<ReportInput, ReportResult>
    {
        public ValueTask<ReportResult> ExecuteAsync(ReportInput input, JobExecutionContext context, CancellationToken cancellationToken) =>
            control.Execute(input, context, cancellationToken);
    }

    private sealed class UnusedProgress : IJobProgressReporter
    {
        public ValueTask ReportAsync(JobProgress progress, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This script does not report progress.");
    }

    private sealed record RuntimeHarness(ServiceProvider Provider, JobsWorker Worker, ScriptedJobTransport Transport, FakeTimeProvider Clock) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await Provider.DisposeAsync();
        }
    }
}

/// <summary>Scripted protocol responses only: no durable state, admission, retry, recovery or result-selection algorithm.</summary>
internal sealed class ScriptedJobTransport : IJobCommands, IJobQueries
{
    public ValueTask<JobPayloadCleanupResult> ExpirePayloadsAsync(JobPayloadCleanupRequest request, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobPage<JobPhysicalReceiptInspection>> ListPhysicalReceiptInspectionsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken = default) => throw Unexpected();
    internal Func<JobAdmission, CancellationToken, ValueTask<JobAdmissionResult>>? Admission { get; init; }
    internal Func<JobClaimRequest, CancellationToken, ValueTask<JobClaimResult>>? Claim { get; set; }
    internal Func<JobAttemptLease, TimeSpan, CancellationToken, ValueTask<JobRenewal>>? Renewal { get; set; }
    internal Func<JobAttemptLease, CancellationToken, ValueTask<JobTransition>>? Start { get; set; }
    internal Func<JobAttemptLease, JobPhysicalReceipt, CancellationToken, ValueTask<JobTransition>>? Completion { get; set; }
    internal Func<JobScope, string, JobPhysicalReceipt, CancellationToken, ValueTask<JobTransition>>? PhysicalReceipt { get; set; }
    internal Func<JobAttemptLease, JobProgress, CancellationToken, ValueTask<JobTransition>>? Progress { get; set; }
    internal Func<JobScope, string, CancellationToken, ValueTask<JobSnapshot?>>? Find { get; set; }
    internal Func<JobMaterializationRequest, CancellationToken, ValueTask<JobMaterializationResult>>? Materialization { get; set; }
    internal ConcurrentQueue<JobAdmission> Admissions { get; } = new();
    internal ConcurrentQueue<JobClaimRequest> Claims { get; } = new();
    internal ConcurrentQueue<JobAttemptLease> Renewals { get; } = new();
    internal ConcurrentQueue<JobPhysicalReceipt> Completions { get; } = new();
    internal ConcurrentQueue<JobProgress> ProgressReports { get; } = new();
    internal ConcurrentQueue<JobMaterializationRequest> Materializations { get; } = new();
    internal TaskCompletionSource<JobPhysicalReceipt> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<JobPhysicalReceipt> ReceiptRecorded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask<JobAdmissionResult> AdmitAsync(JobAdmission admission, CancellationToken cancellationToken = default)
    {
        Admissions.Enqueue(admission);
        return (Admission ?? throw Unexpected())(admission, cancellationToken);
    }
    public ValueTask<JobClaimResult> ClaimAsync(JobClaimRequest request, CancellationToken cancellationToken = default)
    {
        Claims.Enqueue(request);
        return (Claim ?? throw Unexpected())(request, cancellationToken);
    }
    public ValueTask<JobRenewal> RenewAsync(JobAttemptLease lease, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        Renewals.Enqueue(lease);
        return (Renewal ?? throw Unexpected())(lease, leaseDuration, cancellationToken);
    }
    public ValueTask<JobTransition> RecordPhysicalStartAsync(JobAttemptLease lease, CancellationToken cancellationToken = default) =>
        (Start ?? throw Unexpected())(lease, cancellationToken);
    public async ValueTask<JobTransition> CompleteAsync(JobAttemptLease lease, JobPhysicalReceipt receipt, CancellationToken cancellationToken = default)
    {
        var result = await (Completion ?? throw Unexpected())(lease, receipt, cancellationToken);
        Completions.Enqueue(receipt);
        Completed.TrySetResult(receipt);
        return result;
    }
    public ValueTask<JobSnapshot?> FindAsync(JobScope scope, string jobId, CancellationToken cancellationToken = default) =>
        (Find ?? throw Unexpected())(scope, jobId, cancellationToken);

    private static InvalidOperationException Unexpected() => new("The test did not script this protocol call.");
    public async ValueTask<JobTransition> RecordPhysicalReceiptAsync(JobScope scope, string jobId, JobPhysicalReceipt receipt, CancellationToken cancellationToken = default)
    {
        var response = await (PhysicalReceipt ?? throw Unexpected())(scope, jobId, receipt, cancellationToken);
        ReceiptRecorded.TrySetResult(receipt);
        return response;
    }
    public async ValueTask<JobTransition> RecordProgressAsync(JobAttemptLease lease, JobProgress progress, CancellationToken cancellationToken = default)
    {
        var response = await (Progress ?? throw Unexpected())(lease, progress, cancellationToken);
        ProgressReports.Enqueue(progress);
        return response;
    }
    public ValueTask<JobTransition> CancelAsync(JobCancelRequest request, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobTransition> CancelOriginAsync(JobCancelOriginRequest request, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobAdmissionResult> RetryAsNewRunAsync(JobRetryAsNewRunRequest request, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobTransition> PauseAsync(JobPauseRequest request, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobTransition> ReleaseHoldAsync(JobReleaseHoldRequest request, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobTransition> SetCapacityAsync(JobSetCapacityRequest request, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobTransition> SaveScheduleAsync(JobSaveScheduleRequest request, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobTransition> SetScheduleEnabledAsync(JobSetScheduleEnabledRequest request, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobMaterializationResult> MaterializeDueAsync(JobMaterializationRequest request, CancellationToken cancellationToken = default)
    {
        Materializations.Enqueue(request);
        return (Materialization ?? throw Unexpected())(request, cancellationToken);
    }
    public ValueTask<JobContinuationResult> GrantContinuationAsync(JobContinuationRequest request, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobContinuationPermission?> FindContinuationAsync(JobScope scope, string jobId, string continuationId, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobDeliveryStatusSnapshot?> FindDeliveryAsync(JobScope scope, string jobId, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobOriginSnapshot?> FindOriginAsync(JobScope scope, JobOrigin origin, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobOperationAuditSnapshot?> FindOperationAuditAsync(JobScope scope, string operationId, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobPage<JobOperationAuditSnapshot>> ListOperationAuditsAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobTransition> AcknowledgeDeliveryAsync(JobAcknowledgeDeliveryRequest request, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobPage<JobSnapshot>> ListJobsAsync(JobScope scope, JobListQuery query, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobPage<JobAttemptSnapshot>> ListAttemptsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobPage<JobPhysicalReceipt>> ListPhysicalReceiptsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobPage<JobHold>> ListHoldsAsync(JobScope scope, JobGateKey? gate, JobPageRequest page, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobPage<JobCapacity>> ListCapacityAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobScheduleSnapshot?> FindScheduleAsync(JobScope scope, string scheduleId, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobPage<JobScheduleSnapshot>> ListSchedulesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobPage<JobDeliverySnapshot>> ListPendingDeliveriesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) => throw Unexpected();
    public ValueTask<JobPage<JobQueueSnapshot>> ListQueuesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) => throw Unexpected();
}
