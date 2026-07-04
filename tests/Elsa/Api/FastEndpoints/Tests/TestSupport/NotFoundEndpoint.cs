using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using FastEndpoints;
using Microsoft.Extensions.Logging;

namespace Elsa.Api.FastEndpoints.Tests.TestSupport;

/// <summary>The request handled by <see cref="NotFoundEndpoint"/>; the stub sender always reports the entity missing.</summary>
public sealed class NotFoundRequest : IRequest<string>
{
    public string? Id { get; set; }
}

/// <summary>
/// A trivial endpoint built on the real <see cref="ElsaRequestHandlerEndpoint{TRequest,TResponse}"/> base whose
/// request sender throws <see cref="EntityNotFoundException"/>. It exists to prove the base-class catch maps a
/// not-found lookup to a 404 ProblemDetails response instead of a generic 500 (issue #393).
/// </summary>
public sealed class NotFoundEndpoint(IRequestSender requestSender, ILogger<NotFoundEndpoint> logger)
    : ElsaRequestHandlerEndpoint<NotFoundRequest, string>(requestSender, logger)
{
    public const string Route = "test/not-found";

    public override void Configure()
    {
        Get(Route);
        ConfigurePermissions();
    }
}

/// <summary>An <see cref="IRequestSender"/> stub that always signals the requested entity is missing.</summary>
public sealed class EntityNotFoundRequestSender : IRequestSender
{
    public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
        throw new EntityNotFoundException("The requested entity cannot be found.");
}
