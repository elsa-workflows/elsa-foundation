using Elsa.Mediator.Core.Contracts;

namespace Elsa.Mediator.Requests;

public sealed class RequestSender(IRequestPipeline requestPipeline, IServiceProvider serviceProvider) : IRequestSender
{
    public async Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default)
        where T : notnull
    {
        var context = new RequestContext<T>(request, serviceProvider, cancellationToken);
        await requestPipeline.Execute(context);

        return context.Response!;
    }
}
