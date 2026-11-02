import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ElsaStudioModuleApi, StudioAgentContextAttachment, StudioAgentContributionDiagnostic, StudioAgentMode, StudioAgentPromptRequest, StudioAgentSurface, StudioAgentToolResult } from "../../sdk";
import { AgentExecutionError, classifyAutonomyFailure, createAgentClient, getAgentErrorCode, POLICY_CHANGED, type AgentClient } from "../agent/agentClient";
import { collectAgentContext } from "../agent/agentContext";
import { getActiveAgentCapabilities, getActivePromptStarters } from "../agent/agentContributions";
import { getAgentErrorMessage } from "../agent/agentErrors";
import { subscribeAgentSessionStream, type AgentStreamSubscription } from "../agent/agentStream";
import { createClientInvocationRunner } from "../agent/clientInvocationRunner";
import type {
  AgentAcceptedPolicy,
  AgentActionProposal,
  AgentActionProposalPayload,
  AgentClientAcknowledgement,
  AgentAutonomyMode,
  AgentBootstrapResponse,
  AgentMessageViewModel,
  AgentPlanStep,
  AgentProviderDiagnostics,
  AgentSessionDetailResponse,
  AgentSessionPolicy,
  AgentStreamEvent
} from "../agent/agentTypes";

export type WeaverChatState = "idle" | "streaming" | "awaiting-review" | "awaiting-confirmation";
// The Weaver "auto-apply" control selects the session's server-side autonomy mode.
export type WeaverAutoApplyMode = AgentAutonomyMode;

const ALL_AUTONOMY_MODES: readonly WeaverAutoApplyMode[] = ["manual", "auto-read-only", "full-auto"];

/** Display names of the autonomy modes; the probe driver and tests select menu items by these names. */
export const WEAVER_AUTO_APPLY_LABELS: Readonly<Record<WeaverAutoApplyMode, string>> = {
  "manual": "Manual review",
  "auto-read-only": "Auto-apply low-risk",
  "full-auto": "Autopilot"
};

/**
 * Where the displayed auto-apply mode comes from:
 * - `requested`: no conversation yet; the selection is the create-time request.
 * - `acknowledged`: the server acknowledged the displayed mode for the current conversation.
 * - `pending`: a conditional change is in flight; the displayed (acknowledged) mode is still in effect.
 * - `unresolved`: a change was sent but its outcome is unknown; dependent prompts stay blocked until it resolves.
 * - `unsupported`: this conversation cannot change, or cannot report, its autonomy.
 */
export type WeaverAutoApplyState = "requested" | "acknowledged" | "pending" | "unresolved" | "unsupported";

/** The latest disposition of an auto-apply selection, for the surface to explain. */
export type WeaverAutoApplyNotice =
  | { kind: "clamped"; requestedMode: WeaverAutoApplyMode; acknowledgedMode: WeaverAutoApplyMode }
  | { kind: "reconciled"; acknowledgedMode: WeaverAutoApplyMode }
  | { kind: "busy" | "closed" | "starting"; requestedMode: WeaverAutoApplyMode }
  | { kind: "conflict"; requestedMode: WeaverAutoApplyMode; pendingMode: WeaverAutoApplyMode }
  | { kind: "refused"; requestedMode: WeaverAutoApplyMode; message: string };

interface AutonomyUpdate {
  seq: number;
  sessionId: string;
  requestedMode: WeaverAutoApplyMode;
  expectedRevision: number;
  status: "pending" | "unresolved";
  /** `readback` after a stale answer: the outcome is settled only by authoritative session detail. */
  phase: "update" | "readback";
  attempts: number;
}

/** Responses are attributed to the transport and conversation they were sent through, never to the current ones. */
interface PolicyScope { client: AgentClient; sessionId: string }

// A lost response is retried once with the same conditional request before it is shown as unresolved.
const AUTOMATIC_AUTONOMY_RETRIES = 1;

export type WeaverTimelineEntry =
  | { id: string; kind: "step"; stepIndex: number; status: "running" | "completed" }
  | { id: string; kind: "tool"; toolCallId: string; toolName: string; status: "requested" | "running" | "completed"; succeeded?: boolean; summary?: string }
  | { id: string; kind: "progress"; label: string };

export interface WeaverFollowup {
  id: string;
  message: string;
}

export interface UseWeaverSessionOptions {
  api: ElsaStudioModuleApi;
  surface: StudioAgentSurface;
  client?: AgentClient;
  subscribeStream?: typeof subscribeAgentSessionStream;
}

export interface UseWeaverSession {
  bootstrap: AgentBootstrapResponse | null;
  sessionId: string | null;
  ready: boolean;
  chatState: WeaverChatState;
  busy: boolean;
  error: string | null;
  messages: AgentMessageViewModel[];
  timeline: WeaverTimelineEntry[];
  plan: AgentPlanStep[];
  proposals: AgentActionProposal[];
  toolResults: StudioAgentToolResult[];
  attachments: StudioAgentContextAttachment[];
  contextDiagnostics: StudioAgentContributionDiagnostic[];
  followups: WeaverFollowup[];
  pendingProposalIds: ReadonlySet<string>;
  /** The backend-acknowledged mode of the current conversation, or the create-time request before one exists. */
  autoApplyMode: WeaverAutoApplyMode;
  autoApplyState: WeaverAutoApplyState;
  /** The requested mode of an in-flight or unresolved change; it is not in effect. */
  pendingAutoApplyMode: WeaverAutoApplyMode | null;
  autoApplyNotice: WeaverAutoApplyNotice | null;
  /** Why the selector is unavailable on this conversation. */
  autoApplyUnsupported: "unsupported" | "unreadable" | null;
  /** Dependent prompt submission is blocked while a mode change is unresolved. */
  autoApplyBlocked: boolean;
  allowedAutoApplyModes: readonly WeaverAutoApplyMode[];
  promptStarters: ReturnType<typeof getActivePromptStarters>;
  capabilities: ReturnType<typeof getActiveAgentCapabilities>;
  activeProvider: AgentProviderDiagnostics | undefined;
  send(message: string | StudioAgentPromptRequest): void;
  stop(): void;
  removeFollowup(id: string): void;
  approveProposal(proposal: AgentActionProposal): void;
  denyProposal(proposal: AgentActionProposal): void;
  executeProposal(proposal: AgentActionProposal): void;
  retryConfirmation(proposal: AgentActionProposal): void;
  submitFeedback(messageId: string, rating: "positive" | "negative"): void;
  setAutoApplyMode(mode: WeaverAutoApplyMode): void;
  /** Resends the same unresolved conditional change, or repeats its authoritative readback. */
  retryAutoApplyMode(): void;
}

const ASSISTANT_PLACEHOLDER = "Awaiting assistant response…";
type ClientConfirmation = { invocation: NonNullable<AgentActionProposal["preparedInvocation"]>; acknowledgement: AgentClientAcknowledgement; generation: number };

