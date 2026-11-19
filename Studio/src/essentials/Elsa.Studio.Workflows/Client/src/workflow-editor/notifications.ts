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
