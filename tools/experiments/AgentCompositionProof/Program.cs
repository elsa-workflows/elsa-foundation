using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Elsa.UnifiedExecution.WorkProof;

namespace Elsa.AgentCompositionProof;

internal static class Program
{
    private const string ProofKey = "elsa-agent-composition-proof-local-key-2026";
    private const int ChildFailureExitCode = 74;
    private static int _assertions;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--agent-worker": return await AgentWorker.RunAsync(args);
                case "--tool-worker": return await AgentToolWorker.RunAsync(args);
                case "--resume-fanin": return ResumeFanIn(args);
                case "--inspect-collaboration": return InspectCollaboration(args);
                case "--drive-collaboration": return await DriveCollaboration(args);
                case "--resume-collaboration": return await ResumeCollaboration(args);
                default: throw new ArgumentException($"Unknown command '{args[0]}'.");
            }
        }

        string runRoot = Path.Combine(Path.GetTempPath(), "elsa-agent-composition-proof", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runRoot);
        string workProofStatus = ReadGitOutput("status", "--porcelain", "--", "tools/experiments/UnifiedExecution.WorkProof");
        var evidence = new Dictionary<string, object?>
        {
            ["claimBoundary"] = "Candidate native agent composition policy over the experimental UnifiedExecution.WorkProof substrate; not production Elsa integration and not exact Microsoft Agent Framework compatibility.",
            ["workProofProject"] = "tools/experiments/UnifiedExecution.WorkProof/UnifiedExecution.WorkProof.csproj (referenced unchanged)",
            ["repositoryHead"] = ReadGitOutput("rev-parse", "HEAD"),
            ["workProofSourceStatus"] = string.IsNullOrEmpty(workProofStatus) ? "clean" : workProofStatus,
            ["adapter"] = "Deterministic fake agent adapter; no LLM, external service, or network calls.",
            ["recordedUtc"] = DateTimeOffset.UtcNow,
            ["runtime"] = Environment.Version.ToString(),
            ["harnessProcessId"] = Environment.ProcessId,
            ["runCommand"] = "dotnet run --project tools/experiments/AgentCompositionProof/AgentCompositionProof.csproj",
            ["verificationCommands"] = new[]
            {
                "dotnet build tools/experiments/AgentCompositionProof/AgentCompositionProof.csproj --no-restore",
                "dotnet run --project tools/experiments/AgentCompositionProof/AgentCompositionProof.csproj --no-build"
            },
            ["frameworkSemanticsProbe"] = new
            {
                path = "tools/experiments/UnifiedExecution.AgentFrameworkProbe/evidence.json",
                package = "Microsoft.Agents.AI.Workflows 1.25.0",
                assemblyInformationalVersion = "1.25.0+95511ff025d81cc546c44e653d4be65de5bd7f35",
                sourceObservationCommit = "44fe223da7ea6853eefb7053e25c2c213a2e5c15",
                runCommand = "python3 fetch_feed.py; python3 prepare_package_cache.py; dotnet run --configuration Release",
                packageCommitProvenance = "The package nuspec does not declare that source commit; source observation is separate."
            },
            ["recommendation"] = "Continue native-composite design; these candidate semantics do not warrant a separate execution model. This is not production acceptance.",
            ["hostingClassification"] = "open",
            ["independentReview"] = "Required by issue #2913; pending parent review of this isolated experiment.",
            ["issue2913Coverage"] = new[]
            {
                "Sequence before/composite/after lifecycle and sequential parent behavior",
                "Researcher and Specialist concurrent work, Reviewer complete-round barrier",
                "One bounded manager feedback round, exact tool approval, approval race, idempotent tool effect recovery",
                "Private/conversation-shared/versioned knowledge references and denied access",
                "Persisted configuration and deterministic usage/budget accounting",
                "Fresh-process accepted-decision and fan-in recovery without re-running accepted model decisions",
                "Conflicting message/result, early release, cancellation, budget admission boundaries, and exact tool invocation binding"
            },
            ["limitations"] = new[]
            {
                "SQLite WAL and child processes exercise one-machine durability and ownership, not a multi-node cluster or distributed transport.",
                "The destination is a local idempotent SQLite test sink; production connector semantics, credentials, backpressure, and retry policies are not tested.",
                "Memory references are read-only pinned snapshots. External memory writes, consistency under mutation, real approval identity, and cancellation compensation are not implemented.",
                "If a live model response exists only in worker memory and is lost before result acceptance, provider-specific retry cost and duplicate-decision behavior are outside this deterministic probe.",
                "Usage admission uses fixed deterministic test units per agent and manager step; it does not guarantee a real model's token or monetary cost.",
                "This candidate policy proof does not integrate with Elsa production APIs or Studio, and does not prove exact compatibility with another agent framework."
            }
        };

        try
        {
            evidence["typedReferences"] = ProbeTypedReferences();
            evidence["policyRejectionFixtures"] = ProbePolicyRejections();
            evidence["exhaustedUsageBudget"] = ProbeExhaustedUsageBudget();
            evidence["usageBudgetAdmissionBoundaries"] = await ProbeUsageBudgetAdmissionBoundaries(runRoot);
            evidence["enclosingSequenceCollaboration"] = await ProbeEnclosingSequenceCollaboration(runRoot);
            evidence["receiverMailbox"] = await ProbeMailboxAndParallelReceivers(runRoot);
            evidence["roundBarrier"] = await ProbeRoundBarrier(runRoot);
            evidence["bufferedFanInRestore"] = await ProbeFanInRestore(runRoot);
            evidence["cancellation"] = await ProbeCancellation(runRoot);
            evidence["recoveryAfterEffect"] = await ProbeRecoveryAfterEffect(runRoot);
            evidence["assertionCount"] = _assertions;
            evidence["outcome"] = "passed";
        }
        catch (Exception exception)
        {
            evidence["assertionCount"] = _assertions;
            evidence["outcome"] = "failed";
            evidence["error"] = $"{exception.GetType().Name}: {exception.Message}";
            evidence["diagnosticDirectory"] = runRoot;
            string failedJson = JsonSerializer.Serialize(evidence, JsonOptions);
            await File.WriteAllTextAsync(EvidencePath(), failedJson + Environment.NewLine);
            Console.Error.WriteLine(failedJson);
            return 1;
        }

        string json = JsonSerializer.Serialize(evidence, JsonOptions);
        string evidencePath = EvidencePath();
        await File.WriteAllTextAsync(evidencePath, json + Environment.NewLine);
        Console.WriteLine(json);
        Console.WriteLine($"Evidence written to {evidencePath}");
        TryDeleteDirectory(runRoot);
        return 0;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static object ProbeTypedReferences()
    {
        var resolver = new ResourceReferenceResolver();
        WorkScope scope = NewScope();
        ResourceReference[] valid = DefaultReferences();
        ResolvedReferenceSnapshot[] resolved = resolver.ResolveAll(valid, scope).ToArray();
        Assert(resolved.Length == 2 && resolved.Any(x => x.Kind == "memory") && resolved.Any(x => x.Kind == "knowledge"),
            "Memory and knowledge resource references resolve through their typed adapters.");

        Expect<InvalidOperationException>(() => resolver.ResolveMemory(valid[1], scope), "wrong resource kind is rejected");
        Expect<KeyNotFoundException>(() => resolver.ResolveAll(
            [new MemoryReference(scope.TenantId, scope.PersistenceScope, scope.Authority, "missing", "r1")], scope), "missing resource is rejected");
        WorkScope foreignScope = new HmacWorkScopeAuthorizer(ReadKey()).Issue("tenant-blue", "shared-persistence", "agent-host");
        Expect<UnauthorizedAccessException>(() => resolver.ResolveAll(valid, foreignScope), "foreign scope is rejected");

        MemoryReference privateMemory = PrivateMemory(scope, "Researcher", "researcher-private", "r2");
        MemoryReference conversationMemory = ConversationMemory(scope, "conversation-42", "shared-conversation", "r5");
        Assert(resolver.ResolveAll([privateMemory, conversationMemory], scope, "Researcher", "conversation-42").Count == 2,
            "The private owner and conversation participants can read their scoped memory snapshots.");
        Expect<UnauthorizedAccessException>(() => resolver.ResolveAll([privateMemory], scope, "Specialist", "conversation-42"),
            "private memory is hidden from another participant");
        Expect<UnauthorizedAccessException>(() => resolver.ResolveAll([privateMemory with { Permission = MemoryPermission.ReadWrite }], scope, "Researcher", "conversation-42"),
            "write access is denied by the read-only memory adapter");
        Expect<UnauthorizedAccessException>(() => resolver.ResolveAll([conversationMemory], scope, "Specialist", "conversation-elsewhere"),
            "conversation-shared memory is hidden outside its conversation");

        string serialized = ProofJson.Serialize(new AgentEnvelope("agent-A", 0, "invocation-A", "message-A", "input", valid));
        Assert(!serialized.Contains("private context alpha", StringComparison.Ordinal)
            && !serialized.Contains("curated fact omega", StringComparison.Ordinal),
            "Serialized route envelope carries typed resource identities, not resolved resource content.");
        AgentEnvelope decoded = ProofJson.Deserialize<AgentEnvelope>(serialized);
        Assert(decoded.ReceiverId == "agent-A" && decoded.ResourceReferences[0] is MemoryReference
            && decoded.ResourceReferences[1] is KnowledgeReference, "Routing and resource-reference types survive serialization independently.");

        return new
        {
            acceptedKinds = resolved.Select(x => x.Kind).Order(StringComparer.Ordinal).ToArray(),
            stableIdentities = resolved.Select(x => x.StableIdentity).ToArray(),
            contentSha256 = resolved.Select(x => x.ContentSha256).ToArray(),
            wrongKindRejected = true,
            missingRejected = true,
            foreignScopeRejected = true,
            privateAndConversationVisibilityEnforced = true,
            memoryWritesDeniedByReadOnlyAdapter = true,
            contentExcludedFromDispatch = true
        };
    }

    private static object ProbePolicyRejections()
    {
        ResourceReference[] refs = DefaultReferences();
        var mailbox = new ReceiverMailboxPolicy();
        AgentEnvelope mailboxMessage = Envelope("agent-A", 0, "mail-A", "mail-message", refs);
        CompositionPlan mailboxStart = mailbox.Start(ProofJson.Serialize(new MailboxStartInput([mailboxMessage])));
        var wrongReceiver = new AgentExecutionResult("mail-A", "agent-B", 0, "mail-message", "fingerprint", [], null);
        Expect<InvalidOperationException>(() => mailbox.ApplyResult(mailboxStart.StateJson, Receipt(wrongReceiver)),
            "mailbox cannot use a forged result route to advance another receiver queue");
        Expect<InvalidOperationException>(() => mailbox.Start(ProofJson.Serialize(new MailboxStartInput([]))),
            "empty mailbox is rejected rather than left permanently running");

        var rounds = new RoundBarrierPolicy();
        AgentEnvelope roundMessage = Envelope("agent-A", 0, "round-A", "round-message", refs);
        CompositionPlan roundStart = rounds.Start(ProofJson.Serialize(new RoundStartInput([roundMessage])));
        var wrongRound = new AgentExecutionResult("round-A", "agent-A", 1, "round-message", "fingerprint", [], null);
        Expect<InvalidOperationException>(() => rounds.ApplyResult(roundStart.StateJson, Receipt(wrongRound)),
            "round barrier validates the pinned receiver and round for each receipt");
        Expect<InvalidOperationException>(() => rounds.Start(ProofJson.Serialize(new RoundStartInput(
            [roundMessage, roundMessage with { InvocationId = "round-A2", MessageId = "round-message-2" }]))),
            "round policy rejects same-receiver parallel work rather than claiming mailbox serialization");

        var fanIn = new BufferedFanInPolicy();
        AgentEnvelope goodSource = SourceEnvelope("source-good", "A", "same-message-id", "same-target", refs);
        AgentEnvelope unknownSource = goodSource with
        {
            InvocationId = "source-unknown",
            Emission = goodSource.Emission! with { SourceId = "X" }
        };
        Expect<InvalidOperationException>(() => fanIn.Start(ProofJson.Serialize(new FanInStartInput([unknownSource]))),
            "unknown fan-in source is rejected");
        AgentEnvelope wrongFanInRound = goodSource with
        {
            InvocationId = "source-wrong-round",
            Emission = goodSource.Emission! with { Message = goodSource.Emission.Message with { RoundId = 1 } }
        };
        Expect<InvalidOperationException>(() => fanIn.Start(ProofJson.Serialize(new FanInStartInput([wrongFanInRound]))),
            "fan-in rejects an unsupported message round");

        AgentEnvelope alteredDuplicate = goodSource with
        {
            InvocationId = "source-altered-duplicate",
            Emission = goodSource.Emission! with { Message = goodSource.Emission.Message with { Payload = "altered body" } }
        };
        CompositionPlan fanInStart = fanIn.Start(ProofJson.Serialize(new FanInStartInput([goodSource, alteredDuplicate])));
        var adapter = new DeterministicFakeAgentAdapter(new ResourceReferenceResolver());
        WorkScope scope = NewScope();
        CompositionPlan accepted = fanIn.ApplyResult(fanInStart.StateJson, Receipt(adapter.Execute(goodSource, scope)));
        Expect<InvalidOperationException>(() => fanIn.ApplyResult(accepted.StateJson, Receipt(adapter.Execute(alteredDuplicate, scope))),
            "same MessageId with changed payload is rejected as conflicting content");

        var collaboration = new AgentCollaborationPolicy(usageBudget: 8);
        CompositionPlan collaborationPlan = collaboration.Start(ProofJson.Serialize(CollaborationInput(scope)));
        while (ProofJson.Deserialize<AgentCollaborationPolicy.CollaborationState>(collaborationPlan.StateJson).Stage != "AwaitingToolApproval")
        {
            WorkDefinition[] batch = collaborationPlan.Work.ToArray();
            if (batch.Length == 0) throw new InvalidOperationException("Collaboration rejection fixture did not reach tool approval.");
            foreach (WorkDefinition work in batch)
            {
                AgentEnvelope envelope = ProofJson.Deserialize<AgentEnvelope>(work.InputJson);
                collaborationPlan = collaboration.ApplyResult(collaborationPlan.StateJson, Receipt(adapter.Execute(envelope, scope)));
            }
        }
        ToolActionRequest approvedRequest = ProofJson.Deserialize<AgentCollaborationPolicy.CollaborationState>(collaborationPlan.StateJson).PendingToolRequest!;
        CompositionPlan approvedTool = AgentCollaborationPolicy.CreateApprovalPlan(collaborationPlan.StateJson,
            approvedRequest.RequestId, approvedRequest.ToolName, approvedRequest.ArgumentsJson,
            approvedRequest.RequiredCapability, "fixture-approver");
        var forgedToolResult = new ToolActionResult(approvedRequest.RequestId, approvedRequest.ToolName,
            approvedRequest.ArgumentsFingerprint, "fixture-effect", "fixture-output");
        string forgedToolResultJson = ProofJson.Serialize(forgedToolResult);
        var wrongToolInvocationReceipt = new WorkReceipt("wrong-tool-invocation", "Succeeded", forgedToolResultJson,
            ResourceReferenceResolver.Sha256(forgedToolResultJson));
        Expect<InvalidOperationException>(() => collaboration.ApplyResult(approvedTool.StateJson, wrongToolInvocationReceipt),
            "tool result receipt must bind to the exact approved invocation ID");

        return new { outcome = "passed", rejected = new[] { "wrong mailbox receiver", "empty mailbox", "wrong round", "same-receiver parallel round", "unknown fan-in source", "unsupported fan-in round", "conflicting same-MessageId payload", "wrong approved tool invocation ID" } };
    }

    private static async Task<object> ProbeUsageBudgetAdmissionBoundaries(string runRoot)
    {
        int[] budgets = [0, 1, 2, 3, 4, 5, 6, 7];
        int[] expectedUsage = [0, 0, 2, 3, 4, 4, 6, 7];
        int[] expectedAgentWork = [0, 0, 2, 3, 3, 3, 5, 6];
        int[] expectedFollowUpRounds = [0, 0, 0, 0, 0, 0, 1, 1];
        var boundaryResults = new List<object>();

        for (int index = 0; index < budgets.Length; index++)
        {
            WorkScope scope = NewScope();
            var policy = new AgentCollaborationPolicy(budgets[index]);
            var adapter = new DeterministicFakeAgentAdapter(new ResourceReferenceResolver());
            CompositionPlan plan = policy.Start(ProofJson.Serialize(CollaborationInput(scope)));
            int agentWork = 0;

            for (int iteration = 0; iteration < 8; iteration++)
            {
                AgentCollaborationPolicy.CollaborationState state = ProofJson.Deserialize<AgentCollaborationPolicy.CollaborationState>(plan.StateJson);
                if (state.Stage == "BudgetExhausted") break;
                WorkDefinition[] batch = plan.Work.ToArray();
                if (batch.Length == 0) throw new InvalidOperationException($"Budget {budgets[index]} left collaboration at '{state.Stage}' without work or a terminal outcome.");

                foreach (WorkDefinition work in batch)
                {
                    AgentEnvelope envelope = ProofJson.Deserialize<AgentEnvelope>(work.InputJson);
                    plan = policy.ApplyResult(plan.StateJson, Receipt(adapter.Execute(envelope, scope)));
                    if (envelope.Configuration?.AgentId is "Researcher" or "Specialist" or "Reviewer") agentWork++;
                }
            }

            AgentCollaborationPolicy.CollaborationState final = ProofJson.Deserialize<AgentCollaborationPolicy.CollaborationState>(plan.StateJson);
            Assert(final.Stage == "BudgetExhausted" && final.Completed && plan.IsComplete && plan.Work.Count == 0,
                $"Budget {budgets[index]} commits a terminal exhaustion outcome without dispatching unadmitted work.");
            Assert(final.UsageUsed == expectedUsage[index] && final.UsageUsed <= final.UsageBudget,
                $"Budget {budgets[index]} never accounts more than the admitted deterministic work units.");
            Assert(agentWork == expectedAgentWork[index] && final.ActiveMessages.Length == 0,
                $"Budget {budgets[index]} leaves no unadmitted participant or reviewer work active.");
            Assert(final.Decisions.Length == policy.ManagerDecisionEvaluations,
                $"Budget {budgets[index]} persists exactly the manager decisions actually evaluated.");
            Assert(final.FollowUpRoundsUsed == expectedFollowUpRounds[index],
                $"Budget {budgets[index]} counts a follow-up only when its work is admitted.");
            boundaryResults.Add(new { budget = budgets[index], used = final.UsageUsed, dispatchedAgentWork = agentWork,
                managerDecisions = final.Decisions.Length, followUpRoundsUsed = final.FollowUpRoundsUsed, stage = final.Stage });
        }

        Harness h = NewHarness(runRoot, "budget-4-coordinator-drain");
        const int integrationBudget = 4;
        var integrationPolicy = new AgentCollaborationPolicy(integrationBudget);
        var coordinator = new CompositionCoordinator(h.Store, h.Scope, h.ExecutionId, "budget-boundary-owner", integrationPolicy);
        coordinator.Start(ProofJson.Serialize(CollaborationInput(h.Scope)));
        Assert((await WaitAsync(StartWorker(h, "sequence-before"))).ExitCode == 0 && coordinator.ApplyPendingResults() == 1,
            "Budget-4 coordinator accepts its Sequence before-child before agent work begins.");

        for (int iteration = 0; iteration < 8; iteration++)
        {
            AgentCollaborationPolicy.CollaborationState state = ReadCollaborationState(h);
            if (state.Stage == "BudgetExhausted") break;
            WorkSnapshot[] owed = h.Store.ReadWork(h.Scope, h.ExecutionId)
                .Where(x => x.Status == "owed" && x.HandlerId == AgentWorker.HandlerId).ToArray();
            if (owed.Length == 0) throw new InvalidOperationException($"Budget-4 coordinator left stage '{state.Stage}' without owed work.");
            ChildResult[] results = await Task.WhenAll(owed.Select(async work => await WaitAsync(StartWorker(h, work.InvocationId))));
            Assert(results.All(x => x.ExitCode == 0), "Budget-4 coordinator workers complete without exceeding admitted work.");
            Assert(coordinator.ApplyPendingResults() == owed.Length, "Budget-4 coordinator applies every successful receipt exactly once.");
        }

        AgentCollaborationPolicy.CollaborationState integrationFinal = ReadCollaborationState(h);
        ExecutionSnapshot execution = h.Store.ReadExecution(h.Scope, h.ExecutionId);
        Assert(integrationFinal.Stage == "BudgetExhausted" && execution.Status == "Completed"
            && integrationFinal.UsageUsed == integrationBudget && integrationFinal.FollowUpRoundsUsed == 0
            && integrationFinal.Decisions.Single().Choice == "follow-up",
            "Budget-4 coordinator terminalizes after admitting the manager decision but not the unfit follow-up round.");
        Assert(h.Store.ReadPendingResults(h.Scope, h.ExecutionId).Count == 0
            && h.Store.ReadWork(h.Scope, h.ExecutionId).All(x => x.Status != "owed")
            && h.Store.ReadWork(h.Scope, h.ExecutionId).All(x => x.InvocationId != "researcher-r2" && x.InvocationId != "specialist-r2"),
            "Budget-4 coordinator leaves no succeeded receipt wedged and never dispatches follow-up work beyond budget.");

        return new { outcome = "passed", fixedCostModel = "one deterministic test unit per participant, reviewer, and manager decision",
            boundaryResults, coordinatorBudget4 = new { status = execution.Status, stage = integrationFinal.Stage,
                usageUsed = integrationFinal.UsageUsed, pendingReceipts = h.Store.ReadPendingResults(h.Scope, h.ExecutionId).Count } };
    }

    private static object ProbeExhaustedUsageBudget()
    {
        WorkScope scope = NewScope();
        var policy = new AgentCollaborationPolicy(usageBudget: 7);
        var adapter = new DeterministicFakeAgentAdapter(new ResourceReferenceResolver());
        CollaborationStartInput input = CollaborationInput(scope);
        CompositionPlan plan = policy.Start(ProofJson.Serialize(input));
        plan = policy.ApplyResult(plan.StateJson, Receipt(adapter.Execute(ProofJson.Deserialize<AgentEnvelope>(plan.Work.Single().InputJson), scope)));
        plan = ApplySyntheticBatch(policy, plan.StateJson, plan.Work, adapter, scope);
        plan = ApplySyntheticBatch(policy, plan.StateJson, plan.Work, adapter, scope);
        plan = ApplySyntheticBatch(policy, plan.StateJson, plan.Work, adapter, scope);
        plan = ApplySyntheticBatch(policy, plan.StateJson, plan.Work, adapter, scope);
        AgentCollaborationPolicy.CollaborationState state = ProofJson.Deserialize<AgentCollaborationPolicy.CollaborationState>(plan.StateJson);
        Assert(plan.IsComplete && plan.Work.Count == 0 && state.Stage == "BudgetExhausted",
            "Exhausted usage budget stops before an unbudgeted second manager decision or tool dispatch.");
        Assert(state.UsageUsed == 7 && state.Decisions.Length == 1 && policy.ManagerDecisionEvaluations == 1,
            "Usage and the accepted follow-up decision remain exactly accounted for at exhaustion.");
        return new { outcome = "passed", usageBudget = state.UsageBudget, usageUsed = state.UsageUsed,
            acceptedManagerDecisions = state.Decisions.Length, managerEvaluations = policy.ManagerDecisionEvaluations,
            stage = state.Stage, toolDispatched = false };
    }

    private static CompositionPlan ApplySyntheticBatch(AgentCollaborationPolicy policy, string stateJson,
        IReadOnlyList<WorkDefinition> work, DeterministicFakeAgentAdapter adapter, WorkScope scope)
    {
        CompositionPlan current = new(stateJson, work, [], false);
        foreach (WorkDefinition item in work)
        {
            AgentEnvelope envelope = ProofJson.Deserialize<AgentEnvelope>(item.InputJson);
            current = policy.ApplyResult(current.StateJson, Receipt(adapter.Execute(envelope, scope)));
        }
        return current;
    }

    private static async Task<object> ProbeEnclosingSequenceCollaboration(string runRoot)
    {
        Harness h = NewHarness(runRoot, "sequence-collaboration");
        CollaborationStartInput input = CollaborationInput(h.Scope);
        var policy = new AgentCollaborationPolicy(usageBudget: 8);
        var coordinator = new CompositionCoordinator(h.Store, h.Scope, h.ExecutionId, "sequence-collaboration-owner", policy);
        coordinator.Start(ProofJson.Serialize(input));
        Assert(h.Store.ReadWork(h.Scope, h.ExecutionId).Select(x => x.InvocationId).SequenceEqual(["sequence-before"]),
            "Enclosing Sequence starts only its before-child before entering collaboration.");
        Assert((await WaitAsync(StartWorker(h, "sequence-before"))).ExitCode == 0 && coordinator.ApplyPendingResults() == 1,
            "Sequence before-child completes before collaboration work is dispatched.");

        string[] roundOneWorkers = ["researcher-r1", "specialist-r1"];
        Assert(roundOneWorkers.All(id => h.Store.ReadWork(h.Scope, h.ExecutionId).Any(x => x.InvocationId == id)),
            "Researcher and Specialist dispatch concurrently after the Sequence before-child.");
        h.Store.CreateBarrier("collaboration-researcher-r1-gate");
        h.Store.CreateBarrier("collaboration-specialist-r1-gate");
        RunningChild researcher = StartWorker(h, "researcher-r1", "collaboration-researcher-r1-gate");
        RunningChild specialist = StartWorker(h, "specialist-r1", "collaboration-specialist-r1-gate");
        await WaitForBarrier(h.Store, "collaboration-researcher-r1-gate", 1);
        await WaitForBarrier(h.Store, "collaboration-specialist-r1-gate", 1);
        int[] parallelPids = h.Store.BarrierProcessIds("collaboration-researcher-r1-gate")
            .Concat(h.Store.BarrierProcessIds("collaboration-specialist-r1-gate")).ToArray();
        Assert(parallelPids.Distinct().Count() == 2, "Researcher and Specialist overlap in independent held OS processes.");
        Assert(h.Store.ReadWork(h.Scope, h.ExecutionId).All(x => x.InvocationId != "reviewer-r1"),
            "Reviewer is not dispatched before the intended round is complete.");

        h.Store.ReleaseBarrier("collaboration-researcher-r1-gate");
        Assert((await WaitAsync(researcher)).ExitCode == 0 && coordinator.ApplyPendingResults() == 1,
            "Researcher result is accepted while Specialist is held.");
        Assert(h.Store.ReadWork(h.Scope, h.ExecutionId).All(x => x.InvocationId != "reviewer-r1"),
            "Partial round does not release Reviewer early.");
        h.Store.ReleaseBarrier("collaboration-specialist-r1-gate");
        Assert((await WaitAsync(specialist)).ExitCode == 0 && coordinator.ApplyPendingResults() == 1,
            "Specialist completion closes the first participant round.");
        WorkSnapshot reviewerOneWork = h.Store.ReadWork(h.Scope, h.ExecutionId).Single(x => x.InvocationId == "reviewer-r1");
        AgentEnvelope reviewerOneEnvelope = ProofJson.Deserialize<AgentEnvelope>(reviewerOneWork.InputJson);
        Assert(reviewerOneEnvelope.ContextMessageIds?.Length == 2
            && reviewerOneEnvelope.ContextMessageIds.Contains("researcher-message-r1", StringComparer.Ordinal)
            && reviewerOneEnvelope.ContextMessageIds.Contains("specialist-message-r1", StringComparer.Ordinal),
            "Reviewer receives both intended participant message IDs at the round barrier.");
        Assert(reviewerOneEnvelope.ResourceReferences.OfType<MemoryReference>().Any(x => x.Visibility == MemoryVisibility.ConversationShared)
            && reviewerOneEnvelope.ResourceReferences.OfType<KnowledgeReference>().Any(),
            "Reviewer bindings separately identify conversation memory and versioned knowledge.");

        Assert((await WaitAsync(StartWorker(h, "reviewer-r1"))).ExitCode == 0 && coordinator.ApplyPendingResults() == 1,
            "Reviewer result is accepted and the manager persists its first bounded follow-up decision.");
        AgentCollaborationPolicy.CollaborationState afterDecisionOne = ReadCollaborationState(h);
        Assert(afterDecisionOne.Stage == "AgentRound2" && afterDecisionOne.FollowUpRoundsUsed == 1
            && afterDecisionOne.Decisions.Single().Choice == "follow-up", "Exactly one feedback round is durably requested.");

        ChildResult restartDecision = await WaitAsync(StartChild("--inspect-collaboration", h.WorkDb, h.SinkDb,
            EncodeScope(h.Scope), h.ExecutionId, "collaboration-inspector-after-decision"));
        Assert(restartDecision.ExitCode == 0, $"Decision restart inspection succeeded: {restartDecision.Output}");
        using JsonDocument decisionRestartDoc = JsonDocument.Parse(restartDecision.Output);
        Assert(decisionRestartDoc.RootElement.GetProperty("decisionCount").GetInt32() == 1
            && decisionRestartDoc.RootElement.GetProperty("managerDecisionEvaluations").GetInt32() == 0,
            "Fresh policy process sees the accepted follow-up decision without reevaluating it.");

        ChildResult drive = await WaitAsync(StartChild("--drive-collaboration", h.WorkDb, h.SinkDb, EncodeScope(h.Scope),
            h.ExecutionId, "collaboration-driver", "8"), TimeSpan.FromSeconds(90));
        Assert(drive.ExitCode == 0, $"Fresh driver reached the exact tool-approval boundary: {drive.Output}");
        using JsonDocument driveDoc = JsonDocument.Parse(drive.Output);
        JsonElement driven = driveDoc.RootElement;
        Assert(driven.GetProperty("stage").GetString() == "AwaitingToolApproval"
            && driven.GetProperty("decisionCount").GetInt32() == 2
            && driven.GetProperty("managerDecisionEvaluations").GetInt32() == 1,
            "Fresh driver makes only the not-yet-accepted second manager decision and reaches approval.");
        Assert(h.Store.ReadExecution(h.Scope, h.ExecutionId).Status == "Running"
            && h.Store.ReadWork(h.Scope, h.ExecutionId).All(x => x.InvocationId != "sequence-after"),
            "Neither awaiting approval nor accepted tool proposal prematurely completes the enclosing Sequence.");
        Assert(driven.GetProperty("usageUsed").GetInt32() == 8,
            "Persisted accounting includes participant, reviewer, and manager decision units within the configured budget.");
        JsonElement reviewerContexts = driven.GetProperty("reviewerContexts");
        Assert(reviewerContexts.GetArrayLength() == 2 && reviewerContexts.EnumerateArray().All(x => x.GetProperty("contextMessageCount").GetInt32() == 2),
            "Both Reviewer rounds receive two-message context after their barriers.");

        string acceptedStateJson = driven.GetProperty("stateJson").GetString()!;
        AgentCollaborationPolicy.CollaborationState awaiting = ProofJson.Deserialize<AgentCollaborationPolicy.CollaborationState>(acceptedStateJson);
        ToolActionRequest request = awaiting.PendingToolRequest!;
        Expect<UnauthorizedAccessException>(() => AgentCollaborationPolicy.CreateApprovalPlan(acceptedStateJson, request.RequestId,
            request.ToolName, "{\"category\":\"tampered\",\"title\":\"Review collaboration result\"}",
            request.RequiredCapability, "human-approver-test"), "altered tool arguments are not the approved request");

        CompositionPlan approvalPlan = AgentCollaborationPolicy.CreateApprovalPlan(acceptedStateJson, request.RequestId,
            request.ToolName, request.ArgumentsJson, request.RequiredCapability, "human-approver-test");
        string approvalCommitId = $"approval:{request.RequestId}";
        CoordinatorSnapshot coordinatorSnapshot = h.Store.ReadCoordinator(h.Scope, h.ExecutionId);
        CommitReceipt acceptedApproval = h.Store.CommitPlan(h.Scope, h.ExecutionId, coordinatorSnapshot.Owner, coordinatorSnapshot.Fence,
            driven.GetProperty("revision").GetInt64(), approvalCommitId, approvalPlan);
        Assert(acceptedApproval.Status == "Running", "Exact tool request and arguments are durably approved before tool work executes.");
        Expect<InvalidOperationException>(() => h.Store.CommitPlan(h.Scope, h.ExecutionId, coordinatorSnapshot.Owner,
            coordinatorSnapshot.Fence, driven.GetProperty("revision").GetInt64(), "approval-race-loser", approvalPlan),
            "second approval contender based on the same stale state revision loses the race");
        CommitReceipt replay = h.Store.CommitPlan(h.Scope, h.ExecutionId, coordinatorSnapshot.Owner, coordinatorSnapshot.Fence,
            driven.GetProperty("revision").GetInt64(), approvalCommitId, approvalPlan);
        Assert(replay.CommitId == acceptedApproval.CommitId
            && h.Store.ReadWork(h.Scope, h.ExecutionId).Count(x => x.InvocationId == "tool-action-1") == 1,
            "Exact approval replay is idempotent and creates one tool invocation.");

        ChildResult approvalRestart = await WaitAsync(StartChild("--inspect-collaboration", h.WorkDb, h.SinkDb,
            EncodeScope(h.Scope), h.ExecutionId, "collaboration-inspector-after-approval"));
        Assert(approvalRestart.ExitCode == 0, "Fresh process sees the accepted approval and pinned request before tool execution.");
        using JsonDocument approvalRestartDoc = JsonDocument.Parse(approvalRestart.Output);
        Assert(approvalRestartDoc.RootElement.GetProperty("approved").GetBoolean()
            && approvalRestartDoc.RootElement.GetProperty("managerDecisionEvaluations").GetInt32() == 0,
            "Restart preserves accepted tool decision/approval without model or manager reevaluation.");

        string toolSinkDb = Path.Combine(Path.GetDirectoryName(h.SinkDb)!, "tool-sink.db");
        var toolSink = new IndependentSink(toolSinkDb, new HmacWorkScopeAuthorizer(ReadKey()));
        ChildResult toolCrash = await WaitAsync(StartChild("--tool-worker", h.WorkDb, toolSinkDb, EncodeScope(h.Scope), h.ExecutionId,
            "tool-action-1", "tool-worker-first", "1", "crash-after-sink", "once"));
        Assert(toolCrash.ExitCode == ChildFailureExitCode && toolCrash.Output.Contains("toolEffectPersistedBeforeAcknowledgement", StringComparison.Ordinal),
            "Approved tool effect is persisted before simulated worker crash and missing acknowledgement.");
        ChildResult toolRetry = await WaitAsync(StartChild("--tool-worker", h.WorkDb, toolSinkDb, EncodeScope(h.Scope), h.ExecutionId,
            "tool-action-1", "tool-worker-retry", "1", "-", "wait"));
        Assert(toolRetry.ExitCode == 0 && toolRetry.Output.Contains("\"duplicateEffect\":true", StringComparison.Ordinal),
            "Tool retry uses the persisted idempotency identity and accepts the same effect once.");
        Assert(toolSink.CountEffects(h.Scope, h.ExecutionId) == 1 && toolSink.CountDeliveries(h.Scope, h.ExecutionId) == 2,
            "Crash/retry leaves one logical tool effect and two recorded deliveries.");

        ChildResult completed = await WaitAsync(StartChild("--resume-collaboration", h.WorkDb, h.SinkDb, EncodeScope(h.Scope),
            h.ExecutionId, "collaboration-completion-owner"), TimeSpan.FromSeconds(45));
        Assert(completed.ExitCode == 0, $"Fresh process recovered tool result and completed the enclosing Sequence: {completed.Output}");
        using JsonDocument completedDoc = JsonDocument.Parse(completed.Output);
        JsonElement final = completedDoc.RootElement;
        Assert(final.GetProperty("status").GetString() == "Completed"
            && final.GetProperty("managerDecisionEvaluations").GetInt32() == 0
            && final.GetProperty("persistedDecisionCount").GetInt32() == 2,
            "Completion restart consumes accepted tool result and returns Sequence output without rerunning manager decisions.");
        Assert(h.Store.ReadWork(h.Scope, h.ExecutionId).Any(x => x.InvocationId == "sequence-after" && x.Status == "succeeded"),
            "Enclosing Sequence after-child runs only after collaboration tool completion.");

        AgentCollaborationPolicy.CollaborationState finalState = ReadCollaborationState(h);
        Assert(finalState.AcceptedObservations.Any(x => x.Configuration?.AgentId == "Researcher" && x.ResolvedResources.Count == 3)
            && finalState.AcceptedObservations.Any(x => x.Configuration?.AgentId == "Specialist" && x.ResolvedResources.Count == 3),
            "Accepted participant observations persist versioned private/shared memory and knowledge snapshots.");

        return new
        {
            outcome = "passed",
            executionId = h.ExecutionId,
            enclosingSequence = new { beforeChild = "sequence-before", afterChild = "sequence-after", finalStatus = final.GetProperty("status").GetString() },
            parentCompletionHeldUntilAfterChild = true,
            participants = new[] { "Researcher", "Specialist", "Reviewer" },
            firstRoundWorkerProcessIds = parallelPids,
            reviewerContexts = reviewerContexts.EnumerateArray().Select(x => new { invocationId = x.GetProperty("invocationId").GetString(), messageCount = x.GetProperty("contextMessageCount").GetInt32() }).ToArray(),
            decisionsPersisted = final.GetProperty("persistedDecisionCount").GetInt32(),
            managerEvaluationsInFreshCompletionProcess = final.GetProperty("managerDecisionEvaluations").GetInt32(),
            usage = new { budget = finalState.UsageBudget, used = finalState.UsageUsed, configurationsPersisted = finalState.Configurations.Length },
            exactToolApproval = new { request.RequestId, request.ToolName, request.ArgumentsFingerprint, request.RequiredCapability, commitId = acceptedApproval.CommitId },
            approvalRace = new { alteredArgumentsRejected = true, staleApprovalContenderRejected = true, exactReplayIdempotent = true },
            toolEffect = new { sinkEffects = toolSink.CountEffects(h.Scope, h.ExecutionId), sinkDeliveries = toolSink.CountDeliveries(h.Scope, h.ExecutionId), retryAttempt = JsonDocument.Parse(toolRetry.Output).RootElement.GetProperty("attempt").GetInt32() },
            workInvocationCount = h.Store.CountWorkRows(h.Scope, h.ExecutionId)
        };
    }

    private static async Task<object> ProbeMailboxAndParallelReceivers(string runRoot)
    {
        Harness h = NewHarness(runRoot, "mailbox");
        ResourceReference[] refs = DefaultReferences();
        AgentEnvelope[] messages =
        [
            Envelope("agent-A", 0, "A1", "A1-message", refs),
            Envelope("agent-A", 0, "A2", "A2-message", refs),
            Envelope("agent-B", 0, "B1", "B1-message", refs)
        ];
        var coordinator = new CompositionCoordinator(h.Store, h.Scope, h.ExecutionId, "mailbox-owner", new ReceiverMailboxPolicy());
        coordinator.Start(ProofJson.Serialize(new MailboxStartInput(messages)));
        h.Store.CreateBarrier("mailbox-a1-gate");
        h.Store.CreateBarrier("mailbox-b1-gate");

        RunningChild a1 = StartWorker(h, "A1", "mailbox-a1-gate");
        RunningChild b1 = StartWorker(h, "B1", "mailbox-b1-gate");
        await WaitForBarrier(h.Store, "mailbox-a1-gate", 1);
        await WaitForBarrier(h.Store, "mailbox-b1-gate", 1);
        int[] processIds = h.Store.BarrierProcessIds("mailbox-a1-gate").Concat(h.Store.BarrierProcessIds("mailbox-b1-gate")).ToArray();
        Assert(processIds.Length == 2 && processIds.Distinct().Count() == 2, "Two receiver mailboxes are held concurrently by separate OS worker processes.");
        Assert(h.Store.ReadWork(h.Scope, h.ExecutionId).Select(x => x.InvocationId).Order(StringComparer.Ordinal).SequenceEqual(["A1", "B1"]),
            "Second message A2 remains serialized behind A1 while agent B work is independently dispatched.");

        h.Store.ReleaseBarrier("mailbox-b1-gate");
        ChildResult b1Result = await WaitAsync(b1);
        Assert(b1Result.ExitCode == 0, "B1 worker completes while A1 remains held.");
        Assert(coordinator.ApplyPendingResults() == 1, "B1 result is accepted before A1 is released.");
        Assert(h.Store.ReadWork(h.Scope, h.ExecutionId).All(x => x.InvocationId != "A2"), "A2 is not dispatched by B1 result application.");

        h.Store.ReleaseBarrier("mailbox-a1-gate");
        ChildResult a1Result = await WaitAsync(a1);
        Assert(a1Result.ExitCode == 0, "A1 worker completes after its barrier release.");
        Assert(coordinator.ApplyPendingResults() == 1, "A1 result is accepted and releases the next same-receiver message.");
        Assert(h.Store.ReadWork(h.Scope, h.ExecutionId).Any(x => x.InvocationId == "A2"), "A2 is dispatched only after A1 result acceptance.");
        ChildResult a2Result = await WaitAsync(StartWorker(h, "A2"));
        Assert(a2Result.ExitCode == 0 && coordinator.ApplyPendingResults() == 1, "A2 completes and the mailbox execution finishes.");
        Assert(h.Store.ReadExecution(h.Scope, h.ExecutionId).Status == "Completed", "Mailbox execution reaches Completed after both queues drain.");

        WorkSnapshot a1Snapshot = h.Store.ReadWork(h.Scope, h.ExecutionId).Single(x => x.InvocationId == "A1");
        AgentExecutionResult a1ExecutionResult = ProofJson.Deserialize<AgentExecutionResult>(a1Snapshot.ResultJson!);
        var a1Lease = new WorkLease(h.Scope, h.ExecutionId, "A1", a1Snapshot.Owner!, a1Snapshot.Fence, a1Snapshot.Attempt,
            a1Snapshot.HandlerId, a1Snapshot.HandlerVersion, a1Snapshot.InputJson);
        WorkReceipt duplicate = h.Store.CompleteWork(a1Lease, a1Snapshot.ResultJson!);
        Assert(duplicate.Duplicate && duplicate.Status == "Succeeded", "Repeating the same successful invocation result is identified as a duplicate result, not a new message.");
        Expect<InvalidOperationException>(() => h.Store.CompleteWork(a1Lease, "{\"conflicting\":true}"),
            "same invocation with conflicting result content is rejected");
        Assert(coordinator.ApplyPendingResults() == 0, "Duplicate completion does not create a second pending result event.");
        Assert(h.Sink.CountEffects(h.Scope, h.ExecutionId) == 3, "One idempotent destination effect exists per invocation.");

        return new
        {
            outcome = "passed",
            receiverAInvocationOrder = new[] { "A1", "A2" },
            otherReceiver = "B1 completed while A1 was held",
            workerProcessIds = processIds,
            invocationCount = h.Store.CountWorkRows(h.Scope, h.ExecutionId),
            effects = h.Sink.CountEffects(h.Scope, h.ExecutionId),
            repeatedSameResultMarkedDuplicate = duplicate.Duplicate,
            resolvedResourcesFromA1 = a1ExecutionResult.ResolvedResources
        };
    }

    private static async Task<object> ProbeRoundBarrier(string runRoot)
    {
        Harness h = NewHarness(runRoot, "rounds");
        ResourceReference[] refs = DefaultReferences();
        AgentEnvelope fast = Envelope("agent-A", 0, "fast-A", "fast-A-message", refs,
            new AgentEmission("A", Envelope("agent-A", 1, "A2", "A2-next-round", refs)));
        AgentEnvelope slow = Envelope("agent-B", 0, "slow-B", "slow-B-message", refs);
        var coordinator = new CompositionCoordinator(h.Store, h.Scope, h.ExecutionId, "round-owner", new RoundBarrierPolicy());
        coordinator.Start(ProofJson.Serialize(new RoundStartInput([fast, slow])));
        h.Store.CreateBarrier("round-slow-b-gate");
        RunningChild slowWorker = StartWorker(h, "slow-B", "round-slow-b-gate");
        await WaitForBarrier(h.Store, "round-slow-b-gate", 1);
        ChildResult fastResult = await WaitAsync(StartWorker(h, "fast-A"));
        Assert(fastResult.ExitCode == 0 && coordinator.ApplyPendingResults() == 1, "Fast round-zero result is accepted while slow-B remains held.");
        RoundBarrierPolicy.RoundState duringRound = ProofJson.Deserialize<RoundBarrierPolicy.RoundState>(h.Store.ReadExecution(h.Scope, h.ExecutionId).StateJson);
        Assert(duringRound.NextRoundMessages.Select(x => x.InvocationId).SequenceEqual(["A2"]), "Next-round message is durably buffered while the round remains open.");
        Assert(h.Store.ReadWork(h.Scope, h.ExecutionId).All(x => x.InvocationId != "A2"), "No next-round work is dispatched before the slow current-round result is accepted.");

        h.Store.ReleaseBarrier("round-slow-b-gate");
        Assert((await WaitAsync(slowWorker)).ExitCode == 0, "Slow round-zero worker completes after release.");
        Assert(coordinator.ApplyPendingResults() == 1, "Last current-round result is accepted.");
        Assert(h.Store.ReadWork(h.Scope, h.ExecutionId).Any(x => x.InvocationId == "A2"), "Round transition dispatches A2 only after all round-zero results are accepted.");
        Assert((await WaitAsync(StartWorker(h, "A2"))).ExitCode == 0 && coordinator.ApplyPendingResults() == 1,
            "A2 completes in round one.");
        Assert(h.Store.ReadExecution(h.Scope, h.ExecutionId).Status == "Completed", "Round-barrier execution completes after round one.");

        return new
        {
            outcome = "passed",
            currentRoundHeldAtBarrier = true,
            bufferedNextRoundMessages = duringRound.NextRoundMessages.Select(x => x.InvocationId).ToArray(),
            nextRoundDispatchObservedAfterCurrentRoundAccepted = true,
            status = h.Store.ReadExecution(h.Scope, h.ExecutionId).Status
        };
    }

    private static async Task<object> ProbeFanInRestore(string runRoot)
    {
        Harness h = NewHarness(runRoot, "fanin");
        ResourceReference[] refs = DefaultReferences();
        AgentEnvelope[] sourceWork =
        [
            SourceEnvelope("source-A1", "A", "fan-message-A1", "fanin-A1", refs),
            SourceEnvelope("source-A2", "A", "fan-message-A2", "fanin-A2", refs),
            SourceEnvelope("source-A-duplicate", "A", "fan-message-A1", "fanin-A1", refs),
            SourceEnvelope("source-B1", "B", "fan-message-B1", "fanin-B1", refs)
        ];
        var coordinator = new CompositionCoordinator(h.Store, h.Scope, h.ExecutionId, "fanin-owner", new BufferedFanInPolicy());
        coordinator.Start(ProofJson.Serialize(new FanInStartInput(sourceWork)));
        foreach (string invocation in new[] { "source-A1", "source-A2", "source-A-duplicate" })
        {
            ChildResult worker = await WaitAsync(StartWorker(h, invocation));
            Assert(worker.ExitCode == 0, $"Fan-in source worker {invocation} completed.");
        }
        Assert(coordinator.ApplyPendingResults() == 3, "All three A deliveries are accepted into durable fan-in policy state.");
        BufferedFanInPolicy.FanInState beforeRestore = ProofJson.Deserialize<BufferedFanInPolicy.FanInState>(h.Store.ReadExecution(h.Scope, h.ExecutionId).StateJson);
        Assert(beforeRestore.BufferedMessages.Length == 2 && beforeRestore.SeenSourceIds.SequenceEqual(["A"]),
            "Two distinct messages from A are both buffered while the same MessageId redelivery is deduplicated.");
        Assert(beforeRestore.DuplicateMessageDeliveries == 1 && !beforeRestore.Joined,
            "A repeated MessageId is counted once as duplicate and cannot satisfy missing source B.");

        WorkSnapshot a1Snapshot = h.Store.ReadWork(h.Scope, h.ExecutionId).Single(x => x.InvocationId == "source-A1");
        var a1Lease = new WorkLease(h.Scope, h.ExecutionId, "source-A1", a1Snapshot.Owner!, a1Snapshot.Fence, a1Snapshot.Attempt,
            a1Snapshot.HandlerId, a1Snapshot.HandlerVersion, a1Snapshot.InputJson);
        WorkReceipt duplicateResult = h.Store.CompleteWork(a1Lease, a1Snapshot.ResultJson!);
        Assert(duplicateResult.Duplicate && h.Store.ReadPendingResults(h.Scope, h.ExecutionId).Count == 0,
            "Same invocation/result replay is suppressed by the durable store and does not redeliver a composition message.");

        Assert((await WaitAsync(StartWorker(h, "source-B1"))).ExitCode == 0, "B source result becomes durable before fresh-process restoration.");
        string scope64 = EncodeScope(h.Scope);
        ChildResult restore = await WaitAsync(StartChild("--resume-fanin", h.WorkDb, h.SinkDb, scope64, h.ExecutionId, "fanin-owner-after-restart"));
        Assert(restore.ExitCode == 0, $"Fresh OS process restored fan-in state: {restore.Output}");
        using JsonDocument doc = JsonDocument.Parse(restore.Output);
        JsonElement summary = doc.RootElement;
        Assert(summary.GetProperty("appliedPendingResults").GetInt32() == 1, "Fresh coordinator applies the pending B source result.");
        Assert(summary.GetProperty("joined").GetBoolean() && summary.GetProperty("bufferedMessageCount").GetInt32() == 3,
            "Fresh coordinator restores two A messages, accepts B, and completes the join.");
        Assert(summary.GetProperty("resourceIdentityStable").GetBoolean(), "Fresh process resolves typed references to the same stable resource identities and content hashes.");
        Assert(summary.GetProperty("sourceWorkAttemptsAllOne").GetBoolean(), "Restoring partial fan-in does not rerun already completed A source work.");

        return new
        {
            outcome = "passed",
            preRestoreDistinctBufferedMessagesFromA = beforeRestore.BufferedMessages.Length,
            preRestoreDuplicateMessageIdDeliveries = beforeRestore.DuplicateMessageDeliveries,
            sameInvocationResultReplayCreatedNoMessage = duplicateResult.Duplicate,
            freshCoordinatorProcessId = summary.GetProperty("processId").GetInt32(),
            restoredMessageCount = summary.GetProperty("bufferedMessageCount").GetInt32(),
            stableResourceIdentityAcrossRestart = summary.GetProperty("resourceIdentityStable").GetBoolean(),
            sourceWorkAttemptsAllOne = summary.GetProperty("sourceWorkAttemptsAllOne").GetBoolean(),
            executionStatus = summary.GetProperty("status").GetString()
        };
    }

    private static async Task<object> ProbeCancellation(string runRoot)
    {
        Harness h = NewHarness(runRoot, "cancellation");
        AgentEnvelope[] messages = [Envelope("agent-A", 0, "cancelled-A1", "cancelled-message", DefaultReferences())];
        var coordinator = new CompositionCoordinator(h.Store, h.Scope, h.ExecutionId, "cancel-owner", new ReceiverMailboxPolicy());
        coordinator.Start(ProofJson.Serialize(new MailboxStartInput(messages)));
        h.Store.CreateBarrier("cancel-before-effect");
        RunningChild worker = StartWorker(h, "cancelled-A1", "cancel-before-effect");
        await WaitForBarrier(h.Store, "cancel-before-effect", 1);
        Assert(h.Store.CancelWork(h.Scope, h.ExecutionId, "cancelled-A1", "cancel-request-1"), "Durable cancellation marks the claimed invocation cancelled.");
        h.Store.ReleaseBarrier("cancel-before-effect");
        ChildResult child = await WaitAsync(worker);
        Assert(child.ExitCode == 0 && child.Output.Contains("LateRejected", StringComparison.Ordinal), "A late result from the cancelled worker is rejected.");
        Assert(coordinator.ApplyPendingResults() == 0 && h.Store.ReadPendingResults(h.Scope, h.ExecutionId).Count == 0,
            "Late rejection does not emit an accepted result event.");
        Assert(h.Sink.CountEffects(h.Scope, h.ExecutionId) == 1, "This WorkProof cancellation leaves the already-authorized external effect uncompensated.");

        return new
        {
            outcome = "passed",
            workStatus = h.Store.ReadWork(h.Scope, h.ExecutionId).Single().Status,
            lateResult = "LateRejected",
            acceptedResultEvents = h.Store.ReadPendingResults(h.Scope, h.ExecutionId).Count,
            effectsRemainAfterCancellation = h.Sink.CountEffects(h.Scope, h.ExecutionId),
            limitation = "Cancellation rejects late result acceptance; it does not roll back an external side effect or advance this candidate composition policy."
        };
    }

    private static async Task<object> ProbeRecoveryAfterEffect(string runRoot)
    {
        Harness h = NewHarness(runRoot, "recovery");
        var coordinator = new CompositionCoordinator(h.Store, h.Scope, h.ExecutionId, "recovery-owner", new ReceiverMailboxPolicy());
        coordinator.Start(ProofJson.Serialize(new MailboxStartInput([Envelope("agent-A", 0, "recover-A1", "recover-message", DefaultReferences())])));

        ChildResult crashed = await WaitAsync(StartWorker(h, "recover-A1", barrier: "-", leaseSeconds: 1, failpoint: "crash-after-sink"));
        Assert(crashed.ExitCode == ChildFailureExitCode && crashed.Output.Contains("effectPersistedBeforeResult", StringComparison.Ordinal),
            "First worker persists the destination effect and exits before committing its result.");
        ChildResult retry = await WaitAsync(StartWorker(h, "recover-A1", barrier: "-", leaseSeconds: 1, waitForClaim: true));
        Assert(retry.ExitCode == 0 && retry.Output.Contains("\"duplicateEffect\":true", StringComparison.Ordinal),
            "Retry takes the expired lease, re-delivers the same effect key idempotently, and commits the result.");
        Assert(coordinator.ApplyPendingResults() == 1 && h.Store.ReadExecution(h.Scope, h.ExecutionId).Status == "Completed",
            "Recovered durable result advances the composition policy exactly once.");
        Assert(h.Sink.CountEffects(h.Scope, h.ExecutionId) == 1 && h.Sink.CountDeliveries(h.Scope, h.ExecutionId) == 2,
            "Sink records one effect and two idempotent deliveries.");

        return new
        {
            outcome = "passed",
            crashExitCode = crashed.ExitCode,
            retry = JsonDocument.Parse(retry.Output).RootElement.Clone(),
            sinkEffects = h.Sink.CountEffects(h.Scope, h.ExecutionId),
            sinkDeliveries = h.Sink.CountDeliveries(h.Scope, h.ExecutionId),
            finalStatus = h.Store.ReadExecution(h.Scope, h.ExecutionId).Status
        };
    }

    private static int ResumeFanIn(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("Usage: --resume-fanin <workDb> <sinkDb> <scopeBase64> <executionId> <owner>");
        WorkScope scope = DecodeScope(args[3]);
        var authorizer = new HmacWorkScopeAuthorizer(ReadKey());
        var store = new DurableWorkStore(args[1], authorizer);
        _ = new IndependentSink(args[2], authorizer);
        string executionId = args[4];
        string owner = args[5];
        _ = store.AcquireExecution(scope, executionId, owner);
        var coordinator = new CompositionCoordinator(store, scope, executionId, owner, new BufferedFanInPolicy());
        coordinator.Activate();
        int applied = coordinator.ApplyPendingResults();
        ExecutionSnapshot execution = store.ReadExecution(scope, executionId);
        BufferedFanInPolicy.FanInState state = ProofJson.Deserialize<BufferedFanInPolicy.FanInState>(execution.StateJson);
        var resolver = new ResourceReferenceResolver();
        bool stable = state.BufferedMessages.All(buffered =>
            resolver.ResolveAll(buffered.Emission.Message.ResourceReferences, scope).SequenceEqual(buffered.ResolvedResources));
        bool attemptsOne = store.ReadWork(scope, executionId).All(x => x.Attempt == 1);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            processId = Environment.ProcessId,
            appliedPendingResults = applied,
            joined = state.Joined,
            bufferedMessageCount = state.BufferedMessages.Length,
            distinctMessageIds = state.SeenMessageIds,
            seenSourceIds = state.SeenSourceIds,
            duplicateMessageDeliveries = state.DuplicateMessageDeliveries,
            resourceIdentityStable = stable,
            sourceWorkAttemptsAllOne = attemptsOne,
            status = execution.Status
        }));
        return 0;
    }

    private static int InspectCollaboration(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("Usage: --inspect-collaboration <workDb> <sinkDb> <scopeBase64> <executionId> <owner>");
        WorkScope scope = DecodeScope(args[3]);
        var auth = new HmacWorkScopeAuthorizer(ReadKey());
        var store = new DurableWorkStore(args[1], auth);
        _ = new IndependentSink(args[2], auth);
        string executionId = args[4];
        string owner = args[5];
        _ = store.AcquireExecution(scope, executionId, owner);
        var freshPolicy = new AgentCollaborationPolicy(8);
        ExecutionSnapshot execution = store.ReadExecution(scope, executionId);
        AgentCollaborationPolicy.CollaborationState state = ProofJson.Deserialize<AgentCollaborationPolicy.CollaborationState>(execution.StateJson);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            processId = Environment.ProcessId,
            owner,
            revision = execution.Revision,
            stage = state.Stage,
            decisionCount = state.Decisions.Length,
            managerDecisionEvaluations = freshPolicy.ManagerDecisionEvaluations,
            approved = state.Approval is not null,
            pendingToolRequestId = state.PendingToolRequest?.RequestId,
            stateJson = execution.StateJson
        }));
        return 0;
    }

    private static async Task<int> DriveCollaboration(string[] args)
    {
        if (args.Length != 7) throw new ArgumentException("Usage: --drive-collaboration <workDb> <sinkDb> <scopeBase64> <executionId> <owner> <budget>");
        WorkScope scope = DecodeScope(args[3]);
        Harness h = HarnessFrom(args[1], args[2], scope, args[4]);
        string owner = args[5];
        int budget = int.Parse(args[6]);
        _ = h.Store.AcquireExecution(scope, h.ExecutionId, owner);
        var policy = new AgentCollaborationPolicy(budget);
        var coordinator = new CompositionCoordinator(h.Store, scope, h.ExecutionId, owner, policy);
        coordinator.Activate();

        AgentCollaborationPolicy.CollaborationState state;
        for (int iteration = 0; iteration < 12; iteration++)
        {
            coordinator.ApplyPendingResults();
            ExecutionSnapshot execution = h.Store.ReadExecution(scope, h.ExecutionId);
            state = ProofJson.Deserialize<AgentCollaborationPolicy.CollaborationState>(execution.StateJson);
            if (state.Stage is "AwaitingToolApproval" or "BudgetExhausted") break;

            WorkSnapshot[] owed = h.Store.ReadWork(scope, h.ExecutionId)
                .Where(x => x.Status == "owed" && x.HandlerId == AgentWorker.HandlerId).ToArray();
            if (owed.Length == 0)
            {
                if (h.Store.ReadPendingResults(scope, h.ExecutionId).Count > 0) continue;
                throw new InvalidOperationException($"Collaboration driver found no owed work or pending result at stage '{state.Stage}'.");
            }

            RunningChild[] workers = owed.Select(x => StartWorker(h, x.InvocationId)).ToArray();
            ChildResult[] results = await Task.WhenAll(workers.Select(async worker => await WaitAsync(worker)));
            if (results.Any(x => x.ExitCode != 0))
                throw new InvalidOperationException($"One or more deterministic collaboration workers failed: {string.Join(" | ", results.Select(x => x.Output))}");
        }

        ExecutionSnapshot finalExecution = h.Store.ReadExecution(scope, h.ExecutionId);
        state = ProofJson.Deserialize<AgentCollaborationPolicy.CollaborationState>(finalExecution.StateJson);
        if (state.Stage is not ("AwaitingToolApproval" or "BudgetExhausted"))
            throw new InvalidOperationException($"Collaboration driver exceeded its bounded steps at '{state.Stage}'.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            processId = Environment.ProcessId,
            revision = finalExecution.Revision,
            stage = state.Stage,
            decisionCount = state.Decisions.Length,
            managerDecisionEvaluations = policy.ManagerDecisionEvaluations,
            usageBudget = state.UsageBudget,
            usageUsed = state.UsageUsed,
            reviewerContexts = state.AcceptedObservations.Where(x => x.Configuration?.AgentId == "Reviewer")
                .Select(x => new { invocationId = x.InvocationId, contextMessageCount = x.ContextMessageIds?.Length ?? 0 }).ToArray(),
            stateJson = finalExecution.StateJson
        }));
        return 0;
    }

    private static async Task<int> ResumeCollaboration(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("Usage: --resume-collaboration <workDb> <sinkDb> <scopeBase64> <executionId> <owner>");
        WorkScope scope = DecodeScope(args[3]);
        Harness h = HarnessFrom(args[1], args[2], scope, args[4]);
        string owner = args[5];
        _ = h.Store.AcquireExecution(scope, h.ExecutionId, owner);
        var policy = new AgentCollaborationPolicy(8);
        var coordinator = new CompositionCoordinator(h.Store, scope, h.ExecutionId, owner, policy);
        coordinator.Activate();
        int appliedToolResult = coordinator.ApplyPendingResults();
        AgentCollaborationPolicy.CollaborationState afterTool = ReadCollaborationState(h);
        if (afterTool.Stage != "SequenceAfter") throw new InvalidOperationException($"Tool result did not resume parent Sequence: {afterTool.Stage}.");
        WorkSnapshot afterWork = h.Store.ReadWork(scope, h.ExecutionId).Single(x => x.InvocationId == "sequence-after");
        if (afterWork.Status != "owed") throw new InvalidOperationException("Expected the parent Sequence after-child to be durably owed.");
        ChildResult afterResult = await WaitAsync(StartWorker(h, "sequence-after"));
        if (afterResult.ExitCode != 0) throw new InvalidOperationException($"Sequence after-child failed: {afterResult.Output}");
        int appliedAfter = coordinator.ApplyPendingResults();
        AgentCollaborationPolicy.CollaborationState finalState = ReadCollaborationState(h);
        ExecutionSnapshot execution = h.Store.ReadExecution(scope, h.ExecutionId);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            processId = Environment.ProcessId,
            owner,
            appliedToolResult,
            appliedAfter,
            status = execution.Status,
            stage = finalState.Stage,
            persistedDecisionCount = finalState.Decisions.Length,
            managerDecisionEvaluations = policy.ManagerDecisionEvaluations,
            usageBudget = finalState.UsageBudget,
            usageUsed = finalState.UsageUsed,
            sequenceAfterWorkStatus = h.Store.ReadWork(scope, h.ExecutionId).Single(x => x.InvocationId == "sequence-after").Status
        }));
        return 0;
    }

    private static Harness NewHarness(string runRoot, string name)
    {
        string directory = Path.Combine(runRoot, name);
        Directory.CreateDirectory(directory);
        WorkScope scope = NewScope();
        string workDb = Path.Combine(directory, "work.db");
        string sinkDb = Path.Combine(directory, "sink.db");
        var auth = new HmacWorkScopeAuthorizer(ReadKey());
        return new Harness(workDb, sinkDb, $"agent-{name}-{Guid.NewGuid():N}", scope, new DurableWorkStore(workDb, auth), new IndependentSink(sinkDb, auth));
    }

    private static Harness HarnessFrom(string workDb, string sinkDb, WorkScope scope, string executionId)
    {
        var auth = new HmacWorkScopeAuthorizer(ReadKey());
        return new Harness(workDb, sinkDb, executionId, scope, new DurableWorkStore(workDb, auth), new IndependentSink(sinkDb, auth));
    }

    private static CollaborationStartInput CollaborationInput(WorkScope scope)
        => new("Prepare a scoped deterministic review", "conversation-42", scope.TenantId, scope.PersistenceScope, scope.Authority);

    private static AgentCollaborationPolicy.CollaborationState ReadCollaborationState(Harness h)
        => ProofJson.Deserialize<AgentCollaborationPolicy.CollaborationState>(h.Store.ReadExecution(h.Scope, h.ExecutionId).StateJson);

    private static WorkScope NewScope() => new HmacWorkScopeAuthorizer(ReadKey()).Issue("tenant-red", "shared-persistence", "agent-host");
    private static string EvidencePath() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../evidence.json"));

    private static ResourceReference[] DefaultReferences()
    {
        const string tenant = "tenant-red", persistence = "shared-persistence", authority = "agent-host";
        return
        [
            new MemoryReference(tenant, persistence, authority, "agent-context", "r7"),
            new KnowledgeReference(tenant, persistence, authority, "dataset-runbook", "r3")
        ];
    }

    private static MemoryReference PrivateMemory(WorkScope scope, string owner, string resourceId, string revision)
        => new(scope.TenantId, scope.PersistenceScope, scope.Authority, resourceId, revision,
            MemoryVisibility.Private, owner, null, MemoryPermission.Read, MemoryConsistency.Snapshot);

    private static MemoryReference ConversationMemory(WorkScope scope, string conversationId, string resourceId, string revision)
        => new(scope.TenantId, scope.PersistenceScope, scope.Authority, resourceId, revision,
            MemoryVisibility.ConversationShared, null, conversationId, MemoryPermission.Read, MemoryConsistency.Snapshot);

    private static AgentEnvelope Envelope(string receiver, int round, string invocation, string messageId,
        ResourceReference[] refs, AgentEmission? emission = null)
        => new(receiver, round, invocation, messageId, $"payload:{invocation}", refs, emission);

    private static WorkReceipt Receipt(AgentExecutionResult result)
    {
        string json = ProofJson.Serialize(result);
        return new WorkReceipt(result.InvocationId, "Succeeded", json, ResourceReferenceResolver.Sha256(json));
    }

    private static AgentEnvelope SourceEnvelope(string invocation, string sourceId, string messageId, string targetInvocation,
        ResourceReference[] refs)
    {
        AgentEnvelope message = Envelope("fan-in", 0, targetInvocation, messageId, refs);
        return Envelope("source-agent", 0, invocation, $"source-message:{invocation}", refs, new AgentEmission(sourceId, message));
    }

    private static RunningChild StartWorker(Harness harness, string invocationId, string barrier = "-", int leaseSeconds = 20,
        string failpoint = "-", bool waitForClaim = false)
        => StartChild("--agent-worker", harness.WorkDb, harness.SinkDb, EncodeScope(harness.Scope), harness.ExecutionId,
            invocationId, $"worker-{invocationId}-{Guid.NewGuid():N}", barrier, leaseSeconds.ToString(), failpoint, waitForClaim ? "wait" : "once");

    private static RunningChild StartChild(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (string argument in args) start.ArgumentList.Add(argument);
        start.Environment["ELSA_AGENT_PROOF_KEY"] = ProofKey;
        Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start composition proof child process.");
        return new RunningChild(process, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
    }

    private static async Task<ChildResult> WaitAsync(RunningChild child, TimeSpan? timeout = null)
    {
        using var limit = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        try { await child.Process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            try { child.Process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Composition proof process {child.Process.Id} did not exit within the bounded wait.");
        }
        return new ChildResult(child.Process.Id, child.Process.ExitCode, (await child.Stdout) + (await child.Stderr));
    }

    private static async Task WaitForBarrier(DurableWorkStore store, string barrier, int expected)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (store.BarrierEntryCount(barrier) < expected)
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException($"Workers did not enter gate '{barrier}'.");
            await Task.Delay(10);
        }
    }

    private static string EncodeScope(WorkScope scope) => Convert.ToBase64String(Encoding.UTF8.GetBytes(ProofJson.Serialize(scope)));
    private static WorkScope DecodeScope(string encoded) => ProofJson.Deserialize<WorkScope>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
    private static string ReadKey() => Environment.GetEnvironmentVariable("ELSA_AGENT_PROOF_KEY") ?? ProofKey;
    private static string ReadGitOutput(params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add("-C");
            start.ArgumentList.Add(AppContext.BaseDirectory);
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git for evidence metadata.");
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return process.ExitCode == 0 ? output.Trim() : "unavailable";
        }
        catch { return "unavailable"; }
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException($"Assertion failed: {message}");
    }

    private static void Expect<TException>(Action action, string message) where TException : Exception
    {
        try { action(); }
        catch (TException) { _assertions++; return; }
        throw new InvalidOperationException($"Assertion failed: expected {typeof(TException).Name}: {message}");
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { /* Evidence is already emitted; leave diagnostics if the OS still has an open handle. */ }
    }

    private sealed record Harness(string WorkDb, string SinkDb, string ExecutionId, WorkScope Scope, DurableWorkStore Store, IndependentSink Sink);
    private sealed record RunningChild(Process Process, Task<string> Stdout, Task<string> Stderr);
    private sealed record ChildResult(int ProcessId, int ExitCode, string Output);
}

