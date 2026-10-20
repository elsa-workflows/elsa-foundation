import { describe, expect, it } from "vitest";
import { compensationScopeStructureKind } from "../compensationScope";
import {
  deriveCompensationRuntime,
  interventionDecisionLabel,
  offeredDecisions,
  operatorHistoryLabel,
  readCompensationJournal,
  recoveryStateLabel,
  scopePhaseLabel,
  type CompensationJournal,
  type CompensationJournalEntry
} from "../compensationRuntime";
import type { ActivityExecutionStateSummary, ActivityNode, ActivityStateView, IncidentStateSummary } from "../workflowTypes";

// Spec 260 T420: a live scope's rows come from its journal (compensation.journal/1); the M1–M3 derivation stays the
// fallback, marked derived; operator history comes from incidents.

const decisions = ["compensation.resolve.effect-absent", "compensation.resolve.effect-accepted", "compensation.abandon"];

describe("compensation run view from the journal", () => {
  it("shows a waiting handler from the journal, with its attempts, operation key, offered decisions and what is left to undo", () => {
    const runtime = deriveCompensationRuntime(journeyScope(), waitingRun(), waitingIncidents(), {
      journal: view(journal("AwaitingIntervention", [
        entry("e1", "Reserve inventory", 1, "Registered", "ae-1", { handler: "release" }),
        entry("e2", "Reserve delivery", 2, "Failed", "ae-2", { handler: "cancel", attempts: ["ae-4", "ae-5", "ae-6"], waiting: "inc-6" })
      ], { remaining: ["e2", "e1"], cause: { kind: "fault", step: "Confirm order", activityExecutionId: "ae-3", incidentId: "inc-3", failureType: "order.rejected", message: null, messageWithheld: true } }))
    });

    expect(runtime.source).toBe("journal");
    expect(runtime.phase).toBe("AwaitingIntervention");
    expect(runtime.rows.map(row => row.recovery.state)).toEqual(["Pending", "NeedsAction", "NotApplicable"]);
    expect(runtime.rows[1]).toMatchObject({
      entryId: "e2",
      operationKey: "ae-scope/step/1",
      recovery: { state: "NeedsAction", attempts: { count: 3, max: 3 }, incidentId: "inc-6", activityExecutionId: "ae-6", order: 1 },
      actions: { incidentId: "inc-6", sourceActivityExecutionId: "ae-6", decisions, target: "handler" }
    });
    expect(runtime.cause).toMatchObject({ kind: "fault", step: "Confirm order", failureType: "order.rejected", messageWithheld: true });
    expect(runtime.cause?.message).toBeUndefined();
    expect(runtime.remaining).toEqual([{ step: "Reserve delivery", handlerNodeId: "cancel" }, { step: "Reserve inventory", handlerNodeId: "release" }]);
  });

  it.each([
    ["Registered", "Running", {}, "Registered"],
    ["Registered", "Compensating", {}, "Pending"],
    ["Registered", "Cancelled", {}, "Kept"],
    ["Compensating", "Compensating", { attempts: ["ae-4"] }, "Running"],
    ["Compensating", "Compensating", { attempts: ["ae-4", "ae-5"] }, "Retrying"],
    ["Compensated", "Compensating", { attempts: ["ae-4"] }, "Compensated"],
    ["Failed", "Cancelled", { attempts: ["ae-4"], waiting: "inc-6" }, "Failed"],
    ["Confirmed", "Running", {}, "Confirmed"],
    ["ResolvedManually", "Compensating", { resolution: "EffectAbsent" }, "ResolvedAbsent"],
    ["ResolvedManually", "Compensating", { resolution: "EffectAccepted" }, "ResolvedKept"],
    ["Failed", "Abandoned", { attempts: ["ae-4"] }, "NotUndone"],
    ["Registered", "Abandoned", {}, "NotUndone"],
    ["NoHandler", "Running", { handler: null }, "NotNeeded"]
  ] as const)("maps a %s entry while the scope is %s to %s", (status, phase, options, expected) => {
    const activityStatus = phase === "Cancelled" ? "Cancelled" : phase === "Abandoned" ? "Faulted" : "Running";
    const runtime = deriveCompensationRuntime(journeyScope(), waitingRun(activityStatus), [], {
      journal: view(journal(phase === "Cancelled" ? "Compensating" : phase, [
        entry("e2", "Reserve delivery", 2, status, "ae-2", { handler: "cancel", ...options } as EntryOptions)
      ]), activityStatus)
    });

    expect(runtime.rows[1].recovery.state).toBe(expected);
    expect(recoveryStateLabel(runtime.rows[1].recovery.state)).not.toBe("");
    if (phase === "Abandoned") expect(runtime.phase).toBe("Abandoned");
    if (phase === "Cancelled") expect(runtime.rows[1].actions).toBeUndefined();
  });

  it("labels every new state and phase as text", () => {
    expect(["Retrying", "ResolvedAbsent", "ResolvedKept", "NotUndone"].map(state => recoveryStateLabel(state as never)))
      .toEqual(["Retrying", "Resolved: effect absent", "Resolved: effect kept", "Not undone (recovery abandoned)"]);
    expect(scopePhaseLabel("Abandoned")).toBe("Abandoned");
    expect(decisions.map(interventionDecisionLabel)).toEqual(["Resolve: effect absent or undone", "Resolve: effect present, keep it", "Abandon recovery"]);
    expect(interventionDecisionLabel("payments.refund.manual")).toBe("payments.refund.manual");
  });

  it("shows an uncertain step that waits with no handler, and offers its decisions on the step", () => {
    const runtime = deriveCompensationRuntime(uncertainScope(), uncertainRun(), [waiting(incident("inc-2", "ae-2"), { "compensation.forwardOutcome": "unknown" })], {
      journal: view(journal("AwaitingIntervention", [
        entry("e1", "Reserve inventory", 1, "Registered", "ae-1", { handler: "release" }),
        entry("e2", "Notify carrier", 2, "Uncertain", "ae-2", { handler: null, forwardOutcome: "Uncertain", waiting: "inc-2" })
      ], { remaining: [] }))
    });

    expect(runtime.rows[1].forward.state).toBe("Uncertain");
    expect(runtime.rows[1].recovery.state).toBe("NeedsAction");
    expect(runtime.rows[1].actions).toEqual({ incidentId: "inc-2", sourceActivityExecutionId: "ae-2", decisions, target: "step" });
  });

  it("agrees with the derivation for the same waiting run", () => {
    const derived = deriveCompensationRuntime(journeyScope(), waitingRun(), waitingIncidents());
    const fromJournal = deriveCompensationRuntime(journeyScope(), waitingRun(), waitingIncidents(), {
      journal: view(journal("AwaitingIntervention", [
        entry("e1", "Reserve inventory", 1, "Registered", "ae-1", { handler: "release" }),
        entry("e2", "Reserve delivery", 2, "Failed", "ae-2", { handler: "cancel", attempts: ["ae-4", "ae-5", "ae-6"], waiting: "inc-6" })
      ], { remaining: ["e2", "e1"] }))
    });

    expect(derived.source).toBe("derived");
    const shape = (runtime: typeof derived) => ({
      phase: runtime.phase,
      rows: runtime.rows.map(row => ({ forward: row.forward.state, recovery: row.recovery.state, attempts: row.recovery.attempts, incident: row.recovery.incidentId, actions: row.actions }))
    });
    expect(shape(fromJournal)).toEqual(shape(derived));
  });

  it("shows what run data says while an operator's retry runs, since the journal clears the wait only afterwards", () => {
    const run = [...waitingRun(), execution("ae-7", "cancel", "Running", "17", { parent: "ae-scope", handler: true, scheduledBy: "ae-2" })];
    const incidents = [...waitingIncidents().slice(0, 3), retried(incident("inc-6", "ae-6"), "alice", "Service back")];
    const runtime = deriveCompensationRuntime(journeyScope(), run, incidents, {
      journal: view(journal("AwaitingIntervention", [
        entry("e1", "Reserve inventory", 1, "Registered", "ae-1", { handler: "release" }),
        entry("e2", "Reserve delivery", 2, "Failed", "ae-2", { handler: "cancel", attempts: ["ae-4", "ae-5", "ae-6"], waiting: "inc-6" })
      ]))
    });

    expect(runtime.rows[1].recovery).toMatchObject({ state: "Retrying", attempts: { count: 4, max: 3 } });
    expect(runtime.rows[1].actions).toBeUndefined();
    expect(runtime.phase).toBe("Compensating");
  });

  it("builds handed-over rows from the journal, keeping where they came from", () => {
    const runtime = deriveCompensationRuntime(journeyScope(), [...waitingRun(), execution("ae-inner", "inner", "Completed", "09", { parent: "ae-scope" })], [], {
      journal: view(journal("Compensating", [
        { ...entry("e9", "Reserve hotel", 3, "Compensating", "ae-hotel", { handler: "cancel-hotel", attempts: ["ae-cancel-hotel"] }), origin: { scopeActivityExecutionId: "ae-inner", entryId: "e1" } }
      ]))
    });

    expect(runtime.received).toEqual([expect.objectContaining({
      step: "Reserve hotel",
      entryId: "e9",
      handlerNodeId: "cancel-hotel",
      origin: expect.objectContaining({ activityExecutionId: "ae-inner" }),
      recovery: expect.objectContaining({ state: "Running", activityExecutionId: "ae-cancel-hotel" })
    })]);
  });
});

