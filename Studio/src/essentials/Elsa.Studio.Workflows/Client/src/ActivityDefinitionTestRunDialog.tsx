import {
  useCallback,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  type Dispatch,
  type SetStateAction
} from "react";
import {
  AlertCircle,
  CheckCircle2,
  CircleStop,
  ExternalLink,
  Play,
  RotateCcw,
  X
} from "lucide-react";
import {
  StudioHttpError,
  type StudioActivityDiagnostic,
  type StudioActivityDiagnosticFocusResult,
  type StudioEndpointContext,
  type StudioWorkflowRunInputEditorContribution
} from "@elsa-workflows/studio-sdk";
import type {
  ActivityDefinitionDraftView,
  ActivityDraftValidationView
} from "./activityDefinitionTypes";
import {
  cancelActivityDraftTestRun,
  getActivityDraftTestRun,
  getActivityDraftTestRunByIdempotencyKey,
  startActivityDraftTestRun,
  type ActivityDraftTestRunView
} from "./api/publishing";
import {
  activityTestRunWorkbenchUrl,
  classifyActivityTestRun,
  isTerminalActivityTestRun,
  parseActivityTestRunInputs,
  toWorkflowRunInput,
  type ActivityTestRunInputDraft,
  type ActivityTestRunStage
} from "./activityDefinitionTestRuns";
import { ActivityDiagnosticList } from "./ActivityDefinitionDiagnosticsPanel";
import { observeActivityDefinitions } from "./activityDefinitionObservability";
import { WorkflowRunInputField } from "./workflow-editor/WorkflowRunInputDialog";
import { useDialogFocus } from "./workflow-editor/useDialogFocus";

type PreparationPhase = "saving" | "validating" | "ready" | "failed";
type DispatchPhase = "idle" | "dispatching" | "reconciling" | "tracking" | "cancelling";

export interface PreparedActivityTestRunRevision {
  revision: number;
  validation: ActivityDraftValidationView;
}

