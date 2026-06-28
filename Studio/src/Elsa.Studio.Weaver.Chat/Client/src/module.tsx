import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Bot, MessageSquare, RefreshCcw, Send } from "lucide-react";
import type { ElsaStudioModuleApi, StudioAiContextAttachment, StudioAiPromptRequest } from "@elsa-workflows/studio-sdk";
import { getWeaverCapabilities, listWeaverTools, streamWeaverChat, type WeaverCapabilities, type WeaverTool } from "./weaverClient";
import "./styles.css";

type ChatState = "idle" | "streaming";
type CapabilityState = "loading" | "ready" | "unavailable";

interface WeaverMessage {
  id: string;
  role: "user" | "assistant" | "system";
  content: string;
}

interface QueuedPrompt extends StudioAiPromptRequest {
  id: string;
}

const promptSubscribers = new Set<(prompt: QueuedPrompt) => void>();
const pendingPrompts: QueuedPrompt[] = [];
let promptSequence = 0;

export function register(api: ElsaStudioModuleApi) {
  api.navigation.add({
    id: "weaver",
    label: "Weaver",
    path: "/weaver",
    order: 30,
    iconColor: "#2563eb"
  });

  api.routes.add({
    id: "weaver-chat",
    path: "/weaver",
    label: "Weaver",
    component: () => <WeaverChatSurface api={api} variant="route" />
  });

  api.panels.add({
    id: "weaver-chat",
    title: "Weaver",
    order: 40,
    component: () => <WeaverChatSurface api={api} variant="panel" />
  });

  api.ai.surfaces.add({
    id: "weaver-chat",
    title: "Weaver chat",
    placement: "panel",
    moduleId: "Elsa.Studio.Weaver.Chat"
  });

  api.ai.onPrompt(prompt => publishPrompt(prompt));
}

