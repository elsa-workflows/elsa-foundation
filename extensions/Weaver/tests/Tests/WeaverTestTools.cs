using System.Text.Json;
using Elsa.Weaver.Core;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Weaver.Tests;

internal sealed class EchoTool : IWeaverTool
{
    public WeaverToolDescriptor Descriptor { get; } = new(DeterministicWeaverProvider.ReadOnlyToolName, "Echo", "Echoes text.",
        WeaverToolMutability.ReadOnly, WeaverRisk.ReadOnly, """{"type":"object","properties":{"text":{"type":"string"}}}""", [], [])
    { ModuleId = "test.module", DisclosureSourceId = WeaverTestContextSource.TestModuleSourceId, ResultType = "test.echo", ResultSchemaVersion = "1" };
    public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(WeaverResult<WeaverToolPreparation>.Success(WeaverTestToolHelpers.Preparation(invocation, Descriptor)));
    public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var text = invocation.Arguments.TryGetProperty("text", out var value) ? value.ToString() : string.Empty;
        return Task.FromResult(WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, $"echo:{text}",
            new("test.echo", "1", JsonSerializer.SerializeToElement(new { text })))));
    }
}

internal sealed class ApplyChangeTool : IWeaverTool
{
    public int Invocations { get; private set; }
    public WeaverToolDescriptor Descriptor { get; } = new(DeterministicWeaverProvider.MutatingToolName, "Apply change", "Applies a test resource change.",
        WeaverToolMutability.Mutating, WeaverRisk.ReviewRequired, """{"type":"object","properties":{"change":{"type":"string"}}}""", [], [])
    { ModuleId = "test.module", DisclosureSourceId = WeaverTestContextSource.TestModuleSourceId, ResultType = "test.change", ResultSchemaVersion = "1" };
    public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(WeaverResult<WeaverToolPreparation>.Success(WeaverTestToolHelpers.Preparation(invocation, Descriptor) with
        { Target = new("test.module", "test.resource", "resource-1", "revision-1") }));
    public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        Invocations++;
        var data = new WeaverPayload("test.change", "1", JsonSerializer.SerializeToElement(new { invocations = Invocations }));
        return Task.FromResult(WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "applied", data)
        { Receipt = new(invocation.InvocationId, invocation.Target!, invocation.Target! with { Revision = $"revision-{Invocations + 1}" }, WeaverEffectScope.ServerPersisted, data) }));
    }
}

internal sealed class ThrowingTool : IWeaverTool
{
    public WeaverToolDescriptor Descriptor { get; } = new("boom", "Boom", "Always throws.", WeaverToolMutability.ReadOnly, WeaverRisk.ReadOnly, "{}", [], [])
    { ModuleId = "test.module", DisclosureSourceId = WeaverTestContextSource.TestModuleSourceId };
    public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(WeaverResult<WeaverToolPreparation>.Success(WeaverTestToolHelpers.Preparation(invocation, Descriptor)));
    public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("tool exploded");
}

internal static class WeaverTestToolHelpers
{
    public static WeaverToolInvocation Invocation(string toolName, string sessionId = "session-1")
        => new($"{toolName}-call", toolName, sessionId, "actor-1", new Dictionary<string, object?> { ["text"] = "hi" }, [])
        { TenantId = "tenant-1", TurnId = "turn-1" };

    public static WeaverToolPreparation Preparation(WeaverToolInvocation invocation, WeaverToolDescriptor descriptor) =>
        new(null, JsonSerializer.SerializeToElement(invocation.Arguments), descriptor.Risk,
            new("test.preview", "1", JsonSerializer.SerializeToElement(new { tool = descriptor.Name })), RequiresApproval: false);

    public static WeaverPreparedToolInvocation Prepared(WeaverToolInvocation invocation, WeaverToolDescriptor descriptor, WeaverToolPreparation preparation) =>
        new("invocation-1", invocation.SessionId, invocation.TurnId, invocation.ToolCallId, descriptor.Name, descriptor.ContractVersion,
            descriptor.ModuleId, invocation.ActorId, invocation.TenantId, preparation.Target, preparation.Arguments, "test-digest", preparation.Risk,
            preparation.Preview, descriptor.ExecutionLocation, preparation.RequiresApproval, preparation.Client, DateTimeOffset.UtcNow.AddMinutes(5))
        { DisclosureSourceId = descriptor.DisclosureSourceId };
}

internal sealed class WeaverTestToolsPublisher(params IWeaverTool[] tools) : IInlineEventPublisher
{
    public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event is WeaverToolsCollecting collecting)
            foreach (var tool in tools) collecting.Tools.Add(tool);
        return Task.CompletedTask;
    }
}

internal sealed class WeaverTestToolInvoker(Func<WeaverToolInvocation, WeaverPolicy, Task<WeaverToolDispatch>>? dispatch = null) : IWeaverToolInvoker
{
    public Task<WeaverToolDispatch> DispatchAsync(WeaverToolInvocation invocation, WeaverPolicy policy, CancellationToken cancellationToken = default)
        => dispatch?.Invoke(invocation, policy) ?? Task.FromResult(WeaverToolDispatch.Denied(new("test.not_configured", "No tool response configured.")));
}