describe("compensation journal fallback", () => {
  it("derives a completed scope from run data and says its journal is no longer kept", () => {
    const run = [
      execution("ae-scope", "scope", "Completed", "10", { outcomeNames: ["Compensated"] }),
      execution("ae-1", "reserve", "Completed", "11", { parent: "ae-scope" })
    ];

    const runtime = deriveCompensationRuntime(journeyScope(), run, []);
    expect(runtime).toMatchObject({ source: "derived", journalUnavailable: "StateCleared" });

    const cleared = deriveCompensationRuntime(journeyScope(), waitingRun(), [], { journal: { ...view(journal("Running", [])), available: false, unavailableReason: "StateCleared", view: null } });
    expect(cleared).toMatchObject({ source: "derived", journalUnavailable: "StateCleared" });
  });

  it("derives a live scope when the host has no journal, and ignores a journal for another occurrence", () => {
    expect(deriveCompensationRuntime(journeyScope(), waitingRun(), [], { journal: "unavailable" })).toMatchObject({ source: "derived", journalUnavailable: "Unavailable" });
    expect(deriveCompensationRuntime(journeyScope(), waitingRun(), [], { journal: "loading" }).journalUnavailable).toBeUndefined();
    const other = { ...view(journal("Compensating", [])), activityExecutionId: "ae-other" };
    expect(deriveCompensationRuntime(journeyScope(), waitingRun(), [], { journal: other }).source).toBe("derived");
  });

  it("reads only the journal kind and version it knows", () => {
    expect(readCompensationJournal({ ...view(journal("Running", [])), viewVersion: 2 })).toBeNull();
    expect(readCompensationJournal({ ...view(journal("Running", [])), viewKind: "other" })).toBeNull();
    expect(readCompensationJournal(view(journal("Running", [])))).toMatchObject({ phase: "Running", entries: [], remaining: [] });
  });

  it("derives operator results from incidents when there is no journal", () => {
    const resolvedRun = [
      ...waitingRun("Running").map(activity => activity.activityExecutionId === "ae-scope" ? { ...activity, status: "Completed", outcomeNames: ["Compensated"] } : activity),
      execution("ae-8", "release", "Completed", "18", { parent: "ae-scope", handler: true, scheduledBy: "ae-1" })
    ];
    const resolved = deriveCompensationRuntime(journeyScope(), resolvedRun, [...waitingIncidents().slice(0, 3), decided(incident("inc-6", "ae-6"), "compensation.resolve.effect-absent", "bob", "Refunded")]);
    expect(resolved.rows[1].recovery.state).toBe("ResolvedAbsent");
    expect(resolved.rows[0].recovery.state).toBe("Compensated");

    const abandonedRun = waitingRun("Faulted");
    const abandoned = deriveCompensationRuntime(journeyScope(), abandonedRun, [...waitingIncidents().slice(0, 3), decided(incident("inc-6", "ae-6"), "compensation.abandon", "bob", "Written off")]);
    expect(abandoned.phase).toBe("Abandoned");
    expect(abandoned.rows.map(row => row.recovery.state)).toEqual(["NotUndone", "NotUndone", "NotApplicable"]);
  });

  it("offers the derived wait's decisions, and none for a wait from before spec 260", () => {
    const runtime = deriveCompensationRuntime(journeyScope(), waitingRun(), waitingIncidents());
    expect(runtime.rows[1].actions).toEqual({ incidentId: "inc-6", sourceActivityExecutionId: "ae-6", decisions, target: "handler" });
    expect(offeredDecisions({ resolutionOutcome: { actionKind: "WaitForIntervention", metadata: {} } })).toEqual([]);
    expect(offeredDecisions({ resolutionOutcome: { actionKind: "WaitForIntervention", metadata: { "runtime.interventionDecisions": "not json" } } })).toEqual([]);
  });
});

