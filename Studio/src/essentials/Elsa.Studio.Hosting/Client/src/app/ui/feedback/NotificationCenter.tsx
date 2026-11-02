import React, { useEffect, useState, useSyncExternalStore } from "react";
import * as Dialog from "@radix-ui/react-dialog";
import { Bell, X } from "lucide-react";
import type { StudioNotificationController, StudioNotificationEntry } from "../../../sdk";
import { notificationController } from "../../notifications";
import { NotificationLevelIcon, runNotificationAction } from "./notificationShared";

export interface NotificationCenterProps {
  navigate(path: string): void;
  /** Defaults to the host singleton; tests and the browser fixture pass their own. */
  controller?: StudioNotificationController;
  /** Clock for relative times; injectable so tests are deterministic. */
  now?: () => number;
}

const relativeUnits: ReadonlyArray<readonly [Intl.RelativeTimeFormatUnit, number]> = [
  ["day", 86_400_000],
  ["hour", 3_600_000],
  ["minute", 60_000]
];

function formatRelativeTime(timestamp: number, now: number): string {
  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
  const elapsed = now - timestamp;
  for (const [unit, size] of relativeUnits) {
    if (Math.abs(elapsed) >= size) return formatter.format(-Math.trunc(elapsed / size), unit);
  }
  return formatter.format(-Math.trunc(elapsed / 1000), "second");
}

/**
 * Header bell with unread badge plus the right-side, non-modal Notification Center sheet.
 * Rendered as a fragment so the bell is a direct child of the top bar actions and inherits their styling.
 */
export function NotificationCenter({ navigate, controller = notificationController, now = Date.now }: NotificationCenterProps) {
  const entries = useSyncExternalStore(controller.subscribe, controller.getEntries, controller.getEntries);
  const [open, setOpen] = useState(false);
  const unread = entries.filter(entry => !entry.read);
  const unreadCount = unread.length;
  const hasUnreadError = unread.some(entry => entry.level === "error");

  return (
    <Dialog.Root open={open} onOpenChange={setOpen} modal={false}>
      <Dialog.Trigger asChild>
        <button type="button" className="studio-notification-bell" aria-label={`Notifications, ${unreadCount} unread`} title="Notifications">
          <Bell size={16} aria-hidden="true" />
          {unreadCount > 0 ? (
            <span className="studio-notification-badge" data-tone={hasUnreadError ? "danger" : undefined} aria-hidden="true">
              {unreadCount > 9 ? "9+" : unreadCount}
            </span>
          ) : null}
        </button>
      </Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Content className="studio-notification-center" aria-describedby={undefined}>
          <div className="studio-notification-header">
            <Dialog.Title className="studio-notification-title">Notifications</Dialog.Title>
            <button type="button" className="studio-notification-header-button" disabled={unreadCount === 0} onClick={() => controller.markAllRead()}>Mark all read</button>
            <button type="button" className="studio-notification-header-button" disabled={entries.length === 0} onClick={() => controller.dismissAll()}>Dismiss all</button>
          </div>
          <NotificationList entries={entries} controller={controller} navigate={navigate} now={now} onClose={() => setOpen(false)} />
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

function NotificationList({ entries, controller, navigate, now, onClose }: {
  entries: readonly StudioNotificationEntry[];
  controller: StudioNotificationController;
  navigate(path: string): void;
  now: () => number;
  onClose(): void;
}) {
  // Relative times are computed while rendering; tick so an open sheet does not show stale ages.
  const [, setTick] = useState(0);
  useEffect(() => {
    const timer = setInterval(() => setTick(tick => tick + 1), 30_000);
    return () => clearInterval(timer);
  }, []);

  if (entries.length === 0) {
    return <div className="studio-notification-empty">No notifications</div>;
  }

  const current = now();
  return (
    <ul className="studio-notification-list">
      {entries.map(entry => (
        <NotificationEntryRow key={entry.id} entry={entry} controller={controller} navigate={navigate} now={current} onClose={onClose} />
      ))}
    </ul>
  );
}

function NotificationEntryRow({ entry, controller, navigate, now, onClose }: {
  entry: StudioNotificationEntry;
  controller: StudioNotificationController;
  navigate(path: string): void;
  now: number;
  onClose(): void;
}) {
  const { id, link, action } = entry;
  const markRead = () => controller.markRead(id);

  const openLink = () => {
    if (!link) return;
    navigate(link.path);
    markRead();
    onClose();
  };

  const runAction = () => {
    if (!action) return;
    runNotificationAction(controller, action);
    markRead();
  };

  return (
    <li className="studio-notification-entry" data-level={entry.level} data-unread={entry.read ? undefined : "true"}>
      <span className="studio-notification-entry-icon" aria-hidden="true"><NotificationLevelIcon level={entry.level} /></span>
      <div className="studio-notification-entry-body">
        <button type="button" className="studio-notification-entry-main" onClick={markRead}>
          <span className="studio-notification-entry-title">{entry.title}</span>
          {entry.message ? <span className="studio-notification-entry-message">{entry.message}</span> : null}
          <span className="studio-notification-time" title={new Date(entry.updatedAt).toLocaleString()}>{formatRelativeTime(entry.updatedAt, now)}</span>
        </button>
        {link || action ? (
          <div className="studio-notification-entry-actions">
            {link ? <button type="button" className="studio-notification-link" onClick={openLink}>{link.label}</button> : null}
            {action ? <button type="button" className="studio-notification-action" onClick={runAction}>{action.label}</button> : null}
          </div>
        ) : null}
      </div>
      <button
        type="button"
        className="studio-notification-dismiss"
        aria-label="Dismiss notification"
        title="Dismiss"
        onClick={() => controller.dismiss(id)}
      >
        <X size={16} aria-hidden="true" />
      </button>
    </li>
  );
}
