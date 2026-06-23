import React, { useEffect, useMemo, useRef, useState } from "react";
import { X } from "lucide-react";
import type { ElsaStudioModuleApi, StudioAgentContextAttachment, StudioAgentMode, StudioAgentSurface } from "../../sdk";
import { createAgentClient, type AgentClient } from "./agentClient";
import { collectAgentContext } from "./agentContext";
import { getActiveAgentCapabilities, getActivePromptStarters } from "./agentContributions";
import { getAgentErrorMessage } from "./agentErrors";
import { subscribeAgentSessionStream, type AgentStreamSubscription } from "./agentStream";
import { AgentComposer } from "./AgentComposer";
import { AgentContextChips } from "./AgentContextChips";
import { AgentMessageList } from "./AgentMessageList";
import { AgentPromptStarters } from "./AgentPromptStarters";
import { AgentProposalReview } from "./AgentProposalReview";
import type { AgentActionProposal, AgentActionProposalPayload, AgentBootstrapResponse, AgentMessageViewModel, AgentStreamEvent } from "./agentTypes";

export function AgentPanel({
  api,
  surface,
  onClose,
  client,
  subscribeStream = subscribeAgentSessionStream
}: {
  api: ElsaStudioModuleApi;
  surface: StudioAgentSurface;
  onClose(): void;
  client?: AgentClient;
  subscribeStream?: typeof subscribeAgentSessionStream;
}) {
  const [bootstrap, setBootstrap] = useState<AgentBootstrapResponse | null>(null);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [attachments, setAttachments] = useState<StudioAgentContextAttachment[]>([]);
  const [messages, setMessages] = useState<AgentMessageViewModel[]>([]);
  const [proposals, setProposals] = useState<AgentActionProposal[]>([]);
  const [busy, setBusy] = useState(false);
  const [pendingProposalIds, setPendingProposalIds] = useState<ReadonlySet<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const streamSubscriptionRef = useRef<AgentStreamSubscription | null>(null);
  const pendingProposalIdsRef = useRef(new Set<string>());
  const mode: StudioAgentMode = bootstrap?.modes.includes("troubleshoot") ? "troubleshoot" : bootstrap?.modes[0] ?? "troubleshoot";
  const agentClient = useMemo(() => client ?? createAgentClient(api.backend), [api.backend, client]);
  const contributionFilter = useMemo(() => ({
    policy: {
      allowedCapabilityIds: bootstrap?.enabled === true
        ? bootstrap.capabilities.map(capability => capability.id)
        : []
    }
  }), [bootstrap]);
  const capabilities = useMemo(() => getActiveAgentCapabilities(api, surface, contributionFilter), [api, surface, contributionFilter]);
  const promptStarters = useMemo(() => getActivePromptStarters(api, surface, capabilities, contributionFilter), [api, surface, capabilities, contributionFilter]);
  const disabled = busy || !canUseWeaver(bootstrap) || capabilities.length === 0;

  useEffect(() => {
    closeButtonRef.current?.focus();
    return () => streamSubscriptionRef.current?.close();
  }, []);

  useEffect(() => {
    let disposed = false;

    async function loadBootstrap() {
      try {
        setError(null);
        setBootstrap(await agentClient.bootstrap());
      } catch (e) {
        if (!disposed) {
          setBootstrap({
            enabled: false,
            providerStatus: "unavailable",
            modes: [],
            capabilities: [],
            policy: { contextVisibility: true, requiresApprovalForMutations: true }
          });
          setError(getAgentErrorMessage(e));
        }
      }
    }

    void loadBootstrap();
    return () => {
      disposed = true;
    };
  }, [agentClient]);

  async function sendMessage(message: string, capabilityId = capabilities[0]?.id) {
    if (busy || !canUseWeaver(bootstrap) || !capabilityId) {
      if (!canUseWeaver(bootstrap)) {
        setError("Weaver is still initializing or unavailable.");
      } else if (!capabilityId) {
        setError("Weaver has no enabled capabilities for this surface.");
      }
      return;
    }

    setBusy(true);
    setError(null);
    const userMessageId = `user-${Date.now()}`;
    setMessages(current => [...current, { id: userMessageId, role: "user", content: message, status: "completed" }]);

    try {
      const collected = await collectAgentContext(api, surface, mode, sessionId ?? undefined);
      setAttachments(collected);
      const activeSessionId = sessionId ?? await createSession(collected);
      const response = await agentClient.sendMessage(activeSessionId, {
        message,
        mode,
        contextAttachments: collected,
        capabilityId
      });

      setMessages(current => [
        ...current,
        {
          id: response.messageId,
          role: "assistant",
          content: "Awaiting assistant response...",
          status: response.status === "pending" ? "streaming" : response.status
        }
      ]);
      streamSubscriptionRef.current?.close();
      streamSubscriptionRef.current = subscribeStream(
        api.backend,
        response.streamUrl,
        handleStreamEvent,
        streamError => {
          setError(getAgentErrorMessage(streamError));
          setBusy(false);
        },
        { defaultMessageId: response.messageId });
    } catch (e) {
      const messageText = getAgentErrorMessage(e);
      setError(messageText);
      setMessages(current => [...current, { id: `error-${Date.now()}`, role: "error", content: messageText, status: "failed" }]);
      setBusy(false);
    }
  }

  async function createSession(collected: StudioAgentContextAttachment[]) {
    const response = await agentClient.createSession({
      mode,
      activeSurface: surface,
      clientContext: {
        studioVersion: api.host.hostVersion,
        sdkVersion: api.host.sdkVersion,
        moduleIds: []
      }
    });

    const nextAttachments = mergeAttachments(collected, response.contextAttachments);
    setAttachments(nextAttachments);
    setSessionId(response.sessionId);
    return response.sessionId;
  }

  async function submitFeedback(messageId: string, rating: "positive" | "negative") {
    try {
      await agentClient.submitFeedback(sessionId ?? "", messageId, { rating });
    } catch (e) {
      setError(getAgentErrorMessage(e));
    }
  }

  async function approveProposal(proposal: AgentActionProposal) {
    await withProposalDecision(proposal.id, async () => {
      const response = await agentClient.approveProposal(proposal.id, { revision: proposal.revision });
      updateProposalStatus(proposal.id, response.approvalStatus);
    });
  }

  async function denyProposal(proposal: AgentActionProposal) {
    await withProposalDecision(proposal.id, async () => {
      const response = await agentClient.denyProposal(proposal.id, { revision: proposal.revision });
      updateProposalStatus(proposal.id, response.approvalStatus);
    });
  }

  async function executeProposal(proposal: AgentActionProposal) {
    await withProposalDecision(proposal.id, async () => {
      const response = await agentClient.executeProposal(proposal.id, { revision: proposal.revision });
      updateProposalStatus(proposal.id, response.approvalStatus);
    });
  }

  async function withProposalDecision(proposalId: string, action: () => Promise<void>) {
    if (pendingProposalIdsRef.current.has(proposalId)) {
      return;
    }

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
  }

  function updateProposalStatus(proposalId: string, status: AgentActionProposal["status"]) {
    setProposals(current => current.map(proposal => proposal.id === proposalId ? { ...proposal, status } : proposal));
  }

  function handleStreamEvent(event: AgentStreamEvent) {
    switch (event.type) {
      case "message-started":
        setMessages(current => upsertMessage(current, { id: event.messageId, role: event.role, content: "", status: "streaming" }));
        break;
      case "message-delta":
        setMessages(current => upsertMessageDelta(current, event.messageId, event.content));
        break;
      case "message-completed":
        setMessages(current => current.map(message => message.id === event.messageId ? { ...message, status: "completed" } : message));
        setBusy(false);
        break;
      case "proposal-created":
        setProposals(current => upsertProposal(current, normalizeProposal(event.proposalId, event.proposal)));
        break;
      case "progress":
        setMessages(current => [
          ...current,
          {
            id: `progress-${Date.now()}`,
            role: "progress",
            content: event.percent === undefined ? event.label : `${event.label} (${event.percent}%)`,
            status: "streaming"
          }
        ]);
        break;
      case "error":
        setError(event.message);
        setMessages(current => [...current, { id: `error-${Date.now()}`, role: "error", content: event.message, status: "failed" }]);
        setBusy(false);
        break;
    }
  }

  return (
    <aside id="studio-agent-panel" className="agent-panel" aria-label="Weaver assistant" aria-busy={busy}>
      <header className="agent-panel-header">
        <div>
          <span>Weaver</span>
          <strong>Workflow assistant</strong>
        </div>
        <button ref={closeButtonRef} type="button" aria-label="Close assistant" onClick={onClose}>
          <X size={16} />
        </button>
      </header>

      {bootstrap?.enabled === false ? (
        <div className="agent-banner" data-tone="warning">
          Weaver features are disabled or unavailable. Studio remains fully usable.
        </div>
      ) : null}
      {error ? <div className="agent-banner" data-tone="danger">{error}</div> : null}

      <AgentContextChips attachments={attachments} />
      <AgentPromptStarters prompts={promptStarters} disabled={disabled} onSelect={(prompt, capabilityId) => sendMessage(prompt, capabilityId)} />
      <AgentMessageList messages={messages} onFeedback={submitFeedback} />
      <AgentProposalReview proposals={proposals} disabled={busy} pendingProposalIds={pendingProposalIds} onApprove={approveProposal} onDeny={denyProposal} onExecute={executeProposal} />
      <AgentComposer disabled={disabled} onSubmit={sendMessage} />
    </aside>
  );
}