describe("compensation operator history", () => {
  it("lists retries and decisions newest first, with actor, reason and time, and leaves out other scopes' actions", () => {
    const run = [
      ...waitingRun(),
      // A handler that is a Sequence: the fault, and so the retry, is on an activity inside it.
      execution("ae-seq", "cancel", "Superseded", "15", { parent: "ae-scope", handler: true, scheduledBy: "ae-2" }),
      execution("ae-deep", "call", "Faulted", "16", { parent: "ae-seq" }),
      execution("ae-other", "other-scope", "Running", "09"),
      execution("ae-other-step", "x", "Faulted", "09", { parent: "ae-other" })
    ];
    const incidents = [
      ...waitingIncidents().slice(0, 3),
      retried(incident("inc-5", "ae-5"), "alice", "Delivery service restored", "2026-10-20T10:42:00Z"),
      retried(incident("inc-deep", "ae-deep"), "carol", undefined, "2026-10-20T10:50:00Z"),
      decided(incident("inc-6", "ae-6"), "compensation.resolve.effect-absent", "bob", "Refunded manually, ticket FIN-221", "2026-10-20T11:05:00Z"),
      retried(incident("inc-other", "ae-other-step"), "dave", "Not ours", "2026-10-20T12:00:00Z"),
      { ...decided(incident("inc-other-2", "ae-other-step"), "compensation.abandon", "erin", "Not ours"), resolutionOutcome: { actionKind: "OperatorDecision", appliedAt: "2026-10-20T12:01:00Z", metadata: { "runtime.interventionDecision": "compensation.abandon", "runtime.interventionRequestedBy": "ae-other" } } }
    ];

    const history = deriveCompensationRuntime(journeyScope(), run, incidents).history!;

    expect(history.map(item => [item.incidentId, operatorHistoryLabel(item), item.actor, item.reason ?? null, item.step ?? null])).toEqual([
      ["inc-6", "Resolved — effect absent", "bob", "Refunded manually, ticket FIN-221", "Reserve delivery"],
      ["inc-deep", "Retry", "carol", null, "Reserve delivery"],
      ["inc-5", "Retry", "alice", "Delivery service restored", "Reserve delivery"]
    ]);
    expect(history[0].appliedAt).toBe("2026-10-20T11:05:00Z");
  });

  it("says a decision was not applied when the scope was cancelled before it arrived", () => {
    const run = waitingRun("Cancelled");
    const incidents = [...waitingIncidents().slice(0, 3), decided(incident("inc-6", "ae-6"), "compensation.resolve.effect-accepted", "bob", "Keep it")];
    const runtime = deriveCompensationRuntime(journeyScope(), run, incidents, {
      journal: view(journal("AwaitingIntervention", [
        entry("e2", "Reserve delivery", 2, "Failed", "ae-2", { handler: "cancel", attempts: ["ae-4", "ae-5", "ae-6"], waiting: "inc-6" })
      ]), "Cancelled")
    });

    expect(runtime.phase).toBe("Cancelled");
    expect(runtime.history).toEqual([expect.objectContaining({ incidentId: "inc-6", kind: "ResolvedKept", notApplied: true })]);
  });
});

