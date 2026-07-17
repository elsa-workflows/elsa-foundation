import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityDefinitionsPage } from "../ActivityDefinitionsPage";
import { activityDesignKeys } from "../api/activityDesign";
import { clearApiCapabilityCache } from "../api/capabilities";
import { activityDefinitionsObservationEvent, observeActivityDefinitions } from "../activityDefinitionObservability";
import type { ActivityDefinitionManagementView, ActivityManagementPage } from "../activityDefinitionTypes";

afterEach(() => {
  clearApiCapabilityCache();
  window.history.replaceState({}, "", "/");
});

describe("Activity Definitions experience", () => {
  it("distinguishes initial loading, an empty collection, and no filter matches", async () => {
    let resolveCollection!: (value: unknown) => void;
    const firstCollection = new Promise(resolve => { resolveCollection = resolve; });
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.includes("search=missing")) return page([]);
      return firstCollection;
    });
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "Loading Activity Definitions");
    resolveCollection(page([]));
    await waitForText(rendered.container, "No Activity Definitions yet");

    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!, "missing");
    await waitForText(rendered.container, "No Activity Definitions match");
    await rendered.unmount();
  });

  it("shows a safe failed-without-data state", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      throw new Error("secret upstream failure for definition-hidden");
    });
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "Couldn't load Activity Definitions");
    expect(rendered.container.textContent).not.toContain("secret upstream failure");
    expect(rendered.container.textContent).not.toContain("definition-hidden");
    await rendered.unmount();
  });

  it("opens the stable definition workbench and keeps an exact draft identity in the URL", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions?")) return page([definition()]);
      if (url === "/design/activities/definitions/definition-1") return definition();
      if (url.startsWith("/design/activities/definitions/definition-1/drafts?")) return page([draft()]);
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return page([version()]);
      if (url === "/design/activities/drafts/draft-1") return draftDetail();
      if (url === "/design/activities/versions/version-1") return versionDetail();
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "Invoice evaluator");
    const row = rendered.container.querySelector<HTMLElement>("[role='row'][tabindex='0']")!;
    row.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));

    await waitForText(rendered.container, "Stable Activity Definition");
    expect(rendered.container.textContent).toContain("Contoso.InvoiceEvaluator");
    expect(rendered.container.textContent).toContain("visual-graph");
    expect(getJson.mock.calls.some(([url]) => String(url).includes("/drafts?"))).toBe(false);
    expect(getJson.mock.calls.some(([url]) => String(url).includes("/versions?"))).toBe(false);
    click(buttonByText(rendered.container, "Drafts"));
    await waitForText(rendered.container, "Draft updated");
    click(buttonByText(rendered.container, "Draft updated"));

    expect(window.location.pathname).toBe("/workflows/activity-definitions");
    expect(new URLSearchParams(window.location.search).get("definition")).toBe("definition-1");
    expect(new URLSearchParams(window.location.search).get("draft")).toBe("draft-1");
    await rendered.unmount();
  });

  it("resolves an exact draft bookmark even when the selected draft is outside the loaded page", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/definitions/definition-1") return definition();
      if (url.startsWith("/design/activities/definitions/definition-1/drafts?")) return page([]);
      if (url === "/design/activities/drafts/draft-2") return draftDetail({ draftId: "draft-2", presentationLabel: "Parallel review" });
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1&section=drafts&draft=draft-2");

    await waitForText(rendered.container, "Parallel review");
    expect(rendered.container.textContent).toContain("draft-2");
    expect(rendered.container.textContent).toContain("revision 3");
    await rendered.unmount();
  });

  it.each([403, 404])("purges retained child identities when a draft collection fails closed with %s", async status => {
    let draftReads = 0;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions?")) return page([definition()]);
      if (url === "/design/activities/definitions/definition-1") return definition();
      if (url.startsWith("/design/activities/definitions/definition-1/drafts?")) {
        draftReads += 1;
        if (draftReads === 1) return page([{ ...draft(), draft: { ...draft().draft, presentationLabel: "Secret draft identity" } }]);
        throw new StudioHttpError(status, "Hidden draft-1", null, { detail: "Hidden draft-1" });
      }
      throw new Error(`Unexpected GET ${url}`);
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const rendered = renderPage(getJson, queryClient);

    await waitForText(rendered.container, "Invoice evaluator");
    click(rendered.container.querySelector<HTMLElement>("[role='row'][tabindex='0']")!);
    await waitForText(rendered.container, "Stable Activity Definition");
    click(buttonByText(rendered.container, "Drafts"));
    await waitForText(rendered.container, "Secret draft identity");

    await queryClient.refetchQueries({ queryKey: ["activity-design", "definition", "definition-1", "drafts"] });
    await waitForText(rendered.container, "No identities or counts are shown");
    expect(rendered.container.textContent).not.toContain("Secret draft identity");
    expect(rendered.container.textContent).not.toContain("draft-1");
    expect(queryClient.getQueriesData({ queryKey: ["activity-design", "definition", "definition-1", "drafts"] }).every(([, data]) => {
      const childPage = data as { items?: unknown[]; totalCount?: number } | undefined;
      return childPage?.items?.length === 0 && childPage.totalCount === 0;
    })).toBe(true);
    await rendered.unmount();
  });

  it("uses bounded cursor paging and sends filters to the server", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.includes("cursor=cursor-2")) return page([definition({ definitionId: "definition-2", displayName: "Second definition" })]);
      return page([definition()], { continuation: "cursor-2", hasMore: true, totalCount: 2 });
    });
    const rendered = renderPage(getJson);
    await waitForText(rendered.container, "Invoice evaluator");

    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!, "invoice");
    change(rendered.container.querySelector<HTMLSelectElement>("select")!, "Design");
    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Exact provider key']")!, "visual-graph");
    await waitFor(() => getJson.mock.calls.some(([url]) => String(url).includes("search=invoice") && String(url).includes("authority=Design") && String(url).includes("providerKey=visual-graph")));

    click(buttonByText(rendered.container, "Next"));
    await waitForText(rendered.container, "Second definition");
    expect(getJson.mock.calls.some(([url]) => String(url).includes("limit=25") && String(url).includes("cursor=cursor-2"))).toBe(true);
    await rendered.unmount();
  });

  it("retains confirmed rows after a failed refresh without exposing backend error text", async () => {
    let reads = 0;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      reads += 1;
      if (reads === 1) return page([definition()]);
      throw new Error("secret backend detail for hidden-definition-99");
    });
    const rendered = renderPage(getJson);
    await waitForText(rendered.container, "Invoice evaluator");

    click(buttonByText(rendered.container, "Refresh"));
    await waitForText(rendered.container, "Refresh failed; showing retained data");

    expect(rendered.container.textContent).toContain("Invoice evaluator");
    expect(rendered.container.textContent).not.toContain("secret backend detail");
    expect(rendered.container.textContent).not.toContain("hidden-definition-99");
    await rendered.unmount();
  });

  it("clears cached identities and counts when authorization is rejected", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      throw new StudioHttpError(403, "Hidden Invoice evaluator definition-1", null, { detail: "Hidden Invoice evaluator definition-1" });
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    queryClient.setQueryData(activityDesignKeys.definitionCollection({ limit: 25, cursor: null, search: "", authority: "", providerKey: "" }), page([definition()]));
    queryClient.setQueryData(activityDesignKeys.definition("definition-old"), definition({ definitionId: "definition-old", displayName: "Previously visible" }));
    queryClient.setQueryData(activityDesignKeys.definitionDrafts({ definitionId: "definition-old", limit: 10, cursor: null }), page([draft()]));
    const rendered = renderPage(getJson, queryClient);

    await waitForText(rendered.container, "No identities or counts were loaded");
    expect(rendered.container.textContent).not.toContain("Invoice evaluator");
    expect(rendered.container.textContent).not.toContain("definition-1");
    await waitFor(() => queryClient.getQueriesData({ queryKey: activityDesignKeys.definitionResources }).every(([, data]) => {
      if (data !== null) throw new Error("Waiting for resource cache redaction.");
    }));
    expect(queryClient.getQueriesData({ queryKey: activityDesignKeys.definitionCollections }).every(([, data]) => {
      const page = data as { items?: unknown[]; totalCount?: number } | undefined;
      return page?.items?.length === 0 && page.totalCount === 0;
    })).toBe(true);
    await rendered.unmount();
  });

  it("restarts from the first page when a retained cursor snapshot expires", async () => {
    let firstPageReads = 0;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.includes("cursor=cursor-2")) throw new StudioHttpError(410, "expired", null);
      firstPageReads += 1;
      return page([definition()], { continuation: "cursor-2", hasMore: true, totalCount: 2 });
    });
    const rendered = renderPage(getJson);
    await waitForText(rendered.container, "Invoice evaluator");

    click(buttonByText(rendered.container, "Next"));
    await waitForText(rendered.container, "Collection snapshot expired");
    click(buttonByText(rendered.container, "Restart paging"));
    await waitFor(() => {
      if (firstPageReads < 2) throw new Error("Waiting for a fresh first-page request.");
    });
    expect(rendered.container.textContent).toContain("Invoice evaluator");
    await rendered.unmount();
  });

  it("renders an explicit unavailable contribution and never probes a legacy endpoint", async () => {
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? { capabilities: [] } : Promise.reject(new Error(`Unexpected domain request ${url}`)));
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "Unavailable contribution");
    expect(rendered.container.textContent).toContain("No legacy activity fallback is used");
    expect(getJson).toHaveBeenCalledTimes(1);
    expect(getJson).toHaveBeenCalledWith("/capabilities");
    await rendered.unmount();
  });

  it("drops non-allowlisted telemetry fields", () => {
    const listener = vi.fn();
    window.addEventListener(activityDefinitionsObservationEvent, listener);
    try {
      observeActivityDefinitions({
        event: "query-success",
        surface: "collection",
        outcome: "ready",
        resultBand: "one-to-ten",
        displayName: "Secret activity",
        definitionId: "definition-secret",
        providerManifest: { source: "secret" },
        diagnostic: "sensitive free text"
      } as never);
      const detail = (listener.mock.calls[0][0] as CustomEvent).detail;
      expect(detail).toEqual({ event: "query-success", surface: "collection", outcome: "ready", resultBand: "one-to-ten" });
      expect(JSON.stringify(detail)).not.toContain("Secret");
      expect(JSON.stringify(detail)).not.toContain("definition-secret");
    } finally {
      window.removeEventListener(activityDefinitionsObservationEvent, listener);
    }
  });
});