internal static class AgentWorker
{
    public const string HandlerId = "deterministic-agent";
    public const string HandlerVersion = "1";
    private const int FailAfterEffectExitCode = 74;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 11) throw new ArgumentException("Usage: --agent-worker <workDb> <sinkDb> <scopeBase64> <executionId> <invocationId> <workerId> <barrier|-> <leaseSeconds> <failpoint|-> <wait|once>");
        try
        {
            string workDb = args[1];
            string sinkDb = args[2];
            WorkScope scope = DecodeScope(args[3]);
            string executionId = args[4];
            string invocationId = args[5];
            string workerId = args[6];
            string barrier = args[7];
            int leaseSeconds = int.Parse(args[8]);
            string failpoint = args[9];
            bool waitForClaim = args[10] == "wait";
            var auth = new HmacWorkScopeAuthorizer(ReadKey());
            var store = new DurableWorkStore(workDb, auth);
            var sink = new IndependentSink(sinkDb, auth);

            WorkLease? lease = null;
            DateTime deadline = DateTime.UtcNow.AddSeconds(25);
            do
            {
                lease = store.TryClaimWork(scope, executionId, invocationId, workerId, HandlerId, HandlerVersion, leaseSeconds);
                if (lease is not null || !waitForClaim) break;
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("Work did not become claimable before the bounded retry wait ended.");
                await Task.Delay(50);
            } while (true);

            if (lease is null)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { outcome = "not-claimed", invocationId }));
                return 9;
            }

            if (barrier != "-") await WaitAtBarrier(store, barrier, workerId, invocationId);
            var resolver = new ResourceReferenceResolver();
            AgentEnvelope envelope = ProofJson.Deserialize<AgentEnvelope>(lease.InputJson);
            AgentExecutionResult result = new DeterministicFakeAgentAdapter(resolver).Execute(envelope, scope);
            string resultJson = ProofJson.Serialize(result);
            bool duplicateEffect = sink.Write(scope, executionId, $"agent/{invocationId}", resultJson);
            if (failpoint == "crash-after-sink")
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    outcome = "effectPersistedBeforeResult",
                    invocationId,
                    duplicateEffect,
                    processId = Environment.ProcessId,
                    attempt = lease.Attempt,
                    fence = lease.Fence
                }));
                Environment.Exit(FailAfterEffectExitCode);
            }

            WorkReceipt receipt = store.CompleteWork(lease, resultJson);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                outcome = receipt.Status,
                receipt.Duplicate,
                duplicateEffect,
                invocationId,
                processId = Environment.ProcessId,
                attempt = lease.Attempt,
                fence = lease.Fence,
                result
            }));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"AGENT_WORKER_ERROR: {exception.GetType().Name}: {exception.Message}");
            return 3;
        }
    }

    private static async Task WaitAtBarrier(DurableWorkStore store, string barrier, string workerId, string invocationId)
    {
        store.EnterBarrier(barrier, workerId, Environment.ProcessId, invocationId);
        DateTime deadline = DateTime.UtcNow.AddSeconds(25);
        while (!store.IsBarrierReleased(barrier))
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException($"Test gate '{barrier}' was not released.");
            await Task.Delay(10);
        }
    }

    private static WorkScope DecodeScope(string encoded) => ProofJson.Deserialize<WorkScope>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
    private static string ReadKey() => Environment.GetEnvironmentVariable("ELSA_AGENT_PROOF_KEY") ?? throw new InvalidOperationException("Parent did not provide the local proof key.");
}