type EntryOptions = {
  handler?: string | null;
  attempts?: string[];
  waiting?: string;
  resolution?: "EffectAbsent" | "EffectAccepted";
  forwardOutcome?: "Completed" | "Uncertain";
};

function entry(id: string, step: string, ordinal: number, status: string, forward: string, options: EntryOptions = {}): CompensationJournalEntry {
  return {
    id,
    step,
    ordinal,
    status,
    forwardOutcome: options.forwardOutcome ?? "Completed",
    operationKey: `ae-scope/step/${ordinal - 1}`,
    forward: { activityExecutionId: forward, iterationId: null },
    handler: options.handler === null ? null : { executableNodeId: options.handler ?? "release", remaining: true },
    attempts: (options.attempts ?? []).map((attempt, index) => ({ number: index + 1, activityExecutionId: attempt, incidentId: `inc-${attempt.slice(3)}`, result: "Faulted" })),
    waiting: options.waiting ? { incidentId: options.waiting, decisions } : null,
    resolution: options.resolution ? { outcome: options.resolution, incidentId: "inc-6" } : null,
    origin: null,
    confirmedBy: status === "Confirmed" ? "ae-confirm" : null
  };
}

function journal(phase: string, entries: CompensationJournalEntry[], extra: Partial<CompensationJournal> = {}): CompensationJournal {
  return { phase, stateVersion: 4, cause: null, entries, remaining: [], confirmations: [], handOvers: { received: [], inProgress: [] }, abandonment: null, ...extra };
}

