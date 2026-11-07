import { useCallback, useEffect, useRef, useState } from "react";
import { qualifyStudioBackendPath, type StudioBackendSession } from "@elsa-workflows/studio-sdk";
import { capabilityIds, hasCapabilityLink } from "../api/capabilities";
import { listPublicationSlots, type PublicationSlot } from "../api/publishing";
import { getExecutable } from "../api/runtime";
import { RunWorkflowPicker } from "../workflow-editor/RunWorkflowPicker";
import { directServiceBinding, directServicePatch, type DirectWorkerInput } from "./bpmnDirectBinding";
import type { BpmnElement } from "./bpmnTypes";
import type { WorkflowExecutableDetails, WorkflowInput, WorkflowTestRunInputContract } from "../workflowTypes";
import { isArgumentTypeCompatible } from "../workflowReferenceAuthoring";
import { parseLiteralJson, serializeTriggerPayload } from "../standalone-triggers/json";

interface Props {
  session: StudioBackendSession;
  documentId: string;
  element: BpmnElement;
  readOnly?: boolean;
  onChange(patch: Partial<BpmnElement>): void;
  modelInputs?: WorkflowInput[];
}

function sessionScope(session: StudioBackendSession) {
  return JSON.stringify([session.key, session.profile.id, session.profile.baseUrl, session.context.baseUrl,
    session.identity.status, session.identity.subject, session.identity.tenantId]);
}

/** Uses the ordinary catalog/publication flow. Selecting a worker never creates or publishes one. */
export function BpmnServiceBindingEditor(props: Props) {
  return <ServiceBindingForm key={JSON.stringify([sessionScope(props.session), props.documentId, props.element, props.modelInputs])} {...props} />;
}

