using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>Rejects ambiguous tooling orchestration without resolving optional request services.</summary>
internal sealed class ExpressionToolingStartupTask(IServiceCollection registrations) : IStartupTask
{
    internal bool Owns(IServiceCollection collection) => ReferenceEquals(registrations, collection);

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(registrations);
        return Task.CompletedTask;
    }

    internal static void Validate(IServiceCollection registrations)
    {
        if (registrations.Count(item => item.ServiceType == typeof(IExpressionToolingOperations)) > 1)
            throw new InvalidOperationException("IExpressionToolingOperations has multiple registrations; select one explicit expression tooling orchestration.");
    }
}
