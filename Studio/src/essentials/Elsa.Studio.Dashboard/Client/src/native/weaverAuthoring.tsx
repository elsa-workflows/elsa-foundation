import React, { useEffect, useMemo, useState, useSyncExternalStore } from "react";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { evaluateBackendRequirements, studioBackendCapabilitiesChangedEvent, type ElsaStudioModuleApi, type StudioAgentContextAttachment, type StudioAgentContextCollection, type StudioAgentResultRendererProps, type StudioAgentSurface, type StudioAgentToolResult } from "@elsa-workflows/studio-sdk";
import type { DashboardAuthoringController, DashboardAuthoringProposal, DashboardAuthoringSnapshot, DashboardAuthoringTarget } from "./authoring";
import { sameAuthoringTarget } from "./authoring";
import { DashboardApi } from "./api";
import { dashboardActorScope } from "./requestScheduler";
import { ResultView } from "./ResultView";
import type { QueryResult, QuestionDefinition } from "./types";
import { accessLost, errorMessage } from "./types";
import { useRequestLifetime } from "./useRequestLifetime";

const moduleId = "Elsa.Studio.Dashboard";
const requirements = [{ id: "elsa.api.studio-dashboard-authoring", contractVersion: "1" }];
const surfaces = ["/questions", "/dashboards"];
type Bound = Pick<DashboardAuthoringSnapshot, "client" | "target" | "workingRevision">;
type Proposal = Bound & { definition: QuestionDefinition; definitionFingerprint: string; interpretation?: string };
type Clarification = Bound & { clarification: { question: string; choices: { id: string; label: string; description: string }[] } };
type Intent = { origin: Bound; definition?: QuestionDefinition; confirmedInterpretation?: string };
type Pending = { requestId: string; origin: Bound };
type Runtime = { api: ElsaStudioModuleApi; controller: DashboardAuthoringController; native: DashboardApi; intents: Map<string, Intent>; matches(bound: Bound): DashboardAuthoringSnapshot | undefined; dispatch(bound: Bound, message: string, confirmation?: string, definition?: QuestionDefinition): { accepted: boolean; reason?: string }; status(): { available: boolean; reason?: string } };

