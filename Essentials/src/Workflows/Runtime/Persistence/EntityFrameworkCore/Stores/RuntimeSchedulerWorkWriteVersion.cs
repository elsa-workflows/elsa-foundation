using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class RuntimeSchedulerWorkWriteVersion
{
    public static string For(RuntimeDbContext context, RuntimeSchedulerWorkItem item, string? storedVersion = null)
    {
        var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
        var state = gate?.StateOf(RuntimeSchedulerWorkEfModule.SchemaFamily);
        if (gate is not null && state is null)
            throw new InvalidOperationException("The scheduler-work family has not been admitted by its schema gate.");
        if (state is { WritesRefused: true })
            throw new EfSchemaFamilyWritesRefusedException(RuntimeSchedulerWorkEfModule.SchemaFamily,
                state.WriteVersion, state.UnreadableFinalizedVersion!, state.ReadableVersions);
        var minimum = IsModelWorker(item) ? RuntimeSchedulerWorkEfModule.SchemaVersion :
            item.CommandKind == WorkflowExecutionCommandKind.ExecuteModel ? RuntimeSchedulerWorkEfModule.ModelSchemaVersion : "1.0.0";
        if (storedVersion is not null && Version.Parse(storedVersion) > Version.Parse(minimum)) minimum = storedVersion;
        var version = state?.WriteVersion ?? (minimum == RuntimeSchedulerWorkEfModule.SchemaVersion ? minimum : RuntimeSchedulerWorkEfModule.ModelSchemaVersion);
        if (Version.Parse(version) < Version.Parse(minimum))
            throw new EfSchemaWriteRefusedException(RuntimeSchedulerWorkEfModule.SchemaFamily, version,
                minimum);
        return version;
    }

    internal static bool IsModelWorker(RuntimeSchedulerWorkItem item)
    {
        if (item.Payload is not { ValueKind: JsonValueKind.Object } payload) return false;
        if (Property(payload, "modelWorkOrigin") is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) }) return true;
        var authority = Property(payload, "startAuthority");
        if (authority.ValueKind != JsonValueKind.Object) return false;
        var kind = Property(authority, "kind");
        return Property(authority, "modelWork") is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } ||
            kind.ValueKind == JsonValueKind.Number && kind.TryGetInt32(out var number) && number == (int)WorkflowExecutableStartAuthorityKind.ModelWork ||
            kind.ValueKind == JsonValueKind.String && kind.GetString() == nameof(WorkflowExecutableStartAuthorityKind.ModelWork);
    }

    private static JsonElement Property(JsonElement value, string name) => value.EnumerateObject()
        .FirstOrDefault(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
}
