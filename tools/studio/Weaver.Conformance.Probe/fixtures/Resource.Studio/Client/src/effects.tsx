import React, { useEffect, useRef, useState } from "react";
import type {
  ElsaStudioModuleApi, StudioWeaverContextCollection, StudioWeaverPayload, StudioWeaverPreparedInvocation,
  StudioWeaverResourceTarget, StudioWeaverResultRendererProps
} from "@elsa-workflows/studio-sdk";

const moduleId = "Acme.Home";
const widgetId = "acme.home:status";
const hostId = "weaver-conformance3438";
const ownerRoute = "/qualification/acme-home";
// A sibling route keeps the declaring provider outside every legacy settings/Home context collection.
const route = "/qualification/acme-home-effects";
const endpoint = "/_qualification/acme/home-widget";
const contextKind = "acme.home.effects";
const checkTool = "acme_home_check_widget";
const checkType = "acme.home.check-result";
const checkRenderer = "acme.home.check-result-renderer";
const codes = ["owner_unavailable", "permission_denied", "scope_denied", "invalid", "stale", "target_missing", "changed", "check_passed", "check_failed"]
  .map(code => `acme.home.effects.${code}`);
const permissions = ["studio/preferences:read", "studio/preferences:write"];
const profiles = [
  { id: "acme_home_admin_visibility", label: "Administrative visibility", risk: "admin", description: "Review an administrative personal visibility change." },
  { id: "acme_home_irrecoverable_visibility", label: "Administrative and irrecoverable classes", risk: "admin", description: "Review a personal visibility change requiring both configured classes." },
  { id: "acme_home_omitted_admin_visibility", label: "Admin risk with omitted administrative class", risk: "admin", description: "The module supplies Irrecoverable; Weaver must include Administrative for Admin risk." },
  { id: "acme_home_raised_risk_visibility", label: "Risk raised during preparation", risk: "review-required", description: "The owner raises ReviewRequired to Admin while preparing the exact change." },
  { id: "acme_home_activation_visibility", label: "Activation class", risk: "review-required", description: "Review a personal visibility change requiring the configured Activation class." }
] as const;

interface Widget { widgetId: string; hostId: string; revision: string; visible: boolean }
interface CheckPreview { widgetId: string; hostId: string; revision: string; expectedVisible: boolean }
interface Check extends CheckPreview { actualVisible: boolean; passed: boolean; ran: true; callNumber: number }
interface Run { invocationId: string; toolCallId: string; toolName: string; revision: string; passed: boolean; callNumber: number }
interface Observations { widgetId: string; hostId: string; revision: string; ranCount: number; runs: Run[] }
type Verification = { state: "confirmed" | "stale" | "unavailable" | "duplicate"; message: string; ranCount?: number; matchingRuns?: number };

