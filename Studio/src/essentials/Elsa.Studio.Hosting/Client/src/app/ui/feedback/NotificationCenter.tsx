import React, { useEffect, useLayoutEffect, useRef, useState, useSyncExternalStore } from "react";
import * as Dialog from "@radix-ui/react-dialog";
import { Bell, X } from "lucide-react";
import type { StudioNotificationController, StudioNotificationEntry } from "../../../sdk";
import { notificationController } from "../../notifications";
import { focusIfConnected, NotificationLevelIcon, notificationLevelLabel, runNotificationAction } from "./notificationShared";

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

const relativeTimeFormat = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });

function formatRelativeTime(timestamp: number, now: number): string {
  const elapsed = now - timestamp;
  for (const [unit, size] of relativeUnits) {
    if (Math.abs(elapsed) >= size) return relativeTimeFormat.format(-Math.trunc(elapsed / size), unit);
  }
  return relativeTimeFormat.format(-Math.trunc(elapsed / 1000), "second");
}

/**
 * Header bell with unread badge plus the right-side, non-modal Notification Center sheet.
 * Rendered as a fragment so the bell is a direct child of the top bar actions and inherits their styling.
 * Toasts are suppressed while the sheet is open, since they would cover it and repeat what it lists.
 */
export function NotificationCenter({ navigate, controller = notificationController, now = Date.now }: NotificationCenterProps) {
  const entries = useSyncExternalStore(controller.subscribe, controller.getEntries, controller.getEntries);
  const [open, setOpen] = useState(false);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const unread = entries.filter(entry => !entry.read);
  const unreadCount = unread.length;
  const hasUnreadError = unread.some(entry => entry.level === "error");

  const changeOpen = (next: boolean) => {
    setOpen(next);
    controller.setToastsSuppressed(next);
  };

  useEffect(() => () => controller.setToastsSuppressed(false), [controller]);

  // The header buttons disable themselves once used, which would drop focus; keep it in the sheet.
  const markAllRead = () => {
    controller.markAllRead();
    headingRef.current?.focus();
  };
  const dismissAll = () => {
    controller.dismissAll();
    headingRef.current?.focus();
  };

  return (
    <Dialog.Root open={open} onOpenChange={changeOpen} modal={false}>
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
            <Dialog.Title ref={headingRef} className="studio-notification-title" tabIndex={-1}>Notifications</Dialog.Title>
            <Dialog.Close className="studio-notification-close" aria-label="Close notifications" title="Close">
              <X size={16} aria-hidden="true" />
            </Dialog.Close>
            <div className="studio-notification-header-actions">
              <button type="button" className="studio-notification-header-button" disabled={unreadCount === 0} onClick={markAllRead}>Mark all read</button>
              <button type="button" className="studio-notification-header-button" disabled={entries.length === 0} onClick={dismissAll}>Dismiss all</button>
            </div>
          </div>
          <NotificationList entries={entries} controller={controller} navigate={navigate} now={now} headingRef={headingRef} onClose={() => changeOpen(false)} />
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

function NotificationList({ entries, controller, navigate, now, headingRef, onClose }: {
  entries: readonly StudioNotificationEntry[];
  controller: StudioNotificationController;
  navigate(path: string): void;
  now: () => number;
  headingRef: React.RefObject<HTMLHeadingElement | null>;
  onClose(): void;
}) {
  const listRef = useRef<HTMLUListElement>(null);
  const focusedRemovalIndex = useRef<number | null>(null);
  // Relative times are computed while rendering; tick so an open sheet does not show stale ages.
  const [, setTick] = useState(0);
  useEffect(() => {
    const timer = setInterval(() => setTick(tick => tick + 1), 30_000);
    return () => clearInterval(timer);
  }, []);

  // An entry dismissed while focused hands focus to the entry now in its place, else to the heading.
  useLayoutEffect(() => {
    const index = focusedRemovalIndex.current;
    if (index === null) return;
    focusedRemovalIndex.current = null;
    const remaining = listRef.current?.querySelectorAll<HTMLElement>(".studio-notification-entry-main") ?? [];
    if (focusIfConnected(remaining[Math.min(index, remaining.length - 1)])) return;
    focusIfConnected(headingRef.current);
  });

  if (entries.length === 0) {
    return <div className="studio-notification-empty">No notifications</div>;
  }

  const current = now();
  return (
    <ul ref={listRef} className="studio-notification-list">
      {entries.map((entry, index) => (
        <NotificationEntryRow
          key={entry.id}
          entry={entry}
          index={index}
          controller={controller}
          navigate={navigate}
          now={current}
          onClose={onClose}
          onRemovedWhileFocused={removedIndex => { focusedRemovalIndex.current = removedIndex; }}
        />
      ))}
    </ul>
  );
}

function NotificationEntryRow({ entry, index, controller, navigate, now, onClose, onRemovedWhileFocused }: {
  entry: StudioNotificationEntry;
  index: number;
  controller: StudioNotificationController;
  navigate(path: string): void;
  now: number;
  onClose(): void;
  onRemovedWhileFocused(index: number): void;
}) {
  const { id, link, action } = entry;
  const rowRef = useRef<HTMLLIElement>(null);
  const latest = useRef({ index, onRemovedWhileFocused });
  latest.current = { index, onRemovedWhileFocused };
  const markRead = () => controller.markRead(id);

  // Runs while the row is still in the document, so it can tell whether it held focus.
  useLayoutEffect(() => {
    const node = rowRef.current;
    return () => {
      if (node?.contains(document.activeElement)) latest.current.onRemovedWhileFocused(latest.current.index);
    };
  }, []);

  const openLink = () => {
    if (!link) return;
    try {
      markRead();
      onClose();
    } finally {
      navigate(link.path);
    }
  };

  const runAction = () => {
    if (!action) return;
    runNotificationAction(controller, id, action);
    markRead();
  };

  return (
    <li ref={rowRef} className="studio-notification-entry" data-level={entry.level} data-unread={entry.read ? undefined : "true"}>
      <span className="studio-notification-entry-icon" aria-hidden="true"><NotificationLevelIcon level={entry.level} /></span>
      <div className="studio-notification-entry-body">
        <button type="button" className="studio-notification-entry-main" onClick={markRead}>
          <span className="studio-notification-sr-only">{`${entry.read ? "" : "Unread, "}${notificationLevelLabel[entry.level]}: `}</span>
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