function mergeAttachments(left: StudioAgentContextAttachment[], right: StudioAgentContextAttachment[]) {
  const byId = new Map(left.map(attachment => [attachment.id, attachment]));
  for (const attachment of right) {
    byId.set(attachment.id, attachment);
  }

  return [...byId.values()];
}

function upsertMessage(messages: AgentMessageViewModel[], next: AgentMessageViewModel) {
  return messages.some(message => message.id === next.id)
    ? messages.map(message => message.id === next.id ? next : message)
    : [...messages, next];
}

function upsertMessageDelta(messages: AgentMessageViewModel[], messageId: string, delta: string) {
  const existing = messages.find(message => message.id === messageId);
  if (!existing) {
    const message: AgentMessageViewModel = { id: messageId, role: "assistant", content: delta, status: "streaming" };
    return [...messages, message];
  }

  return messages.map(message => message.id === messageId
    ? { ...message, content: appendDelta(message.content, delta), status: "streaming" }
    : message);
}

function upsertProposal(proposals: AgentActionProposal[], next: AgentActionProposal) {
  return proposals.some(proposal => proposal.id === next.id)
    ? proposals.map(proposal => proposal.id === next.id ? next : proposal)
    : [...proposals, next];
}

function normalizeProposal(proposalId: string, proposal?: AgentActionProposalPayload): AgentActionProposal {
  const revision = proposal?.revision ?? proposal?.baseRevision;
  const reviewReady = Boolean(
    proposal?.title?.trim()
    && proposal?.summary?.trim()
    && revision
    && (proposal.operations?.length ?? 0) > 0
  );

  return {
    id: proposal?.id ?? proposalId,
    title: proposal?.title ?? "Weaver proposal",
    summary: proposal?.summary ?? "Weaver created a workflow-change proposal. Review details before approving or executing it.",
    risk: proposal?.risk ?? "review-required",
    status: proposal?.status ?? (reviewReady ? "awaiting-approval" : "draft"),
    revision,
    reviewReady,
    operations: proposal?.operations,
    risks: proposal?.risks,
    rollback: proposal?.rollback
  };
}

function appendDelta(current: string, delta: string) {
  return current === "Awaiting assistant response..." ? delta : `${current}${delta}`;
}

function canUseWeaver(bootstrap: AgentBootstrapResponse | null) {
  return bootstrap?.enabled === true && bootstrap.providerStatus !== "unavailable";
}
