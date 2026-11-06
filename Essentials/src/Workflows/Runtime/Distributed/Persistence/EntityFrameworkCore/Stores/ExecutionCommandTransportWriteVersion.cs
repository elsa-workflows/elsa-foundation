using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;

internal static class ExecutionCommandTransportWriteVersion
{
    internal static bool Requires(WorkflowExecutionCommandEnvelope? envelope) =>
        envelope?.Command.Kind is WorkflowExecutionCommandKind.Start or WorkflowExecutionCommandKind.Checkpoint &&
        envelope?.Command.Payload is { ValueKind: JsonValueKind.Object } payload && payload.EnumerateObject().Any(x =>
            (x.Name.Equals("testEntry", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("testContext", StringComparison.OrdinalIgnoreCase)) &&
            x.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined));

    internal static EfSchemaModuleGate? Gate(ExecutionCommandTransportDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());

    internal static string For(ExecutionCommandTransportDbContext context, WorkflowExecutionCommandEnvelope? envelope,
        string? storedVersion = null, string? streamVersion = null)
    {
        var minimum = Requires(envelope) ? ExecutionCommandTransportEfModule.TestEntrySchemaVersion : ExecutionCommandTransportEfModule.SchemaVersion;
        foreach (var previous in new[] { storedVersion, streamVersion })
            if (previous is not null && Version.Parse(previous) > Version.Parse(minimum)) minimum = previous;
        var gate = Gate(context);
        var state = gate?.StateOf(ExecutionCommandTransportEfModule.SchemaFamily);
        if (gate is not null && state is null)
            throw new InvalidOperationException("The command-transport family has not been admitted by its schema gate.");
        if (state is { WritesRefused: true })
            throw new EfSchemaFamilyWritesRefusedException(ExecutionCommandTransportEfModule.SchemaFamily,
                state.WriteVersion, state.UnreadableFinalizedVersion!, state.ReadableVersions);
        var version = state?.WriteVersion ?? minimum;
        if (Version.Parse(version) < Version.Parse(minimum))
            throw new EfSchemaWriteRefusedException(ExecutionCommandTransportEfModule.SchemaFamily, version, minimum);
        return version;
    }
}
