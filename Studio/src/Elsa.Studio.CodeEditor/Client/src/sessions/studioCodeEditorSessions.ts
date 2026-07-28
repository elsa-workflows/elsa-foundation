import type { StudioCodeEditorSession } from "../types";

const maxSessions = 100;
const authorizationSessionEndedEvent = "elsa:auth-session-ended";
const sessions = new Map<string, StudioCodeEditorSessionImpl>();
const sourceBearingSessions = new Set<StudioCodeEditorSessionImpl>();
const revocationListeners = new Set<() => void>();

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
  for (const session of [...sourceBearingSessions]) session.revoke();
  sessions.clear();
  for (const listener of revocationListeners) listener();
}

/** Observes authorization-driven revocation so mounted editors can remove active source-bearing views. */
export function subscribeToStudioCodeEditorSessionRevocation(listener: () => void) {
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
