using System.Collections.Frozen;
using Elsa.Events.Core.Contracts;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>Named Sequential contribution event; model modules contribute through the contributor contract.</summary>
internal sealed class RuntimeExecutionProtocolsInitializing : IEvent
{
    public ICollection<RuntimeExecutionProtocolRegistration> Registrations { get; } = [];
}

internal sealed class RegisterRuntimeExecutionProtocols(IEnumerable<IRuntimeExecutionProtocolContributor> contributors)
    : IEventHandler<RuntimeExecutionProtocolsInitializing>
{
    public Task Handle(RuntimeExecutionProtocolsInitializing domainEvent, CancellationToken cancellationToken)
    {
        foreach (var contributor in contributors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var registration in contributor.GetProtocols()) domainEvent.Registrations.Add(registration);
        }
        return Task.CompletedTask;
    }
}

internal sealed class RuntimeExecutionProtocolRegistry
{
    private FrozenDictionary<RuntimeExecutionProtocol, Entry>? entries;
    public void Initialize(IEnumerable<RuntimeExecutionProtocolRegistration> contributions, IServiceCollection services)
    {
        if (entries is not null) throw new InvalidOperationException("Runtime execution protocols are already initialized.");
        var result = new Dictionary<RuntimeExecutionProtocol, Entry>();
        foreach (var contribution in contributions)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            var registrations = services.Where(item => item.ServiceType == contribution.DriverType).ToArray();
            if (registrations is not [{ Lifetime: ServiceLifetime.Scoped }])
                throw new InvalidOperationException($"Execution driver '{contribution.DriverType.Name}' requires exactly one scoped registration.");
            if (contribution.InputKinds.Count == 0 || contribution.InputKinds.Any(string.IsNullOrWhiteSpace) ||
                contribution.InputKinds.Distinct(StringComparer.Ordinal).Count() != contribution.InputKinds.Count)
                throw new InvalidOperationException($"Execution protocol '{contribution.Protocol}' has no valid input kinds.");
            if (!result.TryAdd(contribution.Protocol, new(contribution.DriverType, contribution.InputKinds.ToFrozenSet(StringComparer.Ordinal))))
                throw new InvalidOperationException($"Execution protocol '{contribution.Protocol.Key}/{contribution.Protocol.Version}' is registered more than once.");
        }
        entries = result.ToFrozenDictionary();
    }
    public Entry? Find(RuntimeExecutionProtocol protocol) =>
        (entries ?? throw new InvalidOperationException("Runtime execution protocols have not completed startup initialization.")).GetValueOrDefault(protocol);

    internal sealed record Entry(Type DriverType, IReadOnlySet<string> InputKinds);
}

internal sealed class RuntimeExecutionProtocolsStartupTask(IInlineEventPublisher publisher,
    RuntimeExecutionProtocolRegistry registry, IServiceCollection registrations) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var contribution = new RuntimeExecutionProtocolsInitializing();
        await publisher.Publish(contribution, cancellationToken);
        registry.Initialize(contribution.Registrations, registrations);
    }
}
