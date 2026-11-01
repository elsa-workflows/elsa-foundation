import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { StudioButton, CopyableIdentifier } from "@elsa-workflows/studio-ui";
import { evaluateBackendRequirements, type StudioBackendSession } from "@elsa-workflows/studio-sdk";
import type { BpmnExecution, BpmnInspection, createBpmnRuntimeClient } from "../api/bpmnRuntime";
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

export function BpmnRunDetails({ client, session, executionId }: { client: Client; session: StudioBackendSession; executionId: string }) {
  const [selectedElement, setSelectedElement] = useState<string | null>(null);
  const query = useQuery({ queryKey: [...client.queryKey, client.scope, "inspection", executionId],
    queryFn: async ({ signal }) => {
      const value = await client.inspection(executionId, signal);
      if (value.execution.executionId !== executionId) throw new Error("The inspection belongs to another BPMN run.");
      return value;
    }, retry: false, refetchOnWindowFocus: false });
  const command = useBpmnCommand(client, { kind: "complete-human", executionId }, () => { void query.refetch(); });
  const completionAvailable = evaluateBackendRequirements(session, [{ id: "elsa.api.bpmn-runtime", contractVersion: "1", relations: ["bpmn-complete-human"] }]).state === "available";
  if (query.isError) return <div role="alert"><p>{query.error.message}</p><StudioButton type="button" onClick={() => void query.refetch()}>Reload run</StudioButton></div>;
  if (!query.data) return <p role="status">Loading BPMN run…</p>;
  const inspection = query.data;
  const canComplete = completionAvailable && session.identity.status === "authenticated" && inspection.profile === "foundation-direct-v1"
    && inspection.execution.status === "Running" && !command.blocked && !command.busy && (!command.record || command.confirmed);
  return <div className="wf-bpmn-run">
    <header className="wf-bpmn-run-heading">
      <div><h3>{processName(inspection)}</h3><p role="status">{bpmnRunState(inspection.execution, inspection)}</p>
        <p>Published version {inspection.source?.sourceVersion ?? "unavailable"} · Revision {inspection.execution.revision}</p></div>
      <StudioButton type="button" disabled={query.isFetching || command.busy} onClick={() => void query.refetch()}>Refresh run</StudioButton>
    </header>
    <BpmnCommandNotice command={command} label="completion" />
    <div className="wf-bpmn-run-workbench">
      <BpmnRunCanvas key={executionId} inspection={inspection} selectedElement={selectedElement} onSelect={setSelectedElement} />
      <section className="wf-bpmn-current-work" aria-label="Current BPMN work">
        <h3>Current work</h3>
        {!inspection.work.length ? <p>{inspection.execution.status.startsWith("Terminal") ? "No outstanding work." : "No current work is recorded."}</p> : null}
        {inspection.work.map(work => <article key={`${work.invocationId}:${work.attemptId}`} className="wf-bpmn-work-item">
          <h4>{elementName(inspection, work.elementId)}</h4>
          <p>{work.kind === "Human" ? "Human wait" : work.kind === "Timer" ? `Timer${work.dueAt ? ` · ${new Date(work.dueAt).toLocaleString()}` : ""}` : "Native work requested"}</p>
          <div className="wf-bpmn-actions"><StudioButton type="button" variant="default" onClick={() => setSelectedElement(work.elementId)}>Show on diagram</StudioButton>
            {work.kind === "Human" ? <StudioButton type="button" variant="primary" disabled={!canComplete}
              title={!completionAvailable ? "This backend does not offer human completion." : inspection.profile !== "foundation-direct-v1" ? "This process needs a compatible work action." : undefined}
              onClick={() => { if (canComplete) void command.begin({ kind: "complete-human", executionId, invocationId: work.invocationId, attemptId: work.attemptId }); }}>Complete work</StudioButton> : null}</div>
        </article>)}
        {inspection.incidents.length ? <><h3>Incidents</h3>{inspection.incidents.map(incident => <p key={incident.incidentId}>{elementName(inspection, incident.elementId)} · {incident.status} · {incident.faultCode}</p>)}</> : null}
      </section>
    </div>
    {inspection.attempts.length ? <section><h3>Native attempts</h3><ul>{inspection.attempts.map(attempt => <li key={`${attempt.invocationId}:${attempt.attemptId}`}>
      {attempt.status}{attempt.attemptNumber ? ` · Attempt ${attempt.attemptNumber}` : ""}{attempt.outcomeKey ? ` · ${attempt.outcomeKey}` : ""}{attempt.faultCode ? ` · ${attempt.faultCode}` : ""}
      <CopyableIdentifier label="Native run" value={attempt.workerExecutionId} />
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
