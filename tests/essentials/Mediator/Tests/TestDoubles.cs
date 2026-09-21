using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;

namespace Elsa.Mediator.Tests;

// Test doubles used across the mediator dispatch tests. Kept internal but discoverable by the
// assembly-scan registration helpers (AddRequestHandlersFrom / AddCommandHandlersFrom).

public sealed class BoomException(string message) : Exception(message);

// --- Requests ---------------------------------------------------------------------------------

public sealed record PingRequest(string Value) : IRequest<string>;

public sealed class PingRequestHandler : IRequestHandler<PingRequest, string>
{
    public Task<string> Handle(PingRequest request, CancellationToken cancellationToken) =>
        Task.FromResult($"pong:{request.Value}");
}

/// <summary>A request with no registered handler — exercises the no-handler failure path.</summary>
public sealed record OrphanRequest : IRequest<string>;

public sealed record DuplicateRequest : IRequest<string>;

public sealed class DuplicateRequestHandlerA : IRequestHandler<DuplicateRequest, string>
{
    public Task<string> Handle(DuplicateRequest request, CancellationToken cancellationToken) => Task.FromResult("a");
}

public sealed class DuplicateRequestHandlerB : IRequestHandler<DuplicateRequest, string>
{
    public Task<string> Handle(DuplicateRequest request, CancellationToken cancellationToken) => Task.FromResult("b");
}

public sealed record BoomRequest : IRequest<string>;

public sealed class BoomRequestHandler : IRequestHandler<BoomRequest, string>
{
    public Task<string> Handle(BoomRequest request, CancellationToken cancellationToken) =>
        throw new BoomException("boom");
}

/// <summary>A scoped marker whose identity reveals which DI scope constructed the handler.</summary>
public sealed class ScopeMarker
{
    public Guid Id { get; } = Guid.NewGuid();
}

public sealed record ScopeRequest : IRequest<Guid>;

public sealed class ScopeRequestHandler(ScopeMarker marker) : IRequestHandler<ScopeRequest, Guid>
{
    public Task<Guid> Handle(ScopeRequest request, CancellationToken cancellationToken) => Task.FromResult(marker.Id);
}

// --- Commands ---------------------------------------------------------------------------------

public sealed record AddCommand(int X, int Y) : ICommand<int>;

public sealed class AddCommandHandler : ICommandHandler<AddCommand, int>
{
    public Task<int> Handle(AddCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(command.X + command.Y);
}

/// <summary>Records that a Unit-returning command handler ran.</summary>
public sealed class CommandRecorder
{
    public int Count { get; private set; }
    public void Record() => Count++;
}

public sealed record SideEffectCommand : ICommand;

public sealed class SideEffectCommandHandler(CommandRecorder recorder) : ICommandHandler<SideEffectCommand>
{
    public Task<Unit> Handle(SideEffectCommand command, CancellationToken cancellationToken)
    {
        recorder.Record();
        return Task.FromResult(Unit.Instance);
    }
}
