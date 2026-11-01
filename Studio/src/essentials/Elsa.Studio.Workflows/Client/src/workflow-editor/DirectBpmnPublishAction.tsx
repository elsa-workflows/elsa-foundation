import { useEffect, useMemo, useRef, useState } from "react";
import type { StudioBackendSession } from "@elsa-workflows/studio-sdk";
import { createBpmnRuntimeClient } from "../api/bpmnRuntime";
import { getDraft, promoteDraft } from "../api/workflowDesign";
import type { WorkflowDraft } from "../workflowTypes";
import { getDraftSignature } from "./editorHelpers";

interface PublicationReceipt {
  formatVersion: 1;
  scope: string;
  definitionId: string;
  draftId: string;
  draftHash: string;
  promotionKey: string;
  publicationKey: string;
  versionId?: string;
}

async function draftHash(draft: WorkflowDraft) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(getDraftSignature(draft)));
  return [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, "0")).join("");
}

function readReceipt(key: string, scope: string, definitionId: string): PublicationReceipt | null {
  const raw = sessionStorage.getItem(key);
  if (!raw) return null;
  const value = JSON.parse(raw) as PublicationReceipt;
  if (value.formatVersion !== 1 || value.scope !== scope || value.definitionId !== definitionId
    || ![value.draftId, value.promotionKey, value.publicationKey].every(item => typeof item === "string" && item.length > 0)
    || typeof value.draftHash !== "string" || !/^[a-f\d]{64}$/.test(value.draftHash)
    || (value.versionId !== undefined && (typeof value.versionId !== "string" || !value.versionId))) {
    throw new Error("The retained BPMN publication cannot be read safely. Keep this tab open for diagnosis.");
  }
  return value;
}

