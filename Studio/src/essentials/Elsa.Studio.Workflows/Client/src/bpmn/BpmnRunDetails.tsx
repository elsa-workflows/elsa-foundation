import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { StudioButton, CopyableIdentifier } from "@elsa-workflows/studio-ui";
import { evaluateBackendRequirements, type StudioBackendSession } from "@elsa-workflows/studio-sdk";
import type { BpmnExecution, BpmnIncident, BpmnInspection, createBpmnRuntimeClient } from "../api/bpmnRuntime";
import { BpmnRunCanvas } from "./BpmnRunCanvas";
import { BpmnCommandNotice } from "./BpmnCommandNotice";
import { useBpmnCommand } from "./useBpmnCommand";

type Client = ReturnType<typeof createBpmnRuntimeClient>;
export function bpmnRunState(execution: BpmnExecution, inspection?: BpmnInspection) {
  const terminal = typeof execution.result?.status === "string" ? execution.result.status : "Finished";
  switch (execution.status) {
    case "TerminalSettled": return terminal;
    case "TerminalPendingSettlement": return `${terminal} · awaiting settlement`;
    case "WaitingIncident": return "Needs attention";
    case "Running": return inspection?.work.some(work => work.kind === "Human") ? "Waiting for a person"
      : inspection?.work.some(work => work.kind === "Timer") ? "Waiting for a timer"
        : inspection?.work.some(work => work.kind === "Native") ? "Awaiting native work" : "Running";
    default: return "Unsupported lifecycle";
  }
}
function processName(inspection: BpmnInspection) {
  return typeof inspection.process.name === "string" && inspection.process.name.trim() ? inspection.process.name
    : inspection.source?.activityPresentation.find(item => item.displayName?.trim())?.displayName ?? "BPMN process";
}
function elementName(inspection: BpmnInspection, id: string) {
  const elements = inspection.process.elements;
  const element = Array.isArray(elements) ? elements.find(item => item && typeof item === "object" && item.elementId === id) : null;
  return element && typeof element.name === "string" && element.name.trim() ? element.name : id;
}
function linkScope(session: StudioBackendSession) {
  return JSON.stringify([session.key, session.profile.id, session.profile.baseUrl, session.identity.status, session.identity.subject, session.identity.tenantId]);
}

