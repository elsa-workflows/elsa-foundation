import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ElsaStudioModuleApi, StudioAgentContextAttachment, StudioAgentContributionDiagnostic, StudioAgentMode, StudioAgentPromptRequest, StudioAgentSurface, StudioAgentToolResult } from "../../sdk";
import { createAgentClient, type AgentClient } from "../agent/agentClient";
import { collectAgentContext } from "../agent/agentContext";
import { getActiveAgentCapabilities, getActivePromptStarters } from "../agent/agentContributions";
import { getAgentErrorMessage } from "../agent/agentErrors";
import { subscribeAgentSessionStream, type AgentStreamSubscription } from "../agent/agentStream";
import { createClientInvocationRunner } from "../agent/clientInvocationRunner";
import type {
  AgentActionProposal,
  AgentActionProposalPayload,
  AgentAutonomyMode,
  AgentBootstrapResponse,
  AgentMessageViewModel,
  AgentPlanStep,
  AgentProviderDiagnostics,
  AgentStreamEvent
} from "../agent/agentTypes";

export type WeaverChatState = "idle" | "streaming";
// The Weaver "auto-apply" control selects the session's server-side autonomy mode.
export type WeaverAutoApplyMode = AgentAutonomyMode;

const ALL_AUTONOMY_MODES: readonly WeaverAutoApplyMode[] = ["manual", "auto-read-only", "full-auto"];

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
  autoApplyMode: WeaverAutoApplyMode;
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
  submitFeedback(messageId: string, rating: "positive" | "negative"): void;
  setAutoApplyMode(mode: WeaverAutoApplyMode): void;
}

const ASSISTANT_PLACEHOLDER = "Awaiting assistant response…";

const DISABLED_POLICY: AgentBootstrapResponse["policy"] = {
  contextVisibility: true,
  defaultAutonomyMode: "auto-read-only",
  maxAutonomyMode: "auto-read-only",
  allowedAutonomyModes: ["manual", "auto-read-only"]
};

