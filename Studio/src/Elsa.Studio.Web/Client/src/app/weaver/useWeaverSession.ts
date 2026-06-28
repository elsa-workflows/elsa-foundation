import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ElsaStudioModuleApi, StudioAgentContextAttachment, StudioAgentMode, StudioAgentSurface } from "../../sdk";
import { createAgentClient, type AgentClient } from "../agent/agentClient";
import { collectAgentContext } from "../agent/agentContext";
import { getActiveAgentCapabilities, getActivePromptStarters } from "../agent/agentContributions";
import { getAgentErrorMessage } from "../agent/agentErrors";
import { subscribeAgentSessionStream, type AgentStreamSubscription } from "../agent/agentStream";
import type { WorkflowBatchReviewModel } from "../agent/AgentWorkflowBatchReview";
import {
  canDirectApplyWorkflowBatch,
  requestWorkflowBatchApply,
  requestWorkflowBatchUndo
} from "../agent/workflowGraphOperations";
import type {
  AgentActionProposal,
  AgentActionProposalPayload,
  AgentBootstrapResponse,
  AgentMessageViewModel,
  AgentPlanStep,
  AgentProviderDiagnostics,
  AgentStreamEvent,
  WorkflowGraphOperationBatch
} from "../agent/agentTypes";

export type WeaverChatState = "idle" | "streaming";
export type WeaverAutoApplyMode = "manual" | "auto-read-only" | "full-auto";

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
  workflowBatches: WorkflowBatchReviewModel[];
  attachments: StudioAgentContextAttachment[];
  followups: WeaverFollowup[];
  pendingProposalIds: ReadonlySet<string>;
  autoApplyMode: WeaverAutoApplyMode;
  promptStarters: ReturnType<typeof getActivePromptStarters>;
  capabilities: ReturnType<typeof getActiveAgentCapabilities>;
  activeProvider: AgentProviderDiagnostics | undefined;
  send(message: string): void;
  stop(): void;
  removeFollowup(id: string): void;
  approveProposal(proposal: AgentActionProposal): void;
  denyProposal(proposal: AgentActionProposal): void;
  executeProposal(proposal: AgentActionProposal): void;
  applyWorkflowBatch(batch: WorkflowBatchReviewModel): void;
  undoWorkflowBatch(batch: WorkflowBatchReviewModel): void;
  submitFeedback(messageId: string, rating: "positive" | "negative"): void;
  setAutoApplyMode(mode: WeaverAutoApplyMode): void;
}

const ASSISTANT_PLACEHOLDER = "Awaiting assistant response…";

