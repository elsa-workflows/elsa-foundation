using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class CustomWorkflowAlterationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ContributedHandler_ResolvesExactDottedVersion_PerScope_AndStagesProjectedState()
    {
        var services = NewServices();
        services.AddWorkflowAlterationHandler<NormalizeV1Handler>(new WorkflowAlterationDescriptor("Contoso.Normalize", 1, "Normalize v1"));
        services.AddWorkflowAlterationHandler<NormalizeV2Handler>(new WorkflowAlterationDescriptor("Contoso.Normalize", 2, "Normalize v2"));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var registry = provider.GetRequiredService<IWorkflowAlterationRegistry>();
        Assert.Equal("Normalize v1", registry.GetRequired("Contoso.Normalize", 1).DisplayName);
        Assert.Equal("Normalize v2", registry.GetRequired("Contoso.Normalize", 2).DisplayName);
        Assert.Null(registry.Find("Contoso.Normalize", 3));
        Assert.DoesNotContain(registry.Descriptors, descriptor => JsonSerializer.Serialize(descriptor).Contains(nameof(NormalizeV1Handler), StringComparison.Ordinal));

        using var firstScope = provider.CreateScope();
        var firstResolver = firstScope.ServiceProvider.GetRequiredService<IWorkflowAlterationHandlerResolver>();
        var firstHandler = Assert.IsType<NormalizeV1Handler>(firstResolver.Resolve(Envelope(1, "first")));
        Assert.IsType<NormalizeV2Handler>(firstResolver.Resolve(Envelope(2, "second")));
        Assert.Same(firstHandler, firstResolver.Resolve(Envelope(1, "again")));

        var request = NewRequest(Envelope(1, "accepted"));
        var result = await firstScope.ServiceProvider.GetRequiredService<WorkflowAlterationJobExecutor>().ExecuteAsync(request);
        var writer = firstScope.ServiceProvider.GetRequiredService<RecordingCheckpointWriter>();

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, result.Job.Status);
        Assert.Equal(["accepted"], writer.AppliedMarkers);
        Assert.Equal("accepted", request.ProjectedState.GetRequired<CustomProjection>().Marker);
        Assert.Equal(1, firstHandler.Probe.PreflightCount);

        using var secondScope = provider.CreateScope();
        var secondHandler = Assert.IsType<NormalizeV1Handler>(secondScope.ServiceProvider.GetRequiredService<IWorkflowAlterationHandlerResolver>().Resolve(Envelope(1, "other")));
        Assert.NotSame(firstHandler, secondHandler);
        Assert.NotEqual(firstHandler.Probe.ScopeId, secondHandler.Probe.ScopeId);
    }

    [Fact]
    public async Task ContributedHandler_InvalidPayload_DiscardsStaging_AndLeaksNeitherPayloadNorHandlerIdentity()
    {
        var services = NewServices();
        services.AddWorkflowAlterationHandler<NormalizeV1Handler>(new WorkflowAlterationDescriptor("Contoso.Normalize", 1, "Normalize v1"));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var secret = "operator-supplied-secret";

        var result = await scope.ServiceProvider.GetRequiredService<WorkflowAlterationJobExecutor>().ExecuteAsync(NewRequest(Envelope(1, secret, valid: false)));
        var writer = scope.ServiceProvider.GetRequiredService<RecordingCheckpointWriter>();

        Assert.Equal(WorkflowAlterationJobStatus.Failed, result.Job.Status);
        Assert.Empty(writer.AppliedMarkers);
        Assert.Equal("InvalidContosoNormalizePayload", Assert.Single(result.Job.Outcomes).Code);
        var durableResult = JsonSerializer.Serialize(result.Job);
        Assert.DoesNotContain(secret, durableResult, StringComparison.Ordinal);
        Assert.DoesNotContain("\"payload\"", durableResult, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(NormalizeV1Handler), durableResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContributedHandler_AcknowledgementReplay_AppliesItsCheckpointChangeExactlyOnce()
    {
        var services = NewServices(loseAcknowledgement: true);
        services.AddWorkflowAlterationHandler<NormalizeV1Handler>(new WorkflowAlterationDescriptor("Contoso.Normalize", 1, "Normalize v1"));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<WorkflowAlterationJobExecutor>().ExecuteAsync(NewRequest(Envelope(1, "replayed")));
        var writer = scope.ServiceProvider.GetRequiredService<RecordingCheckpointWriter>();

        Assert.True(result.ReconciledAfterAcknowledgementLoss);
        Assert.Equal(1, writer.CommitCount);
        Assert.Equal(["replayed"], writer.AppliedMarkers);
    }

    [Fact]
    public void ContributionStartup_RejectsDuplicateExactIdentity_AndAcceptsIndependentVersions()
    {
        var services = NewServices();
        var v1 = new WorkflowAlterationDescriptor("Contoso.Normalize", 1, "Normalize v1");
        services.AddWorkflowAlterationHandler<NormalizeV1Handler>(v1);
        services.AddWorkflowAlterationHandler<NormalizeV2Handler>(new WorkflowAlterationDescriptor("Contoso.Normalize", 2, "Normalize v2"));

        Assert.Throws<InvalidOperationException>(() => services.AddWorkflowAlterationHandler<NormalizeV2Handler>(v1));
        using var provider = services.BuildServiceProvider();
        Assert.Equal(2, provider.GetRequiredService<IWorkflowAlterationRegistry>().Descriptors.Count(descriptor => descriptor.Kind == "Contoso.Normalize"));
    }

    private static ServiceCollection NewServices(bool loseAcknowledgement = false)
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddScoped<ScopedHandlerProbe>();
        services.AddSingleton(new RecordingCheckpointWriter(loseAcknowledgement));
        services.Replace(ServiceDescriptor.Scoped<IWorkflowAlterationCheckpointWriter>(serviceProvider => serviceProvider.GetRequiredService<RecordingCheckpointWriter>()));
        return services;
    }

    private static WorkflowAlterationJobExecutionRequest NewRequest(params WorkflowAlterationEnvelope[] envelopes) =>
        new(
            new WorkflowAlterationJobState(
                "job-1", "plan-1", "workflow-1", "tenant-a", 0,
                WorkflowAlterationJobStatus.Running,
                new WorkflowAlterationJobClaim("worker-1", "claim-1", Now.AddMinutes(1)),
                1, [], null, null, Now, Now, null, 0),
            envelopes,
            new WorkflowAlterationProjectedState());

    private static WorkflowAlterationEnvelope Envelope(int version, string marker, bool valid = true)
    {
        object payload = valid ? new { marker } : new { marker = (string?)null, secret = marker };
        return new WorkflowAlterationEnvelope("Contoso.Normalize", version, JsonSerializer.SerializeToElement(payload));
    }

    private sealed class ScopedHandlerProbe
    {
        public Guid ScopeId { get; } = Guid.NewGuid();
        public int PreflightCount { get; set; }
    }

    private sealed class NormalizeV1Handler(ScopedHandlerProbe probe) : IWorkflowAlterationPreflightHandler
    {
        public ScopedHandlerProbe Probe { get; } = probe;

        public ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(WorkflowAlterationPreflightContext context, CancellationToken cancellationToken = default)
        {
            Probe.PreflightCount++;
            if (context.Envelope.Payload.ValueKind != JsonValueKind.Object ||
                !context.Envelope.Payload.TryGetProperty("marker", out var marker) ||
                marker.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(marker.GetString()))
            {
                return ValueTask.FromResult(WorkflowAlterationPreflightResult.Rejected("InvalidContosoNormalizePayload", "The custom alteration payload is invalid."));
            }

            var projected = new CustomProjection(marker.GetString()!);
            context.ProjectedState.Set(projected);
            context.Staging.Stage(new CustomNormalizeStagedChange(projected.Marker));
            return ValueTask.FromResult(WorkflowAlterationPreflightResult.Accepted());
        }
    }

    private sealed class NormalizeV2Handler : IWorkflowAlterationPreflightHandler
    {
        public ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(WorkflowAlterationPreflightContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(WorkflowAlterationPreflightResult.Accepted());
    }

    private sealed record CustomProjection(string Marker);

    private sealed record CustomNormalizeStagedChange(string Marker) : IWorkflowAlterationRuntimeCheckpointStagedChange
    {
        public string ChangeKey => $"contoso.normalize:{Marker}";
        public void Apply(WorkflowAlterationRuntimeCheckpointStateBuilder builder) { }
    }

    private sealed class RecordingCheckpointWriter(bool loseAcknowledgement) : IWorkflowAlterationCheckpointWriter
    {
        private readonly Dictionary<string, WorkflowAlterationCheckpointWriteResult> _committed = new(StringComparer.Ordinal);
        private bool _loseAcknowledgement = loseAcknowledgement;

        public List<string> AppliedMarkers { get; } = [];
        public int CommitCount { get; private set; }

        public ValueTask<WorkflowAlterationCheckpointWriteResult> CommitAsync(WorkflowAlterationCheckpointWriteRequest request, CancellationToken cancellationToken = default)
        {
            CommitCount++;
            AppliedMarkers.AddRange(request.StagedChanges.OfType<CustomNormalizeStagedChange>().Select(change => change.Marker));
            var result = new WorkflowAlterationCheckpointWriteResult(request.TerminalJobChange.CheckpointCommitId, request.TerminalJobChange);
            _committed.Add(result.CheckpointCommitId, result);
            if (_loseAcknowledgement)
            {
                _loseAcknowledgement = false;
                throw new WorkflowAlterationCheckpointAcknowledgementLostException(result.CheckpointCommitId);
            }

            return ValueTask.FromResult(result);
        }

        public ValueTask<WorkflowAlterationCheckpointWriteResult?> FindCommittedAsync(string checkpointCommitId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_committed.GetValueOrDefault(checkpointCommitId));
    }
}
