import type { StudioCodeEditorSession } from "../types";

const maxSessions = 100;
const authorizationSessionEndedEvent = "elsa:auth-session-ended";
const authorizationSessionStartedEvent = "elsa:auth-session-started";
const expressionEditorSessionEndedEvent = "elsa:expression-editor-session-ended";
const expressionToolingAuthorizationRevokedEvent = "elsa:expression-tooling-authorization-revoked";
const expressionToolingAuthorizationRestoredEvent = "elsa:expression-tooling-authorization-restored";
const sessionScopeSeparator = "\u001f";
const sessions = new Map<string, StudioCodeEditorSessionImpl>();
const sourceBearingSessions = new Set<StudioCodeEditorSessionImpl>();
const revokedSessionScopes = new Set<string>();
const revocationListeners = new Set<(scope?: string) => void>();
let authorizationSessionRevoked = false;

class StudioCodeEditorSessionImpl implements StudioCodeEditorSession {
  private readonly entries = new Map<string, unknown>();
  private revoked = false;

  constructor(readonly id: string) {}

  get(documentUri: string) {
    const value = this.entries.get(documentUri);
    if (value !== undefined) {
      this.entries.delete(documentUri);
      this.entries.set(documentUri, value);
    }
    return value;
  }

  set(documentUri: string, value: unknown) {
    if (this.revoked) return;
    this.entries.delete(documentUri);
    this.entries.set(documentUri, value);
    sourceBearingSessions.add(this);
  }

  clear() {
    this.entries.clear();
    sourceBearingSessions.delete(this);
  }

  revoke() {
    this.clear();
    this.revoked = true;
  }
}

/** Creates a standalone, memory-only session for a mounted workflow editor. */
export function createStudioCodeEditorSession(id = createSessionId()): StudioCodeEditorSession {
  return new StudioCodeEditorSessionImpl(id);
}

/**
 * Resolves a bounded shared session for hosts that only have stable document
 * identity. Entries are process-local and are intentionally never persisted.
 */
export function getStudioCodeEditorSession(sessionKey: string): StudioCodeEditorSession {
  const existing = sessions.get(sessionKey);
  if (existing) {
    sessions.delete(sessionKey);
    sessions.set(sessionKey, existing);
    return existing;
  }

  const next = new StudioCodeEditorSessionImpl(sessionKey);
  sessions.set(sessionKey, next);
  while (sessions.size > maxSessions) {
    const evictedKey = sessions.keys().next().value!;
    const evicted = sessions.get(evictedKey)!;
    sessions.delete(evictedKey);
    evicted.clear();
  }
  return next;
}

export function clearStudioCodeEditorSession(session: StudioCodeEditorSession) {
  asInternalSession(session).clear();
}

/** Clears every source-bearing editor state, for example when authorization identity changes. */
export function clearAllStudioCodeEditorSessions() {
  authorizationSessionRevoked = true;
  for (const key of sessions.keys()) {
    const separator = key.indexOf(sessionScopeSeparator);
    if (separator >= 0) revokedSessionScopes.add(key.slice(0, separator));
  }
  for (const session of [...sourceBearingSessions]) session.revoke();
  sessions.clear();
  for (const listener of revocationListeners) listener(undefined);
}

/** Clears source and history retained for one workflow-editor lifetime. */
export function clearStudioCodeEditorSessionScope(scope: string) {
  const prefix = `${scope}${sessionScopeSeparator}`;
  for (const [key, session] of [...sessions]) {
    if (!key.startsWith(prefix)) continue;
    sessions.delete(key);
    session.revoke();
  }
}

/** Indicates whether a newly mounted editor belongs to a terminally revoked authorization scope. */
export function isStudioCodeEditorSessionRevoked(sessionKey: string) {
  if (authorizationSessionRevoked) return true;
  const separator = sessionKey.indexOf(sessionScopeSeparator);
  return separator >= 0 && revokedSessionScopes.has(sessionKey.slice(0, separator));
}

/** Observes authorization-driven revocation so mounted editors can remove active source-bearing views. */
export function subscribeToStudioCodeEditorSessionRevocation(listener: (scope?: string) => void) {
  revocationListeners.add(listener);
  return () => {
    revocationListeners.delete(listener);
  };
}

/** @internal Engine-only state storage. CodeMirror values never cross the public contract. */
export function getStudioCodeEditorSessionEntry<T>(session: StudioCodeEditorSession, documentUri: string): T | undefined {
  return asInternalSession(session).get(documentUri) as T | undefined;
}

/** @internal Engine-only state storage. */
export function setStudioCodeEditorSessionEntry<T>(session: StudioCodeEditorSession, documentUri: string, value: T) {
  asInternalSession(session).set(documentUri, value);
}

function asInternalSession(session: StudioCodeEditorSession) {
  if (!(session instanceof StudioCodeEditorSessionImpl)) {
    throw new Error("Studio code editor sessions must be created by Elsa.Studio.CodeEditor.");
  }
  return session;
}

function createSessionId() {
  return `studio-code-editor-${Math.random().toString(36).slice(2)}`;
}

if (typeof window !== "undefined")
  window.addEventListener(authorizationSessionEndedEvent, clearAllStudioCodeEditorSessions);
if (typeof window !== "undefined")
  window.addEventListener(authorizationSessionStartedEvent, () => {
    authorizationSessionRevoked = false;
  });
if (typeof window !== "undefined")
  window.addEventListener(expressionEditorSessionEndedEvent, event => {
    const scope = (event as CustomEvent<{ scope?: unknown }>).detail?.scope;
    if (typeof scope === "string") {
      clearStudioCodeEditorSessionScope(scope);
      revokedSessionScopes.delete(scope);
    }
  });
if (typeof window !== "undefined")
  window.addEventListener(expressionToolingAuthorizationRevokedEvent, event => {
    const scope = (event as CustomEvent<{ scope?: unknown }>).detail?.scope;
    if (typeof scope !== "string") {
      clearAllStudioCodeEditorSessions();
      return;
    }
    revokedSessionScopes.add(scope);
    clearStudioCodeEditorSessionScope(scope);
    for (const listener of revocationListeners) listener(scope);
  });
if (typeof window !== "undefined")
  window.addEventListener(expressionToolingAuthorizationRestoredEvent, event => {
    const scope = (event as CustomEvent<{ scope?: unknown }>).detail?.scope;
    if (typeof scope === "string") revokedSessionScopes.delete(scope);
  });
