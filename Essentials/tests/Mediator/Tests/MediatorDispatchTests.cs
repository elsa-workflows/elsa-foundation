using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Extensions;
using Elsa.Mediator.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Mediator.Tests;

/// <summary>
/// Covers the unified command/request dispatch: closed-generic handler resolution, the
/// single/duplicate/no-handler outcomes, the Unit command path, compiled-invoker result and raw
/// exception passthrough.
/// </summary>
public class MediatorDispatchTests
{
    [Fact]
    public async Task Request_ResolvesClosedGenericHandler_ViaAssemblyScan()
    {
        // The assembly-scan registration path registers each handler under its closed generic, which
        // is what the invoker resolves. Registering the whole test assembly proves only the one
        // relevant handler is constructed and dispatched.
        await using var provider = MediatorTestHost.Build(services =>
            services.AddRequestHandlersFrom(typeof(PingRequest).Assembly));

        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IRequestSender>();

        var response = await sender.Send(new PingRequest("hi"));

        Assert.Equal("pong:hi", response);
    }

    [Fact]
    public async Task Request_SingleHandler_ReturnsResponse()
    {
        await using var provider = MediatorTestHost.Build(services =>
            services.AddRequestHandler<PingRequestHandler, PingRequest, string>());

        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IRequestSender>();

        Assert.Equal("pong:x", await sender.Send(new PingRequest("x")));
    }

    [Fact]
    public async Task Request_NoHandler_Throws()
    {
        await using var provider = MediatorTestHost.Build(_ => { });

        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IRequestSender>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new OrphanRequest()));
        Assert.Contains("no handler", ex.Message);
        Assert.Contains("request", ex.Message);
    }

    [Fact]
    public async Task Request_DuplicateHandlers_Throws()
    {
        await using var provider = MediatorTestHost.Build(services =>
        {
            services.AddScoped<IRequestHandler<DuplicateRequest, string>, DuplicateRequestHandlerA>();
            services.AddScoped<IRequestHandler<DuplicateRequest, string>, DuplicateRequestHandlerB>();
        });

        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IRequestSender>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new DuplicateRequest()));
        Assert.Contains("Multiple handlers", ex.Message);
    }

    [Fact]
    public async Task Request_HandlerException_PropagatesRaw()
    {
        // The compiled invoker calls Handle directly, so a handler exception surfaces as itself — not
        // wrapped in a TargetInvocationException the way reflection Invoke would wrap it.
        await using var provider = MediatorTestHost.Build(services =>
            services.AddRequestHandler<BoomRequestHandler, BoomRequest, string>());

        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IRequestSender>();

        var ex = await Assert.ThrowsAsync<BoomException>(() => sender.Send(new BoomRequest()));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task Command_WithResult_ReturnsResult()
    {
        await using var provider = MediatorTestHost.Build(services =>
            services.AddScoped<ICommandHandler<AddCommand, int>, AddCommandHandler>());

        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ICommandSender>();

        Assert.Equal(7, await sender.Send(new AddCommand(3, 4)));
    }

    [Fact]
    public async Task Command_Unit_RunsHandler()
    {
        var recorder = new CommandRecorder();
        await using var provider = MediatorTestHost.Build(services =>
        {
            services.AddSingleton(recorder);
            services.AddScoped<ICommandHandler<SideEffectCommand, Unit>, SideEffectCommandHandler>();
        });

        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ICommandSender>();

        await sender.Send(new SideEffectCommand());

        Assert.Equal(1, recorder.Count);
    }
}
