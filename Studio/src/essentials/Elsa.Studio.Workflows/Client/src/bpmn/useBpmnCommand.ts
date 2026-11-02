import { useEffect, useRef, useState } from "react";
import type { BpmnCommandOutcome, BpmnOperation, BpmnOperationInput, createBpmnRuntimeClient } from "../api/bpmnRuntime";

type Client = ReturnType<typeof createBpmnRuntimeClient>;
type ExecutionKind = "complete-human" | "claim-human" | "complete-claimed-human" | "retry-service";
type Input = Extract<BpmnOperationInput, { kind: "start" | ExecutionKind }>;
type Target = { kind: "start"; sourceReferenceId: string } | { kind: ExecutionKind; executionId: string };
export interface BpmnCommandReceipt { formatVersion: 1; operation: BpmnOperation; outcome?: BpmnCommandOutcome }

const executionKinds: readonly string[] = ["complete-human", "claim-human", "complete-claimed-human", "retry-service"];
function forExecution(operation: BpmnOperation): operation is Extract<BpmnOperation, { kind: ExecutionKind }> {
  return executionKinds.includes(operation.kind);
}
function belongs(operation: BpmnOperation, target: Target) {
  return operation.kind === target.kind && (operation.kind === "start" && target.kind === "start"
    ? operation.sourceReferenceId === target.sourceReferenceId
    : forExecution(operation) && target.kind !== "start" && operation.executionId === target.executionId);
}
const sameWork = (left: { executionId: string; invocationId: string; attemptId: string }, right: { executionId: string; invocationId: string; attemptId: string }) =>
  left.executionId === right.executionId && left.invocationId === right.invocationId && left.attemptId === right.attemptId;
function sameInput(operation: BpmnOperation, input: Input) {
  return operation.kind === input.kind && (operation.kind === "start" && input.kind === "start"
    ? operation.sourceReferenceId === input.sourceReferenceId
    : operation.kind === "complete-human" && input.kind === "complete-human" && operation.executionId === input.executionId
      && operation.invocationId === input.invocationId && operation.attemptId === input.attemptId
      && JSON.stringify(operation.outcomes ?? []) === JSON.stringify(input.outcomes ?? [])
      || operation.kind === "retry-service" && input.kind === "retry-service" && operation.executionId === input.executionId
        && operation.incidentId === input.incidentId && operation.invocationId === input.invocationId && operation.failedAttemptId === input.failedAttemptId
      || operation.kind === "claim-human" && input.kind === "claim-human" && sameWork(operation, input)
      || operation.kind === "complete-claimed-human" && input.kind === "complete-claimed-human" && sameWork(operation, input)
        && operation.claimId === input.claimId && JSON.stringify(operation.outcomes ?? []) === JSON.stringify(input.outcomes ?? []));
}
export function confirmedBpmnCommand(receipt: BpmnCommandReceipt | null) {
  return receipt?.outcome?.pending === false && ["Accepted", "Rejected"].includes(String(receipt.outcome.result?.status));
}
function validateOutcome(operation: BpmnOperation, outcome: BpmnCommandOutcome) {
  if (!outcome || outcome.inputId !== operation.requestId || typeof outcome.executionId !== "string" || !outcome.executionId
    || typeof outcome.pending !== "boolean" || forExecution(operation) && outcome.executionId !== operation.executionId)
    throw new Error("The command response does not match the retained BPMN request.");
}