export function ActivityDefinitionTestRunDialog({
  context,
  draft,
  definitionLabel,
  inputEditors,
  prepareExactRevision,
  onFocusDiagnostic,
  onClose,
  onOpenRun
}: {
  context: StudioEndpointContext;
  draft: ActivityDefinitionDraftView;
  definitionLabel: string;
  inputEditors: StudioWorkflowRunInputEditorContribution[];
  prepareExactRevision(onPhase: (phase: "saving" | "validating") => void): Promise<PreparedActivityTestRunRevision | null>;
  onFocusDiagnostic(diagnostic: StudioActivityDiagnostic, trigger: HTMLButtonElement): Promise<StudioActivityDiagnosticFocusResult>;
  onClose(): void;
  onOpenRun(path: string): void;
}) {
  const titleId = useId();
  const dialogRef = useRef<HTMLElement>(null);
  const pollTimerRef = useRef<number | null>(null);
  const prepareExactRevisionRef = useRef(prepareExactRevision);
  const preparationTaskRef = useRef<Promise<PreparedActivityTestRunRevision | null> | null>(null);
  const preparationPhaseRef = useRef<PreparationPhase>("saving");
  const preparationListenerRef = useRef<((phase: "saving" | "validating") => void) | null>(null);
  const [preparation, setPreparation] = useState<PreparationPhase>("saving");
  const [prepared, setPrepared] = useState<PreparedActivityTestRunRevision | null>(null);
  const [dispatchPhase, setDispatchPhase] = useState<DispatchPhase>("idle");
  const [inputDrafts, setInputDrafts] = useState<Record<string, ActivityTestRunInputDraft>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [failedEditorKeys, setFailedEditorKeys] = useState<Set<string>>(() => new Set());
  const [currentRun, setCurrentRun] = useState<ActivityDraftTestRunView | null>(null);
  const [evidenceHistory, setEvidenceHistory] = useState<ActivityDraftTestRunView[]>([]);
  const [reconcileKey, setReconcileKey] = useState<string | null>(null);
  const [reconciledAfterAcceptance, setReconciledAfterAcceptance] = useState(false);
  const [operationError, setOperationError] = useState("");
  const blocksClose = dispatchPhase === "dispatching" ||
    dispatchPhase === "reconciling" ||
    dispatchPhase === "cancelling";
  const contractInputs = useMemo(
    () => [...draft.contract.inputs].sort((left, right) =>
      (left.order ?? 0) - (right.order ?? 0) ||
      left.referenceKey.localeCompare(right.referenceKey)),
    [draft.contract.inputs]);

  useDialogFocus(dialogRef, blocksClose ? null : onClose);

  useEffect(() => {
    let active = true;
    const listener = (phase: "saving" | "validating") => {
      if (active) setPreparation(phase);
    };
    preparationListenerRef.current = listener;
    setPreparation(preparationPhaseRef.current);
    preparationTaskRef.current ??= prepareExactRevisionRef.current(phase => {
      preparationPhaseRef.current = phase;
      preparationListenerRef.current?.(phase);
    });
    void preparationTaskRef.current.then(result => {
      if (!active) return;
      if (!result) {
        setPreparation("failed");
        return;
      }
      setPrepared(result);
      setPreparation("ready");
    }, () => {
      if (active) setPreparation("failed");
    });
    return () => {
      active = false;
      if (preparationListenerRef.current === listener) preparationListenerRef.current = null;
    };
  }, []);

  useEffect(() => () => {
    if (pollTimerRef.current !== null) window.clearTimeout(pollTimerRef.current);
  }, []);

  const recordRun = useCallback((view: ActivityDraftTestRunView) => {
    setCurrentRun(view);
    setEvidenceHistory(current => {
      const withoutCurrent = current.filter(item => item.testRunId !== view.testRunId);
      return [view, ...withoutCurrent].slice(0, 5);
    });
    setDispatchPhase("tracking");
    observeActivityDefinitions({
      event: "test-run",
      surface: "editor",
      outcome: observationOutcome(classifyActivityTestRun(view))
    });
  }, []);

  useEffect(() => {
    if (!reconcileKey) return;
    let active = true;
    const reconcile = async () => {
      try {
        const view = await getActivityDraftTestRunByIdempotencyKey(
          context,
          draft.draftId,
          reconcileKey);
        if (!active) return;
        setReconcileKey(null);
        setOperationError("");
        recordRun(view);
      } catch (error) {
        if (!active) return;
        if (!(error instanceof StudioHttpError) || error.status !== 404) {
          setOperationError("Runtime acknowledgement remains unavailable. Studio is reconciling the durable receipt without dispatching again.");
        }
        pollTimerRef.current = window.setTimeout(() => void reconcile(), 900);
      }
    };
    void reconcile();
    return () => {
      active = false;
      if (pollTimerRef.current !== null) window.clearTimeout(pollTimerRef.current);
    };
  }, [context, draft.draftId, reconcileKey, recordRun]);

  const stage = currentRun
    ? classifyActivityTestRun(currentRun, reconciledAfterAcceptance)
    : null;

  useEffect(() => {
    if (!currentRun || !stage || isTerminalActivityTestRun(stage)) return;
    let active = true;
    pollTimerRef.current = window.setTimeout(async () => {
      try {
        const next = await getActivityDraftTestRun(context, currentRun.testRunId);
        if (!active) return;
        if (classifyActivityTestRun(currentRun) === "accepted") {
          setReconciledAfterAcceptance(true);
        }
        setOperationError("");
        recordRun(next);
      } catch {
        if (!active) return;
        setOperationError("Latest Runtime Evidence is temporarily unavailable. Studio will keep reconciling this Test Run.");
        pollTimerRef.current = window.setTimeout(() => {
          setCurrentRun(current => current ? { ...current } : current);
        }, 900);
      }
    }, 900);
    return () => {
      active = false;
      if (pollTimerRef.current !== null) window.clearTimeout(pollTimerRef.current);
    };
  }, [context, currentRun, recordRun, stage]);

  const dispatch = async () => {
    if (!prepared?.validation.isValid || dispatchPhase === "dispatching" || dispatchPhase === "reconciling") return;
    const parsed = parseActivityTestRunInputs(
      contractInputs,
      inputDrafts,
      inputEditors,
      failedEditorKeys);
    const contributionFailureKeys = Object.keys(parsed.contributionFailures ?? {});
    if (contributionFailureKeys.length > 0) {
      setFailedEditorKeys(current => new Set([...current, ...contributionFailureKeys]));
      setErrors({
        ...parsed.errors,
        ...Object.fromEntries(contributionFailureKeys.map(key => [
          key,
          `${parsed.errors[key] ?? "The contributed input editor could not prepare this value."} Use the standard input control and retry.`
        ]))
      });
      return;
    }
    setErrors(parsed.errors);
    if (Object.keys(parsed.errors).length > 0) return;

    const idempotencyKey = `activity-test-run-${crypto.randomUUID()}`;
    setDispatchPhase("dispatching");
    setOperationError("");
    setReconciledAfterAcceptance(false);
    observeActivityDefinitions({ event: "test-run", surface: "editor", outcome: "dispatching" });
    try {
      const view = await startActivityDraftTestRun(context, draft.draftId, {
        expectedRevision: prepared.revision,
        idempotencyKey,
        inputs: parsed.inputs
      });
      recordRun(view);
    } catch (error) {
      if (error instanceof StudioHttpError && error.status < 500) {
        setDispatchPhase("idle");
        setOperationError(dispatchRejectionDescription(error.status));
        observeActivityDefinitions({ event: "test-run", surface: "editor", outcome: "dispatch-rejected" });
        return;
      }
      setDispatchPhase("reconciling");
      setReconcileKey(idempotencyKey);
      setOperationError("The dispatch response was ambiguous. Studio is reconciling the durable receipt with the same idempotency key; it will not create a duplicate run.");
      observeActivityDefinitions({ event: "test-run", surface: "editor", outcome: "reconciling" });
    }
  };

  const cancel = async () => {
    if (!currentRun || dispatchPhase === "cancelling") return;
    setDispatchPhase("cancelling");
    setOperationError("");
    try {
      recordRun(await cancelActivityDraftTestRun(context, currentRun.testRunId));
    } catch {
      setDispatchPhase("tracking");
      setOperationError("Cancellation acknowledgement is unavailable. The current Runtime Evidence remains authoritative.");
    }
  };

  const validation = prepared?.validation;
  const busy = blocksClose;
  const canRerun = Boolean(stage && isTerminalActivityTestRun(stage));

  return (
    <div className="ad-dialog-backdrop" role="presentation">
      <section
        ref={dialogRef}
        className="ad-dialog ad-management-dialog ad-test-run-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
      >
        <header>
          <div>
            <span className="ad-kicker">Activity Definition Test Run</span>
            <h2 id={titleId}>{definitionLabel}</h2>
            <p>Exact draft revision {prepared?.revision ?? draft.revision}. Dispatch creates immutable Runtime Evidence without publishing this Activity Definition.</p>
          </div>
          <button type="button" className="ad-icon-action" aria-label="Close Test Run" onClick={onClose} disabled={busy}>
            <X size={17} aria-hidden />
          </button>
        </header>

        {preparation !== "ready" ? (
          <section className="ad-historical-contract ad-test-run-preparation" role={preparation === "failed" ? "alert" : "status"} aria-live="polite">
            <strong>{preparationLabel(preparation)}</strong>
            <p>{preparationDescription(preparation)}</p>
          </section>
        ) : null}

        {validation && !validation.isValid ? (
          <section className="ad-historical-contract ad-test-run-rejection" aria-labelledby={`${titleId}-validation`}>
            <h3 id={`${titleId}-validation`}><AlertCircle size={17} aria-hidden /> Draft validation rejected revision {validation.revision}</h3>
            <p>Correct the shared structured Diagnostics before dispatch. No Runtime request was sent.</p>
            <ActivityDiagnosticList
              diagnostics={validation.diagnostics}
              label="Test Run validation diagnostics"
              onFocus={async (diagnostic, trigger) => {
                onClose();
                await nextFrame();
                return onFocusDiagnostic(diagnostic, trigger);
              }}
            />
          </section>
        ) : null}

        {validation?.isValid && !currentRun ? (
          <section className="ad-historical-contract ad-test-run-inputs" aria-labelledby={`${titleId}-inputs`}>
            <div className="ad-section-heading-row ad-test-run-section-heading">
              <div>
                <h3 id={`${titleId}-inputs`}>Contract inputs</h3>
                <p>Absent applies the contract default. Present null and Present value are sent explicitly and never collapse into Absent.</p>
              </div>
              <span><CheckCircle2 size={16} aria-hidden /> Revision {validation.revision} validated</span>
            </div>
            {contractInputs.length === 0 ? (
              <p className="ad-dialog-note">This Activity Definition declares no public inputs.</p>
            ) : contractInputs.map(contractInput => {
              const input = toWorkflowRunInput(contractInput);
              const value = inputDrafts[input.referenceKey] ?? { presence: "Absent", draft: "" };
              return (
                <div className="ad-form-grid ad-test-run-input" key={input.referenceKey}>
                  <label className="ad-dialog-field">
                    <span>{input.displayName || input.name} presence</span>
                    <select
                      aria-label={`${input.displayName || input.name} presence`}
                      value={value.presence}
                      disabled={busy}
                      onChange={event => {
                        const presence = event.currentTarget.value as ActivityTestRunInputDraft["presence"];
                        setInputDrafts(current => ({
                          ...current,
                          [input.referenceKey]: { ...value, presence }
                        }));
                        clearError(setErrors, input.referenceKey);
                      }}
                    >
                      <option value="Absent">Absent{contractInput.default ? " · apply default" : ""}</option>
                      <option value="Null" disabled={!contractInput.isNullable}>Present null</option>
                      <option value="Value">Present value</option>
                    </select>
                    <small>{presenceDescription(contractInput, value.presence)}</small>
                  </label>
                  {value.presence === "Value" ? (
                    <WorkflowRunInputField
                      input={input}
                      editors={failedEditorKeys.has(input.referenceKey) ? [] : inputEditors}
                      value={value.draft}
                      error={errors[input.referenceKey]}
                      disabled={busy}
                      onEditorFailure={() => {
                        setFailedEditorKeys(current => new Set(current).add(input.referenceKey));
                        setErrors(current => ({
                          ...current,
                          [input.referenceKey]: `The ${input.displayName || input.name} editor failed. Use the standard input control.`
                        }));
                      }}
                      onChange={next => {
                        setInputDrafts(current => ({
                          ...current,
                          [input.referenceKey]: { presence: "Value", draft: next }
                        }));
                        clearError(setErrors, input.referenceKey);
                      }}
                    />
                  ) : errors[input.referenceKey] ? <small className="ad-test-run-input-error" role="alert">{errors[input.referenceKey]}</small> : null}
                </div>
              );
            })}
          </section>
        ) : null}

        {currentRun && stage ? (
          <ActivityTestRunEvidence
            definitionLabel={definitionLabel}
            view={currentRun}
            stage={stage}
            dispatchPhase={dispatchPhase}
            history={evidenceHistory}
          />
        ) : null}

        {operationError ? <p className="ad-inline-error ad-test-run-operation-error" role="alert">{operationError}</p> : null}

        <footer>
          <button type="button" onClick={onClose} disabled={busy}>Close</button>
          {currentRun?.cancellation.capabilityAdvertised && currentRun.cancellation.available && stage && !isTerminalActivityTestRun(stage) ? (
            <button type="button" onClick={() => void cancel()} disabled={busy}>
              <CircleStop size={16} aria-hidden /> {dispatchPhase === "cancelling" ? "Requesting cancellation…" : "Cancel Test Run"}
            </button>
          ) : null}
          {currentRun?.outerActivityExecutionId ? (
            <button type="button" onClick={() => onOpenRun(activityTestRunWorkbenchUrl(currentRun))}>
              <ExternalLink size={16} aria-hidden /> Open focused Runtime Evidence
            </button>
          ) : null}
          {canRerun ? (
            <button type="button" onClick={() => void dispatch()} disabled={busy}>
              <RotateCcw size={16} aria-hidden /> Rerun as new evidence
            </button>
          ) : null}
          {!currentRun && validation?.isValid ? (
            <button type="button" className="ad-primary-action" onClick={() => void dispatch()} disabled={busy}>
              <Play size={16} aria-hidden /> {dispatchPhase === "dispatching" ? "Dispatching…" : dispatchPhase === "reconciling" ? "Reconciling…" : "Dispatch Test Run"}
            </button>
          ) : null}
        </footer>
      </section>
    </div>
  );
}