export function useWeaverSession({ api, surface, client, subscribeStream = subscribeAgentSessionStream }: UseWeaverSessionOptions): UseWeaverSession {
  const [bootstrap, setBootstrap] = useState<AgentBootstrapResponse | null>(null);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [contextDiagnostics, setContextDiagnostics] = useState<StudioAgentContributionDiagnostic[]>([]);
  const [attachments, setAttachments] = useState<StudioAgentContextAttachment[]>([]);
  const [messages, setMessages] = useState<AgentMessageViewModel[]>([]);
  const [timeline, setTimeline] = useState<WeaverTimelineEntry[]>([]);
  const [plan, setPlan] = useState<AgentPlanStep[]>([]);
  const [toolResults, setToolResults] = useState<StudioAgentToolResult[]>([]);
  const [proposals, setProposals] = useState<AgentActionProposal[]>([]);
  const [followups, setFollowups] = useState<WeaverFollowup[]>([]);
  const [chatState, setChatState] = useState<WeaverChatState>("idle");
  const [error, setError] = useState<string | null>(null);
  const [autoApplyMode, setAutoApplyMode] = useState<WeaverAutoApplyMode>("manual");
  const [pendingProposalIds, setPendingProposalIds] = useState<ReadonlySet<string>>(new Set());

  const clientControllersRef = useRef(new Set<AbortController>());
  const turnAbortRef = useRef<AbortController | null>(null);
  const turnIdRef = useRef<string | null>(null);
  // Bumped whenever a turn starts or is stopped, so an in-flight startTurn can detect that it was
  // superseded/cancelled during one of its awaits and bail out before re-subscribing a stream.
  const turnGenerationRef = useRef(0);
  // Set when Stop is pressed after the stream is open but before 'turn-started' reveals the turn id.
  // The stream is kept open just long enough to learn the id and cancel the turn server-side.
  const stopRequestedRef = useRef(false);
  const sessionIdRef = useRef<string | null>(null);
  const streamRef = useRef<AgentStreamSubscription | null>(null);
  const pendingProposalIdsRef = useRef(new Set<string>());
  const autoApplyModeRef = useRef(autoApplyMode);
  autoApplyModeRef.current = autoApplyMode;
  // Correlation id for the in-flight turn, when a caller asked for the response routed back (see send()).
  const turnRequestIdRef = useRef<string | null>(null);
  // Accumulated assistant text per message id, so a correlated turn can publish its final answer.
  const assistantTextByIdRef = useRef(new Map<string, string>());

  const clientRunner = useMemo(() => createClientInvocationRunner(api), [api]);
  const agentClient = useMemo(() => client ?? createAgentClient(api.backend), [api.backend, client]);
  const mode: StudioAgentMode = bootstrap?.modes[0] ?? "explain";
  const contributionFilter = useMemo(() => ({
    policy: {
      allowedCapabilityIds: bootstrap?.enabled === true ? bootstrap.capabilities.map(capability => capability.id) : []
    }
  }), [bootstrap]);
  const capabilities = useMemo(() => getActiveAgentCapabilities(api, surface, contributionFilter), [api, surface, contributionFilter]);
  const promptStarters = useMemo(() => getActivePromptStarters(api, surface, capabilities, contributionFilter), [api, surface, capabilities, contributionFilter]);
  const activeProvider = useMemo(() => bootstrap?.provider, [bootstrap]);
  const ready = useMemo(() => bootstrap?.enabled === true && bootstrap.providerStatus !== "unavailable" && capabilities.length > 0, [bootstrap, capabilities]);
  // The deployment ceiling determines which autonomy modes the dock may offer.
  const allowedAutoApplyModes = useMemo<readonly WeaverAutoApplyMode[]>(
    () => bootstrap?.policy.allowedAutonomyModes?.length ? bootstrap.policy.allowedAutonomyModes : ALL_AUTONOMY_MODES,
    [bootstrap]);
  const allowedAutoApplyModesRef = useRef(allowedAutoApplyModes);
  allowedAutoApplyModesRef.current = allowedAutoApplyModes;
  // Belt-and-suspenders: never let a requested mode exceed the ceiling, even if the UI offered it.
  const selectAutoApplyMode = useCallback((mode: WeaverAutoApplyMode) => {
    setAutoApplyMode(current => allowedAutoApplyModesRef.current.includes(mode) ? mode : current);
  }, []);

  useEffect(() => () => {
    streamRef.current?.close(); turnAbortRef.current?.abort();
    for (const controller of clientControllersRef.current) controller.abort();
  }, []);

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
          setAutoApplyMode(result.policy.defaultAutonomyMode);
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
  }, [agentClient, api]);

  const ensureSession = useCallback(async (collected: StudioAgentContextAttachment[]) => {
    if (sessionIdRef.current) return sessionIdRef.current;
    const response = await agentClient.createSession({
      mode,
      autonomyMode: autoApplyModeRef.current,
      activeSurface: surface,
      clientContext: { studioVersion: api.host.hostVersion, sdkVersion: api.host.sdkVersion, moduleIds: [] }
    });
    sessionIdRef.current = response.sessionId;
    setSessionId(response.sessionId);
    setAttachments(mergeAttachments(collected, response.contextAttachments));
    return response.sessionId;
  }, [agentClient, api.host.hostVersion, api.host.sdkVersion, mode, surface]);

  const handleStreamEvent = useCallback((event: AgentStreamEvent) => {
    if (stopRequestedRef.current) {
      // Stopped before we knew the turn id: cancel the turn as soon as it identifies itself,
      // tear down on any terminal event, and drop everything else so no content keeps streaming.
      if (event.type === "turn-started") {
        stopRequestedRef.current = false;
        turnIdRef.current = event.turnId;
        if (sessionIdRef.current) void agentClient.cancelTurn(sessionIdRef.current, event.turnId).catch(() => undefined);
        streamRef.current?.close();
        streamRef.current = null;
      } else if (event.type === "message-completed" || event.type === "turn-cancelled" || event.type === "error") {
        stopRequestedRef.current = false;
        streamRef.current?.close();
        streamRef.current = null;
      }
      return;
    }

    switch (event.type) {
      case "module-result":
        setToolResults(current => [...current.filter(item => item.toolCallId !== event.result.toolCallId), event.result]);
        break;
      case "turn-started":
        turnIdRef.current = event.turnId;
        break;
      case "message-started":
        if (event.role === "assistant") assistantTextByIdRef.current.set(event.messageId, "");
        setMessages(current => upsertMessage(current, { id: event.messageId, role: event.role, content: "", status: "streaming" }));
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
      case "clarification-requested":
        setMessages(current => [...current, { id: event.clarification.id, role: "assistant", content: event.clarification.prompt, status: "completed" }]);
        // Weaver needs more input from the user in the chat; release any correlated caller so it stops waiting.
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
        setMessages(current => current.map(message => message.status === "streaming" ? { ...message, status: "cancelled" } : message));
        publishTurnResult("cancelled", "");
        finishTurn();
        break;
      case "error":
        setError(event.message);
        setMessages(current => [...current, { id: `error-${current.length}`, role: "error", content: event.message, status: "failed" }]);
        publishTurnResult("failed", "");
        finishTurn();
        break;
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeProvider, agentClient]);

  function finishTurn() {
    setChatState("idle");
    setFollowups([]);
    turnIdRef.current = null;
  }

  // Route a correlated turn's outcome back to the caller (e.g. the Create dialog) exactly once. Defaults
  // to the in-flight turn, but a superseded/errored turn can pass its own captured id (the ref may have
  // since moved on to a newer turn — in that case we publish without clearing the newer turn's id).
  const publishTurnResult = useCallback((status: "completed" | "cancelled" | "failed", text: string, requestId = turnRequestIdRef.current) => {
    if (!requestId) return;
    if (turnRequestIdRef.current === requestId) turnRequestIdRef.current = null;
    api.agent.publishPromptResult({ requestId, status, text, autoApply: autoApplyModeRef.current === "full-auto" });
  }, [api]);

  const startTurn = useCallback(async (request: StudioAgentPromptRequest) => {
    const { message, requestId } = request;
    const deliveryId = requestId ?? globalThis.crypto?.randomUUID?.() ?? `message-${Date.now()}-${Math.random()}`;
    const generation = ++turnGenerationRef.current;
    turnAbortRef.current?.abort();
    const turnAbort = new AbortController(); turnAbortRef.current = turnAbort;
    stopRequestedRef.current = false;
    turnRequestIdRef.current = requestId ?? null;
    setChatState("streaming");
    setError(null);
    setTimeline([]);
    setPlan([]);
    setMessages(current => [...current, { id: `user-${current.length}`, role: "user", content: message, status: "completed" }]);

    // Release a correlated caller from before the stream opens (supersede/error), so it never hangs.
    // Pass the captured requestId because a supersede may have already moved turnRequestIdRef on.
    const settle = (status: "cancelled" | "failed") => publishTurnResult(status, "", requestId);

    try {
      const collection = await collectAgentContext(api, surface, mode, sessionIdRef.current ?? undefined, request.attachments, turnAbort.signal);
      setContextDiagnostics(collection.diagnostics);
      const missing = collection.diagnostics.filter(item => item.required);
      if (missing.length) throw new Error(missing.map(item => item.message).join(" "));
      const collected = collection.attachments;
      if (turnGenerationRef.current !== generation) return settle("cancelled");
      setAttachments(collected);
      const activeSessionId = await ensureSession(collected);
      if (turnGenerationRef.current !== generation) return settle("cancelled");
      const response = await agentClient.sendMessage(activeSessionId, { message, mode, contextAttachments: collected, capabilityId: capabilities[0]?.id, requestId: deliveryId, source: request.source, promptMode: request.mode });
      if (response.warnings?.length) setContextDiagnostics(current => [...current, ...response.warnings!.filter(item => item.code !== "agent.message.duplicate").map(item => ({ contributorId: item.target ?? "agent", code: item.code, message: item.message, required: false }))]);
      if (response.status !== "pending" && response.warnings?.some(item => item.code === "agent.message.duplicate")) {
        setError("This delivery was already accepted; it will not run again. Review the existing session outcome.");
        settle("failed"); finishTurn(); return;
      }
      // The user stopped (or started another turn) while we were awaiting; do not re-open a stream.
      if (turnGenerationRef.current !== generation) return settle("cancelled");
      setMessages(current => [...current, { id: response.messageId, role: "assistant", content: ASSISTANT_PLACEHOLDER, status: "streaming" }]);
      streamRef.current?.close();
      streamRef.current = subscribeStream(
        api.backend,
        response.streamUrl,
        handleStreamEvent,
        streamError => { setError(getAgentErrorMessage(streamError)); publishTurnResult("failed", ""); finishTurn(); },
        { defaultMessageId: response.messageId });
    } catch (e) {
      if (turnGenerationRef.current !== generation) return settle("cancelled");
      settle("failed");
      setError(getAgentErrorMessage(e));
      setMessages(current => [...current, { id: `error-${current.length}`, role: "error", content: getAgentErrorMessage(e), status: "failed" }]);
      finishTurn();
    }
  }, [agentClient, api, capabilities, ensureSession, handleStreamEvent, mode, publishTurnResult, subscribeStream, surface]);

  const steer = useCallback(async (message: string) => {
    const id = `followup-${Date.now()}-${Math.random()}`;
    setFollowups(current => [...current, { id, message }]);
    try {
      const session = sessionIdRef.current;
      if (!session) throw new Error("The running session is unavailable.");
      await agentClient.sendMessage(session, { message, mode, requestId: id, promptMode: "steer", contextAttachments: [], capabilityId: capabilities[0]?.id });
    } catch (error) { setFollowups(current => current.filter(item => item.id !== id)); setError(getAgentErrorMessage(error)); }
  }, [agentClient, capabilities, mode]);

  const send = useCallback((value: string | StudioAgentPromptRequest) => {
    const request = typeof value === "string" ? { message: value } : value;
    const message = request.message.trim();
    if (!message || !ready) return;
    if (request.agent) {
      setError("The requested assistant is unavailable; the prompt was not delivered.");
      if (request.requestId) api.agent.publishPromptResult({ requestId: request.requestId, status: "failed", text: "" });
      return;
    }
    if (chatState === "streaming") {
      if (!request.requestId && !request.source && !request.attachments?.length && request.mode !== "enqueue") { void steer(message); return; }
      setError("Wait for the current turn before sending this contextual request; the prompt was not delivered.");
      if (request.requestId) api.agent.publishPromptResult({ requestId: request.requestId, status: "failed", text: "" });
      return;
    }
    void startTurn({ ...request, message });
  }, [api, chatState, ready, startTurn, steer]);

  const stop = useCallback(() => {
    // Invalidate any startTurn still awaiting so it can't re-subscribe a stream after we cancel.
    turnGenerationRef.current++;
    turnAbortRef.current?.abort();
    for (const controller of clientControllersRef.current) controller.abort();
    const sessionId = sessionIdRef.current;
    const turnId = turnIdRef.current;
    if (sessionId && turnId) {
      void agentClient.cancelTurn(sessionId, turnId).catch(() => undefined);
      streamRef.current?.close();
      streamRef.current = null;
    } else if (sessionId && streamRef.current) {
      // The stream is open but 'turn-started' hasn't arrived yet, so we don't know the turn id.
      // Keep listening until it does (handleStreamEvent cancels + tears down) instead of closing
      // blind and leaving the backend turn running.
      stopRequestedRef.current = true;
    } else {
      streamRef.current?.close();
      streamRef.current = null;
    }
    setMessages(current => current.map(message => message.status === "streaming" ? { ...message, status: "cancelled" } : message));
    publishTurnResult("cancelled", "");
    finishTurn();
  }, [agentClient, publishTurnResult]);

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

  const approveProposal = useCallback((proposal: AgentActionProposal) => void withProposalDecision(proposal.id, async () => {
    const response = await agentClient.approveProposal(proposal.id, { revision: proposal.revision });
    updateProposal(proposal.id, { status: response.approvalStatus });
  }), [agentClient, updateProposal, withProposalDecision]);

  const denyProposal = useCallback((proposal: AgentActionProposal) => void withProposalDecision(proposal.id, async () => {
    const response = await agentClient.denyProposal(proposal.id, { revision: proposal.revision });
    updateProposal(proposal.id, { status: response.approvalStatus });
  }), [agentClient, updateProposal, withProposalDecision]);

  const resumeFromReceipt = useCallback((session: string) => {
    streamRef.current?.close();
    setChatState("streaming");
    streamRef.current = subscribeStream(api.backend, `/_elsa/agent/sessions/${encodeURIComponent(session)}/stream`, handleStreamEvent,
      streamError => { setError(getAgentErrorMessage(streamError)); finishTurn(); });
  }, [api.backend, handleStreamEvent, subscribeStream]);

  const executeProposal = useCallback((proposal: AgentActionProposal) => void withProposalDecision(proposal.id, async () => {
    let response = await agentClient.executeProposal(proposal.id, { revision: proposal.revision });
    updateProposal(proposal.id, { status: response.approvalStatus });
    if (response.clientInvocation) {
      const invocation = response.clientInvocation;
      const deliveryGeneration = turnGenerationRef.current;
      const cancellation = new AbortController(); clientControllersRef.current.add(cancellation);
      turnIdRef.current = invocation.turnId;
      setChatState("streaming");
      try {
        const acknowledgement = await clientRunner.run(invocation, cancellation.signal);
        response = await agentClient.acknowledgeClient(invocation.sessionId, invocation.invocationId, acknowledgement);
        // Continue the retained turn from its stored receipt; never resend the original prompt.
        if (deliveryGeneration === turnGenerationRef.current) resumeFromReceipt(invocation.sessionId);
      } catch (error) {
        updateProposal(proposal.id, { status: "uncertain", error: "The dispatched change could not be confirmed. It will not be replayed." });
        setChatState("idle");
        throw error;
      } finally { clientControllersRef.current.delete(cancellation); }
    } else if (response.toolResult) {
      const session = proposal.preparedInvocation?.sessionId ?? sessionIdRef.current;
      if (session) resumeFromReceipt(session);
    }
    updateProposal(proposal.id, { status: response.approvalStatus, toolResult: response.toolResult, result: response.toolResult?.payload, resultType: response.toolResult?.payload?.typeId });
  }), [agentClient, clientRunner, resumeFromReceipt, updateProposal, withProposalDecision]);

  function maybeAutoApplyProposal(proposalId: string, payload?: AgentActionProposalPayload) {
    const prepared = payload?.preparedInvocation;
    if (autoApplyModeRef.current !== "full-auto" || !prepared || prepared.requiresApproval || prepared.executionLocation === "clientWorkingState") return;
    const revision = payload?.revision ?? payload?.baseRevision;
    void withProposalDecision(proposalId, async () => {
      await agentClient.approveProposal(proposalId, { revision });
      const response = await agentClient.executeProposal(proposalId, { revision });
      updateProposal(proposalId, { status: response.approvalStatus, toolResult: response.toolResult, result: response.toolResult?.payload, resultType: response.toolResult?.payload?.typeId });
      if (response.toolResult) resumeFromReceipt(prepared.sessionId);
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
    submitFeedback,
    setAutoApplyMode: selectAutoApplyMode
  };
}

function mergeAttachments(left: StudioAgentContextAttachment[], right: StudioAgentContextAttachment[]) {
  const byId = new Map(left.map(attachment => [attachment.id, attachment]));
  for (const attachment of right) byId.set(attachment.id, attachment);
  return [...byId.values()];
}

function upsertMessage(messages: AgentMessageViewModel[], next: AgentMessageViewModel) {
  return messages.some(message => message.id === next.id) ? messages.map(message => message.id === next.id ? next : message) : [...messages, next];
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