function isRecord(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === "object" && !Array.isArray(value); }
function boundData(value: unknown): Bound | undefined {
  if (!isRecord(value) || !isRecord(value.client) || !isRecord(value.target) || typeof value.workingRevision !== "string") return;
  const client = value.client; const target = value.target;
  if (typeof client.clientId !== "string" || typeof client.generationId !== "string" || target.moduleId !== moduleId || !["question", "question-draft"].includes(String(target.resourceType)) || typeof target.resourceId !== "string" || typeof target.revision !== "string") return;
  return { client: { clientId: client.clientId, generationId: client.generationId }, target: { moduleId, resourceType: target.resourceType as DashboardAuthoringTarget["resourceType"], resourceId: target.resourceId, revision: target.revision }, workingRevision: value.workingRevision };
}
function questionData(value: unknown): value is QuestionDefinition {
  return isRecord(value) && isRecord(value.query) && value.query.schemaVersion === 1 && isRecord(value.query.dataset) && typeof value.query.dataset.id === "string" && isRecord(value.visualization) && Array.isArray(value.visualization.y) && isRecord(value.parameterValues);
}
function sameBinding(left: Bound, right: Bound) {
  return left.client.clientId === right.client.clientId && left.client.generationId === right.client.generationId && left.workingRevision === right.workingRevision && sameAuthoringTarget(left.target, right.target);
}
function matchesSurface(snapshot: DashboardAuthoringSnapshot, surface: StudioAgentSurface) {
  const route = surface.route.split(/[?#]/, 1)[0];
  if (!route.startsWith("/questions/")) return false;
  const id = decodeURIComponent(route.slice("/questions/".length));
  if (id === "new" ? snapshot.target.resourceType !== "question-draft" : snapshot.target.resourceType !== "question" || snapshot.target.resourceId !== id) return false;
  return (!surface.resourceType || surface.resourceType === snapshot.target.resourceType) && (!surface.resourceId || surface.resourceId === snapshot.target.resourceId);
}
function attachment(snapshot: DashboardAuthoringSnapshot, intent?: Intent): StudioAgentContextAttachment {
  const content = {
    target: snapshot.target, client: snapshot.client, workingRevision: snapshot.workingRevision,
    definition: intent?.definition ?? snapshot.definition, name: snapshot.name,
    dirty: snapshot.dirty || Boolean(intent?.definition), ...(intent?.confirmedInterpretation ? { confirmedInterpretation: intent.confirmedInterpretation } : {})
  };
  return { id: `dashboard-draft:${snapshot.generationId}`, source: "studio", sourceId: snapshot.target.resourceId, label: "Dashboard authoring draft", contentType: "dashboards.authoring", sensitivity: "internal", scope: "dashboard-question", summary: "The current declarative question draft; no query results are attached.", content, moduleId, schemaVersion: "1", target: snapshot.target, origin: "clientSnapshot", client: snapshot.client, agentScopes: surfaces };
}

/** Module contributions only. Weaver retains conversation, provider, policy and cancellation ownership. */
export function registerDashboardWeaver(api: ElsaStudioModuleApi, controller: DashboardAuthoringController) {
  if (!api.agent) return;
  const endpoint = api.backend;
  const session = api.backendSession;
  const intents = new Map<string, Intent>();
  let pending: Pending | undefined;
  const status = () => {
    if (api.backend !== endpoint || session?.disposed || session && api.backendSession?.key !== session.key) return { available: false, reason: "The originating backend connection is unavailable." };
    if (!api.backendSession) return { available: false, reason: "Dashboard authoring capabilities have not been discovered." };
    const support = evaluateBackendRequirements(api.backendSession, requirements);
    if (support.state !== "available") return { available: false, reason: support.reason || "This backend does not support Dashboard authoring." };
    if (!api.agent.providerAvailability.get()) return { available: false, reason: "Weaver's provider is unavailable. Ordinary editing, saving and refresh remain available." };
    return { available: true };
  };
  const matches = (bound: Bound) => {
    if (!session || session.disposed || api.backend !== endpoint || api.backendSession?.key !== session.key || evaluateBackendRequirements(api.backendSession, requirements).state !== "available") return;
    const snapshot = controller.snapshots.get(bound.client.generationId);
    return snapshot && sameBinding(snapshot, bound) ? snapshot : undefined;
  };
  const dispatch: Runtime["dispatch"] = (bound, message, confirmation, definition) => {
    const availability = status(); const snapshot = matches(bound);
    if (!availability.available) return { accepted: false, reason: availability.reason };
    if (!snapshot || !snapshot.canEdit) return { accepted: false, reason: "The exact originating question changed, closed or became read-only. Nothing was sent." };
    if (!message.trim() || message.length > 4000) return { accepted: false, reason: "Enter a question of at most 4,000 characters." };
    if (pending) return { accepted: false, reason: "The previous bound question is still being collected. Try again after it finishes." };
    if (definition) intents.set(snapshot.generationId, { ...intents.get(snapshot.generationId), origin: structuredClone(bound), definition: structuredClone(definition) });
    if (confirmation) {
      if (confirmation.length > 2000) return { accepted: false, reason: "This clarification exceeds the supported confirmation bound." };
      intents.set(snapshot.generationId, { ...intents.get(snapshot.generationId), origin: structuredClone(bound), confirmedInterpretation: confirmation });
    }
    const requestId = `dashboards-authoring:${crypto.randomUUID()}`;
    pending = { requestId, origin: structuredClone(bound) };
    try {
      // The context contribution supplies exactly one attachment. An explicit duplicate would
      // violate generic collection and the backend's single-draft admission rule.
      api.agent.dispatchPrompt({ message: message.trim(), mode: "enqueue", requestId, source: { moduleId, actionId: "dashboards.ask", label: "Author a dashboard question" } });
      return { accepted: true };
    } catch (cause) { pending = undefined; return { accepted: false, reason: errorMessage(cause) }; }
  };
  const runtime: Runtime = { api, controller, native: new DashboardApi(endpoint, session?.identity ? dashboardActorScope(session.identity) : undefined), intents, matches, dispatch, status };
  controller.subscribe(() => {
    for (const [id, intent] of intents) if (!matches(intent.origin)) intents.delete(id);
    // Retain a dispatched target until collection (or its correlated cancellation) consumes it.
    // Clearing it on navigation would silently retarget the original prompt to another editor.
  });
  api.agent.onPromptResult(result => { if (pending?.requestId === result.requestId) pending = undefined; });
  controller.weaver = {
    status,
    subscribe(listener) {
      const stopProvider = api.agent.providerAvailability.subscribe(() => listener());
      const stopDraft = controller.subscribe(listener);
      window.addEventListener(studioBackendCapabilitiesChangedEvent, listener);
      return () => { stopProvider(); stopDraft(); window.removeEventListener(studioBackendCapabilitiesChangedEvent, listener); };
    },
    request(generationId, message) {
      const snapshot = controller.snapshots.get(generationId);
      return snapshot ? dispatch(snapshot, message) : { accepted: false, reason: "The question editor is unavailable." };
    }
  };
  api.agent.contextProviders.add({ id: "dashboards.authoring", moduleId, displayName: "Dashboard question draft", surfaces, sensitivity: "internal", required: true, requiresBackend: requirements,
    async collect(request): Promise<StudioAgentContextCollection> {
      const origin = pending?.origin;
      try {
        request.signal?.throwIfAborted();
        const availability = status();
        if (!availability.available) return diagnostic("dashboards.context.unavailable", availability.reason || "Dashboard authoring is unavailable.");
        const candidates = [...controller.snapshots.values()].filter(snapshot => matchesSurface(snapshot, request.surface));
        if (origin && (!matches(origin) || !candidates.some(snapshot => sameBinding(snapshot, origin)))) return diagnostic("dashboards.context.target_changed", "The requested question changed or navigation moved to another resource. Ask again from the intended editor.");
        if (candidates.length > 1) return diagnostic("dashboards.context.ambiguous", "Select one exact active question editor before asking Weaver.");
        const selected = origin ? candidates.find(snapshot => sameBinding(snapshot, origin)) : candidates[0];
        if (!selected) return { attachments: [], diagnostics: [] };
        const intent = intents.get(selected.generationId);
        const context = attachment(selected, intent && sameBinding(intent.origin, selected) ? intent : undefined);
        if (new TextEncoder().encode(JSON.stringify(context.content)).length > 24576) return diagnostic("dashboards.context.too_large", "This draft exceeds the authoring context bound. Simplify it or continue with ordinary controls.");
        request.signal?.throwIfAborted();
        return { attachments: [context], diagnostics: [] };
      } finally { pending = undefined; }
    }
  });
  api.agent.capabilities.add({ id: "dashboards.authoring", moduleId, displayName: "Author dashboard questions", description: "Compose supported queries and presentations; preview and Save remain explicit native actions.", kind: "answer", risk: "read-only", surfaces, requiredPermissions: ["studio/questions:read"], requiresBackend: requirements });
  api.agent.promptStarters.add({ id: "dashboards.ask", moduleId, label: "Create a new dashboard question", prompt: "Help me compose a new supported dashboard question. Discover current module datasets and clarify consequential metric or time meanings. Propose a validated query; do not invent or request source results, and do not save.", surfaces: ["/questions"], requiredCapabilities: ["dashboards.authoring"], requiresBackend: requirements });
  api.agent.resultRenderers.add({ id: "dashboards.question-preview", moduleId, displayName: "Proposed dashboard question", resultTypes: ["dashboards.question-preview"], schemaVersions: ["1"], requiresBackend: requirements, component: props => <ProposalResult {...props} runtime={runtime} /> });
  api.agent.resultRenderers.add({ id: "dashboards.clarification", moduleId, displayName: "Clarify dashboard question", resultTypes: ["dashboards.clarification"], schemaVersions: ["1"], requiresBackend: requirements, component: props => <ClarificationResult {...props} runtime={runtime} /> });
  api.agent.resultRenderers.add({ id: "dashboards.dataset-catalog", moduleId, displayName: "Dashboard dataset discovery", resultTypes: ["dashboards.dataset-catalog"], schemaVersions: ["1"], requiresBackend: requirements, component: () => <p>Dataset support was discovered for this authoring turn. Use the native dataset selector to inspect currently authorized fields and metrics.</p> });
}

function diagnostic(code: string, message: string): StudioAgentContextCollection { return { attachments: [], diagnostics: [{ contributorId: "dashboards.authoring", code, message, required: true }] }; }
function useLiveSnapshot(runtime: Runtime, bound?: Bound) {
  const subscribe = useMemo(() => (listener: () => void) => {
    const stop = runtime.controller.subscribe(listener);
    window.addEventListener(studioBackendCapabilitiesChangedEvent, listener);
    return () => { stop(); window.removeEventListener(studioBackendCapabilitiesChangedEvent, listener); };
  }, [runtime]);
  return useSyncExternalStore(subscribe, () => bound ? runtime.matches(bound) : undefined, () => undefined);
}
function useRuntimeStatus(runtime: Runtime) {
  const subscribe = useMemo(() => (listener: () => void) => runtime.controller.weaver?.subscribe(listener) ?? (() => undefined), [runtime]);
  const value = useSyncExternalStore(subscribe, () => JSON.stringify(runtime.status()), () => "{}");
  return JSON.parse(value) as { available: boolean; reason?: string };
}
function payload(result: StudioAgentToolResult | undefined, type: string): unknown {
  return result?.succeeded && result.payload?.typeId === type && result.payload.schemaVersion === "1" ? result.payload.data : undefined;
}
function parseProposal(value: unknown): Proposal | undefined {
  const bound = boundData(value);
  if (!bound || !isRecord(value) || !questionData(value.definition) || typeof value.definitionFingerprint !== "string" || value.preview != null) return;
  return { ...bound, definition: value.definition, definitionFingerprint: value.definitionFingerprint, interpretation: typeof value.interpretation === "string" ? value.interpretation : undefined };
}

function ProposalResult(props: StudioAgentResultRendererProps & { runtime: Runtime }) {
  const { runtime } = props;
  const toolResult = props.toolResult;
  const proposal = useMemo(() => parseProposal(payload(toolResult, "dashboards.question-preview")), [toolResult]);
  const snapshot = useLiveSnapshot(runtime, proposal);
  const availability = useRuntimeStatus(runtime);
  const lifetime = useRequestLifetime();
  const [result, setResult] = useState<QueryResult>();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [applied, setApplied] = useState(false);
  useEffect(() => {
    if (!snapshot || !proposal) { lifetime.cancel("preview"); setResult(undefined); setBusy(false); return; }
    const previous = runtime.intents.get(snapshot.generationId);
    runtime.intents.set(snapshot.generationId, { origin: structuredClone(proposal), definition: structuredClone(proposal.definition), confirmedInterpretation: previous?.confirmedInterpretation });
  }, [snapshot, proposal, runtime, lifetime]);
  if (!proposal) return <p role="status">This proposal is unsupported or malformed. Request a fresh validated question.</p>;
  if (!snapshot) return <p role="status">{applied ? "Applied to the native draft. Use its Undo or Save controls; nothing was saved automatically." : "The originating question changed or closed. Protected preview content has been cleared; request a fresh proposal."}</p>;
  const preview = async () => {
    const request = lifetime.begin("preview"); setBusy(true); setError(undefined); setResult(undefined);
    try {
      const response = proposal.target.resourceType === "question" ? await runtime.native.savedPreview(proposal.target.resourceId, proposal.target.revision, proposal.definition, request.signal) : await runtime.native.preview(proposal.definition, request.signal);
      if (request.current() && runtime.matches(proposal)) setResult(response);
    } catch (cause) { if (request.current()) { setError(errorMessage(cause)); if (accessLost(cause)) setResult(undefined); } }
    finally { if (request.current()) setBusy(false); request.finish(); }
  };
  return <section className="named-weaver-proposal" aria-label="Proposed dashboard question"><p>{proposal.interpretation || "A validated declarative question proposal."}</p><p>Preview reads data as you, outside the model transcript. Nothing has been applied or saved.</p>
    <div className="named-actions"><StudioButton disabled={busy} onClick={() => void preview()}>Preview proposed question</StudioButton>{busy ? <StudioButton onClick={() => { lifetime.cancel("preview"); setBusy(false); setResult(undefined); setError("Native preview cancelled."); }}>Cancel proposed preview</StudioButton> : null}
      <StudioButton disabled={busy || !result || result.outcome !== "succeeded" || !snapshot.canEdit} onClick={() => {
        const request = lifetime.begin("use"); setBusy(true); setError(undefined); setResult(undefined);
        // The server fingerprint is SHA-256. The native controller accepts a different optional
        // full-request fingerprint, so deliberately omit that field rather than substituting it.
        const nativeProposal: DashboardAuthoringProposal = { client: proposal.client, target: proposal.target, workingRevision: proposal.workingRevision, definition: proposal.definition, interpretation: proposal.interpretation };
        void runtime.controller.useProposal(proposal.client.generationId, nativeProposal).then(outcome => { if (!request.current()) return; if (outcome.outcome === "applied") { setApplied(true); setResult(undefined); } else setError(outcome.summary); }).catch(cause => { if (request.current()) setError(errorMessage(cause)); }).finally(() => { if (request.current()) setBusy(false); request.finish(); });
      }}>Use this question</StudioButton>
      <StudioButton disabled={busy || !availability.available} onClick={() => {
        const current = lifetime.capture(); void runtime.api.dialogs.prompt({ title: "Refine proposed question", message: "Describe the query or presentation change. Existing source result values will not be sent to the model." }).then(message => { if (!message?.trim() || !current()) return; const sent = runtime.dispatch(proposal, message, undefined, proposal.definition); if (!sent.accepted) setError(sent.reason); });
      }}>Refine proposal</StudioButton></div>
    {error ? <p role="alert">{error}</p> : null}{result ? <ResultView result={result} visualization={proposal.definition.visualization} /> : null}
  </section>;
}

function ClarificationResult(props: StudioAgentResultRendererProps & { runtime: Runtime }) {
  const value = payload(props.toolResult, "dashboards.clarification");
  const bound = useMemo(() => boundData(value), [value]);
  const snapshot = useLiveSnapshot(props.runtime, bound);
  const availability = useRuntimeStatus(props.runtime);
  const [error, setError] = useState<string>();
  if (!bound || !isRecord(value) || !isRecord(value.clarification) || typeof value.clarification.question !== "string" || !Array.isArray(value.clarification.choices)) return <p role="status">This clarification is unsupported. Ask again from the question editor.</p>;
  const clarification = value as unknown as Clarification;
  if (!snapshot) return <p role="status">The originating editor changed or closed. This clarification cannot confirm another question.</p>;
  return <section aria-label="Clarify dashboard question"><p>{clarification.clarification.question}</p><div className="named-actions">{clarification.clarification.choices.filter(choice => choice && typeof choice.id === "string" && typeof choice.label === "string" && typeof choice.description === "string").map(choice => <StudioButton key={choice.id} disabled={!availability.available} onClick={() => { const confirmation = `${clarification.clarification.question} ${choice.label}. ${choice.description}`; const sent = props.runtime.dispatch(bound, `I choose ${choice.label}. ${choice.description}`, confirmation); if (!sent.accepted) setError(sent.reason); }}>{choice.label}</StudioButton>)}</div>{error ? <p role="alert">{error}</p> : null}</section>;
}

export function DashboardAskWeaver({ controller, generationId }: { controller?: DashboardAuthoringController; generationId: string }) {
  const [question, setQuestion] = useState("");
  const [error, setError] = useState<string>();
  const subscribe = useMemo(() => (listener: () => void) => controller?.weaver?.subscribe(listener) ?? (() => undefined), [controller]);
  const status = useSyncExternalStore(subscribe, () => JSON.stringify(controller?.weaver?.status() ?? { available: false, reason: "Dashboard conversational authoring is unavailable. Ordinary editing remains available." }), () => "{}");
  const availability = JSON.parse(status) as { available: boolean; reason?: string };
  const subscribeDraft = useMemo(() => (listener: () => void) => controller?.subscribe(listener) ?? (() => undefined), [controller]);
  const snapshot = useSyncExternalStore(subscribeDraft, () => controller?.snapshots.get(generationId), () => undefined);
  return <section className="named-weaver-ask" aria-label="Ask Weaver"><h3>Ask Weaver</h3><p>Describe a supported data question. Weaver helps clarify and propose it; preview, Use and Save stay with you.</p><form className="named-search" onSubmit={event => { event.preventDefault(); const sent = controller?.weaver?.request(generationId, question); setError(sent?.accepted ? undefined : sent?.reason ?? "Dashboard authoring is unavailable."); }}><label>Your question<input value={question} maxLength={4000} onChange={event => setQuestion(event.target.value)} placeholder="Show workflows whose failure rate increased this week" /></label><StudioButton type="submit" disabled={!availability.available || !snapshot?.canEdit || !question.trim()}>Ask Weaver</StudioButton></form>{!availability.available ? <p role="status">{availability.reason}</p> : snapshot && !snapshot.canEdit ? <p role="status">This definition is read-only. Create a personal question to author a new draft.</p> : null}{error ? <p role="alert">{error}</p> : null}</section>;
}
