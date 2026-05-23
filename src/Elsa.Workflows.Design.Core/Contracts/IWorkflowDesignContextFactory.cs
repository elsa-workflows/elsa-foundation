namespace Elsa.Workflows.Design.Core.Contracts;

public interface IWorkflowDesignContextFactory
{
    ValueTask<IWorkflowDesignContext> Create(CancellationToken cancellationToken);
}