function ActivityTestRunEvidence({
  definitionLabel,
  view,
  stage,
  dispatchPhase,
  history
}: {
  definitionLabel: string;
  view: ActivityDraftTestRunView;
  stage: ActivityTestRunStage;
  dispatchPhase: DispatchPhase;
  history: ActivityDraftTestRunView[];
}) {
  const copy = stageCopy(stage);
  return (
    <section className={`ad-historical-contract ad-test-run-evidence is-${stage}`} aria-labelledby="activity-test-run-evidence-title">
      <div className="ad-section-heading-row ad-test-run-section-heading">
        <div>
          <span className="ad-kicker">Runtime Evidence</span>
          <h3 id="activity-test-run-evidence-title">{copy.title}</h3>
          <p>{copy.description}</p>
        </div>
        <span className="ad-badge ad-test-run-stage" role="status" aria-live="polite">{dispatchPhase === "cancelling" ? "Cancellation requested" : copy.label}</span>
      </div>
      <p className="ad-selected-record ad-test-run-subject"><strong>{definitionLabel}</strong> · exact draft revision {view.draftRevision}</p>
      {view.failure ? (
        <div className={`ad-inline-error ad-test-run-failure is-${view.failure.kind === "Validation" ? "validation" : "runtime"}`} role="alert">
          <strong>{view.failure.kind === "Validation" ? "Draft Diagnostics" : "Runtime dispatch rejection"}</strong>
          <code>{safeCode(view.failure.code)}</code>
          <p>{view.failure.message}</p>
        </div>
      ) : null}
      {view.failure?.diagnostics.length ? (
        <ActivityDiagnosticList
          diagnostics={view.failure.diagnostics}
          label="Test Run rejection diagnostics"
          onFocus={async () => ({
            kind: "unsupported",
            announcement: "This Runtime diagnostic does not map to an editable draft control."
          })}
        />
      ) : null}
      <ExpirationEvidence view={view} />
      <details>
        <summary>Technical details</summary>
        <dl className="ad-review-facts ad-test-run-technical">
          <div><dt>Test Run</dt><dd><code>{view.testRunId}</code></dd></div>
          <div><dt>Workflow execution</dt><dd><code>{view.workflowExecutionId}</code></dd></div>
          {view.outerActivityExecutionId ? <div><dt>Outer activity execution</dt><dd><code>{view.outerActivityExecutionId}</code></dd></div> : null}
          {view.artifactId ? <div><dt>Wrapper artifact</dt><dd><code>{view.artifactId}</code></dd></div> : null}
          {view.sourceReferenceId ? <div><dt>Source Reference</dt><dd><code>{view.sourceReferenceId}</code></dd></div> : null}
          {view.commandDispatchStatus ? <div><dt>Command dispatch</dt><dd>{view.commandDispatchStatus}</dd></div> : null}
        </dl>
      </details>
      {history.length > 1 ? (
        <details>
          <summary>Immutable rerun history ({history.length})</summary>
          <ol className="ad-test-run-history">
            {history.map(item => <li key={item.testRunId}><code>{item.testRunId}</code> · revision {item.draftRevision} · {item.status}</li>)}
          </ol>
        </details>
      ) : null}
    </section>
  );
}