function record(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : null;
}
function exact(value: Record<string, unknown>, fields: string[]) {
  return Object.keys(value).length === fields.length && fields.every(field => Object.hasOwn(value, field));
}
function identifier(value: unknown): value is string { return typeof value === "string" && value.length > 0 && value.length <= 256; }
function count(value: unknown, minimum = 1): value is number { return typeof value === "number" && Number.isSafeInteger(value) && value >= minimum; }
function owner(value: Record<string, unknown>) { return value.widgetId === widgetId && value.hostId === hostId && identifier(value.revision); }
function widget(value: unknown): Widget {
  const data = record(value);
  if (!data || !exact(data, ["widgetId", "hostId", "revision", "visible"]) || !owner(data) || typeof data.visible !== "boolean")
    throw new Error("The owning widget observation is incompatible.");
  return data as unknown as Widget;
}
function preview(payload: StudioWeaverPayload | null | undefined): CheckPreview | null {
  if (payload?.typeId !== checkType || payload.schemaVersion !== "1") return null;
  const data = record(payload.data);
  return data && exact(data, ["widgetId", "hostId", "revision", "expectedVisible"]) && owner(data) && typeof data.expectedVisible === "boolean"
    ? data as unknown as CheckPreview : null;
}
function check(payload: StudioWeaverPayload | null | undefined): Check | null {
  if (payload?.typeId !== checkType || payload.schemaVersion !== "1") return null;
  const data = record(payload.data);
  return data && exact(data, ["widgetId", "hostId", "revision", "expectedVisible", "actualVisible", "passed", "ran", "callNumber"])
    && owner(data) && typeof data.expectedVisible === "boolean" && typeof data.actualVisible === "boolean" && typeof data.passed === "boolean"
    && data.ran === true && count(data.callNumber) && data.passed === (data.expectedVisible === data.actualVisible)
    ? data as unknown as Check : null;
}
function observations(value: unknown): Observations {
  const data = record(value);
  if (!data || !exact(data, ["widgetId", "hostId", "revision", "ranCount", "runs"]) || !owner(data) || !count(data.ranCount, 0)
    || !Array.isArray(data.runs) || data.runs.length !== Math.min(data.ranCount, 16)) throw new Error("The owning check observations are incompatible.");
  const total = data.ranCount;
  const rows = data.runs;
  rows.forEach((value, index) => {
    const row = record(value);
    if (!row || !exact(row, ["invocationId", "toolCallId", "toolName", "revision", "passed", "callNumber"])
      || !identifier(row.invocationId) || !identifier(row.toolCallId) || row.toolName !== checkTool || !identifier(row.revision)
      || typeof row.passed !== "boolean" || row.callNumber !== total - rows.length + index + 1)
      throw new Error("The owning check observations are incompatible.");
  });
  return data as unknown as Observations;
}
function own(target: StudioWeaverResourceTarget | null | undefined, revision: string) {
  return target?.moduleId === moduleId && target.resourceType === "home-widget" && target.resourceId === widgetId && target.revision === revision;
}
function frozenCheck(value: StudioWeaverPreparedInvocation | undefined): CheckPreview | null {
  const proposed = preview(value?.preview);
  const args = record(value?.arguments);
  return value && proposed && value.moduleId === moduleId && value.toolName === checkTool && value.toolContractVersion === "1"
    && value.executionLocation === "server" && value.client === null && value.requiresApproval && value.risk === "admin"
    && identifier(value.invocationId) && identifier(value.toolCallId) && own(value.target, proposed.revision)
    && args && exact(args, ["expectedVisible"]) && args.expectedVisible === proposed.expectedVisible ? proposed : null;
}
function acceptedCheck(props: StudioWeaverResultRendererProps): Check | null {
  const prepared = props.preparedInvocation;
  const proposed = frozenCheck(prepared);
  const result = props.toolResult;
  const receipt = result?.receipt;
  const value = check(result?.payload);
  const evidence = check(receipt?.evidence);
  return proposed && prepared && result?.succeeded === true && result.toolName === checkTool && result.toolCallId === prepared.toolCallId
    && receipt && receipt.invocationId === prepared.invocationId && receipt.scope === "noPersistedEffect"
    && value && evidence && own(receipt.before, proposed.revision) && own(receipt.after, proposed.revision)
    && value.revision === proposed.revision && value.expectedVisible === proposed.expectedVisible
    // The shared boundary can safely replace an unsupported typed code without discarding this genuine owner receipt.
    // Business meaning comes from this module's bounded payload/evidence plus independent native observation below.
    && typeof receipt.moduleCode === "string"
    && ["widgetId", "hostId", "revision", "expectedVisible", "actualVisible", "passed", "ran", "callNumber"]
      .every(field => Object.is(record(value)![field], record(evidence)![field])) ? value : null;
}
function verify(value: Check, invocation: StudioWeaverPreparedInvocation, current: Widget, observed: Observations): Verification {
  const runs = observed.runs.filter(run => run.invocationId === invocation.invocationId);
  const details = { ranCount: observed.ranCount, matchingRuns: runs.length };
  if (runs.length > 1) return { ...details, state: "duplicate", message: "The owner recorded multiple runs for this invocation. Do not repeat it; inspect the shared operation." };
  if (current.revision !== value.revision || observed.revision !== value.revision)
    return { ...details, state: "stale", message: "The current widget revision differs from this check. Its recorded run is historical evidence." };
  const run = runs[0];
  if (!run || run.toolCallId !== invocation.toolCallId || run.toolName !== checkTool || run.revision !== value.revision
    || run.passed !== value.passed || run.callNumber !== value.callNumber || current.visible !== value.actualVisible)
    return { ...details, state: "unavailable", message: "The owner could not independently correlate this check and business verdict. Do not repeat the action." };
  return { ...details, state: "confirmed", message: "The owner independently confirmed this recorded run and business verdict at the unchanged widget revision." };
}
const unavailable = "The owning service could not confirm this observation. Inspect the shared operation status before requesting another action.";

