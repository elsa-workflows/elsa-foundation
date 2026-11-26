import { useLayoutEffect, useRef } from "react";
import type { StudioNotificationApi, StudioNotificationHandle } from "@elsa-workflows/studio-sdk";

// Host-provided notification service, captured at registration so imperative handlers can publish
// results without prop-drilling. Hosts built against an older SDK provide none, so fall back to a no-op.
const noopHandle: StudioNotificationHandle = { id: "", update() {}, dismiss() {} };
const noopNotifications: StudioNotificationApi = {
  notify: () => noopHandle,
  track: work => work
};

let notifications: StudioNotificationApi = noopNotifications;

export function setNotifications(api: StudioNotificationApi | undefined) {
  notifications = api ?? noopNotifications;
}

export function getNotifications(): StudioNotificationApi {
  return notifications;
}

export function notifySuccess(title: string): void {
  notifications.notify({ level: "success", title });
}

/** Capture before awaiting an action so a late outcome cannot toast into another page/backend. */
export function useNotificationGuard(context: object) {
  const current = useRef({ context, active: false, generation: 0 });
  if (current.current.context !== context) current.current = { context, active: false, generation: 0 };
  const scope = current.current;
  useLayoutEffect(() => {
    scope.active = true;
    return () => { scope.active = false; scope.generation++; };
  }, [scope]);
  return () => {
    const captured = scope.generation;
    return Object.assign(
      () => scope.active && current.current === scope && scope.generation === captured,
      { isOwned: () => current.current === scope }
    );
  };
}