export function useWeaverSession({ api, surface, client, subscribeStream = subscribeAgentSessionStream }: UseWeaverSessionOptions): UseWeaverSession {
  const [bootstrap, setBootstrap] = useState<AgentBootstrapResponse | null>(null);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [attachments, setAttachments] = useState<StudioAgentContextAttachment[]>([]);
  const [messages, setMessages] = useState<AgentMessageViewModel[]>([]);
  const [timeline, setTimeline] = useState<WeaverTimelineEntry[]>([]);
  const [plan, setPlan] = useState<AgentPlanStep[]>([]);
  const [proposals, setProposals] = useState<AgentActionProposal[]>([]);
  const [workflowBatches, setWorkflowBatches] = useState<WorkflowBatchReviewModel[]>([]);
  const [followups, setFollowups] = useState<WeaverFollowup[]>([]);
  const [chatState, setChatState] = useState<WeaverChatState>("idle");
  const [error, setError] = useState<string | null>(null);
  const [autoApplyMode, setAutoApplyMode] = useState<WeaverAutoApplyMode>("manual");
  const [pendingProposalIds, setPendingProposalIds] = useState<ReadonlySet<string>>(new Set());

  const turnIdRef = useRef<string | null>(null);
  const sessionIdRef = useRef<string | null>(null);
  const streamRef = useRef<AgentStreamSubscription | null>(null);
  const pendingProposalIdsRef = useRef(new Set<string>());
  const seenBatchIdsRef = useRef(new Set<string>());
  const autoApplyModeRef = useRef(autoApplyMode);
  autoApplyModeRef.current = autoApplyMode;

  const agentClient = useMemo(() => client ?? createAgentClient(api.backend), [api.backend, client]);
  const mode: StudioAgentMode = bootstrap?.modes.includes("troubleshoot") ? "troubleshoot" : bootstrap?.modes[0] ?? "troubleshoot";
  const contributionFilter = useMemo(() => ({
    policy: {
      allowedCapabilityIds: bootstrap?.enabled === true ? bootstrap.capabilities.map(capability => capability.id) : []
    }
  }), [bootstrap]);
  const capabilities = useMemo(() => getActiveAgentCapabilities(api, surface, contributionFilter), [api, surface, contributionFilter]);
  const promptStarters = useMemo(() => getActivePromptStarters(api, surface, capabilities, contributionFilter), [api, surface, capabilities, contributionFilter]);
  const activeProvider = useMemo(() => bootstrap?.providers?.find(provider => provider.isAvailable) ?? bootstrap?.providers?.[0], [bootstrap]);
  const ready = useMemo(() => bootstrap?.enabled === true && bootstrap.providerStatus !== "unavailable" && capabilities.length > 0, [bootstrap, capabilities]);

  useEffect(() => () => streamRef.current?.close(), []);

  useEffect(() => {
    let disposed = false;
    void (async () => {
      try {
        const result = await agentClient.bootstrap();
        if (!disposed) setBootstrap(result);
      } catch (e) {
        if (!disposed) {
          setBootstrap({ enabled: false, providerStatus: "unavailable", modes: [], capabilities: [], providers: [], policy: { contextVisibility: true, requiresApprovalForMutations: true } });
          setError(getAgentErrorMessage(e));
        }
      }
    })();
    return () => { disposed = true; };
  }, [agentClient]);

  const ensureSession = useCallback(async (collected: StudioAgentContextAttachment[]) => {
    if (sessionIdRef.current) return sessionIdRef.current;
    const response = await agentClient.createSession({
      mode,
      activeSurface: surface,
      clientContext: { studioVersion: api.host.hostVersion, sdkVersion: api.host.sdkVersion, moduleIds: [] }
    });
    sessionIdRef.current = response.sessionId;
    setSessionId(response.sessionId);
    setAttachments(current => mergeAttachments(collected, response.contextAttachments));
    return response.sessionId;
  }, [agentClient, api.host.hostVersion, api.host.sdkVersion, mode, surface]);

  const handleStreamEvent = useCallback((event: AgentStreamEvent) => {
    switch (event.type) {
      case "turn-started":
        turnIdRef.current = event.turnId;
        break;
      case "message-started":
        setMessages(current => upsertMessage(current, { id: event.messageId, role: event.role, content: "", status: "streaming" }));
        break;
      case "message-delta":
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
      case "workflow-batch-created": {
        const model = createBatch(event.batch, activeProvider);
        setWorkflowBatches(current => upsertBatch(current, model));
        if (!seenBatchIdsRef.current.has(model.id)) {
          seenBatchIdsRef.current.add(model.id);
          maybeAutoApplyBatch(model);
        }
        break;
      }
      case "clarification-requested":
        setMessages(current => [...current, { id: event.clarification.id, role: "assistant", content: event.clarification.prompt, status: "completed" }]);
        finishTurn();
        break;
      case "message-completed":
        setMessages(current => current.map(message => message.id === event.messageId ? { ...message, status: "completed" } : message));
        finishTurn();
        break;
      case "turn-cancelled":
        setMessages(current => current.map(message => message.status === "streaming" ? { ...message, status: "cancelled" } : message));
        finishTurn();
        break;
      case "error":
        setError(event.message);
        setMessages(current => [...current, { id: `error-${current.length}`, role: "error", content: event.message, status: "failed" }]);
        finishTurn();
        break;
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeProvider]);

  function finishTurn() {
    setChatState("idle");
    setFollowups([]);
    turnIdRef.current = null;
  }

  const startTurn = useCallback(async (message: string) => {
    setChatState("streaming");
    setError(null);
    setTimeline([]);
    setPlan([]);
    setMessages(current => [...current, { id: `user-${current.length}`, role: "user", content: message, status: "completed" }]);

    try {
      const collected = await collectAgentContext(api, surface, mode, sessionIdRef.current ?? undefined);
      setAttachments(collected);
      const activeSessionId = await ensureSession(collected);
      const response = await agentClient.sendMessage(activeSessionId, { message, mode, contextAttachments: collected, capabilityId: capabilities[0]?.id });
      setMessages(current => [...current, { id: response.messageId, role: "assistant", content: ASSISTANT_PLACEHOLDER, status: "streaming" }]);
      streamRef.current?.close();
      streamRef.current = subscribeStream(
        api.backend,
        response.streamUrl,
        handleStreamEvent,
        streamError => { setError(getAgentErrorMessage(streamError)); finishTurn(); },
        { defaultMessageId: response.messageId });
    } catch (e) {
      setError(getAgentErrorMessage(e));
      setMessages(current => [...current, { id: `error-${current.length}`, role: "error", content: getAgentErrorMessage(e), status: "failed" }]);
      finishTurn();
    }
  }, [agentClient, api, capabilities, ensureSession, handleStreamEvent, mode, subscribeStream, surface]);

  const steer = useCallback(async (message: string) => {
    const followupId = `followup-${Date.now()}`;
    // Shown as an in-flight chip until the turn ends (finishTurn clears it). The running turn folds the
    // message in between steps (server-side steering), so it is not added as a separate chat bubble here.
    setFollowups(current => [...current, { id: followupId, message }]);
    try {
      const activeSessionId = sessionIdRef.current ?? await ensureSession(attachments);
      await agentClient.sendMessage(activeSessionId, { message, mode, contextAttachments: [], capabilityId: capabilities[0]?.id });
    } catch (e) {
      setFollowups(current => current.filter(item => item.id !== followupId));
      setError(getAgentErrorMessage(e));
    }
  }, [agentClient, attachments, capabilities, ensureSession, mode]);

  const send = useCallback((message: string) => {
    const trimmed = message.trim();
    if (!trimmed || !ready) return;
    if (chatState === "streaming") void steer(trimmed);
    else void startTurn(trimmed);
  }, [chatState, ready, startTurn, steer]);

  const stop = useCallback(() => {
    streamRef.current?.close();
    streamRef.current = null;
    const turnId = turnIdRef.current;
    if (sessionIdRef.current && turnId) void agentClient.cancelTurn(sessionIdRef.current, turnId).catch(() => undefined);
    setMessages(current => current.map(message => message.status === "streaming" ? { ...message, status: "cancelled" } : message));
    finishTurn();
  }, [agentClient]);

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

  const executeProposal = useCallback((proposal: AgentActionProposal) => void withProposalDecision(proposal.id, async () => {
    const response = await agentClient.executeProposal(proposal.id, { revision: proposal.revision });
    updateProposal(proposal.id, { status: response.approvalStatus });
  }), [agentClient, updateProposal, withProposalDecision]);

  function maybeAutoApplyProposal(proposalId: string, payload?: AgentActionProposalPayload) {
    if (autoApplyModeRef.current !== "full-auto") return;
    const revision = payload?.revision ?? payload?.baseRevision;
    void withProposalDecision(proposalId, async () => {
      await agentClient.approveProposal(proposalId, { revision });
      const response = await agentClient.executeProposal(proposalId, { revision });
      updateProposal(proposalId, { status: response.approvalStatus });
    });
  }

  const applyWorkflowBatch = useCallback(async (item: WorkflowBatchReviewModel) => {
    if (item.status === "applying" || !item.canApply) return;
    setWorkflowBatches(current => current.map(batch => batch.id === item.id ? { ...batch, status: "applying", error: undefined } : batch));
    const response = await requestWorkflowBatchApply(item.batch);
    setWorkflowBatches(current => current.map(batch => {
      if (batch.id !== item.id) return batch;
      return response.ok
        ? { ...batch, status: "applied", result: response.result, error: undefined }
        : { ...batch, status: "failed", error: response.message };
    }));
    if (!response.ok) setError(response.message);
  }, []);

  const undoWorkflowBatch = useCallback(async (item: WorkflowBatchReviewModel) => {
    const undoToken = item.result?.undoToken;
    if (!undoToken) return;
    const response = await requestWorkflowBatchUndo(undoToken);
    setWorkflowBatches(current => current.map(batch => batch.id === item.id ? { ...batch, status: response.ok ? "undone" : batch.status, error: response.ok ? undefined : response.message } : batch));
    if (!response.ok) setError(response.message);
  }, []);

  function maybeAutoApplyBatch(model: WorkflowBatchReviewModel) {
    if (autoApplyModeRef.current === "manual") return;
    if (model.canApply) void applyWorkflowBatch(model);
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
    workflowBatches,
    attachments,
    followups,
    pendingProposalIds,
    autoApplyMode,
    promptStarters,
    capabilities,
    activeProvider,
    send,
    stop,
    removeFollowup,
    approveProposal,
    denyProposal,
    executeProposal,
    applyWorkflowBatch: item => void applyWorkflowBatch(item),
    undoWorkflowBatch: item => void undoWorkflowBatch(item),
    submitFeedback,
    setAutoApplyMode
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

function upsertBatch(batches: WorkflowBatchReviewModel[], next: WorkflowBatchReviewModel) {
  return batches.some(batch => batch.id === next.id)
    ? batches.map(batch => batch.id === next.id ? { ...batch, ...next, status: batch.status, result: batch.result, error: batch.error } : batch)
    : [...batches, next];
}

function createBatch(batch: WorkflowGraphOperationBatch, provider: AgentProviderDiagnostics | undefined): WorkflowBatchReviewModel {
  const canApply = canDirectApplyWorkflowBatch(batch, provider?.riskProfile, provider?.supportedOperations);
  return {
    id: `${batch.workflowDefinitionId}:${batch.baseRevision ?? "draft"}:${batch.operations.map(operation => operation.id).join(",")}`,
    batch,
    status: "ready",
    canApply,
    disabledReason: canApply ? undefined : "Provider requires review before direct apply."
  };
}

function normalizeProposal(proposalId: string, proposal?: AgentActionProposalPayload): AgentActionProposal {
  const revision = proposal?.revision ?? proposal?.baseRevision;
  return {
    id: proposal?.id ?? proposalId,
    title: proposal?.title ?? "Weaver proposal",
    summary: proposal?.summary ?? "Weaver prepared a change. Review the details before approving or applying it.",
    risk: proposal?.risk ?? "review-required",
    status: proposal?.status ?? "awaiting-approval",
    revision,
    reviewReady: Boolean(proposal?.title?.trim() && proposal?.summary?.trim() && revision),
    toolId: proposal?.toolId,
    moduleId: proposal?.moduleId,
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