function view(value: CompensationJournal, activityStatus = "Running"): ActivityStateView {
  return {
    workflowExecutionId: "wf-1",
    activityExecutionId: "ae-scope",
    executableNodeId: "scope",
    activityStatus,
    available: true,
    unavailableReason: null,
    viewKind: "compensation.journal",
    viewVersion: 1,
    canInspectValues: false,
    view: value
  };
}

function leaf(nodeId: string): ActivityNode {
  return { nodeId, activityVersionId: `${nodeId}-v1`, inputs: [], outputs: [] };
}

function scopeNode(steps: { name: string; activity: ActivityNode | null; compensation: ActivityNode | null }[]): ActivityNode {
  return {
    nodeId: "scope",
    activityVersionId: "scope-v1",
    displayName: "Order",
    inputs: [],
    outputs: [],
    structure: { kind: compensationScopeStructureKind, schemaVersion: "1.0.0", payload: { steps } }
  } as ActivityNode;
}

function journeyScope(): ActivityNode {
  return scopeNode([
    { name: "Reserve inventory", activity: leaf("reserve"), compensation: leaf("release") },
    { name: "Reserve delivery", activity: leaf("deliver"), compensation: leaf("cancel") },
    { name: "Confirm order", activity: leaf("confirm"), compensation: null }
  ]);
}

function uncertainScope(): ActivityNode {
  return scopeNode([
    { name: "Reserve inventory", activity: leaf("reserve"), compensation: leaf("release") },
    { name: "Notify carrier", activity: leaf("notify"), compensation: null }
  ]);
}

function execution(
  id: string,
  nodeId: string,
  status: string,
  at: string,
  options: { parent?: string; handler?: boolean; scheduledBy?: string; outcomeNames?: string[] } = {}
): ActivityExecutionStateSummary {
  return {
    activityExecutionId: id,
    workflowExecutionId: "wf-1",
    executableNodeId: nodeId,
    authoredActivityId: nodeId,
    activityType: nodeId === "scope" ? "Elsa.Activities.Compensation.Activities.CompensationScope" : "Elsa.Test",
    activityTypeVersion: "1.0.0",
    status,
    scheduledAt: `2026-10-19T10:00:${at}Z`,
    parentActivityExecutionId: null,
    schedulingActivityExecutionId: options.scheduledBy ?? null,
    outcomeNames: options.outcomeNames ?? null,
    provenance: {
      parentActivityExecutionId: options.parent ?? null,
      schedulingActivityExecutionId: options.scheduledBy ?? null,
      schedulingCause: options.handler ? "compensation" : "ActivityCompletion"
    },
    metadata: {}
  };
}

