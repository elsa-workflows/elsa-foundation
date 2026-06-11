using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ExecuteWorkflowRequestHandlerTests
{
    [Fact]
    public async Task RejectsUnknownArtifactId()
    {
        var handler = new ExecuteWorkflowRequestHandler(
            new InMemoryWorkflowExecutableStore(),
            new SequentialWorkflowExecutor(new ThrowingActivityFactory(), new EmptyServiceProvider()));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new ExecuteWorkflow("missing-artifact"), CancellationToken.None));

        Assert.Contains("missing-artifact", exception.Message);
    }

    private sealed class ThrowingActivityFactory : Elsa.Activities.Runtime.Core.Contracts.IActivityFactory
    {
        public ValueTask<Elsa.Activities.Runtime.Core.Contracts.IActivity> Create(
            string descriptorType,
            System.Text.Json.JsonElement payload,
            IDictionary<string, Elsa.Activities.Runtime.Core.Models.InputArgument>? inputs,
            IDictionary<string, Elsa.Activities.Runtime.Core.Models.OutputArgument>? outputs,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Should not construct an activity for a missing artifact.");
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