interface WorkerInputDraft { kind: "Omit" | "Input" | "Literal"; name: string; literal: string }
function savedInputDrafts(inputs: Record<string, DirectWorkerInput> | undefined): Record<string, WorkerInputDraft> { return Object.fromEntries(Object.entries(inputs ?? {}).map(([name, source]) => [name, source.kind === "Input" ? { kind: "Input", name: source.name, literal: "" } : { kind: "Literal", name: "", literal: serializeTriggerPayload(source.value) }])); }
function ServiceBindingForm({ session, element, readOnly = false, onChange, modelInputs = [] }: Props) {
  const current = directServiceBinding(element);
  const scope = sessionScope(session);
  const context = session.context;
  const [definitionId, setDefinitionId] = useState(current?.executable.definitionId ?? "");
  const [slots, setSlots] = useState<PublicationSlot[]>([]);
  const [slotId, setSlotId] = useState("");
  const [support, setSupport] = useState<"loading" | "available" | "unavailable">("loading");
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [disposition, setDisposition] = useState<"FinalFault" | "HoldForRetry">(current?.faultPolicy.disposition ?? "HoldForRetry");
  const [attempts, setAttempts] = useState(current?.faultPolicy.maxAttempts ?? 3);
  const [businessErrorCode, setBusinessErrorCode] = useState(current?.businessErrorCode ?? "service-failure");
  const [mappingContract, setMappingContract] = useState<WorkflowTestRunInputContract>();
  const [inputDrafts, setInputDrafts] = useState(() => savedInputDrafts(current?.formatVersion === 2 ? current.inputs : undefined));
  const [inputErrors, setInputErrors] = useState<Record<string, string>>({});
  const mappingHeading = useRef<HTMLHeadingElement>(null);
  const publicationButton = useRef<HTMLButtonElement>(null);
  useEffect(() => { if (mappingContract) mappingHeading.current?.focus(); }, [mappingContract]);
  const generation = useRef(0);
  const mounted = useRef(true);
  const retireReads = useCallback(() => { generation.current++; }, []);
  const live = useRef({ readOnly }); live.current = { readOnly };
  const maxAttempts = disposition === "FinalFault" ? 1 : attempts;
  const active = useCallback(() => mounted.current && !session.disposed && session.context === context && sessionScope(session) === scope && !live.current.readOnly, [session, scope, context]);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; retireReads(); }; }, [retireReads]);

  useEffect(() => {
    let retired = false;
    if (readOnly) return;
    void Promise.all([
      hasCapabilityLink(session.context, capabilityIds.workflowDesign, "workflow-definitions-page"),
      hasCapabilityLink(session.context, capabilityIds.runtime, "workflow-executable"),
      hasCapabilityLink(session.context, capabilityIds.bpmnPublishing, "bpmn-authored-service-publish")
    ]).then(values => {
      if (!retired && active()) setSupport(values.every(Boolean) ? "available" : "unavailable");
    }, failure => { if (!retired && active()) { setSupport("unavailable"); setError(String(failure)); } });
    return () => { retired = true; };
  }, [session, readOnly, active]); // Identity/element changes remount this form before any response can be applied.

  useEffect(() => {
    const request = ++generation.current;
    setSlots([]); setSlotId(""); setError(""); setBusy(false);
    if (!definitionId || support !== "available" || readOnly) { setLoading(false); return; }
    setLoading(true);
    void listPublicationSlots(session.context, definitionId).then(result => {
      if (!active() || request !== generation.current) return;
      if (!result.available) throw new Error(result.reason);
      setSlots(result.slots.filter(slot => slot.definitionId === definitionId && slot.publication?.status === "active"
        && slot.publication.definitionId === definitionId && slot.publication.sourceReferenceId
        && slot.activeActivationId === slot.publication.publicationId && slot.slotName === slot.publication.slotName));
      setLoading(false);
    }).catch(failure => {
      if (active() && request === generation.current) { setError(failure instanceof Error ? failure.message : String(failure)); setLoading(false); }
    });
    return retireReads;
  }, [session, definitionId, support, readOnly, active, retireReads]);

  const chooseDefinition = (id: string) => {
    if (!active() || busy) return;
    generation.current++; setSlots([]); setSlotId(""); setDefinitionId(id); setMappingContract(undefined); setInputDrafts({}); setInputErrors({});
  };
  const applyPublication = async (finishMappings = false) => {
    if (!active() || busy || support !== "available") return;
    const slot = slots.find(value => value.slotId === slotId);
    const publication = slot?.publication;
    if (!publication?.sourceReferenceId || !businessErrorCode.trim() || !Number.isInteger(maxAttempts) || maxAttempts < 1 || maxAttempts > 32) return;
    const request = generation.current;
    setBusy(true); setError("");
    try {
      const executable = await getExecutable(session.context, publication.artifactId, publication.sourceReferenceId);
      if (!active() || request !== generation.current) return;
      const reference = executable.references.find(value => value.sourceReferenceId === publication.sourceReferenceId);
      if (executable.artifactId !== publication.artifactId || !executable.artifactHash
        || publication.artifactHash && executable.artifactHash !== publication.artifactHash
        || executable.chosenReference?.sourceReferenceId !== publication.sourceReferenceId || !reference
        || reference.artifactId !== executable.artifactId || reference.definitionId !== definitionId
        || reference.definitionVersionId !== (publication.definitionVersionId ?? publication.versionId)
        || !reference.artifactVersion || reference.scope.toLowerCase() !== "published" || reference.deletedAt != null
        || reference.expiresAt != null && !(Date.parse(reference.expiresAt) > Date.now()))
        throw new Error("The selected publication no longer has its exact live published reference. Refresh the workflow selection.");
      const contract = (executable as WorkflowExecutableDetails & { inputContract?: WorkflowTestRunInputContract | null }).inputContract;
      if (contract && contract.version !== 1) throw new Error("This service publication uses an unsupported input contract.");
      if (!finishMappings && contract?.inputs.length) { setMappingContract(contract); return; }
      const inputs: Record<string, DirectWorkerInput> = Object.create(null) as Record<string, DirectWorkerInput>;
      if (finishMappings) {
        const errors = Object.create(null) as Record<string, string>;
        if (!contract || serializeTriggerPayload(contract) !== serializeTriggerPayload(mappingContract)) throw new Error("The service input contract changed. Select its publication again.");
        for (const input of contract.inputs) {
          const draft = Object.hasOwn(inputDrafts, input.name) ? inputDrafts[input.name] : undefined;
          if (!draft || draft.kind === "Omit") { if (input.isRequired && input.defaultValue == null) errors[input.name] = "Map this required service input or choose a publication with a default."; continue; }
          if (draft.kind === "Input") {
            const source = modelInputs.find(value => value.name === draft.name);
            if (!source) errors[input.name] = "Choose an input declared by the current process document.";
            else if (!isArgumentTypeCompatible(source.type, input.type.alias, input.type.collectionKind)) errors[input.name] = "The process input type is incompatible with this service input.";
            else inputs[input.name] = { kind: "Input", name: source.name };
          } else { try { inputs[input.name] = { kind: "Literal", value: parseLiteralJson(draft.literal) }; } catch { errors[input.name] = "Enter a valid JSON literal."; } }
        }
        for (const [name, draft] of Object.entries(inputDrafts)) if (draft.kind !== "Omit" && !contract.inputs.some(input => input.name === name)) errors[name] = "This mapping is absent from the selected service contract. Remove it before applying.";
        setInputErrors(errors);
        if (Object.keys(errors).length) { mappingHeading.current?.focus(); return; }
      }
      const patch = directServicePatch(element, {
        ...(Object.keys(inputs).length ? { formatVersion: 2 as const, inputs } : { formatVersion: 1 as const }), kind: "nativeService", sourceReferenceId: reference.sourceReferenceId,
        executable: { artifactId: executable.artifactId, artifactHash: executable.artifactHash, definitionId: reference.definitionId,
          definitionVersionId: reference.definitionVersionId, artifactVersion: reference.artifactVersion },
        businessErrorCode: businessErrorCode.trim(), faultPolicy: { formatVersion: 1, disposition, maxAttempts }
      });
      if (!patch.elementType) throw new Error("The publication does not provide a complete supported service binding.");
      onChange(patch);
    } catch (failure) { if (active() && request === generation.current) setError(failure instanceof Error ? failure.message : String(failure)); }
    finally { if (active() && request === generation.current) setBusy(false); }
  };

  return <section aria-label="Published service workflow">
    {current ? <p>Retained service version {current.executable.artifactVersion}. Later publications do not change this binding.</p> : null}
    {current?.formatVersion === 2 ? <section aria-label="Saved service input mappings"><h4>Saved service inputs</h4><dl>{Object.entries(current.inputs).map(([name, source]) => <div key={name}><dt>{name}</dt><dd>{source.kind === "Input" ? `Process input: ${source.name}` : `Configured JSON: ${serializeTriggerPayload(source.value)}`}</dd></div>)}</dl></section> : null}
    {readOnly ? <p className="wf-muted">This saved binding is read-only.</p> : <>
      {support === "available" ? <fieldset disabled={busy}>
        <legend>Choose a published service workflow</legend>
        <RunWorkflowPicker context={session.context} definitionId={definitionId} onChange={chooseDefinition} />
        {loading ? <p role="status">Loading published versions…</p> : null}
        {definitionId && !loading && slots.length === 0 ? <p>No active publication is available. Publish this service workflow, then select it again.</p> : null}
        <label className="wf-form-field"><span>Publication</span><select aria-label="Service publication" value={slotId} disabled={loading || slots.length === 0}
          onChange={event => { setSlotId(event.target.value); setMappingContract(undefined); setInputErrors({}); }}><option value="">Choose a publication</option>
          {slots.map(slot => <option key={slot.slotId} value={slot.slotId}>{slot.slotName}</option>)}
        </select></label>
        <label className="wf-form-field"><span>Technical failure</span><select aria-label="Service technical failure" value={disposition}
          onChange={event => setDisposition(event.target.value as typeof disposition)}>
          <option value="HoldForRetry">Pause for an explicit retry</option><option value="FinalFault">Fail the process</option>
        </select></label>
        {disposition === "HoldForRetry" ? <label className="wf-form-field"><span>Maximum attempts, including the first</span>
          <input aria-label="Service maximum attempts" type="number" min={1} max={32} value={attempts} onChange={event => setAttempts(Number(event.target.value))} />
        </label> : null}
        <label className="wf-form-field"><span>Business error code</span><input aria-label="Service business error code" value={businessErrorCode} onChange={event => setBusinessErrorCode(event.target.value)} /></label>
        {mappingContract ? <section aria-label="Service input mappings">
          <h4 tabIndex={-1} ref={mappingHeading}>Map service workflow inputs</h4>
          <p>Use declared inputs from this process document or configured JSON values. Publication validates the actual types. Retrying may repeat external effects.</p>
          {mappingContract.inputs.map(input => { const draft = Object.hasOwn(inputDrafts, input.name) ? inputDrafts[input.name] : { kind: "Omit" as const, name: "", literal: "" }; const update = (patch: Partial<WorkerInputDraft>) => setInputDrafts(previous => ({ ...previous, [input.name]: { ...draft, ...patch } })); return <fieldset key={input.name}><legend>{input.name}{input.isRequired ? " · Required" : ""} · {input.type.alias}</legend>
            <label className="wf-form-field"><span>Value source</span><select aria-label={`Service input ${input.name} source`} value={draft.kind} onChange={event => update({ kind: event.target.value as WorkerInputDraft["kind"] })}><option value="Omit">Use service default / omit</option><option value="Input">Declared process input</option><option value="Literal">Configured JSON value</option></select></label>
            {draft.kind === "Input" ? <label className="wf-form-field"><span>Process input</span><select aria-label={`Process input for service ${input.name}`} value={draft.name} onChange={event => update({ name: event.target.value })}><option value="">Choose a declared process input</option>{draft.name && !modelInputs.some(value => value.name === draft.name) ? <option value={draft.name}>{draft.name} · Missing declaration</option> : null}{modelInputs.map(source => <option key={source.referenceKey} value={source.name} disabled={!isArgumentTypeCompatible(source.type, input.type.alias, input.type.collectionKind)}>{source.displayName || source.name} · {source.type.alias}</option>)}</select></label> : draft.kind === "Literal" ? <label className="wf-form-field"><span>JSON value</span><textarea aria-label={`JSON value for service ${input.name}`} value={draft.literal} onChange={event => update({ literal: event.target.value })} /></label> : null}
            {Object.hasOwn(inputErrors, input.name) ? <p role="alert">{inputErrors[input.name]}</p> : null}
          </fieldset>; })}
          {Object.entries(inputDrafts).filter(([name, draft]) => draft.kind !== "Omit" && !mappingContract.inputs.some(input => input.name === name)).map(([name]) => <p key={name}>{name} is absent from this service contract. <button type="button" onClick={() => setInputDrafts(previous => Object.fromEntries(Object.entries(previous).filter(([key]) => key !== name)))}>Remove obsolete mapping {name}</button></p>)}
          <button type="button" disabled={busy} onClick={() => { setMappingContract(undefined); setInputErrors({}); setInputDrafts(savedInputDrafts(current?.formatVersion === 2 ? current.inputs : undefined)); queueMicrotask(() => publicationButton.current?.focus()); }}>Cancel input mapping</button>
          <button type="button" disabled={busy} onClick={() => void applyPublication(true)}>{busy ? "Validating service inputs…" : "Apply service input mapping"}</button>
        </section> : <p className="wf-muted">Select a publication to inspect its declared inputs. Publications without mapped inputs retain their existing binding format. Retrying may repeat external effects.</p>}
        <button ref={publicationButton} type="button" disabled={!!mappingContract || !slotId || busy || !businessErrorCode.trim() || !Number.isInteger(maxAttempts) || maxAttempts < 1 || maxAttempts > 32} onClick={() => void applyPublication()}>
          {busy ? "Reading publication…" : "Use this service publication"}
        </button>
      </fieldset> : <p role="status">{support === "loading" ? "Checking service selection support…" : "This backend does not advertise authored service publication and its required paged workflow and executable reads."}</p>}
      <p><a href={qualifyStudioBackendPath("/workflows/definitions?homeAction=create")} target="_blank" rel="noopener noreferrer">Create a service workflow</a></p>
      <p className="wf-muted">For ETL, create a Sequence workflow, add RunEtl and select a published pipeline, then save, review and publish. Return here to select its publication.</p>
    </>}
    {error ? <p role="alert">{error}</p> : null}
  </section>;
}
