import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { notifyStudioNavigationGuardReleased, studioNavigationRequestedEvent, useStudioWorkspaceTab, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { JobsClient, JobsFault, statusOf, type Relation } from "./jobsApi";
import { permissions, type Access } from "./types";

export function can(identity: Access | undefined, permission: string) { return Boolean(identity?.permissions.includes(permission)); }
export function useJobsActorBoundary(api: ElsaStudioModuleApi, identity: Access | undefined, reset: () => void) {
  const previous = useRef<string | undefined>(undefined); const clear = useRef(reset); clear.current = reset;
  const current = identity ? JSON.stringify([api.backend.baseUrl, identity.tenantId, identity.subjectId]) : undefined;
  const changed = Boolean(previous.current && current && previous.current !== current);
  useEffect(() => {
    if (!current) return;
    if (previous.current && previous.current !== current) clear.current();
    previous.current = current;
  }, [current]);
  return changed;
}
export function useJobsRead<T>(api: ElsaStudioModuleApi, key: string, load: (client: JobsClient) => Promise<T>, permission: string = permissions.read) {
  const client = useMemo(() => new JobsClient(api), [api]);
  const loader = useRef(load); loader.current = load;
  const sequence = useRef(0);
  const [state, setState] = useState<{ client: JobsClient; key: string; loading: boolean; value?: T; identity?: Access; error?: unknown }>({ client, key, loading: true });
  const refresh = useCallback(async () => {
    const current = ++sequence.current; setState({ client, key, loading: true });
    try {
      const identity = await client.access();
      if (sequence.current !== current) return;
      if (!can(identity, permission)) { setState({ client, key, loading: false, identity, error: new JobsFault(403) }); return; }
      const value = await loader.current(client);
      if (sequence.current === current) setState({ client, key, loading: false, value, identity });
    } catch (error) { if (sequence.current === current) setState({ client, key, loading: false, error }); }
  }, [client, key, permission]);
  useEffect(() => { void refresh(); return () => { ++sequence.current; }; }, [refresh]);
  return { ...(state.key === key && state.client === client ? state : { loading: true, value: undefined, identity: undefined, error: undefined }), refresh };
}

export function useJobsNavigationGuard(blocked: boolean) {
  const tab = useStudioWorkspaceTab(); const currentTab = useRef(tab); currentTab.current = tab;
  useEffect(() => {
    if (!blocked) return;
    currentTab.current?.markPendingWork();
    const prevent = (event: Event) => event.preventDefault();
    window.addEventListener("beforeunload", prevent); window.addEventListener(studioNavigationRequestedEvent, prevent);
    return () => {
      window.removeEventListener("beforeunload", prevent); window.removeEventListener(studioNavigationRequestedEvent, prevent);
      notifyStudioNavigationGuardReleased();
    };
  }, [blocked]);
}

export interface JobsCommand {
  method: "POST" | "PUT"; relation: Relation; segments: string[]; body: unknown; permission: string; title: string;
}
interface Pending extends JobsCommand { actor: string }
export function useJobsMutation(api: ElsaStudioModuleApi, confirmed: (result: unknown) => void, recheck?: () => void) {
  const client = useMemo(() => new JobsClient(api), [api]);
  const completed = useRef(confirmed); completed.current = confirmed;
  const revalidate = useRef(recheck); revalidate.current = recheck;
  const pending = useRef<Pending | undefined>(undefined); const busyRef = useRef(false);
  const [busy, setBusy] = useState(false); const [problem, setProblem] = useState<unknown>();
  const [unconfirmed, setUnconfirmed] = useState(false);
  useJobsNavigationGuard(busy || unconfirmed);
  const execute = useCallback(async (command?: JobsCommand) => {
    if (busyRef.current || command && pending.current) return;
    const retrying = pending.current !== undefined;
    const attempt = pending.current ?? (command ? { ...command, segments: [...command.segments], body: structuredClone(command.body), actor: "" } : undefined);
    if (!attempt) return;
    busyRef.current = true; setBusy(true); setProblem(undefined);
    try {
      const identity = await client.access();
      const actor = JSON.stringify([api.backend.baseUrl, identity.tenantId, identity.subjectId]);
      if (!can(identity, attempt.permission) || attempt.actor && attempt.actor !== actor) throw new JobsFault(403);
      attempt.actor = actor; pending.current = attempt;
      const result = await client.write<unknown>(attempt.method, attempt.relation, attempt.segments, attempt.body);
      pending.current = undefined; setUnconfirmed(false);
      const linkedId = typeof result === "object" && result !== null && "jobId" in result && typeof result.jobId === "string" ? result.jobId : null;
      api.notifications?.notify({ level: "success", title: attempt.title,
        link: linkedId ? { label: "Open job", path: `/jobs/${encodeURIComponent(linkedId)}` } : undefined });
      completed.current(result);
    } catch (error) {
      const status = statusOf(error);
      // A refusal on a later replay does not disprove the original ambiguous commit.
      const ambiguous = retrying || status === 0 || status >= 500;
      if (ambiguous && pending.current) setUnconfirmed(true);
      else { pending.current = undefined; setUnconfirmed(false); }
      setProblem(error);
      if ([401, 403, 404].includes(status)) revalidate.current?.();
      if (![400, 409].includes(status)) api.notifications?.notify({ level: "error", title: "Jobs operation needs review", message: ambiguous ? "The outcome is unconfirmed. Retain and retry the original operation." : "Refresh current access and state before continuing." });
    } finally { busyRef.current = false; setBusy(false); }
  }, [api, client]);
  const forget = async () => {
    if (busyRef.current || !pending.current) return;
    if (!await api.dialogs.confirm({ title: "Forget local confirmation?", message: "Check the current server state and copy this operation ID first. Forgetting it does not cancel accepted work. A later new operation has a new identity.", confirmLabel: "Forget local confirmation", cancelLabel: "Keep original operation", tone: "danger" })) return;
    pending.current = undefined; setUnconfirmed(false); setProblem(undefined);
  };
  return { busy, problem, unconfirmed, blocked: busy || unconfirmed, pending: pending.current, run: execute, retry: () => execute(), forget };
}
