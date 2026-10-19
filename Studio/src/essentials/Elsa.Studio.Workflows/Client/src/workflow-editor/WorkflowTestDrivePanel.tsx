import "../workflowTestDrive.css";
import { Suspense, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import type {
  StudioEndpointContext,
  StudioWorkflowRunInputEditorContribution,
  StudioTestStimulusEditorContribution
} from "@elsa-workflows/studio-sdk";
import type {
  WorkflowDraft,
  WorkflowExecutionInputs,
  WorkflowInput,
  WorkflowTestRunInputContract,
  WorkflowTestRunOption,
  WorkflowTestRunStart,
  WorkflowTestRunView
} from "../workflowTypes";
import { getWorkflowDraftTestRunOptions, getWorkflowTestRun } from "../api/publishing";
import { ApiCapabilityUnavailableError, ApiCapabilityVersionMismatchError, capabilityIds, invalidateApiCapabilityCache } from "../api/capabilities";
import { readWorkflowInputs } from "../workflowReferenceAuthoring";
import { readArgumentType } from "../workflowProperties";
import { createDraftSnapshotId, getDraftSignature } from "./editorHelpers";
import { WorkflowRunInputField } from "./WorkflowRunInputDialog";
import { parseWorkflowRunInputs, type WorkflowRunInputDrafts } from "../workflowRunInputs";
import type { WorkflowTestDriveSetupState } from "./editorTypes";

type TestDriveSnapshot = { draft: WorkflowDraft; signature: string; snapshotId: string };
const testRunOptionsRelation = "workflow-draft-test-run-options";
const testRunSetupUnavailableMessage = "Test Run setup is unavailable on this backend. Use compatible Studio and backend versions, restart it, then select Refresh setup.";

export function WorkflowTestDrivePanel({
  context,
  draft,
  runInputEditors,
  stimulusEditors,
  setupState,
  onSetupStateChange,
  busy,
  onStart,
  onUpdateRun
}: {
  context: StudioEndpointContext;
  draft: WorkflowDraft;
  runInputEditors: StudioWorkflowRunInputEditorContribution[];
  stimulusEditors: StudioTestStimulusEditorContribution[];
  setupState: WorkflowTestDriveSetupState | null;
  onSetupStateChange(state: WorkflowTestDriveSetupState): void;
  busy: boolean;
  onStart(snapshot: WorkflowDraft, inputs: WorkflowExecutionInputs, start: WorkflowTestRunStart, label: string, inputNames: string[], onStarted?: (view: WorkflowTestRunView) => Promise<void>): Promise<WorkflowTestRunView | null>;
  onUpdateRun(signature: string, view: WorkflowTestRunView, startLabel: string, inputNames: string[], actionResult?: { contributionId: string; value: { kind: string; [key: string]: unknown } }, actionError?: string): void;
}) {
  const [snapshot, setSnapshot] = useState<TestDriveSnapshot>(() => setupState?.snapshot ?? capture(draft));
  const [options, setOptions] = useState<WorkflowTestRunOption[]>([]);
  const [inputContract, setInputContract] = useState<WorkflowTestRunInputContract | null>(null);
  const [optionDiagnostics, setOptionDiagnostics] = useState<{ code: string; message: string; nodeId?: string | null }[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState("");
  const [testRunSetupUnavailable, setTestRunSetupUnavailable] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const [selectedOptionId, setSelectedOptionId] = useState(() => setupState?.selectedOptionId ?? "");
  const [selectedModeId, setSelectedModeId] = useState(() => setupState?.selectedModeId ?? "");
  const selectedOptionIdRef = useRef(selectedOptionId);
  const selectedModeIdRef = useRef(selectedModeId);
  selectedOptionIdRef.current = selectedOptionId;
  selectedModeIdRef.current = selectedModeId;
  const [inputDrafts, setInputDrafts] = useState<WorkflowRunInputDrafts>(() => setupState?.inputDrafts ?? {});
  const [stimulusDraft, setStimulusDraft] = useState(() => setupState?.stimulusDraft ?? "{}");
  const [inputErrors, setInputErrors] = useState<Record<string, string>>({});
  const [stimulusError, setStimulusError] = useState("");
  const [actionError, setActionError] = useState("");

  const currentSignature = getDraftSignature(draft);
  const isStale = snapshot.signature !== currentSignature;
  const workflowInputs = useMemo(() => readWorkflowInputsForTestRun(snapshot, inputContract), [inputContract, snapshot]);
  const selectedOption = options.find(option => option.optionId === selectedOptionId) ?? null;
  const selectedMode = selectedOption?.modes.find(mode => mode.modeId === selectedModeId) ?? null;
  const editor = useMemo(() => {
    if (!selectedOption || !selectedMode) return undefined;
    for (const candidate of [...stimulusEditors].sort((left, right) => (left.order ?? 500) - (right.order ?? 500))) {
      try {
        if (candidate.supports({ option: selectedOption, mode: selectedMode })) return candidate;
      } catch (error) {
        console.error(`[workflow.test-stimulus.editors] ${candidate.id} failed while matching ${selectedOption.driverId}/${selectedMode.modeId}.`, error);
      }
    }
    return undefined;
  }, [selectedMode, selectedOption, stimulusEditors]);
  const editorContext = selectedOption && selectedMode ? { option: selectedOption, mode: selectedMode, draft: stimulusDraft } : null;

  useEffect(() => {
    onSetupStateChange({ snapshot, selectedOptionId, selectedModeId, inputDrafts, stimulusDraft });
  }, [inputDrafts, onSetupStateChange, selectedModeId, selectedOptionId, snapshot, stimulusDraft]);

  useEffect(() => {
    let current = true;
    setLoading(true);
    setLoadError("");
    setTestRunSetupUnavailable(false);
    getWorkflowDraftTestRunOptions(context, {
      definitionId: snapshot.draft.definitionId,
      snapshotId: snapshot.snapshotId,
      state: snapshot.draft.state
    }).then(result => {
      if (!current) return;
      const nextOptions = Array.isArray(result.options) ? result.options : [];
      setOptions(nextOptions);
      setInputContract(result.inputContract ?? null);
      setOptionDiagnostics(Array.isArray(result.diagnostics) ? result.diagnostics : []);
      const retainedOption = nextOptions.find(option => option.optionId === selectedOptionIdRef.current);
      if (!retainedOption) {
        setSelectedOptionId("");
        setSelectedModeId("");
        setStimulusDraft("{}");
      } else if (!retainedOption.modes.some(mode => mode.modeId === selectedModeIdRef.current)) {
        setSelectedModeId(retainedOption.modes.find(mode => mode.isDefault)?.modeId ?? retainedOption.modes[0]?.modeId ?? "");
        setStimulusDraft("{}");
      }
      setInputErrors({});
      setStimulusError("");
      setActionError("");
    }).catch(error => {
      if (!current) return;
      setOptions([]);
      setInputContract(null);
      setOptionDiagnostics([]);
      const unsupportedTestRunSetup = (error instanceof ApiCapabilityUnavailableError
        && error.capabilityId === capabilityIds.publishing
        && (!error.relation || error.relation === testRunOptionsRelation))
        || (error instanceof ApiCapabilityVersionMismatchError && error.capabilityId === capabilityIds.publishing);
      setTestRunSetupUnavailable(unsupportedTestRunSetup);
      setLoadError(unsupportedTestRunSetup
        ? testRunSetupUnavailableMessage
        : error instanceof Error ? error.message : "Start options could not be loaded.");
    }).finally(() => current && setLoading(false));
    return () => { current = false; };
  }, [context, reloadKey, snapshot]);

  const refresh = () => {
    setLoading(true);
    if (testRunSetupUnavailable) invalidateApiCapabilityCache(context);
    setSnapshot(capture(draft));
    setReloadKey(value => value + 1);
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setActionError("");
    if (loading || loadError) return;
    if (isStale) {
      setLoadError("The draft changed. Refresh the setup to use its latest snapshot.");
      return;
    }
    if (selectedOption?.isAvailable === false) return;
    if (selectedOption && selectedMode && !editor && !selectedMode.configurationSchema) {
      setStimulusError("No installed module editor is available, and this driver does not declare a JSON Schema for trigger data.");
      return;
    }
    const parsedInputs = parseWorkflowRunInputs(workflowInputs, inputDrafts, runInputEditors);
    setInputErrors(parsedInputs.errors);
    if (Object.keys(parsedInputs.errors).length) return;

    let start: WorkflowTestRunStart = { kind: "direct" };
    let label = "Direct start";
    let data: unknown;
    if (selectedOption && selectedMode && editorContext) {
      let validationError: string | undefined;
      try { validationError = editor?.validate(editorContext); }
      catch (error) { validationError = error instanceof Error ? error.message : "The selected trigger editor could not validate its data."; }
      if (validationError) {
        setStimulusError(validationError);
        return;
      }
      try {
        if (editor) data = editor.serialize(editorContext);
        else data = parseJsonDraft(stimulusDraft);
      } catch (error) {
        setStimulusError(error instanceof Error ? error.message : "Enter valid JSON for the trigger data.");
        return;
      }
      setStimulusError("");
      label = `${selectedOption.label} · ${selectedMode.label}`;
      start = { kind: "trigger", optionId: selectedOption.optionId, modeId: selectedMode.modeId, ...(data !== undefined ? { data } : {}) };
    }

    await onStart(snapshot.draft, parsedInputs.values, start, label, workflowInputs.map(input => input.name), async view => {
      if (!view.driverAction || !selectedOption || !selectedMode) return;
      let nextView = view;
      let actionResult: { contributionId: string; value: { kind: string; [key: string]: unknown } } | undefined;
      let actionFailure: string | undefined;
      if (!editor?.executeAction) {
        actionFailure = "No installed module editor can send the prepared action.";
        setActionError(actionFailure);
      } else {
        try {
          const result = await editor.executeAction({
            backend: context,
            option: selectedOption,
            mode: selectedMode,
            action: view.driverAction,
            data,
            editorDraft: stimulusDraft
          });
          actionResult = { contributionId: editor.id, value: result };
        } catch {
          actionFailure = "The trigger action could not be sent. The Test Run preparation remains available.";
          setActionError(actionFailure);
        }
      }
      try {
        nextView = await getWorkflowTestRun(context, view.testRunId);
      } catch {
        actionFailure ??= "The request was sent, but the Test Run status could not be refreshed.";
        setActionError(current => current || actionFailure!);
      }
      onUpdateRun(snapshot.signature, nextView, label, workflowInputs.map(input => input.name), actionResult, actionFailure);
    });
  };

  const triggerUnavailable = Boolean(selectedOption && (
    selectedOption.isAvailable === false
    || !selectedMode
    || (!editor && !selectedMode.configurationSchema)
  ));
  const triggerUnavailableReason = selectedOption?.isAvailable === false
    ? selectedOption.reason || "This start option is unavailable."
    : selectedOption && selectedMode && !editor && !selectedMode.configurationSchema
      ? "No installed module editor is available, and this driver does not declare a JSON Schema for trigger data."
      : selectedOption && !selectedMode
        ? "This start option has no available modes."
        : "";

  return (
    <div className="wf-test-drive-panel">
      <div className="wf-test-drive-heading">
        <div>
          <h3>Test Run setup</h3>
          <p>Test the captured draft with workflow inputs and one selected start.</p>
        </div>
        <button type="button" onClick={refresh} disabled={busy || loading}>Refresh setup</button>
      </div>
      {isStale ? <div className="wf-test-drive-stale" role="status">Draft changed · Refresh setup to capture the latest version.</div> : null}
      <p className="wf-test-drive-environment">Test Run uses the connected environment. Workflow activities can still produce real side effects.</p>
      {loadError ? <div className="wf-test-drive-error" role="alert">{loadError}</div> : null}
      {optionDiagnostics.length ? <ul className="wf-test-drive-diagnostics" aria-label="Start diagnostics">{optionDiagnostics.map((diagnostic, index) => <li key={`${diagnostic.code}-${diagnostic.nodeId ?? index}`}><strong>{diagnostic.code}</strong> {diagnostic.message}{diagnostic.nodeId ? <span> · Node {diagnostic.nodeId}</span> : null}</li>)}</ul> : null}
      {options.some(option => option.isAvailable === false) ? <ul className="wf-test-drive-diagnostics" aria-label="Unavailable starts">{options.filter(option => option.isAvailable === false).map(option => <li key={option.optionId}><strong>{option.label}</strong>{option.nodeId ? ` · Node ${option.nodeId}` : ""}{option.reason ? ` · ${option.reason}` : " · Unavailable"}</li>)}</ul> : null}
      {loading ? <p role="status">Loading available starts…</p> : null}
      <Suspense fallback={<p className="wf-test-drive-loading" role="status">Loading Test Drive editor…</p>}>
      <form onSubmit={event => { void submit(event); }}>
        <label className="wf-form-field">
          <span>Start</span>
          <select value={selectedOptionId ? `trigger:${selectedOptionId}` : "direct"} disabled={busy || loading || isStale} onChange={event => {
            const value = event.currentTarget.value;
            if (value === "direct") {
              setSelectedOptionId("");
              setSelectedModeId("");
        setStimulusDraft("{}");
              setStimulusError("");
            } else {
              const next = value.slice("trigger:".length);
              const option = options.find(candidate => candidate.optionId === next);
              const mode = option?.modes.find(candidate => candidate.isDefault) ?? option?.modes[0];
              setSelectedOptionId(next);
              setSelectedModeId(mode?.modeId ?? "");
        setStimulusDraft("{}");
        setStimulusError("");
            }
          }}>
            <option value="direct">Direct start</option>
            {options.map(option => <option key={option.optionId} value={`trigger:${option.optionId}`} disabled={option.isAvailable === false}>{option.label}{option.nodeId ? ` · ${option.nodeId}` : ""}{option.isAvailable === false ? " · unavailable" : ""}</option>)}
          </select>
        </label>
        {selectedOption && selectedOption.modes.length > 1 ? (
          <label className="wf-form-field">
            <span>Mode</span>
            <select value={selectedModeId} disabled={busy || isStale} onChange={event => {
              setSelectedModeId(event.currentTarget.value);
              setStimulusDraft("{}");
              setStimulusError("");
            }}>
              {selectedOption.modes.map(mode => <option key={mode.modeId} value={mode.modeId}>{mode.label}</option>)}
            </select>
          </label>
        ) : null}
        {selectedOption?.reason && selectedOption.isAvailable === false ? <div className="wf-test-drive-error" role="status">{selectedOption.reason}{selectedOption.nodeId ? ` · Node ${selectedOption.nodeId}` : ""}</div> : null}
        {triggerUnavailableReason && selectedOption?.isAvailable !== false ? <div className="wf-test-drive-error" role="status">{triggerUnavailableReason}</div> : null}
        {selectedMode?.description ? <p className="wf-test-drive-description">{selectedMode.description}</p> : null}
        {selectedOption && selectedMode ? (
          <div className="wf-test-drive-stimulus">
            {editor && editorContext ? (
              <editor.component
                option={selectedOption}
                mode={selectedMode}
                draft={stimulusDraft}
                disabled={busy || isStale}
                controlProps={{ id: "workflow-test-stimulus", "aria-label": `${selectedMode.label} data`, "aria-invalid": Boolean(stimulusError) }}
                onChange={value => { setStimulusDraft(value); setStimulusError(""); }}
              />
            ) : selectedMode.configurationSchema ? (
              <label className="wf-form-field" htmlFor="workflow-test-stimulus">
                <span>Trigger data</span>
                <textarea id="workflow-test-stimulus" aria-label="Trigger data" aria-invalid={Boolean(stimulusError)} rows={5} value={stimulusDraft} disabled={busy || isStale} onChange={event => setStimulusDraft(event.currentTarget.value)} />
                <small>Enter JSON matching the trigger’s declared schema.</small>
              </label>
            ) : <p className="wf-test-drive-description">This trigger does not require data.</p>}
            {stimulusError ? <small className="wf-test-drive-error" role="alert">{stimulusError}</small> : null}
          </div>
        ) : null}
        {workflowInputs.length ? (
          <fieldset className="wf-test-drive-inputs" disabled={busy || isStale}>
            <legend>Workflow inputs</legend>
            <div className="wf-run-input-list">
              {workflowInputs.map(input => (
                <WorkflowRunInputField
                  key={input.referenceKey}
                  input={input}
                  editors={runInputEditors}
                  value={inputDrafts[input.referenceKey] ?? ""}
                  error={inputErrors[input.referenceKey]}
                  disabled={busy || isStale}
                  onEditorFailure={() => setInputErrors(current => ({ ...current, [input.referenceKey]: "The input editor failed. Enter a standard value instead." }))}
                  onChange={value => setInputDrafts(current => ({ ...current, [input.referenceKey]: value }))}
                />
              ))}
            </div>
          </fieldset>
        ) : null}
        {actionError ? <div className="wf-test-drive-error" role="alert">{actionError}</div> : null}
        <div className="wf-test-drive-actions">
          <button type="submit" disabled={busy || loading || isStale || Boolean(loadError) || triggerUnavailable || !snapshot.draft.state.rootActivity}>{busy ? "Starting…" : editor?.startLabel ?? "Run workflow"}</button>
        </div>
      </form>
      </Suspense>
    </div>
  );
}

function capture(draft: WorkflowDraft): TestDriveSnapshot {
  const copy = structuredClone(draft);
  return { draft: copy, signature: getDraftSignature(copy), snapshotId: createDraftSnapshotId(copy) };
}

function readWorkflowInputsForTestRun(snapshot: TestDriveSnapshot, contract: WorkflowTestRunInputContract | null): WorkflowInput[] {
  const authoredInputs = readWorkflowInputs(snapshot.draft.state.inputs);
  if (!contract || !Array.isArray(contract.inputs)) return authoredInputs;
  const authoredByName = new Map(authoredInputs.map(input => [input.name, input]));
  return contract.inputs.filter(input => typeof input.name === "string" && input.type && typeof input.type.alias === "string")
    .map(input => {
      const authored = authoredByName.get(input.name);
      return {
        ...authored,
        referenceKey: authored?.referenceKey ?? input.name,
        name: input.name,
        displayName: authored?.displayName || input.name,
        description: authored?.description ?? "",
        type: readArgumentType({ type: input.type }),
        isRequired: input.isRequired && (input.defaultValue === undefined || input.defaultValue === null),
        isNullable: authored?.isNullable ?? false,
        category: authored?.category ?? "",
        uiHint: authored?.uiHint ?? "",
        storageDriverType: authored?.storageDriverType ?? null
      } as WorkflowInput;
    });
}

function parseJsonDraft(value: string) {
  if (!value.trim()) return undefined;
  return JSON.parse(value);
}