export function registerEffects(api: ElsaStudioModuleApi) {
  async function readWidget(signal?: AbortSignal) { return widget(await api.backend.http.getJson<unknown>(endpoint, { signal })); }
  async function readChecks(signal?: AbortSignal) { return observations(await api.backend.http.getJson<unknown>(`${endpoint}/checks`, { signal })); }

  function EffectsPage() {
    const [current, setCurrent] = useState<Widget | null>(null);
    const [runs, setRuns] = useState<number | null>(null);
    const [error, setError] = useState("");
    const [loading, setLoading] = useState(true);
    const [refresh, setRefresh] = useState(0);
    const [queued, setQueued] = useState("");
    const [dispatching, setDispatching] = useState(false);
    const dispatchingRef = useRef(false);
    const [selected, setSelected] = useState<string>(profiles[0].id);
    const [visible, setVisible] = useState(false);
    const [expectedVisible, setExpectedVisible] = useState(true);
    const lifetime = useRef<AbortController | null>(null);
    useEffect(() => {
      const cancellation = new AbortController();
      lifetime.current = cancellation;
      return () => { cancellation.abort(); if (lifetime.current === cancellation) lifetime.current = null; };
    }, []);
    useEffect(() => {
      const cancellation = new AbortController();
      setLoading(true); setError(""); setCurrent(null); setRuns(null);
      void Promise.all([readWidget(cancellation.signal), readChecks(cancellation.signal)]).then(([value, observed]) => {
        if (!cancellation.signal.aborted) {
          if (value.revision !== observed.revision) setError("The widget changed while reading owner observations. Refresh before requesting an action.");
          else { setCurrent(value); setRuns(observed.ranCount); }
        }
      }).catch(() => { if (!cancellation.signal.aborted) setError(unavailable); })
        .finally(() => { if (!cancellation.signal.aborted) setLoading(false); });
      return () => cancellation.abort();
    }, [refresh]);
    async function request(toolName: string, args: Record<string, unknown>, label: string) {
      const active = lifetime.current;
      if (dispatchingRef.current || loading || !current || !active || active.signal.aborted) return;
      dispatchingRef.current = true;
      setDispatching(true); setError(""); setQueued("");
      try {
        await readWidget(active.signal); // Availability only. The registered context source rereads/admit-checks the actual request.
        if (active.signal.aborted || lifetime.current !== active) return;
        api.weaver.dispatchPrompt({ message: JSON.stringify({ toolName, arguments: args }), mode: "enqueue", requestId: crypto.randomUUID(),
          source: { moduleId, actionId: toolName, label } });
        setQueued("Request queued for Weaver. Review its operation status in the assistant.");
      } catch { if (!active.signal.aborted && lifetime.current === active) setError(unavailable); }
      finally { dispatchingRef.current = false; if (!active.signal.aborted && lifetime.current === active) setDispatching(false); }
    }
    const profile = profiles.find(item => item.id === selected);
    return <section className="acme-home-settings acme-home-effects" aria-label="Acme widget effects">
      <h1>Acme widget effects</h1>
      <p>Review a classed personal visibility change or run a check against the actual widget. Weaver owns approval and execution.</p>
      {loading ? <p role="status">Reading the owning widget service…</p> : null}
      {current ? <dl className="acme-home-facts">
        <dt>Visibility</dt><dd data-testid="acme-effects-visibility">{current.visible ? "Visible" : "Hidden"}</dd>
        <dt>Revision</dt><dd data-testid="acme-effects-revision">{current.revision}</dd>
        <dt>Owner run count</dt><dd data-testid="acme-effects-ran-count">{runs}</dd>
      </dl> : null}
      <label>Visibility operation<select aria-label="Visibility operation" value={selected} onChange={event => setSelected(event.target.value)}>
        {profiles.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
      </select></label>
      <p>{profile?.description} Required classes and their current availability come from the server review.</p>
      <label><input type="checkbox" checked={visible} onChange={event => setVisible(event.target.checked)} />Show the card after the visibility change</label>
      <button type="button" disabled={!current || !profile || loading || dispatching || current.visible === visible}
        onClick={() => { if (profile) void request(profile.id, { visible }, profile.label); }}>Review classed visibility change</button>
      <label><input type="checkbox" checked={expectedVisible} onChange={event => setExpectedVisible(event.target.checked)} />Expect the card to be visible when checked</label>
      <button type="button" disabled={!current || loading || dispatching} onClick={() => void request(checkTool, { expectedVisible }, "Check Acme widget")}>Review widget check</button>
      <p>A check can run and fail its business condition. It records that it ran and leaves the widget layout and revision unchanged.</p>
      <div className="acme-home-actions">
        <button type="button" disabled={loading || dispatching} onClick={() => setRefresh(value => value + 1)}>Refresh owner observations</button>
        <a href={ownerRoute}>Personal card settings</a>
      </div>
      {queued ? <p role="status">{queued}</p> : null}
      {error ? <p role="alert">{error}</p> : null}
    </section>;
  }

  function CheckResult(props: StudioWeaverResultRendererProps) {
    const { preparedInvocation: invocation, toolResult } = props;
    const proposed = frozenCheck(invocation);
    const value = acceptedCheck(props);
    const [retry, setRetry] = useState(0);
    const [observation, setObservation] = useState<{ key: string; value: Verification } | null>(null);
    const key = JSON.stringify([invocation?.invocationId, invocation?.toolCallId, value, toolResult?.receipt?.invocationId, retry]);
    useEffect(() => {
      if (!value || !invocation) return;
      const cancellation = new AbortController();
      void Promise.all([readWidget(cancellation.signal), readChecks(cancellation.signal)]).then(([current, observed]) => {
        if (!cancellation.signal.aborted) setObservation({ key, value: verify(value, invocation, current, observed) });
      }).catch(() => { if (!cancellation.signal.aborted) setObservation({ key, value: { state: "unavailable", message: unavailable } }); });
      return () => cancellation.abort();
      // Each fixed invocation/result or explicit read refresh gets independent owner confirmation; no action is replayed.
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [key]);
    if (!toolResult) return proposed ? <section className="acme-home-result acme-home-check-result" aria-label="Review Acme widget check" data-testid="acme-check-review">
      <strong>Widget check plan</strong>
      <p>Check whether the actual personal widget is {proposed.expectedVisible ? "visible" : "hidden"} at revision {proposed.revision}.</p>
      <p>The action records a run and a business verdict. It does not change or save the widget layout.</p>
      <p>The plan alone confirms no run. Use the shared review and operation status.</p>
    </section> : <p role="status">The widget check review is unavailable or incompatible. Use the shared review status.</p>;
    if (!value || !invocation) return <section className="acme-home-result acme-home-check-result" aria-label="Acme widget check outcome">
      <strong>Widget check is unconfirmed</strong><p role="status">No matching owner receipt is available here. Use the shared operation status; a possibly completed action must not be repeated automatically.</p>
    </section>;
    const confirmed = observation?.key === key ? observation.value : null;
    return <section className="acme-home-result acme-home-check-result" aria-label="Acme widget check outcome" data-testid="acme-check-result">
      <strong>{confirmed?.state === "confirmed" ? "Widget check ran" : "Widget check owner confirmation"}</strong>
      <p>{confirmed?.state === "confirmed" ? `Business condition ${value.passed ? "passed" : "failed"}.` : "This receipt has not been independently confirmed."}</p>
      <p>The receipt describes no widget layout persistence at revision <span data-testid="acme-check-revision">{value.revision}</span>.</p>
      <p>Invocation <span data-testid="acme-check-invocation">{invocation.invocationId}</span> · recorded call <span data-testid="acme-check-call-number">{value.callNumber}</span>.</p>
      <p role="status" data-testid="acme-check-confirmation">{confirmed?.message ?? "Reading the independent widget and owner check observations…"}</p>
      {confirmed?.ranCount !== undefined ? <p>Actual owner run count: <span data-testid="acme-check-owner-count">{confirmed.ranCount}</span>. Matching recent invocation rows: {confirmed.matchingRuns}.</p> : null}
      <p>The owner returns its latest 16 rows. Missing older evidence stays unconfirmed; this view does not prove lifetime exactly-once delivery.</p>
      <p>Owner confirmation describes the physical check. The shared operation status governs delivery and recovery.</p>
      <button type="button" disabled={!confirmed} onClick={() => setRetry(value => value + 1)}>Read owner observations again</button>
    </section>;
  }

  api.navigation.add({ id: "acme.home.effects", label: "Acme widget effects", path: route, placement: "main", order: 901 });
  api.routes.add({ id: "acme.home.effects", label: "Acme widget effects", path: route, component: EffectsPage });
  api.weaver.contextProviders.add({ id: "acme.home.effects-context", moduleId, displayName: "Acme widget effects", surfaces: [route], sensitivity: "internal",
    required: true, moduleCodes: codes,
    async collect({ signal }): Promise<StudioWeaverContextCollection> {
      try {
        const value = await readWidget(signal);
        return { attachments: [{ id: `acme-home-effects:${widgetId}`, moduleId, schemaVersion: "1", source: moduleId, sourceId: widgetId,
          label: "Acme widget effects", contentType: contextKind, sensitivity: "internal", scope: route, origin: "clientSnapshot",
          summary: "Current personal widget reference for classed effects and a non-persisting check. Backend-installed guidance owns the semantics.",
          target: { moduleId, resourceType: "home-widget", resourceId: widgetId, revision: value.revision, displayName: "Acme status card", route: ownerRoute },
          content: value }], diagnostics: [] };
      } catch (failure) {
        if (signal?.aborted) throw failure;
        return { attachments: [], diagnostics: [{ contributorId: "acme.home.effects-context", code: "acme.home.effects.owner_unavailable", message: unavailable, required: true }] };
      }
    } });
  api.weaver.capabilities.add({ id: "acme.home.effects", moduleId, displayName: "Review Acme widget effects", description: "Review classed visibility and a real non-persisting widget check.",
    kind: "proposal", risk: "admin", surfaces: [route], requiredPermissions: permissions });
  api.weaver.toolSlots.add({ id: "acme.home.effects-tools", moduleId, displayName: "Acme widget effects", surfaces: [route], invocationModes: ["proposal"] });
  const targetSchema = { type: "object", required: ["moduleId", "resourceType", "resourceId", "revision"], properties: {
    moduleId: { const: moduleId }, resourceType: { const: "home-widget" }, resourceId: { const: widgetId }, revision: { type: "string", minLength: 1 } } };
  const visibilitySchema = { type: "object", required: ["widgetId", "revision", "visible", "beforeVisible", "hostId"], properties: {
    widgetId: { const: widgetId }, revision: { type: "string", minLength: 1 }, visible: { type: "boolean" }, beforeVisible: { type: "boolean" }, hostId: { type: "string", minLength: 1 } }, additionalProperties: false };
  for (const profile of profiles) api.weaver.toolContracts.add({ id: profile.id, slotId: "acme.home.effects-tools", moduleId, displayName: profile.label,
    description: profile.description, surfaces: [route], contractVersion: "1", executionLocation: "server", risk: profile.risk, requiredPermissions: permissions, moduleCodes: codes,
    inputSchema: { type: "object", required: ["visible"], properties: { visible: { type: "boolean" } }, additionalProperties: false },
    resourceTargetSchema: targetSchema, resultSchema: visibilitySchema, invocationModes: ["proposal"], resultRendererIds: ["acme.home.widget-result"] });
  api.weaver.toolContracts.add({ id: checkTool, slotId: "acme.home.effects-tools", moduleId, displayName: "Check Acme widget", surfaces: [route],
    description: "Evaluate actual widget visibility, record a run and leave the native widget revision unchanged.", contractVersion: "1", executionLocation: "server", risk: "admin",
    requiredPermissions: permissions, moduleCodes: codes, canPerformWithoutPersistence: true,
    inputSchema: { type: "object", required: ["expectedVisible"], properties: { expectedVisible: { type: "boolean" } }, additionalProperties: false }, resourceTargetSchema: targetSchema,
    resultSchema: { type: "object", required: ["widgetId", "revision", "hostId", "expectedVisible", "actualVisible", "passed", "ran", "callNumber"], properties: {
      widgetId: { const: widgetId }, revision: { type: "string", minLength: 1 }, hostId: { type: "string", minLength: 1 }, expectedVisible: { type: "boolean" },
      actualVisible: { type: "boolean" }, passed: { type: "boolean" }, ran: { const: true }, callNumber: { type: "integer", minimum: 1 } }, additionalProperties: false },
    invocationModes: ["proposal"], resultRendererIds: [checkRenderer] });
  api.weaver.resultRenderers.add({ id: checkRenderer, moduleId, displayName: "Acme widget check", resourceTypes: ["home-widget"], resultTypes: [checkType], schemaVersions: ["1"], component: CheckResult });
}
