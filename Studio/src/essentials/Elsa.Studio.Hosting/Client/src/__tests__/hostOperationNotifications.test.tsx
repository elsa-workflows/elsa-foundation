import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { useHostOperations, type HostOperations } from "../app/modules/useModuleManagement";
import type { HostModel } from "../app/modules/moduleManagementApi";
import { createEndpointContext } from "../sdk";
import { createTestQueryClient, withQueryClient } from "./queryTestUtils";

function host(name: string): HostModel {
  return { id: "studio", label: name, context: createEndpointContext(`https://${name}.example`), mutationsAvailable: true, readRegistry: vi.fn() };
}

function fixture() {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  const client = createTestQueryClient();
  const notify = vi.fn();
  let operations!: HostOperations;
  function Probe({ target }: { target: HostModel }) {
    operations = useHostOperations(target, null, [], notify);
    return <>{operations.activeError ? <p role="alert">{operations.activeError}</p> : null}{operations.activeStatus ? <p role="status">{operations.activeStatus}</p> : null}</>;
  }
  const render = (target: HostModel) => flushSync(() => root.render(withQueryClient(<Probe target={target} />, client)));
  const unmount = () => { flushSync(() => root.unmount()); container.remove(); client.clear(); };
  return { container, notify, render, unmount, get operations() { return operations; } };
}

describe("host operation feedback", () => {
  it("toasts retention changes once while keeping restart requirements and failures inline", async () => {
    const view = fixture();
    try {
      view.render(host("one"));
      await view.operations.runHostOperation(async () => {}, "Retention saved.", "toast");
      expect(view.notify).toHaveBeenCalledWith("Retention saved.");
      expect(view.notify).toHaveBeenCalledTimes(1);
      expect(view.container.querySelector('[role="status"]')).toBeNull();
      await view.operations.runHostOperation(async () => {}, "Restart required.");
      await vi.waitFor(() => expect(view.container.textContent).toContain("Restart required."));
      await view.operations.runHostOperation(async () => { throw new Error("Save failed."); }, "Should not toast.", "toast");
      await vi.waitFor(() => expect(view.container.querySelector('[role="alert"]')?.textContent).toContain("Save failed."));
      expect(view.notify).toHaveBeenCalledTimes(1);
    } finally { view.unmount(); }
  });

  it.each(["switch", "unmount"])("suppresses a delayed toast after %s", async action => {
    const view = fixture();
    let finish!: () => void;
    view.render(host("one"));
    const pending = view.operations.runHostOperation(() => new Promise<void>(resolve => { finish = resolve; }), "Old host saved.", "toast");
    await vi.waitFor(() => expect(finish).toBeTypeOf("function"));
    if (action === "switch") view.render(host("two"));
    else view.unmount();
    finish();
    await pending;
    expect(view.notify).not.toHaveBeenCalled();
    if (action === "switch") view.unmount();
  });
});
