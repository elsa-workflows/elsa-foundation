import React from "react";
import { AlertTriangle, CheckCircle2, Info, Loader2, XCircle } from "lucide-react";
import type { StudioNotificationAction, StudioNotificationController, StudioNotificationLevel } from "../../../sdk";

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

/** Runs a notification action; a throw or rejection surfaces as an error notification, never unhandled. */
export function runNotificationAction(controller: StudioNotificationController, action: StudioNotificationAction) {
  const reportFailure = (error: unknown) => {
    controller.api.notify({
      level: "error",
      title: `${action.label} failed`,
      message: error instanceof Error ? error.message : String(error)
    });
  };
  try {
    void Promise.resolve(action.run()).catch(reportFailure);
  } catch (error) {
    reportFailure(error);
  }
}
