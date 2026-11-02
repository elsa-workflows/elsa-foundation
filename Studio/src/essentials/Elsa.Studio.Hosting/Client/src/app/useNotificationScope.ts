import { useEffect } from "react";
import type { StudioNotificationController } from "../sdk";
import { notificationScopeKey, type LocalNotificationStorage } from "./notificationStorage";

/**
 * Scopes notifications to the active identity and backend: switching either flushes the current
 * list and loads the stored list for the new scope, and no session (sign-out) clears it. While a
 * scope is active, lists written by other Studio tabs for the same scope are merged in.
 */
export function useNotificationScope(
  backendSession: Parameters<typeof notificationScopeKey>[0],
  controller: Pick<StudioNotificationController, "setScope" | "replaceEntries">,
  storage: Pick<LocalNotificationStorage, "subscribe">
) {
  const scopeKey = notificationScopeKey(backendSession);
  useEffect(() => {
    controller.setScope(scopeKey);
    if (scopeKey === null) return;
    return storage.subscribe(scopeKey, entries => controller.replaceEntries(entries));
  }, [controller, scopeKey, storage]);
}
