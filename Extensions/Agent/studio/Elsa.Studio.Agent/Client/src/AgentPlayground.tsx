import React, { useCallback, useEffect, useId, useRef, useState } from "react";
import { FlaskConical, Play, RefreshCw, Square } from "lucide-react";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StatusChip, StudioAlert, StudioButton, StudioPage, StudioPageHeader, StudioResultState } from "@elsa-workflows/studio-ui";
import { activityDefinitionPath } from "./AgentsPage";
import { InvocationEvidenceDetails } from "./InvocationEvidence";
import { invocationPath } from "./AgentInvocationPage";
import {
  acceptedConversation, allowsContinuity, businessInputs, cancelRun, getDraft, getEvidence, isTerminal, newIdempotencyKey, startPlaygroundRun,
  type ConversationReference, type PlaygroundDraft, type PlaygroundEvidence, type PlaygroundInput
} from "./playgroundApi";

type Load = { kind: "loading" } | { kind: "missing" } | { kind: "denied" } | { kind: "error"; message: string } | { kind: "ready"; draft: PlaygroundDraft };
type Refusal = { code?: string | null; message: string; diagnostics: Array<{ code: string; message: string }> };
type Run = { testRunId: string; evidence: PlaygroundEvidence | null; refusal: Refusal | null; stopping: boolean; stopNote: string | null; failures: number };
type Attempt = { key: string; signature: string };

const pollIntervalMs = 1000;
// Consecutive failed status reads back off and then stop; the author retries explicitly.
const maximumPollFailures = 5;

/** The first read is immediate; later reads wait one interval, and each consecutive failure doubles the wait. */
export function pollDelay(interval: number, failures: number, hasEvidence: boolean) {
  if (failures > 0) return interval * 2 ** failures;
  return hasEvidence ? interval : 0;
}

/** The draft ID a playground URL names: /agents/drafts/{draftId}/playground. */
export function readPlaygroundDraftId(pathname: string) {
  const match = /^\/agents\/drafts\/([^/]+)\/playground\/?$/.exec(pathname);
  return match ? decodeURIComponent(match[1]) : null;
}

export function playgroundPath(draftId: string) {
  return `/agents/drafts/${encodeURIComponent(draftId)}/playground`;
}

/**
 * Runs the exact saved revision of one agent draft through the production agent runtime. Each run is a generic
 * draft Test Run; this page adds the agent's own evidence: model, turn state, usage, conversation and accepted output.
 */
export function AgentPlayground({ context, draftId: given, navigate, pollInterval = pollIntervalMs }:
  { context: StudioEndpointContext; draftId?: string | null; navigate?(path: string): void; pollInterval?: number }) {
  const draftId = given ?? readPlaygroundDraftId(window.location.pathname);
  return <Playground key={draftId ?? ""} context={context} draftId={draftId} navigate={navigate} pollInterval={pollInterval} />;
}