export function BpmnRunDetails({ client, session, executionId }: { client: Client; session: StudioBackendSession; executionId: string }) {
  const [selectedElement, setSelectedElement] = useState<string | null>(null);
  const query = useQuery({ queryKey: [...client.queryKey, client.scope, "inspection", executionId],
    queryFn: async ({ signal }) => {
      const value = await client.inspection(executionId, signal);
      if (value.execution.executionId !== executionId) throw new Error("The inspection belongs to another BPMN run.");
      return value;
    }, retry: false, refetchOnWindowFocus: false });
  const command = useBpmnCommand(client, { kind: "complete-human", executionId }, () => { void query.refetch(); });
  const retry = useBpmnCommand(client, { kind: "retry-service", executionId }, () => { void query.refetch(); });
  const claim = useBpmnCommand(client, { kind: "claim-human", executionId }, () => { void query.refetch(); });
  const claimed = useBpmnCommand(client, { kind: "complete-claimed-human", executionId }, () => { void query.refetch(); });
  const completionAvailable = evaluateBackendRequirements(session, [{ id: "elsa.api.bpmn-runtime", contractVersion: "1", relations: ["bpmn-complete-human"] }]).state === "available";
  const retryAvailable = evaluateBackendRequirements(session, [{ id: "elsa.api.bpmn-runtime", contractVersion: "1", relations: ["bpmn-retry-service"] }]).state === "available";
  const claimsAvailable = evaluateBackendRequirements(session, [{ id: "elsa.api.bpmn-claims", contractVersion: "1",
    relations: ["bpmn-claim-human", "bpmn-complete-claimed-human"] }]).state === "available";
  const scope = linkScope(session), context = session.context;
  if (query.isError) return <div role="alert"><p>{query.error.message}</p><StudioButton type="button" onClick={() => void query.refetch()}>Reload run</StudioButton></div>;
  if (!query.data) return <p role="status">Loading BPMN run…</p>;
  const inspection = query.data;
  const ready = (value: ReturnType<typeof useBpmnCommand>) => !value.blocked && !value.busy && (!value.record || value.confirmed);
  const humanProfile = inspection.profile === "foundation-direct-v1" || inspection.profile === "foundation-direct-claims-v1";
  const canComplete = completionAvailable && session.identity.status === "authenticated" && humanProfile
    && inspection.execution.status === "Running" && ready(command);
  // Claim actions follow the server's exact current owner; the browser never supplies a claimant.
  const claimable = claimsAvailable && !session.disposed && session.identity.status === "authenticated" && inspection.profile === "foundation-direct-claims-v1"
    && inspection.execution.status === "Running" && !query.isFetching;
  const owns = (work: BpmnInspection["work"][number]) => !!work.claim && !!session.identity.subject && work.claim.claimantId === session.identity.subject;
  const canRetry = (incident: BpmnIncident) => retryAvailable && !session.disposed && session.identity.status === "authenticated"
    && inspection.profile === "foundation-native-work-v1" && inspection.execution.status === "WaitingIncident" && !query.isFetching
    && !retry.blocked && !retry.busy && (!retry.record || retry.confirmed) && incident.status === "Open" && incident.retryAttemptId === null
    && Number.isInteger(incident.attemptNumber) && incident.attemptNumber > 0 && Number.isInteger(incident.maxAttempts) && incident.attemptNumber < incident.maxAttempts
    && inspection.work.some(work => work.kind === "Native" && work.elementId === incident.elementId && work.bindingId === incident.bindingId
      && work.invocationId === incident.invocationId && work.attemptId === incident.failedAttemptId && work.workerExecutionId === incident.workerExecutionId)
    && inspection.attempts.some(attempt => attempt.bindingId === incident.bindingId && attempt.invocationId === incident.invocationId
      && attempt.attemptId === incident.failedAttemptId && attempt.workerExecutionId === incident.workerExecutionId && attempt.attemptNumber === incident.attemptNumber);
  return <div className="wf-bpmn-run">
    <header className="wf-bpmn-run-heading">
      <div><h3>{processName(inspection)}</h3><p role="status">{bpmnRunState(inspection.execution, inspection)}</p>
        <p>Published version {inspection.source?.sourceVersion ?? "unavailable"} · Revision {inspection.execution.revision}</p></div>
      <StudioButton type="button" disabled={query.isFetching || command.busy || retry.busy || claim.busy || claimed.busy} onClick={() => void query.refetch()}>Refresh run</StudioButton>
    </header>
    <BpmnCommandNotice command={command} label="completion" />
    <BpmnCommandNotice command={retry} label="retry" />
    <BpmnCommandNotice command={claim} label="claim" />
    <BpmnCommandNotice command={claimed} label="completion" />
    <div className="wf-bpmn-run-workbench">
      <BpmnRunCanvas key={executionId} inspection={inspection} selectedElement={selectedElement} onSelect={setSelectedElement} />
      <section className="wf-bpmn-current-work" aria-label="Current BPMN work">
        <h3>Current work</h3>
        {!inspection.work.length ? <p>{inspection.execution.status.startsWith("Terminal") ? "No outstanding work." : "No current work is recorded."}</p> : null}
        {inspection.work.map(work => <article key={`${work.invocationId}:${work.attemptId}`} className="wf-bpmn-work-item">
          <h4>{elementName(inspection, work.elementId)}</h4>
          <p>{work.kind === "Human" ? "Human wait" : work.kind === "Timer" ? `Timer${work.dueAt ? ` · ${new Date(work.dueAt).toLocaleString()}` : ""}` : "Native work requested"}</p>
          {work.kind === "Human" && work.claimRequired ? <p role="note">{!work.claim ? "Unclaimed. Claim this work before completing it."
            : owns(work) ? "Claimed by you." : `Claimed by ${work.claim.claimantId}.`}</p> : null}
          <div className="wf-bpmn-actions"><StudioButton type="button" variant="default" onClick={() => setSelectedElement(work.elementId)}>Show on diagram</StudioButton>
            {work.kind === "Human" && work.claimRequired ? (!work.claim
              ? <StudioButton type="button" variant="primary" disabled={!claimable || !ready(claim)}
                title={!claimsAvailable ? "This backend does not offer human work claims." : undefined}
                onClick={() => { if (claimable && ready(claim)) void claim.begin({ kind: "claim-human", executionId, invocationId: work.invocationId, attemptId: work.attemptId }); }}>Claim work</StudioButton>
              : <StudioButton type="button" variant="primary" disabled={!claimable || !ready(claimed) || !owns(work)}
                title={!claimsAvailable ? "This backend does not offer human work claims." : !owns(work) ? "Only the current owner can complete this work." : undefined}
                onClick={() => { if (claimable && ready(claimed) && owns(work) && work.claim) void claimed.begin({ kind: "complete-claimed-human", executionId,
                  invocationId: work.invocationId, attemptId: work.attemptId, claimId: work.claim.claimId }); }}>Complete work</StudioButton>)
            : work.kind === "Human" ? <StudioButton type="button" variant="primary" disabled={!canComplete}
              title={!completionAvailable ? "This backend does not offer human completion." : !humanProfile ? "This process needs a compatible work action." : undefined}
              onClick={() => { if (canComplete) void command.begin({ kind: "complete-human", executionId, invocationId: work.invocationId, attemptId: work.attemptId }); }}>Complete work</StudioButton> : null}</div>
        </article>)}
        {inspection.incidents.length ? <><h3>Incidents</h3>{inspection.incidents.map(incident => <article key={incident.incidentId} className="wf-bpmn-work-item">
          <h4>{elementName(inspection, incident.elementId)}</h4><p>{incident.status} · {incident.faultCode}</p>
          <p>{incident.attemptNumber} of {incident.maxAttempts} attempts used · Failed <time dateTime={incident.failedAt}>{new Date(incident.failedAt).toLocaleString()}</time></p>
          <CopyableIdentifier label="Failed attempt" value={incident.failedAttemptId} />
          {incident.retryAttemptId ? <CopyableIdentifier label="Replacement attempt" value={incident.retryAttemptId} /> : null}
          {incident.status === "Open" ? <StudioButton type="button" disabled={!canRetry(incident)}
            title={!retryAvailable ? "This backend does not offer service retry." : undefined}
            onClick={() => { if (canRetry(incident)) void retry.begin({ kind: "retry-service", executionId, incidentId: incident.incidentId,
              invocationId: incident.invocationId, failedAttemptId: incident.failedAttemptId }); }}>Retry service</StudioButton> : null}
        </article>)}</> : null}
      </section>
    </div>
    {inspection.attempts.length ? <section><h3>Native attempts</h3><ul>{inspection.attempts.map(attempt => <li key={`${attempt.invocationId}:${attempt.attemptId}`}>
      {attempt.status}{attempt.attemptNumber ? ` · Attempt ${attempt.attemptNumber}` : ""}{attempt.outcomeKey ? ` · ${attempt.outcomeKey}` : ""}{attempt.faultCode ? ` · ${attempt.faultCode}` : ""}
      <CopyableIdentifier label="Native run" value={attempt.workerExecutionId} />
      <a data-bpmn-native-attempt="" href={`/workflows/instances/${encodeURIComponent(attempt.workerExecutionId)}?${new URLSearchParams({ backend: session.profile.id })}`}
        onClick={event => { if (session.disposed || session.context !== context || linkScope(session) !== scope) event.preventDefault(); }}>Open native attempt</a>
      <details><summary>Pinned native executable</summary><div className="wf-bpmn-identifiers">
        <CopyableIdentifier label="Executable" value={attempt.workerExecutable.artifactId} /><CopyableIdentifier label="Definition" value={attempt.workerExecutable.definitionId} />
        <CopyableIdentifier label="Definition version" value={attempt.workerExecutable.definitionVersionId} /><CopyableIdentifier label="Artifact version" value={attempt.workerExecutable.artifactVersion} />
        <CopyableIdentifier label="Artifact hash" value={attempt.workerExecutable.artifactHash} />
      </div></details>
    </li>)}</ul></section> : null}
    {inspection.settlements.length ? <section><h3>Settlement</h3><ul>{inspection.settlements.map((item, index) => <li key={`${item.kind}:${item.executionId}:${index}`}>
      {item.kind === "NativeParent" ? "Native parent" : "Native cancellation"}: {item.notAdmitted ? "Not admitted" : item.settled ? "Settled" : "Pending"}
    </li>)}</ul></section> : null}
    <details><summary>Run identifiers</summary><div className="wf-bpmn-identifiers">
      <CopyableIdentifier label="BPMN run" value={executionId} /><CopyableIdentifier label="Executable" value={inspection.execution.executable.artifactId} />
      <CopyableIdentifier label="Definition" value={inspection.execution.executable.definitionId} /><CopyableIdentifier label="Definition version" value={inspection.execution.executable.definitionVersionId} />
      <CopyableIdentifier label="Artifact version" value={inspection.execution.executable.artifactVersion} /><CopyableIdentifier label="Artifact hash" value={inspection.execution.executable.artifactHash} />
      <CopyableIdentifier label="Material incarnation" value={inspection.execution.materialIncarnationId} />
      <CopyableIdentifier label="Authority system" value={inspection.authority.systemIdentity} /><CopyableIdentifier label="Root initiator" value={inspection.authority.rootInitiator} />
      {inspection.source ? <><CopyableIdentifier label="Source reference" value={inspection.source.sourceReferenceId} /><CopyableIdentifier label="Source version" value={inspection.source.sourceId} /></> : null}
    </div></details>
  </div>;
}