function renderPage(getJson: (url: string) => Promise<unknown>, queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } }), path = "/workflows/activity-definitions") {
  window.history.replaceState({}, "", path);
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const context = { baseUrl: `test://activity-definitions-${Math.random()}`, http: { getJson } } as unknown as StudioEndpointContext;
  flushSync(() => root.render(<QueryClientProvider client={queryClient}><ActivityDefinitionsPage context={context} /></QueryClientProvider>));
  return {
    container,
    async unmount() {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

function capabilities() {
  return { capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links: [
    { rel: "activity-definitions", href: "design/activities/definitions" },
    { rel: "activity-definition", href: "design/activities/definitions/{definitionId}", templated: true },
    { rel: "activity-definition-drafts", href: "design/activities/definitions/{definitionId}/drafts", templated: true },
    { rel: "activity-definition-draft", href: "design/activities/drafts/{draftId}", templated: true },
    { rel: "activity-definition-versions", href: "design/activities/definitions/{definitionId}/versions", templated: true },
    { rel: "activity-definition-version", href: "design/activities/versions/{versionId}", templated: true }
  ] }] };
}

function page<T>(items: T[], options: { continuation?: string; hasMore?: boolean; totalCount?: number } = {}): ActivityManagementPage<T> {
  return { items, count: items.length, totalCount: options.totalCount ?? items.length, hasMore: options.hasMore ?? false, continuation: options.continuation ?? null, snapshot: { snapshotId: "snapshot-1", asOf: "2026-07-17T10:00:00Z" } };
}

function definition(overrides: Partial<ActivityDefinitionManagementView["definition"]> = {}): ActivityDefinitionManagementView {
  return {
    definition: { definitionId: "definition-1", activityTypeKey: "Contoso.InvoiceEvaluator", tenantId: null, category: "Finance", displayName: "Invoice evaluator", description: "Evaluates invoice policy.", contentAuthority: { kind: "Design", authorityKey: "elsa.activity-design", sourceId: null }, forkedFrom: null, headVersionId: "version-1", recommendedVersionId: "version-1", ...overrides },
    lifecycle: { draftCount: 1, versionCount: 1, head: { versionId: "version-1", version: "1.0.0", lifecycle: "Active", providerKey: "visual-graph", providerSchemaVersion: "1" }, recommendation: { versionId: "version-1", version: "1.0.0", lifecycle: "Active", providerKey: "visual-graph", providerSchemaVersion: "1" } },
    actions: [],
    updatedAt: "2026-07-17T10:00:00Z"
  };
}

function draft() {
  return { draft: { draftId: "draft-1", definitionId: "definition-1", revision: 3, sourceVersionId: "version-1", status: "Active", providerKey: "visual-graph", providerSchemaVersion: "1", updatedAt: "2026-07-17T10:00:00Z", presentationLabel: null }, actions: [] };
}

function version() {
  return { version: { versionId: "version-1", definitionId: "definition-1", version: "1.0.0", lifecycle: "Active", publishedAt: "2026-07-17T09:00:00Z" }, providerKey: "visual-graph", providerSchemaVersion: "1", isRecommended: true, actions: [] };
}

function draftDetail(overrides: Partial<{ draftId: string; presentationLabel: string | null }> = {}) { return { ...draftDetailBase(), ...overrides }; }
function draftDetailBase() { return { draftId: "draft-1", definitionId: "definition-1", tenantId: null, revision: 3, sourceVersionId: "version-1", status: "Active", contract: {}, provider: { providerKey: "visual-graph", schemaVersion: "1", manifestFingerprint: "fingerprint", payload: { hidden: "not rendered" } }, layout: [], validation: null, createdAt: "2026-07-17T09:00:00Z", updatedAt: "2026-07-17T10:00:00Z", presentationLabel: null }; }
function versionDetail() { return { definition: definition().definition, versionId: "version-1", version: "1.0.0", sourceDraftId: "draft-1", sourceVersionId: null, contract: {}, provider: { providerKey: "visual-graph", schemaVersion: "1", manifestFingerprint: "fingerprint", payload: { hidden: "not rendered" } }, template: {}, lifecycle: "Active", publishedAt: "2026-07-17T09:00:00Z" }; }

function buttonByText(container: HTMLElement, text: string) {
  const button = [...container.querySelectorAll("button")].find(candidate => candidate.textContent?.includes(text));
  if (!button) throw new Error(`Button '${text}' not found.`);
  return button;
}

function click(element: Element) { flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true }))); }
function change(element: HTMLInputElement | HTMLSelectElement, value: string) { flushSync(() => { const setter = Object.getOwnPropertyDescriptor(element instanceof HTMLInputElement ? HTMLInputElement.prototype : HTMLSelectElement.prototype, "value")?.set; setter?.call(element, value); element.dispatchEvent(new Event("change", { bubbles: true })); element.dispatchEvent(new Event("input", { bubbles: true })); }); }
async function waitForText(container: HTMLElement, text: string) { await waitFor(() => { if (!container.textContent?.includes(text)) throw new Error(`Waiting for '${text}'.`); }); }
async function waitFor(assertion: () => void) { await vi.waitFor(assertion, { timeout: 10_000 }); }
