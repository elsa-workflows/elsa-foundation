import { useId, useState } from "react";
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
}

export function HttpTestResponse({ result }: { result: HttpTestResult }) {
  const baseId = useId();
  const [activeTab, setActiveTab] = useState("body");
  return <section className="wf-http-test-result" aria-label="HTTP response">
    <header>
      <span>Response</span>
      <strong data-success={result.status >= 200 && result.status < 300 || undefined} data-error={result.status < 200 || result.status >= 300 || undefined}>
        {result.status} {result.statusText}
      </strong>
    </header>
    {result.acceptedMessage ? <p className="wf-http-test-accepted" role="status">
      <AlertTriangle size={15} /> {result.acceptedMessage}{" "}
      <StudioLink href="/workflows/instances">Inspect workflow runs</StudioLink>
    </p> : null}
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
