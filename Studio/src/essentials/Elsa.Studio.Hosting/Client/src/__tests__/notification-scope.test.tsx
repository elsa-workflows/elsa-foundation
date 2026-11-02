import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { AuthSession, StudioNotificationEntry } from "../sdk";
import { notificationScopeKey } from "../app/notificationStorage";
import { useNotificationScope } from "../app/useNotificationScope";

let cleanup: (() => void) | null = null;

afterEach(() => {
  cleanup?.();
  cleanup = null;
});

type Session = { key: string; identity: AuthSession } | null;

const identity = (subject: string): AuthSession => ({ status: "authenticated", subject, roles: [], permissions: [] });

function setup() {
  const listeners = new Map<string, (entries: StudioNotificationEntry[]) => void>();
  const unsubscribe = vi.fn((scope: string) => listeners.delete(scope));
  const controller = { setScope: vi.fn(), replaceEntries: vi.fn() };
  const storage = {
    subscribe: vi.fn((scope: string, listener: (entries: StudioNotificationEntry[]) => void) => {
      listeners.set(scope, listener);
      return () => unsubscribe(scope);
    })
  };
  function Probe({ session }: { session: Session }) {
    useNotificationScope(session, controller, storage);
    return null;
  }
  const host = document.createElement("div");
  const root = createRoot(host);
  const render = (session: Session) => flushSync(() => root.render(<Probe session={session} />));
  cleanup = () => flushSync(() => root.unmount());
  return { controller, storage, listeners, unsubscribe, render };
}

describe("useNotificationScope", () => {
  it("scopes to the session and follows identity and backend switches", () => {
    const { controller, storage, unsubscribe, render } = setup();
    const jane = { key: "https://a.example", identity: identity("jane") };
    const janeScope = notificationScopeKey(jane)!;

    render(jane);
    expect(controller.setScope).toHaveBeenLastCalledWith(janeScope);
    expect(storage.subscribe).toHaveBeenLastCalledWith(janeScope, expect.any(Function));

    render({ ...jane });
    expect(controller.setScope).toHaveBeenCalledTimes(1);

    const otherBackend = { key: "https://b.example", identity: identity("jane") };
    render(otherBackend);
    expect(unsubscribe).toHaveBeenCalledWith(janeScope);
    expect(controller.setScope).toHaveBeenLastCalledWith(notificationScopeKey(otherBackend));

    const otherUser = { key: "https://b.example", identity: identity("john") };
    render(otherUser);
    expect(controller.setScope).toHaveBeenLastCalledWith(notificationScopeKey(otherUser));
    expect(storage.subscribe).toHaveBeenCalledTimes(3);
  });

  it("clears the scope and stops listening without a session", () => {
    const { controller, storage, unsubscribe, render } = setup();
    const jane = { key: "https://a.example", identity: identity("jane") };

    render(jane);
    render(null);

    expect(controller.setScope).toHaveBeenLastCalledWith(null);
    expect(unsubscribe).toHaveBeenCalledWith(notificationScopeKey(jane));
    expect(storage.subscribe).toHaveBeenCalledTimes(1);
  });

  it("applies lists from other tabs to the controller", () => {
    const { controller, listeners, render } = setup();
    const jane = { key: "https://a.example", identity: identity("jane") };
    render(jane);
    const incoming: StudioNotificationEntry[] = [{ id: "r", level: "info", title: "Remote", source: "studio", createdAt: 1, updatedAt: 1, read: false, toasting: false }];

    listeners.get(notificationScopeKey(jane)!)!(incoming);

    expect(controller.replaceEntries).toHaveBeenCalledWith(incoming);
  });
});
