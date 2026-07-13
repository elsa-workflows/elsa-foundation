import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it } from "vitest";
import { AnonymousAuthProvider } from "@elsa-workflows/studio-sdk";
import { AttentionWidget } from "../AttentionWidget";
import type { AttentionSnapshot } from "../attentionApi";
describe("Attention widget rendering", () => {
  it("distinguishes complete all-clear from incomplete empty data", async () => { const clear = await render({ generatedAt: "", items: [], failures: [] }); expect(clear.container.textContent).toContain("All clear"); clear.unmount(); const partial = await render({ generatedAt: "", items: [], failures: [{ contributorId: "host.backend", displayName: "Backend attention", status: "unavailable", evaluatedAt: "" }] }); expect(partial.container.textContent).not.toContain("All clear"); expect(partial.container.textContent).toContain("data is incomplete"); partial.unmount(); });
  it("filters critical items and preserves inspect destinations", async () => { const view = await render({ generatedAt: "", failures: [], items: [item("warning", "/warning"), item("critical", "/critical")] }); expect(view.container.querySelector('a[href="/warning"]')).not.toBeNull(); click([...view.container.querySelectorAll("button")].find(value => value.textContent === "Critical")!); expect(view.container.querySelector('a[href="/warning"]')).toBeNull(); expect(view.container.querySelector('a[href="/critical"]')?.textContent).toBe("Inspect"); view.unmount(); });
});
function item(severity: "critical"|"warning", path: string): any { return { id: severity, generation: "1", severity, title: severity, occurredAt: "2026-01-01T00:00:00Z", lastObservedAt: "", count: 1, destination: { path, label: "Inspect" }, correlations: [], sensitivity: "metadata", contributorId: "test", contributorName: "Test" }; }
async function render(snapshot: AttentionSnapshot) { const container = document.createElement("div"); const root = createRoot(container); const api = { runtime: { hostId: "test" }, host: { baseUrl: "studio" }, backend: { baseUrl: "backend", http: { getJson: async () => { throw new Error("none"); }, putJson: async () => ({}) } } } as any; flushSync(() => root.render(<AnonymousAuthProvider><AttentionWidget api={api} snapshot={snapshot} settings={undefined} /></AnonymousAuthProvider>)); await new Promise(resolve => setTimeout(resolve, 0)); return { container, unmount: () => flushSync(() => root.unmount()) }; }
function click(element: Element) { flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true }))); }