function ExpirationEvidence({ view }: { view: ActivityDraftTestRunView }) {
  const expiration = view.expiration;
  if (!expiration.sourceReferenceExpired) {
    return <p className="ad-test-run-expiry">Source Reference expires {formatTime(expiration.sourceReferenceExpiresAt)}. The durable receipt expires {formatTime(expiration.receiptExpiresAt)}.</p>;
  }
  if (expiration.runStillActive) {
    return <p className="ad-test-run-expiry"><strong>Run still active.</strong> The Source Reference lifetime elapsed, but Runtime continues this already-started execution and retains its evidence.</p>;
  }
  if (expiration.evidenceRetained) {
    return <p className="ad-test-run-expiry"><strong>Source Reference expired.</strong> Runtime Evidence is retained independently and remains available.</p>;
  }
  return <p className="ad-test-run-expiry"><strong>Test Run expired.</strong> Neither its Source Reference nor Runtime Evidence remains available.</p>;
}

function stageCopy(stage: ActivityTestRunStage) {
  switch (stage) {
    case "accepted": return { label: "Accepted", title: "Dispatch accepted", description: "Runtime accepted the command. Acceptance is not execution success; Studio is waiting for scheduling evidence." };
    case "waiting": return { label: "Waiting for scheduling", title: "Waiting for scheduling", description: "The durable receipt exists, but the outer activity execution boundary is not available yet." };
    case "running": return { label: "Running", title: "Activity Definition is running", description: "Runtime has scheduled the outer activity execution boundary." };
    case "suspended": return { label: "Suspended", title: "Activity Definition is suspended", description: "Runtime Evidence is retained while the execution waits for a resume condition." };
    case "completed": return { label: "Completed", title: "Activity Definition completed", description: "Runtime reports terminal completion for this exact Test Run." };
    case "faulted": return { label: "Faulted", title: "Activity Definition faulted", description: "Runtime reports terminal fault evidence for this exact Test Run." };
    case "cancelled": return { label: "Cancelled", title: "Activity Definition was cancelled", description: "Runtime reports terminal cancellation evidence." };
    case "validation-rejected": return { label: "Validation rejected", title: "Draft validation rejected dispatch", description: "The server rejected the exact draft revision before Runtime dispatch." };
    case "dispatch-rejected": return { label: "Runtime rejected", title: "Runtime rejected dispatch", description: "Draft validation and Runtime dispatch are separate. Runtime did not accept this command." };
    case "dispatch-ambiguous": return { label: "Reconciling", title: "Dispatch outcome is ambiguous", description: "Studio is reconciling the durable receipt and will not issue a second dispatch." };
    case "expired": return { label: "Expired", title: "Test Run evidence expired", description: "The transient Source Reference and Runtime Evidence are no longer retained." };
  }
}