function WeaverChatSurface({ api, variant }: { api: ElsaStudioModuleApi; variant: "route" | "panel" }) {
  const [capabilityState, setCapabilityState] = useState<CapabilityState>("loading");
  const [capabilities, setCapabilities] = useState<WeaverCapabilities | null>(null);
  const [tools, setTools] = useState<WeaverTool[]>([]);
  const [messages, setMessages] = useState<WeaverMessage[]>([]);
  const [queuedPrompts, setQueuedPrompts] = useState<QueuedPrompt[]>([]);
  const [input, setInput] = useState("");
  const [selectedAgent, setSelectedAgent] = useState<string>("");
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [chatState, setChatState] = useState<ChatState>("idle");
  const [error, setError] = useState("");
  const messagesRef = useRef<HTMLDivElement>(null);
  const pinnedToBottomRef = useRef(true);
  const promptActions = useMemo(() => api.ai.promptActions.list(), [api]);
  const contextProviders = useMemo(() => api.ai.contextProviders.list(), [api]);
  const proposalRenderers = useMemo(() => api.ai.proposalRenderers.list(), [api]);
  const surfaces = useMemo(() => api.ai.surfaces.list(), [api]);
  const activeAttachments = useMemo(
    () => queuedPrompts.flatMap(prompt => prompt.attachments ?? []).slice(0, 8),
    [queuedPrompts]
  );

  const loadCapabilities = useCallback(async () => {
    setCapabilityState("loading");
    setError("");
    try {
      const response = await getWeaverCapabilities(api.backend);
      setCapabilities(response);
      setSelectedAgent(current => current || response.agents[0]?.name || "");
      setCapabilityState(response.streaming ? "ready" : "unavailable");
      if (response.streaming) {
        setTools(await listWeaverTools(api.backend, selectedAgent || response.agents[0]?.name));
      }
    } catch (e) {
      setCapabilities(null);
      setTools([]);
      setCapabilityState("unavailable");
      setError(e instanceof Error ? e.message : String(e));
    }
  }, [api.backend, selectedAgent]);

  useEffect(() => {
    void loadCapabilities();
  }, [loadCapabilities]);

  useEffect(() => {
    const subscriber = (prompt: QueuedPrompt) => {
      setQueuedPrompts(current => [...current, prompt]);
      if (prompt.mode === "steer") {
        setInput(prompt.message);
      }
    };

    promptSubscribers.add(subscriber);
    if (pendingPrompts.length > 0) {
      const pending = pendingPrompts.splice(0);
      for (const prompt of pending) {
        subscriber(prompt);
      }
    }
    return () => {
      promptSubscribers.delete(subscriber);
    };
  }, []);

  useEffect(() => {
    if (capabilityState !== "ready" || chatState !== "idle") return;

    const prompt = queuedPrompts.find(item => item.mode !== "steer");
    if (!prompt) return;

    submitQueuedPrompt(prompt);
  }, [capabilityState, chatState, queuedPrompts]);

  useEffect(() => {
    const container = messagesRef.current;
    // Only follow new content when the user is already at the bottom, so streaming
    // deltas don't yank them away while they scroll up to read earlier output.
    if (container && pinnedToBottomRef.current) container.scrollTop = container.scrollHeight;
  }, [messages]);

  function handleMessagesScroll() {
    const container = messagesRef.current;
    if (container) pinnedToBottomRef.current = container.scrollHeight - container.scrollTop - container.clientHeight < 24;
  }

  async function submitPrompt(prompt: StudioAiPromptRequest) {
    const message = prompt.message.trim();
    if (!message || capabilityState !== "ready" || chatState === "streaming") return;

    const assistantMessageId = createId("assistant");
    setChatState("streaming");
    setError("");
    setInput("");
    setMessages(current => [
      ...current,
      { id: createId("user"), role: "user", content: message },
      { id: assistantMessageId, role: "assistant", content: "" }
    ]);

    try {
      await streamWeaverChat(api.backend, {
        conversationId,
        message,
        agent: prompt.agent ?? selectedAgent,
        attachments: prompt.attachments
      }, event => {
        if (event.conversationId) setConversationId(event.conversationId);
        if (event.type === "assistant.delta" && typeof event.data.content === "string") {
          appendAssistantDelta(assistantMessageId, event.data.content);
        } else if (event.type === "conversation.error" && typeof event.data.content === "string") {
          appendAssistantDelta(assistantMessageId, event.data.content);
        }
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      appendAssistantDelta(assistantMessageId, "Weaver is unavailable for this request.");
    } finally {
      setChatState("idle");
    }
  }

  function appendAssistantDelta(messageId: string, delta: string) {
    setMessages(current => current.map(message =>
      message.id === messageId ? { ...message, content: `${message.content}${delta}` } : message));
  }

  function submitQueuedPrompt(prompt: QueuedPrompt) {
    setQueuedPrompts(current => current.filter(item => item.id !== prompt.id));
    void submitPrompt(prompt);
  }

  function submitInput() {
    void submitPrompt({ message: input, agent: selectedAgent || null, mode: "enqueue" });
  }

  function changeAgent(name: string) {
    if (name === selectedAgent) return;
    setSelectedAgent(name);

    // The backend pins a conversation to the provider it was created with, so an in-flight
    // conversation keeps responding from the original agent. Start a fresh conversation so the
    // newly selected agent actually handles the next message.
    if (conversationId) {
      setConversationId(null);
      const label = capabilities?.agents.find(agent => agent.name === name)?.displayName || name;
      setMessages(current => current.length === 0
        ? current
        : [...current, { id: createId("system"), role: "system", content: `Switched to ${label}. Starting a new conversation.` }]);
    }
  }

  return (
    <section className={variant === "panel" ? "weaver-surface panel" : "weaver-surface"}>
      <div className="weaver-header">
        <div>
          <h2>Weaver</h2>
          <p>{capabilitySummary(capabilityState, capabilities)}</p>
        </div>
        <button type="button" className="weaver-icon-button" aria-label="Refresh Weaver capabilities" title="Refresh" onClick={() => void loadCapabilities()}>
          <RefreshCcw size={15} />
        </button>
      </div>

      {error ? <div className="weaver-alert">{error}</div> : null}

      <div className="weaver-layout">
        <aside className="weaver-sidebar">
          <label className="weaver-field">
            <span>Agent</span>
            <select value={selectedAgent} onChange={event => changeAgent(event.target.value)} disabled={capabilityState !== "ready" || chatState === "streaming"}>
              {capabilities?.agents.length ? capabilities.agents.map(agent => (
                <option key={agent.name} value={agent.name}>{agent.displayName || agent.name}</option>
              )) : <option value="">No agent</option>}
            </select>
          </label>
          <ContextStack attachments={activeAttachments} />
          <ContributionSummary
            promptActions={promptActions.length}
            contextProviders={contextProviders.length}
            proposalRenderers={proposalRenderers.length}
            surfaces={surfaces.length}
            tools={tools.length}
          />
        </aside>

        <main className="weaver-chat">
          <div className="weaver-messages" aria-live="polite" ref={messagesRef} onScroll={handleMessagesScroll}>
            {messages.length === 0 ? (
              <div className="weaver-empty">
                <Bot size={24} />
                <strong>Ask Weaver about the current Studio context.</strong>
                <span>Workflow modules and third-party modules can attach resources, tools, and proposals when their Weaver features are enabled.</span>
              </div>
            ) : messages.map(message => (
              <article className={`weaver-message ${message.role}`} key={message.id}>
                <span>{message.role === "user" ? "You" : message.role === "system" ? "System" : "Weaver"}</span>
                <p>{message.content || (chatState === "streaming" && message.role === "assistant" ? "Thinking..." : "")}</p>
              </article>
            ))}
          </div>

          {queuedPrompts.length > 0 ? (
            <div className="weaver-queue" aria-label="Queued Weaver prompts">
              {queuedPrompts.map(prompt => (
                <button type="button" key={prompt.id} onClick={() => submitQueuedPrompt(prompt)} disabled={chatState === "streaming" || capabilityState !== "ready"}>
                  <MessageSquare size={13} />
                  <span>{prompt.source?.label ?? prompt.message}</span>
                </button>
              ))}
            </div>
          ) : null}

          <div className="weaver-composer">
            <textarea
              aria-label="Prompt Weaver"
              placeholder={capabilityState === "ready" ? "Ask Weaver..." : "Weaver backend is unavailable"}
              value={input}
              onChange={event => setInput(event.target.value)}
              disabled={capabilityState !== "ready"}
              rows={variant === "panel" ? 2 : 3}
            />
            <button type="button" onClick={submitInput} disabled={!input.trim() || capabilityState !== "ready" || chatState === "streaming"}>
              <Send size={15} />
              {chatState === "streaming" ? "Streaming" : "Send"}
            </button>
          </div>
        </main>
      </div>
    </section>
  );
}

function ContextStack({ attachments }: { attachments: StudioAiContextAttachment[] }) {
  return (
    <section className="weaver-context-stack">
      <h3>Context stack</h3>
      {attachments.length === 0 ? <p>No context attached.</p> : attachments.map((attachment, index) => (
        <div className="weaver-context-item" key={`${attachment.kind}-${attachment.referenceId ?? index}`}>
          <strong>{attachment.kind}</strong>
          <span>{attachment.referenceId ?? attachment.scope ?? "current selection"}</span>
        </div>
      ))}
    </section>
  );
}

function ContributionSummary({ promptActions, contextProviders, proposalRenderers, surfaces, tools }: {
  promptActions: number;
  contextProviders: number;
  proposalRenderers: number;
  surfaces: number;
  tools: number;
}) {
  return (
    <section className="weaver-contributions">
      <h3>Enabled contributions</h3>
      <span>{promptActions} prompt actions</span>
      <span>{contextProviders} context providers</span>
      <span>{proposalRenderers} proposal renderers</span>
      <span>{surfaces} surfaces</span>
      <span>{tools} backend tools</span>
    </section>
  );
}

function publishPrompt(prompt: StudioAiPromptRequest) {
  const queued = { ...prompt, id: createId("prompt") };
  if (promptSubscribers.size === 0) {
    pendingPrompts.push(queued);
    return;
  }

  for (const subscriber of promptSubscribers) {
    subscriber(queued);
  }
}

function capabilitySummary(state: CapabilityState, capabilities: WeaverCapabilities | null) {
  if (state === "loading") return "Checking backend AI capabilities.";
  if (state === "unavailable") return "Enable Elsa Server AI capabilities to use Weaver chat.";
  const persistence = capabilities?.conversationPersistence ? "durable sessions" : "ephemeral sessions";
  const proposals = capabilities?.proposalReview ? "proposal review" : "draft-only suggestions";
  return `${capabilities?.agents.length ?? 0} agent(s), ${persistence}, ${proposals}.`;
}

function createId(prefix: string) {
  return `${prefix}-${Date.now()}-${++promptSequence}`;
}
