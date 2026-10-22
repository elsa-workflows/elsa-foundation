import { useEffect, useId, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getWorkflowInstance } from "../api/runtime";
import type { WorkflowInstanceDetails } from "../workflowTypes";
import { isActiveIncident } from "../workflowRuntimeOverlays";
import { StudioTabs, StudioTabPanel } from "@elsa-workflows/studio-ui";
import { AlertTriangle } from "lucide-react";
import { StudioLink } from "../StudioLink";
import { HttpMessageBodyEditor } from "./HttpMessageBodyEditor";

export interface HttpTestResult {
  status: number;
  statusText: string;
  contentType: string;
  headers: [string, string][];
  body: string;
  acceptedMessage: string | null;
  responseReason?: string | null;
  workflowExecutionId?: string | null;
}

export function HttpTestResponse({ result, context }: { result: HttpTestResult; context?: StudioEndpointContext }) {
  const baseId = useId();
  const [activeTab, setActiveTab] = useState("body");
  const [runFeedback, setRunFeedback] = useState<{ result: HttpTestResult; message: string } | null>(null);
  useEffect(() => {
    if (!context || result.status !== 202 || result.responseReason !== "workflow-response-not-produced" || !result.workflowExecutionId) return;
    let active = true;
    // One best-effort inspection after the HTTP exchange; the raw response remains visible throughout.
    void getWorkflowInstance(context, result.workflowExecutionId).then(inspection => {
      const message = describeRunIssue(inspection);
      if (active && message) setRunFeedback({ result, message });
    }).catch(() => { /* Run inspection permission or availability must not hide the HTTP response. */ });
    return () => { active = false; };
  }, [context, result]);
  const issueMessage = runFeedback?.result === result ? runFeedback.message : null;
  const acceptedMessage = issueMessage ?? result.acceptedMessage;
  return <section className="wf-http-test-result" aria-label="HTTP response">
    <header>
      <span>Response</span>
      <strong data-success={result.status >= 200 && result.status < 300 || undefined} data-error={result.status < 200 || result.status >= 300 || undefined}>
        {result.status} {result.statusText}
      </strong>
    </header>
    {acceptedMessage ? <div className="wf-http-test-accepted" role="status" data-error={!!issueMessage || undefined}>
      <AlertTriangle size={15} aria-hidden="true" />
      <div>
        <p>{acceptedMessage}</p>
        <StudioLink href={result.workflowExecutionId ? `/workflows/instances/${encodeURIComponent(result.workflowExecutionId)}` : "/workflows/instances"}>
          {result.workflowExecutionId ? "Inspect run" : "Inspect workflow runs"}
        </StudioLink>
      </div>
    </div> : null}
    <StudioTabs
      baseId={baseId}
      ariaLabel="HTTP response sections"
      tabs={[{ id: "body", label: "Body" }, { id: "headers", label: `Headers (${result.headers.length})` }]}
      activeTab={activeTab}
      onSelect={setActiveTab}
    />
    <StudioTabPanel baseId={baseId} index={0} hidden={activeTab !== "body"}>
      <div className="wf-http-test-body-editor">
        {result.body ? <HttpMessageBodyEditor label="Response body" contentType={result.contentType} value={result.body} readOnly /> : <p className="wf-http-test-empty">The response body is empty.</p>}
      </div>
    </StudioTabPanel>
    <StudioTabPanel baseId={baseId} index={1} hidden={activeTab !== "headers"}>
      <p className="wf-http-test-hint">Headers available to this browser.</p>
      {result.headers.length ? <div className="wf-http-test-headers"><table aria-label="Response headers">
        <thead><tr><th scope="col">Header</th><th scope="col">Value</th></tr></thead>
        <tbody>{result.headers.map(([name, value]) => <tr key={name}><th scope="row">{name}</th><td>{value}</td></tr>)}</tbody>
      </table></div> : <p className="wf-http-test-empty">No response headers are available.</p>}
    </StudioTabPanel>
  </section>;
}

function describeRunIssue({ instance, incidents }: WorkflowInstanceDetails): string | null {
  const blocking = incidents?.filter(incident => incident.isBlocking && isActiveIncident(incident)) ?? [];
  if ([instance.status, instance.subStatus].some(status => status?.toLowerCase() === "faulted")
    || blocking.some(incident => incident.severity.toLowerCase() === "error" || incident.severity.toLowerCase() === "critical")) {
    return "The workflow encountered an error before it could return an HTTP response. Inspect the run to find the cause.";
  }
  if (blocking.length > 0 || (instance.blockingIncidentCount ?? 0) > 0) {
    return "The workflow has a blocking issue and did not return an HTTP response. Inspect the run for details.";
  }
  return null;
}
