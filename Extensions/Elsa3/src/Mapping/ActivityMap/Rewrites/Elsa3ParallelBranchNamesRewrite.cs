using System.Text.Json;
using Elsa3.Mapping.Conversion;
using static Elsa3.Models.Elsa3ActivityDiagnosticCodes;

namespace Elsa3.Mapping.ActivityMap.Rewrites;

/// <summary>
/// Parallel and Fork (R7): Elsa 4 Parallel names each branch, so branches are named <c>Branch {n}</c> in document order
/// (1-based). Fork's join mode becomes the Parallel threshold: WaitAll → null (all branches), WaitAny → 1, with a
/// warning that cancellation of the other branches may differ.
/// </summary>
internal sealed class Elsa3ParallelBranchNamesRewrite : IElsa3NodeRewrite
{
    private const string BranchSlot = "Parallel.Branch";

    public Elsa3NodeRewriteKind Kind => Elsa3NodeRewriteKind.ParallelBranchNames;

    public ValueTask ApplyAsync(Elsa3RewriteContext context, CancellationToken cancellationToken)
    {
        var slots = context.Node.Slots;
        var branch = 0;
        for (var index = 0; index < slots.Count; index++)
        {
            if (slots[index].SlotName != BranchSlot)
                continue;
            branch++;
            slots[index] = slots[index] with { Label = $"Branch {branch}" };
        }

        // Elsa 3 Fork waits for all branches by default (elsa-core 5b0c2d73: JoinMode = ForkJoinMode.WaitAll), which is
        // what a Parallel without a threshold does.
        if (!context.TryGetProperty("JoinMode", out var property, out var joinMode) || !Elsa3TypedValues.IsBound(joinMode))
            return ValueTask.CompletedTask;

        var mode = joinMode.ValueKind switch
        {
            JsonValueKind.String => joinMode.GetString(),
            // ForkJoinMode: WaitAll = 0, WaitAny = 1.
            JsonValueKind.Number => joinMode.GetInt32() == 0 ? "WaitAll" : "WaitAny",
            _ => Elsa3TypedValues.TryGetLiteral(joinMode, out var literal) ? literal : null
        };
        mode = mode?.Trim() switch { "0" => "WaitAll", "1" => "WaitAny", var other => other };
        switch (mode?.Trim())
        {
            case "WaitAll" or "waitAll":
                context.Node.StructureFields["threshold"] = null;
                break;
            case "WaitAny" or "waitAny":
                WaitAny(context, context.Node.Resolved!.Entry!.FindMember("JoinMode")!, property);
                break;
            default:
                context.Error(UnsupportedMode,
                    $"Fork join mode '{mode ?? joinMode.GetRawText()}' has no Elsa 4 counterpart.",
                    "Use WaitAll or WaitAny.",
                    property);
                break;
        }

        return ValueTask.CompletedTask;
    }

    private static void WaitAny(Elsa3RewriteContext context, Elsa3MemberRule rule, string property)
    {
        context.Node.StructureFields["threshold"] = 1;
        if (rule.BehaviourWarning is { } warning)
            context.Warning(warning,
                "Fork WaitAny becomes a Parallel with threshold 1; cancellation of the other branches may differ.",
                "Review the converted workflow's behaviour for this difference.",
                property);
    }
}