/** One ordinary save → promotion → direct publication. Retain identities before either durable command. */
export function DirectBpmnPublishAction({ session, definitionId, draft, busy, readOnly, saveDraft, flushMetadata,
  setPaused, setBusy, setStatus, setError, reload }: {
  session: StudioBackendSession;
  definitionId: string;
  draft: WorkflowDraft;
  busy: boolean;
  readOnly: boolean;
  saveDraft(draft: WorkflowDraft, status: string): Promise<WorkflowDraft>;
  flushMetadata(): Promise<boolean>;
  setPaused(value: boolean): void;
  setBusy(value: boolean): void;
  setStatus(value: string): void;
  setError(value: string): void;
  reload(options: { shouldLoadDraft(): boolean }): Promise<void>;
}) {
  const client = useMemo(() => createBpmnRuntimeClient(session), [session]);
  const storageKey = `elsa:bpmn-publication:1:${client.scope}:${definitionId}`;
  const [storageError, setStorageError] = useState("");
  const [receipt, setReceipt] = useState<PublicationReceipt | null>(() => {
    try { return readReceipt(storageKey, client.scope, definitionId); }
    catch { return null; }
  });
  const inFlight = useRef(false);
  const live = useRef({ draft, readOnly }); live.current = { draft, readOnly };
  const lifetime = useRef<AbortController | null>(null);
  useEffect(() => {
    const controller = new AbortController(); lifetime.current = controller;
    try { readReceipt(storageKey, client.scope, definitionId); }
    catch (error) { setStorageError(error instanceof Error ? error.message : String(error)); }
    return () => { controller.abort(); lifetime.current = null; };
  }, [client.scope, definitionId, storageKey]);
  useEffect(() => { setPaused(!!receipt || !!storageError); return () => setPaused(false); }, [receipt, setPaused, storageError]);

  const publish = async () => {
    if (busy || readOnly || inFlight.current || storageError || session.disposed) return;
    const signal = lifetime.current?.signal;
    if (!signal || signal.aborted) return;
    const assertActive = () => {
      if (signal.aborted || session.disposed || live.current.readOnly) throw new DOMException("The editor is no longer editable in this connection.", "AbortError");
      // prepare also verifies the captured backend/account/tenant, without issuing a request.
      client.prepare({ kind: "publish", versionId: "validate-session" }, "validate-session");
    };
    let pending = receipt;
    const snapshot = live.current.draft;
    let reloadSnapshot = false;
    inFlight.current = true; setBusy(true); setPaused(true); setError("");
    try {
      assertActive();
      if (!pending) {
        if (!await flushMetadata()) return;
        assertActive(); setStatus("Saving BPMN publication snapshot…");
        const saved = await saveDraft(snapshot, "BPMN publication snapshot saved");
        assertActive();
        const hash = await draftHash(saved); assertActive();
        const prepared: PublicationReceipt = { formatVersion: 1, scope: client.scope, definitionId, draftId: saved.id, draftHash: hash,
          promotionKey: crypto.randomUUID(), publicationKey: crypto.randomUUID() };
        // Failure to retain this receipt refuses the promotion; a reload must never create a replacement key.
        sessionStorage.setItem(storageKey, JSON.stringify(prepared)); pending = prepared; setReceipt(pending);
      } else if (!pending.versionId) {
        let saved: WorkflowDraft | null = null;
        try { saved = await getDraft(session.context, pending.draftId); }
        catch (error) { if (!(error && typeof error === "object" && "status" in error && error.status === 404)) throw error; }
        assertActive();
        // A missing draft may be the result of a succeeded promotion. The same promotion key
        // resolves its receipt before lookup; a first promotion of a missing draft still refuses.
        if (saved && await draftHash(saved) !== pending.draftHash) throw new Error("The saved draft changed since this publication started. The retained operation is unchanged; no new promotion was sent.");
        assertActive();
      }
      // A resumed operation can publish an older saved version while the author has newer local
      // edits. Those edits must survive even when they predate this click, rather than arriving mid-request.
      reloadSnapshot = await draftHash(snapshot) === pending.draftHash; assertActive();
      if (!pending.versionId) {
        setStatus("Promoting BPMN publication version…");
        const promoted = await promoteDraft(session.context, pending.draftId, undefined, pending.promotionKey, signal);
        assertActive(); pending = { ...pending, versionId: promoted.id };
        sessionStorage.setItem(storageKey, JSON.stringify(pending)); setReceipt(pending);
      }
      assertActive(); setStatus("Publishing BPMN process…");
      await client.execute(client.prepare({ kind: "publish", versionId: pending.versionId! }, pending.publicationKey), signal);
      assertActive(); sessionStorage.removeItem(storageKey); setReceipt(null); pending = null;
      setStatus("BPMN process published.");
      try { await reload({ shouldLoadDraft: () => reloadSnapshot && live.current.draft.id === snapshot.id && getDraftSignature(live.current.draft) === getDraftSignature(snapshot) }); }
      catch { assertActive(); setError("BPMN publication succeeded, but the editor could not refresh. Reload to discover the published process."); }
    } catch (error) {
      if (signal.aborted || session.disposed) return;
      // A synchronous validation refusal created no publication. Let the author correct the
      // draft explicitly; uncertain transport/auth/conflict outcomes keep the same receipt.
      if (error && typeof error === "object" && "status" in error && error.status === 422) {
        try { sessionStorage.removeItem(storageKey); pending = null; setReceipt(null); }
        catch { /* Preserve the original receipt if browser storage becomes unavailable. */ }
      }
      setStatus("");
      setError(`${error instanceof Error ? error.message : String(error)}${pending ? " The publication identity is retained. Resume this same publication after reconnecting." : ""}`);
    } finally {
      inFlight.current = false;
      if (!signal.aborted) { setBusy(false); setPaused(!!pending); }
    }
  };
  return <span>
    <button type="button" disabled={busy || readOnly || !!storageError || session.disposed} onClick={() => void publish()}
      title={receipt ? "Continue the retained BPMN publication using the same operation identities" : "Save a version and publish this process to the BPMN runtime"}>
      {receipt ? "Resume BPMN publication" : "Publish BPMN"}
    </button>
    {storageError ? <span role="alert">{storageError}</span> : null}
  </span>;
}