// The spec 251 W state: Confirm order failed, Cancel delivery failed three times and waits for an operator.
function waitingRun(scopeStatus = "Running"): ActivityExecutionStateSummary[] {
  return [
    execution("ae-scope", "scope", scopeStatus, "10"),
    execution("ae-1", "reserve", "Completed", "11", { parent: "ae-scope" }),
    execution("ae-2", "deliver", "Completed", "12", { parent: "ae-scope" }),
    execution("ae-3", "confirm", "Faulted", "13", { parent: "ae-scope" }),
    execution("ae-4", "cancel", "Faulted", "14", { parent: "ae-scope", handler: true, scheduledBy: "ae-2" }),
    execution("ae-5", "cancel", "Faulted", "15", { parent: "ae-scope", handler: true, scheduledBy: "ae-2" }),
    execution("ae-6", "cancel", "Faulted", "16", { parent: "ae-scope", handler: true, scheduledBy: "ae-2" })
  ];
}

function uncertainRun(): ActivityExecutionStateSummary[] {
  return [
    execution("ae-scope", "scope", "Running", "10"),
    execution("ae-1", "reserve", "Completed", "11", { parent: "ae-scope" }),
    execution("ae-2", "notify", "Faulted", "12", { parent: "ae-scope" })
  ];
}

function waitingIncidents(): IncidentStateSummary[] {
  return [
    incident("inc-3", "ae-3"),
    absorbed(incident("inc-4", "ae-4"), { "compensation.maxAttempts": "3" }),
    absorbed(incident("inc-5", "ae-5"), { "compensation.maxAttempts": "3" }),
    waiting(incident("inc-6", "ae-6"), { "compensation.maxAttempts": "3" })
  ];
}

function incident(incidentId: string, activityExecutionId: string): IncidentStateSummary {
  return {
    incidentId,
    workflowExecutionId: "wf-1",
    activityExecutionId,
    severity: "Error",
    status: "Resolved",
    resolutionOutcome: { actionKind: "AbsorbFault", metadata: {} },
    failureType: "ActivityReturnedFault",
    message: "Failed",
    createdAt: `2026-10-19T10:00:${activityExecutionId.slice(3).padStart(2, "0")}Z`,
    isBlocking: false,
    metadata: {}
  };
}

function absorbed(source: IncidentStateSummary, metadata: Record<string, string>): IncidentStateSummary {
  return { ...source, resolutionOutcome: { actionKind: "AbsorbFault", systemSource: "StructuralFaultAbsorption", metadata } };
}

function waiting(source: IncidentStateSummary, metadata: Record<string, string>): IncidentStateSummary {
  return {
    ...source,
    status: "Blocking",
    isBlocking: true,
    resolvedAt: null,
    resolutionOutcome: {
      actionKind: "WaitForIntervention",
      systemSource: "StructuralIntervention",
      metadata: { "runtime.interventionDecisions": JSON.stringify(decisions), "runtime.interventionRequestedBy": "ae-scope", ...metadata }
    }
  };
}

function retried(source: IncidentStateSummary, actor: string, reason?: string, at = "2026-10-20T10:42:00Z"): IncidentStateSummary {
  return {
    ...source,
    resolutionOutcome: {
      actionKind: "OperatorRetry",
      systemSource: "OperatorRetryAlteration",
      appliedAt: at,
      metadata: { "runtime.alteration.submittedBy": actor, ...(reason ? { "runtime.alteration.reason": reason } : {}) }
    }
  };
}

function decided(source: IncidentStateSummary, decision: string, actor: string, reason: string, at = "2026-10-20T11:05:00Z"): IncidentStateSummary {
  return {
    ...source,
    resolutionOutcome: {
      actionKind: "OperatorDecision",
      systemSource: "OperatorDecisionAlteration",
      appliedAt: at,
      metadata: {
        "runtime.alteration.submittedBy": actor,
        "runtime.alteration.reason": reason,
        "runtime.interventionDecision": decision,
        "runtime.interventionRequestedBy": "ae-scope"
      }
    }
  };
}
