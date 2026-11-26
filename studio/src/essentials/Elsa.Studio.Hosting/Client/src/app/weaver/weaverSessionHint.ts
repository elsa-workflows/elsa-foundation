import type { ElsaStudioModuleApi } from "../../sdk";
import { notificationScopeKey } from "../notificationStorage";

export const weaverSessionHintKeyPrefix = "elsa-studio:weaver:v1:";

/**
 * Preview (#3441): where a Weaver dock may remember the last session it showed. The scope is the existing
 * user-and-backend notification scope, computed over the backend profile rather than the per-connection key,
 * so it survives a reload. The workspace sent to the server is the backend profile; it only narrows the
 * owner's session list after the server scoped it to the authenticated actor and tenant.
 */
export function weaverSessionScope(api: Pick<ElsaStudioModuleApi, "backendSession">): { storageKey: string; workspace: string } | null {
  const session = api.backendSession;
  if (!session) return null;
  const scope = notificationScopeKey({ key: JSON.stringify([session.profile.id, session.profile.baseUrl]), identity: session.identity });
  return scope ? { storageKey: `${weaverSessionHintKeyPrefix}${scope}:last-session`, workspace: `studio:${session.profile.id}` } : null;
}

function storage(): Storage | null {
  try { return window.localStorage; } catch { return null; }
}

/** The remembered session id. It is only a hint: the server's owner-scoped list and details stay authoritative. */
export function readSessionHint(key: string): string | null {
  try {
    const value = storage()?.getItem(key);
    return value && value.length <= 256 ? value : null;
  } catch { return null; }
}

export function writeSessionHint(key: string, sessionId: string) {
  try { storage()?.setItem(key, sessionId); } catch { /* the hint is best effort */ }
}

export function clearSessionHint(key: string, sessionId?: string) {
  try {
    const store = storage();
    if (!store || (sessionId !== undefined && store.getItem(key) !== sessionId)) return;
    store.removeItem(key);
  } catch { /* the hint is best effort */ }
}
