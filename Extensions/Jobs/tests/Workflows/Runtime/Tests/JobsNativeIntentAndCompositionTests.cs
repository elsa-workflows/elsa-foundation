using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Jobs.Workflows.Runtime.Tests;

public sealed class JobsNativeIntentAndCompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_source_or_late_start_of_a_cancelled_activity_settles_the_exact_origin_without_admission(bool explicitCancel)
    {
        var (saved, admission, original) = await NativeFixture.SavedAsync();
        var activity = saved with { Status = ActivityExecutionStatus.Cancelled, BookmarkIds = [], TriggerRegistrations = [], PrivateState = null };
        var activities = new ActivityStore(); activities.States.Add(activity.InvocationId, activity);
        var transport = new JobTransport();
        var handler = new JobsNativeIntentHandler(transport, transport, new WorkflowStore(NativeFixture.Parent()), activities,
            new BookmarkStore(NativeFixture.Bookmark(saved)), new Access(), [], null!);
        await handler.HandleIntentAsync(explicitCancel ? JobsNativeBoundary.Intent(admission, cancel: true) : original);
        var cancellation = Assert.Single(transport.Cancellations);
        Assert.Equal(admission.Origin, cancellation.Origin); Assert.Equal(admission.RequestSha256, cancellation.RequestSha256);
        Assert.Empty(transport.Admissions);
    }

    [Fact]
    public async Task Wrong_parent_material_or_tenant_cannot_submit_the_saved_job()
    {
        var (saved, _, intent) = await NativeFixture.SavedAsync();
        var activities = new ActivityStore(); activities.States.Add(saved.InvocationId, saved);
        var transport = new JobTransport();
        foreach (var parent in new[] { NativeFixture.Parent() with { TenantId = "different-tenant" },
                     NativeFixture.Parent() with { PinnedExecutable = NativeFixture.Executable with { ArtifactHash = new('f', 64) } } })
        {
            var handler = new JobsNativeIntentHandler(transport, transport, new WorkflowStore(parent), activities,
                new BookmarkStore(NativeFixture.Bookmark(saved)), new Access(), [], null!);
            var error = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(() => handler.HandleIntentAsync(intent).AsTask());
            Assert.Equal(PostCommitFailureKind.Permanent, error.Kind);
        }
        Assert.Empty(transport.Admissions); Assert.Empty(transport.Cancellations);
    }

    [Fact]
    public async Task Exact_cancelled_origin_replay_confirms_start_after_native_cleanup_but_unknown_sender_remains_owed()
    {
        var (saved, admission, start) = await NativeFixture.SavedAsync();
        var transport = new JobTransport();
        var handler = new JobsNativeIntentHandler(transport, transport, new WorkflowStore(NativeFixture.Parent()) { Missing = true }, new ActivityStore(),
            new BookmarkStore(NativeFixture.Bookmark(saved)), new Access(), [], null!);
        await handler.HandleIntentAsync(JobsNativeBoundary.Intent(admission, cancel: true));
        Assert.Equal(admission.Origin, Assert.Single(transport.Cancellations).Origin);
        await handler.HandleIntentAsync(start);
        transport.Origin = null;
        var unavailable = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(() => handler.HandleIntentAsync(start).AsTask());
        Assert.Equal(PostCommitFailureKind.Transient, unavailable.Kind); Assert.Empty(transport.Admissions);
    }

    [Fact]
    public async Task Admission_uses_captured_values_and_the_same_protected_source_on_sender_replay()
    {
        var (saved, admission, intent) = await NativeFixture.SavedAsync();
        var activities = new ActivityStore(); activities.States.Add(saved.InvocationId, saved);
        var transport = new JobTransport();
        var handler = new JobsNativeIntentHandler(transport, transport, new WorkflowStore(NativeFixture.Parent()), activities,
            new BookmarkStore(NativeFixture.Bookmark(saved)), new Access(), [], null!);
        await handler.HandleIntentAsync(intent); await handler.HandleIntentAsync(intent);
        Assert.Single(transport.Admissions);
        Assert.All(transport.Admissions, value =>
        {
            Assert.Equal(admission.Origin, value.Origin); Assert.Equal(admission.ProducerKey, value.IdempotencyKey);
            Assert.Equal(admission.ProposedJobId, value.ProposedJobId); Assert.Equal(admission.RequestSha256, value.RequestSha256);
            Assert.Equal(42, value.Request.Input.Value.GetProperty("Count").GetInt32());
        });
    }

    [Fact]
    public async Task Known_canonical_acceptance_confirms_lost_start_reply_after_the_native_wait_has_retired()
    {
        var (saved, admission, start) = await NativeFixture.SavedAsync(); var (transport, _) = NativeFixture.Selected(admission);
        var handler = new JobsNativeIntentHandler(transport, transport, new WorkflowStore(NativeFixture.Parent()) { Missing = true }, new ActivityStore(),
            new BookmarkStore(NativeFixture.Bookmark(saved)), new Access(), [], null!);
        await handler.HandleIntentAsync(start);
        Assert.NotEqual(admission.ProposedJobId, transport.Origin!.Acceptance!.JobId);
        Assert.Empty(transport.Admissions); Assert.Empty(transport.Cancellations);
    }

    [Fact]
    public async Task Coordinator_registration_has_no_handler_dependency_and_shares_exactly_one_delivery_lifecycle()
    {
        var services = new ServiceCollection(); JobWorkflowRuntimeOptions? retained = null;
        services.AddJobsWorkflowsRuntime(options => { retained = options; options.EnableResultDelivery = true; options.DeliveryScopes.Add(NativeFixture.Scope); });
        retained!.DeliveryScopes.Clear(); retained.EnableResultDelivery = false; retained.PollInterval = TimeSpan.FromSeconds(50);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var settings = provider.GetRequiredService<JobWorkflowRuntimeSettings>();
        Assert.Equal(NativeFixture.Scope, Assert.Single(settings.Scopes)); Assert.Equal(TimeSpan.FromMilliseconds(250), settings.PollInterval);
        var worker = provider.GetRequiredService<JobsResultDeliveryWorker>();
        Assert.Same(worker, Assert.Single(provider.GetServices<IBackgroundTask>()));
        Assert.Same(worker, Assert.Single(provider.GetServices<IHostedService>().OfType<JobsResultDeliveryWorker>()));
    }

    [Fact]
    public async Task Hooks_only_registration_starts_no_delivery_loop_and_duplicate_composition_is_refused()
    {
        var services = new ServiceCollection(); services.AddJobsWorkflowsRuntime();
        Assert.Throws<InvalidOperationException>(() => services.AddJobsWorkflowsRuntime());
        await using var provider = services.BuildServiceProvider();
        Assert.Empty(provider.GetServices<IBackgroundTask>()); Assert.Null(provider.GetService<JobsResultDeliveryWorker>());
    }

    [Fact]
    public void Enabled_delivery_requires_explicit_distinct_trusted_scopes()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddJobsWorkflowsRuntime(options => options.EnableResultDelivery = true));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddJobsWorkflowsRuntime(options =>
        { options.EnableResultDelivery = true; options.DeliveryScopes.Add(NativeFixture.Scope); options.DeliveryScopes.Add(NativeFixture.Scope); }));
    }
}
