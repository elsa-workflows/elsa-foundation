import { useEffect, useRef, useState } from "react";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getWorkflowTestRun } from "../api/publishing";
import type { WorkflowDraft } from "../workflowTypes";
import { getDraftSignature, isRejectedTestRun } from "./editorHelpers";
import type { WorkflowTestRunState } from "./editorTypes";

interface Receipt {
  testRunId: string;
  definitionId: string;
  definitionVersionId: string;
  artifactId: string;
  workflowExecutionId: string | null;
  snapshotDigest: string;
  expiresAt: string | null;
}

function receiptKey(context: StudioEndpointContext, definitionId: string) {
  return `elsa.editor.test-run:${JSON.stringify([context.baseUrl, definitionId])}`;
}

function readReceipt(key: string): Receipt | null {
  try {
    const value = JSON.parse(sessionStorage.getItem(key) ?? "null") as Partial<Receipt> | null;
    if (!value || ![value.testRunId, value.definitionId, value.definitionVersionId, value.artifactId].every(item => typeof item === "string" && item.length > 0 && item.length < 256)
      || typeof value.snapshotDigest !== "string" || !/^[a-f0-9]{64}$/.test(value.snapshotDigest)
      || !(value.workflowExecutionId === null || typeof value.workflowExecutionId === "string" && value.workflowExecutionId.length < 256)
      || !(value.expiresAt === null || typeof value.expiresAt === "string" && Number.isFinite(Date.parse(value.expiresAt)))) return null;
    return value as Receipt;
  } catch { return null; }
}

function removeReceipt(key: string) {
  try { sessionStorage.removeItem(key); } catch { /* Storage is optional; execution remains in memory. */ }
}

async function snapshotDigest(signature: string) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(signature));
  return Array.from(new Uint8Array(digest), value => value.toString(16).padStart(2, "0")).join("");
}

/** Tab-scoped references only. Reattachment requires a fresh authorized read and the exact loaded draft. */
export function useTestRunRestoration({ context, definitionId, draft, testRun, onRestore }: {
  context: StudioEndpointContext;
  definitionId: string;
  draft: WorkflowDraft | null;
  testRun: WorkflowTestRunState | null;
  onRestore(value: WorkflowTestRunState): void;
}) {
  const key = receiptKey(context, definitionId);
  const [candidate, setCandidate] = useState(() => ({ key, receipt: readReceipt(key) }));
  const [state, setState] = useState({ key, pending: false, message: "" });
  const generation = useRef(0);
  const previous = useRef({ context, key });
  const current = useRef({ context, draft, testRun }); current.current = { context, draft, testRun };
  const request = useRef(false);
  const notify = useRef(onRestore); notify.current = onRestore;

  useEffect(() => {
    // A new authenticated endpoint object in this mounted editor revokes the old receipt, even at the same URL.
    if (previous.current.context !== context) {
      removeReceipt(previous.current.key); removeReceipt(key);
    }
    previous.current = { context, key };
    generation.current++; request.current = false;
    setCandidate({ key, receipt: readReceipt(key) }); setState({ key, pending: false, message: "" });
    const revoke = () => {
      generation.current++; request.current = false;
      removeReceipt(key); setCandidate({ key, receipt: null }); setState({ key, pending: false, message: "" });
    };
    window.addEventListener(authSessionEndedEvent, revoke);
    window.addEventListener(authSessionStartedEvent, revoke);
    return () => {
      generation.current++;
      window.removeEventListener(authSessionEndedEvent, revoke);
      window.removeEventListener(authSessionStartedEvent, revoke);
    };
  }, [context, key]);

  const view = testRun?.view;
  const signature = testRun?.snapshot ? testRun.draftSignature : null;
  const authority = testRun?.endpointContext;
  useEffect(() => {
    if (!view || !signature || authority !== context || view.definitionId !== definitionId || !view.artifactId || isRejectedTestRun(view)) return;
    let disposed = false;
    const expectedGeneration = generation.current;
    void snapshotDigest(signature).then(digest => {
      if (disposed || expectedGeneration !== generation.current) return;
      const receipt: Receipt = { testRunId: view.testRunId, definitionId, definitionVersionId: view.definitionVersionId,
        artifactId: view.artifactId!, workflowExecutionId: view.workflowExecutionId ?? null, snapshotDigest: digest, expiresAt: view.expiresAt ?? null };
      try { sessionStorage.setItem(key, JSON.stringify(receipt)); } catch { /* Optional recovery only. */ }
      setCandidate({ key, receipt });
    }).catch(() => { /* Secure hashing/storage may be unavailable; never persist authored or captured values. */ });
    return () => { disposed = true; };
    // Status polling must not rewrite a receipt after an authorization event revoked it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authority, context, definitionId, key, signature, view?.testRunId, view?.definitionVersionId, view?.artifactId, view?.workflowExecutionId, view?.expiresAt]);

  const receipt = !testRun && candidate.key === key ? candidate.receipt : null;
  const dismiss = () => {
    generation.current++; request.current = false; removeReceipt(key);
    setCandidate({ key, receipt: null }); setState({ key, pending: false, message: "" });
  };
  const restore = async () => {
    if (!receipt || !draft || draft.definitionId !== definitionId || request.current) return;
    const expectedGeneration = generation.current;
    const isCurrent = () => expectedGeneration === generation.current && current.current.context === context
      && current.current.draft === draft && !current.current.testRun;
    request.current = true;
    setState({ key, pending: true, message: "" });
    try {
      if (receipt.definitionId !== definitionId) throw new Error("The previous Test Run belongs to another definition.");
      if (receipt.expiresAt && Date.parse(receipt.expiresAt) <= Date.now()) throw new Error("This Test Run has expired. Start a new Test Run explicitly if needed.");
      const signature = getDraftSignature(draft);
      if (await snapshotDigest(signature) !== receipt.snapshotDigest) throw new Error("The saved design differs from this Test Run. Its captured editor snapshot is unavailable after reload. Open the run to inspect it.");
      if (!isCurrent()) return;
      const refreshed = await getWorkflowTestRun(context, receipt.testRunId);
      if (!isCurrent()) return;
      if (refreshed.testRunId !== receipt.testRunId || refreshed.definitionId !== definitionId || refreshed.definitionVersionId !== receipt.definitionVersionId
        || refreshed.artifactId !== receipt.artifactId || receipt.workflowExecutionId && refreshed.workflowExecutionId !== receipt.workflowExecutionId)
        throw new Error("The previous Test Run identity could not be verified.");
      if (refreshed.status === "Expired" || refreshed.expiresAt && Date.parse(refreshed.expiresAt) <= Date.now()) throw new Error("This Test Run has expired. Start a new Test Run explicitly if needed.");
      if (isRejectedTestRun(refreshed)) throw new Error("This Test Run cannot be attached.");
      notify.current({ view: refreshed, draftSignature: signature, snapshot: structuredClone(draft), endpointContext: context });
      setState({ key, pending: false, message: "" });
    } catch (error) {
      if (isCurrent()) setState({ key, pending: false, message: error instanceof Error ? error.message : "The previous Test Run is unavailable or expired." });
    } finally {
      if (expectedGeneration === generation.current) {
        request.current = false;
        setState(value => value.key === key && value.pending ? { ...value, pending: false } : value);
      }
    }
  };
  return { receipt, restore, dismiss, pending: state.key === key && state.pending, message: state.key === key ? state.message : "" };
}
