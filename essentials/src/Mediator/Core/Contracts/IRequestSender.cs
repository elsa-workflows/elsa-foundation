namespace Elsa.Mediator.Core.Contracts;

public interface IRequestSender
{
    /// <summary>
    /// Sends a request.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <typeparam name="T">The type of the response.</typeparam>
    /// <returns>The response.</returns>
    Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default)
        where T : notnull;
}
