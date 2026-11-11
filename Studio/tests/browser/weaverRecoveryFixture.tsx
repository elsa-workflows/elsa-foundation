import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import { createEndpointContext, StudioHttpError, type StudioWeaverPreparedInvocation, type StudioBackendSession, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { createStudioRegistry } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/registry";
import { createAgentClient } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/agentClient";
import type { subscribeAgentSessionStream } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/agentStream";
import type { AgentStreamEvent } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/agentTypes";
import { WeaverSurface } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/weaver";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/agent.css";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/weaver/weaver.css";

// A durable fake Agent backend (#3441 T022). State lives in localStorage, so a reload behaves like a server that kept
// running and a second tab in the same browser context sees the same server. Requests go through the real Studio
// agent client; only the transport and the turn stream are simulated.
type Message = { id: string; role: string; content: string; status: string; statusReason?: string };
type Proposal = { id: string; title: string; summary: string; risk: string; status: string; baseRevision: string; resourceTarget: unknown; preparedInvocation?: StudioWeaverPreparedInvocation; ownerResolution?: { claim: string; actorId: string }; result?: unknown };
type Session = { id: string; owner: string; workspace?: string; continuedFrom?: string; title: string; status: string; updatedAt: string; messages: Message[]; proposals: Proposal[]; paused?: boolean };
type Store = { next: number; sessions: Session[]; log: string[] };

const params = new URLSearchParams(window.location.search);
const user = params.get("user") ?? "ada";
const storageKey = "weaver-recovery-fixture:server";
const read = (): Store => JSON.parse(localStorage.getItem(storageKey) ?? "null") ?? { next: 0, sessions: [], log: [] };
const write = (store: Store) => localStorage.setItem(storageKey, JSON.stringify(store));
const policy = { autonomyMode: "manual", maxAutonomyMode: "fullAuto", allowedAutonomyModes: ["manual", "autoReadOnly", "fullAuto"], revision: 1 };
const target = { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r1", displayName: "Sample one" };

const backendSession = {
  key: `fixture-${Math.random()}`,
  profile: { id: "fixture", name: "Fixture", baseUrl: "https://fixture.example/", environment: "Development", source: "configuration" },
  context: createEndpointContext("https://fixture.example/"),
  identity: { status: "authenticated", subject: user, tenantId: "tenant", roles: [], permissions: [] },
  capabilities: null,
  pendingWrites: 0,
  disposed: false,
  refreshCapabilities: async () => { throw new Error("unused"); }
} as unknown as StudioBackendSession;
const api = createStudioRegistry({ hostVersion: "1.0.0", sdkVersion: "1.0.0", ...createEndpointContext(window.location.origin + "/") }, { backendBaseUrl: "https://fixture.example/", backendSession });
api.weaver.capabilities.add({ id: "weaver.fixture", displayName: "Fixture", description: "Fixture capability.", kind: "answer", risk: "read-only", surfaces: ["*"] });
let releaseDelivery: (() => void) | null = null;
api.weaver.toolContracts.add({
  id: "sample_change", slotId: "sample", moduleId: "Sample", displayName: "Move sample", description: "Move the sample in the open editor.", surfaces: ["*"],
  inputSchema: {}, resourceTargetSchema: {}, resultSchema: {}, risk: "review-required", invocationModes: ["proposal"], contractVersion: "1", executionLocation: "clientWorkingState",
  clientTargetAvailability: () => params.get("target") === "gone" ? { status: "unavailable", reason: "The sample editor that held this unsaved change is closed." } : { status: "available" },
  executeClient: () => new Promise(resolve => { releaseDelivery = () => resolve({ outcome: "applied", summary: "Moved the sample.", result: { typeId: "sample.move", schemaVersion: "1", data: { moved: true } } }); })
});

function refuse(status: number, code: string, message: string): never {
  throw new StudioHttpError(status, message, null, { error: { code, message, statusCode: status } });
}

function owned(store: Store, id: string) {
  const session = store.sessions.find(item => item.id === id);
  if (!session || session.owner !== user) refuse(404, "weaver.session.not_found", `Agent session '${id}' was not found.`);
  return session;
}

function proposalOf(store: Store, proposalId: string) {
  for (const session of store.sessions) {
    const proposal = session.proposals.find(item => item.id === proposalId);
    if (proposal && session.owner === user) return { session, proposal };
  }
  return refuse(404, "weaver.proposal.not_found", "Not found.");
}

function details(session: Session) {
  return { sessionId: session.id, status: session.status, title: session.title, policy, contextAttachments: [], messages: session.messages };
}

function route(method: string, path: string, body: Record<string, unknown> = {}): unknown {
  const store = read();
  const url = new URL(path, "https://fixture.example/");
  const parts = url.pathname.split("/").filter(Boolean).slice(2); // after _elsa/agent
  const done = <T,>(value: T) => { write(store); return { data: value }; };
  if (method === "GET" && parts[0] === "bootstrap") {
    return { data: { enabled: true, providerStatus: "available", modes: ["explain"], capabilities: [{ id: "weaver.fixture", displayName: "Fixture", description: "", kind: "answer", risk: "readOnly", surfaces: ["*"] }],
      provider: { providerId: "fixture", isAvailable: true, status: "available", providerKind: "provider-sdk-binding", supportedOperations: ["chat"], riskProfile: "read-only", metadata: {} },
      policy: { contextVisibility: true, defaultAutonomyMode: "manual", maxAutonomyMode: "fullAuto", allowedAutonomyModes: ["manual", "autoReadOnly", "fullAuto"] } } };
  }
  if (parts[0] === "sessions" && parts.length === 1 && method === "POST") {
    const session: Session = { id: `agt_${++store.next}`, owner: user, workspace: body.workspace as string | undefined, continuedFrom: body.continuedFrom as string | undefined, title: "Studio assistant", status: "active", updatedAt: new Date().toISOString(), messages: [], proposals: [] };
    store.sessions.push(session); store.log.push("create");
    return done({ sessionId: session.id, status: "active", policy });
  }
  if (parts[0] === "sessions" && parts.length === 1 && method === "GET") {
    const workspace = url.searchParams.get("workspace");
    const items = store.sessions.filter(item => item.owner === user && (!workspace || item.workspace === workspace)).reverse()
      .map(item => ({ id: item.id, title: item.title, status: item.status, updatedAt: item.updatedAt, expiresAt: null, pendingReviewCount: item.proposals.filter(p => p.status === "awaitingApproval").length, recoveryState: null }));
    return { data: { items, next: null } };
  }
  if (parts[0] === "sessions") {
    const session = owned(store, decodeURIComponent(parts[1]));
    const rest = parts.slice(2);
    if (rest.length === 0) return { data: details(session) };
    if (rest[0] === "proposals") return { data: session.proposals };
    if (rest[0] === "messages" && rest.length === 1) {
      if (session.messages.some(item => item.role === "user" && (item.status === "pending" || item.status === "streaming"))) refuse(409, "weaver.message.delivery_conflict", "Another prompt is pending.");
      const message = { id: `msg_${store.next}_${session.messages.length + 1}`, role: "user", content: String(body.content ?? body.message), status: "pending" };
      session.messages.push(message); store.log.push("message");
      return done({ message: { id: message.id, status: "pending", context: [] }, warnings: [] });
    }
    if (rest[0] === "messages" && rest[2] === "withdraw") {
      const message = session.messages.find(item => item.id === rest[1]) ?? refuse(404, "weaver.message.not_found", "Not found.");
      if (message.status !== "pending") refuse(409, "weaver.message.not_pending", "The prompt already started.");
      Object.assign(message, { status: "cancelled", statusReason: "withdrawn" }); store.log.push("withdraw");
      return done(message);
    }
    if (rest[0] === "client-invocations") {
      const proposal = session.proposals.find(item => item.preparedInvocation?.invocationId === rest[1]) ?? refuse(404, "weaver.invocation.not_found", "Not found.");
      if (rest[2] === "resolution") {
        if (proposal.status !== "uncertain") refuse(409, "weaver.invocation.not_uncertain", "Not uncertain.");
        const claim = String(body.claim);
        if (proposal.ownerResolution && proposal.ownerResolution.claim !== claim) refuse(409, "weaver.invocation.resolution_conflict", "A different statement is recorded.");
        proposal.ownerResolution = { claim, actorId: user }; store.log.push(`resolve:${claim}`);
        return done(proposal);
      }
      const outcome = String(body.outcome);
      proposal.status = outcome === "applied" ? "executed" : outcome === "refused" ? "failed" : "uncertain";
      store.log.push(`ack:${outcome}`);
      return done({ proposalId: proposal.id, executed: outcome === "applied", status: proposal.status, resourceType: "sample", resourceId: "one", summary: String(body.summary) });
    }
    if (rest[0] === "turns") { store.log.push("cancel"); return done({ turnId: rest[1], cancelled: true }); }
    if (rest[0] === "autonomy") return done(policy);
  }
  if (parts[0] === "proposals") {
    const { proposal } = proposalOf(store, parts[1]);
    store.log.push(parts[2]);
    if (parts[2] === "approve") { proposal.status = "approved"; return done({ id: proposal.id, status: "approved", approvedAt: new Date().toISOString() }); }
    if (parts[2] === "deny") { proposal.status = "denied"; return done({ id: proposal.id, status: "denied" }); }
    if (parts[2] === "execute") {
      proposal.status = "awaitingClient";
      proposal.preparedInvocation = { ...proposal.preparedInvocation!, client: { clientId: api.weaver.clientId, generationId: "generation" }, expiresAt: new Date(Date.now() + 300_000).toISOString() };
      return done({ proposalId: proposal.id, executed: false, status: "awaitingClient", resourceType: "sample", resourceId: "one", summary: "Delivered to the originating page.", clientInvocation: proposal.preparedInvocation });
    }
  }
  if (parts[0] === "feedback") return done({ id: "feedback" });
  return refuse(404, "fixture.route", `No fixture route for ${method} ${path}.`);
}

const http = {
  async getJson(path: string) { return route("GET", path); },
  async postJson(path: string, body: Record<string, unknown>) { return route("POST", path, body); },
  async putJson(path: string, body: Record<string, unknown>) { return route("PUT", path, body); }
};
const context = { baseUrl: "https://fixture.example/", http } as unknown as StudioEndpointContext;
const client = createAgentClient(context);

// The turn stream: a turn runs only while it is open, as on the server.
const subscribeStream: typeof subscribeAgentSessionStream = (_context, url, onEvent, onError, options) => {
  let closed = false;
  const sessionId = decodeURIComponent(url.split("/")[4]);
  const replyId = options?.defaultMessageId ?? "reply";
  const emit = (event: AgentStreamEvent) => { if (!closed) onEvent(event); };
  void (async () => {
    const tick = () => new Promise(resolve => setTimeout(resolve, 30));
    const store = read();
    const session = store.sessions.find(item => item.id === sessionId)!;
    store.log.push("stream"); write(store);
    const prompt = session.messages.find(item => item.role === "user" && item.status === "pending");
    await tick();
    if (!prompt) { emit({ type: "message-completed", messageId: replyId }); return; }
    prompt.status = "streaming"; write(store);
    emit({ type: "turn-started", turnId: `turn_${prompt.id}`, maxSteps: 4 });
    await tick();
    if (prompt.content.includes("[limit]")) {
      // The documented session limit: the turn stops before the provider, and everything recorded stays.
      prompt.status = "failed"; write(store);
      emit({ type: "error", code: "weaver.context.disclosure_budget_exceeded", limit: "bytes", message: "Session limit reached: this conversation has recorded 8 MiB of recorded disclosure. Continue in a new session; nothing recorded here was discarded." });
      return;
    }
    if (prompt.content.includes("[drop]")) {
      emit({ type: "message-delta", messageId: replyId, content: "Partial answer" });
      await tick();
      // The connection drops: the server cancels the turn with no reason, and the partial answer is not accepted.
      Object.assign(prompt, { status: "cancelled" });
      session.messages.push({ id: `${prompt.id}_answer`, role: "assistant", content: "Partial answer", status: "failed" });
      write(store);
      if (!closed) onError(new Error("The connection to Weaver was lost."));
      return;
    }
    if (prompt.content.includes("[review]") || prompt.content.includes("[client]")) {
      const proposal: Proposal = { id: `prop_${prompt.id}`, title: prompt.content.includes("[client]") ? "Move sample" : "Rename sample", summary: "Change the sample after review.", risk: "reviewRequired", status: "awaitingApproval", baseRevision: "r1", resourceTarget: target,
        ...(prompt.content.includes("[client]") ? { preparedInvocation: { invocationId: `inv_${prompt.id}`, sessionId, turnId: `turn_${prompt.id}`, toolCallId: "call", toolName: "sample_change", toolContractVersion: "1", moduleId: "Sample", actorId: user, tenantId: "tenant", target, arguments: { x: 1 }, approvalDigest: "digest", risk: "reviewRequired", preview: { typeId: "sample.preview", schemaVersion: "1", data: {} }, executionLocation: "clientWorkingState", requiresApproval: true, client: null, expiresAt: new Date(Date.now() + 300_000).toISOString() } } : {}) };
      session.proposals.push(proposal); write(store);
      emit({ type: "proposal-created", proposalId: proposal.id, messageId: replyId, proposal: { ...proposal, risk: "review-required", status: "awaiting-approval" } as never });
      emit({ type: "review-requested", messageId: replyId });
      return;
    }
    emit({ type: "message-delta", messageId: replyId, content: `Answer to: ${prompt.content}` });
    prompt.status = "completed";
    session.messages.push({ id: `${prompt.id}_answer`, role: "assistant", content: `Answer to: ${prompt.content}`, status: "completed" });
    write(store);
    await tick();
    emit({ type: "message-completed", messageId: replyId });
  })();
  return { close() { closed = true; } };
};

Object.assign(window, {
  weaverFixture: {
    seed(sessions: Partial<Session>[]) {
      const store = read();
      for (const item of sessions) {
        store.sessions.push({ id: `agt_${++store.next}`, owner: "ada", workspace: "studio:fixture", title: "Studio assistant", status: "active", updatedAt: new Date().toISOString(), messages: [], proposals: [], ...item });
      }
      write(store);
      return store.sessions.map(item => item.id);
    },
    state: () => read(),
    log: () => read().log,
    release: () => releaseDelivery?.(),
    target
  }
});

function Page() {
  const [open, setOpen] = useState(true);
  return (
    <div style={{ display: "grid", gridTemplateColumns: "1fr minmax(0, 420px)", minHeight: "100vh" }}>
      <main style={{ padding: 16 }}>
        <p>Signed in as {user}.</p>
        <button type="button" onClick={() => setOpen(value => !value)}>{open ? "Hide Weaver" : "Show Weaver"}</button>
      </main>
      {open ? <WeaverSurface api={api} surface={{ route: "/sample" }} variant="dock" client={client} subscribeStream={subscribeStream} onClose={() => setOpen(false)} /> : null}
    </div>
  );
}

createRoot(document.getElementById("root")!).render(<ThemeProvider><Page /></ThemeProvider>);
