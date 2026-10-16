import type {
  StudioTestStimulusResultProps,
  StudioTestStimulusEditorContribution,
  StudioTestStimulusEditorProps
} from "@elsa-workflows/studio-sdk";

export const httpWorkflowTestStimulusEditor: StudioTestStimulusEditorContribution = {
  id: "elsa.workflows.test-stimulus.http-request",
  order: 100,
  startLabel: "Send request and start",
  actionLabel: "Send HTTP request",
  supports: ({ option, mode }) => option.driverId === "Elsa.HttpEndpoint" && mode.modeId === "http-request",
  component: HttpRequestEditor,
  validate: ({ draft }) => {
    const value = readObject(draft);
    if (!value) return "Enter valid JSON for the HTTP request fields.";
    for (const name of ["routeValues", "query", "headers"]) {
      if (!isStringMap(value[name], name === "routeValues" ? "string" : "string[]")) return `${name} must be an object of ${name === "routeValues" ? "string values" : "string arrays"}.`;
    }
    if (value.body !== undefined && value.body !== null && typeof value.body !== "string") return "body must be text or null.";
    const headers = asRecord(value.headers) ?? {};
    if (Object.keys(headers).some(name => ["authorization", "cookie", "proxy-authorization"].includes(name.toLowerCase()))) {
      return "Authentication headers are supplied by the authenticated API transport.";
    }
    return undefined;
  },
  serialize: ({ draft }) => readObject(draft) ?? {},
  executeAction: sendHttpRequest,
  actionResultComponent: HttpExchangeResult
};

export const eventWorkflowTestStimulusEditor: StudioTestStimulusEditorContribution = {
  id: "elsa.workflows.test-stimulus.event",
  order: 110,
  startLabel: "Fire event now",
  supports: ({ option, mode }) => option.driverId === "Elsa.Event" && mode.modeId === "event",
  component: EventEditor,
  validate: ({ draft }) => {
    try {
      const value: unknown = JSON.parse(draft || "{}");
      return isRecord(value) && (value.payload === undefined || isJsonValue(value.payload))
        ? undefined
        : "Event data must be an object with an optional JSON payload.";
    } catch { return "Enter valid JSON for the event payload."; }
  },
  serialize: ({ draft }) => JSON.parse(draft || "{}")
};

export const schedulingWorkflowTestStimulusEditor: StudioTestStimulusEditorContribution = {
  id: "elsa.workflows.test-stimulus.schedule-fire-now",
  order: 120,
  startLabel: "Fire once now",
  supports: ({ option, mode }) =>
    (option.driverId === "Elsa.Timer" || option.driverId === "Elsa.Cron") && mode.modeId === "fire-now",
  component: SchedulingEditor,
  validate: () => undefined,
  serialize: () => undefined
};

function HttpRequestEditor({ option, mode, draft, disabled, controlProps, onChange }: StudioTestStimulusEditorProps) {
  const configuration = asRecord(mode.configuration) ?? asRecord(option.configuration) ?? {};
  const method = typeof configuration.method === "string" ? configuration.method : "HTTP";
  const template = typeof configuration.template === "string" ? configuration.template : "route resolved by the server";
  const placeholder = JSON.stringify({ routeValues: {}, query: {}, headers: {}, body: "" }, null, 2);
  return (
    <div className="wf-test-drive-http">
      <dl><div><dt>Endpoint</dt><dd>{method} {template}</dd></div></dl>
      <label className="wf-form-field" htmlFor={controlProps.id}>
        <span>HTTP request data</span>
        <textarea {...controlProps} rows={10} value={draft} placeholder={placeholder} disabled={disabled} onChange={event => onChange(event.currentTarget.value)} />
        <small>Use routeValues as strings, query and headers as arrays of strings, and body as text.</small>
      </label>
      <small>Requests use your current Studio identity. Authorization and Cookie headers are managed by Studio.</small>
    </div>
  );
}

function EventEditor({ option, mode, draft, disabled, controlProps, onChange }: StudioTestStimulusEditorProps) {
  const configuration = asRecord(mode.configuration) ?? asRecord(option.configuration) ?? {};
  const eventName = typeof configuration.eventName === "string" ? configuration.eventName : "Named event";
  const correlationId = typeof configuration.correlationId === "string" ? configuration.correlationId : "";
  return (
    <div className="wf-test-drive-event">
      <dl><div><dt>Event</dt><dd>{eventName}</dd></div>{correlationId ? <div><dt>Correlation</dt><dd>{correlationId}</dd></div> : null}</dl>
      <label className="wf-form-field" htmlFor={controlProps.id}>
        <span>Event data</span>
        <textarea {...controlProps} rows={5} value={draft} disabled={disabled} onChange={event => onChange(event.currentTarget.value)} />
        <small>Enter an object with an optional payload value.</small>
      </label>
    </div>
  );
}