function Playground({ context, draftId, navigate, pollInterval }: { context: StudioEndpointContext; draftId: string | null; navigate?(path: string): void; pollInterval: number }) {
  const [load, setLoad] = useState<Load>({ kind: "loading" });
  const [values, setValues] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  // The selection names a conversation; its expected revision is always the latest one accepted on this page.
  const [selected, setSelected] = useState<string | null>(null);
  // Conversations accepted on this page belong to the saved revision they ran against; another revision cannot continue them.
  const [conversations, setConversations] = useState<{ revision: number; items: ConversationReference[] }>({ revision: 0, items: [] });
  const [run, setRun] = useState<Run | null>(null);
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);
  const attempt = useRef<Attempt | null>(null);
  const alive = useRef(true);
  useEffect(() => () => { alive.current = false; }, []);

  const loadDraft = useCallback(async () => {
    if (!draftId) { setLoad({ kind: "missing" }); return; }
    try {
      const draft = await getDraft(context, draftId);
      if (!alive.current) return;
      setLoad({ kind: "ready", draft });
    } catch (error) {
      if (!alive.current) return;
      const status = error instanceof StudioHttpError ? error.status : (error as { status?: number }).status;
      setLoad(status === 404 ? { kind: "missing" } : status === 401 || status === 403 ? { kind: "denied" }
        : { kind: "error", message: error instanceof Error ? error.message : "The agent draft could not be loaded." });
    }
  }, [context, draftId]);

  useEffect(() => { void loadDraft(); }, [loadDraft]);

  // A reloaded draft at another revision is another candidate: its conversations and selection start over.
  const loadedRevision = load.kind === "ready" ? load.draft.revision : null;
  useEffect(() => {
    if (loadedRevision === null || loadedRevision === conversations.revision) return;
    setConversations({ revision: loadedRevision, items: [] });
    setSelected(null);
  }, [loadedRevision, conversations.revision]);

  // Polls the run's evidence until the generic Test Run settles; a failed read keeps the last evidence on screen.
  useEffect(() => {
    if (!run || run.refusal || (run.evidence && isTerminal(run.evidence.status)) || run.failures >= maximumPollFailures) return;
    const abort = new AbortController();
    const timer = setTimeout(async () => {
      try {
        const evidence = await getEvidence(context, run.testRunId, abort.signal);
        if (abort.signal.aborted) return;
        setRun(current => current && current.testRunId === run.testRunId ? { ...current, evidence, failures: 0 } : current);
        const accepted = acceptedConversation(evidence);
        if (accepted && isTerminal(evidence.status))
          setConversations(current => current.revision !== evidence.draftRevision ? current
            : { revision: current.revision, items: [accepted, ...current.items.filter(item => item.conversationId !== accepted.conversationId)] });
      } catch {
        if (!abort.signal.aborted) setRun(current => current && current.testRunId === run.testRunId ? { ...current, failures: current.failures + 1 } : current);
      }
    }, pollDelay(pollInterval, run.failures, run.evidence !== null));
    return () => { abort.abort(); clearTimeout(timer); };
  }, [context, run, pollInterval]);

  if (load.kind === "loading") return <StudioPage aria-busy="true"><p>Loading agent draft…</p></StudioPage>;
  if (load.kind === "missing")
    return <StudioPage data-testid="playground-missing"><StudioResultState title="Agent draft not found" description="No visible agent draft matches this link. It may have been published, discarded or belong to another tenant." /></StudioPage>;
  if (load.kind === "denied")
    return <StudioPage data-testid="playground-denied"><StudioResultState tone="danger" title="Not permitted" description="Testing an agent needs permission to read its draft and to manage draft Test Runs." /></StudioPage>;
  if (load.kind === "error")
    return <StudioPage><StudioResultState tone="danger" title="Agent draft unavailable" description={load.message}
      actions={<StudioButton onClick={() => void loadDraft()}>Retry</StudioButton>} /></StudioPage>;

  const draft = load.draft;
  const inputs = businessInputs(draft);
  const continuity = allowsContinuity(draft);
  // A run whose status is no longer refreshed does not hold the page: the author can reload or start another run.
  const active = run !== null && !(run.evidence && isTerminal(run.evidence.status)) && run.refusal === null && run.failures < maximumPollFailures;
  const continuation = conversations.revision === draft.revision ? conversations.items.find(item => item.conversationId === selected) ?? null : null;

  const start = async () => {
    const parsed = parseInputs(inputs, values);
    setErrors(parsed.errors);
    if (Object.keys(parsed.errors).length > 0 || active || starting) return;
    // Retrying the same submission reuses its idempotency key, so a lost response never starts a second run.
    const signature = JSON.stringify({ revision: draft.revision, values: parsed.values, continuation });
    const current = attempt.current?.signature === signature ? attempt.current : { key: newIdempotencyKey(), signature };
    attempt.current = current;
    setStarting(true);
    setStartError(null);
    try {
      const started = await startPlaygroundRun(context, draft, parsed.values, continuation, current.key);
      if (!alive.current) return;
      attempt.current = null;
      // A refused start is still a 202 receipt: its reason is only on this response, not in the agent evidence.
      const refused = ["validationrejected", "dispatchrejected"].includes(started.status.toLowerCase());
      setRun({ testRunId: started.testRunId, evidence: null, stopping: false, stopNote: null, failures: 0,
        refusal: refused ? { code: started.failure?.code, message: started.failure?.message ?? started.reason ?? "The run was refused before the agent started.",
          diagnostics: (started.failure?.diagnostics ?? []).map(item => ({ code: item.code, message: item.message })) } : null });
    } catch (error) {
      if (!alive.current) return;
      const status = error instanceof StudioHttpError ? error.status : (error as { status?: number }).status;
      // Only an answer that may still succeed keeps the key; a refusal of this exact submission frees it.
      if (status !== undefined && status < 500 && status !== 408 && status !== 429) attempt.current = null;
      setStartError(error instanceof Error ? error.message : "The run could not be started.");
    } finally {
      if (alive.current) setStarting(false);
    }
  };

  const stop = async () => {
    if (!run || run.stopping) return;
    setRun({ ...run, stopping: true });
    try {
      const cancelled = await cancelRun(context, run.testRunId);
      if (!alive.current) return;
      const status = cancelled.cancellation?.status;
      const reason = cancelled.cancellation?.reason;
      // Only a requested or cancelling stop was taken; anything else leaves Stop available to try again.
      setRun(current => current && (status === "Requested" || status === "Cancelling"
        ? { ...current, stopNote: "Stop requested. A request already sent to the model is not recalled." }
        : { ...current, stopping: false, stopNote: status === "Terminal" ? "The run had already ended." : `Stop was not applied${reason ? `: ${reason}` : "."}` }));
    } catch (error) {
      if (!alive.current) return;
      setRun(current => current && { ...current, stopping: false, stopNote: error instanceof Error ? error.message : "Stop could not be requested." });
    }
  };

  return (
    <StudioPage className="agent-playground">
      <StudioPageHeader
        icon={<FlaskConical aria-hidden="true" size={18} />}
        eyebrow="Agent playground"
        title={`Draft revision ${draft.revision}`}
        description="Runs the saved revision on the production agent runtime. Unsaved editor changes are not included."
        actions={<>
          {navigate ? <StudioButton onClick={() => navigate(activityDefinitionPath(draft.definitionId, draft.draftId))}>Edit draft</StudioButton> : null}
          <StudioButton aria-label="Reload draft" disabled={active} onClick={() => void loadDraft()}><RefreshCw aria-hidden="true" size={14} /> Reload draft</StudioButton>
        </>} />

      <form className="agent-playground__form" aria-label="Run" onSubmit={event => { event.preventDefault(); void start(); }}>
        {inputs.map(input => (
          <InputField key={input.referenceKey} input={input} value={values[input.referenceKey] ?? ""} error={errors[input.referenceKey]} disabled={active || starting}
            onChange={value => {
              setValues(current => ({ ...current, [input.referenceKey]: value }));
              // An edited field's earlier complaint no longer describes it; the next run validates again.
              setErrors(current => { const { [input.referenceKey]: _, ...rest } = current; return rest; });
            }} />
        ))}
        {continuity ? (
          <fieldset className="agent-playground__conversation" disabled={active || starting}>
            <legend>Conversation</legend>
            <label><input type="radio" name="conversation" checked={continuation === null} onChange={() => setSelected(null)} /> Fresh conversation</label>
            {conversations.items.map(item => (
              <label key={item.conversationId}>
                <input type="radio" name="conversation" checked={continuation?.conversationId === item.conversationId}
                  onChange={() => setSelected(item.conversationId)} /> Continue conversation at revision {item.revision}
              </label>
            ))}
            {conversations.items.length === 0 ? <p className="agent-playground__hint">Conversations accepted by runs of this revision can be continued here.</p> : null}
          </fieldset>
        ) : null}
        <div className="agent-playground__actions">
          <StudioButton type="submit" variant="primary" disabled={active || starting}><Play aria-hidden="true" size={14} /> {starting ? "Starting…" : "Run"}</StudioButton>
          {active ? <StudioButton disabled={run?.stopping} onClick={() => void stop()}><Square aria-hidden="true" size={14} /> {run?.stopping ? "Stopping…" : "Stop"}</StudioButton> : null}
        </div>
        {startError ? <StudioAlert tone="danger">{startError}</StudioAlert> : null}
      </form>

      {/* Mounted before any run, so the first status a run reports is announced too. */}
      <p role="status" className="agent-playground__announce" data-testid="playground-announce">{run ? announcement(run) : ""}</p>
      {run ? <RunEvidence run={run} revision={draft.revision} onContinue={reference => setSelected(reference.conversationId)}
        onRetry={() => setRun(current => current && { ...current, failures: 0 })} /> : null}
    </StudioPage>
  );
}

