import "./workflowTestDrive.css";
import type { StudioTestStimulusEditorProps, StudioTestStimulusResultProps } from "@elsa-workflows/studio-sdk";
import { defaultHttpTestRequestDraft } from "./workflowTestRequestDefaults";
import { asRecord } from "./workflowTestStimulusEditorUtils";

export function HttpRequestEditorView({ option, mode, draft, disabled, controlProps, onChange }: StudioTestStimulusEditorProps) {
  const configuration = asRecord(mode.configuration) ?? asRecord(option.configuration) ?? {};
  const method = typeof configuration.method === "string" ? configuration.method : "HTTP";
  const template = typeof configuration.template === "string" ? configuration.template : "route resolved by the server";
  const placeholder = defaultHttpTestRequestDraft;
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

export function EventEditorView({ option, mode, draft, disabled, controlProps, onChange }: StudioTestStimulusEditorProps) {
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

export function SchedulingEditorView({ option, mode }: StudioTestStimulusEditorProps) {
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

export function HttpExchangeResultView({ action, result }: StudioTestStimulusResultProps) {
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
