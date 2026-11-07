import { ExternalAuthenticationClientError, type ConnectionTest, type ConnectionView } from "./sdk";

export interface PendingPreview {
  baseUrl: string; recordId: string; key: string; materialRevision: string; startedAt: number; test: ConnectionTest | null;
}
const prefix = "elsa.external-preview.v1:";
const queryKeys = ["externalPreviewNonce", "externalPreviewHandle", "externalPreviewConnection", "externalPreviewFailure"];
const failureMessages: Record<string, string> = {
  ProviderFailed: "The provider sign-in was cancelled or could not be verified. Your Elsa session is unchanged. Test the connection, then preview again.",
  StaleConnection: "The saved connection changed during preview. Refresh, test and preview its current configuration.",
  Unavailable: "The preview service is unavailable. Check the installed authentication services, then retry.",
  InvalidState: "The provider response could not be verified. Start another preview from this saved connection.",
  InvalidResult: "The preview result could not be prepared. Check the connection's provisioning policy, then retry.",
  Conflict: "The preview result could not be saved. Start another preview."
};

export function preparePreviewReturn(url: URL, baseUrl: string, view: ConnectionView, test: ConnectionTest | null, storage: Storage) {
  const nonce = Array.from(crypto.getRandomValues(new Uint8Array(32)), byte => byte.toString(16).padStart(2, "0")).join("");
  const pending: PendingPreview = { baseUrl, recordId: view.document.id, key: view.document.key,
    materialRevision: view.materialRevision, startedAt: Date.now(), test };
  try { storage.setItem(prefix + nonce, JSON.stringify(pending)); }
  catch { throw new ExternalAuthenticationClientError("This browser cannot retain the preview return. Allow site storage, then retry."); }
  for (const key of queryKeys) url.searchParams.delete(key);
  url.searchParams.set("externalPreviewNonce", nonce);
  return url.href;
}

/** Reads only a return initiated in this browser tab/backend; never stores returned identity/permission data. */
export function takePreviewReturn(url: URL, baseUrl: string, storage: Storage) {
  const handle = url.searchParams.get("externalPreviewHandle");
  const nonce = url.searchParams.get("externalPreviewNonce");
  const key = url.searchParams.get("externalPreviewConnection");
  const failure = url.searchParams.get("externalPreviewFailure");
  if (!handle && !nonce && !key && !failure) return null;
  if (!nonce || !key || queryKeys.some(name => url.searchParams.getAll(name).length > 1) || !/^[a-f0-9]{64}$/.test(nonce) ||
    (handle ? Boolean(failure) || !/^[a-zA-Z0-9_-]{43,128}$/.test(handle) : !failure || !Object.hasOwn(failureMessages, failure)))
    throw new ExternalAuthenticationClientError("This preview return is incomplete. Start another preview from the saved connection.");
  let pending: PendingPreview | null;
  try { pending = JSON.parse(storage.getItem(prefix + nonce) ?? "null") as PendingPreview | null; storage.removeItem(prefix + nonce); }
  catch { throw new ExternalAuthenticationClientError("The preview return could not be restored. Start another preview."); }
  if (!pending || pending.baseUrl !== baseUrl || pending.key !== key || typeof pending.recordId !== "string" || typeof pending.materialRevision !== "string" ||
    !Number.isFinite(pending.startedAt) || pending.startedAt > Date.now() || Date.now() - pending.startedAt > 30 * 60_000)
    throw new ExternalAuthenticationClientError("This preview belongs to another backend, browser tab, or an expired request. Start another preview here.");
  return handle ? { handle, key, pending, error: null } : { handle: null, key, pending, error: failureMessages[failure!] };
}

export function cleanPreviewUrl(url: URL) { for (const key of queryKeys) url.searchParams.delete(key); return url; }
