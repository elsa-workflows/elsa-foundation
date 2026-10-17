import { useCallback, useEffect, useRef, useState } from "react";
import { LoaderCircle, Send, X } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { EditableCombobox } from "../EditableCombobox";
import { HttpMessageBodyEditor } from "./HttpMessageBodyEditor";
import { HttpTestResponse, type HttpTestResult } from "./HttpTestResponse";
import { getExecutable, getExecutableInputSources } from "../api/runtime";
import {
  describeHttpEndpointAcceptedResponse,
  findPublishedHttpEndpoint,
  getWorkflowHttpBasePath,
  makeInitialHttpTestPath,
  publishedHttpEndpointInputsUnavailableMessage,
  readHttpEndpointResponseMetadata,
  resolveHttpTestEndpointPath,
  type PublishedHttpEndpoint
} from "./httpTestRequest";
import { useDialogFocus } from "./useDialogFocus";
import "./HttpTestRequestDialog.css";

const contentTypeSuggestions = [
  "application/json",
  "text/plain",
  "text/html",
  "application/xml",
  "application/x-www-form-urlencoded",
  "application/octet-stream"
];

const exampleJson = `{
  "name": "Ada Lovelace"
}`;
// Leave room for the default 30-second server deadline to return its HTTP 408 response.
const requestTimeoutMs = 35_000;