function InputField({ input, value, error, disabled, onChange }: { input: PlaygroundInput; value: string; error?: string; disabled: boolean; onChange(value: string): void }) {
  const id = useId();
  const text = isText(input);
  return (
    <div className="agent-playground__input">
      <label htmlFor={id}>{input.displayName?.trim() || input.name}{input.isRequired ? " (required)" : ""}</label>
      {input.description ? <p className="agent-playground__hint">{input.description}</p> : null}
      <textarea id={id} value={value} disabled={disabled} aria-invalid={error ? true : undefined}
        placeholder={text ? "Text" : "JSON value"} onChange={event => onChange(event.target.value)} />
      {error ? <p className="agent-playground__error" role="alert">{error}</p> : null}
    </div>
  );
}

// A single string travels as typed text; every other type, collections of strings included, is entered as JSON.
function isText(input: PlaygroundInput) {
  return input.type.alias === "String" && (input.type.collectionKind ?? "Single") === "Single";
}

/**
 * An empty optional input is sent as null when it can be null. Draft Test Runs cannot leave an input out yet (#3768), so
 * an empty optional input that cannot be null still needs a value.
 */
export function parseInputs(inputs: PlaygroundInput[], raw: Record<string, string>) {
  const values: Record<string, unknown> = {};
  const errors: Record<string, string> = {};
  for (const input of inputs) {
    const text = raw[input.referenceKey] ?? "";
    if (!text.trim()) {
      if (input.isRequired) errors[input.referenceKey] = "Enter a value.";
      else if (input.isNullable) values[input.referenceKey] = null;
      else errors[input.referenceKey] = "Enter a value; a test run cannot leave this input out yet.";
      continue;
    }
    if (isText(input)) { values[input.referenceKey] = text; continue; }
    try { values[input.referenceKey] = JSON.parse(text); }
    catch { errors[input.referenceKey] = "Enter valid JSON."; }
  }
  return { values, errors };
}