const DISABLED_POLICY: AgentBootstrapResponse["policy"] = {
  contextVisibility: true,
  defaultAutonomyMode: "auto-read-only",
  maxAutonomyMode: "auto-read-only",
  allowedAutonomyModes: ["manual", "auto-read-only"]
};

export function useWeaverSession({ api, surface, client, subscribeStream = subscribeAgentSessionStream }: UseWeaverSessionOptions): UseWeaverSession {
  const [bootstrap, setBootstrap] = useState<AgentBootstrapResponse | null>(null);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [requestContextDiagnostics, setRequestContextDiagnostics] = useState<StudioAgentContributionDiagnostic[]>([]);
  const [streamContextDiagnostics, setStreamContextDiagnostics] = useState<StudioAgentContributionDiagnostic[]>([]);
  const contextDiagnostics = useMemo(() => [...requestContextDiagnostics, ...streamContextDiagnostics], [requestContextDiagnostics, streamContextDiagnostics]);
  const [attachments, setAttachments] = useState<StudioAgentContextAttachment[]>([]);
  const [messages, setMessages] = useState<AgentMessageViewModel[]>([]);
  const [timeline, setTimeline] = useState<WeaverTimelineEntry[]>([]);
  const [plan, setPlan] = useState<AgentPlanStep[]>([]);
  const [toolResults, setToolResults] = useState<StudioAgentToolResult[]>([]);
  const [proposals, setProposals] = useState<AgentActionProposal[]>([]);
  const [followups, setFollowups] = useState<WeaverFollowup[]>([]);
  const [chatState, setChatState] = useState<WeaverChatState>("idle");
  const [error, setError] = useState<string | null>(null);
  // Before a conversation exists the selector holds the create-time request; afterwards the display is the session policy.
  const [draftAutoApplyMode, setDraftAutoApplyModeState] = useState<WeaverAutoApplyMode>("manual");
  const [sessionPolicy, setSessionPolicyState] = useState<AgentSessionPolicy | null>(null);
  const [autonomyUpdate, setAutonomyUpdateState] = useState<AutonomyUpdate | null>(null);
  const [autonomyUnsupported, setAutonomyUnsupportedState] = useState<"unsupported" | "unreadable" | null>(null);
  const [autoApplyNotice, setAutoApplyNotice] = useState<WeaverAutoApplyNotice | null>(null);
  const autoApplyMode = sessionPolicy?.autonomyMode ?? draftAutoApplyMode;
  const [pendingProposalIds, setPendingProposalIds] = useState<ReadonlySet<string>>(new Set());

  const clientControllersRef = useRef(new Set<AbortController>());
  const clientConfirmationsRef = useRef(new Map<string, ClientConfirmation>());
  const mountedRef = useRef(true);
  const turnAbortRef = useRef<AbortController | null>(null);
  const turnIdRef = useRef<string | null>(null);
  // Bumped whenever a turn starts or is stopped, so an in-flight startTurn can detect that it was
  // superseded/cancelled during one of its awaits and bail out before re-subscribing a stream.
  const turnGenerationRef = useRef(0);
  const diagnosticGenerationRef = useRef<number | null>(null);
  const diagnosticSurfaceRef = useRef({ route: surface.route, resourceType: surface.resourceType, resourceId: surface.resourceId });
  // A stream stopped before its turn id arrives retains only its original cancellation observer.
  const detachedStreamsRef = useRef(new Set<AgentStreamSubscription>());
  const sessionIdRef = useRef<string | null>(null);
  const streamRef = useRef<AgentStreamSubscription | null>(null);
  const pendingProposalIdsRef = useRef(new Set<string>());
  const autoApplyModeRef = useRef(autoApplyMode);
  autoApplyModeRef.current = autoApplyMode;
  const draftAutoApplyModeRef = useRef(draftAutoApplyMode);
  const sessionPolicyRef = useRef<AgentSessionPolicy | null>(null);
  const autonomyUpdateRef = useRef<AutonomyUpdate | null>(null);
  const autonomyUnsupportedRef = useRef<"unsupported" | "unreadable" | null>(null);
  const policyScopeRef = useRef<PolicyScope | null>(null);
  const autonomySeqRef = useRef(0);
  const creatingSessionsRef = useRef(0);
  // Acknowledged revisions seen by this surface, so a result can name the mode that admitted it.
  const revisionModesRef = useRef(new Map<number, WeaverAutoApplyMode>());
  // The policy the server stamped on the current turn's accepted message.
  const turnAcceptedPolicyRef = useRef<AgentAcceptedPolicy | null>(null);
  const setDraftAutoApplyMode = useCallback((mode: WeaverAutoApplyMode) => { draftAutoApplyModeRef.current = mode; setDraftAutoApplyModeState(mode); }, []);
  const setSessionPolicy = useCallback((policy: AgentSessionPolicy | null) => { sessionPolicyRef.current = policy; setSessionPolicyState(policy); }, []);
  const setAutonomyUpdate = useCallback((update: AutonomyUpdate | null) => { autonomyUpdateRef.current = update; setAutonomyUpdateState(update); }, []);
  const setAutonomyUnsupported = useCallback((reason: "unsupported" | "unreadable" | null) => { autonomyUnsupportedRef.current = reason; setAutonomyUnsupportedState(reason); }, []);
  // Correlation id for the in-flight turn, when a caller asked for the response routed back (see send()).
  const turnRequestIdRef = useRef<string | null>(null);
  const turnMessageIdRef = useRef<string | null>(null);
  const settledRequestIdsRef = useRef(new Set<string>());
  // Accumulated assistant text per message id, so a correlated turn can publish its final answer.
  const assistantTextByIdRef = useRef(new Map<string, string>());

  const clearContextDiagnostics = useCallback(() => {
    diagnosticGenerationRef.current = null;
    setRequestContextDiagnostics([]);
    setStreamContextDiagnostics([]);
  }, []);

  useEffect(() => {
    const previous = diagnosticSurfaceRef.current;
    if (previous.route !== surface.route || previous.resourceType !== surface.resourceType || previous.resourceId !== surface.resourceId) {
      diagnosticSurfaceRef.current = { route: surface.route, resourceType: surface.resourceType, resourceId: surface.resourceId };
      clearContextDiagnostics();
    }
  }, [clearContextDiagnostics, surface.route, surface.resourceType, surface.resourceId]);

  const clientRunner = useMemo(() => createClientInvocationRunner(api), [api]);
  const agentClient = useMemo(() => client ?? createAgentClient(api.backend), [api.backend, client]);
  const mode: StudioAgentMode = bootstrap?.modes[0] ?? "explain";
  const contributionFilter = useMemo(() => ({
    // This only presents capabilities the backend already admitted. It grants no tool authority;
    // permissions are checked again by the backend when a request or invocation is executed.
    permissions: bootstrap?.enabled === true ? bootstrap.capabilities.flatMap(capability => capability.requiredPermissions ?? []) : [],
    policy: {
      allowedCapabilityIds: bootstrap?.enabled === true ? bootstrap.capabilities.map(capability => capability.id) : []
    }
  }), [bootstrap]);
  const capabilities = useMemo(() => getActiveAgentCapabilities(api, surface, contributionFilter), [api, surface, contributionFilter]);
  const promptStarters = useMemo(() => getActivePromptStarters(api, surface, capabilities, contributionFilter), [api, surface, capabilities, contributionFilter]);
  const activeProvider = useMemo(() => bootstrap?.provider, [bootstrap]);
  const ready = useMemo(() => bootstrap?.enabled === true && bootstrap.providerStatus !== "unavailable" && capabilities.length > 0, [bootstrap, capabilities]);
  // The deployment ceiling determines which autonomy modes the dock may offer.
  // An established conversation offers its own acknowledged ceiling; before one exists the deployment's applies.
  const allowedAutoApplyModes = useMemo<readonly WeaverAutoApplyMode[]>(
    () => sessionPolicy?.allowedAutonomyModes.length ? sessionPolicy.allowedAutonomyModes
      : bootstrap?.policy.allowedAutonomyModes?.length ? bootstrap.policy.allowedAutonomyModes : ALL_AUTONOMY_MODES,
    [bootstrap, sessionPolicy]);
  const allowedAutoApplyModesRef = useRef(allowedAutoApplyModes);
  allowedAutoApplyModesRef.current = allowedAutoApplyModes;

  useEffect(() => {
    let disposed = false;
    void (async () => {
      try {
        const result = await agentClient.bootstrap();
        if (!disposed) {
          setBootstrap(result);
          api.agent.providerAvailability.set(result.enabled && result.providerStatus !== "unavailable" && result.providerStatus !== "disabled");
          // Adopt the deployment's default autonomy mode (already clamped to its ceiling server-side).
          // The dropdown is disabled until ready, so this can't clobber a user's choice.
          setDraftAutoApplyMode(result.policy.defaultAutonomyMode);
        }
      } catch (e) {
        if (!disposed) {
          setBootstrap({ enabled: false, providerStatus: "unavailable", modes: [], capabilities: [], provider: undefined, policy: DISABLED_POLICY });
          api.agent.providerAvailability.set(false);
          setError(getAgentErrorMessage(e));
        }
      }
    })();
    return () => { disposed = true; };
  }, [agentClient, api, setDraftAutoApplyMode]);

  // A different backend transport opens a new policy scope: a late response sent through the previous one
  // can no longer label this conversation, and its unresolved change no longer blocks it.
  const scopedClientRef = useRef(agentClient);
  useEffect(() => {
    if (scopedClientRef.current === agentClient) return;
    scopedClientRef.current = agentClient;
    policyScopeRef.current = sessionIdRef.current ? { client: agentClient, sessionId: sessionIdRef.current } : null;
    setAutonomyUpdate(null);
  }, [agentClient, setAutonomyUpdate]);

  /** Adopts an authoritative policy unless a newer acknowledged revision is already displayed. */
  const acceptPolicy = useCallback((scope: PolicyScope, policy: AgentSessionPolicy) => {
    if (!mountedRef.current || policyScopeRef.current !== scope) return false;
    const known = sessionPolicyRef.current;
    if (known && policy.revision < known.revision) return false;
    revisionModesRef.current.set(policy.revision, policy.autonomyMode);
    setSessionPolicy(policy);
    setDraftAutoApplyMode(policy.autonomyMode);
    return true;
  }, [setDraftAutoApplyMode, setSessionPolicy]);

  /** Attributes readback messages to the policy revision that accepted them; an earlier attribution is never replaced. */
  const attributeMessages = useCallback((detail: AgentSessionDetailResponse) => {
    const revisions = new Map(detail.messages.filter(item => item.acceptedPolicyRevision !== undefined).map(item => [item.id, item.acceptedPolicyRevision!]));
    if (revisions.size === 0) return;
    setMessages(current => current.map(message => {
      const revision = revisions.get(message.id);
      return revision === undefined || message.acceptedPolicy ? message : { ...message, acceptedPolicy: { revision, autonomyMode: revisionModesRef.current.get(revision) } };
    }));
  }, []);

  /** Reads the current conversation's policy back without settling any in-flight change. */
  const refreshSessionPolicy = useCallback(async () => {
    const scope = policyScopeRef.current;
    if (!scope) return;
    try {
      const detail = await scope.client.getSession(scope.sessionId);
      if (!mountedRef.current || policyScopeRef.current !== scope) return;
      if (detail.policy) acceptPolicy(scope, detail.policy);
      attributeMessages(detail);
    } catch {
      // The refused prompt already explains itself; a later prompt is refused again while the policy stays stale.
    }
  }, [acceptPolicy, attributeMessages]);

  const ensureSession = useCallback(async (generation: number) => {
    if (sessionIdRef.current) return sessionIdRef.current;
    const requested = draftAutoApplyModeRef.current;
    creatingSessionsRef.current++;
    try {
      const response = await agentClient.createSession({
        mode,
        autonomyMode: requested,
        activeSurface: surface,
        clientContext: { studioVersion: api.host.hostVersion, sdkVersion: api.host.sdkVersion, moduleIds: [] }
      });
      if (generation === turnGenerationRef.current) {
        sessionIdRef.current = response.sessionId;
        setSessionId(response.sessionId);
        const scope = { client: agentClient, sessionId: response.sessionId };
        policyScopeRef.current = scope;
        setAutonomyUpdate(null);
        if (response.policy) {
          setAutonomyUnsupported(null);
          acceptPolicy(scope, response.policy);
          setAutoApplyNotice(response.policy.autonomyMode !== requested ? { kind: "clamped", requestedMode: requested, acknowledgedMode: response.policy.autonomyMode } : null);
        } else {
          // Without a readable policy this conversation can neither confirm nor change its autonomy.
          setSessionPolicy(null);
          setAutonomyUnsupported("unreadable");
        }
      }
      return response.sessionId;
    } finally {
      creatingSessionsRef.current--;
    }
  }, [acceptPolicy, agentClient, api.host.hostVersion, api.host.sdkVersion, mode, setAutonomyUnsupported, setAutonomyUpdate, setSessionPolicy, surface]);

  const reconcileAutonomy = useCallback(async (scope: PolicyScope, update: AutonomyUpdate) => {
    const checking: AutonomyUpdate = { ...update, status: "pending", phase: "readback" };
    setAutonomyUpdate(checking);
    const isCurrent = () => mountedRef.current && policyScopeRef.current === scope && autonomyUpdateRef.current?.seq === update.seq;
    try {
      const detail = await scope.client.getSession(scope.sessionId);
      if (!isCurrent()) return;
      setAutonomyUpdate(null);
      if (!detail.policy) { setAutonomyUnsupported("unreadable"); setAutoApplyNotice(null); return; }
      acceptPolicy(scope, detail.policy);
      attributeMessages(detail);
      // Another write committed. The authoritative state is shown without claiming which intent won.
      setAutoApplyNotice({ kind: "reconciled", acknowledgedMode: sessionPolicyRef.current?.autonomyMode ?? detail.policy.autonomyMode });
    } catch {
      if (isCurrent()) setAutonomyUpdate({ ...checking, status: "unresolved" });
    }
  }, [acceptPolicy, attributeMessages, setAutonomyUnsupported, setAutonomyUpdate]);

  const deliverAutonomyUpdate = useCallback(async (scope: PolicyScope, update: AutonomyUpdate) => {
    const isCurrent = () => mountedRef.current && policyScopeRef.current === scope && autonomyUpdateRef.current?.seq === update.seq;
    let attempt = update;
    for (;;) {
      try {
        const policy = await scope.client.updateAutonomy(scope.sessionId, { autonomyMode: update.requestedMode, expectedRevision: update.expectedRevision });
        if (!isCurrent()) return;
        // An acknowledgement older than the displayed revision is an old response; only readback may settle it.
        if (!acceptPolicy(scope, policy)) return void reconcileAutonomy(scope, attempt);
        setAutonomyUpdate(null);
        setAutoApplyNotice(policy.autonomyMode !== update.requestedMode ? { kind: "clamped", requestedMode: update.requestedMode, acknowledgedMode: policy.autonomyMode } : null);
        return;
      } catch (error) {
        if (!isCurrent()) return;
        const failure = classifyAutonomyFailure(error);
        if (failure === "lost") {
          // The change may have committed. Only the same conditional request or a readback can resolve it.
          if (attempt.attempts < AUTOMATIC_AUTONOMY_RETRIES) { attempt = { ...attempt, attempts: attempt.attempts + 1 }; setAutonomyUpdate(attempt); continue; }
          setAutonomyUpdate({ ...attempt, status: "unresolved" });
          return;
        }
        if (failure === "stale") return void reconcileAutonomy(scope, attempt);
        setAutonomyUpdate(null);
        if (failure === "unsupported") { setAutonomyUnsupported("unsupported"); setAutoApplyNotice(null); return; }
        setAutoApplyNotice(failure === "busy" || failure === "closed"
          ? { kind: failure, requestedMode: update.requestedMode }
          : { kind: "refused", requestedMode: update.requestedMode, message: getAgentErrorMessage(error) });
        return;
      }
    }
  }, [acceptPolicy, reconcileAutonomy, setAutonomyUnsupported, setAutonomyUpdate]);

  // Belt-and-suspenders: never request a mode above the offered ceiling, even if the UI offered it.
  const selectAutoApplyMode = useCallback((mode: WeaverAutoApplyMode) => {
    if (!allowedAutoApplyModesRef.current.includes(mode)) return;
    const session = sessionIdRef.current;
    if (!session) {
      // A create request already carries the earlier selection; a later one cannot silently replace it.
      if (creatingSessionsRef.current > 0) { setAutoApplyNotice({ kind: "starting", requestedMode: mode }); return; }
      setDraftAutoApplyMode(mode);
      setAutoApplyNotice(null);
      return;
    }
    const inFlight = autonomyUpdateRef.current;
    if (inFlight) {
      // At most one change per conversation: a duplicate joins it, a conflicting one is refused unsent.
      if (inFlight.requestedMode !== mode) setAutoApplyNotice({ kind: "conflict", requestedMode: mode, pendingMode: inFlight.requestedMode });
      return;
    }
    const scope = policyScopeRef.current;
    const policy = sessionPolicyRef.current;
    if (!scope || scope.sessionId !== session || !policy || autonomyUnsupportedRef.current) return;
    setAutoApplyNotice(null);
    if (mode === policy.autonomyMode) return; // Already acknowledged: nothing to change.
    const update: AutonomyUpdate = { seq: ++autonomySeqRef.current, sessionId: session, requestedMode: mode, expectedRevision: policy.revision, status: "pending", phase: "update", attempts: 0 };
    setAutonomyUpdate(update);
    void deliverAutonomyUpdate(scope, update);
  }, [deliverAutonomyUpdate, setAutonomyUpdate, setDraftAutoApplyMode]);

  const retryAutoApplyMode = useCallback(() => {
    const update = autonomyUpdateRef.current;
    const scope = policyScopeRef.current;
    if (!update || update.status !== "unresolved" || !scope || scope.sessionId !== update.sessionId) return;
    const retry: AutonomyUpdate = { ...update, status: "pending", attempts: 0 };
    setAutonomyUpdate(retry);
    if (retry.phase === "readback") void reconcileAutonomy(scope, retry);
    else void deliverAutonomyUpdate(scope, retry);
  }, [deliverAutonomyUpdate, reconcileAutonomy, setAutonomyUpdate]);

  const handleStreamEvent = useCallback((event: AgentStreamEvent) => {

    switch (event.type) {
      case "context-admitted":
        if (diagnosticGenerationRef.current !== turnGenerationRef.current) break;
        setAttachments(event.attachments);
        // A new provider-disclosure projection replaces only its server limitations. Client
        // collection and message-acceptance limitations still belong to this same request.
        setStreamContextDiagnostics(event.diagnostics);
        break;
      case "module-result":
        setToolResults(current => [...current.filter(item => item.toolCallId !== event.result.toolCallId), event.result]);
        break;
      case "turn-started":
        turnIdRef.current = event.turnId;
        break;
      case "message-started":
        if (event.role === "assistant") assistantTextByIdRef.current.set(event.messageId, "");
        setMessages(current => upsertMessage(current, { id: event.messageId, role: event.role, content: "", status: "streaming", ...(event.role === "assistant" && turnAcceptedPolicyRef.current ? { acceptedPolicy: turnAcceptedPolicyRef.current } : {}) }));
        break;
      case "message-delta":
        assistantTextByIdRef.current.set(event.messageId, (assistantTextByIdRef.current.get(event.messageId) ?? "") + event.content);
        setMessages(current => upsertDelta(current, event.messageId, event.content));
        break;
      case "step-started":
        setTimeline(current => [...current, { id: `step-${event.stepIndex}`, kind: "step", stepIndex: event.stepIndex, status: "running" }]);
        break;
      case "step-completed":
        setTimeline(current => current.map(entry => entry.kind === "step" && entry.stepIndex === event.stepIndex ? { ...entry, status: "completed" } : entry));
        break;
      case "tool-call-requested":
        setTimeline(current => upsertTool(current, { id: `tool-${event.toolCallId}`, kind: "tool", toolCallId: event.toolCallId, toolName: event.toolName, status: "requested" }));
        break;
      case "tool-call-started":
        setTimeline(current => upsertTool(current, { id: `tool-${event.toolCallId}`, kind: "tool", toolCallId: event.toolCallId, toolName: event.toolName, status: "running" }));
        break;
      case "tool-call-completed":
        setTimeline(current => upsertTool(current, { id: `tool-${event.toolCallId}`, kind: "tool", toolCallId: event.toolCallId, toolName: event.toolName, status: "completed", succeeded: event.succeeded, summary: event.summary }));
        break;
      case "progress":
        setTimeline(current => [...current, { id: `progress-${current.length}-${event.label}`, kind: "progress", label: event.label }]);
        break;
      case "plan-updated":
        setPlan(event.steps);
        break;
      case "proposal-created":
        setProposals(current => upsertProposal(current, normalizeProposal(event.proposalId, event.proposal)));
        maybeAutoApplyProposal(event.proposalId, event.proposal);
        break;
      case "review-requested":
        // The retained turn is paused, not completed. Only its observed receipt continuation
        // can settle the original correlated request.
        setChatState("awaiting-review");
        setMessages(current => current.map(message => message.status === "streaming" ? { ...message, status: "pending", content: message.content === ASSISTANT_PLACEHOLDER ? "Waiting for review of the proposed change." : message.content } : message));
        streamRef.current?.close();
        streamRef.current = null;
        break;
      case "clarification-requested":
        setMessages(current => [...current, { id: event.clarification.id, role: "assistant", content: event.clarification.prompt, status: "completed" }]);
        // Weaver needs more input from the user in the chat; release any correlated caller so it stops waiting.
        clearContextDiagnostics();
        publishTurnResult("cancelled", "");
        finishTurn();
        break;
      case "message-completed":
        setMessages(current => current.map(message => message.id === event.messageId ? { ...message, status: "completed" } : message));
        publishTurnResult("completed", assistantTextByIdRef.current.get(event.messageId) ?? "");
        assistantTextByIdRef.current.delete(event.messageId);
        finishTurn();
        break;
      case "turn-cancelled":
        clearContextDiagnostics();
        setMessages(current => current.map(message => message.status === "streaming" ? { ...message, status: "cancelled" } : message));
        publishTurnResult("cancelled", "");
        finishTurn();
        break;
      case "error":
        // A disclosure refusal must never leave an earlier admission displayed as current.
        setAttachments([]);
        clearContextDiagnostics();
        setError(event.message);
        setMessages(current => [...current, { id: `error-${current.length}`, role: "error", content: event.message, status: "failed" }]);
        publishTurnResult("failed", "");
        finishTurn();
        break;
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeProvider, agentClient, clearContextDiagnostics]);

  function finishTurn() {
    setChatState("idle");
    setFollowups([]);
    turnIdRef.current = null;
    turnMessageIdRef.current = null;
  }

  // Route a correlated turn's outcome back to the caller (e.g. the Create dialog) exactly once. Defaults
  // to the in-flight turn, but a superseded/errored turn can pass its own captured id (the ref may have
  // since moved on to a newer turn — in that case we publish without clearing the newer turn's id).
  const publishTurnResult = useCallback((status: "completed" | "cancelled" | "failed", text: string, requestId = turnRequestIdRef.current) => {
    if (!requestId || settledRequestIdsRef.current.has(requestId)) return;
    settledRequestIdsRef.current.add(requestId);
    if (turnRequestIdRef.current === requestId) turnRequestIdRef.current = null;
    // The policy that admitted the turn, not the current selector value, describes its result.
    api.agent.publishPromptResult({ requestId, status, text, autoApply: (turnAcceptedPolicyRef.current?.autonomyMode ?? autoApplyModeRef.current) === "full-auto" });
  }, [api]);

  useEffect(() => {
    mountedRef.current = true;
    const controllers = clientControllersRef.current;
    const confirmations = clientConfirmationsRef.current;
    const generation = turnGenerationRef;
    const detached = detachedStreamsRef.current;
    return () => {
      const uncertain = controllers.size > 0 || [...confirmations.values()].some(item => item.generation === generation.current);
      mountedRef.current = false;
      generation.current++;
      streamRef.current?.close(); turnAbortRef.current?.abort();
      for (const stream of detached) stream.close(); detached.clear();
      for (const controller of controllers) controller.abort();
      publishTurnResult("failed", uncertain ? "The assistant closed before confirmation; the observed change may already have been applied." : "The assistant closed before completing the request.");
    };
  }, [publishTurnResult]);

  const subscribeToTurn = useCallback((session: string, url: string, generation: number, messageId: string | undefined, requestId: string | null) => {
    const closeDetached = () => { if (subscription) { subscription.close(); detachedStreamsRef.current.delete(subscription); } };
    const subscription = subscribeStream(api.backend, url, event => {
      if (generation !== turnGenerationRef.current) {
        if (event.type === "turn-started") { void agentClient.cancelTurn(session, event.turnId).catch(() => undefined); closeDetached(); }
        else if (event.type === "message-completed" || event.type === "turn-cancelled" || event.type === "error") closeDetached();
        return;
      }
      handleStreamEvent(event);
    }, error => {
      if (generation !== turnGenerationRef.current) { closeDetached(); return; }
      clearContextDiagnostics(); setError(getAgentErrorMessage(error)); publishTurnResult("failed", "", requestId); finishTurn();
    }, { defaultMessageId: messageId });
    return subscription;
  }, [agentClient, api.backend, clearContextDiagnostics, handleStreamEvent, publishTurnResult, subscribeStream]);

  const startTurn = useCallback(async (request: StudioAgentPromptRequest) => {
    const { message, requestId } = request;
    const deliveryId = requestId ?? globalThis.crypto?.randomUUID?.() ?? `message-${Date.now()}-${Math.random()}`;
    const generation = ++turnGenerationRef.current;
    diagnosticGenerationRef.current = generation;
    turnAbortRef.current?.abort();
    const turnAbort = new AbortController(); turnAbortRef.current = turnAbort;
    turnRequestIdRef.current = requestId ?? null;
    turnAcceptedPolicyRef.current = null;
    setChatState("streaming");
    setError(null);
    setTimeline([]);
    setPlan([]);
    setAttachments([]);
    setRequestContextDiagnostics([]);
    setStreamContextDiagnostics([]);
    setMessages(current => [...current, { id: `user-${current.length}`, role: "user", content: message, status: "completed" }]);

    // Release a correlated caller from before the stream opens (supersede/error), so it never hangs.
    // Pass the captured requestId because a supersede may have already moved turnRequestIdRef on.
    const settle = (status: "cancelled" | "failed") => publishTurnResult(status, "", requestId);

    let missingRequiredContext = false;
    try {
      const collection = await collectAgentContext(api, surface, mode, sessionIdRef.current ?? undefined, request.attachments, turnAbort.signal);
      if (turnGenerationRef.current !== generation) return settle("cancelled");
      if (diagnosticGenerationRef.current === generation) setRequestContextDiagnostics(collection.diagnostics);
      const missing = collection.diagnostics.filter(item => item.required);
      missingRequiredContext = missing.length > 0;
      if (missing.length) throw new Error(missing.map(item => item.message).join(" "));
      const collected = collection.attachments;
      if (turnGenerationRef.current !== generation) return settle("cancelled");
      const activeSessionId = await ensureSession(generation);
      if (turnGenerationRef.current !== generation) return settle("cancelled");
      const response = await agentClient.sendMessage(activeSessionId, { message, mode, activeSurface: surface, contextAttachments: collected, capabilityId: capabilities[0]?.id, requestId: deliveryId, source: request.source, promptMode: request.mode });
      // A stopped/superseded response cannot mutate or finish the current conversation.
      if (turnGenerationRef.current !== generation) return settle("cancelled");
      // Candidates and session-creation metadata are not admitted request evidence. Older or
      // incomplete responses intentionally leave this empty instead of asserting local authority.
      setAttachments(response.contextAttachments ?? []);
      if (response.warnings?.length && diagnosticGenerationRef.current === generation) setRequestContextDiagnostics(current => [...current, ...response.warnings!.filter(item => item.code !== "agent.message.duplicate").map(item => ({ contributorId: item.target ?? "agent", code: item.code, message: item.message, required: false }))]);
      if (response.status !== "pending" && response.warnings?.some(item => item.code === "agent.message.duplicate")) {
        setError("This delivery was already accepted; it will not run again. Review the existing session outcome.");
        clearContextDiagnostics();
        settle("failed"); finishTurn(); return;
      }
      const accepted = response.acceptedPolicy
        ? { ...response.acceptedPolicy, autonomyMode: response.acceptedPolicy.autonomyMode ?? revisionModesRef.current.get(response.acceptedPolicy.revision) }
        : null;
      turnAcceptedPolicyRef.current = accepted;
      setMessages(current => [...current, { id: response.messageId, role: "assistant", content: ASSISTANT_PLACEHOLDER, status: "streaming", ...(accepted ? { acceptedPolicy: accepted } : {}) }]);
      // Admission under a newer revision than displayed means the display is behind: read it back.
      if (accepted && sessionPolicyRef.current && accepted.revision > sessionPolicyRef.current.revision) void refreshSessionPolicy();
      turnMessageIdRef.current = response.messageId;
      streamRef.current?.close();
      streamRef.current = subscribeToTurn(activeSessionId, response.streamUrl, generation, response.messageId, requestId ?? null);
    } catch (e) {
      if (turnGenerationRef.current !== generation) return settle("cancelled");
      if (!missingRequiredContext) clearContextDiagnostics();
      settle("failed");
      // A prompt that lost to a committed policy change was not accepted. Read the policy back and let the
      // user decide to resend; it is never resent automatically under a policy they have not seen.
      const message = getAgentErrorCode(e) === POLICY_CHANGED
        ? "The auto-apply mode changed before Weaver accepted this prompt, so it was not sent. Check the current mode, then send it again."
        : getAgentErrorMessage(e);
      if (getAgentErrorCode(e) === POLICY_CHANGED) void refreshSessionPolicy();
      setError(message);
      setMessages(current => [...current, { id: `error-${current.length}`, role: "error", content: message, status: "failed" }]);
      finishTurn();
    }
  }, [agentClient, api, capabilities, clearContextDiagnostics, ensureSession, mode, publishTurnResult, refreshSessionPolicy, subscribeToTurn, surface]);

  const steer = useCallback(async (message: string) => {
    const generation = turnGenerationRef.current;
    const session = sessionIdRef.current;
    const id = `followup-${Date.now()}-${Math.random()}`;
    setFollowups(current => [...current, { id, message }]);
    try {
      if (!session) throw new Error("The running session is unavailable.");
      await agentClient.sendMessage(session, { message, mode, activeSurface: surface, requestId: id, promptMode: "steer", contextAttachments: [], capabilityId: capabilities[0]?.id });
    } catch (error) {
      if (generation !== turnGenerationRef.current || session !== sessionIdRef.current) return;
      setFollowups(current => current.filter(item => item.id !== id)); setError(getAgentErrorMessage(error));
    }
  }, [agentClient, capabilities, mode, surface]);

  const send = useCallback((value: string | StudioAgentPromptRequest) => {
    const request = typeof value === "string" ? { message: value } : value;
    const message = request.message.trim();
    if (!message || !ready) {
      if (request.requestId) publishTurnResult("failed", "", request.requestId);
      if (message && !ready) setError("The assistant is unavailable for this context; the prompt was not delivered.");
      return;
    }
    if (request.agent) {
      setError("The requested assistant is unavailable; the prompt was not delivered.");
      if (request.requestId) publishTurnResult("failed", "", request.requestId);
      return;
    }
    if (autonomyUpdateRef.current) {
      // A prompt must never run under a mode the server has not acknowledged.
      setError("The auto-apply mode change is not confirmed yet, so the prompt was not delivered. Send it again once the mode is confirmed.");
      if (request.requestId) publishTurnResult("failed", "", request.requestId);
      return;
    }
    if (chatState === "awaiting-review" || chatState === "awaiting-confirmation") {
      setError(chatState === "awaiting-confirmation" ? "Confirm the observed result before sending another request; the prompt was not delivered." : "Review the pending proposal before sending another request; the prompt was not delivered.");
      if (request.requestId) publishTurnResult("failed", "", request.requestId);
      return;
    }
    if (chatState === "streaming") {
      if (!request.requestId && !request.source && !request.attachments?.length && request.mode !== "enqueue") { void steer(message); return; }
      setError("Wait for the current turn before sending this contextual request; the prompt was not delivered.");
      if (request.requestId) publishTurnResult("failed", "", request.requestId);
      return;
    }
    void startTurn({ ...request, message });
  }, [chatState, publishTurnResult, ready, startTurn, steer]);

  const stop = useCallback(() => {
    // Invalidate any startTurn still awaiting so it can't re-subscribe a stream after we cancel.
    turnGenerationRef.current++;
    clearContextDiagnostics();
    turnAbortRef.current?.abort();
    for (const controller of clientControllersRef.current) controller.abort();
    const sessionId = sessionIdRef.current;
    const turnId = turnIdRef.current;
    const stoppedStream = streamRef.current;
    // Stop detaches this foreground conversation before any new prompt may start.
    sessionIdRef.current = null; setSessionId(null); streamRef.current = null;
    // The next conversation requests the last acknowledged mode; late policy responses for this one are ignored.
    policyScopeRef.current = null; setAutonomyUpdate(null); setSessionPolicy(null); setAutonomyUnsupported(null); setAutoApplyNotice(null);
    if (sessionId && turnId) { void agentClient.cancelTurn(sessionId, turnId).catch(() => undefined); stoppedStream?.close(); }
    else if (sessionId && stoppedStream) detachedStreamsRef.current.add(stoppedStream);
    else stoppedStream?.close();
    setMessages(current => [...current.map(message => message.status === "streaming" ? { ...message, status: "cancelled" as const } : message), { id: `stopped-${turnGenerationRef.current}`, role: "progress", content: "Stopped this conversation. Your next prompt starts a new conversation; prior changes and receipts remain available.", status: "completed" }]);
    publishTurnResult("cancelled", "");
    finishTurn();
    setProposals(current => current.map(proposal => clientConfirmationsRef.current.has(proposal.id) ? { ...proposal, status: "uncertain", error: "Stopped waiting for confirmation. The observed change may already have been applied; retry only confirms its receipt." } : proposal));
  }, [agentClient, clearContextDiagnostics, publishTurnResult, setAutonomyUnsupported, setAutonomyUpdate, setSessionPolicy]);

  const removeFollowup = useCallback((id: string) => setFollowups(current => current.filter(item => item.id !== id)), []);

  const withProposalDecision = useCallback(async (proposalId: string, action: () => Promise<void>) => {
    if (pendingProposalIdsRef.current.has(proposalId)) return;
    pendingProposalIdsRef.current.add(proposalId);
    setPendingProposalIds(new Set(pendingProposalIdsRef.current));
    setError(null);
    try {
      await action();
    } catch (e) {
      setError(getAgentErrorMessage(e));
    } finally {
      pendingProposalIdsRef.current.delete(proposalId);
      setPendingProposalIds(new Set(pendingProposalIdsRef.current));
    }
  }, []);

  const updateProposal = useCallback((proposalId: string, changes: Partial<AgentActionProposal>) => {
    setProposals(current => current.map(proposal => proposal.id === proposalId ? { ...proposal, ...changes } : proposal));
  }, []);

  const resumeFromOutcome = useCallback((session: string) => {
    const generation = turnGenerationRef.current;
    const requestId = turnRequestIdRef.current;
    const messageId = turnMessageIdRef.current;
    streamRef.current?.close();
    setChatState("streaming");
    setMessages(current => current.map(message => message.id === messageId && message.status === "pending" ? { ...message, status: "streaming", content: ASSISTANT_PLACEHOLDER } : message));
    streamRef.current = subscribeToTurn(session, `/_elsa/agent/sessions/${encodeURIComponent(session)}/stream`, generation, messageId ?? undefined, requestId);
  }, [subscribeToTurn]);

  const runReviewedDecision = useCallback(async (action: "approveProposal" | "denyProposal" | "executeProposal", proposalId: string, revision: string | undefined, prepared?: AgentActionProposal["preparedInvocation"]) => {
    const generation = turnGenerationRef.current;
    const requestId = turnRequestIdRef.current;
    const session = prepared?.sessionId ?? sessionIdRef.current;
    if (prepared?.sessionId && prepared.sessionId !== sessionIdRef.current) {
      updateProposal(proposalId, { disabledReason: "This conversation has stopped. Start a new request to review this operation." });
      return null;
    }
    try {
      return await agentClient[action](proposalId, { revision });
    } catch (error) {
      if (!(error instanceof AgentExecutionError)) throw error;
      const outcome = error.terminalOutcome;
      updateProposal(proposalId, { status: outcome.approvalStatus, toolResult: outcome.toolResult, result: outcome.toolResult?.payload, resultType: outcome.toolResult?.payload?.typeId, error: error.message });
      if (generation === turnGenerationRef.current && session === sessionIdRef.current) {
        setError(error.message);
        publishTurnResult("failed", "", requestId);
        // Drain the retained failure through its ordinary outcome continuation, with no new prompt
        // and no effect replay. A refusal is never turned into an applied result.
        if (session) resumeFromOutcome(session);
        else finishTurn();
      }
      return null;
    }
  }, [agentClient, publishTurnResult, resumeFromOutcome, updateProposal]);

  const approveProposal = useCallback((proposal: AgentActionProposal) => void withProposalDecision(proposal.id, async () => {
    const response = await runReviewedDecision("approveProposal", proposal.id, proposal.revision, proposal.preparedInvocation);
    if (response) updateProposal(proposal.id, { status: response.approvalStatus });
  }), [runReviewedDecision, updateProposal, withProposalDecision]);

  const denyProposal = useCallback((proposal: AgentActionProposal) => void withProposalDecision(proposal.id, async () => {
    const generation = turnGenerationRef.current;
    const requestId = turnRequestIdRef.current;
    const session = proposal.preparedInvocation?.sessionId ?? sessionIdRef.current;
    const response = await runReviewedDecision("denyProposal", proposal.id, proposal.revision, proposal.preparedInvocation);
    if (!response) return;
    updateProposal(proposal.id, { status: response.approvalStatus });
    if (!mountedRef.current || generation !== turnGenerationRef.current || session !== sessionIdRef.current) return;
    publishTurnResult("cancelled", "", requestId);
    if (session) resumeFromOutcome(session);
    else finishTurn();
  }), [publishTurnResult, resumeFromOutcome, runReviewedDecision, updateProposal, withProposalDecision]);

  const confirmClientResult = useCallback(async (proposalId: string, confirmation: ClientConfirmation) => {
    const generation = confirmation.generation;
    try {
      const response = await agentClient.acknowledgeClient(confirmation.invocation.sessionId, confirmation.invocation.invocationId, confirmation.acknowledgement);
      clientConfirmationsRef.current.delete(proposalId);
      if (!mountedRef.current) return;
      updateProposal(proposalId, { confirmationPending: false, status: response.approvalStatus, toolResult: response.toolResult, result: response.toolResult?.payload, resultType: response.toolResult?.payload?.typeId, error: undefined });
      if (generation === turnGenerationRef.current) resumeFromOutcome(confirmation.invocation.sessionId);
    } catch (error) {
      if (!mountedRef.current) return;
      updateProposal(proposalId, { status: "uncertain", confirmationPending: true, error: `Confirmation was not received. Retry sends the same observed receipt without running the change again. ${getAgentErrorMessage(error)}` });
      if (generation === turnGenerationRef.current) setChatState("awaiting-confirmation");
    }
  }, [agentClient, resumeFromOutcome, updateProposal]);

  const retryConfirmation = useCallback((proposal: AgentActionProposal) => void withProposalDecision(proposal.id, async () => {
    const confirmation = clientConfirmationsRef.current.get(proposal.id);
    if (!confirmation) throw new Error("The originating observed receipt is unavailable.");
    updateProposal(proposal.id, { status: "awaiting-client", error: undefined });
    if (confirmation.generation === turnGenerationRef.current) setChatState("streaming");
    await confirmClientResult(proposal.id, confirmation);
  }), [confirmClientResult, updateProposal, withProposalDecision]);

  const executeProposal = useCallback((proposal: AgentActionProposal) => void withProposalDecision(proposal.id, async () => {
    const deliveryGeneration = turnGenerationRef.current;
    const requestId = turnRequestIdRef.current;
    const response = await runReviewedDecision("executeProposal", proposal.id, proposal.revision, proposal.preparedInvocation);
    if (!response) return;
    updateProposal(proposal.id, { status: response.approvalStatus });
    if (response.clientInvocation) {
      const invocation = response.clientInvocation;
      const cancellation = new AbortController(); clientControllersRef.current.add(cancellation);
      if (deliveryGeneration !== turnGenerationRef.current) cancellation.abort();
      else { turnIdRef.current = invocation.turnId; setChatState("streaming"); }
      try {
        let acknowledgement: AgentClientAcknowledgement;
        try {
          acknowledgement = structuredClone(await clientRunner.run(invocation, cancellation.signal));
          JSON.stringify(acknowledgement);
        } catch {
          acknowledgement = { client: invocation.client!, approvalDigest: invocation.approvalDigest, outcome: "uncertain", summary: "The owning module returned an unsupported observation after dispatch; application cannot be confirmed." };
        }
        const confirmation = { invocation, acknowledgement, generation: deliveryGeneration };
        clientConfirmationsRef.current.set(proposal.id, confirmation);
        if (mountedRef.current) updateProposal(proposal.id, { confirmationPending: true });
        // Admit an observed receipt even after Stop; only the original active generation may resume.
        await confirmClientResult(proposal.id, confirmation);
        return;
      } catch (error) {
        updateProposal(proposal.id, { status: "uncertain", error: "The dispatched change could not be confirmed. It will not be replayed." });
        if (deliveryGeneration === turnGenerationRef.current) { publishTurnResult("failed", "", requestId); finishTurn(); }
        throw error;
      } finally { clientControllersRef.current.delete(cancellation); }
    } else if (response.toolResult) {
      const session = proposal.preparedInvocation?.sessionId ?? sessionIdRef.current;
      if (session && deliveryGeneration === turnGenerationRef.current) resumeFromOutcome(session);
    }
    updateProposal(proposal.id, { status: response.approvalStatus, toolResult: response.toolResult, result: response.toolResult?.payload, resultType: response.toolResult?.payload?.typeId });
  }), [clientRunner, confirmClientResult, runReviewedDecision, publishTurnResult, resumeFromOutcome, updateProposal, withProposalDecision]);

  function maybeAutoApplyProposal(proposalId: string, payload?: AgentActionProposalPayload) {
    const prepared = payload?.preparedInvocation;
    // The policy that admitted this turn decides, not a selector value chosen afterwards.
    if ((turnAcceptedPolicyRef.current?.autonomyMode ?? autoApplyModeRef.current) !== "full-auto" || !prepared || prepared.requiresApproval || prepared.executionLocation === "clientWorkingState") return;
    const revision = payload?.revision ?? payload?.baseRevision;
    void withProposalDecision(proposalId, async () => {
      const generation = turnGenerationRef.current;
      const approved = await runReviewedDecision("approveProposal", proposalId, revision, prepared);
      if (!approved || generation !== turnGenerationRef.current) return;
      const response = await runReviewedDecision("executeProposal", proposalId, revision, prepared);
      if (!response) return;
      updateProposal(proposalId, { status: response.approvalStatus, toolResult: response.toolResult, result: response.toolResult?.payload, resultType: response.toolResult?.payload?.typeId });
      if (response.toolResult && generation === turnGenerationRef.current) resumeFromOutcome(prepared.sessionId);
    });
  }

  const submitFeedback = useCallback((messageId: string, rating: "positive" | "negative") => {
    void agentClient.submitFeedback(sessionIdRef.current ?? "", messageId, { rating }).catch(e => setError(getAgentErrorMessage(e)));
  }, [agentClient]);

  return {
    bootstrap,
    sessionId,
    ready,
    chatState,
    busy: chatState === "streaming",
    error,
    messages,
    timeline,
    plan,
    proposals,
    toolResults,
    attachments,
    contextDiagnostics,
    followups,
    pendingProposalIds,
    autoApplyMode,
    autoApplyState: !sessionId ? "requested"
      : autonomyUpdate ? autonomyUpdate.status
        : autonomyUnsupported ? "unsupported"
          : sessionPolicy ? "acknowledged" : "requested",
    pendingAutoApplyMode: autonomyUpdate?.requestedMode ?? null,
    autoApplyNotice,
    autoApplyUnsupported: sessionId ? autonomyUnsupported : null,
    autoApplyBlocked: autonomyUpdate !== null,
    allowedAutoApplyModes,
    promptStarters,
    capabilities,
    activeProvider,
    send,
    stop,
    removeFollowup,
    approveProposal,
    denyProposal,
    executeProposal,
    retryConfirmation,
    submitFeedback,
    setAutoApplyMode: selectAutoApplyMode,
    retryAutoApplyMode
  };
}

function upsertMessage(messages: AgentMessageViewModel[], next: AgentMessageViewModel) {
  // A restarted message keeps the policy attribution of the turn that admitted it.
  return messages.some(message => message.id === next.id)
    ? messages.map(message => message.id === next.id ? { ...next, acceptedPolicy: next.acceptedPolicy ?? message.acceptedPolicy } : message)
    : [...messages, next];
}

function upsertDelta(messages: AgentMessageViewModel[], messageId: string, delta: string) {
  const existing = messages.find(message => message.id === messageId);
  if (!existing) return [...messages, { id: messageId, role: "assistant" as const, content: delta, status: "streaming" as const }];
  return messages.map(message => message.id === messageId
    ? { ...message, content: message.content === ASSISTANT_PLACEHOLDER ? delta : `${message.content}${delta}`, status: "streaming" as const }
    : message);
}

function upsertTool(timeline: WeaverTimelineEntry[], next: Extract<WeaverTimelineEntry, { kind: "tool" }>) {
  return timeline.some(entry => entry.id === next.id)
    ? timeline.map(entry => entry.id === next.id ? { ...entry, ...next } : entry)
    : [...timeline, next];
}

function upsertProposal(proposals: AgentActionProposal[], next: AgentActionProposal) {
  return proposals.some(proposal => proposal.id === next.id) ? proposals.map(proposal => proposal.id === next.id ? next : proposal) : [...proposals, next];
}

function normalizeProposal(proposalId: string, proposal?: AgentActionProposalPayload): AgentActionProposal {
  const revision = proposal?.revision ?? proposal?.baseRevision ?? proposal?.preparedInvocation?.target?.revision ?? undefined;
  return {
    id: proposal?.id ?? proposalId,
    title: proposal?.title ?? "Weaver proposal",
    summary: proposal?.summary ?? "Weaver prepared a change. Review the details before approving or applying it.",
    risk: proposal?.risk === ("reviewRequired" as string) ? "review-required" : proposal?.risk ?? "review-required",
    status: normalizeProposalStatus(proposal?.status),
    revision,
    reviewReady: Boolean(proposal?.title?.trim() && proposal?.summary?.trim() && revision),
    preparedInvocation: proposal?.preparedInvocation,
    toolResult: proposal?.toolResult,
    toolId: proposal?.toolId ?? proposal?.preparedInvocation?.toolName,
    moduleId: proposal?.moduleId ?? proposal?.preparedInvocation?.moduleId,
    invocationMode: proposal?.invocationMode,
    requiredPermissions: proposal?.requiredPermissions,
    policy: proposal?.policy,
    resourceTarget: proposal?.resourceTarget,
    operations: proposal?.operations,
    risks: proposal?.risks,
    rollback: proposal?.rollback,
    result: proposal?.result,
    resultRendererId: proposal?.resultRendererId,
    resultType: proposal?.resultType
  };
}

function normalizeProposalStatus(value: unknown): AgentActionProposal["status"] {
  if (typeof value === "number") return (["draft", "awaiting-approval", "approved", "denied", "edited", "expired", "executed", "failed", "cancelled", "executing", "awaiting-client", "uncertain"] as const)[value] ?? "failed";
  return (String(value ?? "awaiting-approval").replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase()) as AgentActionProposal["status"];
}