function SchedulingEditor({ option, mode }: StudioTestStimulusEditorProps) {
  const configuration = asRecord(mode.configuration) ?? asRecord(option.configuration) ?? {};
  const kind = typeof configuration.kind === "string" ? configuration.kind : option.driverId === "Elsa.Cron" ? "cron" : "timer";
  const expression = typeof configuration.expression === "string" ? configuration.expression : "";
  const occurrences = Array.isArray(configuration.nextOccurrences)
    ? configuration.nextOccurrences.filter((item): item is string => typeof item === "string")
    : [];
  return (
    <div className="wf-test-drive-schedule">
      <dl>
        <div><dt>Schedule</dt><dd>{kind === "cron" ? "Cron" : "Timer"}</dd></div>
        <div><dt>Expression</dt><dd>{expression || "Unavailable"}</dd></div>
        <div><dt>Timezone</dt><dd>UTC</dd></div>
      </dl>
      <p>Fire once now starts one test execution. The authored schedule is unchanged.</p>
      {occurrences.length ? <div><span>Next occurrences · UTC</span><ol>{occurrences.map(item => <li key={item}>{item}</li>)}</ol></div> : null}
    </div>
  );
}

async function sendHttpRequest({ backend, action, data }: Parameters<NonNullable<StudioTestStimulusEditorContribution["executeAction"]>>[0]) {
  const relativeUrl = action.relativeUrl;
  if (!relativeUrl || !relativeUrl.startsWith("/") || relativeUrl.startsWith("//")) {
    throw new Error("The prepared HTTP action did not contain a safe server-relative URL.");
  }
  if (!backend.http.request) throw new Error("This Studio host does not expose the authenticated HTTP transport.");
  const base = new URL(backend.baseUrl || window.location.href, window.location.href);
  const url = new URL(relativeUrl, base.origin);
  if (url.origin !== base.origin) throw new Error("The prepared HTTP action must stay on the Studio API host.");
  const body = asRecord(data) ?? {};
  const query = asRecord(body.query) ?? {};
  for (const [key, raw] of Object.entries(query)) {
    const values = Array.isArray(raw) ? raw : [raw];
    for (const value of values) if (typeof value === "string") url.searchParams.append(key, value);
  }
  const headers = new Headers();
  for (const [key, raw] of Object.entries(asRecord(body.headers) ?? {})) {
    const values = Array.isArray(raw) ? raw : [raw];
    for (const value of values) if (typeof value === "string") headers.append(key, value);
  }
  const method = (action.method || "POST").toUpperCase();
  const textBody = typeof body.body === "string" ? body.body : undefined;
  const startedAt = performance.now();
  const controller = new AbortController();
  const timeoutMs = typeof body.timeoutMs === "number" && Number.isFinite(body.timeoutMs)
    ? Math.max(1000, Math.min(body.timeoutMs, 120000))
    : 30000;
  const timeout = window.setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await backend.http.request(url.href, {
      method,
      headers,
      ...(textBody !== undefined && !["GET", "HEAD"].includes(method) ? { body: textBody } : {}),
      signal: controller.signal
    });
    const responseText = await response.text();
    return {
      kind: "http-exchange",
      method,
      status: response.status,
      statusText: response.statusText,
      ok: response.ok,
      headers: [...response.headers.entries()],
      body: responseText,
      durationMs: Math.round(performance.now() - startedAt)
    };
  } catch {
    return {
      kind: "http-exchange",
      method,
      error: controller.signal.aborted ? `Request timed out after ${timeoutMs} ms.` : "The HTTP request failed.",
      durationMs: Math.round(performance.now() - startedAt)
    };
  } finally {
    window.clearTimeout(timeout);
  }
}

function HttpExchangeResult({ action, result }: StudioTestStimulusResultProps) {
  const exchange = result as Record<string, unknown>;
  const status = typeof exchange.status === "number" ? `${exchange.status} ${typeof exchange.statusText === "string" ? exchange.statusText : ""}`.trim() : null;
  const headers = Array.isArray(exchange.headers) ? exchange.headers as [string, string][] : [];
  return (
    <section className="wf-test-drive-exchange" aria-label="HTTP request and response">
      <header><strong>HTTP exchange</strong><span>{typeof exchange.method === "string" ? exchange.method : action.method}</span>{status ? <span data-ok={exchange.ok === true}>{status}</span> : null}</header>
      {typeof exchange.error === "string" ? <p role="alert">{exchange.error}</p> : null}
      {typeof exchange.durationMs === "number" ? <p>{exchange.durationMs} ms</p> : null}
      {headers.length ? <details><summary>Response headers</summary><dl>{headers.map(([name, value], index) => <div key={`${name}-${index}`}><dt>{name}</dt><dd>{value}</dd></div>)}</dl></details> : null}
      {typeof exchange.body === "string" ? <details open><summary>Response body</summary><pre>{exchange.body || "(empty response)"}</pre></details> : null}
    </section>
  );
}

function readObject(draft: string): Record<string, unknown> | null {
  try {
    const value: unknown = JSON.parse(draft || "{}");
    return isRecord(value) ? value : null;
  } catch { return null; }
}

function isStringMap(value: unknown, itemKind: "string" | "string[]") {
  if (!isRecord(value)) return false;
  return Object.values(value).every(item => itemKind === "string"
    ? typeof item === "string"
    : Array.isArray(item) && item.every(entry => typeof entry === "string"));
}

function isJsonValue(value: unknown): boolean {
  if (value === null || ["string", "number", "boolean"].includes(typeof value)) return true;
  if (Array.isArray(value)) return value.every(isJsonValue);
  return isRecord(value) && Object.values(value).every(isJsonValue);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function asRecord(value: unknown) {
  return isRecord(value) ? value : null;
}
