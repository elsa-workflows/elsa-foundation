import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { PersistenceModeSection } from "../PersistenceModeSection";
import { clearApiCapabilityCache } from "../api/capabilities";

let container: HTMLDivElement;
let root: ReturnType<typeof createRoot>;
beforeEach(() => { container = document.createElement("div"); root = createRoot(container); });
afterEach(() => { flushSync(() => root.unmount()); clearApiCapabilityCache(); });

function host(load: () => Promise<unknown>, advertised = true) {
  const getJson = vi.fn(async (path: string) => path === "/capabilities" ? {
    capabilities: [{ id: "elsa.api.runtime", contractVersion: "1", links: advertised
      ? [{ rel: "runtime-persistence-mode", href: "custom/persistence-default" }] : [] }]
  } : load());
  return { context: { baseUrl: `test://${Math.random()}`, http: { getJson } } as unknown as StudioEndpointContext, getJson };
}

function render(context: StudioEndpointContext, strategyOptions: unknown = {}) {
  const onChange = vi.fn();
  flushSync(() => root.render(<PersistenceModeSection context={context} strategyOptions={strategyOptions} onChange={onChange} />));
  return { select: container.querySelector<HTMLSelectElement>("select")!, onChange };
}

describe("host default persistence mode", () => {
  it.each(["Durable", "Balanced", "Ephemeral"])("shows the configured %s default without pinning the draft", async mode => {
    const { context, getJson } = host(async () => ({ persistenceMode: mode }));
    const { select, onChange } = render(context);
    await vi.waitFor(() => expect(select.options[0].textContent).toBe(`Use host default (${mode})`));
    expect(select.value).toBe("");
    expect(getJson).toHaveBeenCalledWith("/custom/persistence-default");
    expect(container.querySelector("#wf-persistence-mode-help")?.textContent).toContain(mode === "Durable"
      ? "Commits after every activity" : mode === "Balanced" ? "every N activities" : "unless the host turns it off");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("keeps an explicit workflow override when the default is different", async () => {
    const { context } = host(async () => ({ persistenceMode: "Durable" }));
    const { select, onChange } = render(context, { persistenceMode: "Balanced" });
    await vi.waitFor(() => expect(select.options[0].textContent).toBe("Use host default (Durable)"));
    expect(select.value).toBe("Balanced");
    expect(container.querySelector("#wf-persistence-mode-help")?.textContent).toContain("every N activities");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("does not guess a default or probe a route on an older host", async () => {
    const { context, getJson } = host(async () => { throw new Error("Unexpected request"); }, false);
    const { select } = render(context);
    await vi.waitFor(() => expect(getJson).toHaveBeenCalledOnce());
    expect(select.options[0].textContent).toBe("Use host default");
    expect(getJson).toHaveBeenCalledWith("/capabilities");
  });

  it.each(["failure", "invalid", "missing"])("leaves inheritance intact when the default read is %s", async outcome => {
    const { context } = host(async () => {
      if (outcome === "failure") throw Object.assign(new Error("Forbidden"), { status: 403 });
      return outcome === "invalid" ? { persistenceMode: " " } : {};
    });
    const { select, onChange } = render(context);
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(select.options[0].textContent).toBe("Use host default");
    expect(select.value).toBe("");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("discards a delayed default after switching hosts", async () => {
    let resolveOld!: (value: unknown) => void;
    const oldHost = host(() => new Promise(resolve => { resolveOld = resolve; }));
    render(oldHost.context);
    await vi.waitFor(() => expect(resolveOld).toBeDefined());
    const nextHost = host(async () => ({ persistenceMode: "Ephemeral" }));
    const { select } = render(nextHost.context);
    expect(select.options[0].textContent).toBe("Use host default");
    await vi.waitFor(() => expect(select.options[0].textContent).toBe("Use host default (Ephemeral)"));
    resolveOld({ persistenceMode: "Durable" });
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(select.options[0].textContent).toBe("Use host default (Ephemeral)");
  });
});