function RunEvidence({ run, revision, onContinue, onRetry }: { run: Run; revision: number; onContinue(reference: ConversationReference): void; onRetry(): void }) {
  const evidence = run.evidence;
  const invocation = evidence?.invocation;
  const terminal = evidence ? isTerminal(evidence.status) : false;
  // Only a run of the revision on screen can be continued; an earlier revision's conversation belongs to another candidate.
  const accepted = evidence && evidence.draftRevision === revision ? acceptedConversation(evidence) : null;
  return (
    <section className="agent-playground__run" aria-label="Run result" data-status={evidence?.status ?? "Starting"}>
      <div>
        <h2>Run <StatusChip tone={statusTone(evidence?.status)}>{evidence?.status ?? (run.refusal ? "Refused" : "Starting")}</StatusChip></h2>
        <p data-testid="playground-stage">{run.refusal ? "The run was refused before the agent started." : stageText(evidence)}</p>
      </div>
      {run.refusal ? (
        <StudioAlert tone="danger">
          <span data-testid="playground-refusal">{run.refusal.message}</span>{run.refusal.code ? <> <code>{run.refusal.code}</code></> : null}
          {run.refusal.diagnostics.length > 0 ? (
            <ul className="agent-playground__diagnostics" data-testid="playground-diagnostics">
              {run.refusal.diagnostics.map((item, index) => <li key={`${item.code}-${index}`}><code>{item.code}</code> {item.message}</li>)}
            </ul>
          ) : null}
        </StudioAlert>
      ) : null}
      {run.failures > 0 && run.failures < maximumPollFailures ? <p className="agent-playground__hint">The latest status could not be read; showing the last known state.</p> : null}
      {run.failures >= maximumPollFailures ? (
        <p className="agent-playground__hint" data-testid="playground-poll-stopped">The status could not be read, so it is no longer refreshed.{" "}
          <StudioButton size="sm" onClick={onRetry}>Retry</StudioButton></p>
      ) : null}
      {run.stopNote ? <p role="status">{run.stopNote}</p> : null}
      {invocation ? <InvocationEvidenceDetails invocation={invocation} valuesPermitted={evidence?.valuesPermitted !== false} terminal={terminal} versionLabel="Candidate"
        outcomeUnknownAdvice={invocation.agent.allowContinuity
          ? "Start a fresh conversation, or continue from the last accepted revision."
          : "Run again only once a repeated request is acceptable."} /> : null}
      {invocation ? <p><a href={invocationPath({ workflowExecutionId: invocation.workflowExecutionId, activityExecutionId: invocation.activityExecutionId })}
        data-testid="playground-invocation-link">Inspect this invocation</a></p> : null}
      {terminal && accepted ? <StudioButton onClick={() => onContinue(accepted)}>Continue this conversation</StudioButton> : null}
    </section>
  );
}


function announcement(run: Run) {
  if (run.refusal) return "The run was refused before the agent started.";
  return `${run.evidence?.status ?? "Starting"}. ${stageText(run.evidence)}`;
}

function stageText(evidence: PlaygroundEvidence | null) {
  if (!evidence) return "Starting the run…";
  const turn = evidence.invocation?.turn;
  if (isTerminal(evidence.status) && !turn) return evidence.invocation ? "The agent ended before a model turn was admitted." : "The run ended before the agent started.";
  switch (turn?.status) {
    case undefined: return "Waiting for the agent to start.";
    case "Prepared": case "Claimed": return "Admitted; waiting for a model worker.";
    case "Dispatching": return "Calling the model. Partial output is not shown; only an accepted result is.";
    case "Accepted": return "The model's result was accepted.";
    case "OutcomeUnknown": return "The outcome of the model request is unknown.";
    case "Failed": return `The model turn failed${turn.failureCode ? ` (${turn.failureCode})` : ""}.`;
    case "Cancelled": return "The model turn was cancelled.";
  }
}



function statusTone(status?: string) {
  switch (status?.toLowerCase()) {
    case "completed": return "success" as const;
    case "faulted": case "failed": case "validationrejected": case "dispatchrejected": return "danger" as const;
    case "cancelled": case "expired": return "warning" as const;
    default: return "accent" as const;
  }
}
