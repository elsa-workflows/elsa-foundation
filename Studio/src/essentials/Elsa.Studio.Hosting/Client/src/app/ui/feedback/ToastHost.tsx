import React, { useId, useLayoutEffect, useRef, useState, useSyncExternalStore } from "react";
import { X } from "lucide-react";
import { notificationToastDurationMs, type StudioNotificationController, type StudioNotificationEntry } from "../../../sdk";
import { notificationController } from "../../notifications";
import { focusIfConnected, NotificationLevelIcon, notificationLevelLabel, runNotificationAction } from "./notificationShared";
import { useNoticeTimer } from "./useNoticeTimer";

const maxVisibleToasts = 3;
const zeroWidthSpace = "\u200B";

export interface ToastHostProps {
  navigate(path: string): void;
  /** Defaults to the host singleton; tests and the browser fixture pass their own. */
  controller?: StudioNotificationController;
}

interface Announcements {
  polite: string;
  assertive: string;
  /** The entries the announcements were derived from. */
  seen: readonly StudioNotificationEntry[];
  suppressed: boolean;
}

function announcementText(entry: StudioNotificationEntry) {
  const text = entry.message ? `${entry.title}. ${entry.message}` : entry.title;
  return entry.level === "error" || entry.level === "warning" ? `${notificationLevelLabel[entry.level]}: ${text}` : text;
}

/** The newest entry worth announcing, comparing the current entries with the ones last seen. */
function findAnnouncement(entries: readonly StudioNotificationEntry[], visible: readonly StudioNotificationEntry[], seen: readonly StudioNotificationEntry[], suppressed: boolean) {
  const previous = new Map(seen.map(entry => [entry.id, entry]));
  if (suppressed) {
    // No toast is shown while the Notification Center is open, but new outcomes are still announced.
    return entries.find(entry => previous.get(entry.id)?.level !== entry.level);
  }
  return visible.find(entry => {
    const before = previous.get(entry.id);
    return !before || before.level !== entry.level || before.updatedAt !== entry.updatedAt;
  });
}

/**
 * Host-owned toast stack: the newest toasting entries, newest on top, plus the visually hidden
 * live regions that announce them. Entries stay in the Notification Center after their toast closes.
 */
export function ToastHost({ navigate, controller = notificationController }: ToastHostProps) {
  const entries = useSyncExternalStore(controller.subscribe, controller.getEntries, controller.getEntries);
  const suppressed = useSyncExternalStore(controller.subscribe, controller.getToastsSuppressed, controller.getToastsSuppressed);
  const visible = entries.filter(entry => entry.toasting).slice(0, maxVisibleToasts);
  const [announcements, setAnnouncements] = useState<Announcements>(() => ({ polite: "", assertive: "", seen: entries, suppressed }));
  const hostRef = useRef<HTMLDivElement>(null);
  const focusedRemovalIndex = useRef<number | null>(null);

  // Announce the newest entry that is new or changed in place, not every render. Derived while
  // rendering so the live region updates in the same commit as the toast. Opening or closing the
  // Notification Center only moves the baseline, so it announces nothing by itself.
  if (entries !== announcements.seen || suppressed !== announcements.suppressed) {
    const next: Announcements = { ...announcements, seen: entries, suppressed };
    const changed = suppressed === announcements.suppressed ? findAnnouncement(entries, visible, announcements.seen, suppressed) : undefined;
    if (changed) {
      const region = changed.level === "error" ? "assertive" : "polite";
      const text = announcementText(changed);
      // Screen readers ignore a live region whose text did not change; alternate a zero-width space so a repeat is announced.
      next[region] = text === announcements[region] ? `${text}${zeroWidthSpace}` : text;
    }
    setAnnouncements(next);
  }

  // A toast that closed while focused hands focus to the toast now in its place, else to the bell.
  useLayoutEffect(() => {
    const index = focusedRemovalIndex.current;
    if (index === null) return;
    focusedRemovalIndex.current = null;
    const remaining = hostRef.current?.querySelectorAll<HTMLElement>(".studio-toast") ?? [];
    if (focusIfConnected(remaining[Math.min(index, remaining.length - 1)])) return;
    focusIfConnected(document.querySelector<HTMLElement>(".studio-notification-bell"));
  });

  return (
    <>
      {visible.length > 0 ? (
        <div ref={hostRef} className="studio-toast-host" role="region" aria-label="Notifications">
          {visible.map((entry, index) => (
            <Toast
              key={entry.id}
              entry={entry}
              index={index}
              controller={controller}
              navigate={navigate}
              onRemovedWhileFocused={removedIndex => { focusedRemovalIndex.current = removedIndex; }}
            />
          ))}
        </div>
      ) : null}
      <div className="studio-toast-live" role="status" aria-live="polite" aria-atomic="true">{announcements.polite}</div>
      <div className="studio-toast-live" role="alert" aria-live="assertive" aria-atomic="true">{announcements.assertive}</div>
    </>
  );
}

function Toast({ entry, index, controller, navigate, onRemovedWhileFocused }: {
  entry: StudioNotificationEntry;
  index: number;
  controller: StudioNotificationController;
  navigate(path: string): void;
  onRemovedWhileFocused(index: number): void;
}) {
  const { id, level, updatedAt, link, action } = entry;
  const titleId = useId();
  const toastRef = useRef<HTMLDivElement>(null);
  const latest = useRef({ index, onRemovedWhileFocused });
  latest.current = { index, onRemovedWhileFocused };
  const timerHandlers = useNoticeTimer({
    durationMs: notificationToastDurationMs(level),
    onElapse: () => controller.hideToast(id),
    resetKey: `${level}:${updatedAt}`
  });

  // Runs while the toast is still in the document, so it can tell whether it held focus.
  useLayoutEffect(() => {
    const node = toastRef.current;
    return () => {
      if (node?.contains(document.activeElement)) latest.current.onRemovedWhileFocused(latest.current.index);
    };
  }, []);

  const openLink = () => {
    if (!link) return;
    try {
      controller.markRead(id);
      controller.hideToast(id);
    } finally {
      navigate(link.path);
    }
  };

  const runAction = () => {
    if (!action) return;
    runNotificationAction(controller, id, action);
    controller.hideToast(id);
  };

  return (
    <div ref={toastRef} className="studio-toast" data-level={level} role="group" aria-labelledby={titleId} tabIndex={-1} {...timerHandlers}>
      <span className="studio-toast-icon" aria-hidden="true"><NotificationLevelIcon level={level} /></span>
      <div className="studio-toast-body">
        <div className="studio-toast-title" id={titleId}>{entry.title}</div>
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