export function HttpTestRequestDialog({
  context,
  open,
  artifactId,
  authoredActivityId,
  onClose
}: {
  context: StudioEndpointContext;
  open: boolean;
  artifactId: string | null;
  authoredActivityId: string;
  onClose(): void;
}) {
  const dialogRef = useRef<HTMLElement>(null);
  const activeRequest = useRef<AbortController | null>(null);
  const sending = useRef(false);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState("");
  const [endpoint, setEndpoint] = useState<PublishedHttpEndpoint | null>(null);
  const [workflowHttpBasePath, setWorkflowHttpBasePath] = useState<string | null>(null);
  const [path, setPath] = useState("");
  const [method, setMethod] = useState("");
  const [requestBody, setRequestBody] = useState(exampleJson);
  const [contentType, setContentType] = useState("application/json");
  const [requestError, setRequestError] = useState("");
  const [result, setResult] = useState<HttpTestResult | null>(null);
  const [isSending, setIsSending] = useState(false);

  const close = useCallback(() => {
    const request = activeRequest.current;
    activeRequest.current = null;
    sending.current = false;
    request?.abort();
    onClose();
  }, [onClose]);

  useDialogFocus(dialogRef, close);

  useEffect(() => {
    if (!open) {
      activeRequest.current?.abort();
      return;
    }
    let active = true;
    setEndpoint(null);
    setWorkflowHttpBasePath(null);
    setLoadError("");
    setRequestError("");
    setResult(null);
    if (!artifactId) {
      setLoadError("Publish this workflow before testing its HTTP endpoint.");
      return () => { active = false; };
    }

    setLoading(true);
    void (async () => {
      try {
        const [executable, configuredBasePath] = await Promise.all([
          getExecutable(context, artifactId),
          getWorkflowHttpBasePath(context)
        ]);
        if (!active) return;
        setWorkflowHttpBasePath(configuredBasePath);
        const sourceReferenceId = executable.chosenReference?.sourceReferenceId;
        if (!sourceReferenceId) throw new Error(publishedHttpEndpointInputsUnavailableMessage);

        const inputSources = await getExecutableInputSources(context, artifactId, sourceReferenceId).catch(() => {
          throw new Error(publishedHttpEndpointInputsUnavailableMessage);
        });
        if (!active) return;
        const selected = findPublishedHttpEndpoint(executable, authoredActivityId, inputSources);
        if (!selected) {
          setLoadError("Studio needs literal Path and SupportedMethods values in the published endpoint to build a test request.");
          return;
        }
        setEndpoint(selected);
        setPath(makeInitialHttpTestPath(selected.routeTemplate, configuredBasePath).slice(configuredBasePath.length));
        setMethod(selected.supportedMethods.includes("POST") ? "POST" : selected.supportedMethods[0]);
        setRequestBody(exampleJson);
        setContentType("application/json");
      } catch (error) {
        if (active) setLoadError(error instanceof Error ? error.message : "Studio could not load the published workflow endpoint.");
      } finally {
        if (active) setLoading(false);
      }
    })();

    return () => { active = false; };
  }, [artifactId, authoredActivityId, context, open]);

  useEffect(() => () => {
    activeRequest.current?.abort();
    activeRequest.current = null;
  }, []);

  const sendRequest = async () => {
    if (sending.current || !endpoint || !workflowHttpBasePath || !method) return;
    setResult(null);
    const send = context.http.requestResponse;
    if (!send) {
      setRequestError("This Studio connection cannot send requests while preserving the backend response. Refresh Studio and try again.");
      return;
    }

    let requestPath: string;
    try {
      requestPath = resolveHttpTestEndpointPath(context.baseUrl, path, workflowHttpBasePath);
    } catch (error) {
      setRequestError(error instanceof Error ? error.message : "Enter a valid path on the configured backend.");
      return;
    }

    const controller = new AbortController();
    activeRequest.current = controller;
    sending.current = true;
    setIsSending(true);
    setRequestError("");
    let timedOut = false;
    const timeout = window.setTimeout(() => {
      timedOut = true;
      controller.abort();
    }, requestTimeoutMs);

    try {
      const carriesBody = method !== "GET" && method !== "HEAD";
      const headers = new Headers({ Accept: "*/*" });
      if (carriesBody && contentType.trim()) headers.set("Content-Type", contentType.trim());

      // `context.http` keeps Studio's configured auth and credentials transport. This endpoint returns
      // workflow output, so retain the raw response instead of passing it through the JSON API parser.
      const response = await send.call(context.http, requestPath, {
        method,
        headers,
        ...(carriesBody && requestBody !== "" ? { body: requestBody } : {}),
        cache: "no-store",
        retryOnUnauthorized: false,
        signal: controller.signal,
        // The dialog timer also covers reading the response body. Give the SDK's request timer a little
        // extra room so the dialog can present one consistent timeout for the full exchange.
        timeoutMs: requestTimeoutMs + 1_000,
        redirect: "manual"
      });
      if (response.type === "opaqueredirect") {
        throw new Error("The workflow redirected this request, but the browser does not expose the redirect response. Studio does not follow workflow redirects.");
      }
      const body = await response.text();
      if (activeRequest.current !== controller) return;
      const responseMetadata = response.status === 202 ? readHttpEndpointResponseMetadata(body) : null;
      setResult({
        status: response.status,
        statusText: response.statusText,
        contentType: response.headers.get("content-type") || "",
        headers: [...response.headers.entries()],
        body,
        acceptedMessage: response.status === 202
          ? describeHttpEndpointAcceptedResponse(responseMetadata)
          : null
      });
    } catch (error) {
      if (activeRequest.current === controller && timedOut) {
        setRequestError(`The HTTP request took longer than ${requestTimeoutMs / 1000} seconds. The published workflow may still be running.`);
      } else if (activeRequest.current === controller && !controller.signal.aborted) {
        setRequestError(error instanceof Error ? error.message : "Studio could not send the HTTP request.");
      }
    } finally {
      window.clearTimeout(timeout);
      if (activeRequest.current === controller) {
        activeRequest.current = null;
        sending.current = false;
        setIsSending(false);
      }
    }
  };

  if (!open) return null;

  const bodyDisabled = method === "GET" || method === "HEAD";
  const currentRequestPath = path.trim();

  return (
    <div className="wf-dialog-backdrop" role="presentation" onMouseDown={event => {
      if (event.target === event.currentTarget) close();
    }}>
      <section
        ref={dialogRef}
        className="wf-dialog wf-http-test-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="http-test-title"
        aria-describedby="http-test-description"
        tabIndex={-1}
      >
        <header className="wf-http-test-heading">
          <div>
            <span>Published workflow</span>
            <h3 id="http-test-title">Send an HTTP test request</h3>
            <p id="http-test-description">Send a real request to this workflow’s published endpoint.</p>
          </div>
          <button type="button" className="wf-icon-button" aria-label="Close test request" onClick={close}>
            <X size={18} aria-hidden="true" />
          </button>
        </header>

        <div className="wf-http-test-body">
          {loading ? <p className="wf-http-test-state" role="status"><LoaderCircle size={16} className="wf-http-test-spinner" /> Loading published endpoint…</p> : null}
          {loadError ? <p className="wf-http-test-error" role="alert">{loadError}</p> : null}

          {endpoint ? (
            <form onSubmit={event => { event.preventDefault(); void sendRequest(); }}>
              <div className="wf-http-test-request-line">
                <label className="wf-form-field wf-http-test-method">
                  <span>Method</span>
                  <select aria-label="HTTP method" value={method} disabled={isSending} onChange={event => setMethod(event.target.value)}>
                    {endpoint.supportedMethods.map(supportedMethod => <option key={supportedMethod} value={supportedMethod}>{supportedMethod}</option>)}
                  </select>
                </label>
                <div className="wf-form-field wf-http-test-path">
                  <span>Request path</span>
                  <div className="wf-http-test-path-input">
                    <input
                      className="wf-http-test-mount"
                      aria-label="Workflow HTTP mount"
                      value={workflowHttpBasePath ?? ""}
                      size={(workflowHttpBasePath?.length ?? 0) + 1}
                      readOnly
                      spellCheck={false}
                    />
                    <input
                      aria-label="Endpoint path"
                      aria-describedby="http-test-path-hint"
                      value={path}
                      disabled={isSending}
                      autoCapitalize="off"
                      autoCorrect="off"
                      spellCheck={false}
                      onChange={event => setPath(event.target.value)}
                      placeholder="/orders/1"
                    />
                  </div>
                </div>
                <button type="submit" className="wf-http-test-send" disabled={!currentRequestPath || isSending}>
                  {isSending ? <LoaderCircle size={15} className="wf-http-test-spinner" /> : <Send size={15} />}
                  {isSending ? "Sending request…" : "Send request"}
                </button>
              </div>
              <p id="http-test-path-hint" className="wf-http-test-hint">Edit the endpoint path. The HTTP mount is fixed by the server.</p>

              <EditableCombobox
                  className="wf-form-field wf-http-test-content-type"
                  label="Request content type"
                  value={contentType}
                  suggestions={contentTypeSuggestions}
                  disabled={bodyDisabled || isSending}
                  onChange={setContentType}
                  spellCheck={false}
                />
              <div className="wf-form-field wf-http-test-body-editor">
                <span>Request body</span>
                <HttpMessageBodyEditor
                  label="Request body"
                  contentType={contentType}
                  value={requestBody}
                  readOnly={bodyDisabled || isSending}
                  onChange={setRequestBody}
                />
                {bodyDisabled ? <small>GET and HEAD requests do not send a body.</small> : <small>You can edit this example to test your own request.</small>}
              </div>
            </form>
          ) : null}

          {requestError ? <p className="wf-http-test-error" role="alert">{requestError}</p> : null}
          {result ? <HttpTestResponse result={result} /> : null}
        </div>
      </section>
    </div>
  );
}
