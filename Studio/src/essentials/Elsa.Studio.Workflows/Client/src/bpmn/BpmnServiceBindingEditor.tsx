import { useCallback, useEffect, useRef, useState } from "react";
import { qualifyStudioBackendPath, type StudioBackendSession } from "@elsa-workflows/studio-sdk";
import { capabilityIds, hasCapabilityLink } from "../api/capabilities";
import { listPublicationSlots, type PublicationSlot } from "../api/publishing";
import { getExecutable } from "../api/runtime";
import { RunWorkflowPicker } from "../workflow-editor/RunWorkflowPicker";
import { directServiceBinding, directServicePatch } from "./bpmnDirectBinding";
import type { BpmnElement } from "./bpmnTypes";

interface Props {
  session: StudioBackendSession;
  documentId: string;
  element: BpmnElement;
  readOnly?: boolean;
  onChange(patch: Partial<BpmnElement>): void;
}

function sessionScope(session: StudioBackendSession) {
  return JSON.stringify([session.key, session.profile.id, session.profile.baseUrl, session.context.baseUrl,
    session.identity.status, session.identity.subject, session.identity.tenantId]);
}

/** Uses the ordinary catalog/publication flow. Selecting a worker never creates or publishes one. */
export function BpmnServiceBindingEditor(props: Props) {
  return <ServiceBindingForm key={JSON.stringify([sessionScope(props.session), props.documentId, props.element])} {...props} />;
}

function ServiceBindingForm({ session, element, readOnly = false, onChange }: Props) {
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
    generation.current++; setSlots([]); setSlotId(""); setDefinitionId(id);
  };
  const applyPublication = async () => {
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
      const patch = directServicePatch(element, {
        formatVersion: 1, kind: "nativeService", sourceReferenceId: reference.sourceReferenceId,
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
    {readOnly ? <p className="wf-muted">This saved binding is read-only.</p> : <>
      {support === "available" ? <fieldset disabled={busy}>
        <legend>Choose a published service workflow</legend>
        <RunWorkflowPicker context={session.context} definitionId={definitionId} onChange={chooseDefinition} />
        {loading ? <p role="status">Loading published versions…</p> : null}
        {definitionId && !loading && slots.length === 0 ? <p>No active publication is available. Publish this service workflow, then select it again.</p> : null}
        <label className="wf-form-field"><span>Publication</span><select aria-label="Service publication" value={slotId} disabled={loading || slots.length === 0}
          onChange={event => setSlotId(event.target.value)}><option value="">Choose a publication</option>
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
        <p className="wf-muted">The workflow must supply its own inputs. Publication validates its closed input contract. Retrying may repeat external effects.</p>
        <button type="button" disabled={!slotId || busy || !businessErrorCode.trim() || !Number.isInteger(maxAttempts) || maxAttempts < 1 || maxAttempts > 32} onClick={() => void applyPublication()}>
          {busy ? "Reading publication…" : "Use this service publication"}
        </button>
      </fieldset> : <p role="status">{support === "loading" ? "Checking service selection support…" : "This backend does not advertise authored service publication and its required paged workflow and executable reads."}</p>}
      <p><a href={qualifyStudioBackendPath("/workflows/definitions?homeAction=create")} target="_blank" rel="noopener noreferrer">Create a service workflow</a></p>
      <p className="wf-muted">For ETL, choose RunEtl as the new workflow’s root, select a published pipeline, then save, review and publish. Return here to select its publication.</p>
    </>}
    {error ? <p role="alert">{error}</p> : null}
  </section>;
}
