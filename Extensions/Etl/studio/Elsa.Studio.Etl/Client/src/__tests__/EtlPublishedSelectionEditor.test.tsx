import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { StudioHttpError, type StudioActivityInputDescriptor, type StudioEndpointContext, type StudioHttpClient, type StudioRequestInit } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { EtlPublishedSelectionEditor } from "../EtlPublishedSelectionEditor";
import { clearEtlApiCapabilityCache } from "../etlApi";
import type { EtlApiCapabilityDocument, EtlDefinitionSummaryView, EtlPublishedVersionView } from "../etlTypes";

const token = `etl1:orders:3:${"a".repeat(64)}`;
const secondToken = `etl1:orders:2:${"b".repeat(64)}`;

describe("ETL published selection editor", () => {
  afterEach(() => {
    clearEtlApiCapabilityCache();
    if (active) flushSync(() => active!.root.unmount());
    active = null;
    document.body.replaceChildren();
  });

  it("pages definitions and versions on demand and commits the exact server selection token", async () => {
    const context = endpointContext((url) => {
      if (url.endsWith("/capabilities")) return capabilities();
      if (url.includes("/etl/definitions?")) {
        return url.includes("cursor=definitions-next")
          ? page([definition("returns", 2, 1)])
          : page([definition("orders", 7, 3)], "definitions-next");
      }
      if (url.includes("/orders/versions?")) {
        return url.includes("cursor=versions-next")
          ? page([version(2, secondToken)])
          : page([version(3, token)], "versions-next");
      }
      throw new Error(`Unexpected GET ${url}`);
    });
    const onChange = vi.fn();
    const rendered = mount(context, { onChange });

    expect(rendered.container.textContent).toContain("Loading approved ETL definitions…");
    await waitFor(() => rendered.container.querySelector<HTMLSelectElement>("select[aria-label]") !== null || rendered.container.textContent?.includes("orders") === true);
    const definitions = selectByLabel(rendered.container, "Published ETL definition");
    expect(Array.from(definitions.options).map(option => option.value)).toContain("orders");
    click(buttonByText(rendered.container, "Load more definitions"));
    await waitFor(() => Array.from(definitions.options).some(option => option.value === "returns"));
    choose(definitions, "orders");

    const versions = selectByLabel(rendered.container, "Published ETL version");
    await waitFor(() => Array.from(versions.options).some(option => option.value === token));
    expect(Array.from(versions.options).map(option => option.value)).toContain(token);
    click(buttonByText(rendered.container, "Load more versions"));
    await waitFor(() => Array.from(versions.options).some(option => option.value === secondToken));

    choose(versions, secondToken);
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith(secondToken);
  });

  it("preserves an existing selection that is outside the currently listed page", async () => {
    const oldToken = `etl1:archived:9:${"f".repeat(64)}`;
    const context = endpointContext(url => {
      if (url.endsWith("/capabilities")) return capabilities();
      if (url.includes("/etl/definitions?")) return page([definition("current", 1, 1)]);
      if (url.includes("/archived/versions?")) return page([]);
      throw new Error(`Unexpected GET ${url}`);
    });
    const onChange = vi.fn();
    const rendered = mount(context, { value: oldToken, onChange });

    await waitFor(() => rendered.container.textContent?.includes("Saved definition: archived") === true);
    const version = selectByLabel(rendered.container, "Published ETL version");
    expect(version.value).toBe(oldToken);
    expect(rendered.container.textContent).toContain("Saved immutable selection · archived v9");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("cancels and ignores a stale version response after the definition changes", async () => {
    let resolveStale: ((value: unknown) => void) | undefined;
    let staleSignal: AbortSignal | undefined;
    const context = endpointContext((url, init) => {
      if (url.endsWith("/capabilities")) return capabilities();
      if (url.includes("/etl/definitions?")) return page([
        definition("alpha", 1, 1),
        definition("beta", 1, 1)
      ]);
      if (url.includes("/alpha/versions?")) {
        staleSignal = init?.signal ?? undefined;
        return new Promise(resolve => { resolveStale = resolve; });
      }
      if (url.includes("/beta/versions?")) return page([version(1, `etl1:beta:1:${"c".repeat(64)}`)]);
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = mount(context);
    await waitFor(() => Array.from(selectByLabel(rendered.container, "Published ETL definition").options).some(option => option.value === "beta"));

    const definitionSelect = selectByLabel(rendered.container, "Published ETL definition");
    choose(definitionSelect, "alpha");
    await waitFor(() => resolveStale !== undefined);
    choose(definitionSelect, "beta");
    await waitFor(() => Array.from(selectByLabel(rendered.container, "Published ETL version").options).some(option => option.value.startsWith("etl1:beta:")));
    resolveStale?.(page([version(1, `etl1:alpha:1:${"d".repeat(64)}`)]));
    await waitFor(() => staleSignal?.aborted === true);
    expect(Array.from(selectByLabel(rendered.container, "Published ETL version").options).some(option => option.value.startsWith("etl1:alpha:"))).toBe(false);
  });

  it("shows forbidden access distinctly, retries, and keeps editor controls read-only when requested", async () => {
    let definitionAttempts = 0;
    const context = endpointContext(url => {
      if (url.endsWith("/capabilities")) return capabilities();
      if (url.includes("/etl/definitions?")) {
        if (++definitionAttempts === 1) throw new StudioHttpError(403, "permission denied");
        return page([definition("orders", 1, 1)]);
      }
      if (url.includes("/orders/versions?")) return page([version(3, token)]);
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = mount(context, { disabled: true });

    await waitFor(() => rendered.container.textContent?.includes("You do not have permission to read ETL definitions.") === true);
    click(buttonByText(rendered.container, "Retry"));
    await waitFor(() => Array.from(selectByLabel(rendered.container, "Published ETL definition").options).some(option => option.value === "orders"));
    expect(selectByLabel(rendered.container, "Published ETL definition").disabled).toBe(true);
    expect(selectByLabel(rendered.container, "Published ETL version").disabled).toBe(true);
  });

  it("rediscovers a capability enabled after the initial unavailable response", async () => {
    let available = false;
    const context = endpointContext(url => {
      if (url.endsWith("/capabilities")) return available ? capabilities() : { capabilities: [] };
      if (url.includes("/etl/definitions?")) return page([definition("orders", 1, 1)]);
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = mount(context);
    await waitFor(() => rendered.container.textContent?.includes("The ETL API is unavailable") === true);
    available = true;
    click(buttonByText(rendered.container, "Retry"));
    await waitFor(() => Array.from(selectByLabel(rendered.container, "Published ETL definition").options).some(option => option.value === "orders"));
  });

  it("distinguishes an unavailable provider from a recoverable request error", async () => {
    const unavailableContext = endpointContext(url => {
      if (url.endsWith("/capabilities")) return capabilities();
      if (url.includes("/etl/definitions?")) throw new StudioHttpError(503, "provider unavailable");
      throw new Error(`Unexpected GET ${url}`);
    });
    const unavailable = mount(unavailableContext);
    await waitFor(() => unavailable.container.textContent?.includes("The ETL API is unavailable for ETL definitions.") === true);

    if (active) flushSync(() => active!.root.unmount());
    active = null;
    clearEtlApiCapabilityCache();
    const attempts = { count: 0 };
    const recoverableContext = endpointContext(url => {
      if (url.endsWith("/capabilities")) return capabilities();
      if (url.includes("/etl/definitions?")) {
        if (++attempts.count === 1) throw new StudioHttpError(400, "bad request");
        return page([definition("orders", 1, 1)]);
      }
      throw new Error(`Unexpected GET ${url}`);
    });
    const recoverable = mount(recoverableContext);
    await waitFor(() => recoverable.container.textContent?.includes("Could not load ETL definitions.") === true);
    click(buttonByText(recoverable.container, "Retry"));
    await waitFor(() => Array.from(selectByLabel(recoverable.container, "Published ETL definition").options).some(option => option.value === "orders"));
  });
});

let active: { root: Root; container: HTMLDivElement } | null = null;

function mount(
  endpointContext: StudioEndpointContext,
  options: { value?: unknown; disabled?: boolean; onChange?: (value: unknown) => void } = {}
) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  const rendered = { root, container };
  active = rendered;
  flushSync(() => root.render(
    <EtlPublishedSelectionEditor
      descriptor={{ name: "Selection", typeName: "string" } as StudioActivityInputDescriptor}
      value={options.value ?? ""}
      disabled={options.disabled}
      context={{ activity: {}, expressionDescriptors: [], readOnly: options.disabled }}
      onChange={options.onChange ?? vi.fn()}
      endpointContext={endpointContext}
    />
  ));
  return rendered;
}

type GetHandler = (url: string, init?: RequestInit) => unknown | Promise<unknown>;

function endpointContext(get: GetHandler): StudioEndpointContext {
  clearEtlApiCapabilityCache();
  const client: StudioHttpClient = {
    getJson: async <T,>(url: string, init?: StudioRequestInit) => await get(url, init) as T,
    requestJson: async <T,>() => ({} as T),
    postJson: async <T,>() => ({} as T),
    putJson: async <T,>() => ({} as T),
    deleteJson: async <T,>() => ({} as T),
    postForm: async <T,>() => ({} as T)
  };
  return { baseUrl: "https://studio.example/studio", http: client };
}

function capabilities(): EtlApiCapabilityDocument {
  return {
    capabilities: [{
      id: "elsa.api.etl-design",
      contractVersion: "1",
      links: [
        ["etl-bindings", "etl/bindings"],
        ["etl-definitions", "etl/definitions"],
        ["etl-definition", "etl/definitions/{id}"],
        ["etl-draft", "etl/definitions/{id}/draft"],
        ["etl-validation", "etl/definitions/{id}/validate"],
        ["etl-publication", "etl/definitions/{id}/publish"],
        ["etl-versions", "etl/definitions/{id}/versions"],
        ["etl-version", "etl/definitions/{id}/versions/{version}"]
      ].map(([rel, href]) => ({ rel, href }))
    }]
  };
}

function page<T>(items: T[], nextCursor: string | null = null) {
  return { items, nextCursor };
}

function definition(definitionId: string, revision: number, lastPublishedVersion: number): EtlDefinitionSummaryView {
  return { definitionId, revision, lastPublishedVersion, updatedAt: "2026-10-20T10:00:00Z" };
}

function version(versionNumber: number, selection: string): EtlPublishedVersionView {
  return {
    definitionId: "orders",
    definitionVersion: versionNumber,
    contentHash: selection.split(":").at(-1)!,
    selection,
    publishedAt: "2026-10-20T10:00:00Z"
  };
}

function selectByLabel(container: Element, label: string): HTMLSelectElement {
  const element = Array.from(container.querySelectorAll("label")).find(candidate => candidate.textContent === label);
  if (!element?.htmlFor) throw new Error(`No label found: ${label}`);
  const control = Array.from(container.querySelectorAll<HTMLSelectElement>("select")).find(select => select.id === element.htmlFor);
  if (!control) throw new Error(`No select found for label: ${label}`);
  return control;
}

function choose(select: HTMLSelectElement, value: string) {
  flushSync(() => {
    select.value = value;
    select.dispatchEvent(new Event("change", { bubbles: true }));
  });
}

function buttonByText(container: Element, label: string): HTMLButtonElement {
  const button = Array.from(container.querySelectorAll("button")).find(candidate => candidate.textContent === label);
  if (!button) throw new Error(`No button found: ${label}`);
  return button;
}

function click(button: HTMLButtonElement) {
  flushSync(() => button.click());
}

async function waitFor(predicate: () => boolean) {
  for (let attempt = 0; attempt < 100; attempt++) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  throw new Error("Timed out waiting for the ETL selection editor.");
}
