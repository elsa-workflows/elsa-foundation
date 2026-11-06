import { useCallback, useEffect, useRef, useState } from "react";
import { authSessionEndedEvent, studioBackendCapabilitiesChangedEvent, StudioHttpError, useStudioWorkspaceWhenActive, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { SkeletonRows, StudioAlert, StudioButton, StudioResultState } from "@elsa-workflows/studio-ui";
import { TriggerCapabilityError } from "./api";
import type { ActionAvailability, Finding } from "./types";

export const triggersPath = "/workflows/triggers";
export const triggerPath = (id: string) => `${triggersPath}/${encodeURIComponent(id)}`;
export const deliveryPath = (id: string) => `${triggersPath}/deliveries/${encodeURIComponent(id)}`;
export function label(value: string) { return value.replace(/([a-z])([A-Z])/g, "$1 $2"); }
export function allowed(actions: readonly ActionAvailability[], action: string) { return actions.find(item => item.action === action)?.allowed === true; }
export function actionReason(actions: readonly ActionAvailability[], action: string) { return actions.find(item => item.action === action)?.message; }
export function status(error: unknown) { return error instanceof StudioHttpError ? error.status : typeof error === "object" && error !== null && "status" in error ? Number(error.status) : undefined; }
export function errorMessage(error: unknown) { return error instanceof Error ? error.message : "The request could not be completed. Try again."; }
export function Findings({ items }: { items?: readonly Finding[] }) { return items?.length ? <ul className="wf-trigger-findings">{items.map((item, index) => <li key={`${item.code}:${item.inputName ?? item.field ?? index}`}>{item.inputName ? <strong>{item.inputName}: </strong> : null}{item.message}</li>)}</ul> : null; }
export function ScheduleFailure({ reason }: { reason?: string | null }) {
  return reason === "NoEligibleSubscribers" ? <p>Waiting for eligible subscribers. The due occurrence is retained and will be retried.</p>
    : reason === "AdmissionUnavailable" ? <p>Schedule admission is unavailable. The due occurrence is retained for retry.</p> : null;
}
export function RequestError({ error, retry, title = "Could not load triggers" }: { error: unknown; retry?(): void; title?: string }) {
  const denied = status(error) === 401 || status(error) === 403;
  return <StudioResultState tone={denied || error instanceof TriggerCapabilityError ? undefined : "danger"} title={denied ? "Trigger access unavailable" : error instanceof TriggerCapabilityError ? "Standalone triggers unavailable" : title}
    description={errorMessage(error)} actions={retry ? <StudioButton onClick={retry}>Retry</StudioButton> : undefined} />;
}
export function ActionError({ error, reload }: { error: unknown; reload?(): void }) { return <StudioAlert tone="danger">
  <strong>{status(error) === 409 ? "This resource changed" : "The change was not confirmed"}</strong><p>{errorMessage(error)}</p>{status(error) === 409 ? <p>Your edits are retained. Reload the current resource and review the change again.</p> : null}
  {reload ? <StudioButton onClick={reload}>Reload current resource</StudioButton> : null}
</StudioAlert>; }

/** Include authorization lifetime in every editor key so drafts/results cannot cross users or backends. */
export function useTriggerScope(api: ElsaStudioModuleApi) {
  const [epoch, setEpoch] = useState(0);
  useEffect(() => { const reset = () => setEpoch(value => value + 1); window.addEventListener(authSessionEndedEvent, reset); window.addEventListener(studioBackendCapabilitiesChangedEvent, reset); return () => { window.removeEventListener(authSessionEndedEvent, reset); window.removeEventListener(studioBackendCapabilitiesChangedEvent, reset); }; }, []);
  const session = api.backendSession;
  return JSON.stringify([session?.key, api.backend.baseUrl, session?.identity.subject ?? api.runtime.identity?.subject, session?.identity.tenantId ?? api.runtime.identity?.tenantId, session?.identity.status, epoch]);
}
export type LoadState<T> = { state: "loading" } | { state: "error"; error: unknown } | { state: "loaded"; value: T };
export function useTriggerLoad<T>(load: (signal: AbortSignal) => Promise<T>, key: string) {
  const [attempt, setAttempt] = useState(0);
  const [loaded, setLoaded] = useState<{ key: string; result: LoadState<T> }>();
  const loader = useRef(load); loader.current = load;
  useEffect(() => {
    const controller = new AbortController();
    setLoaded({ key, result: { state: "loading" } });
    void loader.current(controller.signal).then(value => { if (!controller.signal.aborted) setLoaded({ key, result: { state: "loaded", value } }); }, error => { if (!controller.signal.aborted) setLoaded({ key, result: { state: "error", error } }); });
    return () => controller.abort();
  }, [key, attempt]);
  return { result: loaded?.key === key ? loaded.result : { state: "loading" } as LoadState<T>, reload: () => setAttempt(value => value + 1) };
}
export function Loading({ label: text = "Loading triggers" }: { label?: string }) { return <SkeletonRows label={text} />; }
export function useTriggerCompletion() {
  const active = useRef(true);
  const whenActive = useStudioWorkspaceWhenActive();
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  return useCallback((action: () => void) => whenActive(() => { if (active.current) action(); }), [whenActive]);
}
