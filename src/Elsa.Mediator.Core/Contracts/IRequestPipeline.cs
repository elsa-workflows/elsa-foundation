namespace Elsa.Mediator.Core.Contracts;

public interface IRequestPipeline
{
    Task Execute(IRequestContext context);
}
