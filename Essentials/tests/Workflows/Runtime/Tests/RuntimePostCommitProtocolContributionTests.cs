using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimePostCommitDurableSourceTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Actual_committer_stamps_only_declared_protocol_and_physical_scope_before_any_selection(int mode)
    {
        var registrations = Services(); registrations.RemoveAll<RuntimePostCommitIntentHandlerContribution>();
        registrations.AddRuntimePostCommitIntentHandler<SourceHandler>(Kind, RuntimePostCommitRetryPolicy.None, mode != 0, mode == 2 ? 1 : null);
        using var provider = registrations.BuildServiceProvider(); using var scope = provider.CreateScope(); var services = scope.ServiceProvider;
        var commit = Envelope("declared", 1) with { PostCommitIntents = [Pending("ignored").Intent] };
        await services.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(commit);
        var item = (await services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().FindAsync("declared:send"))!;
        Assert.Equal(mode == 2 ? 1 : (int?)null, item.StimulusDeliveryProtocolVersion);
        Assert.Equal(mode == 0 ? null : Scope, item.StimulusDeliverySourceScope); Assert.Null(item.RecipientSnapshot);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, item.Status); Assert.Equal(0, item.DeliveryAttemptCount);
        Assert.Empty(services.GetRequiredService<Recorder>().Contexts);
    }

    [Theory]
    [InlineData(-1, true)] [InlineData(0, true)] [InlineData(2, true)] [InlineData(1, false)]
    public void Invalid_recipient_protocol_refuses_without_changing_service_collection(int version, bool required)
    {
        var services = Services(); var before = services.ToArray();
        Assert.Throws<ArgumentException>(() => services.AddRuntimePostCommitIntentHandler<SourceHandler>("Other", RuntimePostCommitRetryPolicy.None, required, version));
        Assert.Equal(before, services);
    }

    [Fact]
    public void Conflicting_recipient_protocol_refuses_in_registration_dispatch_and_checkpoint_factory()
    {
        var registrations = Services(); var before = registrations.ToArray();
        Assert.Throws<InvalidOperationException>(() => registrations.AddRuntimePostCommitIntentHandler<SourceHandler>(Kind, RuntimePostCommitRetryPolicy.None, true, 1));
        Assert.Equal(before, registrations);
        var contributions = new[] {
            new RuntimePostCommitIntentHandlerContribution(Kind, typeof(SourceHandler), RuntimePostCommitRetryPolicy.None, true),
            new RuntimePostCommitIntentHandlerContribution(Kind, typeof(SourceHandler), RuntimePostCommitRetryPolicy.None, true, 1) };
        using var provider = registrations.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => new RuntimePostCommitIntentDispatcher(contributions, provider));
        Assert.Throws<InvalidOperationException>(() => RuntimePostCommitOutboxItems.CreatePendingChanges(
            Envelope("conflict", 1) with { PostCommitIntents = [Pending("ignored").Intent] }, contributions, Scope));
        Assert.Null(contributions[0].StimulusDeliveryProtocolVersion);
        Assert.NotNull(typeof(RuntimePostCommitIntentHandlerContribution).GetConstructor([typeof(string), typeof(Type), typeof(RuntimePostCommitRetryPolicy), typeof(bool)]));
    }
}
