import { useStudioTimeZone, formatStudioDate } from "@elsa-workflows/studio-sdk";
import type { ActivityContractTypeCapability } from "./activityDefinitionTypes";
import { typeAllowsNull } from "./typeNullability";
import { StatusChip, StudioButton, StudioDialog, StudioDialogFooter, StudioField } from "@elsa-workflows/studio-ui";
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
  CircleStop,
  ExternalLink,
} from "lucide-react";
import {
  StudioHttpError,
  type StudioActivityDiagnostic,
  type StudioActivityDiagnosticFocusResult,
  type StudioExpressionEditorContribution,
  type StudioEndpointContext,
  type StudioWorkflowRunInputEditorContribution
} from "@elsa-workflows/studio-sdk";
import type {
  ActivityDefinitionDraftView,
  ActivityDefinitionVersionView,
  ActivityDraftValidationView
} from "./activityDefinitionTypes";
import {
  cancelActivityDraftTestRun,
  getActivityDraftTestRun,
  getActivityDraftTestRunByIdempotencyKey,
  getActivityVersionTestRunByIdempotencyKey,
  startActivityDraftTestRun,
  startActivityVersionTestRun,
  type ActivityDraftTestRunView
} from "./api/publishing";
import {
  activityTestRunActivityUrl,
  activityTestRunExecutableUrl,
  activityTestRunWorkflowUrl,
  classifyActivityTestRun,
  isTerminalActivityTestRun,
  parseActivityTestRunInputs,
  shortenActivityTestRunIdentifier,
  toWorkflowRunInput,
  type ActivityTestRunInputDraft,
  type ActivityTestRunStage
} from "./activityDefinitionTestRuns";
import { ActivityDiagnosticList } from "./ActivityDefinitionDiagnosticsPanel";
import { observeActivityDefinitions } from "./activityDefinitionObservability";
import { getActivityExecutionInspection } from "./api/runtime";
import type { ActivityExecutionInspection } from "./workflowTypes";
import { runtimeValueContextId, RuntimeValueEvidenceCard, RuntimeValueEvidenceResolutionProvider } from "./workflow-editor/RuntimeValueEvidence";
import { WorkflowRunInputField } from "./workflow-editor/WorkflowRunInputDialog";
import { CopyValueButton } from "./workflow-editor/executableShared";
import { StudioLink } from "./StudioLink";
import { ApiCapabilityUnavailableError, ApiCapabilityVersionMismatchError } from "./api/capabilities";

type PreparationPhase = "saving" | "validating" | "ready" | "failed";
type DispatchPhase = "idle" | "dispatching" | "reconciling" | "tracking" | "cancelling";

export interface PreparedActivityTestRunRevision {
  revision: number;
  validation: ActivityDraftValidationView;
}

