import React, { useState, useSyncExternalStore } from "react";
import { AlertTriangle, CheckCircle2, Info, Loader2, X, XCircle } from "lucide-react";
import { notificationToastDurationMs, type StudioNotificationController, type StudioNotificationEntry, type StudioNotificationLevel } from "../../../sdk";
import { notificationController } from "../../notifications";
import { useNoticeTimer } from "./useNoticeTimer";

const maxVisibleToasts = 3;

export interface ToastHostProps {
  navigate(path: string): void;
  /** Defaults to the host singleton; tests and the browser fixture pass their own. */
  controller?: StudioNotificationController;
}

/**
 * Host-owned toast stack: the newest toasting entries, newest on top, plus the visually hidden
 * live regions that announce them. Entries stay in the Notification Center after their toast closes.
 */
export function ToastHost({ navigate, controller = notificationController }: ToastHostProps) {
  const entries = useSyncExternalStore(controller.subscribe, controller.getEntries, controller.getEntries);
  const visible = entries.filter(entry => entry.toasting).slice(0, maxVisibleToasts);
  const [announcements, setAnnouncements] = useState({ polite: "", assertive: "", signatures: "" });

  // Announce the newest toast that is new or changed in place (level or updatedAt), not every render.
  // Derived while rendering so the live region updates in the same commit as the toast.
  const signatures = visible.map(entry => `${entry.id}:${entry.level}:${entry.updatedAt}`);
  const signatureKey = signatures.join("|");
  if (signatureKey !== announcements.signatures) {
    const previous = new Set(announcements.signatures.split("|"));
    const changed = visible.find((entry, index) => !previous.has(signatures[index]));
    const next = { ...announcements, signatures: signatureKey };
    if (changed) {
      next[changed.level === "error" ? "assertive" : "polite"] = changed.message ? `${changed.title}. ${changed.message}` : changed.title;
    }
    setAnnouncements(next);
  }

  return (
    <>
      <div className="studio-toast-host">
        {visible.map(entry => (
          <Toast key={entry.id} entry={entry} controller={controller} navigate={navigate} />
        ))}
      </div>
      <div className="studio-toast-live" role="status" aria-live="polite" aria-atomic="true">{announcements.polite}</div>
      <div className="studio-toast-live" role="alert" aria-live="assertive" aria-atomic="true">{announcements.assertive}</div>
    </>
  );
}

function Toast({ entry, controller, navigate }: { entry: StudioNotificationEntry; controller: StudioNotificationController; navigate(path: string): void }) {
  const { id, level, updatedAt, link, action } = entry;
  const timerHandlers = useNoticeTimer({
    durationMs: notificationToastDurationMs(level),
    onElapse: () => controller.hideToast(id),
    resetKey: `${level}:${updatedAt}`
  });

  const openLink = () => {
    if (!link) return;
    navigate(link.path);
    controller.markRead(id);
    controller.hideToast(id);
  };

  const runAction = () => {
    if (!action) return;
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
    controller.hideToast(id);
  };

  return (
    <div className="studio-toast" data-level={level} {...timerHandlers}>
      <span className="studio-toast-icon" aria-hidden="true"><ToastIcon level={level} /></span>
      <div className="studio-toast-body">
        <div className="studio-toast-title">{entry.title}</div>
        {entry.message ? <div className="studio-toast-message">{entry.message}</div> : null}
        {link || action ? (
          <div className="studio-toast-actions">
            {link ? <button type="button" className="studio-toast-button" onClick={openLink}>{link.label}</button> : null}
            {action ? <button type="button" className="studio-toast-button" onClick={runAction}>{action.label}</button> : null}
          </div>
        ) : null}
      </div>
      <button
        type="button"
        className="studio-toast-dismiss"
        aria-label="Dismiss notification"
        title="Dismiss"
        onClick={() => controller.hideToast(id)}
      >
        <X size={16} aria-hidden="true" />
      </button>
    </div>
  );
}

function ToastIcon({ level }: { level: StudioNotificationLevel }) {
  switch (level) {
    case "success": return <CheckCircle2 size={18} />;
    case "warning": return <AlertTriangle size={18} />;
    case "error": return <XCircle size={18} />;
    case "progress": return <Loader2 size={18} className="studio-toast-spinner" />;
    default: return <Info size={18} />;
  }
}
