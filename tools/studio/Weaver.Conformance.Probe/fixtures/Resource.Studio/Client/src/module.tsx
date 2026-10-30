import React, { useEffect, useState } from "react";
import {
  registerHomeWidget,
  type ElsaStudioModuleApi,
  type StudioAgentContextAttachment,
  type StudioAgentContextCollection,
  type StudioAgentPayload,
  type StudioAgentPromptRequest,
  type StudioAgentResultRendererProps
} from "@elsa-workflows/studio-sdk";
import "./module.css";

const moduleId = "Acme.Home";
const widgetId = "acme.home:status";
const hostId = "weaver-conformance3438";
const route = "/qualification/acme-home";
const ownerEndpoint = "/_qualification/acme/home-widget";
const surfaces = [route, "/", "/dashboard", "/overview"];
const readTool = "acme_home_read_widget";
const writeTool = "acme_home_set_visibility";
const readResultType = "acme.home.widget-state";
const changeResultType = "acme.home.widget-visibility";

interface WidgetSnapshot { widgetId: string; revision: string; visible: boolean }
interface VisibilityPayload extends WidgetSnapshot { beforeVisible?: boolean }

function record(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : null;
}

function snapshot(value: unknown): WidgetSnapshot {
  const data = record(value);
  if (!data || data.widgetId !== widgetId || typeof data.visible !== "boolean" || typeof data.revision !== "string"
    || data.revision.length === 0 || data.revision.length > 256 || data.hostId !== undefined && data.hostId !== hostId) {
    throw new Error("The owning service did not return the exact Acme status card and revision.");
  }
  return { widgetId, revision: data.revision, visible: data.visible };
}

function resultPayload(payload: StudioAgentPayload | null | undefined): VisibilityPayload | null {
  if (!payload || payload.schemaVersion !== "1" || ![readResultType, changeResultType].includes(payload.typeId)) return null;
  const data = record(payload.data);
  try {
    const value = snapshot(data);
    if (data?.beforeVisible !== undefined && typeof data.beforeVisible !== "boolean") return null;
    return { ...value, beforeVisible: typeof data?.beforeVisible === "boolean" ? data.beforeVisible : undefined };
  } catch { return null; }
}

const visibility = (visible: boolean) => visible ? "Visible" : "Hidden";
const errorMessage = (error: unknown) => error instanceof Error ? error.message : "The owning widget service is unavailable.";

