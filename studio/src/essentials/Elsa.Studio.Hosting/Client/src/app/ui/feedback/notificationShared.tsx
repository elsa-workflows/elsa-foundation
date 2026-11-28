import React from "react";
import { AlertTriangle, CheckCircle2, Info, Loader2, XCircle } from "lucide-react";
import type { StudioNotificationAction, StudioNotificationController, StudioNotificationLevel } from "../../../sdk";

/** Level names for assistive technology, which cannot see the icon colour. */
export const notificationLevelLabel: Record<StudioNotificationLevel, string> = {
  info: "Info",
  success: "Success",
  warning: "Warning",
  error: "Error",
  progress: "In progress"
};

/** Level icon shared by toasts and the Notification Center. */
export function NotificationLevelIcon({ level }: { level: StudioNotificationLevel }) {
  switch (level) {
    case "success": return <CheckCircle2 size={18} />;
    case "warning": return <AlertTriangle size={18} />;
    case "error": return <XCircle size={18} />;
    case "progress": return <Loader2 size={18} className="studio-toast-spinner" />;
    default: return <Info size={18} />;
  }
}

/**
 * Runs a notification action once: the action is removed from the entry first, so a second click (in
 * the toast or the Center) cannot repeat it. A throw or rejection surfaces as an error notification,
 * never as an unhandled rejection.
 */
export function runNotificationAction(controller: StudioNotificationController, entryId: string, action: StudioNotificationAction) {
  const reportFailure = (error: unknown) => {
    controller.api.notify({
      level: "error",
      title: `${action.label} failed`,
      message: error instanceof Error ? error.message : String(error)
    });
  };
  controller.clearAction(entryId);
  try {
    void Promise.resolve(action.run()).catch(reportFailure);
  } catch (error) {
    reportFailure(error);
  }
}

/** Moves focus to an element if it is still in the document; returns whether it did. */
export function focusIfConnected(element: HTMLElement | null | undefined): boolean {
  if (!element?.isConnected) return false;
  element.focus();
  return true;
}
