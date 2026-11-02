import { createNotificationController } from "../sdk";
import { createLocalNotificationStorage } from "./notificationStorage";

/**
 * Shared singleton notification storage and controller for the Studio host.
 *
 * `createStudioRegistry` exposes `notificationController.api` to every module as
 * `api.notifications`, and the host-rendered toast stack and Notification Center subscribe to
 * the same controller. Because `@elsa-workflows/studio-sdk` and `react` are externalized in
 * module bundles, modules and the host share this one instance at runtime. The host scopes the
 * controller to the active identity and backend (see `AppContent`).
 */
export const notificationStorage = createLocalNotificationStorage();

export const notificationController = createNotificationController({ storage: notificationStorage });
