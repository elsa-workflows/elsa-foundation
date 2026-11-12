using System.Text.Json;
using Elsa.Weaver.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Persistence.EntityFrameworkCore.Stores;

/// <summary>Selects the owning context's admitted version for one metadata attempt.</summary>
internal static class WeaverWriteVersion
{
    public static string Select(DbContext context, WeaverSessionRecordWrite? write = null)
    {
        var gate = Gate(context);
        var state = gate?.StateOf(WeaverEfModule.SchemaFamily);
        if (gate is not null && state is null)
            throw new InvalidOperationException("The Weaver family has not been admitted by its schema gate.");
        if (state is { WritesRefused: true })
            throw new EfSchemaFamilyWritesRefusedException(WeaverEfModule.SchemaFamily, state.WriteVersion,
                state.UnreadableFinalizedVersion!, state.ReadableVersions);

        // A direct consumer without a module gate retains the original format; installing this assembly alone
        // never finalizes the family or authorizes the new representation.
        var version = state?.WriteVersion ?? WeaverEfModule.LegacySchemaVersion;
        EfSchemaVersion.EnsureReadable(WeaverEfModule.Chain, version);
        if (version == WeaverEfModule.LegacySchemaVersion && write is not null && NeedsEffects(write))
            throw new EfSchemaWriteRefusedException(WeaverEfModule.SchemaFamily, version, WeaverEfModule.SchemaVersion);
        return version;
    }

    /// <summary>Only the shared gate's verified forward adoption can retry a schema refusal.</summary>
    public static bool IsForwardAdoption(DbContext context, string? selected, Exception exception)
    {
        // Derived dormant/unreadable-family refusals and arbitrary refusals are terminal.
        if (selected is null || exception.GetType() != typeof(EfSchemaWriteRefusedException)
            || exception is not EfSchemaWriteRefusedException refusal
            || refusal.Family != WeaverEfModule.SchemaFamily || refusal.RequiredVersion != selected)
            return false;
        var state = Gate(context)?.StateOf(WeaverEfModule.SchemaFamily);
        if (state is null || state.WritesRefused || state.WriteVersion != refusal.WriteVersion) return false;
        var versions = WeaverEfModule.Chain.ReadableVersions;
        var before = -1;
        var after = -1;
        for (var i = 0; i < versions.Count; i++)
        {
            if (versions[i] == selected) before = i;
            if (versions[i] == state.WriteVersion) after = i;
        }
        return before >= 0 && after > before;
    }

    public static void EnsureCanRewrite(DbContext context, string selected, string stored)
    {
        EfSchemaVersion.EnsureReadable(WeaverEfModule.Chain, stored);
        // Registered contexts leave the original stamp to the shared interceptor, including its fresh confirmation
        // of a finalized version. Ungated consumers cannot confirm adoption and must never lower a retained stamp.
        if (Gate(context) is null && Version.Parse(stored) > Version.Parse(selected))
            throw new EfSchemaWriteRefusedException(WeaverEfModule.SchemaFamily, selected, stored);
    }

    private static EfSchemaModuleGate? Gate(DbContext context) => context.GetService<IDbContextOptions>()
        .FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
        .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());

    private static bool NeedsEffects(WeaverSessionRecordWrite write) =>
        write.Messages.Any(message => message.Message.Error?.OwnerUnavailable == true || message.Message.ToolResults.Any(NeedsEffects)
            || message.Message.DisclosureHistory?.Any(snapshot => snapshot.ToolResults.Any(NeedsEffects)) == true)
        || write.PausedTurn is { } paused && (paused.Turn.DisclosureResults.Any(NeedsEffects)
            || paused.Turn.DisclosureHistory?.Any(snapshot => snapshot.ToolResults.Any(NeedsEffects)) == true)
        || write.Snapshots.Any(NeedsEffects)
        || write.Proposals.Any(record => record.Proposal.Status == WeaverActionProposalStatus.Performed
            || NeedsEffects(record.Proposal.PreparedInvocation) || NeedsEffects(record.Result)
            || record.Execution?.Error?.OwnerUnavailable == true
            || record.Execution?.Value is { } execution && (execution.Status == "performed"
                || NeedsEffects(execution.ClientInvocation) || NeedsEffects(execution.ToolResult)));

    private static bool NeedsEffects(WeaverDisclosureSnapshotRecord snapshot)
    {
        // The owner stores typed disclosure history as canonical text and digest references. Inspect only its
        // known receipt envelope, without reserializing it or interpreting module payloads/context/content.
        using var document = JsonDocument.Parse(snapshot.Canonical);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ToolResults", out var results)
            || results.ValueKind != JsonValueKind.Array) return false;
        foreach (var result in results.EnumerateArray())
        {
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("Receipt", out var receipt)
                || receipt.ValueKind != JsonValueKind.Object) continue;
            if (receipt.TryGetProperty("ModuleCode", out var code) && code.ValueKind != JsonValueKind.Null) return true;
            if (receipt.TryGetProperty("Scope", out var scope) && scope.ValueKind == JsonValueKind.Number
                && scope.TryGetInt32(out var number) && number == (int)WeaverEffectScope.NoPersistedEffect) return true;
        }
        return false;
    }

    private static bool NeedsEffects(WeaverPreparedToolInvocation? invocation) => invocation?.RequiredPrivilegeClasses is { Count: > 0 };

    private static bool NeedsEffects(WeaverToolResult? result) => result?.Receipt is { } receipt
        && (receipt.ModuleCode is not null || receipt.Scope == WeaverEffectScope.NoPersistedEffect);
}