/** Scoped to a mounted process/run. A reload reads the receipt; only an explicit action sends it. */
export function useBpmnCommand(client: Client, target: Target, onOutcome: () => void) {
  const targetId = target.kind === "start" ? target.sourceReferenceId : target.executionId;
  const storageKey = `elsa:bpmn-command:1:${JSON.stringify([client.scope, target.kind, targetId])}`;
  const [initial] = useState(() => {
    try {
      const raw = sessionStorage.getItem(storageKey);
      if (!raw) return { record: null, error: "" };
      const record = JSON.parse(raw) as BpmnCommandReceipt;
      const operation = record?.operation;
      if (record?.formatVersion !== 1 || !operation || operation.scope !== client.scope || !belongs(operation, target)
        || typeof operation.requestId !== "string" || !operation.requestId
        || operation.kind === "complete-human" && (![operation.invocationId, operation.attemptId].every(value => typeof value === "string" && value.length > 0)
          || operation.outcomes !== undefined && (!Array.isArray(operation.outcomes) || operation.outcomes.some(value => typeof value !== "string")))
        || operation.kind === "retry-service" && ![operation.incidentId, operation.invocationId, operation.failedAttemptId]
          .every(value => typeof value === "string" && value.trim().length > 0)
        || operation.kind === "claim-human" && ![operation.invocationId, operation.attemptId].every(value => typeof value === "string" && value.length > 0)
        || operation.kind === "complete-claimed-human" && (![operation.invocationId, operation.attemptId, operation.claimId].every(value => typeof value === "string" && value.length > 0)
          || operation.outcomes !== undefined && (!Array.isArray(operation.outcomes) || operation.outcomes.some(value => typeof value !== "string"))))
        throw new Error("The previous BPMN command cannot be restored safely. Its receipt is kept unchanged.");
      if (record.outcome) validateOutcome(operation, record.outcome);
      return { record, error: "" };
    } catch (error) { return { record: null, error: error instanceof Error ? error.message : String(error) }; }
  });
  const [record, setRecord] = useState<BpmnCommandReceipt | null>(initial.record);
  const [error, setError] = useState(initial.error);
  const [busy, setBusy] = useState(false);
  const inFlight = useRef(false);
  const lifetime = useRef<AbortController | null>(null);
  useEffect(() => {
    const abort = new AbortController(); lifetime.current = abort;
    return () => { abort.abort(); lifetime.current = null; };
  }, []);
  const store = (next: BpmnCommandReceipt) => {
    sessionStorage.setItem(storageKey, JSON.stringify(next)); setRecord(next);
  };
  const run = async (operation: BpmnOperation, readOnly = false) => {
    const signal = lifetime.current?.signal;
    if (inFlight.current || !signal || signal.aborted || initial.error) return;
    inFlight.current = true; setBusy(true); setError("");
    let retained = record?.operation.requestId === operation.requestId;
    try {
      if (!belongs(operation, target)) throw new Error("This command belongs to another BPMN process or run.");
      if (!readOnly) { store({ formatVersion: 1, operation, ...(record?.operation.requestId === operation.requestId && record.outcome ? { outcome: record.outcome } : {}) }); retained = true; }
      const executionId = forExecution(operation) ? operation.executionId : record?.outcome?.executionId;
      const outcome = readOnly
        ? await client.command(executionId ?? "", operation.requestId, signal)
        : await client.execute(operation, signal) as BpmnCommandOutcome;
      if (signal.aborted) return;
      validateOutcome(operation, outcome); store({ formatVersion: 1, operation, outcome }); onOutcome();
    } catch (error) {
      if (!signal.aborted) setError(`${error instanceof Error ? error.message : String(error)}${retained ? " The request identity is retained. Use Resume to recover the same outcome." : ""}`);
    } finally { inFlight.current = false; if (!signal.aborted) setBusy(false); }
  };
  const begin = async (input: Input, explicitlyNew = false) => {
    if (inFlight.current || initial.error) return;
    try {
      if (record && (!confirmedBpmnCommand(record) || !explicitlyNew && sameInput(record.operation, input))) {
        if (!sameInput(record.operation, input)) throw new Error("Resolve the previous command before starting another one.");
        await run(record.operation); return;
      }
      await run(client.prepare(input));
    } catch (error) { setError(error instanceof Error ? error.message : String(error)); }
  };
  return { record, error, busy, blocked: !!initial.error, confirmed: confirmedBpmnCommand(record), begin,
    replay: () => record ? run(record.operation) : Promise.resolve(),
    check: () => record ? run(record.operation, true) : Promise.resolve() };
}