function preparationLabel(phase: PreparationPhase) {
  if (phase === "saving") return "Saving exact draft revision…";
  if (phase === "validating") return "Validating exact saved revision…";
  return "Test Run preparation failed";
}

function preparationDescription(phase: PreparationPhase) {
  if (phase === "saving") return "Studio is flushing autosave and will not dispatch an unsaved editor snapshot.";
  if (phase === "validating") return "The backend is validating the same revision that will be bound to the Test Run receipt.";
  return "The exact saved revision could not be confirmed. No Runtime dispatch was attempted.";
}

function dispatchRejectionDescription(status: number) {
  if (status === 401 || status === 403) {
    return "You are not authorized to dispatch this Test Run. No draft or Runtime identifiers were disclosed.";
  }
  if (status === 404) {
    return "The exact authorized draft is no longer available. No Runtime dispatch was accepted.";
  }
  if (status === 409) {
    return "The exact draft revision changed before dispatch. Reopen Test Run to validate the latest saved revision.";
  }
  return "Runtime rejected the Test Run request before a durable receipt was accepted.";
}

function presenceDescription(
  input: ActivityDefinitionDraftView["contract"]["inputs"][number],
  presence: ActivityTestRunInputDraft["presence"]
) {
  if (presence === "Absent") {
    return input.default
      ? `The ${input.default.syntax || "contract"} default applies only while this input is Absent.`
      : "No value is supplied; the backend evaluates required-input rules.";
  }
  if (presence === "Null") return "A literal JSON null is supplied. The contract default does not apply.";
  return "The contributed or standard editor supplies an explicit value. The contract default does not apply.";
}

function observationOutcome(stage: ActivityTestRunStage) {
  if (stage === "accepted") return "accepted";
  if (stage === "waiting") return "waiting";
  if (stage === "validation-rejected") return "validation-rejected";
  if (stage === "dispatch-rejected") return "dispatch-rejected";
  if (stage === "dispatch-ambiguous") return "reconciling";
  return stage;
}

function safeCode(code: string) {
  return /^[a-z0-9][a-z0-9._-]{0,127}$/.test(code) ? code : "activity.test-run.failure";
}

function formatTime(value: string) {
  const parsed = new Date(value);
  return Number.isNaN(parsed.valueOf()) ? "at the server-defined retention boundary" : parsed.toLocaleString();
}

function clearError(
  setErrors: Dispatch<SetStateAction<Record<string, string>>>,
  key: string
) {
  setErrors(current => {
    if (!Object.prototype.hasOwnProperty.call(current, key)) return current;
    const next = { ...current };
    delete next[key];
    return next;
  });
}

function nextFrame() {
  return new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
}