export function ActivityDefinitionTestRunDialog({
  context,
  draft,
  sourceVersion = null,
  publishedRunAvailability = null,
  definitionLabel,
  inputEditors,
  typeCapabilities = [],
  prepareExactRevision,
  onFocusDiagnostic,
  onClose,
  restoreFocus,
  onOpenRun
}: {
  context: StudioEndpointContext;
  draft: ActivityDefinitionDraftView;
  sourceVersion?: ActivityDefinitionVersionView | null;
  publishedRunAvailability?: "unavailable" | "unverified" | null;
  definitionLabel: string;
  inputEditors: StudioWorkflowRunInputEditorContribution[];
  typeCapabilities?: readonly ActivityContractTypeCapability[];
  expressionEditors: StudioExpressionEditorContribution[];
  prepareExactRevision(onPhase: (phase: "saving" | "validating") => void): Promise<PreparedActivityTestRunRevision | null>;
  onFocusDiagnostic(diagnostic: StudioActivityDiagnostic, trigger: HTMLButtonElement): Promise<StudioActivityDiagnosticFocusResult>;
  onClose(): void;
  restoreFocus?(): void;
  onOpenRun(path: string): void;
}) {

  const titleId = useId();
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

  useEffect(() => {
    if (sourceVersion) {
      const result = {
        revision: 0,
        validation: {
          draftId: sourceVersion.versionId,
          revision: 0,
          isValid: true,
          validatedAt: sourceVersion.publishedAt,
          diagnostics: []
        }
      } satisfies PreparedActivityTestRunRevision;
      preparationTaskRef.current = Promise.resolve(result);
      preparationPhaseRef.current = "ready";
      setPrepared(result);
      setPreparation("ready");
      return;
    }
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
  }, [sourceVersion]);

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
        const view = sourceVersion
          ? await getActivityVersionTestRunByIdempotencyKey(context, sourceVersion.versionId, reconcileKey)
          : await getActivityDraftTestRunByIdempotencyKey(context, draft.draftId, reconcileKey);
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
  }, [context, draft.draftId, reconcileKey, recordRun, sourceVersion]);

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
        setOperationError("The latest run data is temporarily unavailable. Studio will keep reconciling this Test Run.");
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
      failedEditorKeys, typeCapabilities);
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
      const view = sourceVersion
        ? await startActivityVersionTestRun(context, sourceVersion.versionId, {
            idempotencyKey,
            inputs: parsed.inputs
          })
        : await startActivityDraftTestRun(context, draft.draftId, {
            expectedRevision: prepared.revision,
            idempotencyKey,
            inputs: parsed.inputs
          });
      recordRun(view);
    } catch (error) {
      if (sourceVersion && (error instanceof ApiCapabilityUnavailableError || error instanceof ApiCapabilityVersionMismatchError)) {
        setDispatchPhase("idle");
        setOperationError("Published-version Test Run is no longer available on this server. Restart Workbench with the current Foundation build and reload Studio. No Runtime execution was started.");
        return;
      }
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
      setOperationError("Cancellation acknowledgement is unavailable. The current run data remains authoritative.");
    }
  };

  const validation = publishedRunAvailability ? undefined : prepared?.validation;
  const busy = blocksClose;
  const canRerun = Boolean(stage && isTerminalActivityTestRun(stage));

  return (
    <StudioDialog open className="ad-test-run-dialog" title={`Test run: ${definitionLabel}`} onClose={onClose}
      closeLabel="Close Test Run" dismissDisabled={busy}
      onCloseAutoFocus={restoreFocus ? event => { event.preventDefault(); restoreFocus(); } : undefined}
      description={sourceVersion ? `Runs published version ${sourceVersion.version} without publishing.` : `Runs draft revision ${prepared?.revision ?? draft.revision} without publishing.`}>

        <div className="ad-test-run-body">
          {publishedRunAvailability ? (
            <section className="ad-historical-contract ad-test-run-rejection" role="alert">
              <p>{publishedRunAvailability === "unavailable"
                ? "This server does not advertise published Test Run dispatch and receipt lookup. Restart Workbench with the current Foundation build, then reload Studio. No inputs were entered and no Runtime execution was started."
                : "Studio could not verify published Test Run support. Check that the Workbench server is reachable, then reload Studio. No inputs were entered and no Runtime execution was started."}</p>
            </section>
          ) : null}
          {preparation !== "ready" ? (
            <section className="ad-historical-contract ad-test-run-preparation" role={preparation === "failed" ? "alert" : "status"} aria-live="polite">
              <strong>{preparationLabel(preparation)}</strong>
              {preparation === "failed" ? <p>{preparationDescription(preparation)}</p> : null}
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
                <h3 id={`${titleId}-inputs`}>Inputs</h3>
              </div>
              {contractInputs.length === 0 ? (
                <p className="ad-dialog-note">This Activity Definition declares no public inputs.</p>
              ) : contractInputs.map(contractInput => {
                const input = toWorkflowRunInput(contractInput);
                const value = inputDrafts[input.referenceKey] ?? { presence: "Absent", draft: "" };
                return (
                  <div className="ad-form-grid ad-test-run-input" key={input.referenceKey}>
                    <StudioField className="ad-dialog-field" label={input.displayName || input.name}>
                      <select
                        aria-label={input.displayName || input.name}
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
                        <option value="Absent">{contractInput.default ? "Use default" : "Omit"}</option>
                        <option value="Null" disabled={!typeAllowsNull(contractInput.type, typeCapabilities)}>Send null</option>
                        <option value="Value">Enter a value</option>
                      </select>
                    </StudioField>
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
              key={currentRun.testRunId}
              context={context}
              definitionLabel={definitionLabel}
              sourceVersion={sourceVersion}
              view={currentRun}
              stage={stage}
              dispatchPhase={dispatchPhase}
              history={evidenceHistory}
              onOpenRun={onOpenRun}
            />
          ) : null}

          {operationError ? <p className="ad-inline-error ad-test-run-operation-error" role="alert">{operationError}</p> : null}
        </div>

        <StudioDialogFooter
          confirmType="button"
          leading={currentRun?.cancellation.capabilityAdvertised && currentRun.cancellation.available && stage && !isTerminalActivityTestRun(stage) ?
            <StudioButton onClick={() => void cancel()} disabled={busy}><CircleStop size={16} aria-hidden /> {dispatchPhase === "cancelling" ? "Requesting cancellation…" : "Cancel Test Run"}</StudioButton> : null}
          onCancel={onClose} cancelDisabled={busy} cancelLabel="Close"
          confirmLabel={canRerun ? "Run again" : !currentRun && validation?.isValid ?
            (dispatchPhase === "dispatching" ? "Starting…" : dispatchPhase === "reconciling" ? "Reconciling…" : "Run test") : undefined}
          confirmDisabled={busy} onConfirm={() => void dispatch()}
        />
    </StudioDialog>
  );
}

function ActivityTestRunEvidence({
  context,
  definitionLabel,
  sourceVersion,
  view,
  stage,
  dispatchPhase,
  history,
  onOpenRun
}: {
  context: StudioEndpointContext;
  definitionLabel: string;
  sourceVersion: ActivityDefinitionVersionView | null;
  view: ActivityDraftTestRunView;
  stage: ActivityTestRunStage;
  dispatchPhase: DispatchPhase;
  history: ActivityDraftTestRunView[];
  onOpenRun(path: string): void;
}) {

  const copy = stageCopy(stage);
  const resultTitleId = useId();
  const [detailsOpen, setDetailsOpen] = useState(false);
  const [copyStatus, setCopyStatus] = useState("");
  const markCopied = (label: string) => setCopyStatus(`Copied ${label}.`);
  const markCopyFailed = (label: string) => setCopyStatus(`Could not copy ${label}.`);
  const executableUrl = activityTestRunExecutableUrl(view);
  const activityExecutionId = view.outerActivityExecutionId ?? null;
  const workflowExecutionId = view.workflowExecutionId;
  const inspectionIdentity = JSON.stringify([runtimeValueContextId(context), workflowExecutionId, activityExecutionId, view.status, view.updatedAt]);
  const [inspectionState, setInspectionState] = useState<{
    identity: string;
    status: "loading" | "ready" | "failed" | "unavailable";
    inspection?: ActivityExecutionInspection;
  } | null>(null);
  const currentInspection = inspectionState?.identity === inspectionIdentity ? inspectionState : null;
  const inspectionStatus = currentInspection?.status ?? (activityExecutionId && stage !== "expired" ? "loading" : "unavailable");
  const inspection = currentInspection?.inspection;
  const activityUrl = activityTestRunActivityUrl(view);
  const resolutionScope = activityExecutionId ? { context, workflowExecutionId, activityExecutionId } : null;

  useEffect(() => {
    if (!activityExecutionId || stage === "expired") {
      setInspectionState({ identity: inspectionIdentity, status: "unavailable" });
      return;
    }

    const controller = new AbortController();
    let active = true;
    setInspectionState({ identity: inspectionIdentity, status: "loading" });
    getActivityExecutionInspection(context, workflowExecutionId, activityExecutionId, controller.signal).then(result => {
      if (!active) return;
      if (result.workflowExecutionId !== workflowExecutionId || result.activityExecutionId !== activityExecutionId) {
        setInspectionState({ identity: inspectionIdentity, status: "failed" });
      } else {
        setInspectionState({ identity: inspectionIdentity, status: "ready", inspection: result });
      }
    }, () => {
      if (active) setInspectionState({ identity: inspectionIdentity, status: "failed" });
    });

    return () => {
      active = false;
      controller.abort();
    };
  }, [activityExecutionId, context, inspectionIdentity, stage, workflowExecutionId]);

  const outputSnapshots = inspection?.valueSnapshots.filter(snapshot => snapshot.subject === "ActivityOutput") ?? [];
  const inputSnapshots = inspection?.valueSnapshots.filter(snapshot => snapshot.subject === "ActivityInput") ?? [];
  const firstIncident = inspection?.incidents.find(incident => incident.message.trim());

  return (
    <RuntimeValueEvidenceResolutionProvider scope={resolutionScope}>
      <section className={`ad-historical-contract ad-test-run-evidence is-${stage}`} aria-label="Test run result">
        <div className="ad-test-run-result-heading">
          <span className="ad-test-run-stage" role="status" aria-live="polite"><StatusChip>{dispatchPhase === "cancelling" ? "Cancellation requested" : copy.label}</StatusChip></span>
          {stage !== "completed" && stage !== "faulted" && !view.failure ? <p>{copy.description}</p> : null}
        </div>

        {stage === "faulted" ? (
          <div className="ad-inline-error ad-test-run-failure is-runtime" role="alert">
            <strong>Activity Definition failed</strong>
            <p>{view.failure?.message ?? firstIncident?.message ?? (inspectionStatus === "loading" ? "Loading failure details…" : "Failure details are unavailable. View run details or open the full run data.")}</p>
          </div>
        ) : view.failure ? (
          <div className={`ad-inline-error ad-test-run-failure is-${view.failure.kind === "Validation" ? "validation" : "runtime"}`} role="alert">
            <strong>{view.failure.code === "activity.test-run.required-input" ? "Test Run not started" : view.failure.kind === "Validation" ? "Test Run preparation failed" : "Dispatch rejected"}</strong>
            <p>{view.failure.message}</p>
            {stage === "validation-rejected" ? <p>{view.failure.code === "activity.test-run.required-input"
              ? "No Runtime execution was started."
              : "No Runtime execution was started. Review the draft, then try again."}</p> : null}
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

        {stage === "completed" ? (
          <section className="ad-test-run-result" aria-labelledby={`${resultTitleId}-summary`}>
            <h3 id={`${resultTitleId}-summary`}>Result</h3>
            <section className="ad-test-run-outputs" aria-label="Outputs">
              <h4>Outputs</h4>
              {inspectionStatus === "loading" ? <p role="status">Loading output evidence…</p>
                : inspectionStatus !== "ready" ? <p>Output evidence is unavailable.</p>
                  : outputSnapshots.length === 0 ? <p>No output snapshots were recorded for this execution.</p>
                    : <div className="ad-test-run-output-list" role="list">
                      {outputSnapshots.map((snapshot, index) => <RuntimeValueEvidenceCard
                        key={`${snapshot.name}:${index}`}
                        snapshot={snapshot}
                        compactObjects
                        showCaptureMode={false}
                      />)}
                    </div>}
            </section>
            <section className="ad-test-run-outcome" aria-label="Outcome">
              <h4>Outcome</h4>
              {inspectionStatus === "loading" ? <p role="status">Loading outcome…</p>
                : inspectionStatus !== "ready" ? <p>Outcome is unavailable.</p>
                  : inspection?.outcomeNames.length ? <p>{inspection.outcomeNames.join(", ")}</p>
                    : <p>No outcome was recorded.</p>}
            </section>
          </section>
        ) : null}

        <details className="ad-test-run-details" onToggle={event => setDetailsOpen(event.currentTarget.open)}>
          <summary>View run details</summary>
          {detailsOpen ? (
            <div className="ad-test-run-details-content">
              <p className="ad-selected-record"><strong>{definitionLabel}</strong> · {sourceVersion ? `published version ${sourceVersion.version}` : `draft revision ${view.draftRevision}`}</p>
              {stage !== "validation-rejected" && stage !== "dispatch-rejected" ? (
                <section className="ad-test-run-recorded-inputs" aria-label="Recorded inputs">
                  <h4>Recorded inputs</h4>
                  {inspectionStatus === "loading" ? <p role="status">Loading recorded inputs…</p>
                    : inspectionStatus !== "ready" ? <p>Recorded inputs are unavailable.</p>
                      : inputSnapshots.length === 0 ? <p>No input snapshots were recorded for this execution.</p>
                        : <div className="ad-test-run-input-list" role="list">
                          {inputSnapshots.map((snapshot, index) => <RuntimeValueEvidenceCard
                            key={`${snapshot.name}:${index}`}
                            snapshot={snapshot}
                            compactObjects
                            showCaptureMode={false}
                          />)}
                        </div>}
                </section>
              ) : null}
              <dl className="ad-review-facts ad-test-run-technical">
                {sourceVersion ? <div><dt>Published version</dt><dd>{sourceVersion.version}</dd></div> : <div><dt>Draft revision</dt><dd>{view.draftRevision}</dd></div>}
                <div><dt>Requested</dt><dd>{formatTime(view.requestedAt)}</dd></div>
                <div><dt>Updated</dt><dd>{formatTime(view.updatedAt)}</dd></div>
                {inspection?.startedAt ? <div><dt>Started</dt><dd>{formatTime(inspection.startedAt)}</dd></div> : null}
                {inspection?.completedAt ? <div><dt>Completed</dt><dd>{formatTime(inspection.completedAt)}</dd></div> : null}
                <ActivityTestRunTechnicalIdentity label="Test Run" value={view.testRunId} copiedLabel="Test Run ID" onCopied={markCopied} onCopyFailed={markCopyFailed} />
                <ActivityTestRunTechnicalIdentity label={activityExecutionId ? "Workflow execution" : "Reserved workflow execution"} value={view.workflowExecutionId} href={activityExecutionId ? activityTestRunWorkflowUrl(view) : null} onOpenRun={onOpenRun} copiedLabel="workflow execution ID" onCopied={markCopied} onCopyFailed={markCopyFailed} />
                {view.failure?.code ? <div><dt>Failure code</dt><dd><code>{safeCode(view.failure.code)}</code></dd></div> : null}
                {view.outerActivityExecutionId ? <ActivityTestRunTechnicalIdentity label="Outer activity execution" value={view.outerActivityExecutionId} href={activityTestRunActivityUrl(view)} onOpenRun={onOpenRun} copiedLabel="outer activity execution ID" onCopied={markCopied} onCopyFailed={markCopyFailed} /> : null}
                {view.artifactId ? <ActivityTestRunTechnicalIdentity label="Wrapper executable" value={view.artifactId} href={executableUrl} onOpenRun={onOpenRun} copiedLabel="wrapper artifact ID" onCopied={markCopied} onCopyFailed={markCopyFailed} /> : null}
                {view.sourceReferenceId ? <ActivityTestRunTechnicalIdentity label="Source Reference" value={view.sourceReferenceId} href={executableUrl} onOpenRun={onOpenRun} copiedLabel="Source Reference ID" onCopied={markCopied} onCopyFailed={markCopyFailed} /> : null}
                {view.commandDispatchStatus ? <div><dt>Command dispatch</dt><dd>{view.commandDispatchStatus}</dd></div> : null}
              </dl>
              {copyStatus ? <p className="wf-copy-status" role="status" aria-live="polite">{copyStatus}</p> : null}
              {activityUrl ? <StudioButton className="ad-test-run-open-evidence" onClick={() => onOpenRun(activityUrl)}>
                <ExternalLink size={16} aria-hidden /> Open focused run data
              </StudioButton> : null}
              {view.sourceReferenceId ? <ExpirationEvidence view={view} /> : null}
              {history.length > 1 ? (
                <section className="ad-test-run-history-section" aria-label="Test Run rerun history">
                  <h4>Rerun history ({history.length})</h4>
                  <ol className="ad-test-run-history">
                    {history.map(item => <li key={item.testRunId}>
                      <code title={item.testRunId}>{shortenActivityTestRunIdentifier(item.testRunId)}</code>
                      <CopyValueButton scope={`activity-test-run-history:${item.testRunId}`} value={item.testRunId} ariaLabel="Copy historical Test Run ID" copiedLabel="historical Test Run ID" onCopied={markCopied} onCopyFailed={markCopyFailed} />
                      <span>revision {item.draftRevision} · {item.status}</span>
                    </li>)}
                  </ol>
                </section>
              ) : null}
            </div>
          ) : null}
        </details>
      </section>
    </RuntimeValueEvidenceResolutionProvider>
  );
}

function ActivityTestRunTechnicalIdentity({
  label,
  value,
  href,
  onOpenRun,
  copiedLabel,
  onCopied,
  onCopyFailed
}: {
  label: string;
  value: string;
  href?: string | null;
  onOpenRun?(path: string): void;
  copiedLabel: string;
  onCopied(label: string): void;
  onCopyFailed(label: string): void;
}) {
  const identity = <code title={value}>{shortenActivityTestRunIdentifier(value)}</code>;
  return (
    <div>
      <dt>{label}</dt>
      <dd className="ad-test-run-identity">
        {href && onOpenRun ? <StudioLink href={href} aria-label={`Open ${label}`} onClick={event => {
          if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
          event.preventDefault();
          onOpenRun(href);
        }}>{identity}</StudioLink> : identity}
        <CopyValueButton
          scope={`activity-test-run:${label}`}
          value={value}
          ariaLabel={`Copy ${copiedLabel}`}
          copiedLabel={copiedLabel}
          onCopied={onCopied}
          onCopyFailed={onCopyFailed}
        />
      </dd>
    </div>
  );
}

function ExpirationEvidence({ view }: { view: ActivityDraftTestRunView }) {
  useStudioTimeZone();
  const expiration = view.expiration;
  if (!expiration.sourceReferenceExpired) {
    return <p className="ad-test-run-expiry">Source Reference expires {formatTime(expiration.sourceReferenceExpiresAt)}. The durable receipt expires {formatTime(expiration.receiptExpiresAt)}.</p>;
  }
  if (expiration.runStillActive) {
    return <p className="ad-test-run-expiry"><strong>Run still active.</strong> The Source Reference lifetime elapsed, but Runtime continues this already-started execution and retains its run data.</p>;
  }
  if (expiration.evidenceRetained) {
    return <p className="ad-test-run-expiry"><strong>Source Reference expired.</strong> Run data is retained independently and remains available.</p>;
  }
  return <p className="ad-test-run-expiry"><strong>Test Run expired.</strong> Neither its Source Reference nor run data remains available.</p>;
}

function stageCopy(stage: ActivityTestRunStage) {
  switch (stage) {
    case "accepted": return { label: "Dispatch accepted", description: "Waiting for scheduling details." };
    case "waiting": return { label: "Waiting for scheduling", description: "Waiting for the Activity Definition to start." };
    case "running": return { label: "Activity Definition is running", description: "Waiting for the result." };
    case "suspended": return { label: "Activity Definition is suspended", description: "Waiting for the Activity Definition to resume." };
    case "completed": return { label: "Completed", description: "" };
    case "faulted": return { label: "Activity Definition faulted", description: "" };
    case "cancelled": return { label: "Cancelled", description: "The Activity Definition was cancelled." };
    case "validation-rejected": return { label: "Draft validation rejected", description: "The saved revision was rejected before dispatch." };
    case "dispatch-rejected": return { label: "Runtime rejected dispatch", description: "" };
    case "dispatch-ambiguous": return { label: "Reconciling", description: "Checking the dispatch receipt; no duplicate run will be started." };
    case "expired": return { label: "Test Run data expired", description: "" };
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
  return Number.isNaN(parsed.valueOf()) ? "at the server-defined retention boundary" : formatStudioDate(parsed);
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
