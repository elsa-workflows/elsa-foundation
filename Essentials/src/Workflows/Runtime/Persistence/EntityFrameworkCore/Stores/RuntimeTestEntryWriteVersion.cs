using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Raises physical format requirements; payload presence never grants execution authority.</summary>
internal static class RuntimeTestEntryWriteVersion
{
    internal static bool Requires(WorkflowExecutionState state) => state.TestEntry is not null;

    internal static bool Requires(RuntimeSchedulerWorkItem item) =>
        item.CommandKind is WorkflowExecutionCommandKind.Start or WorkflowExecutionCommandKind.Checkpoint && HasEntry(item.Payload);

    internal static bool Requires(RuntimePostCommitOutboxItem item)
    {
        if (item.Intent.Kind != RuntimePostCommitIntentKinds.EnqueueSchedulerWork) return false;
        if (item.Intent.MaterializedSchedulerWorkItem is { } work) return Requires(work);
        return item.Intent.Payload is { } payload && payload.Deserialize<RuntimeSchedulerWorkItem>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) is { } restored && Requires(restored);
    }

    internal static bool HasEntry(JsonElement? payload) => payload is { ValueKind: JsonValueKind.Object } value &&
        value.EnumerateObject().Any(x =>
            (x.Name.Equals("testEntry", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("testContext", StringComparison.OrdinalIgnoreCase)) &&
            x.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined));

    internal static bool ContentHasEntry(string content)
    {
        using var document = JsonDocument.Parse(content);
        return HasEntry(document.RootElement);
    }

    internal static string Require(RuntimeDbContext context, string family, string minimum)
    {
        var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
        // Direct construction is useful for tooling and focused store tests. Normal admission separately requires
        // a provider boundary proving that these exact physical stores and their live gates are qualified.
        if (gate is null) return minimum;
        var state = gate.StateOf(family) ?? throw new InvalidOperationException("The selected test format family has not been admitted.");
        if (state.WritesRefused)
            throw new EfSchemaFamilyWritesRefusedException(family, state.WriteVersion, state.UnreadableFinalizedVersion!, state.ReadableVersions);
        if (Version.Parse(state.WriteVersion) < Version.Parse(minimum))
            throw new EfSchemaWriteRefusedException(family, state.WriteVersion, minimum);
        return state.WriteVersion;
    }

    internal static string Preserve(string version, string? storedVersion) =>
        storedVersion is not null && Version.Parse(storedVersion) > Version.Parse(version) ? storedVersion : version;
}