export function register(api: ElsaStudioModuleApi) {
  // Module-private observations; neither the Home layout nor unrelated widget options enter this state.
  let current: WidgetSnapshot | null = null;
  let refreshGeneration = 0;
  const listeners = new Set<(value: WidgetSnapshot) => void>();
  async function refresh(signal?: AbortSignal) {
    const generation = ++refreshGeneration;
    const value = snapshot(await api.backend.http.getJson<unknown>(ownerEndpoint, { signal }));
    if (generation === refreshGeneration) {
      current = value;
      for (const listener of listeners) listener(value);
    }
    return value;
  }

  function attachment(value: WidgetSnapshot, id: string): StudioAgentContextAttachment {
    return {
      id, moduleId, schemaVersion: "1", source: moduleId, sourceId: widgetId,
      label: "Acme status card", contentType: "acme.home.widget", sensitivity: "internal", scope: route,
      summary: `Acme status card is ${visibility(value.visible).toLowerCase()} at revision ${value.revision}.`,
      origin: "clientSnapshot",
      target: { moduleId, resourceType: "home-widget", resourceId: widgetId, revision: value.revision, displayName: "Acme status card", route },
      content: { widgetId, visible: value.visible, revision: value.revision }
    };
  }

  async function dispatch(toolName: string, args: Record<string, unknown>, label: string) {
    await refresh();
    const requestId = crypto.randomUUID();
    const request: StudioAgentPromptRequest = {
      message: JSON.stringify({ toolName, arguments: args }), mode: "enqueue", requestId,
      source: { moduleId, actionId: toolName, label }
    };
    api.agent.dispatchPrompt(request);
  }

  function useWidgetObservation() {
    const [value, setValue] = useState(current);
    const [error, setError] = useState("");
    useEffect(() => {
      const cancellation = new AbortController();
      listeners.add(setValue);
      void refresh(cancellation.signal).catch(failure => { if (!cancellation.signal.aborted) setError(errorMessage(failure)); });
      return () => { listeners.delete(setValue); cancellation.abort(); };
    }, []);
    return { value, error, setError };
  }

  function StatusCard() {
    return <section className="acme-home-card" aria-label="Acme status card" data-testid="acme-home-widget">
      <p>Your Acme status card is available on Home.</p>
      <a href={route}>Review Acme status card visibility</a>
    </section>;
  }

  function SettingsPage() {
    const { value, error, setError } = useWidgetObservation();
    const [desiredVisible, setDesiredVisible] = useState(false);
    const [dispatching, setDispatching] = useState(false);
    async function request(toolName: string, args: Record<string, unknown>, label: string) {
      if (dispatching) return;
      setDispatching(true); setError("");
      try { await dispatch(toolName, args, label); }
      catch (failure) { setError(errorMessage(failure)); }
      finally { setDispatching(false); }
    }
    return <section className="acme-home-settings" aria-label="Acme status card settings">
      <h1>Acme status card</h1>
      <p>Read the current personal setting or review a visibility change. Settings remain available when the card is hidden.</p>
      {value ? <dl className="acme-home-facts">
        <dt>Visibility</dt><dd data-testid="acme-home-visibility">{visibility(value.visible)}</dd>
        <dt>Revision</dt><dd data-testid="acme-home-revision">{value.revision}</dd>
      </dl> : <p role="status">The personal widget setting is not available.</p>}
      <label><input type="checkbox" checked={desiredVisible} onChange={event => setDesiredVisible(event.target.checked)} />Show Acme status card on Home</label>
      <div className="acme-home-actions">
        <button type="button" disabled={!value || dispatching} onClick={() => void request(readTool, {}, "Read Acme status card")}>Read with Weaver</button>
        <button type="button" disabled={!value || dispatching} onClick={() => void request(writeTool, { visible: desiredVisible }, "Review Acme status card visibility")}>Review visibility change</button>
        <a href="/dashboard">Reload Home</a>
      </div>
      {error ? <p role="alert">{error}</p> : null}
    </section>;
  }

  function WidgetResult({ preparedInvocation, toolResult }: StudioAgentResultRendererProps) {
    const payload = toolResult?.payload ?? preparedInvocation?.preview;
    const value = resultPayload(payload);
    const target = toolResult?.receipt?.after ?? preparedInvocation?.target;
    const ownerMatches = !target || target.moduleId === moduleId && target.resourceType === "home-widget" && target.resourceId === widgetId;
    const receipt = toolResult?.receipt;
    const serverObservation = toolResult?.succeeded === true && receipt?.scope === "serverPersisted" && ownerMatches;
    const [observed, setObserved] = useState<WidgetSnapshot | null>(null);
    const [error, setError] = useState("");
    useEffect(() => {
      if (!value || !ownerMatches || !toolResult?.succeeded || payload?.typeId === changeResultType && !serverObservation) return;
      const cancellation = new AbortController();
      void refresh(cancellation.signal).then(next => { if (!cancellation.signal.aborted) setObserved(next); })
        .catch(failure => { if (!cancellation.signal.aborted) setError(errorMessage(failure)); });
      return () => cancellation.abort();
      // The owning result is fixed for one tool call; a new receipt starts a fresh owner observation.
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [toolResult?.toolCallId, receipt?.invocationId, receipt?.after.revision]);
    if (!value || !ownerMatches) return <p role="status">Acme widget details are unavailable or incompatible. Use the shared review status.</p>;
    const confirmed = serverObservation && observed?.visible === value.visible && observed.revision === value.revision;
    return <section className="acme-home-result" aria-label="Acme status card result" data-testid="acme-home-result">
      <strong>{toolResult ? toolResult.succeeded ? "Acme status card result" : "Visibility change was not confirmed" : "Review visibility change"}</strong>
      <p>{payload?.typeId === readResultType ? `The owning resource reports ${visibility(value.visible).toLowerCase()}.`
        : `${value.beforeVisible === undefined ? "Set visibility" : `Change visibility from ${visibility(value.beforeVisible).toLowerCase()}`} to ${visibility(value.visible).toLowerCase()}.`}</p>
      <p>Widget: Acme status card · revision <span data-testid="acme-home-result-revision">{value.revision}</span></p>
      {!toolResult ? <p>This changes your personal Home layout only after approval and application.</p> : null}
      {confirmed ? <p role="status">The owning service independently confirmed the saved visibility.</p> : null}
      {observed && !confirmed ? <p>Current owner visibility: {visibility(observed.visible)} · revision {observed.revision}.</p> : null}
      {confirmed ? <a href="/dashboard" data-testid="acme-home-reload">Reload Home to observe the saved visibility</a> : null}
      {error ? <p role="alert">{error}</p> : null}
    </section>;
  }

  registerHomeWidget(api, { id: widgetId, moduleId, title: "Acme status card", description: "An independently contributed personal status card.",
    defaultVisible: false, defaultSize: "S", supportedSizes: ["S", "M"], component: StatusCard });
  api.navigation.add({ id: "acme.home.settings", label: "Acme status card", path: route, placement: "main", order: 900 });
  api.routes.add({ id: "acme.home.settings", label: "Acme status card", path: route, component: SettingsPage });
  api.agent.contextProviders.add({ id: "acme.home.context", moduleId, displayName: "Acme status card", surfaces, sensitivity: "internal",
    async collect({ signal }): Promise<StudioAgentContextCollection> {
      try { return { attachments: [attachment(await refresh(signal), `acme-home:${widgetId}`)], diagnostics: [] }; }
      catch (failure) {
        if (signal?.aborted) throw failure;
        const status = record(failure)?.status;
        if (status === 403 || status === 404 || status === 409) return { attachments: [], diagnostics: [{ contributorId: "acme.home.context", code: "acme.home.unavailable", message: errorMessage(failure), required: true }] };
        throw failure;
      }
    } });
  api.agent.capabilities.add({ id: "acme.home.read", moduleId, displayName: "Read Acme status card", description: "Read this card's current personal visibility and revision.", kind: "answer", risk: "read-only", surfaces });
  api.agent.capabilities.add({ id: "acme.home.visibility", moduleId, displayName: "Review Acme status card visibility", description: "Review a visibility change to this personal card.", kind: "proposal", risk: "review-required", surfaces });
  api.agent.toolSlots.add({ id: "acme.home.tools", moduleId, displayName: "Acme status card", surfaces, invocationModes: ["read-only", "proposal"] });
  for (const [id, displayName, description, risk, invocationModes, inputSchema] of [
    [readTool, "Read Acme status card", "Read the owning personal widget setting.", "read-only", ["read-only"], { type: "object", additionalProperties: false }],
    [writeTool, "Set Acme status card visibility", "Prepare an exact reviewed personal visibility change.", "review-required", ["proposal"], { type: "object", required: ["visible"], properties: { visible: { type: "boolean" } }, additionalProperties: false }]
  ] as const) api.agent.toolContracts.add({ id, slotId: "acme.home.tools", moduleId, displayName, description, surfaces, contractVersion: "1", executionLocation: "server", risk,
    inputSchema, resourceTargetSchema: { type: "object" }, resultSchema: { type: "object" }, invocationModes: [...invocationModes], resultRendererIds: ["acme.home.widget-result"] });
  api.agent.resultRenderers.add({ id: "acme.home.widget-result", moduleId, displayName: "Acme status card", resultTypes: [readResultType, changeResultType], schemaVersions: ["1"], component: WidgetResult });
}
