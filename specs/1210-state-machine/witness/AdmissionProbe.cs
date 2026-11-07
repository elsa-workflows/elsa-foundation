using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.StateMachine.M0.Witness;

internal static partial class Program
{
    private sealed record GatewayReceipt(string InputId, string Disposition, long? Order, DeliveryEvidence[] Attempts);

    private static ProbeInput BusinessInput(string id, bool parentFirst = false) =>
        new(id, JsonSerializer.SerializeToElement(new { parentFirst, payloadCanary = PayloadCanary }));

    // Fixture gateway only. A consumed physical wait is not a semantic admission result. This bounded
    // retry resolves a fresh ordinary carrier, while the business ID and content remain unchanged.
    private static async Task<GatewayReceipt> AdmitThroughCarrierAsync(WorkflowExecutionHarness harness, ProbeInput input, BookmarkState? firstWait = null)
    {
        var attempts = new List<DeliveryEvidence>();
        var wait = firstWait;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            wait ??= await WaitAsync(harness, ProbeNames.Inbox);
            var delivery = await ResumeAsync(harness, wait, input);
            attempts.Add(delivery);
            var state = (await SnapshotAsync(harness)).RequiredState;
            if (state.Receipts.TryGetValue(input.Id, out var receipt))
                return new(input.Id, JsonElement.DeepEquals(receipt.Content, input.Content) ? "AcceptedReceipt" : "Conflict", receipt.Order, attempts.ToArray());
            var decision = state.Decisions.LastOrDefault(x => x.CarrierInvocationId == delivery.ActivityExecutionId && x.InputId == input.Id);
            if (decision?.Status == "Full") return new(input.Id, "Full", null, attempts.ToArray());
            wait = null;
        }
        return new(input.Id, "Unknown", null, attempts.ToArray());
    }

    private static async Task AdmissionGenerationAsync(WorkflowExecutionHarness harness, string directory, string scenario, int generation)
    {
        var observations = new List<object>();
        try
        {
            if (generation == 1)
            {
                var publication = await harness.PublishAsync(Executable(false), "m0-publication");
                await harness.StartPublishedAsync(publication, WorkflowId, partition: Partition);
                observations.Add(await AdmitThroughCarrierAsync(harness, BusinessInput("A", parentFirst: true)));
                await ResumeAsync(harness, await WaitAsync(harness, ProbeNames.Action), new ActionSignal());
                var blocked = (await SnapshotAsync(harness)).RequiredState;
                Check(blocked.AwaitingRecovery && blocked.InFlight == "A" && blocked.Pending.Count == 0 && blocked.Processed.Count == 0,
                    "N7: one selected guard must block evaluation while leaving one pending slot free.");

                GatewayReceipt[] results;
                if (scenario is "admission-race" or "admission-capacity-control")
                {
                    var captured = await WaitAsync(harness, ProbeNames.Inbox);
                    results = await Task.WhenAll(AdmitThroughCarrierAsync(harness, BusinessInput("B"), captured),
                        AdmitThroughCarrierAsync(harness, BusinessInput("C"), captured));
                }
                else
                    results = [await AdmitThroughCarrierAsync(harness, BusinessInput("B")), await AdmitThroughCarrierAsync(harness, BusinessInput("C"))];
                observations.Add(new { competingSenders = results });
                var after = (await SnapshotAsync(harness)).RequiredState;
                Check(after.Pending.Count + (after.InFlight is null ? 0 : 1) == NativeProbeBase.PendingCapacity &&
                      results.Count(x => x.Disposition == "AcceptedReceipt") == 1 && results.Count(x => x.Disposition == "Full") == 1,
                    "N7: selected work must occupy one pending-capacity slot.");
                Check(after.AwaitingRecovery && after.Processed.Count == 0 && after.NextOrder == 2,
                    "N7: admission during recovery must commit without evaluating or bypassing the blocked phase.");

                // Intentionally discard this transport response. The next process resolves the business
                // ID from committed native state, not from a response object or this process's actors.
                var acceptedId = after.Pending.Single();
                await ResumeAsync(harness, await WaitAsync(harness, ProbeNames.Inbox), BusinessInput(acceptedId));
                observations.Add(new { ignoredDuplicateResponseFor = acceptedId, boundary = "request completed before response was ignored; not a provider-commit failure" });
            }
            else
            {
                var restored = await SnapshotAsync(harness);
                await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.restored.json"), restored);
                await CheckOriginalRootAsync(directory, scenario, restored);
                if (generation == 2)
                {
                    var state = restored.RequiredState;
                    Check(state.AwaitingRecovery && state.InFlight == "A" && state.Pending.Count == 1 && state.Receipts.Count == 2,
                        "N7: cold read must retain selected work and the input admitted during recovery.");
                    var acceptedId = state.Pending.Single();
                    var duplicate = await AdmitThroughCarrierAsync(harness, BusinessInput(acceptedId));
                    var conflict = await AdmitThroughCarrierAsync(harness, BusinessInput(acceptedId, parentFirst: true));
                    observations.Add(new { lostResponseResolvedFromReceipt = state.Receipts[acceptedId], duplicate, conflict });
                    Check(duplicate.Disposition == "AcceptedReceipt" && duplicate.Order == 2 && conflict.Disposition == "Conflict" &&
                          (await SnapshotAsync(harness)).RequiredState.NextOrder == 2 && (await SnapshotAsync(harness)).RequiredState.Processed.Count == 0,
                        "N7: blocked duplicate/conflict retries must preserve the original position without evaluation.");
                    observations.Add(await RetryAsync(harness, state.FailedChild!));
                    var drained = (await SnapshotAsync(harness)).RequiredState;
                    Check(drained.Processed.SequenceEqual(["A", acceptedId]) && drained.ExitCompletions == 1,
                        "N7: recovery must drain in committed order without selecting A or executing Exit twice.");
                    var rejectedId = acceptedId == "B" ? "C" : "B";
                    var admittedLater = await AdmitThroughCarrierAsync(harness, BusinessInput(rejectedId));
                    observations.Add(new { previouslyRejectedId = rejectedId, admittedLater });
                    Check(admittedLater.Disposition == "AcceptedReceipt" && admittedLater.Order == 3,
                        "N7: a full rejection must not reserve an ID or admission position.");
                    for (var order = 4; order <= NativeProbeBase.ReceiptBudget; order++)
                    {
                        var admitted = await AdmitThroughCarrierAsync(harness, BusinessInput("fill-" + order));
                        observations.Add(admitted);
                        Check(admitted.Disposition == "AcceptedReceipt" && admitted.Order == order, "N7: each retained receipt must preserve its own business ID and order.");
                    }
                    var settled = (await SnapshotAsync(harness)).RequiredState;
                    Check(settled.Receipts.Count == NativeProbeBase.ReceiptBudget && settled.Pending.Count == 0 && settled.InFlight is null &&
                          settled.Receipts.Values.All(x => x.Status == "Handled"), "N7: receipt budget must be exercised independently of pending capacity.");
                    var overBudget = await AdmitThroughCarrierAsync(harness, BusinessInput("over-budget"));
                    var retainedRetry = await AdmitThroughCarrierAsync(harness, BusinessInput("fill-" + NativeProbeBase.ReceiptBudget));
                    observations.Add(new { overBudget, retainedRetry });
                    Check(overBudget.Disposition == "Full" && retainedRetry.Disposition == "AcceptedReceipt" && retainedRetry.Order == NativeProbeBase.ReceiptBudget,
                        "N7: a full receipt budget must preserve an accepted ID's original result and position.");
                }
                else if (generation == 3)
                {
                    Check(restored.RequiredState.Receipts.Count == NativeProbeBase.ReceiptBudget && restored.RequiredState.Processed.Count == NativeProbeBase.ReceiptBudget,
                        "N7: settled retained receipts must survive another process restart.");
                    await harness.CancelAsync(WorkflowId, Partition);
                }
                else
                {
                    var terminal = restored.Terminal ?? throw new InvalidOperationException("N7: cancelled terminal receipt view is absent.");
                    var prior = JsonSerializer.Deserialize<Snapshot>(await File.ReadAllTextAsync(Path.Join(directory, scenario + "-g2.json")), ProbeNames.Json)!;
                    CheckTerminal(restored, scenario, prior.RequiredState.Receipts.Values.OrderBy(x => x.Order).Select(x => x.Id).ToArray());
                    Check(terminal.Receipts.Length == NativeProbeBase.ReceiptBudget && terminal.Receipts.All(x => x.Status == "Handled"),
                        "N7: cancellation must not relabel already handled inputs.");
                }
            }
            await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.json"), await SnapshotAsync(harness));
            await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.admission.json"), observations);
            Console.WriteLine($"{scenario} generation {generation}: admission assertions passed.");
        }
        catch
        {
            await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.admission.json"), observations);
            await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.failed-state.json"), await SnapshotAsync(harness));
            throw;
        }
    }

    private static async Task<object> CheckNestedAsync(WorkflowExecutionHarness harness, Snapshot snapshot)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        var states = await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(WorkflowId);
        var machine = states.Single(x => x.Execution.ExecutableNodeId == ProbeNames.Root);
        var outer = states.Single(x => x.Execution.ExecutableNodeId == "m0-outer-sequence");
        Check(machine.ParentActivityExecutionId == outer.InvocationId, "N8: the same machine must be an actual native child of the ordinary Sequence.");
        var consumers = states.Where(x => x.Execution.ExecutableNodeId == "m0-result-consumer").ToArray();
        if (snapshot.RootStatus != "Completed")
        {
            Check(consumers.Length == 0 && outer.Status == ActivityExecutionStatus.Running, "N8: the caller must wait for the nested machine to complete.");
            return new { outer.InvocationId, machineInvocationId = machine.InvocationId, machine.ParentActivityExecutionId, consumerCount = consumers.Length, outerStatus = outer.Status.ToString() };
        }
        Check(consumers.Length == 1 && consumers[0].Status == ActivityExecutionStatus.Completed && consumers[0].Attempts?.Count == 1 &&
              outer.Status == ActivityExecutionStatus.Completed && snapshot.WorkflowStatus == "Completed",
            "N8: the surrounding Sequence must continue once through the ordinary result consumer.");
        var consumption = consumers[0].Completion!.Result.InlineValue!.Value.Deserialize<ReceiptConsumption>(ProbeNames.Json)!;
        Check(consumption.RootInvocationId == machine.InvocationId && consumption.InputIds.SequenceEqual(["A", "B", "stop"]) && consumption.ReceiptCount == 3,
            "N8: the ordinary activity must receive the nested machine's declared typed result through normal binding.");
        Check(machine.Completion?.OutcomeKey == ActivityOutcomes.Done && outer.Completion?.OutcomeKey == ActivityOutcomes.Done,
            "N8: the nested machine and surrounding Sequence must preserve the normal declared completion outcome.");
        return new { outer.InvocationId, machineInvocationId = machine.InvocationId, machine.ParentActivityExecutionId,
            consumerInvocationId = consumers[0].InvocationId, boundInput = consumers[0].InputSnapshot, consumption,
            machineOutcome = machine.Completion!.OutcomeKey, outerOutcome = outer.Completion!.OutcomeKey };
    }
}
